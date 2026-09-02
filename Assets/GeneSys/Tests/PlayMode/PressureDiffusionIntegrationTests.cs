using System;
using System.Collections;
using GeneSys.Configuration;
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
    public sealed class PressureDiffusionIntegrationTests
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

        private static IEnumerator ReadMaterialsAndState(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { failed = true; done = true; return; }
                    consume(materials, stateRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
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

        private static void PaintPressure(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(3f, amount, 0f, 0f)
            });
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void DisablePressureSourcesAndDecay(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.slowPassInterval = 100000;
            host.Config.validationIntervalTicks = 100000;
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.windStrength = 0f;
            host.Config.windDamping = 1f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.terrainSolarHeating = 0f;
            host.Config.atmosphereSolarHeating = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.geyserDischargeRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.baseSoilCohesion = 0f;
        }

        private static void ConfigureCategoryDiffusivities(SimulationHost host, float gas, float fluid, float porous, float rigid)
        {
            host.Config.pressureDiffusionRate = 1f;
            host.Config.gasPressureDiffusivity = gas;
            host.Config.fluidPressureDiffusivity = fluid;
            host.Config.porousPressureDiffusivity = porous;
            host.Config.rigidPressureDiffusivity = rigid;
            host.Config.pressureEquilibriumGradient = 0f;
            host.Config.pressureEquilibriumMaximum = 0f;
        }

        private static float SumPressure(Vector4[] states, int width, int x0, int x1, int y0, int y1)
        {
            float sum = 0f;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int xx = ((x % width) + width) % width;
                    sum += states[y * width + xx].y;
                }
            }
            return sum;
        }

        private static void FillBlock(SimulationHost host, int x0, int x1, int y0, int y1, uint materialId)
        {
            int width = host.Grid.angularResolution;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int xx = ((x % width) + width) % width;
                    Paint(host, xx, y, materialId);
                }
            }
        }

        [UnityTest]
        public IEnumerator GasPulseSpreadsAndConservesPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            ConfigureCategoryDiffusivities(host, gas: 1f, fluid: 0.01f, porous: 0.01f, rigid: 0.01f);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.92f), 4, host.Grid.radialResolution - 4);
            FillBlock(host, x - 6, x + 6, y - 2, y + 2, MaterialIds.Air);
            yield return Step(host, 1);

            // Zero residual pressure in the block, then inject a pulse.
            Vector4[] beforeZero = null;
            yield return ReadMaterialsAndState(host, (_, states) => beforeZero = states);
            for (int yy = y - 2; yy <= y + 2; yy++)
            {
                for (int xx = x - 6; xx <= x + 6; xx++)
                {
                    float current = beforeZero[yy * width + xx].y;
                    if (current > 0f)
                        PaintPressure(host, xx, yy, -current);
                }
            }
            yield return Step(host, 1);

            const float pulse = 8f;
            PaintPressure(host, x, y, pulse);
            yield return Step(host, 1);

            float centerAfterPaint = 0f;
            float neighborAfterPaint = 0f;
            float sumAfterPaint = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                centerAfterPaint = states[y * width + x].y;
                neighborAfterPaint = states[y * width + (x + 1)].y;
                sumAfterPaint = SumPressure(states, width, x - 6, x + 6, y - 2, y + 2);
            });
            Assert.That(centerAfterPaint, Is.GreaterThan(pulse * 0.5f));

            yield return Step(host, 24);

            float centerLater = 0f;
            float neighborLater = 0f;
            float sumLater = 0f;
            float minPressure = float.MaxValue;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                centerLater = states[y * width + x].y;
                neighborLater = states[y * width + (x + 1)].y;
                sumLater = SumPressure(states, width, x - 6, x + 6, y - 2, y + 2);
                for (int yy = y - 2; yy <= y + 2; yy++)
                {
                    for (int xx = x - 6; xx <= x + 6; xx++)
                        minPressure = Mathf.Min(minPressure, states[yy * width + xx].y);
                }
            });

            Assert.That(centerLater, Is.LessThan(centerAfterPaint));
            Assert.That(neighborLater, Is.GreaterThan(neighborAfterPaint));
            Assert.That(sumLater, Is.EqualTo(sumAfterPaint).Within(0.35f));
            Assert.That(minPressure, Is.GreaterThanOrEqualTo(-0.001f));
        }

        [UnityTest]
        public IEnumerator CategoryRatesEquilibrateInConfiguredOrder()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            ConfigureCategoryDiffusivities(host, gas: 1f, fluid: 0.35f, porous: 0.12f, rigid: 0.02f);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.7f), 8, host.Grid.radialResolution - 8);
            int gasX = width / 5;
            int fluidX = (2 * width) / 5;
            int porousX = (3 * width) / 5;
            int rigidX = (4 * width) / 5;

            FillBlock(host, gasX - 3, gasX + 3, y - 2, y + 2, MaterialIds.Rock);
            FillBlock(host, gasX - 2, gasX + 2, y - 1, y + 1, MaterialIds.Air);
            FillBlock(host, fluidX - 3, fluidX + 3, y - 2, y + 2, MaterialIds.Rock);
            FillBlock(host, fluidX - 2, fluidX + 2, y - 1, y + 1, MaterialIds.Water);
            FillBlock(host, porousX - 3, porousX + 3, y - 2, y + 2, MaterialIds.Rock);
            FillBlock(host, porousX - 2, porousX + 2, y - 1, y + 1, MaterialIds.Soil);
            FillBlock(host, rigidX - 3, rigidX + 3, y - 2, y + 2, MaterialIds.Rock);
            FillBlock(host, rigidX - 2, rigidX + 2, y - 1, y + 1, MaterialIds.Rock);
            yield return Step(host, 1);

            void ZeroAndPulse(int cx)
            {
                for (int yy = y - 1; yy <= y + 1; yy++)
                {
                    for (int xx = cx - 2; xx <= cx + 2; xx++)
                        PaintPressure(host, xx, yy, -20f);
                }
            }

            ZeroAndPulse(gasX);
            ZeroAndPulse(fluidX);
            ZeroAndPulse(porousX);
            ZeroAndPulse(rigidX);
            yield return Step(host, 1);

            PaintPressure(host, gasX, y, 6f);
            PaintPressure(host, fluidX, y, 6f);
            PaintPressure(host, porousX, y, 6f);
            PaintPressure(host, rigidX, y, 6f);
            yield return Step(host, 1);

            float gasCenter = 0f, gasNeighbor = 0f;
            float fluidCenter = 0f, fluidNeighbor = 0f;
            float porousCenter = 0f, porousNeighbor = 0f;
            float rigidCenter = 0f, rigidNeighbor = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                gasCenter = states[y * width + gasX].y;
                gasNeighbor = states[y * width + gasX + 1].y;
                fluidCenter = states[y * width + fluidX].y;
                fluidNeighbor = states[y * width + fluidX + 1].y;
                porousCenter = states[y * width + porousX].y;
                porousNeighbor = states[y * width + porousX + 1].y;
                rigidCenter = states[y * width + rigidX].y;
                rigidNeighbor = states[y * width + rigidX + 1].y;
            });

            yield return Step(host, 12);

            float gasCenter2 = 0f, gasNeighbor2 = 0f;
            float fluidCenter2 = 0f, fluidNeighbor2 = 0f;
            float porousCenter2 = 0f, porousNeighbor2 = 0f;
            float rigidCenter2 = 0f, rigidNeighbor2 = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                gasCenter2 = states[y * width + gasX].y;
                gasNeighbor2 = states[y * width + gasX + 1].y;
                fluidCenter2 = states[y * width + fluidX].y;
                fluidNeighbor2 = states[y * width + fluidX + 1].y;
                porousCenter2 = states[y * width + porousX].y;
                porousNeighbor2 = states[y * width + porousX + 1].y;
                rigidCenter2 = states[y * width + rigidX].y;
                rigidNeighbor2 = states[y * width + rigidX + 1].y;
            });

            // Compare how far each category has actually carried the pulse after the
            // same window. Fast gas can already be near equilibrium by the first
            // post-paint sample, so neighbor deltas after that are not ordered.
            Assert.That(gasNeighbor2, Is.GreaterThan(fluidNeighbor2 - 0.002f));
            Assert.That(fluidNeighbor2, Is.GreaterThan(porousNeighbor2));
            Assert.That(porousNeighbor2, Is.GreaterThanOrEqualTo(rigidNeighbor2 - 0.01f));
            Assert.That(gasCenter2, Is.LessThan(gasCenter));
            Assert.That(gasCenter2, Is.LessThan(fluidCenter2 + 0.002f));
            Assert.That(rigidCenter - rigidCenter2, Is.LessThan(gasCenter - gasCenter2));
        }

        [UnityTest]
        public IEnumerator RadialAnomaliesRelaxTowardConfiguredProfile()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            host.Config.pressureDiffusionRate = 1.5f;
            host.Config.gasPressureDiffusivity = 1f;
            host.Config.fluidPressureDiffusivity = 1f;
            host.Config.porousPressureDiffusivity = 1f;
            host.Config.rigidPressureDiffusivity = 1f;
            host.Config.pressureEquilibriumGradient = 2f;
            host.Config.pressureEquilibriumMaximum = 2f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int yLow = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.35f), 4, host.Grid.radialResolution - 8);
            int yHigh = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.85f), yLow + 4, host.Grid.radialResolution - 4);

            FillBlock(host, x - 4, x + 4, yLow, yHigh, MaterialIds.Air);
            yield return Step(host, 1);

            for (int y = yLow; y <= yHigh; y++)
            {
                for (int xx = x - 4; xx <= x + 4; xx++)
                    PaintPressure(host, xx, y, -20f);
            }
            yield return Step(host, 1);

            // Flat absolute pressure creates a radial anomaly against the equilibrium profile.
            for (int y = yLow; y <= yHigh; y++)
            {
                for (int xx = x - 4; xx <= x + 4; xx++)
                    PaintPressure(host, xx, y, 1f);
            }
            yield return Step(host, 1);

            float pressureLowBefore = 0f;
            float pressureHighBefore = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                pressureLowBefore = states[yLow * width + x].y;
                pressureHighBefore = states[yHigh * width + x].y;
            });

            yield return Step(host, 40);

            float pressureLowAfter = 0f;
            float pressureHighAfter = 0f;
            float minPressure = float.MaxValue;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                pressureLowAfter = states[yLow * width + x].y;
                pressureHighAfter = states[yHigh * width + x].y;
                for (int y = yLow; y <= yHigh; y++)
                    minPressure = Mathf.Min(minPressure, states[y * width + x].y);
            });

            float radiusLow = host.Grid.Radius01(yLow);
            float radiusHigh = host.Grid.Radius01(yHigh);
            float eqLow = Mathf.Min(2f, (1f - radiusLow) * 2f);
            float eqHigh = Mathf.Min(2f, (1f - radiusHigh) * 2f);
            Assert.That(eqLow, Is.GreaterThan(eqHigh));

            // Anomaly diffusion should deepen the radial gradient toward the equilibrium profile.
            Assert.That(pressureLowAfter - pressureHighAfter, Is.GreaterThan(pressureLowBefore - pressureHighBefore));
            Assert.That(pressureLowAfter, Is.GreaterThan(pressureHighAfter));
            Assert.That(minPressure, Is.GreaterThanOrEqualTo(-0.001f));
        }

        [UnityTest]
        public IEnumerator AngularDiffusionWrapsAcrossSeam()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            ConfigureCategoryDiffusivities(host, gas: 1f, fluid: 0.01f, porous: 0.01f, rigid: 0.01f);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.92f), 4, host.Grid.radialResolution - 4);
            FillBlock(host, width - 4, width + 3, y - 1, y + 1, MaterialIds.Air);
            yield return Step(host, 1);

            for (int xx = -3; xx <= 3; xx++)
            {
                int x = ((xx % width) + width) % width;
                for (int yy = y - 1; yy <= y + 1; yy++)
                    PaintPressure(host, x, yy, -20f);
            }
            yield return Step(host, 1);

            PaintPressure(host, 0, y, 8f);
            yield return Step(host, 1);

            float leftBefore = 0f;
            float rightBefore = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                leftBefore = states[y * width + (width - 1)].y;
                rightBefore = states[y * width + 1].y;
            });

            yield return Step(host, 20);

            float leftAfter = 0f;
            float rightAfter = 0f;
            float centerAfter = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                leftAfter = states[y * width + (width - 1)].y;
                rightAfter = states[y * width + 1].y;
                centerAfter = states[y * width].y;
            });

            Assert.That(leftAfter, Is.GreaterThan(leftBefore));
            Assert.That(rightAfter, Is.GreaterThan(rightBefore));
            Assert.That(centerAfter, Is.GreaterThan(0.5f));
        }

        [UnityTest]
        public IEnumerator HighGradientDoesNotInventPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            ConfigureCategoryDiffusivities(host, gas: 1f, fluid: 0.01f, porous: 0.01f, rigid: 0.01f);
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.92f), 4, host.Grid.radialResolution - 4);
            FillBlock(host, x - 2, x + 2, y - 1, y + 1, MaterialIds.Air);
            yield return Step(host, 1);

            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                for (int yy = y - 1; yy <= y + 1; yy++)
                for (int xx = x - 2; xx <= x + 2; xx++)
                {
                    float current = states[yy * width + xx].y;
                    if (current > 0f)
                        PaintPressure(host, xx, yy, -current);
                }
            });
            yield return Step(host, 1);

            const float high = 8f;
            const float low = 0.05f;
            PaintPressure(host, x, y, high);
            PaintPressure(host, x + 1, y, low);
            yield return Step(host, 1);

            float donorBefore = 0f;
            float receiverBefore = 0f;
            float totalBefore = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                donorBefore = states[y * width + x].y;
                receiverBefore = states[y * width + (x + 1)].y;
                totalBefore = SumPressure(states, width, x - 2, x + 2, y - 1, y + 1);
            });

            yield return Step(host, 1);

            float donorAfter = 0f;
            float receiverAfter = 0f;
            float totalAfter = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                donorAfter = states[y * width + x].y;
                receiverAfter = states[y * width + (x + 1)].y;
                totalAfter = SumPressure(states, width, x - 2, x + 2, y - 1, y + 1);
            });

            Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(0.05f));
            Assert.That(receiverAfter - receiverBefore, Is.LessThanOrEqualTo(donorBefore * 0.25f + 0.02f));
            Assert.That(donorBefore - donorAfter, Is.GreaterThanOrEqualTo(receiverAfter - receiverBefore - 0.02f));
            Assert.That(donorAfter, Is.GreaterThanOrEqualTo(-0.001f));
            Assert.That(receiverAfter, Is.GreaterThanOrEqualTo(-0.001f));
        }

        [UnityTest]
        public IEnumerator AlternatingThermalGradientDoesNotPumpPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisablePressureSourcesAndDecay(host);
            host.Config.thermalRate = 1f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.92f), 4, host.Grid.radialResolution - 4);
            FillBlock(host, x - 2, x + 2, y - 1, y + 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Air);
            Paint(host, x + 1, y, MaterialIds.Air);
            yield return Step(host, 1);

            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                for (int xx = x; xx <= x + 1; xx++)
                {
                    float pressure = states[y * width + xx].y;
                    if (pressure > 0f)
                        PaintPressure(host, xx, y, -pressure);
                }
            });
            yield return Step(host, 1);
            PaintPressure(host, x, y, 1f);
            PaintPressure(host, x + 1, y, 1f);
            yield return Step(host, 1);

            float totalBefore = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                totalBefore = states[y * width + x].y + states[y * width + (x + 1)].y;
            });

            for (int i = 0; i < 16; i++)
            {
                bool flip = (i & 1) == 0;
                yield return ReadMaterialsAndState(host, (_, states) =>
                {
                    float left = states[y * width + x].x;
                    float right = states[y * width + (x + 1)].x;
                    float leftTarget = flip ? 40f : 0f;
                    float rightTarget = flip ? 0f : 40f;
                    PaintHeat(host, x, y, leftTarget - left);
                    PaintHeat(host, x + 1, y, rightTarget - right);
                });
                yield return Step(host, 1);
            }

            float totalAfter = 0f;
            yield return ReadMaterialsAndState(host, (_, states) =>
            {
                totalAfter = states[y * width + x].y + states[y * width + (x + 1)].y;
                Assert.That(states[y * width + x].y, Is.GreaterThanOrEqualTo(-0.001f));
                Assert.That(states[y * width + (x + 1)].y, Is.GreaterThanOrEqualTo(-0.001f));
            });
            Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(0.35f),
                $"Alternating thermal gradient pumped pressure from {totalBefore:F3} to {totalAfter:F3}.");
        }

        private static void PaintHeat(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(1f, amount, 0f, 0f)
            });
        }
    }
}
