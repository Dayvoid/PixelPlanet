using System;
using System.Collections;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Experimental.Rendering;
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
            host.Config.floraPoleDriftRate = 0f;
            host.Config.floraWindShearRate = 0f;
            host.Config.floraRainShearRate = 0f;
            host.Config.floraFragmentYield = 0f;
            host.Config.floraGeneExpressionRange = 0f;
            host.Config.combustionOxygenReplenishRate = 0f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.combustionBurnRate = 0f;
            host.Config.combustionMoistureIgnitionPenalty = 0f;
            host.Config.combustionSteamSuppression = 0f;
            host.Config.combustionSuppressionMoisture = 8f;
            host.Config.combustionHeatYield = 0f;
            host.Config.combustionPressureScale = 0f;
            host.Config.combustionUpdraftStrength = 0f;
            host.Config.ashUpdraftStrength = 0f;
            host.Config.ashSettlingStrength = 0f;
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
            host.Config.grassSeedAtWorldgen = false;
            host.Config.grassRootUptakeRate = 0f;
            host.Config.detritusVaporAbsorbRate = 0f;
            host.Config.detritusEvaporationScale = 0f;
            host.Config.detritusMoistureShareRate = 0f;
            host.Config.detritusNutrientLeachRate = 0f;
            host.Config.detritusDecayRate = 0f;
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

        private static void StampAlgaeStack(SimulationHost host, int x, int y, int height)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Soil);
                for (int dy = 1; dy <= height + 2; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Air);
            }
            for (int dy = 1; dy <= height; dy++)
                Paint(host, x, y + dy, MaterialIds.Algae);
        }

        private static int CountAlgae(uint[] materials)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == MaterialIds.Algae) count++;
            }
            return count;
        }

        private static float SignedThetaDelta(int from, int to, int width)
        {
            int delta = to - from;
            if (delta > width / 2) delta -= width;
            if (delta < -width / 2) delta += width;
            return delta;
        }

        private static float AlgaeCentroidOffset(SimulationHost host, uint[] materials, int originX)
        {
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            float sum = 0f;
            int count = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (materials[Index(host, x, y)] != MaterialIds.Algae) continue;
                    sum += SignedThetaDelta(originX, x, width);
                    count++;
                }
            }
            return count == 0 ? 0f : sum / count;
        }

        private static IEnumerator StampAlgaeGene(SimulationHost host, int geneIndex, byte value)
        {
            uint[] materials = null;
            FloraGenome.Packed[] genomes = null;
            yield return ReadFloraFields(host, (mats, _, _, _, _, packed, _) =>
            {
                materials = mats;
                genomes = packed;
            });

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            var data = new Vector4[width * height];
            for (int i = 0; i < genomes.Length; i++)
            {
                FloraGenome.Packed genome = genomes[i];
                if (materials[i] == MaterialIds.Algae)
                    genome = FloraGenome.EncodeGene(genome, geneIndex, value);
                data[i] = new Vector4(
                    BitConverter.Int32BitsToSingle(unchecked((int)genome.X)),
                    BitConverter.Int32BitsToSingle(unchecked((int)genome.Y)),
                    BitConverter.Int32BitsToSingle(unchecked((int)genome.Z)),
                    BitConverter.Int32BitsToSingle(unchecked((int)genome.W)));
            }

            var staging = new Texture2D(width, height, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
            staging.SetPixelData(data, 0);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, 0, 0, host.Resources.LifeGenomeRead, 1, 0);
            Graphics.CopyTexture(staging, 0, 0, host.Resources.LifeGenomeWrite, 1, 0);
            UnityEngine.Object.Destroy(staging);
        }

        private static int ChooseStackTheta(SimulationHost host, out int expectedStep)
        {
            int width = host.Grid.angularResolution;
            int seed = host.Config.seed;
            int poleTheta = Mathf.FloorToInt(PolarPoleGeometry.PoleAngle01(seed) * width);
            int candidate = host.Grid.WrapTheta(poleTheta + 12);
            expectedStep = PolarPoleGeometry.NearestPoleStep(candidate, width, seed);
            if (expectedStep == 0)
            {
                candidate = host.Grid.WrapTheta(poleTheta + 16);
                expectedStep = PolarPoleGeometry.NearestPoleStep(candidate, width, seed);
            }
            if (expectedStep == 0)
            {
                candidate = host.Grid.WrapTheta(poleTheta - 12);
                expectedStep = PolarPoleGeometry.NearestPoleStep(candidate, width, seed);
            }
            return candidate;
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
            Paint(host, x, y, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Algae);
            Paint(host, x - 1, y + 1, MaterialIds.Rock);
            Paint(host, x + 1, y + 1, MaterialIds.Rock);
            Paint(host, x, y + 2, MaterialIds.Rock);
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
        public IEnumerator StackedAlgaeDriftsTowardNearestPoleAndConservesCount()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int expectedStep;
            int x = ChooseStackTheta(host, out expectedStep);
            Assert.That(expectedStep, Is.Not.EqualTo(0));
            int y = SurfaceY(host);
            StampAlgaeStack(host, x, y, 3);
            yield return Step(host, 2);
            yield return StampAlgaeGene(host, FloraGenome.GenePoleDrift, 255);
            host.Config.floraPoleDriftRate = 4f;
            int countBefore = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                countBefore = CountAlgae(materials);
            });
            Assert.That(countBefore, Is.EqualTo(3));
            yield return Step(host, 40);

            int countAfter = 0;
            float centroid = 0f;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                countAfter = CountAlgae(materials);
                centroid = AlgaeCentroidOffset(host, materials, x);
            });
            Assert.That(countAfter, Is.EqualTo(countBefore));
            Assert.That(centroid * expectedStep, Is.GreaterThan(0.5f));
        }

        [UnityTest]
        public IEnumerator SilentPoleDriftGeneDoesNotMoveStackedAlgae()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int expectedStep;
            int x = ChooseStackTheta(host, out expectedStep);
            Assert.That(expectedStep, Is.Not.EqualTo(0));
            int y = SurfaceY(host);
            StampAlgaeStack(host, x, y, 3);
            yield return Step(host, 2);
            yield return StampAlgaeGene(host, FloraGenome.GenePoleDrift, 0);
            host.Config.floraPoleDriftRate = 4f;
            yield return Step(host, 24);

            float centroid = 1f;
            int count = 0;
            yield return ReadFloraFields(host, (materials, _, _, _, _, _, _) =>
            {
                count = CountAlgae(materials);
                centroid = AlgaeCentroidOffset(host, materials, x);
            });
            Assert.That(count, Is.EqualTo(3));
            Assert.That(Mathf.Abs(centroid), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator WindShearMovesCrestAndFragmentsBiomassIntoSpores()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            for (int dx = -2; dx <= 8; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
                Paint(host, x + dx, y + 2, MaterialIds.Air);
            }
            Paint(host, x, y + 1, MaterialIds.Algae);
            yield return Step(host, 2);
            yield return StampAlgaeGene(host, FloraGenome.GenePoleDrift, 0);
            host.Config.windDamping = 0f;
            host.Config.floraWindShearRate = 4f;
            host.Config.floraRainShearRate = 0f;
            host.Config.floraFragmentYield = 0.5f;
            host.Config.floraAnchorGrip = 0f;
            host.Config.floraPoleDriftRate = 0f;

            float biomassBefore = 0f;
            yield return ReadFloraFields(host, (_, _, _, _, life, _, _) =>
            {
                biomassBefore = life[Index(host, x, y + 1)].y;
            });
            for (int i = 0; i < 12; i++)
            {
                for (int dx = -2; dx <= 6; dx++)
                {
                    host.QueueAngularWind(new Vector2Int(host.Grid.WrapTheta(x + dx), y + 1), 0, 8f);
                    host.QueueAngularWind(new Vector2Int(host.Grid.WrapTheta(x + dx), y + 2), 0, 8f);
                }
                yield return Step(host, 1);
            }

            int algaeX = x;
            float biomassAfter = 1f;
            float sporeLoad = 0f;
            yield return ReadFloraFields(host, (materials, _, _, _, life, _, _) =>
            {
                for (int dx = -2; dx <= 8; dx++)
                {
                    for (int dy = 0; dy <= 3; dy++)
                    {
                        int ix = host.Grid.WrapTheta(x + dx);
                        int iy = y + dy;
                        if (materials[Index(host, ix, iy)] != MaterialIds.Algae) continue;
                        algaeX = ix;
                        biomassAfter = life[Index(host, ix, iy)].y;
                    }
                }
                sporeLoad = life[Index(host, x, y + 2)].x
                    + life[Index(host, x + 1, y + 1)].x
                    + life[Index(host, x - 1, y + 1)].x
                    + life[Index(host, algaeX, y + 2)].x;
            });
            Assert.That(SignedThetaDelta(x, algaeX, host.Grid.angularResolution), Is.GreaterThan(0f));
            Assert.That(biomassAfter, Is.LessThan(biomassBefore - 0.02f));
            Assert.That(sporeLoad, Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesFloraMigrationConfig()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.floraPoleDriftRate = 1.25f;
            host.Config.floraWindShearRate = 2.5f;
            host.Config.floraRainShearRate = 1.75f;
            host.Config.floraFragmentYield = 0.4f;
            host.Config.floraAnchorGrip = 0.8f;

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-flora-migration.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Config.floraPoleDriftRate = 0f;
            host.Config.floraWindShearRate = 0f;
            host.Config.floraRainShearRate = 0f;
            host.Config.floraFragmentYield = 0f;
            host.Config.floraAnchorGrip = 0f;
            Assert.That(snapshots.Load(host, path), Is.True);
            Assert.That(host.Config.floraPoleDriftRate, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(host.Config.floraWindShearRate, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(host.Config.floraRainShearRate, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(host.Config.floraFragmentYield, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(host.Config.floraAnchorGrip, Is.EqualTo(0.8f).Within(0.001f));
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
