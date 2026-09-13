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
    public sealed class MargolusPrototypeTests
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

            // Enable Margolus CA transport and bypass legacy MACE & arbitration
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
        public IEnumerator ClosedBoxDiscreteSedimentPixelsAreConserved()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 7711;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int midX = width / 2;
            int baseY = Mathf.Clamp(Mathf.RoundToInt(height * 0.65f), 10, height - 20);

            // Paint an exact 4x4 block of sediment (16 pixels) floating in air above a solid granite floor
            int paintedSedimentCount = 0;
            for (int dx = -2; dx <= 1; dx++)
            {
                Paint(host, midX + dx, baseY - 2, MaterialIds.Rock); // support floor
                Paint(host, midX + dx, baseY - 1, MaterialIds.Air);
                for (int dy = 0; dy <= 3; dy++)
                {
                    Paint(host, midX + dx, baseY + dy, MaterialIds.Sediment);
                    paintedSedimentCount++;
                }
            }
            yield return Step(host, 1);

            int initialSedimentCount = 0;
            yield return ReadMaterials(host, mats =>
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == MaterialIds.Sediment)
                        initialSedimentCount++;
                }
            });
            Assert.That(initialSedimentCount, Is.GreaterThanOrEqualTo(paintedSedimentCount));

            // Run 30 ticks of Margolus transport
            yield return Step(host, 30);

            int finalSedimentCount = 0;
            yield return ReadMaterials(host, mats =>
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == MaterialIds.Sediment)
                        finalSedimentCount++;
                }
            });

            // Exact mass conservation: bit-exact discrete token conservation
            Assert.That(finalSedimentCount, Is.EqualTo(initialSedimentCount),
                "Margolus CA transport must conserve discrete material pixel count with exact zero loss.");

            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator UnsupportedSedimentFallsVertically()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 5522;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(height * 0.70f), 15, height - 15);

            // Paint air below and a sediment particle above
            Paint(host, x, y - 4, MaterialIds.Rock);
            Paint(host, x, y - 3, MaterialIds.Air);
            Paint(host, x, y - 2, MaterialIds.Air);
            Paint(host, x, y - 1, MaterialIds.Air);
            Paint(host, x, y, MaterialIds.Sediment);
            Paint(host, x + 1, y, MaterialIds.Sediment);

            yield return Step(host, 1); // flush paint brush
            yield return Step(host, 6); // allow vertical fall

            bool particleDropped = false;
            yield return ReadMaterials(host, mats =>
            {
                // Particle at y should have fallen to y-1, y-2, or y-3
                uint atOriginal = mats[y * width + x];
                uint lower1 = mats[(y - 1) * width + x];
                uint lower2 = mats[(y - 2) * width + x];
                uint lower3 = mats[(y - 3) * width + x];

                if (atOriginal != MaterialIds.Sediment && (lower1 == MaterialIds.Sediment || lower2 == MaterialIds.Sediment || lower3 == MaterialIds.Sediment))
                    particleDropped = true;
            });

            Assert.That(particleDropped, Is.True, "Unsupported sediment should fall vertically under gravity.");
            RestoreConfig(host);
        }

        [UnityTest]
        public IEnumerator OversteepenedColumnSpreadsToTalusSlope()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureMargolusOnly(host);
            host.Config.seed = 3311;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int floorY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);

            // Create flat rock floor
            for (int dx = -6; dx <= 6; dx++)
            {
                Paint(host, x + dx, floorY, MaterialIds.Rock);
                for (int dy = 1; dy <= 6; dy++)
                    Paint(host, x + dx, floorY + dy, MaterialIds.Air);
            }

            // Paint a 1-column spire of 4 sediment blocks on top of floor
            for (int dy = 1; dy <= 4; dy++)
            {
                Paint(host, x, floorY + dy, MaterialIds.Sediment);
            }

            yield return Step(host, 1);
            yield return Step(host, 24);

            bool spreadLaterally = false;
            yield return ReadMaterials(host, mats =>
            {
                // Spire should slump so that adjacent columns (x-1 or x+1) now contain sediment
                uint leftNeighbor = mats[(floorY + 1) * width + (x - 1)];
                uint rightNeighbor = mats[(floorY + 1) * width + (x + 1)];
                if (leftNeighbor == MaterialIds.Sediment || rightNeighbor == MaterialIds.Sediment)
                    spreadLaterally = true;
            });

            Assert.That(spreadLaterally, Is.True, "Oversteepened sediment spire should collapse diagonally into a talus pile.");
            RestoreConfig(host);
        }
    }
}
