using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class ClimateIntegrationTests
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
            ResetClimateDefaults(host);
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

        private static void ResetClimateDefaults(SimulationHost host)
        {
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.ticksPerSecond = 20f;
            host.Config.dayLengthSeconds = 180f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.climateLayerEnable = false;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.climatePrevailingInject = false;
            host.Config.climateAlbedoFeedback = false;
            host.Config.climateBiomeFeedback = false;
            host.Config.climateBinCount = 16;
            host.Config.climateCouplePeriod = 1;
            host.Config.climateSeasonalAmplitude = 0f;
            host.Config.prevailingWind = 0f;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
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

        private static IEnumerator ReadClimate(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.ClimateState, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static float SumWater(Vector4[] states, Vector4[] aux)
        {
            double sum = 0d;
            for (int i = 0; i < states.Length; i++)
                sum += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
            return (float)sum;
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

        private static int FindExposedY(uint[] mats, int width, int height, int x)
        {
            for (int y = height - 2; y >= 1; y--)
            {
                uint material = mats[y * width + x];
                uint above = mats[(y + 1) * width + x];
                bool aboveOpen = above == MaterialIds.Void || above == MaterialIds.Air || above == MaterialIds.Vapor;
                bool solid = material != MaterialIds.Void && material != MaterialIds.Air && material != MaterialIds.Vapor;
                if (aboveOpen && solid)
                    return y;
            }
            return Mathf.Clamp(height / 2, 1, height - 2);
        }

        private static int LowerAirY(SimulationHost host)
        {
            int y = Mathf.CeilToInt(host.Grid.atmosphereStartRadius * host.Grid.radialResolution) + 2;
            return Mathf.Clamp(y, 4, host.Grid.radialResolution - 3);
        }

        [UnityTest]
        public IEnumerator DisabledInjectorsLeaveFineFieldsUnchanged()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.climateLayerEnable = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 8);

            uint[] matsOff = null;
            Vector4[] stateOff = null;
            Vector4[] auxOff = null;
            Vector2[] flowOff = null;
            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                matsOff = mats;
                stateOff = states;
                auxOff = aux;
                flowOff = flow;
            });

            host.Config.climateLayerEnable = true;
            host.Config.climatePrevailingInject = false;
            host.Config.climateAlbedoFeedback = false;
            host.Config.climateBiomeFeedback = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 8);

            yield return ReadFields(host, (mats, states, aux, flow) =>
            {
                Assert.That(mats, Is.EqualTo(matsOff));
                Assert.That(states, Is.EqualTo(stateOff));
                Assert.That(aux, Is.EqualTo(auxOff));
                Assert.That(flow, Is.EqualTo(flowOff));
            });
        }

        [UnityTest]
        public IEnumerator ClimateInjectorsConserveTrackedWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.climateLayerEnable = true;
            host.Config.climatePrevailingInject = true;
            host.Config.climateAlbedoFeedback = true;
            host.Config.climateBiomeFeedback = true;
            host.Config.climateThermalWindGain = 1.5f;
            host.Config.climateRoughnessGain = 2f;
            host.Config.climateBucketGain = 1.5f;
            host.Config.climateIceAlbedo = 0.7f;
            host.Config.floraGrowthRate = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.treeWaterUptakeRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            float waterBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __) => waterBefore = SumWater(states, aux));
            yield return Step(host, 24);
            float waterAfter = 0f;
            yield return ReadFields(host, (_, states, aux, __) => waterAfter = SumWater(states, aux));
            Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Math.Max(1d, waterBefore * 0.05d)));
        }

        [UnityTest]
        public IEnumerator ThermalWindPrefersTheWarmSector()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.climateLayerEnable = true;
            host.Config.climatePrevailingInject = true;
            host.Config.climateAlbedoFeedback = false;
            host.Config.climateBiomeFeedback = false;
            host.Config.climateThermalWindGain = 2.5f;
            host.Config.climateSeasonalAmplitude = 0f;
            host.Config.climateSlabHeatCapacity = 8f;
            host.Config.climateHeatTransport = 0.04f;
            host.Config.prevailingWind = 0f;
            host.Config.dayLengthSeconds = 100000f;
            host.Config.solarIntensity = 2f;
            host.Config.windDamping = 0.02f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.verticalBuoyancyStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            yield return Step(host, 32);

            int width = host.Grid.angularResolution;
            int bins = host.Config.climateBinCount;
            int airY = LowerAirY(host);
            int sunTheta = Mathf.Clamp(Mathf.RoundToInt(host.SolarAngle01 * width), 0, width - 1);
            int nightTheta = (sunTheta + width / 2) % width;
            int sunBin = ClimateGrid.BinOf(sunTheta, width, bins);
            int nightBin = ClimateGrid.BinOf(nightTheta, width, bins);
            float sunT = 0f;
            float nightT = 0f;
            yield return ReadClimate(host, state =>
            {
                sunT = state[ClimateGrid.StateIndex(sunBin, ClimateGrid.StateMemory)].x;
                nightT = state[ClimateGrid.StateIndex(nightBin, ClimateGrid.StateMemory)].x;
            });
            Assert.That(sunT, Is.GreaterThan(nightT + 0.25f));

            float towardSun = 0f;
            int count = 0;
            yield return ReadFields(host, (_, __, ___, flow) =>
            {
                for (int i = 1; i < Mathf.Max(2, width / 8); i++)
                {
                    int x = (sunTheta + i) % width;
                    towardSun += -flow[airY * width + x].x;
                    count++;
                }
            });
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(towardSun / count, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator IceSectorRaisesAlbedo()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.climateLayerEnable = true;
            host.Config.climateAlbedoFeedback = true;
            host.Config.climateIceAlbedo = 0.7f;
            host.Config.climateBaseAlbedo = 0.18f;
            host.Config.climateMemoryRate = 1f;
            host.Config.solarIntensity = 0f;
            host.Config.iceCapRadius = 0.28f;
            host.Config.iceCapHeight = 0.05f;
            host.Config.frozenOceans = true;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            host.RebuildClimate();
            for (int i = 0; i < 4; i++) yield return null;

            yield return ReadClimate(host, state =>
            {
                float maxIce = 0f;
                float maxAlbedo = 0f;
                float minAlbedo = 1f;
                int bins = ClimateGrid.ClampBinCount(host.Config.climateBinCount);
                for (int bin = 0; bin < bins; bin++)
                {
                    Vector4 mem = state[ClimateGrid.StateIndex(bin, ClimateGrid.StateMemory)];
                    maxIce = Mathf.Max(maxIce, mem.y);
                    maxAlbedo = Mathf.Max(maxAlbedo, mem.w);
                    minAlbedo = Mathf.Min(minAlbedo, mem.w);
                }
                Assert.That(maxIce, Is.GreaterThan(0.5f));
                Assert.That(maxAlbedo, Is.GreaterThan(host.Config.climateBaseAlbedo + 0.1f));
                Assert.That(maxAlbedo, Is.GreaterThan(minAlbedo));
            });
        }

        [UnityTest]
        public IEnumerator CanopyDarkensAndRoughensItsBin()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.climateLayerEnable = true;
            host.Config.climateBiomeFeedback = true;
            host.Config.climateCanopyAlbedoDrop = 0.35f;
            host.Config.climateRoughnessGain = 2f;
            host.Config.climateMemoryRate = 1f;
            host.Config.treeCanopyOpacity = 1f;
            host.Config.grassCanopyOpacity = 0f;
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.treeDecayRate = 0f;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.waspSeedAtWorldgen = false;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int bins = host.Config.climateBinCount;
            uint[] mats = null;
            yield return ReadFields(host, (materials, _, __, ___) => mats = materials);
            int coverBin = 1;
            int bareBin = Mathf.Max(coverBin + 3, bins - 3);
            ClimateGrid.ThetaRange(coverBin, width, bins, out int coverStart, out int coverEnd);
            ClimateGrid.ThetaRange(bareBin, width, bins, out int bareStart, out int bareEnd);
            for (int x = coverStart; x < coverEnd; x++)
            {
                int y = FindExposedY(mats, width, height, x);
                Paint(host, x, y, MaterialIds.Soil);
                if (y + 1 < height) Paint(host, x, y + 1, MaterialIds.Air);
                if (y + 2 < height) Paint(host, x, y + 2, MaterialIds.Air);
            }
            for (int x = bareStart; x < bareEnd; x++)
            {
                int y = FindExposedY(mats, width, height, x);
                Paint(host, x, y, MaterialIds.Soil);
                if (y + 1 < height) Paint(host, x, y + 1, MaterialIds.Air);
                if (y + 2 < height) Paint(host, x, y + 2, MaterialIds.Air);
            }
            yield return Step(host, 1);

            uint[] planted = null;
            yield return ReadFields(host, (materials, _, __, ___) => planted = materials);
            int sprouts = 0;
            for (int x = coverStart; x < coverEnd && sprouts < 24; x++)
            {
                int y = FindExposedY(planted, width, height, x);
                if (planted[y * width + x] != MaterialIds.Soil)
                    Paint(host, x, y, MaterialIds.Soil);
                host.QueueTreeSprout(new Vector2Int(x, y), 0);
                sprouts++;
            }
            yield return Step(host, 1);
            int leafCount = 0;
            yield return ReadFields(host, (materials, _, __, ___) =>
            {
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] == MaterialIds.Leaf || materials[i] == MaterialIds.Wood)
                        leafCount++;
            });
            Assert.That(leafCount, Is.GreaterThan(0), "Tree sprouts should leave Leaf/Wood for the climate aggregate.");
            host.RebuildClimate();
            for (int i = 0; i < 4; i++) yield return null;
            yield return ReadClimate(host, state =>
            {
                Vector4 coverMem = state[ClimateGrid.StateIndex(coverBin, ClimateGrid.StateMemory)];
                Vector4 bareMem = state[ClimateGrid.StateIndex(bareBin, ClimateGrid.StateMemory)];
                Vector4 coverLand = state[ClimateGrid.StateIndex(coverBin, ClimateGrid.StateLand)];
                Vector4 bareLand = state[ClimateGrid.StateIndex(bareBin, ClimateGrid.StateLand)];
                Assert.That(coverLand.y, Is.GreaterThan(1.05f));
                Assert.That(coverLand.y, Is.GreaterThan(bareLand.y));
                Assert.That(coverMem.w, Is.LessThan(bareMem.w));
            });
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