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
    public sealed class MargolusMetricTests
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
            host.Config.precipitationRate = 0f;
            host.Config.coreHeatRate = 0f;

            host.Config.useMargolusTransport = true;
            host.Config.useLegacyTransport = false;
            host.Config.margolusSubsteps = 1;
            host.Config.margolusGravityBias = 1f;
            host.Config.margolusReposeFriction = 1f;
            host.Config.margolusMetricEnable = true;
        }

        private static void RestoreConfig(SimulationHost host)
        {
            host.Config.useMargolusTransport = false;
            host.Config.useLegacyTransport = true;
            host.Config.geodynamicsLayerEnable = true;
            host.Config.slowPassInterval = 4;
        }

        [UnityTest]
        public IEnumerator ToroidalSeamAllowsSeamlessCrossBoundarySlump()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 8822;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.60f), 10, height - 20);

            // Paint flat rock floor spanning across the seam (x = width-3 to x = 2)
            for (int offset = -3; offset <= 3; offset++)
            {
                int x = (width + offset) % width;
                Paint(host, x, floorY, MaterialIds.Rock);
                for (int dy = 1; dy <= 5; dy++)
                    Paint(host, x, floorY + dy, MaterialIds.Air);
            }

            int seamX = width - 1;
            // Paint a retaining wall at seamX - 1 so sediment can only slide right across the seam into x = 0
            for (int dy = 1; dy <= 5; dy++)
                Paint(host, seamX - 1, floorY + dy, MaterialIds.Rock);

            // Paint a spire of sediment directly on the boundary column x = width - 1
            for (int dy = 1; dy <= 4; dy++)
            {
                Paint(host, seamX, floorY + dy, MaterialIds.Sediment);
            }

            yield return Step(host, 1); // flush paint
            yield return Step(host, 24); // allow slumping

            bool crossedSeamToX0 = false;
            int totalSedimentOnFloor = 0;
            yield return ReadMaterials(host, mats =>
            {
                uint atX0 = mats[(floorY + 1) * width + 0];
                if (atX0 == MaterialIds.Sediment)
                    crossedSeamToX0 = true;

                for (int offset = -3; offset <= 3; offset++)
                {
                    int x = (width + offset) % width;
                    for (int dy = 1; dy <= 4; dy++)
                    {
                        if (mats[(floorY + dy) * width + x] == MaterialIds.Sediment)
                            totalSedimentOnFloor++;
                    }
                }
            });

            Assert.That(crossedSeamToX0, Is.True,
                "Sediment piled at x = width - 1 must seamlessly cross the toroidal boundary to x = 0.");
            Assert.That(totalSedimentOnFloor, Is.EqualTo(4),
                "Total sediment across the toroidal seam must be exactly conserved.");

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator ReposeSlopesAreStableAtDifferentPlanetaryRadii()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 6644;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;

            // Deep pile (near crust base, y ~ 0.45)
            int deepY = Mathf.Clamp(Mathf.RoundToInt(height * 0.45f), 10, height - 30);
            int deepX = width / 4;

            // Shallow pile (near surface, y ~ 0.80)
            int shallowY = Mathf.Clamp(Mathf.RoundToInt(height * 0.80f), 10, height - 15);
            int shallowX = (3 * width) / 4;

            // Paint floors and sediment towers
            for (int dx = -5; dx <= 5; dx++)
            {
                Paint(host, deepX + dx, deepY, MaterialIds.Rock);
                Paint(host, shallowX + dx, shallowY, MaterialIds.Rock);
                for (int dy = 1; dy <= 5; dy++)
                {
                    Paint(host, deepX + dx, deepY + dy, MaterialIds.Air);
                    Paint(host, shallowX + dx, shallowY + dy, MaterialIds.Air);
                }
            }

            for (int dy = 1; dy <= 4; dy++)
            {
                Paint(host, deepX, deepY + dy, MaterialIds.Sediment);
                Paint(host, shallowX, shallowY + dy, MaterialIds.Sediment);
            }

            yield return Step(host, 1);
            yield return Step(host, 24);

            int deepSedimentRemaining = 0;
            int shallowSedimentRemaining = 0;

            yield return ReadMaterials(host, mats =>
            {
                for (int dx = -5; dx <= 5; dx++)
                {
                    for (int dy = 1; dy <= 4; dy++)
                    {
                        if (mats[(deepY + dy) * width + (deepX + dx)] == MaterialIds.Sediment)
                            deepSedimentRemaining++;
                        if (mats[(shallowY + dy) * width + (shallowX + dx)] == MaterialIds.Sediment)
                            shallowSedimentRemaining++;
                    }
                }
            });

            Assert.That(deepSedimentRemaining, Is.EqualTo(4), "Deep pile mass must be conserved.");
            Assert.That(shallowSedimentRemaining, Is.EqualTo(4), "Shallow pile mass must be conserved.");

            RestoreConfig(host);
        }
    }
}
