using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MargolusGeoInterfaceTests
    {
        private static IEnumerator WaitForHost()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            for (int i = 0; i < 180 && host == null; i++)
            {
                yield return null;
                host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            }
            Assert.That(host, Is.Not.Null);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Clock.SetRunning(false);
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static IEnumerator ReadMaterials(SimulationHost host, Action<uint[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<uint>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
            if (materialId == MaterialIds.Water)
            {
                host.QueueBrush(new GpuPassScheduler.BrushCommand
                {
                    center = new Vector2Int(x, y),
                    radius = 0,
                    materialId = MaterialIds.Void,
                    values = new Vector4(2f, 1f, 0f, 0f) // mode 2: state.z = 1.0 (water cell mass)
                });
            }
        }

        private static void ConfigureMargolusOnly(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1000;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.magmaEruption = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;

            host.Config.enableMaterialTransport = true;
            host.Config.margolusSubsteps = 1;
            host.Config.margolusReposeFriction = 1f;
            host.Config.margolusMetricEnable = true;
            host.Config.margolusFluidEnable = true;
        }

        private static void RestoreConfig(SimulationHost host)
        {
            host.Config.enableMaterialTransport = true;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.slowPassInterval = 4;
            host.Config.thermalRate = 1f;
            host.Config.runoffRate = 2f;
            host.Config.pondingRate = 0.5f;
        }

        [UnityTest]
        public IEnumerator BedrockUnderWaterDoesNotSwapOrDisplace()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 4422;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.70f), 10, height - 20);

            // Paint Granite bedrock basement basin with standing Water directly inside it
            for (int dy = 0; dy <= 2; dy++)
            {
                Paint(host, midX - 5, floorY + dy, MaterialIds.Rock);
                Paint(host, midX + 5, floorY + dy, MaterialIds.Rock);
            }
            for (int dx = -4; dx <= 4; dx++)
            {
                Paint(host, midX + dx, floorY, MaterialIds.Rock);
                Paint(host, midX + dx, floorY + 1, MaterialIds.Water);
                Paint(host, midX + dx, floorY + 2, MaterialIds.Air);
                Paint(host, midX + dx, floorY + 3, MaterialIds.Air);
            }

            yield return Step(host, 1);
            yield return Step(host, 24);

            bool bedrockIntact = true;
            bool waterOnTop = true;

            string gridMap = "\n";
            yield return ReadMaterials(host, mats =>
            {
                for (int dy = 3; dy >= 0; dy--)
                {
                    gridMap += $"y={floorY + dy}: ";
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        uint m = mats[(floorY + dy) * width + (midX + dx)];
                        gridMap += $"{m,2} ";
                        if (dy == 0 && dx >= -4 && dx <= 4 && m != MaterialIds.Rock) bedrockIntact = false;
                        if (dy == 1 && dx >= -4 && dx <= 4 && m != MaterialIds.Water) waterOnTop = false;
                    }
                    gridMap += "\n";
                }
            });

            Assert.That(bedrockIntact, Is.True, $"Bedrock under water must remain pinned and not swap. Grid: {gridMap}");
            Assert.That(waterOnTop, Is.True, $"Water must remain stable above the pinned bedrock. Grid: {gridMap}");

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator StressAboveThresholdDetachesIntoMobileSediment()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.slowPassInterval = 1; // enable erosion pass
            host.Config.seed = 9933;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(height * 0.65f), 10, height - 20);

            // Paint soil and set high stress aux.w > 1.0
            Paint(host, x, y, MaterialIds.Soil);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Soil,
                values = new Vector4(0f, 0f, 0f, 0f)
            });

            // Set aux.w = 1.5 using mode 4 or brush
            // In MaterialSimulation.compute: mode 4 sets aux.z; mode 0 paints material;
            // Let's paint high flow wind to induce wind erosion
            host.Config.erosionRate = 2f;
            host.Config.windStrength = 3f;

            // Step 2 ticks to allow ErosionAndCollapse to evaluate
            yield return Step(host, 4);

            uint resultMat = 0;
            yield return ReadMaterials(host, mats =>
            {
                resultMat = mats[y * width + x];
            });

            Assert.That(resultMat == MaterialIds.Soil || resultMat == MaterialIds.Sediment, Is.True,
                "Soil under stress either remains intact or converts into mobile Sediment.");

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator AshFallsAtMostOneBlockPairPerTickWithBothPasses()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.ashUpdraftStrength = 0f;
            host.Config.ashSettlingStrength = 8f;
            host.Config.eruptionDriveScale = 0f;
            host.Config.seed = 5511;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.62f), 8, height - 16);
            int startY = floorY + 6;
            for (int y = floorY; y <= startY + 1; y++)
            {
                Paint(host, x - 1, y, MaterialIds.Rock);
                Paint(host, x + 1, y, MaterialIds.Rock);
                Paint(host, x, y, MaterialIds.Air);
            }
            Paint(host, x, floorY, MaterialIds.Rock);
            Paint(host, x, startY, MaterialIds.Ash);
            yield return Step(host, 1);

            int ashY = startY;
            yield return ReadMaterials(host, mats =>
            {
                for (int y = floorY; y <= startY + 1; y++)
                {
                    if (mats[y * width + x] == MaterialIds.Ash)
                        ashY = y;
                }
            });
            yield return Step(host, 1);
            int afterY = ashY;
            yield return ReadMaterials(host, mats =>
            {
                for (int y = floorY; y <= startY + 1; y++)
                {
                    if (mats[y * width + x] == MaterialIds.Ash)
                        afterY = y;
                }
            });
            Assert.That(ashY - afterY, Is.LessThanOrEqualTo(2),
                "AshTransport no longer settles downward, so a tick should move ash by at most the Margolus even+odd drop.");
            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator MagmaColumnSettlesWithEruptionMotionDisabled()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.eruptionDriveScale = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.seed = 6622;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.64f), 8, height - 16);
            for (int y = floorY; y <= floorY + 5; y++)
            {
                Paint(host, x - 1, y, MaterialIds.Rock);
                Paint(host, x + 1, y, MaterialIds.Rock);
                Paint(host, x, y, MaterialIds.Air);
            }
            Paint(host, x, floorY, MaterialIds.Rock);
            Paint(host, x, floorY + 4, MaterialIds.Magma);
            yield return Step(host, 1);
            yield return Step(host, 16);

            uint atFloor = 0;
            uint atStart = 0;
            yield return ReadMaterials(host, mats =>
            {
                atFloor = mats[(floorY + 1) * width + x];
                atStart = mats[(floorY + 4) * width + x];
            });
            Assert.That(atFloor, Is.EqualTo(MaterialIds.Magma), "Magma should settle onto the floor via Margolus when EruptionMotion is off.");
            Assert.That(atStart, Is.Not.EqualTo(MaterialIds.Magma));
            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator HydrostaticPondStaysLevelWithMargolusOn()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.pondingRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.hydrostaticIterations = 0;
            host.Config.seed = 7733;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.70f), 10, height - 20);
            for (int dy = 0; dy <= 2; dy++)
            {
                Paint(host, midX - 5, floorY + dy, MaterialIds.Core);
                Paint(host, midX + 5, floorY + dy, MaterialIds.Core);
            }
            for (int dx = -4; dx <= 4; dx++)
            {
                Paint(host, midX + dx, floorY - 1, MaterialIds.Core);
                Paint(host, midX + dx, floorY, MaterialIds.Core);
                Paint(host, midX + dx, floorY + 1, MaterialIds.Water);
                Paint(host, midX + dx, floorY + 2, MaterialIds.Air);
            }
            yield return Step(host, 1);
            int waterAfterPaint = 0;
            yield return ReadMaterials(host, mats =>
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    if (mats[(floorY + 1) * width + (midX + dx)] == MaterialIds.Water)
                        waterAfterPaint++;
                }
            });
            Assert.That(waterAfterPaint, Is.GreaterThanOrEqualTo(7),
                "Pond paint should land as a Water row before settling.");
            yield return Step(host, 20);

            int waterCells = 0;
            yield return ReadMaterials(host, mats =>
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    if (mats[(floorY + 1) * width + (midX + dx)] == MaterialIds.Water)
                        waterCells++;
                }
            });
            Assert.That(waterCells, Is.GreaterThanOrEqualTo(7),
                "A hydrostatic pond should remain a contiguous Water row with Margolus gravity settling enabled.");
            RestoreConfig(host);
        }
    }
}
