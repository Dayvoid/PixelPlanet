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
            host.Config.terrainRadiativeCooling = 0.2f;
            host.Config.atmosphereRadiativeCooling = 0.2f;
            host.Config.windStrength = 0.35f;
            host.Config.windDamping = 0.06f;
            host.Config.coriolisStrength = 0f;
            host.Config.velocityAdvectionRate = 0f;
            host.Config.prevailingWind = 0f;
            host.Config.evaporationRate = 0.1f;
            host.Config.condensationRate = 0.12f;
            host.Config.dewRate = 0.12f;
            host.Config.precipitationRate = 0.2f;
            host.Config.vaporPressureScale = 0.25f;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Config.vaporDiffusionRate = 0.05f;
            host.Config.atmosphericBuoyancy = 0.4f;
            host.Config.verticalBuoyancyStrength = 1f;
            host.Config.vaporCapacityScale = 0.55f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.waterPressureResponse = 0.6f;
            host.Config.latentHeatScale = 0.35f;
            host.Config.surfaceAirHeatExchange = 0.45f;
            host.Config.temperatureAdvectionRate = 0.55f;
            host.Config.pressureCompressibility = 0.45f;
            host.Config.atmosphericCflLimit = 0.4f;
            host.Config.surfaceAirTemperature = 18f;
            host.Config.atmosphericLapseRate = 12f;
            host.Config.frontalLiftStrength = 0f;
            host.Config.frontalCollisionPressure = 0f;
            host.Config.frontalDensityDrive = 0f;
            host.Config.frontalSubsidenceScale = 0f;
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
            if (materialId == MaterialIds.Water || materialId == MaterialIds.Ice)
                return;
            if (materialId == MaterialIds.Air || materialId == MaterialIds.Void)
                return;
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 6f, -100f);
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
            host.Config.transportPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.climateLayerEnable = false;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.windDamping = 0f;
            host.Config.coriolisStrength = 0f;
            host.Config.velocityAdvectionRate = 0f;
            host.Config.prevailingWind = 0f;
            host.Config.frontalLiftStrength = 0f;
            host.Config.frontalCollisionPressure = 0f;
            host.Config.frontalDensityDrive = 0f;
            host.Config.frontalSubsidenceScale = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.latentHeatScale = 0f;
            host.Config.waterPressureResponse = 0f;
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

        private static float ColumnSurfaceWater(uint[] mats, Vector4[] states, int width, int x, int bedY)
        {
            int height = mats.Length / Math.Max(1, width);
            float water = 0f;
            uint bed = mats[bedY * width + x];
            if (bed != MaterialIds.Water && bed != MaterialIds.Ice)
                water += Math.Max(0f, states[bedY * width + x].z);
            for (int y = bedY + 1; y < height; y++)
            {
                uint id = mats[y * width + x];
                if (id != MaterialIds.Water && id != MaterialIds.Ice)
                    break;
                water += Math.Max(0f, states[y * width + x].z);
            }
            return water;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.phaseHysteresis = 0.01f;
            host.Config.thermalRate = 1f;
            host.Config.waterPressureResponse = 0f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 1f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.vaporCapacityScale = 2f;
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
            host.Config.evaporationRate = 0f;
            PaintField(host, x, surfaceY, 2f, 0.5f);
            PaintField(host, x, surfaceY, 1f, 40f);
            yield return Step(host, 1);
            host.Config.evaporationRate = 1.5f;

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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 3f;
            host.Config.atmosphericAdvectionRate = 3f;
            host.Config.vaporDiffusionRate = 0.25f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.vaporCapacityScale = 2f;
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
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        float v = aux[(y + dy) * width + xx].x;
                        mass += v;
                        moment += v * dx;
                    }
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
                    float v = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                        v += aux[(y + dy) * width + xx].x;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 3f;
            host.Config.atmosphericAdvectionRate = 3f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.vaporCapacityScale = 2f;
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
            for (int x = width - 8; x < width + 8; x++)
            {
                int xx = ((x % width) + width) % width;
                float pressure = 1.2f - (x - (width - 8)) * 0.12f;
                for (int dy = -1; dy <= 1; dy++)
                    PaintField(host, xx, y + dy, 3f, pressure);
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.verticalBuoyancyStrength = 0f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.verticalBuoyancyStrength = 0f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.verticalBuoyancyStrength = 0f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 8f;
            host.Config.atmosphericAdvectionRate = 8f;
            host.Config.temperatureAdvectionRate = 8f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.atmosphericCflLimit = 0.4f;
            host.Config.vaporCapacityScale = 2f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 3f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 2.5f;
            host.Config.vaporDiffusionRate = 0.01f;
            host.Config.atmosphericBuoyancy = 3f;
            host.Config.verticalBuoyancyStrength = 1f;
            host.Config.velocityAdvectionRate = 2f;
            host.Config.atmosphericLapseRate = 0f;
            host.Config.atmosphericCflLimit = 0.7f;
            host.Config.vaporCapacityScale = 2f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y0 = SurfaceY(host) + 2;
            PaintAirChamber(host, x - 2, x + 2, y0, y0 + 6);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dy = 0; dy <= 6; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = host.Grid.WrapTheta(x + dx);
                    float vapor = aux[(y0 + dy) * width + xx].x;
                    if (vapor > 0f) PaintField(host, xx, y0 + dy, 6f, -vapor);
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
                for (int dx = -1; dx <= 1; dx++)
                    highBand += aux[(y0 + dy) * width + host.Grid.WrapTheta(x + dx)].x;
            });
            Assert.That(highBand, Is.GreaterThan(0.05f), "Vapor should loft several radial cells without relying on diffusion.");
        }

        [UnityTest]
        public IEnumerator UniformHotHumidLayerProducesVerticalUpdraft()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 2.5f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.verticalBuoyancyStrength = 1f;
            host.Config.atmosphericLapseRate = 0f;
            host.Config.atmosphericCflLimit = 0.7f;
            host.Config.vaporCapacityScale = 2f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y0 = AtmosphereY(host);
            PaintAirChamber(host, x - 3, x + 3, y0, y0 + 4);
            yield return Step(host, 1);

            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = 0; dy <= 4; dy++)
                    PaintField(host, x + dx, y0 + dy, 1f, -20f);
            }
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    for (int dy = 0; dy <= 4; dy++)
                    {
                        float vapor = aux[(y0 + dy) * width + ((x + dx + width) % width)].x;
                        if (vapor > 0f) PaintField(host, x + dx, y0 + dy, 6f, -vapor);
                    }
                }
            });
            yield return Step(host, 1);

            for (int dx = -3; dx <= 3; dx++)
            {
                PaintField(host, x + dx, y0, 1f, 60f);
                PaintField(host, x + dx, y0, 6f, 0.8f);
            }

            float highBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dy = 1; dy <= 4; dy++)
                    highBefore += aux[(y0 + dy) * width + x].x;
            });

            yield return Step(host, 45);

            float layerFlow = 0f;
            float highAfter = 0f;
            yield return ReadFields(host, (_, __, aux, flow) =>
            {
                layerFlow = flow[y0 * width + x].y;
                for (int dy = 1; dy <= 4; dy++)
                    highAfter += aux[(y0 + dy) * width + x].x;
            });
            Assert.That(layerFlow, Is.GreaterThan(0.01f), "Angularly uniform hot humid air should still produce an updraft.");
            Assert.That(highAfter, Is.GreaterThan(highBefore + 0.02f), "Vapor should migrate upward from a uniform surface steam layer.");
        }

        [UnityTest]
        public IEnumerator LapseConformingColumnStaysNearNeutral()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0.25f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 2f;
            host.Config.verticalBuoyancyStrength = 1f;
            host.Config.atmosphericLapseRate = 12f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y0 = AtmosphereY(host);
            const int height = 4;
            PaintAirChamber(host, x - 2, x + 2, y0, y0 + height);
            yield return Step(host, 1);

            float expectedStep = host.Config.atmosphericLapseRate /
                Mathf.Max(1f, (1f - host.Grid.atmosphereStartRadius) * host.Grid.radialResolution);
            const float baseTemp = 20f;
            yield return ReadFields(host, (_, states, aux, ___) =>
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = (x + dx + width) % width;
                    for (int dy = 0; dy <= height; dy++)
                    {
                        int index = (y0 + dy) * width + xx;
                        float targetTemp = baseTemp - expectedStep * dy;
                        PaintField(host, xx, y0 + dy, 1f, targetTemp - states[index].x);
                        float vapor = aux[index].x;
                        if (vapor > 0f) PaintField(host, xx, y0 + dy, 6f, -vapor);
                    }
                }
            });
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = (x + dx + width) % width;
                    for (int dy = 0; dy <= height; dy++)
                    {
                        int index = (y0 + dy) * width + xx;
                        if (Mathf.Abs(flow[index].y) > 1e-5f)
                            PaintField(host, xx, y0 + dy, 12f, -flow[index].y);
                        PaintField(host, xx, y0 + dy, 13f, 0f);
                    }
                }
            });
            yield return Step(host, 16);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[y0 * width + x].y, Is.EqualTo(0f).Within(0.03f),
                    "A lapse-conforming column should not drive runaway convection.");
                Assert.That(flow[(y0 + 2) * width + x].y, Is.EqualTo(0f).Within(0.03f));
            });
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 2f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 3f;
            host.Config.atmosphericAdvectionRate = 3f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.cloudRetainMass = 2f;
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
            Assert.That(Math.Abs(meanRadial), Is.LessThan(1.25),
                "CFL-capped buoyancy can leave a convective residual during spin-up, but the column must not run away.");
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.vaporCapacityScale = 0.05f;
            host.Config.cloudRetainMass = 1f;
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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.cloudRetainMass = 0.02f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            for (int dx = -4; dx <= 4; dx++)
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
        public IEnumerator RainOnRockShelfPondsLocallyInsteadOfSheetingAway()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0.45f;
            host.Config.pondingRate = 0.85f;
            host.Config.gravityStrength = 1f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = SurfaceY(host);
            const int cloudBase = 6;
            const int cloudTop = 12;
            for (int dx = -10; dx <= 10; dx++)
            {
                int xx = host.Grid.WrapTheta(x + dx);
                Paint(host, xx, bedY - 1, MaterialIds.Rock);
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int y = bedY + 1; y <= bedY + cloudTop + 1; y++)
                    Paint(host, xx, y, MaterialIds.Air);
            }
            yield return Step(host, 1);
            for (int dy = cloudBase; dy <= cloudTop; dy++)
            {
                PaintField(host, x, bedY + dy, 6f, -100f);
                PaintField(host, x, bedY + dy, 2f, -100f);
                PaintField(host, x, bedY + dy, 2f, 0.85f);
            }
            yield return Step(host, 1);

            host.Config.precipitationRate = 1.5f;
            yield return Step(host, 48);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                float local = 0f;
                float far = 0f;
                for (int dx = -2; dx <= 2; dx++)
                    local += ColumnSurfaceWater(mats, states, width, host.Grid.WrapTheta(x + dx), bedY);
                for (int i = 0; i < 2; i++)
                {
                    int side = i == 0 ? -9 : 9;
                    far += ColumnSurfaceWater(mats, states, width, host.Grid.WrapTheta(x + side), bedY);
                }
                Assert.That(local, Is.GreaterThan(0.7f),
                    "Landed rain should remain as a local pond instead of vanishing.");
                Assert.That(local, Is.GreaterThan(far + 0.35f),
                    "Shallow rain must not sheet across a dry shelf in a few dozen ticks.");
            });
        }

        [UnityTest]
        public IEnumerator FallingRainSpreadsAcrossNeighborColumns()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0.8f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 1f;
            host.Config.gravityStrength = 1f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = SurfaceY(host);
            const int fall = 14;
            for (int dx = -4; dx <= 4; dx++)
            {
                int xx = host.Grid.WrapTheta(x + dx);
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int y = bedY + 1; y <= bedY + fall + 2; y++)
                    Paint(host, xx, y, MaterialIds.Air);
            }
            yield return Step(host, 1);
            for (int dy = fall - 2; dy <= fall; dy++)
            {
                PaintField(host, x, bedY + dy, 6f, -100f);
                PaintField(host, x, bedY + dy, 2f, -100f);
                PaintField(host, x, bedY + dy, 2f, 0.9f);
            }
            yield return Step(host, 1);

            host.Config.precipitationRate = 2f;
            yield return Step(host, 36);

            yield return ReadFields(host, (mats, states, _, __) =>
            {
                int wetColumns = 0;
                for (int dx = -3; dx <= 3; dx++)
                {
                    int xx = host.Grid.WrapTheta(x + dx);
                    if (ColumnSurfaceWater(mats, states, width, xx, bedY) > 0.12f)
                        wetColumns++;
                }
                Assert.That(wetColumns, Is.GreaterThanOrEqualTo(2),
                    "Wind-driven rain should slant into neighboring columns instead of a single-file shaft.");
            });
        }

        [UnityTest]
        public IEnumerator DefaultRetainDeckRainsPixelsAtCloudBaseNotSurfaceFilm()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.cloudRetainMass = 0.9f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 1f;
            host.Config.gravityStrength = 0f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = SurfaceY(host);
            const int fringeHeight = 4;
            const int deckCells = 3;
            int fringeY = bedY + fringeHeight;
            int deckBottom = fringeY + 1;
            int deckTop = deckBottom + deckCells - 1;
            for (int dx = -3; dx <= 3; dx++)
            {
                int xx = host.Grid.WrapTheta(x + dx);
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int y = bedY + 1; y <= deckTop + 1; y++)
                    Paint(host, xx, y, MaterialIds.Air);
            }
            yield return Step(host, 1);
            yield return ReadFields(host, (_, states, aux, __) =>
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int xx = host.Grid.WrapTheta(x + dx);
                    for (int y = bedY + 1; y <= deckTop + 1; y++)
                    {
                        if (aux[y * width + xx].x > 0f) PaintField(host, xx, y, 6f, -aux[y * width + xx].x);
                        if (states[y * width + xx].z > 0f) PaintField(host, xx, y, 2f, -states[y * width + xx].z);
                    }
                }
            });
            yield return Step(host, 1);
            for (int y = deckBottom; y <= deckTop; y++)
            {
                PaintField(host, x, y, 1f, 12f);
                PaintField(host, x, y, 2f, 1.4f);
            }
            PaintField(host, x, fringeY, 1f, 12f);
            PaintField(host, x, fringeY, 2f, 0.3f);
            yield return Step(host, 1);

            host.Config.precipitationRate = 2f;
            int formedY = -1;
            for (int i = 0; i < 12 && formedY < 0; i++)
            {
                yield return Step(host, 1);
                yield return ReadFields(host, (mats, states, _, __) =>
                {
                    for (int y = deckBottom; y <= deckTop; y++)
                    {
                        uint id = mats[y * width + x];
                        Assert.That(id, Is.EqualTo(MaterialIds.Air),
                            "Deck interior must stay Air; cloud should not freeze in place.");
                    }
                    Assert.That(states[bedY * width + x].z, Is.LessThan(0.05f),
                        "Surface film must stay empty until a rain pixel lands.");
                    for (int y = bedY + 1; y <= fringeY; y++)
                    {
                        uint id = mats[y * width + x];
                        if (id == MaterialIds.Water || id == MaterialIds.Ice)
                        {
                            formedY = y;
                            break;
                        }
                    }
                });
            }
            Assert.That(formedY, Is.GreaterThan(bedY),
                "Default-retain deck should materialize a Water/Ice pixel at the cloud-base fringe, not drip as surface film.");
            Assert.That(formedY, Is.LessThan(deckBottom),
                "The drop must form under the deck, not inside it.");
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
            host.Config.dewRate = 0.2f;
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
            host.Config.floraGrowthRate = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.treeWaterUptakeRate = 0f;
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
            host.Config.useOgWorldgen = true;
            host.Config.seed = 4242;
            host.Config.targetOceanCoverage = 0.55f;
            host.Config.dayLengthSeconds = 60f;
            host.Config.floraGrowthRate = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.treeWaterUptakeRate = 0f;
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

        [UnityTest]
        public IEnumerator DenseColdCloudBecomesFallingWaterOrIcePixel()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 2f;
            host.Config.dewRate = 2f;
            host.Config.precipitationRate = 1f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.vaporCapacityScale = 0.05f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int cloudY = AtmosphereY(host);
            int floorY = cloudY - 4;
            PaintAirChamber(host, x - 1, x + 1, floorY + 1, cloudY);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                for (int dy = 0; dy <= 4; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    float vapor = aux[(floorY + dy) * width + (x + dx)].x;
                    if (vapor > 0f) PaintField(host, x + dx, floorY + dy, 6f, -vapor);
                }
            });
            yield return Step(host, 1);

            PaintField(host, x, cloudY, 1f, 40f);
            PaintField(host, x, cloudY, 2f, 0.55f);
            PaintField(host, x, cloudY, 6f, 0.4f);
            yield return Step(host, 1);
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;

            float waterBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) => waterBefore = SumWaterBox(states, aux, width, x - 2, x + 2, floorY, cloudY + 1));

            int formedY = -1;
            uint formedId = 0;
            for (int i = 0; i < 12 && formedY < 0; i++)
            {
                yield return Step(host, 1);
                yield return ReadFields(host, (mats, states, aux, _) =>
                {
                    Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
                    for (int dx = -1; dx <= 1 && formedY < 0; dx++)
                    {
                        int xx = host.Grid.WrapTheta(x + dx);
                        for (int y = floorY + 1; y <= cloudY; y++)
                        {
                            uint id = mats[y * width + xx];
                            if (id == MaterialIds.Water || id == MaterialIds.Ice)
                            {
                                formedY = y;
                                formedId = id;
                                break;
                            }
                        }
                    }
                    float waterAfter = SumWaterBox(states, aux, width, x - 2, x + 2, floorY, cloudY + 1);
                    Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(0.2f));
                });
            }
            Assert.That(formedY, Is.GreaterThan(0), "Dense cloud should materialize into a Water or Ice pixel.");
            Assert.That(formedId, Is.EqualTo(MaterialIds.Water), "Warm condensate should become Water so existing material gravity can move it.");

            host.Config.precipitationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.gravityStrength = 1f;
            yield return Step(host, 8);

            yield return ReadFields(host, (mats, _, __, ___) =>
            {
                int laterY = -1;
                for (int dx = -1; dx <= 1 && laterY < 0; dx++)
                {
                    int xx = host.Grid.WrapTheta(x + dx);
                    for (int y = floorY; y <= cloudY; y++)
                    {
                        uint id = mats[y * width + xx];
                        if (id == MaterialIds.Water || id == MaterialIds.Ice)
                        {
                            laterY = y;
                            break;
                        }
                    }
                }
                Assert.That(laterY, Is.GreaterThan(0));
                Assert.That(laterY, Is.LessThan(formedY), "Materialized rain should fall under gravity.");
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator PrecipitationDisabledKeepsCloudAsAirField()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            PaintAirChamber(host, x - 1, x + 1, y, y);
            yield return Step(host, 1);
            PaintField(host, x, y, 2f, 0.8f);
            yield return Step(host, 12);

            yield return ReadFields(host, (mats, states, _, __) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Air),
                    "Disabled precipitation must keep cloud as an Air field.");
                Assert.That(states[y * width + x].z, Is.GreaterThan(0.4f));
                Assert.That(CountMaterial(mats, MaterialIds.Vapor), Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator DenseColdCloudBecomesIcePixel()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 1f;
            host.Config.gravityStrength = 0f;
            host.Config.vaporCapacityScale = 0.05f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int cloudY = AtmosphereY(host);
            int floorY = cloudY - 3;
            PaintAirChamber(host, x - 1, x + 1, floorY + 1, cloudY);
            yield return Step(host, 1);
            PaintField(host, x, cloudY, 1f, -80f);
            PaintField(host, x, cloudY, 2f, 0.55f);
            yield return Step(host, 1);

            uint formedId = 0;
            for (int i = 0; i < 12 && formedId == 0; i++)
            {
                yield return Step(host, 1);
                yield return ReadFields(host, (mats, _, __, ___) =>
                {
                    for (int dx = -1; dx <= 1 && formedId == 0; dx++)
                    {
                        int xx = host.Grid.WrapTheta(x + dx);
                        for (int y = floorY + 1; y <= cloudY; y++)
                        {
                            uint id = mats[y * width + xx];
                            if (id == MaterialIds.Water || id == MaterialIds.Ice)
                            {
                                formedId = id;
                                break;
                            }
                        }
                    }
                });
            }
            Assert.That(formedId, Is.EqualTo(MaterialIds.Ice), "Cold condensate should become Ice.");
        }

        [UnityTest]
        public IEnumerator PrecipitationRescuesVaporFromTheDropCell()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.vaporCapacityScale = 2f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int cloudY = AtmosphereY(host);
            int dropY = cloudY - 1;
            PaintAirChamber(host, x, x, dropY, cloudY);
            yield return Step(host, 1);
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                if (aux[cloudY * width + x].x > 0f) PaintField(host, x, cloudY, 6f, -aux[cloudY * width + x].x);
                if (aux[dropY * width + x].x > 0f) PaintField(host, x, dropY, 6f, -aux[dropY * width + x].x);
            });
            yield return Step(host, 1);
            PaintField(host, x, cloudY, 1f, 30f);
            PaintField(host, x, cloudY, 2f, 0.6f);
            PaintField(host, x, dropY, 6f, 0.45f);
            yield return Step(host, 1);

            float vaporBefore = 0f;
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                vaporBefore = aux[cloudY * width + x].x + aux[dropY * width + x].x;
                Assert.That(aux[dropY * width + x].x, Is.GreaterThan(0.3f));
            });

            host.Config.precipitationRate = 1f;

            bool formed = false;
            for (int i = 0; i < 8 && !formed; i++)
            {
                yield return Step(host, 1);
                yield return ReadFields(host, (mats, _, aux, __) =>
                {
                    formed = mats[dropY * width + x] == MaterialIds.Water || mats[dropY * width + x] == MaterialIds.Ice;
                    float vaporAfter = aux[cloudY * width + x].x + aux[dropY * width + x].x;
                    Assert.That(vaporAfter, Is.EqualTo(vaporBefore).Within(0.08f));
                    if (formed)
                    {
                        // Destination vapor stays on the drop; the next transport pass
                        // may vent it into the cloud cell without a many-writer copy.
                        Assert.That(aux[dropY * width + x].x, Is.GreaterThan(0.3f));
                    }
                });
            }
            Assert.That(formed, Is.True, "Cloud-base precipitation should form a drop in the cell below.");

            host.Config.atmosphericAdvectionRate = 0.85f;
            yield return Step(host, 2);
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                float vaporAfterVent = aux[cloudY * width + x].x + aux[dropY * width + x].x;
                Assert.That(vaporAfterVent, Is.EqualTo(vaporBefore).Within(0.08f));
            });
        }

        [UnityTest]
        public IEnumerator ConvergingPrecipitationConservesMassAndMixesTemperature()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.vaporCapacityScale = 2f;
            host.Config.cloudRetainMass = 0.05f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int cloudY = AtmosphereY(host);
            int dropY = cloudY - 1;
            float[] sourceTemps = { 8f, 28f, 72f };

            for (int sourceCount = 2; sourceCount <= 3; sourceCount++)
            {
                host.Regenerate();
                for (int i = 0; i < 5; i++) yield return null;
                PaintAirChamber(host, x - 2, x + 2, dropY, cloudY);
                // Airborne water is an invalid precipitation receiver, so every
                // source must fan into the single open Air cell at (x, dropY).
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0) continue;
                    Paint(host, x + dx, dropY - 1, MaterialIds.Air);
                    Paint(host, x + dx, dropY, MaterialIds.Water);
                    PaintField(host, x + dx, dropY, 2f, -100f);
                    PaintField(host, x + dx, dropY, 2f, 1f);
                }
                yield return Step(host, 1);
                yield return ReadFields(host, (_, states, aux, __) =>
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = host.Grid.WrapTheta(x + dx);
                        if (aux[cloudY * width + xx].x > 0f) PaintField(host, xx, cloudY, 6f, -aux[cloudY * width + xx].x);
                        if (aux[dropY * width + xx].x > 0f) PaintField(host, xx, dropY, 6f, -aux[dropY * width + xx].x);
                        if (states[cloudY * width + xx].z > 0f) PaintField(host, xx, cloudY, 2f, -states[cloudY * width + xx].z);
                        if (states[dropY * width + xx].z > 0f) PaintField(host, xx, dropY, 2f, -states[dropY * width + xx].z);
                    }
                });
                yield return Step(host, 1);

                int[] sourceXs = sourceCount == 2
                    ? new[] { x, x - 1 }
                    : new[] { x - 1, x, x + 1 };
                for (int i = 0; i < sourceXs.Length; i++)
                {
                    PaintField(host, sourceXs[i], cloudY, 1f, sourceTemps[i]);
                    PaintField(host, sourceXs[i], cloudY, 2f, 0.7f);
                }
                PaintField(host, x, dropY, 1f, 18f);
                PaintField(host, x, dropY, 6f, 0.4f);
                yield return Step(host, 1);

                float waterBefore = 0f;
                float destVaporBefore = 0f;
                float destTemp = 0f;
                yield return ReadFields(host, (_, states, aux, __) =>
                {
                    waterBefore = SumWater(states, aux);
                    destVaporBefore = aux[dropY * width + x].x;
                    destTemp = states[dropY * width + x].x;
                    Assert.That(destVaporBefore, Is.GreaterThan(0.3f));
                    for (int i = 0; i < sourceXs.Length; i++)
                        Assert.That(aux[cloudY * width + sourceXs[i]].x, Is.LessThan(0.05f));
                });

                host.Config.precipitationRate = 1f;
                bool formed = false;
                float dropTemp = 0f;
                for (int i = 0; i < 16 && !formed; i++)
                {
                    yield return Step(host, 1);
                    yield return ReadFields(host, (mats, states, aux, _) =>
                    {
                        formed = mats[dropY * width + x] == MaterialIds.Water || mats[dropY * width + x] == MaterialIds.Ice;
                        float waterAfter = SumWater(states, aux);
                        float destVapor = aux[dropY * width + x].x;
                        float cloudVapor = 0f;
                        for (int s = 0; s < sourceXs.Length; s++)
                            cloudVapor += Math.Max(0f, aux[cloudY * width + sourceXs[s]].x);
                        Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Mathf.Max(0.25f, waterBefore * 0.02f)),
                            $"{sourceCount}-source precipitation must conserve tracked water.");
                        Assert.That(cloudVapor, Is.LessThan(0.16f),
                            $"{sourceCount}-source precipitation must not copy destination vapor onto the clouds.");
                        if (formed)
                        {
                            dropTemp = states[dropY * width + x].x;
                            Assert.That(destVapor, Is.EqualTo(destVaporBefore).Within(0.12f));
                        }
                    });
                }
                Assert.That(formed, Is.True, $"{sourceCount} converging clouds should form one drop.");

                float tempMin = destTemp;
                float tempMax = destTemp;
                for (int i = 0; i < sourceCount; i++)
                {
                    tempMin = Mathf.Min(tempMin, sourceTemps[i]);
                    tempMax = Mathf.Max(tempMax, sourceTemps[i]);
                }
                Assert.That(dropTemp, Is.InRange(tempMin - 1f, tempMax + 1f));
                host.Config.precipitationRate = 0f;
            }
        }

        [UnityTest]
        public IEnumerator ValleyFloorBlockedFlowDoesNotRatchetPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.pressureCompressibility = 0.8f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int floorY = AtmosphereY(host) - 3;
            int airY = floorY + 1;
            PaintAirChamber(host, x - 1, x + 1, airY, airY + 2);
            yield return Step(host, 1);
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                for (int dx = -1; dx <= 1; dx++)
                for (int y = airY; y <= airY + 2; y++)
                {
                    int xx = host.Grid.WrapTheta(x + dx);
                    float pressure = states[y * width + xx].y;
                    if (pressure > 0f) PaintField(host, xx, y, 3f, -pressure);
                }
            });
            yield return Step(host, 1);
            const float seedPressure = 1f;
            PaintField(host, x, airY, 3f, seedPressure);
            yield return Step(host, 1);

            float pressureBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) => pressureBefore = states[airY * width + x].y);
            Assert.That(pressureBefore, Is.GreaterThan(0.2f));

            float previous = pressureBefore;
            int risingStreak = 0;
            float peak = pressureBefore;
            for (int i = 0; i < 24; i++)
            {
                PaintField(host, x, airY, 12f, -3f);
                yield return Step(host, 1);
                float pressure = 0f;
                yield return ReadFields(host, (_, states, aux, flow) =>
                {
                    pressure = states[airY * width + x].y;
                    Assert.That(IsFinite(states, aux, flow), Is.True);
                });
                if (pressure > previous + 0.002f) risingStreak++;
                else risingStreak = 0;
                Assert.That(risingStreak, Is.LessThan(6),
                    $"Valley-floor pressure rose for {risingStreak} ticks (now {pressure:F3}).");
                peak = Mathf.Max(peak, pressure);
                previous = pressure;
            }
            Assert.That(peak, Is.LessThan(pressureBefore + 0.35f),
                "A blocked floor should impart at most a bounded impulse, not a pressure ratchet.");
        }

        [UnityTest]
        public IEnumerator BoilingMassIsInvariantAcrossMaterialSubsteps()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();

            float water1 = 0f;
            float vapor1 = 0f;
            float pressure1 = 0f;
            yield return RunBoilingSubstepCase(host, 1, (water, vapor, pressure) =>
            {
                water1 = water;
                vapor1 = vapor;
                pressure1 = pressure;
            });

            float water4 = 0f;
            float vapor4 = 0f;
            float pressure4 = 0f;
            yield return RunBoilingSubstepCase(host, 4, (water, vapor, pressure) =>
            {
                water4 = water;
                vapor4 = vapor;
                pressure4 = pressure;
            });

            Assert.That(water4, Is.EqualTo(water1).Within(0.08f));
            Assert.That(vapor4, Is.EqualTo(vapor1).Within(0.08f));
            Assert.That(pressure4, Is.EqualTo(pressure1).Within(0.08f));
        }

        private IEnumerator RunBoilingSubstepCase(SimulationHost host, int substeps, Action<float, float, float> consume)
        {
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.phaseHysteresis = 0.01f;
            host.Config.thermalRate = 1f;
            host.Config.waterPressureResponse = 0f;
            host.Config.materialSubsteps = substeps;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = SurfaceY(host);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Rock);
            }
            Paint(host, x, y, MaterialIds.Water);
            PaintField(host, x, y, 2f, 0.55f);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                    PaintField(host, x + dx, y + dy, 1f, 140f);
            }
            yield return Step(host, 6);

            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Air));
                consume(
                    SumWaterBox(states, aux, width, x - 2, x + 2, y - 2, y + 2),
                    aux[y * width + x].x,
                    states[y * width + x].y);
            });
            host.Config.materialSubsteps = 1;
        }

        [UnityTest]
        public IEnumerator HighPressureRaisesBoilingPoint()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.thermalRate = 1f;
            host.Config.waterPressureResponse = 1f;
            host.Config.latentHeatScale = 0.35f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            PaintAirChamber(host, x, x, y, y);
            yield return Step(host, 1);
            Paint(host, x, y, MaterialIds.Water);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 2f, 1f);
            PaintField(host, x, y, 3f, 8f);
            PaintField(host, x, y, 1f, 115f);
            PaintField(host, x, y, 6f, -100f);
            yield return Step(host, 4);

            yield return ReadFields(host, (mats, _, aux, __) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Water),
                    "High pressure should keep 115 C water from boiling.");
                Assert.That(aux[y * width + x].x, Is.LessThan(0.2f));
            });

            PaintField(host, x, y, 3f, -100f);
            yield return Step(host, 6);
            yield return ReadFields(host, (mats, _, aux, __) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Air),
                    "Relieved pressure should let superheated water boil to Air.");
                Assert.That(aux[y * width + x].x, Is.GreaterThan(0.5f));
            });
        }

        [UnityTest]
        public IEnumerator CondensationReleasesLatentHeat()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 2f;
            host.Config.dewRate = 2f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporCapacityScale = 0.05f;
            host.Config.cloudRetainMass = 1f;
            host.Config.latentHeatScale = 2f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            PaintAirChamber(host, x, x, y, y);
            yield return Step(host, 1);
            yield return ReadFields(host, (_, __, aux, ___) =>
            {
                if (aux[y * width + x].x > 0f) PaintField(host, x, y, 6f, -aux[y * width + x].x);
            });
            yield return Step(host, 1);
            PaintField(host, x, y, 1f, -30f);
            PaintField(host, x, y, 6f, 0.9f);
            yield return Step(host, 1);

            float tempBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) => tempBefore = states[y * width + x].x);

            yield return Step(host, 16);
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Air));
                Assert.That(aux[y * width + x].x, Is.LessThan(0.7f));
                Assert.That(states[y * width + x].z, Is.GreaterThan(0.1f));
                Assert.That(states[y * width + x].x, Is.GreaterThan(tempBefore + 0.2f));
            });
        }


        [UnityTest]
        public IEnumerator CoriolisDeflectsVerticalUpdraftIntoHorizontalWind()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.coriolisStrength = 1.5f;
            host.Config.velocityAdvectionRate = 0f;
            host.Config.prevailingWind = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);

            PaintField(host, x, y, 12f, 1.5f);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[y * width + x].x, Is.LessThan(-0.01f),
                    "Coriolis acceleration must deflect positive radial updraft into negative angular wind.");
            });
        }

        [UnityTest]
        public IEnumerator PrevailingWindAcceleratesAtmosphericFlow()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.coriolisStrength = 0f;
            host.Config.velocityAdvectionRate = 0f;
            host.Config.prevailingWind = 2.0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);

            yield return Step(host, 2);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[y * width + x].x, Is.GreaterThan(0.05f),
                    "Positive prevailing wind must accelerate atmospheric air eastward.");
            });
        }

        [UnityTest]
        public IEnumerator VelocityAdvectionTransportsWindMomentum()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0f;
            host.Config.coriolisStrength = 0f;
            host.Config.prevailingWind = 0f;
            host.Config.velocityAdvectionRate = 2.0f;
            host.Config.atmosphericCflLimit = 0.5f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);

            PaintField(host, x, y, 13f, 3.0f);
            yield return Step(host, 1);
            yield return Step(host, 3);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                int downstreamX = (x + 1) % width;
                Assert.That(flow[y * width + downstreamX].x, Is.GreaterThan(0.05f),
                    "Downstream cell must gain eastward velocity via momentum advection.");
            });
        }

        [UnityTest]
        public IEnumerator OpposingWindsProduceUpdraftAtConvergence()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.frontalLiftStrength = 2f;
            host.Config.frontalCollisionPressure = 0f;
            host.Config.frontalDensityDrive = 0f;
            host.Config.frontalSubsidenceScale = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            for (int dx = -2; dx <= 2; dx++)
                Paint(host, x + dx, y, MaterialIds.Air);
            yield return Step(host, 1);
            PaintField(host, x, y, 13f, 0f);
            for (int dx = -2; dx <= -1; dx++)
                PaintField(host, x + dx, y, 13f, 2.0f);
            for (int dx = 1; dx <= 2; dx++)
                PaintField(host, x + dx, y, 13f, -2.0f);
            yield return Step(host, 6);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                float lift = flow[y * width + x].y;
                float far = flow[y * width + ((x + 8) % width)].y;
                Assert.That(lift, Is.GreaterThan(0.05f),
                    "Convergence of opposing angular winds must produce an updraft.");
                Assert.That(lift, Is.GreaterThan(far + 0.04f),
                    "Lift must concentrate at the meeting column, not far from the front.");
            });
        }

        [UnityTest]
        public IEnumerator HeadOnCollisionBuildsStagnationPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.pressureCompressibility = 0f;
            host.Config.frontalLiftStrength = 0f;
            host.Config.frontalCollisionPressure = 0f;
            host.Config.frontalDensityDrive = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);

            void SeedCollision()
            {
                for (int dx = -2; dx <= 2; dx++)
                    Paint(host, x + dx, y, MaterialIds.Air);
                PaintField(host, x, y, 13f, 0f);
                for (int dx = -2; dx <= -1; dx++)
                    PaintField(host, x + dx, y, 13f, 2.5f);
                for (int dx = 1; dx <= 2; dx++)
                    PaintField(host, x + dx, y, 13f, -2.5f);
            }

            SeedCollision();
            yield return Step(host, 6);
            float baseline = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                baseline = states[y * width + x].y;
            });

            host.Config.frontalCollisionPressure = 2f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            SeedCollision();
            yield return Step(host, 6);

            yield return ReadFields(host, (_, states, __, ___) =>
            {
                Assert.That(states[y * width + x].y, Is.GreaterThan(baseline + 0.02f),
                    "Head-on collision pressure must raise the meeting-column pressure above a control with the knob off.");
            });
        }

        [UnityTest]
        public IEnumerator ColdAirUndercutsWarmAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.frontalLiftStrength = 0f;
            host.Config.frontalCollisionPressure = 0f;
            host.Config.frontalDensityDrive = 2f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int surfaceY = Mathf.Clamp(Mathf.CeilToInt(host.Grid.atmosphereStartRadius * height - 0.5f), 1, height - 5);
            int aloftY = Mathf.Clamp(height - 3, surfaceY + 2, height - 2);

            for (int y = surfaceY; y <= aloftY; y++)
            {
                for (int dx = -4; dx <= 4; dx++)
                    Paint(host, x + dx, y, MaterialIds.Air);
            }
            yield return Step(host, 1);
            for (int y = surfaceY; y <= aloftY; y++)
            {
                for (int dx = -4; dx <= -1; dx++)
                    PaintField(host, x + dx, y, 1f, -40f);
                for (int dx = 1; dx <= 4; dx++)
                    PaintField(host, x + dx, y, 1f, 40f);
                for (int dx = -4; dx <= 4; dx++)
                    PaintField(host, x + dx, y, 13f, 0f);
            }
            yield return Step(host, 6);

            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                Assert.That(flow[surfaceY * width + x].x, Is.GreaterThan(0.02f),
                    "Cold air must undercut toward the warm column near the surface.");
                Assert.That(flow[aloftY * width + x].x, Is.LessThan(-0.02f),
                    "Warm air must return over the cold column aloft.");
            });
        }

        [UnityTest]
        public IEnumerator FrontalCollisionConservesVapor()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericAdvectionRate = 2f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.vaporCapacityScale = 2f;
            host.Config.atmosphericCflLimit = 0.5f;
            host.Config.frontalLiftStrength = 1.5f;
            host.Config.frontalCollisionPressure = 1f;
            host.Config.frontalDensityDrive = 1f;
            host.Config.frontalSubsidenceScale = 0.35f;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int y = AtmosphereY(host);
            int x0 = width / 2;
            PaintAirChamber(host, x0 - 12, x0 + 12, y - 1, y + 1);
            yield return Step(host, 1);

            yield return ReadFields(host, (_, states, aux, ___) =>
            {
                for (int dx = -12; dx <= 12; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int xx = ((x0 + dx) % width + width) % width;
                        float vapor = aux[(y + dy) * width + xx].x;
                        float cloud = states[(y + dy) * width + xx].z;
                        if (vapor > 0f)
                            PaintField(host, xx, y + dy, 6f, -vapor);
                        if (cloud > 0f)
                            PaintField(host, xx, y + dy, 2f, -cloud);
                    }
                }
            });
            yield return Step(host, 1);

            PaintField(host, x0 - 3, y, 6f, 0.6f);
            PaintField(host, x0 + 3, y, 6f, 0.6f);
            for (int dx = -4; dx <= -1; dx++)
                PaintField(host, x0 + dx, y, 13f, 1.8f);
            for (int dx = 1; dx <= 4; dx++)
                PaintField(host, x0 + dx, y, 13f, -1.8f);
            yield return Step(host, 1);

            float massBefore = 0f;
            yield return ReadFields(host, (_, states, aux, ___) =>
            {
                massBefore = SumWaterBox(states, aux, width, x0 - 12, x0 + 12, y - 1, y + 1);
            });
            Assert.That(massBefore, Is.GreaterThan(0.8f));

            yield return Step(host, 20);

            yield return ReadFields(host, (_, states, aux, __) =>
            {
                float massAfter = SumWaterBox(states, aux, width, x0 - 12, x0 + 12, y - 1, y + 1);
                Assert.That(massAfter, Is.EqualTo(massBefore).Within(0.05f));
            });
        }

        [UnityTest]
        public IEnumerator HumidAirSuppressesEvaporation()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 2f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporCapacityScale = 0.05f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.windStrength = 0f;
            host.Config.hydrostaticIterations = 1;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int dryX = 4;
            int humidX = 12;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            foreach (int x in new[] { dryX, humidX })
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                    Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                    Paint(host, x + dx, airY, MaterialIds.Air);
                    Paint(host, x + dx, airY + 1, MaterialIds.Rock);
                }
                PaintField(host, x, surfaceY, 2f, 0.8f);
                PaintField(host, x, surfaceY, 1f, 35f);
            }
            PaintField(host, humidX, airY, 6f, 2f);
            host.Config.evaporationRate = 0f;
            yield return Step(host, 1);
            host.Config.evaporationRate = 2f;

            float dryBefore = 0f, humidBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                dryBefore = states[surfaceY * width + dryX].z;
                humidBefore = states[surfaceY * width + humidX].z;
            });
            Assert.That(dryBefore, Is.GreaterThan(0.2f));
            yield return Step(host, 20);
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                float dryLost = dryBefore - states[surfaceY * width + dryX].z;
                float humidLost = humidBefore - states[surfaceY * width + humidX].z;
                Assert.That(dryLost, Is.GreaterThan(humidLost + 0.01f),
                    $"Dry air should evaporate more than humid air. Dry {dryLost:F3} humid {humidLost:F3}");
            });
        }

        [UnityTest]
        public IEnumerator WindIncreasesEvaporation()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 1.2f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporCapacityScale = 0.08f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.windStrength = 0f;
            host.Config.hydrostaticIterations = 1;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int calmX = 4;
            int windX = 12;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            foreach (int x in new[] { calmX, windX })
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                    Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                    Paint(host, x + dx, airY, MaterialIds.Air);
                    Paint(host, x + dx, airY + 1, MaterialIds.Rock);
                }
                PaintField(host, x, surfaceY, 2f, 0.8f);
                PaintField(host, x, surfaceY, 1f, 35f);
            }
            host.Config.evaporationRate = 0f;
            yield return Step(host, 1);
            PaintField(host, windX, airY, 13f, 4f);
            yield return Step(host, 1);
            host.Config.evaporationRate = 0.15f;

            float calmBefore = 0f, windBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                calmBefore = states[surfaceY * width + calmX].z;
                windBefore = states[surfaceY * width + windX].z;
            });
            Assert.That(calmBefore, Is.GreaterThan(0.2f));
            yield return Step(host, 20);
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                float calmLost = calmBefore - states[surfaceY * width + calmX].z;
                float windLost = windBefore - states[surfaceY * width + windX].z;
                Assert.That(windLost, Is.GreaterThan(calmLost + 0.005f),
                    $"Wind should increase evaporation. Wind {windLost:F3} calm {calmLost:F3}");
            });
        }

        [UnityTest]
        public IEnumerator RisingAirCoolsAdiabatically()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericLapseRate = 20f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = AtmosphereY(host);
            Paint(host, x, y, MaterialIds.Air);
            PaintField(host, x, y, 1f, 30f);
            PaintField(host, x, y, 12f, 2f);
            yield return Step(host, 1);
            float before = 0f;
            yield return ReadFields(host, (_, states, __, ___) => before = states[y * width + x].x);
            yield return Step(host, 8);
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                Assert.That(states[y * width + x].x, Is.LessThan(before - 0.05f),
                    "Rising air must cool along the lapse.");
            });
        }

        [UnityTest]
        public IEnumerator ColdSurfaceCollectsDew()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.vaporCapacityScale = 0.01f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.hydrostaticIterations = 1;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = 4;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, surfaceY - 1, MaterialIds.Rock);
                Paint(host, x + dx, surfaceY, MaterialIds.Soil);
                Paint(host, x + dx, airY, MaterialIds.Air);
                Paint(host, x + dx, airY + 1, MaterialIds.Rock);
            }
            PaintField(host, x, surfaceY, 1f, -8f);
            PaintField(host, x, airY, 6f, 0.2f);
            PaintField(host, x, airY, 1f, 20f);
            yield return Step(host, 1);
            host.Config.condensationRate = 2f;
            host.Config.dewRate = 2f;
            float filmBefore = 0f;
            float vaporBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) =>
            {
                filmBefore = states[surfaceY * width + x].z;
                vaporBefore = aux[airY * width + x].x;
            });
            Assert.That(vaporBefore, Is.GreaterThan(0.05f));
            yield return Step(host, 20);
            yield return ReadFields(host, (_, states, aux, __) =>
            {
                Assert.That(states[surfaceY * width + x].z, Is.GreaterThan(filmBefore + 0.002f),
                    "Cold soil should collect dew from humid air.");
                Assert.That(aux[airY * width + x].x, Is.LessThan(vaporBefore - 0.002f));
            });
        }

        [UnityTest]
        public IEnumerator CloudShadesSurfaceHeating()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.solarIntensity = 3f;
            host.Config.atmosphereAbsorption = 0f;
            host.Config.dayLengthSeconds = 100000f;
            host.Config.solarPolarOutputMin = 1f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.hydrostaticIterations = 1;
            host.Config.slowPassInterval = 100000;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int clearX = 1;
            int cloudX = 3;
            int surfaceY = SurfaceY(host);
            int airY = surfaceY + 1;
            int height = host.Grid.radialResolution;
            foreach (int x in new[] { clearX, cloudX })
            {
                Paint(host, x, surfaceY - 1, MaterialIds.Rock);
                Paint(host, x, surfaceY, MaterialIds.Soil);
                for (int y = airY; y < height; y++)
                {
                    Paint(host, x, y, MaterialIds.Air);
                    PaintField(host, x, y, 6f, -100f);
                }
                PaintField(host, x, surfaceY, 1f, 10f);
            }
            PaintField(host, cloudX, airY + 2, 2f, 2.5f);
            yield return Step(host, 1);
            float clearBefore = 0f, cloudBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                clearBefore = states[surfaceY * width + clearX].x;
                cloudBefore = states[surfaceY * width + cloudX].x;
            });
            yield return Step(host, 20);
            yield return ReadFields(host, (_, states, __, ___) =>
            {
                float clearGain = states[surfaceY * width + clearX].x - clearBefore;
                float cloudGain = states[surfaceY * width + cloudX].x - cloudBefore;
                Assert.That(clearGain, Is.GreaterThan(cloudGain + 0.02f),
                    $"Clear ground should heat more than cloud-shaded ground. Clear {clearGain:F3} cloud {cloudGain:F3}");
            });
        }

        [UnityTest]
        public IEnumerator EvaporationDoesNotPushAirAboveSaturationExceptBoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            DisableWeatherNoise(host);
            host.Config.evaporationRate = 2f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.combustionHeatYield = 0f;
            host.Config.stormStrikeHeat = 0f;
            host.Config.hydrothermalNutrientRate = 0f;
            host.Config.vaporCapacityScale = 0.2f;
            host.Config.windStrength = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int y = SurfaceY(host);
            for (int dx = -3; dx <= 3; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Water);
                PaintField(host, x + dx, y, 2f, 1f);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
                PaintField(host, x + dx, y + 1, 6f, -100f);
            }
            yield return Step(host, 12);

            float scale = host.Config.vaporCapacityScale;
            int oversat = 0;
            yield return ReadFields(host, (mats, states, aux, _) =>
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int xx = ((x + dx) % width + width) % width;
                    int idx = (y + 1) * width + xx;
                    if (mats[idx] != MaterialIds.Air) continue;
                    float t = Mathf.Clamp(states[idx].x, -40f, 80f);
                    float es = Mathf.Exp(SimulationMetrics.WaterMagnusA * t / Mathf.Max(1e-3f, SimulationMetrics.WaterMagnusB + t));
                    float sat = Mathf.Max(1e-4f, Mathf.Max(0.001f, scale) * es);
                    if (aux[idx].x > sat + 0.02f) oversat++;
                }
            });
            Assert.That(oversat, Is.EqualTo(0), "Magnus-deficit evaporation must not push air above saturation.");
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
