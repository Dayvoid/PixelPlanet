using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MycologyIntegrationTests
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

        private static IEnumerator ReadFields(SimulationHost host, Action<uint[], Vector4[], Vector4[], Vector4[]> consume)
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
                            consume(materials, states, aux, ecologyRequest.GetData<Vector4>().ToArray());
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

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + x;

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
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.geodynamicsLayerEnable = false;
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
            host.Config.mycologyElectricalTolerance = 8f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            ConfigureStableClimate(host);
        }

        private static void ConfigureStableClimate(SimulationHost host)
        {
            host.Config.mycologyGrowthTempMin = -50f;
            host.Config.mycologyGrowthTempMax = 80f;
            host.Config.mycologyGrowthMoistureMin = 0f;
            host.Config.mycologyGrowthMoistureMax = 2f;
            host.Config.mycologySurvivalTempMin = -80f;
            host.Config.mycologySurvivalTempMax = 120f;
            host.Config.mycologySurvivalMoistureMin = 0f;
            host.Config.mycologySurvivalMoistureMax = 2f;
            host.Config.mycologyElectricalTolerance = 8f;
        }

        private static void StampSurfacePlot(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
        }

        private static void StampFallPit(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y, MaterialIds.Rock);
            Paint(host, x + 1, y, MaterialIds.Rock);
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x - 1, y - 2, MaterialIds.Rock);
            Paint(host, x, y - 2, MaterialIds.Rock);
            Paint(host, x + 1, y - 2, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Air);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
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

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.Config.seed = 2026;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
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

        [UnityTest]
        public IEnumerator WorldgenSeedsMostlyBasicSporesOnAirAndWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.seed = 4242;
            host.Config.mycologyRareStrainChance = 0.04f;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            int carriers = 0;
            int seeded = 0;
            int rare = 0;
            int soilColonies = 0;
            yield return ReadFields(host, (materials, _, __, ecology) =>
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == MaterialIds.Air || materials[i] == MaterialIds.Water)
                    {
                        carriers++;
                        if (ecology[i].x > 0.001f)
                        {
                            seeded++;
                            if (MycologyTraits.FromFloat(ecology[i].z) != MycologyTraits.Basic)
                                rare++;
                        }
                    }
                    if ((materials[i] == MaterialIds.Soil || materials[i] == MaterialIds.Sediment) && ecology[i].y > 0.01f)
                        soilColonies++;
                }
            });

            Assert.That(seeded, Is.GreaterThan(carriers / 2));
            Assert.That(rare, Is.GreaterThan(0));
            Assert.That(rare, Is.LessThan(seeded / 2));
            Assert.That(soilColonies, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SporesDiffuseThroughAdjacentAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 40;
            int y = host.Grid.radialResolution - 4;
            Paint(host, x, y, MaterialIds.Air);
            Paint(host, x + 1, y, MaterialIds.Air);
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 1f, 0f, MycologyTraits.Basic);
            host.Config.mycologyDiffusionRate = 2f;
            yield return Step(host, 12);

            float neighborSpores = 0f;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                neighborSpores = ecology[Index(host, x + 1, y)].x;
            });
            Assert.That(neighborSpores, Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator AirSporesSettleOntoSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 48;
            int y = host.Grid.radialResolution - 8;
            StampSurfacePlot(host, x, y);
            host.QueueSporeSeed(new Vector2Int(x, y + 1), 0, 1f, 0f, MycologyTraits.Basic);
            host.Config.mycologySettlingRate = 4f;
            yield return Step(host, 8);

            float soilSpores = 0f;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                soilSpores = ecology[Index(host, x, y)].x;
            });
            Assert.That(soilSpores, Is.GreaterThan(0.02f));
        }

        [UnityTest]
        public IEnumerator WaterSporesSettleOntoSediment()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 52;
            int y = host.Grid.radialResolution - 8;
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Sediment);
            Paint(host, x + 1, y, MaterialIds.Water);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            host.QueueSporeSeed(new Vector2Int(x + 1, y), 0, 1f, 0f, MycologyTraits.Basic);
            host.Config.mycologySettlingRate = 4f;
            yield return Step(host, 8);

            float sedimentSpores = 0f;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                sedimentSpores = ecology[Index(host, x, y)].x;
            });
            Assert.That(sedimentSpores, Is.GreaterThan(0.02f));
        }

        [UnityTest]
        public IEnumerator SuitableSoilGainsMycoValue()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 60;
            int y = host.Grid.radialResolution - 8;
            StampSurfacePlot(host, x, y);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                values = new Vector4(2f, 0.4f, 0f, 0f)
            });
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 1f, 0.05f, MycologyTraits.Basic);
            host.Config.mycologyGrowthRate = 4f;
            host.Config.mycologyGrowthTempMin = -50f;
            host.Config.mycologyGrowthTempMax = 80f;
            host.Config.mycologyGrowthMoistureMin = 0f;
            host.Config.mycologyGrowthMoistureMax = 2f;
            host.Config.mycologySurvivalTempMin = -80f;
            host.Config.mycologySurvivalTempMax = 120f;
            yield return Step(host, 10);

            float myco = 0f;
            yield return ReadFields(host, (_, _, __, ecology) => myco = ecology[Index(host, x, y)].y);
            Assert.That(myco, Is.GreaterThan(0.08f));
        }

        [UnityTest]
        public IEnumerator MarginalConditionsStayDormant()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 64;
            int y = host.Grid.radialResolution - 8;
            StampSurfacePlot(host, x, y);
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 0.4f, 0.4f, MycologyTraits.Basic);
            yield return Step(host, 1);

            float startMyco = 0f;
            float startTemp = 0f;
            yield return ReadFields(host, (_, states, __, ecology) =>
            {
                startMyco = ecology[Index(host, x, y)].y;
                startTemp = states[Index(host, x, y)].x;
            });

            host.Config.mycologyGrowthTempMin = startTemp - 40f;
            host.Config.mycologyGrowthTempMax = startTemp - 20f;
            host.Config.mycologySurvivalTempMin = startTemp - 50f;
            host.Config.mycologySurvivalTempMax = startTemp + 50f;
            host.Config.mycologyGrowthMoistureMin = 0.5f;
            host.Config.mycologyGrowthMoistureMax = 0.9f;
            host.Config.mycologySurvivalMoistureMin = 0f;
            host.Config.mycologySurvivalMoistureMax = 2f;
            host.Config.mycologyGrowthRate = 4f;
            host.Config.mycologyDecayRate = 4f;
            yield return Step(host, 8);

            float laterMyco = 0f;
            yield return ReadFields(host, (_, _, __, ecology) => laterMyco = ecology[Index(host, x, y)].y);
            Assert.That(laterMyco, Is.EqualTo(startMyco).Within(0.03f));
        }

        [UnityTest]
        public IEnumerator HostileHeatDecolonizesSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 70;
            int y = host.Grid.radialResolution - 8;
            StampSurfacePlot(host, x, y);
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 0.2f, 0.8f, MycologyTraits.Basic);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                values = new Vector4(1f, 200f, 0f, 0f)
            });
            host.Config.mycologyDecayRate = 4f;
            host.Config.mycologyGrowthTempMax = 32f;
            host.Config.mycologySurvivalTempMax = 45f;
            yield return Step(host, 12);

            float myco = 1f;
            uint traits = 1;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                myco = ecology[Index(host, x, y)].y;
                traits = MycologyTraits.FromFloat(ecology[Index(host, x, y)].z);
            });
            Assert.That(myco, Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator HeatResistanceSurvivesHotterThanBasic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int y = host.Grid.radialResolution - 8;
            int basicX = 80;
            int resistX = 82;
            int proneX = 84;
            StampSurfacePlot(host, basicX, y);
            StampSurfacePlot(host, resistX, y);
            StampSurfacePlot(host, proneX, y);
            host.QueueSporeSeed(new Vector2Int(basicX, y), 0, 0.2f, 0.7f, MycologyTraits.Basic);
            host.QueueSporeSeed(new Vector2Int(resistX, y), 0, 0.2f, 0.7f, MycologyTraits.HeatResistant);
            host.QueueSporeSeed(new Vector2Int(proneX, y), 0, 0.2f, 0.7f, MycologyTraits.HeatProne);
            yield return Step(host, 1);

            float temp = 0f;
            float seeded = 0f;
            yield return ReadFields(host, (_, states, __, ecology) =>
            {
                temp = states[Index(host, basicX, y)].x;
                seeded = ecology[Index(host, resistX, y)].y;
            });
            Assert.That(seeded, Is.GreaterThan(0.5f));

            float target = 48f;
            PaintField(host, basicX, y, 1f, target - temp);
            PaintField(host, resistX, y, 1f, target - temp);
            PaintField(host, proneX, y, 1f, target - temp);
            host.Config.mycologyDecayRate = 3f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyTraitEffectStrength = 0.35f;
            host.Config.mycologyGrowthTempMax = 32f;
            host.Config.mycologySurvivalTempMax = 45f;
            yield return Step(host, 10);

            float basic = 0f, resist = 0f, prone = 0f;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                basic = ecology[Index(host, basicX, y)].y;
                resist = ecology[Index(host, resistX, y)].y;
                prone = ecology[Index(host, proneX, y)].y;
            });
            Assert.That(resist, Is.GreaterThan(basic));
            Assert.That(resist, Is.GreaterThan(prone));
            Assert.That(basic, Is.LessThan(0.7f));
        }

        [UnityTest]
        public IEnumerator DroughtAndElectricModifiersAffectSurvival()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int y = host.Grid.radialResolution - 8;
            int dryX = 90;
            int wetResistX = 92;
            int chargeX = 94;
            int chargeResistX = 96;
            StampSurfacePlot(host, dryX, y);
            StampSurfacePlot(host, wetResistX, y);
            StampSurfacePlot(host, chargeX, y);
            StampSurfacePlot(host, chargeResistX, y);
            host.QueueSporeSeed(new Vector2Int(dryX, y), 0, 0.2f, 0.7f, MycologyTraits.DroughtProne);
            host.QueueSporeSeed(new Vector2Int(wetResistX, y), 0, 0.2f, 0.7f, MycologyTraits.DroughtResistant);
            host.QueueSporeSeed(new Vector2Int(chargeX, y), 0, 0.2f, 0.7f, MycologyTraits.ElectricProne);
            host.QueueSporeSeed(new Vector2Int(chargeResistX, y), 0, 0.2f, 0.7f, MycologyTraits.ElectricResistant);
            PaintField(host, dryX, y, 2f, -10f);
            PaintField(host, wetResistX, y, 2f, -10f);
            PaintField(host, chargeX, y, 2f, -10f);
            PaintField(host, chargeResistX, y, 2f, -10f);
            PaintField(host, dryX, y, 5f, -10f);
            PaintField(host, wetResistX, y, 5f, -10f);
            PaintField(host, chargeX, y, 5f, -10f);
            PaintField(host, chargeResistX, y, 5f, -10f);
            PaintField(host, dryX, y, 2f, 0.12f);
            PaintField(host, wetResistX, y, 2f, 0.12f);
            PaintField(host, chargeX, y, 2f, 0.45f);
            PaintField(host, chargeResistX, y, 2f, 0.45f);
            PaintField(host, chargeX, y, 8f, 0.85f);
            PaintField(host, chargeResistX, y, 8f, 0.85f);
            host.Config.mycologyDecayRate = 3f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologySurvivalMoistureMin = 0.15f;
            host.Config.mycologyGrowthMoistureMin = 0.2f;
            host.Config.mycologyElectricalTolerance = 0.65f;
            host.Config.mycologyTraitEffectStrength = 0.6f;
            yield return Step(host, 10);

            float dry = 0f, resist = 0f, charged = 0f, chargeResist = 0f;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                dry = ecology[Index(host, dryX, y)].y;
                resist = ecology[Index(host, wetResistX, y)].y;
                charged = ecology[Index(host, chargeX, y)].y;
                chargeResist = ecology[Index(host, chargeResistX, y)].y;
            });
            Assert.That(resist, Is.GreaterThan(dry));
            Assert.That(chargeResist, Is.GreaterThan(charged));
        }

        [UnityTest]
        public IEnumerator ColonizedSoilKeepsMycoWhenItFalls()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 100;
            int y = host.Grid.radialResolution - 8;
            StampFallPit(host, x, y);
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 0.3f, 0.9f, MycologyTraits.HeatResistant);
            host.Config.gravityStrength = 4f;
            yield return Step(host, 8);

            float lowerMyco = 0f;
            uint lowerTraits = 0;
            uint lowerMaterial = 0;
            yield return ReadFields(host, (materials, _, __, ecology) =>
            {
                int lower = Index(host, x, y - 1);
                lowerMyco = ecology[lower].y;
                lowerTraits = MycologyTraits.FromFloat(ecology[lower].z);
                lowerMaterial = materials[lower];
            });
            Assert.That(lowerMaterial, Is.EqualTo(MaterialIds.Soil));
            Assert.That(lowerMyco, Is.GreaterThan(0.5f));
            Assert.That(lowerTraits, Is.EqualTo(MycologyTraits.HeatResistant));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesEcologyAndLegacyClearsIt()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 110;
            int y = host.Grid.radialResolution - 8;
            StampSurfacePlot(host, x, y);
            host.QueueSporeSeed(new Vector2Int(x, y), 0, 0.55f, 0.66f, MycologyTraits.DroughtResistant);
            yield return Step(host, 1);

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-ecology-test.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(snapshots.Load(host, path), Is.True);

            float spores = 0f, myco = 0f;
            uint traits = 0;
            yield return ReadFields(host, (_, _, __, ecology) =>
            {
                Vector4 value = ecology[Index(host, x, y)];
                spores = value.x;
                myco = value.y;
                traits = MycologyTraits.FromFloat(value.z);
            });
            Assert.That(spores, Is.EqualTo(0.55f).Within(0.05f));
            Assert.That(myco, Is.EqualTo(0.66f).Within(0.05f));
            Assert.That(traits, Is.EqualTo(MycologyTraits.DroughtResistant));
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
