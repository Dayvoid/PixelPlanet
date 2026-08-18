using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class WeatherIntegrationTests
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

        private static IEnumerator ReadFields(SimulationHost host, Action<uint[], Vector4[], Vector4[], Vector2[]> consume)
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
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                    {
                        if (auxRequest.hasError) { failed = true; done = true; return; }
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        AsyncGPUReadback.Request(host.Resources.FlowRead, 0, flowRequest =>
                        {
                            if (flowRequest.hasError) { failed = true; done = true; return; }
                            consume(materials, states, aux, flowRequest.GetData<Vector2>().ToArray());
                            done = true;
                        });
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
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

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(mode, amount, 0f, 0f)
            });
        }

        private static void DisableWeatherNoise(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
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
            host.Config.windDamping = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.geyserDischargeRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.radiativeCooling = 0f;
        }

        private static int AtmosphereY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.94f), 4, host.Grid.radialResolution - 3);

        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.86f), 4, host.Grid.radialResolution - 5);

        private static void PaintAirChamber(SimulationHost host, int x0, int x1, int y0, int y1)
        {
            int width = host.Grid.angularResolution;
            for (int y = y0 - 1; y <= y1 + 1; y++)
            {
                for (int x = x0 - 1; x <= x1 + 1; x++)
                {
                    int xx = ((x % width) + width) % width;
                    bool border = y < y0 || y > y1 || x < x0 || x > x1;
                    Paint(host, xx, y, border ? MaterialIds.Rock : MaterialIds.Air);
                }
            }
        }

        private static float SumVapor(Vector4[] aux)
        {
            double sum = 0d;
            for (int i = 0; i < aux.Length; i++)
                sum += Math.Max(0d, aux[i].x);
            return (float)sum;
        }

        private static float SumWater(Vector4[] states, Vector4[] aux)
        {
            double sum = 0d;
            for (int i = 0; i < states.Length; i++)
                sum += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
            return (float)sum;
        }

        private static int CountMaterial(uint[] materials, uint materialId)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == materialId) count++;
            return count;
        }

        [UnityTest]
        public IEnumerator BoilingConvertsToAirWithoutCreatingMass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.phaseHysteresis = 0.01f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = SurfaceY(host);
            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Rock);
            }
            Paint(host, x, y, MaterialIds.Water);
            PaintField(host, x, y, 2f, 0.45f);
            PaintField(host, x, y, 1f, 140f);
            yield return Step(host, 1);

            float totalBefore = 0f;
            float waterBefore = 0f;
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                // After the heated step the cell may already be Air with transferred vapor.
                int index = y * width + x;
                waterBefore = states[index].z;
                totalBefore = SumWater(states, aux);
                Assert.That(mats[index] == MaterialIds.Water || mats[index] == MaterialIds.Air, Is.True,
                    $"Expected water or boiled air, found {mats[index]}");
            });

            yield return Step(host, 8);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                int index = y * width + x;
                Assert.That(mats[index], Is.EqualTo(MaterialIds.Air), "Boiling must leave an Air carrier, not Vapor pixels.");
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                Assert.That(states[index].z, Is.EqualTo(0f).Within(0.001f));
                Assert.That(aux[index].x, Is.GreaterThan(0.2f));
                Assert.That(SumWater(states, aux), Is.EqualTo(totalBefore).Within(0.05f));
            });
        }

        [UnityTest]
        public IEnumerator EvaporationVentsIntoAtmosphereAbove()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 1.5f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 1f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.saturationCapacityScale = 2f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                Paint(host, x + dx, airY, MaterialIds.Air);
                Paint(host, x + dx, airY + 1, MaterialIds.Rock);
            }
            PaintField(host, x, surfaceY, 2f, 0.5f);
            PaintField(host, x, surfaceY, 1f, 40f);
            yield return Step(host, 1);

            float surfaceWaterBefore = 0f;
            float columnVaporBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) =>
            {
                surfaceWaterBefore = states[surfaceY * width + x].z;
                columnVaporBefore = aux[surfaceY * width + x].x + aux[airY * width + x].x;
            });

            yield return Step(host, 25);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                Assert.That(mats[surfaceY * width + x], Is.EqualTo(MaterialIds.Soil));
                float surfaceWaterAfter = states[surfaceY * width + x].z;
                float columnVaporAfter = aux[surfaceY * width + x].x + aux[airY * width + x].x;
                Assert.That(surfaceWaterAfter, Is.LessThan(surfaceWaterBefore - 0.01f));
                Assert.That(columnVaporAfter, Is.GreaterThan(columnVaporBefore + 0.01f));
                Assert.That(mats[airY * width + x], Is.EqualTo(MaterialIds.Air));
            });
        }

        [UnityTest]
        public IEnumerator VaporAdvectsDownwindAndConservesMass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 3f;
            host.Config.atmosphericAdvectionRate = 3f;
            host.Config.vaporDiffusionRate = 0.25f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.saturationCapacityScale = 2f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = AtmosphereY(host);
            int x0 = width / 2;
            PaintAirChamber(host, x0 - 20, x0 + 20, y - 1, y + 1);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dx = -20; dx <= 20; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int xx = ((x0 + dx) % width + width) % width;
                        float vapor = aux[(y + dy) * width + xx].x;
                        if (vapor > 0f)
                            PaintField(host, xx, y + dy, 6f, -vapor);
                    }
                }
            });
            yield return Step(host, 1);

            PaintField(host, x0, y, 6f, 0.8f);
            for (int dx = -8; dx <= -1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    PaintField(host, x0 + dx, y + dy, 3f, 0.35f);
            for (int dx = 1; dx <= 8; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    PaintField(host, x0 + dx, y + dy, 3f, -0.35f);
            yield return Step(host, 1);

            float vaporBefore = 0f;
            float centroidBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                float mass = 0f;
                float moment = 0f;
                for (int dx = -20; dx <= 20; dx++)
                {
                    int xx = ((x0 + dx) % width + width) % width;
                    float v = aux[y * width + xx].x;
                    mass += v;
                    moment += v * dx;
                }
                vaporBefore = mass;
                centroidBefore = mass > 1e-5f ? moment / mass : 0f;
            });
            Assert.That(vaporBefore, Is.GreaterThan(0.5f));

            yield return Step(host, 50);

            yield return ReadFields(host, (mats, _, aux, __) =>
            {
                float mass = 0f;
                float moment = 0f;
                int vaporCells = 0;
                float peak = 0f;
                int peakDx = 0;
                for (int dx = -20; dx <= 20; dx++)
                {
                    int xx = ((x0 + dx) % width + width) % width;
                    Assert.That(mats[y * width + xx], Is.Not.EqualTo(MaterialIds.Vapor));
                    float v = aux[y * width + xx].x;
                    if (v > 0.02f) vaporCells++;
                    if (v > peak) { peak = v; peakDx = dx; }
                    mass += v;
                    moment += v * dx;
                }
                float centroidAfter = mass > 1e-5f ? moment / mass : 0f;
                Assert.That(mass, Is.EqualTo(vaporBefore).Within(0.05f));
                Assert.That(centroidAfter > centroidBefore + 0.05f || peakDx > 0, Is.True,
                    $"Expected downwind shift, centroid {centroidBefore} -> {centroidAfter}, peakDx={peakDx}");
                Assert.That(vaporCells, Is.GreaterThan(1));
            });
        }

        [UnityTest]
        public IEnumerator VaporAdvectionWrapsAcrossAngularSeam()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 3f;
            host.Config.atmosphericAdvectionRate = 3f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.saturationCapacityScale = 2f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = AtmosphereY(host);
            PaintAirChamber(host, width - 8, width + 7, y - 1, y + 1);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int x = width - 8; x < width + 8; x++)
                {
                    int xx = ((x % width) + width) % width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        float vapor = aux[(y + dy) * width + xx].x;
                        if (vapor > 0f) PaintField(host, xx, y + dy, 6f, -vapor);
                    }
                }
            });
            yield return Step(host, 1);

            float globalBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) => globalBefore = SumVapor(aux));

            PaintField(host, width - 1, y, 6f, 0.8f);
            for (int dy = -1; dy <= 1; dy++)
            {
                PaintField(host, width - 4, y + dy, 3f, 2f);
                PaintField(host, 2, y + dy, 3f, -2f);
            }
            yield return Step(host, 1);

            float leftBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int x = 0; x < 4; x++)
                    for (int dy = -1; dy <= 1; dy++)
                        leftBefore += aux[(y + dy) * width + x].x;
            });

            yield return Step(host, 50);

            yield return ReadFields(host, (_, __, aux, flow) =>
            {
                float leftAfter = 0f;
                for (int x = 0; x < 4; x++)
                    for (int dy = -1; dy <= 1; dy++)
                        leftAfter += aux[(y + dy) * width + x].x;
                Assert.That(SumVapor(aux), Is.EqualTo(globalBefore + 0.8f).Within(0.1f));
                Assert.That(leftAfter > leftBefore + 0.02f || flow[y * width + (width - 1)].x > 0.01f, Is.True,
                    $"Expected wrap transport or positive seam flow. left {leftBefore}->{leftAfter}");
            });
        }

        [UnityTest]
        public IEnumerator WarmHumidAirProducesOutwardBuoyancy()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.humidityBuoyancy = 1f;
            host.Config.pressureDiffusionRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            for (int dy = -2; dy <= 2; dy++)
                Paint(host, x, y + dy, MaterialIds.Air);
            yield return Step(host, 1);

            PaintField(host, x, y, 1f, -30f);
            PaintField(host, x, y - 1, 1f, 50f);
            PaintField(host, x, y - 1, 6f, 0.8f);
            yield return Step(host, 8);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[(y - 1) * width + x].y, Is.GreaterThan(0.01f));
            });
        }

        [UnityTest]
        public IEnumerator ColdAtmosphereCondensesExcessVaporToCloud()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 2f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.saturationCapacityScale = 0.05f;
            host.Config.cloudPrecipitationThreshold = 1f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            PaintAirChamber(host, x - 2, x + 2, y, y);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    float vapor = aux[y * width + (x + dx)].x;
                    if (vapor > 0f) PaintField(host, x + dx, y, 6f, -vapor);
                }
            });
            yield return Step(host, 1);

            PaintField(host, x, y, 1f, -40f);
            PaintField(host, x, y, 6f, 0.9f);
            yield return Step(host, 1);

            float vaporBefore = 0f;
            float cloudBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) =>
            {
                int index = y * width + x;
                vaporBefore = aux[index].x;
                cloudBefore = states[index].z;
            });
            Assert.That(vaporBefore, Is.GreaterThan(0.5f));

            yield return Step(host, 20);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                int index = y * width + x;
                Assert.That(mats[index], Is.EqualTo(MaterialIds.Air));
                float vaporAfter = aux[index].x;
                float cloudAfter = states[index].z;
                Assert.That(vaporAfter, Is.LessThan(vaporBefore - 0.05f));
                Assert.That(cloudAfter, Is.GreaterThan(cloudBefore + 0.05f));
                Assert.That(vaporAfter + cloudAfter, Is.EqualTo(vaporBefore + cloudBefore).Within(0.05f));
            });
        }

        [UnityTest]
        public IEnumerator AtmosphericRainDepositsOnSurfaceBelow()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.cloudPrecipitationThreshold = 0.02f;
            host.Config.runoffRate = 1f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            for (int dx = -1; dx <= 1; dx++)
            {
                Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                Paint(host, x + dx, airY, MaterialIds.Air);
                Paint(host, x + dx, airY + 1, MaterialIds.Rock);
            }
            yield return Step(host, 1);

            // Inject cloud condensate above the surface with precipitation disabled.
            PaintField(host, x, airY, 2f, 0.5f);
            yield return Step(host, 1);

            float surfaceBefore = 0f;
            float cloudBefore = 0f;
            yield return ReadFields(host, (mats, states, __, ___) =>
            {
                Assert.That(mats[airY * width + x], Is.EqualTo(MaterialIds.Air));
                Assert.That(mats[surfaceY * width + x], Is.EqualTo(MaterialIds.Soil));
                surfaceBefore = states[surfaceY * width + x].z;
                cloudBefore = states[airY * width + x].z;
            });
            Assert.That(cloudBefore, Is.GreaterThan(0.1f));

            host.Config.precipitationRate = 3f;
            yield return Step(host, 40);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                float surfaceAfter = states[surfaceY * width + x].z;
                float cloudAfter = states[airY * width + x].z;
                Assert.That(cloudAfter, Is.LessThan(cloudBefore - 0.05f));
                Assert.That(surfaceAfter, Is.GreaterThan(surfaceBefore + 0.05f));
            });
        }

        [UnityTest]
        public IEnumerator SameSeedProducesIdenticalWeatherStateAfterNTicks()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.seed = 5150;
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.evaporationRate = 0.25f;
            host.Config.condensationRate = 0.2f;
            host.Config.precipitationRate = 0.25f;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            uint[] materialsA = null;
            Vector4[] statesA = null;
            Vector4[] auxA = null;
            Vector2[] flowA = null;
            for (int i = 0; i < 40; i++) { host.Clock.RequestStep(); yield return null; }
            yield return ReadFields(host, (m, s, a, f) =>
            {
                materialsA = (uint[])m.Clone();
                statesA = (Vector4[])s.Clone();
                auxA = (Vector4[])a.Clone();
                flowA = (Vector2[])f.Clone();
            });

            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            for (int i = 0; i < 40; i++) { host.Clock.RequestStep(); yield return null; }

            yield return ReadFields(host, (m, s, a, f) =>
            {
                Assert.That(m, Is.EqualTo(materialsA));
                Assert.That(s, Is.EqualTo(statesA));
                Assert.That(a, Is.EqualTo(auxA));
                Assert.That(f, Is.EqualTo(flowA));
            });
        }

        [UnityTest]
        public IEnumerator WeatherCycleConservesTrackedWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Config.vaporDiffusionRate = 0.08f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            for (int i = 0; i < 5; i++) yield return null;

            float waterBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) => waterBefore = SumWater(states, aux));

            for (int i = 0; i < 120; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            float waterAfter = 0f;
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                waterAfter = SumWater(states, aux);
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
            });

            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;

            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
            Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Math.Max(1d, waterBefore * 0.05d)));
        }
    }
}
