using System;
using System.Collections;
using System.Reflection;
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
    public sealed class CombustionIntegrationTests
    {
        private SimulationConfigSnapshot _configSnapshot;
        private SimulationConfig _config;

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
            if (host != null && host.Config != null)
            {
                _config = host.Config;
                if (_configSnapshot == null)
                    _configSnapshot = SimulationConfigSnapshot.Capture(host.Config);
            }
        }

        [UnityTearDown]
        public IEnumerator RestoreConfig()
        {
            if (_config != null && _configSnapshot != null)
                _configSnapshot.Restore(_config);
            _configSnapshot = null;
            _config = null;
            yield return null;
        }

        private static IEnumerator ReadFields(SimulationHost host, Action<uint[], Vector4[], Vector4[], Vector4[], Vector4[], Vector2[]> consume)
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
                        AsyncGPUReadback.Request(host.Resources.EcologyRead, 0, ecologyRequest =>
                        {
                            if (ecologyRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] ecology = ecologyRequest.GetData<Vector4>().ToArray();
                            AsyncGPUReadback.Request(host.Resources.CombustionRead, 0, combustionRequest =>
                            {
                                if (combustionRequest.hasError) { failed = true; done = true; return; }
                                Vector4[] combustion = combustionRequest.GetData<Vector4>().ToArray();
                                AsyncGPUReadback.Request(host.Resources.FlowRead, 0, flowRequest =>
                                {
                                    if (flowRequest.hasError) { failed = true; done = true; return; }
                                    consume(materials, states, aux, ecology, combustion, flowRequest.GetData<Vector2>().ToArray());
                                    done = true;
                                });
                            });
                        });
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + x;

        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.86f), 4, host.Grid.radialResolution - 5);

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

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void FreezeWorld(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.windDamping = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.radiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 1;
            host.Config.transportPassInterval = 1;
            host.Config.mycologyAirTransportRate = 0f;
            host.Config.mycologyWaterTransportRate = 0f;
            host.Config.mycologyDiffusionRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.mycologySporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.combustionOxygenReplenishRate = 0.15f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 8f;
            host.Config.combustionIgnitionDecayRate = 4f;
            host.Config.combustionSeedIntensity = 0.7f;
            host.Config.combustionBurnRate = 0.8f;
            host.Config.combustionHeatYield = 3f;
            host.Config.combustionPressureScale = 4f;
            host.Config.combustionUpdraftStrength = 8f;
            host.Config.combustionFlashVaporizationRate = 0f;
            host.Config.combustionMoistureIgnitionPenalty = 0f;
            host.Config.combustionSteamSuppression = 0f;
            host.Config.combustionSuppressionMoisture = 8f;
            host.Config.combustionMinFuel = 0.02f;
            host.Config.combustionMinOxygen = 0.05f;
            host.Config.combustionFlameDecay = 0.05f;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.grassRootUptakeRate = 0f;
            host.Config.detritusVaporAbsorbRate = 0f;
            host.Config.detritusEvaporationScale = 0f;
            host.Config.detritusMoistureShareRate = 0f;
            host.Config.detritusNutrientLeachRate = 0f;
            host.Config.detritusDecayRate = 0f;
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.Config.seed = 2026;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        private static void StampBurnPlot(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
        }

        private static void SeedFuel(SimulationHost host, int x, int y, float myco, uint traits)
        {
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 0.2f, myco, traits);
            PaintField(host, x, y, 2f, -10f);
        }

        [UnityTest]
        public IEnumerator IgnitionRequiresFuelHeatAndOxygen()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);

            int y = SurfaceY(host);
            int fueled = 20;
            int noFuel = 40;
            int starved = 60;
            StampBurnPlot(host, fueled, y);
            StampBurnPlot(host, noFuel, y);
            StampBurnPlot(host, starved, y);
            yield return Step(host, 1);

            SeedFuel(host, fueled, y, 0.85f, MycologyTraits.Basic);
            SeedFuel(host, noFuel, y, 0f, MycologyTraits.Basic);
            SeedFuel(host, starved, y, 0.85f, MycologyTraits.Basic);
            PaintField(host, fueled, y, 1f, 250f);
            PaintField(host, noFuel, y, 1f, 250f);
            PaintField(host, starved, y, 1f, 250f);
            host.QueueOxygen(new Vector2Int(fueled, y), 0, 1f);
            host.QueueOxygen(new Vector2Int(noFuel, y), 0, 1f);
            host.QueueOxygen(new Vector2Int(starved, y), 0, 0f);
            host.Config.combustionOxygenReplenishRate = 0f;
            yield return Step(host, 8);

            yield return ReadFields(host, (_, __, ___, ecology, combustion, ____) =>
            {
                Assert.That(ecology[Index(host, fueled, y)].y, Is.GreaterThan(0.2f));
                Assert.That(combustion[Index(host, fueled, y)].y, Is.GreaterThan(0.2f));
                Assert.That(combustion[Index(host, noFuel, y)].y, Is.LessThan(0.05f));
                Assert.That(combustion[Index(host, starved, y)].y, Is.LessThan(0.05f));
            });
        }

        [UnityTest]
        public IEnumerator BurningConsumesFuelAndRaisesTemperature()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);

            int x = 32;
            int y = SurfaceY(host);
            StampBurnPlot(host, x, y);
            yield return Step(host, 1);
            SeedFuel(host, x, y, 0.9f, MycologyTraits.Basic);
            PaintField(host, x, y, 1f, 220f);
            host.QueueOxygen(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 1);

            float fuelBefore = 0f;
            float tempBefore = 0f;
            yield return ReadFields(host, (_, states, __, ecology, ___, ____) =>
            {
                int i = Index(host, x, y);
                fuelBefore = ecology[i].y;
                tempBefore = states[i].x;
            });

            yield return Step(host, 10);

            yield return ReadFields(host, (_, states, __, ecology, combustion, ____) =>
            {
                int i = Index(host, x, y);
                Assert.That(combustion[i].y, Is.GreaterThan(0.1f));
                Assert.That(ecology[i].y, Is.LessThan(fuelBefore - 0.02f));
                Assert.That(states[i].x, Is.GreaterThan(tempBefore + 0.5f));
            });
        }

        [UnityTest]
        public IEnumerator FireProducesUpdraftAndPressure()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);

            int x = 28;
            int y = SurfaceY(host);
            StampBurnPlot(host, x, y);
            yield return Step(host, 1);
            SeedFuel(host, x, y, 0.9f, MycologyTraits.Basic);
            PaintField(host, x, y, 1f, 220f);
            host.QueueOxygen(new Vector2Int(x, y), 0, 1f);
            host.QueueIgnition(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 1);

            float pressureBefore = 0f;
            yield return ReadFields(host, (_, states, __, ___, ____, _____) =>
            {
                pressureBefore = states[Index(host, x, y)].y;
            });

            yield return Step(host, 8);

            yield return ReadFields(host, (_, states, __, ___, combustion, flow) =>
            {
                int i = Index(host, x, y);
                Assert.That(combustion[i].y, Is.GreaterThan(0.1f));
                Assert.That(flow[i].y, Is.GreaterThan(0.02f));
                Assert.That(states[i].y, Is.GreaterThan(pressureBefore + 0.01f));
            });
        }

        [UnityTest]
        public IEnumerator WaterFlashConvertsLiquidToVaporAndSuppressesFire()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.combustionFlashVaporizationRate = 2.5f;
            host.Config.combustionSteamSuppression = 1.2f;
            host.Config.combustionSuppressionMoisture = 0.12f;
            host.Config.combustionFlameDecay = 1.5f;

            int x = 36;
            int y = SurfaceY(host);
            StampBurnPlot(host, x, y);
            yield return Step(host, 1);
            SeedFuel(host, x, y, 0.9f, MycologyTraits.Basic);
            PaintField(host, x, y, 1f, 220f);
            PaintField(host, x, y, 2f, 0.45f);
            host.QueueOxygen(new Vector2Int(x, y), 0, 1f);
            host.QueueIgnition(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 1);

            float waterBefore = 0f;
            yield return ReadFields(host, (_, states, aux, __, ___, ____) =>
            {
                int i = Index(host, x, y);
                waterBefore = Mathf.Max(0f, states[i].z) + Mathf.Max(0f, aux[i].x) + Mathf.Max(0f, aux[i].y);
            });

            yield return Step(host, 12);

            yield return ReadFields(host, (_, states, aux, __, combustion, ____) =>
            {
                int i = Index(host, x, y);
                float waterAfter = Mathf.Max(0f, states[i].z) + Mathf.Max(0f, aux[i].x) + Mathf.Max(0f, aux[i].y);
                Assert.That(aux[i].x, Is.GreaterThan(0.05f));
                Assert.That(states[i].z, Is.LessThan(0.4f));
                Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(0.08f));
                Assert.That(combustion[i].y, Is.LessThan(0.15f));
            });
        }

        [UnityTest]
        public IEnumerator SealedOxygenPocketSmoldersThenExtinguishes()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.combustionOxygenReplenishRate = 0f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.combustionFlameDecay = 1.2f;
            host.Config.combustionBurnRate = 1.5f;

            int x = 44;
            int y = SurfaceY(host);
            StampBurnPlot(host, x, y);
            yield return Step(host, 1);
            SeedFuel(host, x, y, 0.95f, MycologyTraits.Basic);
            PaintField(host, x, y, 1f, 230f);
            host.QueueOxygen(new Vector2Int(x, y), 0, 0.18f);
            host.QueueIgnition(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 2);

            float oxygenMid = 1f;
            yield return ReadFields(host, (_, __, ___, ____, combustion, _____) =>
            {
                int i = Index(host, x, y);
                oxygenMid = combustion[i].x;
                Assert.That(combustion[i].y, Is.GreaterThan(0.05f));
            });

            yield return Step(host, 20);

            yield return ReadFields(host, (_, __, ___, ____, combustion, _____) =>
            {
                int i = Index(host, x, y);
                Assert.That(combustion[i].x, Is.LessThan(oxygenMid));
                Assert.That(combustion[i].y, Is.LessThan(0.08f));
            });
        }

        [UnityTest]
        public IEnumerator HeatProneFuelIgnitesBelowHeatResistantThreshold()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.mycologyTraitEffectStrength = 0.8f;

            int y = SurfaceY(host);
            int prone = 24;
            int resistant = 48;
            StampBurnPlot(host, prone, y);
            StampBurnPlot(host, resistant, y);
            yield return Step(host, 1);

            SeedFuel(host, prone, y, 0.85f, MycologyTraits.HeatProne);
            SeedFuel(host, resistant, y, 0.85f, MycologyTraits.HeatResistant);
            host.QueueOxygen(new Vector2Int(prone, y), 0, 1f);
            host.QueueOxygen(new Vector2Int(resistant, y), 0, 1f);
            yield return Step(host, 1);

            float proneTemp = 0f;
            float resistantTemp = 0f;
            yield return ReadFields(host, (_, states, __, ___, ____, _____) =>
            {
                proneTemp = states[Index(host, prone, y)].x;
                resistantTemp = states[Index(host, resistant, y)].x;
            });

            // Soil ignition is 180. Heat-prone drops it; heat-resistant raises it.
            PaintField(host, prone, y, 1f, 176f - proneTemp);
            PaintField(host, resistant, y, 1f, 176f - resistantTemp);
            yield return Step(host, 8);

            yield return ReadFields(host, (_, __, ___, ____, combustion, _____) =>
            {
                Assert.That(combustion[Index(host, prone, y)].y, Is.GreaterThan(0.15f));
                Assert.That(combustion[Index(host, resistant, y)].y, Is.LessThan(0.05f));
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
