using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
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
        private SimulationConfigSnapshot _configSnapshot;

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

        private IEnumerator WaitForHostAndSnapshot()
        {
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            _configSnapshot = SimulationConfigSnapshot.Capture(host.Config);
            ResetWeatherDefaults(host);
        }

        private static void ResetWeatherDefaults(SimulationHost host)
        {
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.ticksPerSecond = 20f;
            host.Config.dayLengthSeconds = 180f;
            host.Config.solarIntensity = 0.8f;
            host.Config.spaceTemperature = -25f;
            host.Config.radiativeCooling = 0.2f;
            host.Config.windStrength = 0.35f;
            host.Config.windDamping = 0.06f;
            host.Config.evaporationRate = 0.1f;
            host.Config.condensationRate = 0.12f;
            host.Config.precipitationRate = 0.2f;
            host.Config.vaporPressureScale = 0.25f;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Config.vaporDiffusionRate = 0.05f;
            host.Config.atmosphericBuoyancy = 0.4f;
            host.Config.humidityBuoyancy = 0.25f;
            host.Config.saturationCapacityScale = 0.55f;
            host.Config.cloudPrecipitationThreshold = 0.05f;
            host.Config.surfaceAirHeatExchange = 0.45f;
            host.Config.temperatureAdvectionRate = 0.55f;
            host.Config.pressureCompressibility = 0.45f;
            host.Config.atmosphericCflLimit = 0.4f;
            host.Config.surfaceAirTemperature = 18f;
            host.Config.atmosphericLapseRate = 12f;
            host.Config.pressureRate = 0.4f;
            host.Config.pressureDiffusionRate = 0.5f;
            host.Config.slowPassInterval = 4;
            host.Config.validationIntervalTicks = 100000;
            host.Config.mycologyAirTransportRate = 0f;
            host.Config.mycologyWaterTransportRate = 0f;
            host.Config.mycologyDiffusionRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.mycologySporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
        }

        [UnityTearDown]
        public IEnumerator RestoreConfig()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            if (host != null && host.Config != null && _configSnapshot != null)
                _configSnapshot.Restore(host.Config);
            _configSnapshot = null;
            yield return null;
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
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.pressureCompressibility = 0f;
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

        private static float SumWaterBox(Vector4[] states, Vector4[] aux, int width, int x0, int x1, int y0, int y1)
        {
            double sum = 0d;
            int height = states.Length / Math.Max(1, width);
            for (int y = y0; y <= y1; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = x0; x <= x1; x++)
                {
                    int xx = ((x % width) + width) % width;
                    int index = y * width + xx;
                    sum += Math.Max(0d, states[index].z) + Math.Max(0d, aux[index].x) + Math.Max(0d, aux[index].y);
                }
            }
            return (float)sum;
        }

        private static int CountMaterial(uint[] materials, uint materialId)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == materialId) count++;
            return count;
        }

        private static bool IsFinite(Vector4[] states, Vector4[] aux, Vector2[] flow)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (!IsFinite(states[i]) || !IsFinite(aux[i])) return false;
                if (i < flow.Length && (!float.IsFinite(flow[i].x) || !float.IsFinite(flow[i].y))) return false;
            }
            return true;
        }

        private static bool IsFinite(Vector4 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        [UnityTest]
        public IEnumerator BoilingConvertsToAirWithoutCreatingMass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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

            float localBefore = 0f;
            uint boiledMaterial = 0;
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                boiledMaterial = mats[y * width + x];
                localBefore = SumWaterBox(states, aux, width, x - 4, x + 4, y - 2, y + 2);
            });
            Assert.That(boiledMaterial == MaterialIds.Water || boiledMaterial == MaterialIds.Air, Is.True,
                $"Expected water or boiled air, found {boiledMaterial}");

            yield return Step(host, 8);

            uint afterMaterial = 0;
            float localAfter = 0f;
            float cellVapor = 0f;
            float cellCloud = 0f;
            int vaporPixels = 0;
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                int index = y * width + x;
                afterMaterial = mats[index];
                cellCloud = states[index].z;
                cellVapor = aux[index].x;
                vaporPixels = CountMaterial(mats, MaterialIds.Vapor);
                localAfter = SumWaterBox(states, aux, width, x - 4, x + 4, y - 2, y + 2);
            });
            Assert.That(afterMaterial, Is.EqualTo(MaterialIds.Air), "Boiling must leave an Air carrier, not Vapor pixels.");
            Assert.That(vaporPixels, Is.EqualTo(0));
            Assert.That(cellCloud, Is.EqualTo(0f).Within(0.001f));
            Assert.That(cellVapor, Is.GreaterThan(0.2f));
            Assert.That(localAfter, Is.EqualTo(localBefore).Within(0.15f));
        }

        [UnityTest]
        public IEnumerator EvaporationVentsIntoAtmosphereAbove()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            yield return WaitForHostAndSnapshot();
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
            host.Config.atmosphericCflLimit = 0.85f;
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
                Assert.That(centroidAfter, Is.GreaterThan(centroidBefore + 0.05f));
                Assert.That(peakDx, Is.GreaterThan(0));
                Assert.That(vaporCells, Is.GreaterThan(1));
            });
        }

        [UnityTest]
        public IEnumerator VaporAdvectionWrapsAcrossAngularSeam()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            host.Config.atmosphericCflLimit = 0.85f;
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

            float leftAfter = 0f;
            float vaporAfter = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                vaporAfter = SumVapor(aux);
                for (int x = 0; x < 4; x++)
                    for (int dy = -1; dy <= 1; dy++)
                        leftAfter += aux[(y + dy) * width + x].x;
            });
            Assert.That(vaporAfter, Is.EqualTo(globalBefore + 0.8f).Within(0.1f));
            Assert.That(leftAfter, Is.GreaterThan(leftBefore + 0.02f));
        }

        [UnityTest]
        public IEnumerator WarmHumidAirProducesOutwardBuoyancy()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Air);
            yield return Step(host, 1);

            // Same-altitude contrast: warm/moist center vs cool/dry neighbors.
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0) continue;
                PaintField(host, x + dx, y, 1f, -20f);
            }
            PaintField(host, x, y, 1f, 40f);
            PaintField(host, x, y, 6f, 0.8f);
            yield return Step(host, 8);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[y * width + x].y, Is.GreaterThan(0.01f));
            });
        }

        [UnityTest]
        public IEnumerator CoolDryAirProducesDowndraft()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Air);
            yield return Step(host, 1);

            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0) continue;
                PaintField(host, x + dx, y, 1f, 30f);
                PaintField(host, x + dx, y, 6f, 0.4f);
            }
            PaintField(host, x, y, 1f, -40f);
            yield return Step(host, 8);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[y * width + x].y, Is.LessThan(-0.01f));
            });
        }

        [UnityTest]
        public IEnumerator SurfaceHeatingCreatesUpdraft()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            host.Config.humidityBuoyancy = 0f;
            host.Config.surfaceAirHeatExchange = 2f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            for (int dx = -3; dx <= 3; dx++)
            {
                Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                Paint(host, x + dx, airY, MaterialIds.Air);
                Paint(host, x + dx, airY + 1, MaterialIds.Air);
                Paint(host, x + dx, airY + 2, MaterialIds.Rock);
            }
            yield return Step(host, 1);

            for (int dx = -3; dx <= 3; dx++)
                PaintField(host, x + dx, airY, 1f, -15f);
            PaintField(host, x, surfaceY, 1f, 80f);
            yield return Step(host, 12);

            yield return ReadFields(host, (_, states, __, flow) =>
            {
                Assert.That(states[airY * width + x].x, Is.GreaterThan(states[airY * width + (x + 2)].x + 1f));
                Assert.That(flow[airY * width + x].y, Is.GreaterThan(0.01f));
            });
        }

        [UnityTest]
        public IEnumerator CflLimitKeepsTransportStable()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 8f;
            host.Config.atmosphericAdvectionRate = 8f;
            host.Config.temperatureAdvectionRate = 8f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.atmosphericCflLimit = 0.4f;
            host.Config.saturationCapacityScale = 2f;
            host.Config.pressureDiffusionRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = AtmosphereY(host);
            int x0 = width / 2;
            PaintAirChamber(host, x0 - 12, x0 + 12, y - 1, y + 1);
            yield return Step(host, 1);

            PaintField(host, x0, y, 6f, 0.8f);
            for (int dx = -6; dx <= -1; dx++)
                PaintField(host, x0 + dx, y, 3f, 4f);
            for (int dx = 1; dx <= 6; dx++)
                PaintField(host, x0 + dx, y, 3f, -4f);
            yield return Step(host, 1);

            float vaporBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) => vaporBefore = SumVapor(aux));

            yield return Step(host, 40);

            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                Assert.That(IsFinite(states, aux, flow), Is.True);
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                Assert.That(SumVapor(aux), Is.EqualTo(vaporBefore).Within(0.08f));
                for (int i = 0; i < flow.Length; i++)
                {
                    Assert.That(Mathf.Abs(flow[i].x), Is.LessThanOrEqualTo(20.01f));
                    Assert.That(Mathf.Abs(flow[i].y), Is.LessThanOrEqualTo(20.01f));
                }
            });
        }

        [UnityTest]
        public IEnumerator HeatFollowsUpdraft()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 3f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.temperatureAdvectionRate = 3f;
            host.Config.atmosphericCflLimit = 0.8f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y0 = AtmosphereY(host) - 2;
            PaintAirChamber(host, x - 2, x + 2, y0, y0 + 4);
            yield return Step(host, 1);

            for (int dx = -2; dx <= 2; dx++)
                for (int dy = 0; dy <= 4; dy++)
                    PaintField(host, x + dx, y0 + dy, 1f, -20f);
            PaintField(host, x, y0, 1f, 70f);
            yield return Step(host, 8);

            float upperBefore = 0f;
            float radialFlow = 0f;
            yield return ReadFields(host, (_, states, __, flow) =>
            {
                upperBefore = states[(y0 + 3) * width + x].x;
                radialFlow = flow[y0 * width + x].y;
            });
            Assert.That(radialFlow, Is.GreaterThan(0.01f));

            yield return Step(host, 30);

            float upperAfter = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                upperAfter = states[(y0 + 3) * width + x].x;
            });
            Assert.That(upperAfter, Is.GreaterThan(upperBefore + 1f));
        }

        [UnityTest]
        public IEnumerator VaporRisesSeveralCellsWithLowDiffusion()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 2.5f;
            host.Config.vaporDiffusionRate = 0.01f;
            host.Config.atmosphericBuoyancy = 3f;
            host.Config.humidityBuoyancy = 2f;
            host.Config.atmosphericCflLimit = 0.7f;
            host.Config.saturationCapacityScale = 2f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y0 = AtmosphereY(host) - 3;
            PaintAirChamber(host, x - 2, x + 2, y0, y0 + 6);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dy = 0; dy <= 6; dy++)
                {
                    float vapor = aux[(y0 + dy) * width + x].x;
                    if (vapor > 0f) PaintField(host, x, y0 + dy, 6f, -vapor);
                }
            });
            yield return Step(host, 1);

            PaintField(host, x, y0, 6f, 0.9f);
            PaintField(host, x, y0, 1f, 50f);
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0) continue;
                PaintField(host, x + dx, y0, 1f, -20f);
            }
            yield return Step(host, 45);

            float highBand = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dy = 3; dy <= 6; dy++)
                    highBand += aux[(y0 + dy) * width + x].x;
            });
            Assert.That(highBand, Is.GreaterThan(0.05f), "Vapor should loft several radial cells without relying on diffusion.");
        }

        [UnityTest]
        public IEnumerator PressureGradientCreatesReturnFlow()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 2f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0.2f;
            host.Config.pressureCompressibility = 0.8f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            PaintAirChamber(host, x - 6, x + 6, y - 1, y + 1);
            yield return Step(host, 1);

            for (int dx = -6; dx <= 6; dx++)
            {
                PaintField(host, x + dx, y, 3f, 0.05f);
                // Seed an updraft column on the left half.
                if (dx < 0)
                    PaintField(host, x + dx, y, 3f, -0.8f);
            }
            // Kick an initial outward wind on the left so continuity builds a low.
            host.Config.atmosphericBuoyancy = 0f;
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                for (int dx = -4; dx <= -1; dx++)
                    PaintField(host, x + dx, y, 3f, -1.0f);
                for (int dx = 1; dx <= 4; dx++)
                    PaintField(host, x + dx, y, 3f, 1.0f);
            });
            yield return Step(host, 12);

            yield return ReadFields(host, (_, states, __, flow) =>
            {
                float leftP = states[y * width + (x - 3)].y;
                float rightP = states[y * width + (x + 3)].y;
                Assert.That(Mathf.Abs(leftP - rightP), Is.GreaterThan(0.01f));
                // Return flow should oppose the seeded high→low gradient on at least one side.
                Assert.That(flow[y * width + (x + 2)].x < -0.005f || flow[y * width + (x - 2)].x > 0.005f, Is.True,
                    $"Expected return flow. leftP={leftP} rightP={rightP} flowL={flow[y * width + (x - 2)].x} flowR={flow[y * width + (x + 2)].x}");
            });
        }

        [UnityTest]
        public IEnumerator CloudCondensateAdvectsWithWind()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            host.Config.cloudPrecipitationThreshold = 2f;
            host.Config.atmosphericCflLimit = 0.85f;
            host.Config.pressureDiffusionRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = AtmosphereY(host);
            int x0 = width / 2;
            PaintAirChamber(host, x0 - 16, x0 + 16, y, y);
            yield return Step(host, 1);

            PaintField(host, x0, y, 2f, 0.6f);
            for (int dx = -6; dx <= -1; dx++)
                PaintField(host, x0 + dx, y, 3f, 0.4f);
            for (int dx = 1; dx <= 6; dx++)
                PaintField(host, x0 + dx, y, 3f, -0.4f);
            yield return Step(host, 1);

            float centroidBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                float mass = 0f;
                float moment = 0f;
                for (int dx = -16; dx <= 16; dx++)
                {
                    float c = states[y * width + ((x0 + dx + width) % width)].z;
                    mass += c;
                    moment += c * dx;
                }
                centroidBefore = mass > 1e-5f ? moment / mass : 0f;
                Assert.That(mass, Is.GreaterThan(0.3f));
            });

            yield return Step(host, 40);

            yield return ReadFields(host, (_, states, __, ___) =>
            {
                float mass = 0f;
                float moment = 0f;
                for (int dx = -16; dx <= 16; dx++)
                {
                    float c = states[y * width + ((x0 + dx + width) % width)].z;
                    mass += c;
                    moment += c * dx;
                }
                float centroidAfter = mass > 1e-5f ? moment / mass : 0f;
                Assert.That(centroidAfter, Is.GreaterThan(centroidBefore + 0.05f));
            });
        }

        [UnityTest]
        public IEnumerator GlobalMeanRadialDriftNearZero()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            yield return Step(host, 80);

            double meanRadial = 0d;
            int airCount = 0;
            yield return ReadFields(host, (mats, _, __, flow) =>
            {
                double sum = 0d;
                int count = 0;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != MaterialIds.Air) continue;
                    sum += flow[i].y;
                    count++;
                }
                meanRadial = count > 0 ? sum / count : 0d;
                airCount = count;
            });
            Assert.That(airCount, Is.GreaterThan(10));
            Assert.That(Math.Abs(meanRadial), Is.LessThan(0.12));
        }

        [UnityTest]
        public IEnumerator ColdAtmosphereCondensesExcessVaporToCloud()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
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
            yield return WaitForHostAndSnapshot();
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
            yield return WaitForHostAndSnapshot();
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
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Config.vaporDiffusionRate = 0.05f;
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
            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                waterAfter = SumWater(states, aux);
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                Assert.That(IsFinite(states, aux, flow), Is.True);
            });

            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;

            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
            Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Math.Max(1d, waterBefore * 0.05d)));
        }

        [UnityTest]
        public IEnumerator ValidationPresetEmergesPairedCirculation()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.validationIntervalTicks = 100000;
            host.Config.seed = 4242;
            host.Config.targetOceanCoverage = 0.55f;
            host.Config.dayLengthSeconds = 60f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            float vaporVarianceBefore = 0f;
            float waterBefore = 0f;
            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                waterBefore = SumWater(states, aux);
                var before = SimulationMetrics.ComputeAtmosphericMetrics(
                    host.Grid, mats, states, aux, flow, 0f);
                vaporVarianceBefore = before.VaporAltitudeVariance;
            });

            int dayTicks = Mathf.Max(120, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds * 0.4f));
            yield return Step(host, dayTicks);

            AtmosphericCirculationMetrics metrics = default;
            float waterAfter = 0f;
            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                waterAfter = SumWater(states, aux);
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                Assert.That(IsFinite(states, aux, flow), Is.True);
                float solar = 0f;
                // Approximate solar angle from tick count used above.
                solar = (dayTicks / Mathf.Max(1f, host.Config.ticksPerSecond * host.Config.dayLengthSeconds)) % 1f;
                metrics = SimulationMetrics.ComputeAtmosphericMetrics(host.Grid, mats, states, aux, flow, solar);
            });

            Assert.That(metrics.UpdraftCells, Is.GreaterThan(0));
            Assert.That(metrics.DowndraftCells, Is.GreaterThan(0));
            Assert.That(metrics.VaporAltitudeVariance, Is.GreaterThan(vaporVarianceBefore * 0.5f + 1e-6f));
            Assert.That(metrics.PressureAnomalyRms, Is.GreaterThan(0.001f));
            Assert.That(metrics.CirculationEnergy, Is.GreaterThan(1e-5f));
            Assert.That(metrics.UpdraftCells, Is.GreaterThan(metrics.DowndraftCells / 4));
            Assert.That(metrics.DowndraftCells, Is.GreaterThan(metrics.UpdraftCells / 4));
            Assert.That(Mathf.Abs(metrics.SignedMeanVerticalDrift), Is.LessThan(metrics.RmsVerticalFlow * 0.85f + 0.05f));
            Assert.That(Mathf.Abs(metrics.DayNightSurfaceTemperatureDelta), Is.GreaterThan(0.05f));
            Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Math.Max(1d, waterBefore * 0.08d)));
        }

        private sealed class SimulationConfigSnapshot
        {
            private readonly FieldInfo[] fields;
            private readonly object[] values;

            private SimulationConfigSnapshot(FieldInfo[] fields, object[] values)
            {
                this.fields = fields;
                this.values = values;
            }

            public static SimulationConfigSnapshot Capture(SimulationConfig config)
            {
                FieldInfo[] fields = typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public);
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    values[i] = fields[i].GetValue(config);
                return new SimulationConfigSnapshot(fields, values);
            }

            public void Restore(SimulationConfig config)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetValue(config, values[i]);
            }
        }
    }
}
