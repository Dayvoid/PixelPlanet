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
    public sealed class SpeleogenesisIntegrationTests
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

        private static IEnumerator ReadMaterialsAndAux(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool matDone = false, auxDone = false;
            uint[] mats = null;
            Vector4[] aux = null;

            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
            {
                if (!request.hasError) mats = request.GetData<uint>().ToArray();
                matDone = true;
            });
            AsyncGPUReadback.Request(host.Resources.AuxRead, 0, request =>
            {
                if (!request.hasError) aux = request.GetData<Vector4>().ToArray();
                auxDone = true;
            });

            for (int i = 0; i < 240 && (!matDone || !auxDone); i++)
                yield return null;

            Assert.That(mats, Is.Not.Null);
            Assert.That(aux, Is.Not.Null);
            consume(mats, aux);
        }

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(mode, amount, 0f, 0f)
            });
        }

        private static void ConfigureSpeleogenesisIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.magmaEruption = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;

            host.Config.enableMaterialTransport = true;
            host.Config.gravityStrength = 1f;
            host.Config.margolusSubsteps = 1;
            host.Config.margolusReposeFriction = 1f;
            host.Config.margolusMetricEnable = true;
            host.Config.margolusFluidEnable = true;
        }

        private static void RestoreConfig(SimulationHost host)
        {
            host.Config.enableMaterialTransport = true;
            host.Config.gravityStrength = 1f;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.slowPassInterval = 4;
            host.Config.thermalRate = 1f;
            host.Config.runoffRate = 2f;
            host.Config.pondingRate = 0.5f;
            host.Config.dissolutionRate = 0.03f;
            host.Config.collapseRate = 0.03f;
        }

        [UnityTest]
        public IEnumerator UnsupportedGraniteAndLimestoneFallVerticallyUnderGravity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSpeleogenesisIsolation(host);
            host.Config.seed = 3311;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int xGranite = width / 2 - 4;
            int xLimestone = width / 2 + 4;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Paint bedrock floor at floorY
            for (int dx = -6; dx <= 6; dx++)
                Paint(host, width / 2 + dx, floorY, MaterialIds.Rock);

            // Paint Air corridor
            for (int y = floorY + 1; y <= floorY + 6; y++)
            {
                Paint(host, xGranite, y, MaterialIds.Air);
                Paint(host, xLimestone, y, MaterialIds.Air);
            }

            // Paint unsupported rock blocks at floorY + 5
            Paint(host, xGranite, floorY + 5, MaterialIds.Granite);
            Paint(host, xLimestone, floorY + 5, MaterialIds.Limestone);

            yield return Step(host, 1);
            yield return Step(host, 12);

            yield return ReadMaterials(host, mats =>
            {
                uint landedGranite = mats[(floorY + 1) * width + xGranite];
                uint landedLimestone = mats[(floorY + 1) * width + xLimestone];
                uint oldGraniteSlot = mats[(floorY + 5) * width + xGranite];
                uint oldLimestoneSlot = mats[(floorY + 5) * width + xLimestone];

                Assert.That(landedGranite, Is.EqualTo(MaterialIds.Granite),
                    "Unsupported granite must fall straight down under gravity onto the bedrock floor.");
                Assert.That(landedLimestone, Is.EqualTo(MaterialIds.Limestone),
                    "Unsupported limestone must fall straight down under gravity onto the bedrock floor.");
                Assert.That(oldGraniteSlot, Is.EqualTo(MaterialIds.Air),
                    "Original high cell for granite should now be Air.");
                Assert.That(oldLimestoneSlot, Is.EqualTo(MaterialIds.Air),
                    "Original high cell for limestone should now be Air.");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator BedrockCeilingRemainsStableUnderLowStress()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSpeleogenesisIsolation(host);
            host.Config.seed = 4422;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Paint a 3-wide cave chamber with solid abutments and ceiling
            // Bedrock basement at floorY
            for (int dx = -3; dx <= 3; dx++)
            {
                Paint(host, midX + dx, floorY, MaterialIds.Rock);
                Paint(host, midX + dx, floorY + 3, MaterialIds.Rock); // Caprock
            }
            // Side abutments
            Paint(host, midX - 2, floorY + 1, MaterialIds.Rock);
            Paint(host, midX - 2, floorY + 2, MaterialIds.Rock);
            Paint(host, midX + 2, floorY + 1, MaterialIds.Rock);
            Paint(host, midX + 2, floorY + 2, MaterialIds.Rock);

            // Air cave void at floorY + 1
            for (int dx = -1; dx <= 1; dx++)
                Paint(host, midX + dx, floorY + 1, MaterialIds.Air);

            // Limestone ceiling at floorY + 2
            for (int dx = -1; dx <= 1; dx++)
                Paint(host, midX + dx, floorY + 2, MaterialIds.Limestone);

            yield return Step(host, 1);
            yield return Step(host, 15);

            yield return ReadMaterials(host, mats =>
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    uint ceilMat = mats[(floorY + 2) * width + (midX + dx)];
                    uint airMat = mats[(floorY + 1) * width + (midX + dx)];

                    Assert.That(ceilMat, Is.EqualTo(MaterialIds.Limestone),
                        "Structurally anchored limestone ceiling must remain stable and pinned over the cave void under low stress.");
                    Assert.That(airMat, Is.EqualTo(MaterialIds.Air),
                        "Cave void beneath anchored ceiling must remain Air.");
                }
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator AcidicGroundwaterDissolvesLimestoneIntoCaveCavity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSpeleogenesisIsolation(host);
            host.Config.dissolutionRate = 0.8f;
            host.Config.groundwaterRate = 0.5f;
            host.Config.seed = 5533;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Paint Bedrock floor at floorY
            Paint(host, midX, floorY, MaterialIds.Rock);

            // Air cavity below limestone to qualify as vadose
            Paint(host, midX, floorY + 1, MaterialIds.Air);

            // Soluble limestone body at floorY + 2
            Paint(host, midX, floorY + 2, MaterialIds.Limestone);
            PaintField(host, midX, floorY + 2, 5f, 0.35f); // groundwater aux.y
            PaintField(host, midX, floorY + 2, 4f, 0.5f);  // organic fertility aux.z

            // Soil recharge above limestone at floorY + 3
            Paint(host, midX, floorY + 3, MaterialIds.Soil);
            PaintField(host, midX, floorY + 3, 5f, 0.5f);  // groundwater recharge

            yield return Step(host, 1);

            // Step simulation ticks to drive karst dissolution
            yield return Step(host, 30);

            yield return ReadMaterials(host, mats =>
            {
                uint dissolvedMat = mats[(floorY + 2) * width + midX];
                Assert.That(dissolvedMat == MaterialIds.Air || dissolvedMat == MaterialIds.Water, Is.True,
                    "Limestone under sustained acidic groundwater dissolution must dissolve into a cave void (Air or Water).");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator WideUnsupportedCaveRoofCalvesUnderGravity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSpeleogenesisIsolation(host);
            host.Config.collapseRate = 0.8f;
            host.Config.seed = 6644;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Wide cave void span: 5 air cells wide at floorY + 1 and floorY + 2
            for (int dx = -3; dx <= 3; dx++)
            {
                Paint(host, midX + dx, floorY, MaterialIds.Rock); // Floor
                Paint(host, midX + dx, floorY + 1, MaterialIds.Air);
                Paint(host, midX + dx, floorY + 2, MaterialIds.Air);
            }

            // Unsupported roof span at floorY + 3
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, midX + dx, floorY + 3, MaterialIds.Limestone);
            }

            yield return Step(host, 1);

            // Step simulation to allow bending stress to accumulate and trigger calving
            yield return Step(host, 35);

            yield return ReadMaterials(host, mats =>
            {
                // Verify that at least one central roof cell has calved and fallen to the floor (floorY + 1)
                bool calvedRockOnFloor = false;
                for (int dx = -2; dx <= 2; dx++)
                {
                    uint floorItem = mats[(floorY + 1) * width + (midX + dx)];
                    if (floorItem == MaterialIds.Limestone || floorItem == MaterialIds.Sediment)
                        calvedRockOnFloor = true;
                }

                Assert.That(calvedRockOnFloor, Is.True,
                    "A wide unsupported cave roof must accumulate bending stress and calve fallen blocks onto the cave floor.");
            });

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator ClayAquitardThrottlesPercolationAndRetainsGroundwater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSpeleogenesisIsolation(host);
            host.Config.groundwaterRate = 0.5f;
            host.Config.seed = 7755;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Rock basement at floorY
            Paint(host, midX, floorY, MaterialIds.Rock);

            // Clay aquitard at floorY + 1
            Paint(host, midX, floorY + 1, MaterialIds.Clay);

            // Limestone aquifer above clay at floorY + 2
            Paint(host, midX, floorY + 2, MaterialIds.Limestone);
            PaintField(host, midX, floorY + 2, 5f, 0.40f); // Fully saturate limestone

            yield return Step(host, 1);
            yield return Step(host, 15);

            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                float limestoneWater = aux[(floorY + 2) * width + midX].y;
                Assert.That(limestoneWater, Is.GreaterThan(0.1f),
                    "Clay aquitard must throttle vertical percolation and retain perched groundwater in the limestone layer above it.");
            });

            RestoreConfig(host);
        }
    }
}
