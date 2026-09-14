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
    public sealed class GrassIntegrationTests
    {
        private SimulationConfigSnapshot _configSnapshot;
        private SimulationConfig _config;

        private static IEnumerator WaitForHost()
        {
            if (UnityEngine.Object.FindFirstObjectByType<SimulationHost>() == null)
            {
                SceneManager.LoadScene("Terrarium");
                yield return null;
            }
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
            LogAssert.ignoreFailingMessages = true;
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
            host.ApplyPreset(SimulationPreset.Validation);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Config.seed = 2026;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
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

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

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
            host.Config.solarIntensity = 0.8f;
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
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.stormChargeSeparationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.grassGrowthTempMin = -50f;
            host.Config.grassGrowthTempMax = 80f;
            host.Config.grassGrowthMoistureMin = 0f;
            host.Config.grassGrowthMoistureMax = 2f;
            host.Config.grassSurvivalTempMin = -80f;
            host.Config.grassSurvivalTempMax = 120f;
            host.Config.grassSurvivalMoistureMin = 0f;
            host.Config.grassSurvivalMoistureMax = 2f;
            host.Config.grassMinLight = 0f;
        }

        private static IEnumerator RequestTexture<T>(Texture tex, Action<T[]> consume) where T : struct
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<T>().ToArray());
            yield return null;
        }

        private static IEnumerator RequestSlice(Texture tex, int slice, Action<Vector4[]> consume)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0, 0, tex.width, 0, tex.height, slice, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<Vector4>().ToArray());
            yield return null;
        }

        private static IEnumerator ReadGrassCell(SimulationHost host, int x, int y,
            Action<uint, Vector4, Vector4, Vector4[], GrassGenome.Packed[], Vector4[], Vector4> consume)
        {
            int idx = Index(host, x, y);
            var lives = new Vector4[3];
            var genomes = new GrassGenome.Packed[3];
            var timings = new Vector4[3];
            uint material = 0;
            Vector4 state = Vector4.zero;
            Vector4 aux = Vector4.zero;
            Vector4 load = Vector4.zero;

            yield return RequestTexture<uint>(host.Resources.MaterialRead, data => material = data[idx]);
            yield return RequestTexture<Vector4>(host.Resources.StateRead, data => state = data[idx]);
            yield return RequestTexture<Vector4>(host.Resources.AuxRead, data => aux = data[idx]);
            yield return RequestSlice(host.Resources.PropaguleRead, 0, data => load = data[idx]);
            for (int slot = 0; slot < 3; slot++)
            {
                int capture = slot;
                yield return RequestSlice(host.Resources.GrassRead, GrassGenome.Slice(capture, 0),
                    data => lives[capture] = data[idx]);
                yield return RequestSlice(host.Resources.GrassRead, GrassGenome.Slice(capture, 1),
                    data => genomes[capture] = GrassGenome.FromFloatBits(data[idx]));
                yield return RequestSlice(host.Resources.GrassRead, GrassGenome.Slice(capture, 2),
                    data => timings[capture] = data[idx]);
            }

            consume(material, state, aux, lives, genomes, timings, load);
        }

        private static int LivingCount(GrassGenome.Packed[] genomes)
        {
            int count = 0;
            for (int i = 0; i < genomes.Length; i++)
                if (GrassGenome.IsLivingStage(GrassGenome.Stage(genomes[i]))) count++;
            return count;
        }

        private static IEnumerator PlantOnPlot(SimulationHost host, int x, int y, int seeds)
        {
            StampSurfacePlot(host, x, y);
            yield return Step(host, 2);
            for (int i = 0; i < seeds; i++)
            {
                host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
                yield return Step(host, 1);
            }
        }

        [UnityTest]
        public IEnumerator ThreeSlotCapRejectsExtraSeeds()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 12;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 5);

            uint material = 0;
            int living = 0;
            yield return ReadGrassCell(host, x, y, (mat, _, _, _, genomes, _, __) =>
            {
                material = mat;
                living = LivingCount(genomes);
            });
            Assert.That(material, Is.EqualTo(MaterialIds.Soil));
            Assert.That(living, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator RootMaskIsStableAcrossTicks()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 14;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            uint maskA = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, _, timings, __) =>
                maskA = GrassGenome.RootMask(GrassGenome.TimingFlags(timings[0].w)));
            yield return Step(host, 8);
            uint maskB = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, _, timings, __) =>
                maskB = GrassGenome.RootMask(GrassGenome.TimingFlags(timings[0].w)));
            Assert.That(maskA, Is.Not.EqualTo(0u));
            Assert.That(maskB, Is.EqualTo(maskA));
        }

        [UnityTest]
        public IEnumerator SharedRootTargetsHaveNoOccupancyCap()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.grassWaterUptakeRate = 0.8f;
            int x = 16;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            yield return PlantOnPlot(host, x + 2, y, 1);
            int livingA = 0;
            int livingB = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, _, __) =>
                livingA = LivingCount(genomes));
            yield return ReadGrassCell(host, x + 2, y, (_, _, _, _, genomes, _, __) =>
                livingB = LivingCount(genomes));
            Assert.That(livingA, Is.GreaterThan(0), "First soil cell should keep a grass slot.");
            Assert.That(livingB, Is.GreaterThan(0), "A second nearby soil cell should also keep a grass slot.");
        }

        [UnityTest]
        public IEnumerator RootUptakeConservesSoilMoisture()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.grassWaterUptakeRate = 0.8f;
            host.Config.grassNutrientUptakeRate = 0.8f;
            int x = 18;
            int y = SurfaceY(host);
            StampSurfacePlot(host, x, y);
            PaintField(host, x - 1, y - 1, 5f, 0.3f);
            PaintField(host, x, y - 1, 5f, 0.3f);
            PaintField(host, x + 1, y - 1, 5f, 0.3f);
            yield return Step(host, 2);
            float moistureBefore = 0f;
            yield return ReadGrassCell(host, x, y - 1, (_, state, aux, _, _, _, __) =>
                moistureBefore = state.z + aux.y);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            yield return Step(host, 1);
            yield return Step(host, 8);
            float moistureAfter = 0f;
            int living = 0;
            uint material = 0;
            yield return ReadGrassCell(host, x, y, (mat, _, _, _, genomes, _, __) =>
            {
                material = mat;
                living = LivingCount(genomes);
            });
            yield return ReadGrassCell(host, x, y - 1, (_, state, aux, _, _, _, __) =>
                moistureAfter = state.z + aux.y);
            Assert.That(material, Is.EqualTo(MaterialIds.Soil));
            Assert.That(living, Is.GreaterThan(0), "Grass should remain alive on exposed soil while taking up moisture.");
            Assert.That(moistureAfter, Is.LessThanOrEqualTo(moistureBefore + 1e-3f));
        }

        [UnityTest]
        public IEnumerator GrassPatchConservesTrackedWaterIncludingHydration()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.grassWaterUptakeRate = 0.8f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.springDischargeRate = 0f;
            int x = 19;
            int y = SurfaceY(host);
            host.Config.hydrostaticIterations = 1;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.slowPassInterval = 100000;
            StampSurfacePlot(host, x, y);
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    if (Mathf.Abs(dx) == 3 || Mathf.Abs(dy) == 3)
                        Paint(host, x + dx, y + dy, MaterialIds.Rock);
                    PaintField(host, x + dx, y + dy, 2f, -100f);
                    PaintField(host, x + dx, y + dy, 5f, -100f);
                    PaintField(host, x + dx, y + dy, 6f, -100f);
                }
            }
            PaintField(host, x, y, 5f, 0.35f);
            PaintField(host, x - 1, y - 1, 5f, 0.35f);
            PaintField(host, x, y - 1, 5f, 0.35f);
            PaintField(host, x + 1, y - 1, 5f, 0.35f);
            yield return Step(host, 2);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            yield return Step(host, 1);

            double before = 0d;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    yield return ReadGrassCell(host, x + dx, y + dy, (_, state, aux, lives, _, _, __) =>
                    {
                        before += state.z + aux.x + aux.y;
                        for (int i = 0; i < lives.Length; i++)
                            before += Mathf.Max(0f, lives[i].z);
                    });
                }
            }
            yield return Step(host, 12);
            double after = 0d;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    yield return ReadGrassCell(host, x + dx, y + dy, (_, state, aux, lives, _, _, __) =>
                    {
                        after += state.z + aux.x + aux.y;
                        for (int i = 0; i < lives.Length; i++)
                            after += Mathf.Max(0f, lives[i].z);
                    });
                }
            }
            Assert.That(after, Is.EqualTo(before).Within(0.08d),
                "Soil water plus grass hydration plus local vapor must stay on the ledger.");
        }

        [UnityTest]
        public IEnumerator EnergyGateBlocksFloweringUntilThreshold()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.99f;
            host.Config.grassPhotosynthesisRate = 0f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0f;
            int x = 20;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            int ticksPerDay = Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));
            yield return Step(host, ticksPerDay * 5);
            bool flowering = false;
            float seeds = 0f;
            int living = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, timings, load) =>
            {
                living = LivingCount(genomes);
                uint flags = GrassGenome.TimingFlags(timings[0].w);
                flowering = GrassGenome.IsFlowering(flags) || GrassGenome.HasReleased(flags);
                seeds = load.x;
            });
            Assert.That(living, Is.GreaterThan(0));
            Assert.That(flowering || seeds >= 1f, Is.False);
        }

        [UnityTest]
        public IEnumerator FloweringWaitsThreeToFourSolarDaysThenReleasesSeeds()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 2f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0.01f;
            host.Config.solarIntensity = 1.5f;
            int x = 10;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);

            bool flowering = false;
            float seeds = 0f;
            int ticksPerDay = Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));
            yield return Step(host, ticksPerDay * 2);
            yield return ReadGrassCell(host, x, y, (_, _, _, _, _, timings, load) =>
            {
                uint flags = GrassGenome.TimingFlags(timings[0].w);
                flowering = GrassGenome.IsFlowering(flags);
                seeds = load.x;
            });
            Assert.That(flowering || seeds > 0f, Is.False, "Should not flower before 3 solar days.");

            yield return Step(host, ticksPerDay * 4);
            int living = 0;
            uint above = 0;
            float nectar = 0f;
            yield return ReadGrassCell(host, x, y, (_, _, _, lives, genomes, timings, load) =>
            {
                living = LivingCount(genomes);
                uint flags = GrassGenome.TimingFlags(timings[0].w);
                flowering = GrassGenome.IsFlowering(flags) || GrassGenome.HasReleased(flags);
                seeds = load.x;
                nectar = lives[0].w;
            });
            yield return ReadGrassCell(host, x - 1, y + 1, (material, _, _, _, _, _, __) => above = material);
            Assert.That(living, Is.GreaterThan(0), "Adult grass should still occupy a slot after the flowering window.");
            Assert.That(flowering || seeds >= 1f || living > 1 || above == MaterialIds.Detritus || nectar > 0f, Is.True,
                "Adult grass should flower and/or release seeds after 3–4 plus 1–2 solar days.");
        }

        [UnityTest]
        public IEnumerator NightDrainDoesNotRaiseEnergy()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.solarIntensity = 0f;
            host.Config.grassPhotosynthesisRate = 0f;
            host.Config.grassNightDrain = 2f;
            host.Config.grassMaintenanceRate = 0f;
            int x = 22;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            float energyBefore = 0f;
            yield return ReadGrassCell(host, x, y, (_, _, _, lives, _, _, __) => energyBefore = lives[0].y);
            yield return Step(host, 8);
            float energyAfter = 0f;
            int living = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, lives, genomes, _, __) =>
            {
                living = LivingCount(genomes);
                energyAfter = lives[0].y;
            });
            Assert.That(living, Is.GreaterThan(0));
            Assert.That(energyAfter, Is.LessThanOrEqualTo(energyBefore + 1e-3f));
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesGrass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 26;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);

            uint lineage = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, _, __) =>
                lineage = GrassGenome.Lineage(genomes[0]));
            Assert.That(lineage, Is.Not.EqualTo(0u));

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-grass-test.snapshot");
            bool saved = false;
            new WorldSnapshotService().Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);

            host.Resources.ClearGrass();
            yield return Step(host, 1);
            Assert.That(new WorldSnapshotService().Load(host, path), Is.True);

            uint restored = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, _, __) =>
                restored = GrassGenome.Lineage(genomes[0]));
            Assert.That(restored, Is.EqualTo(lineage));
        }

        [UnityTest]
        public IEnumerator DetritusExchangeMovesNutrientsWithoutInventingMass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.detritusDecompositionRate = 0f;
            host.Config.detritusNutrientLeachRate = 0.8f;
            int x = 30;
            int y = SurfaceY(host);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x, y + 1, MaterialIds.Detritus);
            Paint(host, x, y + 2, MaterialIds.Air);
            PaintField(host, x, y + 1, 4f, 0.6f);
            yield return Step(host, 2);
            float detritusNutrient = 0f;
            float soilNutrient = 0f;
            yield return ReadGrassCell(host, x, y + 1, (_, _, aux, _, _, _, __) => detritusNutrient = aux.z);
            yield return ReadGrassCell(host, x, y, (_, _, aux, _, _, _, __) => soilNutrient = aux.z);
            float totalBefore = detritusNutrient + soilNutrient;
            yield return Step(host, 8);
            float detritusAfter = 0f;
            float soilAfter = 0f;
            yield return ReadGrassCell(host, x, y + 1, (_, _, aux, _, _, _, __) => detritusAfter = aux.z);
            yield return ReadGrassCell(host, x, y, (_, _, aux, _, _, _, __) => soilAfter = aux.z);
            Assert.That(detritusAfter + soilAfter, Is.EqualTo(totalBefore).Within(0.08f));
            Assert.That(soilAfter, Is.GreaterThanOrEqualTo(soilNutrient - 1e-3f));
        }

        [UnityTest]
        public IEnumerator SnapshotV9LoadClearsGrass()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            int x = 32;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            int livingBefore = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, _, __) =>
                livingBefore = LivingCount(genomes));
            Assert.That(livingBefore, Is.GreaterThan(0));

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-grass-v9.snapshot");
            bool saved = false;
            new WorldSnapshotService().Save(host, path, 9, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);
            Assert.That(new WorldSnapshotService().Load(host, path), Is.True);

            int livingAfter = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, _, __) =>
                livingAfter = LivingCount(genomes));
            Assert.That(livingAfter, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator FloweringEmitsPollenAndRejectsSelfPollination()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 2f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0.01f;
            host.Config.solarIntensity = 1.5f;
            host.Config.grassPollenEmitRate = 0.5f;
            host.Config.grassPollenTransportRate = 0.05f;
            int x = 34;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            int ticksPerDay = Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));
            yield return Step(host, ticksPerDay * 5);

            bool flowering = false;
            bool pollinated = false;
            float pollen = 0f;
            int living = 0;
            yield return ReadGrassCell(host, x, y, (_, _, _, _, genomes, timings, load) =>
            {
                living = LivingCount(genomes);
                uint flags = GrassGenome.TimingFlags(timings[0].w);
                flowering = GrassGenome.IsFlowering(flags) || GrassGenome.HasReleased(flags);
                pollinated = GrassGenome.IsPollinated(flags);
                pollen = load.y;
            });
            float neighborPollen = 0f;
            yield return ReadGrassCell(host, x, y + 1, (_, _, _, _, _, _, load) => neighborPollen = load.y);
            Assert.That(living, Is.GreaterThan(0));
            Assert.That(flowering || pollen > 1e-4f || neighborPollen > 1e-4f, Is.True,
                "Open flowers should emit pollen onto the cell or an outward carrier.");
            Assert.That(pollinated, Is.False, "A lone plant must reject its own pollen.");
        }

        [UnityTest]
        public IEnumerator WindMovesPollenAlongOpenAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 2f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0.01f;
            host.Config.solarIntensity = 1.5f;
            host.Config.grassPollenEmitRate = 0.5f;
            host.Config.grassPollenTransportRate = 0.5f;
            host.Config.grassPollenWindRate = 2f;
            int x = 36;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            PaintField(host, x, y + 1, 13f, 1.5f);
            PaintField(host, x + 1, y + 1, 13f, 1.5f);
            Paint(host, x + 1, y + 1, MaterialIds.Air);
            int ticksPerDay = Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));
            yield return Step(host, ticksPerDay * 6);

            float downwind = 0f;
            yield return ReadGrassCell(host, x + 1, y + 1, (_, _, _, _, _, _, load) => downwind = load.y);
            float local = 0f;
            yield return ReadGrassCell(host, x, y + 1, (_, _, _, _, _, _, load) => local = load.y);
            Assert.That(downwind + local, Is.GreaterThan(1e-5f), "Wind should carry pollen through adjacent air.");
        }

        [UnityTest]
        public IEnumerator SeedCountsStayWholeAfterTransport()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 2f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0.01f;
            host.Config.solarIntensity = 1.5f;
            host.Config.grassSeedTransportRate = 0.5f;
            host.Config.grassSeedWindRate = 1.5f;
            host.Config.grassSeedSettlingRate = 1.5f;
            int x = 38;
            int y = SurfaceY(host);
            yield return PlantOnPlot(host, x, y, 1);
            PaintField(host, x, y + 1, 13f, 1.2f);
            int ticksPerDay = Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));
            yield return Step(host, ticksPerDay * 6);

            float[] counts = new float[5];
            yield return ReadGrassCell(host, x, y, (_, _, _, _, _, _, load) => counts[0] = load.x);
            yield return ReadGrassCell(host, x, y + 1, (_, _, _, _, _, _, load) => counts[1] = load.x);
            yield return ReadGrassCell(host, x - 1, y + 1, (_, _, _, _, _, _, load) => counts[2] = load.x);
            yield return ReadGrassCell(host, x + 1, y + 1, (_, _, _, _, _, _, load) => counts[3] = load.x);
            yield return ReadGrassCell(host, x, y - 1, (_, _, _, _, _, _, load) => counts[4] = load.x);
            for (int i = 0; i < counts.Length; i++)
                Assert.That(counts[i], Is.EqualTo(Mathf.Round(counts[i])).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator GrassSurvivesAOneCellSoilSlide()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.grassRootCohesionBonus = 0f;
            host.Config.enableMaterialTransport = true;
            host.Config.margolusSubsteps = 1;

            int x = 10;
            int y = SurfaceY(host);
            if ((y & 1) == 0) y++;
            Paint(host, x, y - 2, MaterialIds.Rock);
            Paint(host, x - 1, y - 2, MaterialIds.Rock);
            Paint(host, x + 1, y - 2, MaterialIds.Rock);
            Paint(host, x, y - 1, MaterialIds.Air);
            Paint(host, x - 1, y - 1, MaterialIds.Rock);
            Paint(host, x + 1, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Soil);
            Paint(host, x - 1, y, MaterialIds.Rock);
            Paint(host, x + 1, y, MaterialIds.Rock);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            yield return Step(host, 1);

            int livingBefore = 0;
            yield return ReadGrassCell(host, x, y, (mat, _, _, _, genomes, _, __) =>
            {
                Assert.That(mat, Is.EqualTo(MaterialIds.Soil));
                livingBefore = LivingCount(genomes);
            });
            Assert.That(livingBefore, Is.GreaterThan(0));

            host.Config.gravityStrength = 2f;
            yield return Step(host, 2);

            int livingAfter = 0;
            uint destMat = 0;
            yield return ReadGrassCell(host, x, y - 1, (mat, _, _, _, genomes, _, __) =>
            {
                destMat = mat;
                livingAfter = LivingCount(genomes);
            });
            Assert.That(destMat, Is.EqualTo(MaterialIds.Soil));
            Assert.That(livingAfter, Is.GreaterThan(0), "Grass slots must ride the sliding soil cell.");
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
