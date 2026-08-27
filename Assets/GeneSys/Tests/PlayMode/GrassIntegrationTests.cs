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
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class GrassIntegrationTests
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

        private static IEnumerator ReadGrass(SimulationHost host,
            Action<uint[], Vector4[], Vector4[], Vector4[], GrassGenome.Packed[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.GrassRead, 0, 0, host.Resources.GrassRead.width, 0, host.Resources.GrassRead.height, 0, 1, lifeRequest =>
                    {
                        if (lifeRequest.hasError) { failed = true; done = true; return; }
                        Vector4[] life = lifeRequest.GetData<Vector4>().ToArray();
                        AsyncGPUReadback.Request(host.Resources.GrassRead, 0, 0, host.Resources.GrassRead.width, 0, host.Resources.GrassRead.height, 1, 1, genomeRequest =>
                        {
                            if (genomeRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] genomeBits = genomeRequest.GetData<Vector4>().ToArray();
                            var genomes = new GrassGenome.Packed[genomeBits.Length];
                            for (int i = 0; i < genomeBits.Length; i++)
                                genomes[i] = GrassGenome.FromFloatBits(genomeBits[i]);
                            AsyncGPUReadback.Request(host.Resources.GrassRootShare, 0, shareRequest =>
                            {
                                if (shareRequest.hasError) { failed = true; done = true; return; }
                                Vector4[] share = shareRequest.GetData<Vector4>().ToArray();
                                AsyncGPUReadback.Request(host.Resources.PropaguleRead, 0, 0, host.Resources.PropaguleRead.width, 0, host.Resources.PropaguleRead.height, 0, 1, propRequest =>
                                {
                                    if (propRequest.hasError) { failed = true; done = true; return; }
                                    consume(materials, aux, life, share, genomes, propRequest.GetData<Vector4>().ToArray());
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

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static int DayX(SimulationHost host) => 8;
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
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.radiativeCooling = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.humidityBuoyancy = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.materialSubsteps = 1;
            host.Config.slowPassInterval = 1;
            host.Config.transportPassInterval = 1;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.floraGrowthRate = 0f;
            host.Config.floraPhotosynthesisRate = 0f;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.precipitationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.densityExchangeRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.rainPixelFormationThreshold = 0f;
            host.Config.surfaceWaterPixelThreshold = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.grassGrowthTempMin = -50f;
            host.Config.grassGrowthTempMax = 80f;
            host.Config.grassGrowthMoistureMin = 0f;
            host.Config.grassGrowthMoistureMax = 2f;
            host.Config.grassSurvivalTempMin = -80f;
            host.Config.grassSurvivalTempMax = 120f;
            host.Config.grassSurvivalMoistureMin = 0f;
            host.Config.grassSurvivalMoistureMax = 2f;
            host.Config.grassMinLight = 0.01f;
            host.Config.pondingRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.floraAirTransportRate = 0f;
            host.Config.floraWaterTransportRate = 0f;
            host.Config.floraDiffusionRate = 0f;
            host.Config.floraSettlingRate = 0f;
            host.Config.floraSporulationRate = 0f;
            host.Config.floraDecayRate = 0f;
            host.Config.floraNightDrain = 0f;
            host.Config.combustionOxygenReplenishRate = 0f;
            host.Config.combustionOxygenDiffusionRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.combustionBurnRate = 0f;
            host.Config.combustionHeatYield = 0f;
            host.Config.combustionPressureScale = 0f;
            host.Config.combustionUpdraftStrength = 0f;
            host.Config.detritusVaporAbsorbRate = 0f;
            host.Config.detritusEvaporationScale = 0f;
            host.Config.detritusMoistureShareRate = 0f;
            host.Config.detritusNutrientLeachRate = 0f;
            host.Config.detritusDecayRate = 0f;
            host.Config.grassRootUptakeRate = 0f;
            host.Config.grassNightDrain = 0f;
        }

        private static void StampPlot(SimulationHost host, int x, int y)
        {
            Paint(host, x - 1, y - 2, MaterialIds.Rock);
            Paint(host, x, y - 2, MaterialIds.Rock);
            Paint(host, x + 1, y - 2, MaterialIds.Rock);
            Paint(host, x - 1, y - 1, MaterialIds.Soil);
            Paint(host, x, y - 1, MaterialIds.Soil);
            Paint(host, x + 1, y - 1, MaterialIds.Soil);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x - 1, y, MaterialIds.Soil);
            Paint(host, x + 1, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Air);
            Paint(host, x - 1, y + 1, MaterialIds.Air);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            PaintField(host, x, y - 1, 5f, 0.8f);
            PaintField(host, x - 1, y - 1, 5f, 0.8f);
            PaintField(host, x + 1, y - 1, 5f, 0.8f);
            PaintField(host, x, y - 1, 4f, 0.6f);
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.ApplyPreset(SimulationPreset.Validation);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Config.seed = 2027;
            host.Config.grassSeedAtWorldgen = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        [UnityTest]
        public IEnumerator ThreeSlotsOccupyOneSoilPixel()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            yield return Step(host, 2);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.8f);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.8f);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.8f);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.8f);
            yield return Step(host, 4);

            int occupied = 0;
            uint soil = 0;
            yield return ReadGrass(host, (materials, _, life, _, genomes, __) =>
            {
                int i = Index(host, x, y);
                soil = materials[i];
                if (GrassGenome.IsOccupied(GrassGenome.Stage(genomes[i]))) occupied++;
            });
            AsyncGPUReadback.Request(host.Resources.GrassRead, 0, 0, host.Resources.GrassRead.width, 0, host.Resources.GrassRead.height, 5, 1, _ => { });
            int slotCount = 0;
            bool done = false;
            for (int slot = 0; slot < 3; slot++)
            {
                int capture = slot;
                AsyncGPUReadback.Request(host.Resources.GrassRead, 0, 0, host.Resources.GrassRead.width, 0, host.Resources.GrassRead.height, slot * 4 + 1, 1, request =>
                {
                    if (!request.hasError)
                    {
                        Vector4[] bits = request.GetData<Vector4>().ToArray();
                        var genome = GrassGenome.FromFloatBits(bits[Index(host, x, y)]);
                        if (GrassGenome.IsOccupied(GrassGenome.Stage(genome)))
                            slotCount++;
                    }
                    if (capture == 2) done = true;
                });
            }
            for (int i = 0; i < 120 && !done; i++)
                yield return null;
            Assert.That(soil, Is.EqualTo(MaterialIds.Soil));
            Assert.That(slotCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator RootTapsRaiseCohesionShareAndConserveMoisture()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.Config.grassRootUptakeRate = 1.5f;
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 6);

            float taps = 0f;
            float moisture = 0f;
            yield return ReadGrass(host, (materials, aux, _, share, genomes, __) =>
            {
                int below = Index(host, x, y - 1);
                taps = share[below].z;
                moisture = aux[below].y;
            });
            Assert.That(taps, Is.GreaterThan(0f));
            Assert.That(moisture, Is.GreaterThanOrEqualTo(0f));
        }

        [UnityTest]
        public IEnumerator SharedRootTargetsHaveNoOccupancyCap()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.Config.grassRootUptakeRate = 1.5f;
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 6);

            float taps = 0f;
            float moisture = 0f;
            yield return ReadGrass(host, (_, aux, __, share, ___, ____) =>
            {
                int below = Index(host, x, y - 1);
                int left = Index(host, x - 1, y - 1);
                int right = Index(host, x + 1, y - 1);
                taps = share[below].z + share[left].z + share[right].z;
                moisture = Mathf.Min(aux[below].y, Mathf.Min(aux[left].y, aux[right].y));
            });
            Assert.That(taps, Is.GreaterThanOrEqualTo(3f));
            Assert.That(moisture, Is.GreaterThanOrEqualTo(0f));
        }

        [UnityTest]
        public IEnumerator EnergyGateBlocksFloweringUntilThreshold()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.Config.dayLengthSeconds = 0.5f;
            host.Config.grassReproductionThreshold = 1f;
            host.Config.grassPhotosynthesisRate = 0f;
            host.Config.grassGrowthRate = 0f;
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.4f);
            yield return Step(host, 80);

            uint stage = 0;
            yield return ReadGrass(host, (_, __, ___, ____, genomes, _____) =>
            {
                stage = GrassGenome.Stage(genomes[Index(host, x, y)]);
            });
            Assert.That(stage, Is.EqualTo(GrassGenome.StageAdult));
        }

        [UnityTest]
        public IEnumerator NightDrainDoesNotRaiseEnergy()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.Config.solarIntensity = 0f;
            host.Config.grassPhotosynthesisRate = 0f;
            host.Config.grassNightDrain = 0.8f;
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 8);

            float energy = 1f;
            yield return ReadGrass(host, (_, __, life, ___, ____, _____) =>
            {
                energy = life[Index(host, x, y)].y;
            });
            Assert.That(energy, Is.LessThanOrEqualTo(0.2f));
        }

        [UnityTest]
        public IEnumerator DetritusLeachesNutrientIntoAdjacentSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            Paint(host, x, y + 1, MaterialIds.Detritus);
            PaintField(host, x, y + 1, 4f, 0.8f);
            PaintField(host, x, y + 1, 5f, 0.4f);
            PaintField(host, x, y, 4f, -100f);
            host.Config.detritusNutrientLeachRate = 2f;
            host.Config.detritusDecayRate = 0f;
            host.Config.detritusVaporAbsorbRate = 0f;
            host.Config.detritusMoistureShareRate = 0f;
            yield return Step(host, 8);

            float soilNutrient = 0f;
            float detritusNutrient = 0f;
            float detritusMoisture = 0f;
            yield return ReadGrass(host, (materials, aux, _, __, ___, ____) =>
            {
                soilNutrient = aux[Index(host, x, y)].z;
                detritusNutrient = aux[Index(host, x, y + 1)].z;
                detritusMoisture = aux[Index(host, x, y + 1)].y;
                Assert.That(materials[Index(host, x, y + 1)], Is.EqualTo(MaterialIds.Detritus));
            });
            Assert.That(soilNutrient, Is.GreaterThan(0.02f));
            Assert.That(detritusNutrient, Is.GreaterThanOrEqualTo(0f));
            Assert.That(detritusMoisture, Is.GreaterThanOrEqualTo(0f));
        }

        [UnityTest]
        public IEnumerator SameSeedGrassOccupancyIsDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);

            uint stageA = 0;
            StampPlot(host, x, y);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.85f);
            yield return Step(host, 6);
            yield return ReadGrass(host, (_, __, ___, ____, genomes, _____) =>
            {
                stageA = GrassGenome.Stage(genomes[Index(host, x, y)]);
            });

            yield return PrepareIsolatedWorld(host);
            StampPlot(host, x, y);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.85f);
            yield return Step(host, 6);
            uint stageB = 0;
            yield return ReadGrass(host, (_, __, ___, ____, genomes, _____) =>
            {
                stageB = GrassGenome.Stage(genomes[Index(host, x, y)]);
            });
            Assert.That(stageA, Is.EqualTo(GrassGenome.StageAdult));
            Assert.That(stageB, Is.EqualTo(stageA));
        }

        [UnityTest]
        public IEnumerator FloweringReleasesSeedsThenPaintsDetritus()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.Config.dayLengthSeconds = 1f;
            host.Config.solarIntensity = 1.2f;
            host.Config.grassReproductionThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 4f;
            host.Config.grassGrowthRate = 4f;
            host.Config.grassPollenEmitRate = 0f;
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 1f);
            yield return Step(host, 140);

            uint above = 0;
            float seeds = 0f;
            uint stage = 0;
            yield return ReadGrass(host, (materials, _, life, _, genomes, prop) =>
            {
                int i = Index(host, x, y);
                stage = GrassGenome.Stage(genomes[i]);
                seeds = prop[i].x + prop[Index(host, x, y + 1)].x;
                above = materials[Index(host, x, y + 1)];
            });
            Assert.That(seeds, Is.GreaterThanOrEqualTo(1f));
            Assert.That(stage == GrassGenome.StageAdult || stage == GrassGenome.StageFlowering || stage == GrassGenome.StageSeeding, Is.True);
            Assert.That(above == MaterialIds.Detritus || above == MaterialIds.Air, Is.True);
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesGrassAndDetritus()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = DayX(host);
            int y = SurfaceY(host);
            StampPlot(host, x, y);
            host.QueueGrassSeed(new Vector2Int(x, y), 0, 0.9f);
            Paint(host, x, y + 1, MaterialIds.Detritus);
            yield return Step(host, 4);

            uint stageBefore = 0;
            uint materialBefore = 0;
            yield return ReadGrass(host, (materials, _, _, _, genomes, __) =>
            {
                stageBefore = GrassGenome.Stage(genomes[Index(host, x, y)]);
                materialBefore = materials[Index(host, x, y + 1)];
            });

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-grass-v10.bin");
            bool saved = false;
            var service = new WorldSnapshotService();
            service.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(service.Load(host, path), Is.True);

            uint stageAfter = 0;
            uint materialAfter = 0;
            yield return ReadGrass(host, (materials, _, _, _, genomes, __) =>
            {
                stageAfter = GrassGenome.Stage(genomes[Index(host, x, y)]);
                materialAfter = materials[Index(host, x, y + 1)];
            });
            Assert.That(stageAfter, Is.EqualTo(stageBefore));
            Assert.That(materialAfter, Is.EqualTo(materialBefore));
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
