using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
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
    public sealed class FloraIntegrationTests
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

        private static IEnumerator ReadFloraFields(SimulationHost host,
            Action<uint[], Vector4[], Vector4[], Vector4[], Vector4[], FloraGenome.Packed[], float[]> consume)
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
                        AsyncGPUReadback.Request(host.Resources.CombustionRead, 0, combustionRequest =>
                        {
                            if (combustionRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] combustion = combustionRequest.GetData<Vector4>().ToArray();
                            AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, 0, host.Resources.LifeGenomeRead.width, 0, host.Resources.LifeGenomeRead.height, 0, 1, lifeRequest =>
                            {
                                if (lifeRequest.hasError) { failed = true; done = true; return; }
                                Vector4[] life = lifeRequest.GetData<Vector4>().ToArray();
                                AsyncGPUReadback.Request(host.Resources.LifeGenomeRead, 0, 0, host.Resources.LifeGenomeRead.width, 0, host.Resources.LifeGenomeRead.height, 1, 1, genomeRequest =>
                                {
                                    if (genomeRequest.hasError) { failed = true; done = true; return; }
                                    Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                                    var genomes = new FloraGenome.Packed[genomeBits.Length];
                                    for (int i = 0; i < genomeBits.Length; i++)
                                        genomes[i] = FloraGenome.FromFloatBits(genomeBits[i]);
                                    AsyncGPUReadback.Request(host.Resources.LightField, 0, lightRequest =>
                                    {
                                        if (lightRequest.hasError) { failed = true; done = true; return; }
                                        consume(materials, states, aux, combustion, life, genomes, lightRequest.GetData<float>().ToArray());
                                        done = true;
                                    });
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

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static int DayX(SimulationHost host) => 8;
        private static int NightX(SimulationHost host) => host.Grid.angularResolution / 2;
        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(host.Grid.radialResolution - 10, 8, host.Grid.radialResolution - 4);

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
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
            host.Config.floraGeneExpressionRange = 0f;
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
            host.Config.mycologyAirTransportRate = 0f;
            host.Config.mycologyWaterTransportRate = 0f;
            host.Config.mycologyDiffusionRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.mycologySporulationRate = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.floraAirTransportRate = 0f;
            host.Config.floraWaterTransportRate = 0f;
            host.Config.floraDiffusionRate = 0f;
            host.Config.floraSettlingRate = 0f;
            host.Config.floraSporulationRate = 0f;
            host.Config.floraGrowthRate = 0f;
            host.Config.floraDecayRate = 0f;
            host.Config.floraPhotosynthesisRate = 0f;
            host.Config.floraOxygenYield = 0f;
            host.Config.floraExudationRate = 0f;
            host.Config.floraMaintenanceRate = 0f;
            host.Config.floraNightDrain = 0f;
            host.Config.floraWindDispersalRate = 0f;
            host.Config.floraRainDispersalRate = 0f;
            host.Config.floraStackMigrationRate = 0f;
            host.Config.combustionOxygenReplenishRate = 0f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.combustionBurnRate = 0f;
            host.Config.combustionMoistureIgnitionPenalty = 0f;
            host.Config.combustionSteamSuppression = 0f;
            host.Config.combustionSuppressionMoisture = 8f;
            host.Config.densityExchangeRate = 0f;
            ConfigureFloraClimate(host);
        }

        private static void ConfigureFloraClimate(SimulationHost host)
        {
            host.Config.floraGrowthTempMin = -50f;
            host.Config.floraGrowthTempMax = 80f;
            host.Config.floraGrowthMoistureMin = 0f;
            host.Config.floraGrowthMoistureMax = 2f;
            host.Config.floraSurvivalTempMin = -80f;
            host.Config.floraSurvivalTempMax = 120f;
            host.Config.floraSurvivalMoistureMin = 0f;
            host.Config.floraSurvivalMoistureMax = 2f;
            host.Config.floraMinLight = 0.08f;
            host.Config.floraGerminationSporeThreshold = 0.08f;
        }

        private static void StampSurfacePlot(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x - 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            Paint(host, x - 1, y + 1, MaterialIds.Air);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.ApplyPreset(SimulationPreset.Validation);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Config.seed = 2026;
            host.Config.floraSeedAtWorldgen = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        [UnityTest]
        public IEnumerator LifeBrushSeedsSporesThatSettleOnSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            host.QueueFloraSeed(new Vector2Int(x, y + 1), 0, 1f);
            host.Config.floraSettlingRate = 4f;
            host.Config.floraDiffusionRate = 2f;
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            yield return Step(host, 10);

            float soilSpores = 0f;
            float neighborSpores = 0f;
            yield return ReadFloraFields(host, (materials, _, _, _, life, _, __) =>
            {
                soilSpores = life[Index(host, x, y)].x;
                neighborSpores = life[Index(host, x + 1, y + 1)].x;
            });
            Assert.That(soilSpores, Is.GreaterThan(0.02f));
            Assert.That(neighborSpores, Is.GreaterThan(0.005f));
        }

        [UnityTest]
        public IEnumerator GerminationNeedsLightAndGrowthEnvelope()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            host.QueueFloraSeed(new Vector2Int(x, y + 1), 0, 1f);
            host.Config.floraGerminationSporeThreshold = 0.05f;
            host.Config.solarIntensity = 0f;
            yield return Step(host, 6);

            uint darkMaterial = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                darkMaterial = materials[Index(host, x, y + 1)];
            });
            Assert.That(darkMaterial, Is.Not.EqualTo(MaterialIds.Algae));

            host.Config.solarIntensity = 1.5f;
            yield return Step(host, 8);

            uint litMaterial = 0;
            uint stage = 0;
            float biomass = 0f;
            yield return ReadFloraFields(host, (materials, _, _, _, life, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                litMaterial = materials[i];
                stage = FloraGenome.Stage(genomes[i]);
                biomass = life[i].y;
            });
            Assert.That(litMaterial, Is.EqualTo(MaterialIds.Algae));
            Assert.That(stage, Is.EqualTo(FloraGenome.StageActive));
            Assert.That(biomass, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator LightDecreasesWithWaterCloudAndBurial()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            int cloudX = x + 6;
            int waterX = x + 12;
            int buryX = x + 18;
            for (int dy = -2; dy <= 6; dy++)
            {
                Paint(host, x, y + dy, MaterialIds.Air);
                Paint(host, cloudX, y + dy, MaterialIds.Air);
                Paint(host, waterX, y + dy, MaterialIds.Water);
                Paint(host, buryX, y + dy, MaterialIds.Rock);
            }
            Paint(host, buryX, y, MaterialIds.Air);
            PaintField(host, cloudX, y + 5, 2f, 1.2f);
            PaintField(host, cloudX, y + 6, 2f, 1.2f);
            host.Config.solarIntensity = 1.5f;
            yield return Step(host, 2);

            float openLight = 0f, cloudLight = 0f, waterLight = 0f, buriedLight = 0f, waterSurface = 0f;
            yield return ReadFloraFields(host, (_, _, _, _, _, _, light) =>
            {
                openLight = light[Index(host, x, y)];
                cloudLight = light[Index(host, cloudX, y)];
                waterLight = light[Index(host, waterX, y)];
                waterSurface = light[Index(host, waterX, y + 6)];
                buriedLight = light[Index(host, buryX, y)];
            });
            Assert.That(openLight, Is.GreaterThan(0.4f));
            Assert.That(cloudLight, Is.LessThan(openLight * 0.95f));
            Assert.That(waterLight, Is.LessThan(waterSurface));
            Assert.That(buriedLight, Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator PhotosynthesisRaisesEnergyOnDaySideNotNight()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int day = DayX(host);
            int night = NightX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, day, y);
            StampSurfacePlot(host, night, y);
            Paint(host, day, y + 1, MaterialIds.Algae);
            Paint(host, night, y + 1, MaterialIds.Algae);
            host.Config.solarIntensity = 1.5f;
            host.Config.floraPhotosynthesisRate = 4f;
            host.Config.floraMaintenanceRate = 0.02f;
            host.Config.floraNightDrain = 0.2f;
            yield return Step(host, 10);

            float dayEnergy = 0f, nightEnergy = 0f;
            yield return ReadFloraFields(host, (_, _, _, _, life, _, _) =>
            {
                dayEnergy = life[Index(host, day, y + 1)].z;
                nightEnergy = life[Index(host, night, y + 1)].z;
            });
            Assert.That(dayEnergy, Is.GreaterThan(0.15f));
            Assert.That(dayEnergy, Is.GreaterThan(nightEnergy));
            Assert.That(nightEnergy, Is.LessThanOrEqualTo(0.12f));
        }

        [UnityTest]
        public IEnumerator DivisionProducesMutatedNeighborAndToxinsIncreaseDose()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int toxicX = x + 10;
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            StampSurfacePlot(host, toxicX, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            Paint(host, toxicX, y + 1, MaterialIds.Algae);
            Paint(host, x - 1, y + 1, MaterialIds.Air);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            Paint(host, x, y + 2, MaterialIds.Air);
            Paint(host, toxicX - 1, y + 1, MaterialIds.Air);
            Paint(host, toxicX + 1, y + 1, MaterialIds.Air);
            Paint(host, toxicX, y + 2, MaterialIds.Air);
            PaintField(host, toxicX, y + 1, 8f, 40f);
            host.Config.solarIntensity = 1.5f;
            host.Config.floraPhotosynthesisRate = 4f;
            host.Config.floraGrowthRate = 2f;
            host.Config.floraReproductionThreshold = 0.05f;
            host.Config.floraBaseMutationRate = 0.25f;
            host.Config.floraToxinMutationScale = 4f;
            yield return Step(host, 24);

            int children = 0;
            uint parentStage = 0;
            uint childGeneration = 0;
            uint toxicDose = 0;
            bool genesInRange = true;
            bool genomeChanged = false;
            yield return ReadFloraFields(host, (materials, _, _, _, life, genomes, _) =>
            {
                FloraGenome.Packed parent = genomes[Index(host, x, y + 1)];
                parentStage = FloraGenome.Stage(parent);
                int[,] offsets = { { -1, 0 }, { 1, 0 }, { 0, 1 }, { 0, -1 } };
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + offsets[i, 0];
                    int ny = y + 1 + offsets[i, 1];
                    int idx = Index(host, nx, ny);
                    if (materials[idx] != MaterialIds.Algae) continue;
                    uint gen = FloraGenome.Generation(genomes[idx]);
                    if (gen >= 1)
                    {
                        children++;
                        childGeneration = gen;
                        for (int g = 0; g < FloraGenome.GeneCount; g++)
                        {
                            byte gene = FloraGenome.DecodeGene(genomes[idx], g);
                            if (gene != FloraGenome.DecodeGene(parent, g)) genomeChanged = true;
                        }
                    }
                    int tx = toxicX + offsets[i, 0];
                    int ty = y + 1 + offsets[i, 1];
                    int tidx = Index(host, tx, ty);
                    if (materials[tidx] == MaterialIds.Algae && FloraGenome.Generation(genomes[tidx]) >= 1)
                        toxicDose = Math.Max(toxicDose, FloraGenome.ToxinDose(genomes[tidx]));
                }
                toxicDose = Math.Max(toxicDose, FloraGenome.ToxinDose(genomes[Index(host, toxicX, y + 1)]));
                foreach (FloraGenome.Packed genome in genomes)
                {
                    if (!FloraGenome.IsValidStage(FloraGenome.Stage(genome))) genesInRange = false;
                    for (int g = 0; g < FloraGenome.GeneCount; g++)
                    {
                        int gene = FloraGenome.DecodeGene(genome, g);
                        if (gene < 0 || gene > 255) genesInRange = false;
                    }
                }
            });
            Assert.That(parentStage, Is.EqualTo(FloraGenome.StageActive));
            Assert.That(children, Is.GreaterThan(0));
            Assert.That(childGeneration, Is.GreaterThanOrEqualTo(1u));
            Assert.That(genesInRange, Is.True);
            Assert.That(genomeChanged, Is.True);
            Assert.That(toxicDose, Is.GreaterThan(0u));
        }

        [UnityTest]
        public IEnumerator DroughtDormancyRetainsBiomassAndRainRevives()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            PaintField(host, x, y + 1, 2f, 0.55f);
            host.Config.solarIntensity = 1.5f;
            host.Config.floraGrowthRate = 0.5f;
            yield return Step(host, 2);

            float biomass = 0f;
            uint startStage = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, life, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                biomass = life[i].y;
                startStage = FloraGenome.Stage(genomes[i]);
            });
            Assert.That(startStage, Is.EqualTo(FloraGenome.StageActive));
            Assert.That(biomass, Is.GreaterThan(0.2f));

            host.Config.floraGrowthMoistureMin = 0.4f;
            host.Config.floraSurvivalMoistureMin = 0.05f;
            PaintField(host, x, y + 1, 2f, -10f);
            PaintField(host, x, y + 1, 2f, 0.12f);
            yield return Step(host, 4);

            uint dormant = 0;
            float dormantBiomass = 0f;
            yield return ReadFloraFields(host, (_, _, _, _, life, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                dormant = FloraGenome.Stage(genomes[i]);
                dormantBiomass = life[i].y;
            });
            Assert.That(dormant, Is.EqualTo(FloraGenome.StageDormant));
            Assert.That(dormantBiomass, Is.EqualTo(biomass).Within(0.08f));

            PaintField(host, x, y + 1, 2f, 0.8f);
            host.Config.floraGrowthMoistureMin = 0f;
            yield return Step(host, 4);

            uint revived = 0;
            yield return ReadFloraFields(host, (_, _, _, _, _, genomes, _) =>
            {
                revived = FloraGenome.Stage(genomes[Index(host, x, y + 1)]);
            });
            Assert.That(revived, Is.EqualTo(FloraGenome.StageActive));
        }

        [UnityTest]
        public IEnumerator DesiccatedMatBurnsToAshAndFertility()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.Config.floraSurvivalMoistureMin = 0.2f;
            host.Config.floraGrowthMoistureMin = 0.3f;
            PaintField(host, x, y + 1, 2f, -10f);
            yield return Step(host, 4);

            uint stage = 0;
            yield return ReadFloraFields(host, (_, _, _, _, _, genomes, _) =>
            {
                stage = FloraGenome.Stage(genomes[Index(host, x, y + 1)]);
            });
            Assert.That(stage, Is.EqualTo(FloraGenome.StageDesiccated));

            host.QueueOxygen(new Vector2Int(x, y + 1), 0, 1f);
            host.QueueIgnition(new Vector2Int(x, y + 1), 0, 1f);
            host.Config.combustionBurnRate = 4f;
            host.Config.combustionSeedIntensity = 1f;
            host.Config.combustionMinFuel = 0.02f;
            host.Config.combustionMinOxygen = 0.01f;
            host.Config.combustionPyroFertilityYield = 0.5f;
            yield return Step(host, 20);

            uint material = 0;
            float biomass = 1f;
            float fertility = 0f;
            yield return ReadFloraFields(host, (materials, _, aux, _, life, _, _) =>
            {
                int i = Index(host, x, y + 1);
                material = materials[i];
                biomass = life[i].y;
                fertility = aux[i].z;
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Ash));
            Assert.That(biomass, Is.LessThan(0.05f));
            Assert.That(fertility, Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator AlgaeFloatsOnWaterAndFallsWhenDrained()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host) - 4;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 8; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Core);
            }
            for (int dy = 0; dy <= 6; dy++)
                Paint(host, x, y + dy, MaterialIds.Water);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.Config.densityExchangeRate = 64f;
            yield return Step(host, 16);

            int algaeY = -1;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                for (int dy = 0; dy <= 6; dy++)
                {
                    if (materials[Index(host, x, y + dy)] == MaterialIds.Algae)
                        algaeY = y + dy;
                }
            });
            Assert.That(algaeY, Is.GreaterThan(y + 1));

            for (int dy = 0; dy <= 6; dy++)
            {
                if (y + dy == algaeY) continue;
                Paint(host, x, y + dy, MaterialIds.Air);
            }
            Paint(host, x, y - 1, MaterialIds.Rock);
            host.Config.densityExchangeRate = 0f;
            host.Config.gravityStrength = 8f;
            yield return Step(host, 12);

            int fallenY = algaeY;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                for (int dy = -1; dy <= 6; dy++)
                {
                    if (materials[Index(host, x, y + dy)] == MaterialIds.Algae)
                        fallenY = y + dy;
                }
            });
            Assert.That(fallenY, Is.LessThan(algaeY));
        }

        [UnityTest]
        public IEnumerator PhotosynthesisRaisesOxygenClampedToCapacity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.QueueOxygen(new Vector2Int(x, y + 1), 0, 0.05f);
            host.Config.solarIntensity = 1.5f;
            host.Config.floraPhotosynthesisRate = 4f;
            host.Config.floraOxygenYield = 4f;
            yield return Step(host, 12);

            float oxygen = 0f;
            float capacity = host.MaterialRegistry.Get((int)MaterialIds.Algae).porosity;
            yield return ReadFloraFields(host, (_, _, _, combustion, _, _, _) =>
            {
                oxygen = combustion[Index(host, x, y + 1)].x;
            });
            Assert.That(oxygen, Is.GreaterThan(0.08f));
            Assert.That(oxygen, Is.LessThanOrEqualTo(capacity + 0.02f));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesLifeAndGenome()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.QueueFloraSeed(new Vector2Int(x, y), 0, 0.4f);
            yield return Step(host, 2);

            Vector4 lifeBefore = Vector4.zero;
            FloraGenome.Packed genomeBefore = default;
            yield return ReadFloraFields(host, (_, _, _, _, life, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                lifeBefore = life[i];
                genomeBefore = genomes[i];
            });

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-flora-test.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(snapshots.Load(host, path), Is.True);

            Vector4 lifeAfter = Vector4.zero;
            FloraGenome.Packed genomeAfter = default;
            yield return ReadFloraFields(host, (_, _, _, _, life, genomes, _) =>
            {
                int i = Index(host, x, y + 1);
                lifeAfter = life[i];
                genomeAfter = genomes[i];
            });
            Assert.That(lifeAfter.y, Is.EqualTo(lifeBefore.y).Within(0.02f));
            Assert.That(lifeAfter.z, Is.EqualTo(lifeBefore.z).Within(0.02f));
            Assert.That(genomeAfter.X, Is.EqualTo(genomeBefore.X));
            Assert.That(genomeAfter.Y, Is.EqualTo(genomeBefore.Y));
            Assert.That(genomeAfter.Z, Is.EqualTo(genomeBefore.Z));
            Assert.That(genomeAfter.W, Is.EqualTo(genomeBefore.W));
            Assert.That(host.Config.floraWindDispersalRate, Is.EqualTo(0f));
            Assert.That(host.Config.floraRainDispersalRate, Is.EqualTo(0f));
            Assert.That(host.Config.floraStackMigrationRate, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator StackedMaxMotilityMigratesPolewardOntoNonAlgae()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            int dir = PolewardDir(host.Config.seed, x, host.Grid.angularResolution);
            Assert.That(dir, Is.Not.EqualTo(0));

            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Algae);
            Paint(host, x, y + 2, MaterialIds.Algae);
            Paint(host, x, y + 3, MaterialIds.Rock);
            for (int step = 1; step <= 6; step++)
            {
                int cx = host.Grid.WrapTheta(x + dir * step);
                Paint(host, cx, y, MaterialIds.Soil);
                Paint(host, cx, y + 1, MaterialIds.Soil);
                Paint(host, cx, y + 2, MaterialIds.Air);
                Paint(host, cx, y + 3, MaterialIds.Rock);
            }
            yield return Step(host, 1);
            yield return StampStackMotility(host, x, y + 2, 255);

            host.Config.floraStackMigrationRate = 8f;
            host.Config.floraWindDispersalRate = 0f;
            host.Config.floraRainDispersalRate = 0f;
            for (int i = 0; i < 30; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            int algaeX = -1;
            int algaeY = -1;
            uint below = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, genomes, _) =>
            {
                for (int dx = -8; dx <= 8; dx++)
                {
                    int cx = host.Grid.WrapTheta(x + dx);
                    if (materials[Index(host, cx, y + 2)] != MaterialIds.Algae) continue;
                    if (FloraGenome.DecodeGene(genomes[Index(host, cx, y + 2)], FloraGenome.GeneStackMotility) != 255) continue;
                    algaeX = cx;
                    algaeY = y + 2;
                    below = materials[Index(host, cx, y + 1)];
                    break;
                }
            });
            Assert.That(algaeX, Is.GreaterThanOrEqualTo(0));
            Assert.That(algaeX, Is.Not.EqualTo(host.Grid.WrapTheta(x)));
            int signedSteps = ShortestAngularSteps(x, algaeX, host.Grid.angularResolution);
            Assert.That(Math.Sign(signedSteps), Is.EqualTo(dir));
            Assert.That(below, Is.Not.EqualTo(MaterialIds.Algae));
            Assert.That(algaeY, Is.EqualTo(y + 2));
        }

        [UnityTest]
        public IEnumerator ZeroMotilityLeavesStackUnchangedWithoutEnvironmentalForcing()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            int dir = PolewardDir(host.Config.seed, x, host.Grid.angularResolution);
            Assert.That(dir, Is.Not.EqualTo(0));

            // Local capped stack: soil / algae / algae / rock ceiling, with a short open lane.
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Algae);
            Paint(host, x, y + 2, MaterialIds.Algae);
            Paint(host, x, y + 3, MaterialIds.Rock);
            for (int step = 1; step <= 4; step++)
            {
                int cx = host.Grid.WrapTheta(x + dir * step);
                Paint(host, cx, y, MaterialIds.Soil);
                Paint(host, cx, y + 1, MaterialIds.Soil);
                Paint(host, cx, y + 2, MaterialIds.Air);
                Paint(host, cx, y + 3, MaterialIds.Rock);
            }
            yield return Step(host, 1);

            uint stacked = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                stacked = materials[Index(host, x, y + 2)];
            });
            Assert.That(stacked, Is.EqualTo(MaterialIds.Algae), "stacked algae should exist before motility stamp");

            yield return StampStackMotility(host, x, y + 2, 0);
            yield return ReadFloraFields(host, (materials, _, _, _, _, genomes, _) =>
            {
                Assert.That(materials[Index(host, x, y + 2)], Is.EqualTo(MaterialIds.Algae), "stacked algae should exist after motility stamp");
                Assert.That(FloraGenome.DecodeGene(genomes[Index(host, x, y + 2)], FloraGenome.GeneStackMotility), Is.EqualTo((byte)0));
            });

            host.Config.floraStackMigrationRate = 8f;
            host.Config.floraWindDispersalRate = 0f;
            host.Config.floraRainDispersalRate = 0f;
            yield return Step(host, 20);

            uint top = 0, baseMat = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, genomes, _) =>
            {
                top = materials[Index(host, x, y + 2)];
                baseMat = materials[Index(host, x, y + 1)];
                Assert.That(FloraGenome.DecodeGene(genomes[Index(host, x, y + 2)], FloraGenome.GeneStackMotility), Is.EqualTo((byte)0));
            });
            Assert.That(top, Is.EqualTo(MaterialIds.Algae));
            Assert.That(baseMat, Is.EqualTo(MaterialIds.Algae));
        }

        [UnityTest]
        public IEnumerator WindMovesAlgaeWithZeroNaturalMotility()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            Paint(host, x, y + 2, MaterialIds.Rock);
            Paint(host, x + 1, y + 2, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Algae);
            yield return Step(host, 1);
            yield return StampStackMotility(host, x, y + 1, 0);
            StampTangentialFlow(host, x, y + 1, 4f);
            StampTangentialFlow(host, x + 1, y + 1, 4f);

            host.Config.floraWindDispersalRate = 8f;
            host.Config.floraRainDispersalRate = 0f;
            host.Config.floraStackMigrationRate = 0f;
            for (int i = 0; i < 12; i++)
            {
                StampTangentialFlow(host, x, y + 1, 4f);
                StampTangentialFlow(host, x + 1, y + 1, 4f);
                host.Clock.RequestStep();
                yield return null;
            }

            uint origin = 0, neighbor = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                origin = materials[Index(host, x, y + 1)];
                neighbor = materials[Index(host, x + 1, y + 1)];
            });
            Assert.That(neighbor, Is.EqualTo(MaterialIds.Algae));
            Assert.That(origin, Is.Not.EqualTo(MaterialIds.Algae));
        }

        [UnityTest]
        public IEnumerator RainMovesAlgaeWithZeroNaturalMotility()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x - 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x - 1, y + 1, MaterialIds.Air);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            Paint(host, x - 1, y + 2, MaterialIds.Rock);
            Paint(host, x, y + 2, MaterialIds.Rock);
            Paint(host, x + 1, y + 2, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Algae);
            yield return Step(host, 1);
            yield return StampStackMotility(host, x, y + 1, 0);
            PaintField(host, x, y + 1, 2f, 2f);
            PaintField(host, x, y + 2, 2f, 2f);
            yield return Step(host, 1);

            host.Config.floraRainDispersalRate = 8f;
            host.Config.floraWindDispersalRate = 0f;
            host.Config.floraStackMigrationRate = 0f;
            for (int i = 0; i < 16; i++)
            {
                PaintField(host, x, y + 1, 2f, 2f);
                PaintField(host, x, y + 2, 2f, 2f);
                host.Clock.RequestStep();
                yield return null;
            }

            int algaeCount = 0;
            bool leftOrigin = false;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                leftOrigin = materials[Index(host, x, y + 1)] != MaterialIds.Algae;
                if (materials[Index(host, x - 1, y + 1)] == MaterialIds.Algae) algaeCount++;
                if (materials[Index(host, x + 1, y + 1)] == MaterialIds.Algae) algaeCount++;
            });
            Assert.That(leftOrigin, Is.True);
            Assert.That(algaeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CompetingMoversAreArbitratedWithoutDuplication()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x - 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            Paint(host, x - 1, y + 2, MaterialIds.Rock);
            Paint(host, x, y + 2, MaterialIds.Rock);
            Paint(host, x + 1, y + 2, MaterialIds.Rock);
            Paint(host, x - 1, y + 1, MaterialIds.Algae);
            Paint(host, x + 1, y + 1, MaterialIds.Algae);
            yield return Step(host, 1);
            yield return StampStackMotility(host, x - 1, y + 1, 0);
            yield return StampStackMotility(host, x + 1, y + 1, 0);
            StampTangentialFlow(host, x - 1, y + 1, 4f);
            StampTangentialFlow(host, x + 1, y + 1, -4f);
            StampTangentialFlow(host, x, y + 1, 0f);

            host.Config.floraWindDispersalRate = 8f;
            host.Config.floraRainDispersalRate = 0f;
            host.Config.floraStackMigrationRate = 0f;
            for (int i = 0; i < 10; i++)
            {
                StampTangentialFlow(host, x - 1, y + 1, 4f);
                StampTangentialFlow(host, x + 1, y + 1, -4f);
                StampTangentialFlow(host, x, y + 1, 0f);
                host.Clock.RequestStep();
                yield return null;
            }

            int algaeCount = 0;
            FloraGenome.Packed[] winners = new FloraGenome.Packed[2];
            int winnerCount = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, life, genomes, _) =>
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int i = Index(host, x + dx, y + 1);
                    if (materials[i] != MaterialIds.Algae) continue;
                    algaeCount++;
                    Assert.That(life[i].y, Is.GreaterThan(0.05f));
                    Assert.That(FloraGenome.IsValidStage(FloraGenome.Stage(genomes[i])), Is.True);
                    if (winnerCount < winners.Length)
                        winners[winnerCount++] = genomes[i];
                }
            });
            Assert.That(algaeCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesMovementSettingsAndMovedOrganism()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            Paint(host, x, y + 2, MaterialIds.Rock);
            Paint(host, x + 1, y + 2, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Algae);
            yield return Step(host, 1);
            yield return StampStackMotility(host, x, y + 1, 200);
            StampTangentialFlow(host, x, y + 1, 4f);
            StampTangentialFlow(host, x + 1, y + 1, 4f);
            host.Config.floraWindDispersalRate = 1.25f;
            host.Config.floraRainDispersalRate = 0.75f;
            host.Config.floraStackMigrationRate = 0.5f;
            for (int i = 0; i < 16; i++)
            {
                StampTangentialFlow(host, x, y + 1, 4f);
                StampTangentialFlow(host, x + 1, y + 1, 4f);
                host.Clock.RequestStep();
                yield return null;
            }

            uint materialBefore = 0;
            FloraGenome.Packed genomeBefore = default;
            float windBefore = host.Config.floraWindDispersalRate;
            float rainBefore = host.Config.floraRainDispersalRate;
            float stackBefore = host.Config.floraStackMigrationRate;
            yield return ReadFloraFields(host, (materials, _, _, _, _, genomes, _) =>
            {
                int i = Index(host, x + 1, y + 1);
                materialBefore = materials[i];
                genomeBefore = genomes[i];
            });
            Assert.That(materialBefore, Is.EqualTo(MaterialIds.Algae));

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-flora-move.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Config.floraWindDispersalRate = 0f;
            host.Config.floraRainDispersalRate = 0f;
            host.Config.floraStackMigrationRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(snapshots.Load(host, path), Is.True);

            uint materialAfter = 0;
            FloraGenome.Packed genomeAfter = default;
            yield return ReadFloraFields(host, (materials, _, _, _, _, genomes, _) =>
            {
                int i = Index(host, x + 1, y + 1);
                materialAfter = materials[i];
                genomeAfter = genomes[i];
            });
            Assert.That(materialAfter, Is.EqualTo(MaterialIds.Algae));
            Assert.That(FloraGenome.DecodeGene(genomeAfter, FloraGenome.GeneStackMotility),
                Is.EqualTo(FloraGenome.DecodeGene(genomeBefore, FloraGenome.GeneStackMotility)));
            Assert.That(host.Config.floraWindDispersalRate, Is.EqualTo(windBefore).Within(0.001f));
            Assert.That(host.Config.floraRainDispersalRate, Is.EqualTo(rainBefore).Within(0.001f));
            Assert.That(host.Config.floraStackMigrationRate, Is.EqualTo(stackBefore).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator FixedSeedIsDeterministicAndValidatorPasses()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);

            Vector4 firstLife = Vector4.zero;
            FloraGenome.Packed firstGenome = default;
            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.Config.solarIntensity = 1.2f;
            host.Config.floraPhotosynthesisRate = 2f;
            yield return Step(host, 8);
            yield return ReadFloraFields(host, (_, _, _, _, life, genomes, _) =>
            {
                firstLife = life[Index(host, x, y + 1)];
                firstGenome = genomes[Index(host, x, y + 1)];
            });

            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
            StampSurfacePlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Algae);
            host.Config.solarIntensity = 1.2f;
            host.Config.floraPhotosynthesisRate = 2f;
            yield return Step(host, 8);
            Vector4 secondLife = Vector4.zero;
            FloraGenome.Packed secondGenome = default;
            yield return ReadFloraFields(host, (_, _, _, _, life, genomes, _) =>
            {
                secondLife = life[Index(host, x, y + 1)];
                secondGenome = genomes[Index(host, x, y + 1)];
            });
            Assert.That(secondLife.y, Is.EqualTo(firstLife.y).Within(0.001f));
            Assert.That(secondLife.z, Is.EqualTo(firstLife.z).Within(0.001f));
            Assert.That(secondGenome.X, Is.EqualTo(firstGenome.X));
            Assert.That(secondGenome.W, Is.EqualTo(firstGenome.W));

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            host.Config.solarIntensity = 0.8f;
            host.Config.floraPhotosynthesisRate = 0.35f;
            host.Config.floraGrowthRate = 0.16f;
            for (int i = 0; i < 40; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;
            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
        }

        private static void StampMovementLane(SimulationHost host, int x, int y, int dir, int length)
        {
            for (int step = 0; step <= length; step++)
            {
                int cx = host.Grid.WrapTheta(x + dir * step);
                Paint(host, cx, y, MaterialIds.Soil);
                Paint(host, cx, y + 1, MaterialIds.Soil);
                // Keep a solid ceiling so buoyant algae cannot float off the migration row.
                Paint(host, cx, y + 3, MaterialIds.Rock);
                Paint(host, cx, y + 2, MaterialIds.Air);
            }
        }

        private static IEnumerator StampStackMotility(SimulationHost host, int x, int y, byte motility)
        {
            host.QueueFloraGene(new Vector2Int(host.Grid.WrapTheta(x), y), FloraGenome.GeneStackMotility, motility);
            yield return Step(host, 1);
        }

        private static void StampTangentialFlow(SimulationHost host, int x, int y, float tangential)
        {
            host.QueueTangentialFlow(new Vector2Int(host.Grid.WrapTheta(x), y), tangential);
        }

        private static void WriteGenomeCell(SimulationHost host, int x, int y, FloraGenome.Packed genome)
        {
            // Kept for potential diagnostics; movement tests use QueueFloraGene instead.
            var tex = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var bytes = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(genome.X), 0, bytes, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(genome.Y), 0, bytes, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(genome.Z), 0, bytes, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(genome.W), 0, bytes, 12, 4);
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, false);
            Graphics.CopyTexture(tex, 0, 0, 0, 0, 1, 1, host.Resources.LifeGenomeRead, 1, 0, x, y);
            Graphics.CopyTexture(tex, 0, 0, 0, 0, 1, 1, host.Resources.LifeGenomeWrite, 1, 0, x, y);
            UnityEngine.Object.Destroy(tex);
        }

        private static int PolewardDir(int seed, int x, int width)
        {
            float poleAngle = Hash01(unchecked((uint)seed * 9829u));
            float angular = (x + 0.5f) / Math.Max(1f, width);
            float pole0 = poleAngle;
            float pole1 = poleAngle + 0.5f;
            if (pole1 >= 1f) pole1 -= 1f;
            float target = AngularDistance01(angular, pole0) <= AngularDistance01(angular, pole1) ? pole0 : pole1;
            float delta = target - angular;
            if (delta > 0.5f) delta -= 1f;
            if (delta < -0.5f) delta += 1f;
            if (Math.Abs(delta) <= 1e-6f) return 0;
            return delta > 0f ? 1 : -1;
        }

        private static int ShortestAngularSteps(int from, int to, int width)
        {
            int delta = ((to - from) % width + width) % width;
            if (delta > width / 2) delta -= width;
            return delta;
        }

        private static float AngularDistance01(float a, float b)
        {
            float d = Math.Abs(a - b);
            return Math.Min(d, 1f - d);
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return (value & 0x00ffffff) / 16777215f;
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
