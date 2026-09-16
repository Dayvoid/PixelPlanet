using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class StressVolcanismReproTests
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

        private static IEnumerator MeasureGeology(SimulationHost host, Action<WorldGeologyMetrics> assign)
        {
            bool ready = false;
            WorldGeologyMetrics metrics = default;
            SimulationMetrics.MeasureGeologyAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
        }

        private static IEnumerator MeasureGeodynamics(SimulationHost host, Action<WorldGeodynamicsMetrics> assign)
        {
            bool ready = false;
            WorldGeodynamicsMetrics metrics = default;
            SimulationMetrics.MeasureGeodynamicsAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
        }

        private static float MaxBinMagmaFill(uint[] materials, SimulationHost host)
        {
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            float maxFill = 0f;
            for (int a = 0; a < angularBins; a++)
            {
                GeodynamicsGrid.ThetaRange(a, width, angularBins, out int x0, out int x1);
                for (int r = 0; r < radialBins; r++)
                {
                    GeodynamicsGrid.RadialRange(r, height, host.Grid.atmosphereStartRadius, radialBins, out int y0, out int y1);
                    int cells = 0;
                    int magma = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            cells++;
                            if (materials[y * width + x] == MaterialIds.Magma)
                                magma++;
                        }
                    }
                    if (cells > 0)
                        maxFill = Mathf.Max(maxFill, magma / (float)cells);
                }
            }
            return maxFill;
        }

        private static int CountBinEdgeAlignedTiles(uint[] materials, SimulationHost host)
        {
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int angularBins = host.Config.geodynamicsAngularBins;
            int radialBins = host.Config.geodynamicsRadialBins;
            int aligned = 0;
            for (int a = 0; a < angularBins; a++)
            {
                GeodynamicsGrid.ThetaRange(a, width, angularBins, out int x0, out int x1);
                int binWidth = Mathf.Max(1, x1 - x0);
                if (binWidth < 4) continue;
                for (int r = 0; r < radialBins; r++)
                {
                    GeodynamicsGrid.RadialRange(r, height, host.Grid.atmosphereStartRadius, radialBins, out int y0, out int y1);
                    int magma = 0;
                    int minX = int.MaxValue;
                    int maxX = int.MinValue;
                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            if (materials[y * width + x] != MaterialIds.Magma) continue;
                            magma++;
                            minX = Mathf.Min(minX, x);
                            maxX = Mathf.Max(maxX, x);
                        }
                    }
                    if (magma < 12) continue;
                    if (minX == x0 && maxX == x1 - 1 && magma / (float)Mathf.Max(1, binWidth * Mathf.Max(1, y1 - y0)) >= 0.35f)
                        aligned++;
                }
            }
            return aligned;
        }

        [UnityTest]
        [Category("Stress")]
        public IEnumerator StressPresetVolcanismStaysBoundedThroughTick1500()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.RestoreDefaultSettings();
            host.ApplyPreset(SimulationPreset.Stress);
            host.Config.seed = 12345;
            host.Config.floraGrowthRate = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.treeWaterUptakeRate = 0f;
            host.Config.enableRockChunks = false;
            host.Config.validationIntervalTicks = 100000;
            for (int i = 0; i < 5; i++) yield return null;

            int[] checkTicks = { 250, 500, 1000, 1500 };
            int currentTick = 0;
            int previousMagma = 0;
            for (int t = 0; t < checkTicks.Length; t++)
            {
                int target = checkTicks[t];
                yield return Step(host, target - currentTick);
                currentTick = target;

                uint[] materials = null;
                yield return ReadMaterials(host, mats => materials = mats);
                WorldGeologyMetrics geology = default;
                WorldGeodynamicsMetrics geo = default;
                yield return MeasureGeology(host, result => geology = result);
                yield return MeasureGeodynamics(host, result => geo = result);

                float maxFill = MaxBinMagmaFill(materials, host);
                Debug.Log($"STRESS_VOLCANISM_{currentTick}: magma={geology.MagmaCount} fill={maxFill:P2} events={geo.ActiveVolcanicCount} safety={geo.SafetyThrottle:F2}");

                Assert.That(geo.HasNonFinite, Is.False, $"Non-finite geodynamics at tick {currentTick}.");
                Assert.That(geology.MagmaFraction, Is.LessThanOrEqualTo(host.Config.volcanicMagmaFractionLimit + 0.01f),
                    $"Magma fraction breached the configured limit at tick {currentTick}.");
                Assert.That(maxFill, Is.LessThan(0.85f),
                    $"A lattice tile converted wholesale at tick {currentTick}.");
                Assert.That(CountBinEdgeAlignedTiles(materials, host), Is.EqualTo(0),
                    $"Melt footprint snapped to lattice bin edges at tick {currentTick}.");
                if (currentTick <= 500)
                    Assert.That(geology.MagmaCount - previousMagma, Is.LessThan(4000),
                        $"Magma jumped too quickly between samples at tick {currentTick}.");
                previousMagma = geology.MagmaCount;
            }

            host.ApplyPreset(SimulationPreset.Standard);
            yield return null;
        }
    }
}
