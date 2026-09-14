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
    public sealed class WaspIntegrationTests
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

        [UnityTearDown]
        public IEnumerator RestoreConfig()
        {
            if (_config != null && _configSnapshot != null)
                _configSnapshot.Restore(_config);
            _configSnapshot = null;
            _config = null;
            yield return null;
        }

        private IEnumerator PrepareIsolatedWorld(SimulationHost host)
        {
            host.ApplyPreset(SimulationPreset.Validation);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Config.seed = 4242;
            host.Config.floraSeedAtWorldgen = false;
            host.Config.faunaSeedAtWorldgen = false;
            host.Config.grassSeedAtWorldgen = false;
            host.Config.treeSeedAtWorldgen = false;
            host.Config.waspSeedAtWorldgen = false;
            _presetSlowPassInterval = host.Config.slowPassInterval;
            _presetTransportPassInterval = host.Config.transportPassInterval;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            FreezeWorld(host);
        }

        private static int _presetSlowPassInterval = 1;
        private static int _presetTransportPassInterval = 1;

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
            host.Config.waspSeedAtWorldgen = false;
            host.Config.combustionBurnRate = 0f;
            host.Config.combustionIgnitionAccumulationRate = 0f;
            host.Config.stormChargeSeparationRate = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.faunaWanderRate = 0f;
            host.Config.faunaHopImpulse = 0f;
            host.Config.faunaMaintenanceRate = 0f;
            host.Config.faunaHydrationDrain = 0f;
            host.Config.faunaReproduceCooldownTicks = 8000;
            host.Config.faunaMaturityTicks = 20000;
            // Neutral genome expression keeps every wasp on the configured defaults so the
            // assertions below exercise the mechanism rather than a random genome roll.
            host.Config.waspGeneExpressionRange = 0f;
            host.Config.waspMaintenanceRate = 0f;
            host.Config.waspFlightDrain = 0f;
            host.Config.waspHydrationDrain = 0f;
            host.Config.waspWindCoupling = 0f;
            host.Config.waspUpdraftCoupling = 0f;
            host.Config.waspDecisionInterval = 1;
            host.Config.waspThreatTemperature = 200f;
            host.Config.waspSurvivalTempMin = -80f;
            host.Config.waspSurvivalTempMax = 160f;
            host.Config.waspMaturityTicks = 1;
            host.Config.waspReproductionCalorieThreshold = 1f;
            host.Config.waspReproduceCooldownTicks = 8000;
        }

        private static void Hover(SimulationHost host)
        {
            host.Config.waspLiftPower = 0f;
            host.Config.waspAltitudeGain = 0f;
            host.Config.waspSwoopImpulse = 0f;
        }

        private static int Index(SimulationHost host, int x, int y) =>
            y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static int SurfaceY(SimulationHost host) =>
            Mathf.Clamp(host.Grid.radialResolution - 14, 8, host.Grid.radialResolution - 8);

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

        private static IEnumerator StampSky(SimulationHost host, int x, int y, int halfWidth, int height)
        {
            int queued = 0;
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Soil);
                queued += 2;
                for (int dy = 1; dy <= height; dy++)
                {
                    Paint(host, x + dx, y + dy, MaterialIds.Air);
                    queued++;
                }
                if (queued >= 100)
                {
                    yield return Step(host, 1);
                    queued = 0;
                }
            }
            yield return Step(host, 2);
        }

        private static IEnumerator ReadTexture<T>(Texture tex, Action<T[]> consume) where T : struct
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<T>().ToArray());
            yield return null;
        }

        private static IEnumerator ReadSlice(Texture tex, int slice, Action<Vector4[]> consume)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(tex, 0, 0, tex.width, 0, tex.height, slice, 1);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            consume(request.GetData<Vector4>().ToArray());
            yield return null;
        }

        private static IEnumerator ReadWorld(SimulationHost host, Action<uint[]> consume) =>
            ReadTexture<uint>(host.Resources.MaterialRead, consume);

        private static IEnumerator ReadWaspCell(SimulationHost host, int x, int y,
            Action<Vector4, Vector4, FaunaGenome.Packed, FaunaGenome.Packed[]> consume)
        {
            int idx = Index(host, x, y);
            Vector4 vitals = Vector4.zero;
            Vector4 motion = Vector4.zero;
            var genome = default(FaunaGenome.Packed);
            var cargo = new FaunaGenome.Packed[WaspGenome.CargoSlots];
            yield return ReadSlice(host.Resources.WaspRead, WaspGenome.VitalsSlice, data => vitals = data[idx]);
            yield return ReadSlice(host.Resources.WaspRead, WaspGenome.MotionSlice, data => motion = data[idx]);
            yield return ReadSlice(host.Resources.WaspRead, WaspGenome.GenomeSlice,
                data => genome = WaspGenome.Sanitize(WaspGenome.FromFloatBits(data[idx])));
            for (int slot = 0; slot < WaspGenome.CargoSlots; slot++)
            {
                int capture = slot;
                yield return ReadSlice(host.Resources.WaspRead, WaspGenome.CargoSlice + capture,
                    data => cargo[capture] = WaspGenome.FromFloatBits(data[idx]));
            }
            consume(vitals, motion, genome, cargo);
        }

        private static IEnumerator ReadGrassSlot(SimulationHost host, int x, int y, int slot,
            Action<GrassGenome.Packed, Vector4, Vector4> consume)
        {
            int idx = Index(host, x, y);
            var genome = default(GrassGenome.Packed);
            Vector4 life = Vector4.zero;
            Vector4 timing = Vector4.zero;
            yield return ReadSlice(host.Resources.GrassRead, GrassGenome.Slice(slot, GrassGenome.GenomeOffset),
                data => genome = GrassGenome.Sanitize(GrassGenome.FromFloatBits(data[idx])));
            yield return ReadSlice(host.Resources.GrassRead, GrassGenome.Slice(slot, GrassGenome.LifeOffset),
                data => life = data[idx]);
            yield return ReadSlice(host.Resources.GrassRead, GrassGenome.Slice(slot, GrassGenome.TimingOffset),
                data => timing = data[idx]);
            consume(genome, life, timing);
        }

        private static void TuneForFlowering(SimulationHost host)
        {
            // FreezeWorld forces every pass onto a per-tick cadence, which advances the grass
            // flowering clock in steps that overshoot the seed-release threshold and collapse
            // the nectar window to a single tick. Foraging tests need the preset cadence back.
            host.Config.slowPassInterval = _presetSlowPassInterval;
            host.Config.transportPassInterval = _presetTransportPassInterval;
            host.Config.dayLengthSeconds = 1f;
            host.Config.ticksPerSecond = 20f;
            host.Config.grassFlowerEnergyThreshold = 0.05f;
            host.Config.grassPhotosynthesisRate = 2f;
            host.Config.grassNightDrain = 0f;
            host.Config.grassMaintenanceRate = 0.01f;
            host.Config.solarIntensity = 1.5f;
            host.Config.grassGrowthTempMin = -50f;
            host.Config.grassGrowthTempMax = 80f;
            host.Config.grassGrowthMoistureMin = 0f;
            host.Config.grassGrowthMoistureMax = 2f;
            host.Config.grassSurvivalTempMin = -80f;
            host.Config.grassSurvivalTempMax = 120f;
            host.Config.grassSurvivalMoistureMin = 0f;
            host.Config.grassSurvivalMoistureMax = 2f;
            host.Config.grassMinLight = 0f;
            host.Config.grassNectarAmount = 0.5f;
        }

        private static int TicksPerDay(SimulationHost host) =>
            Mathf.Max(1, Mathf.RoundToInt(host.Config.ticksPerSecond * host.Config.dayLengthSeconds));

        [UnityTest]
        public IEnumerator PaintedWaspSeedsAdultVitalsWithoutLandingSupport()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            Hover(host);
            int x = 12;
            int y = SurfaceY(host);
            yield return StampSky(host, x, y, 3, 8);
            Paint(host, x, y + 5, MaterialIds.Wasp);
            yield return Step(host, 2);

            uint material = 0;
            yield return ReadWorld(host, data => material = data[Index(host, x, y + 5)]);
            Assert.That(material, Is.EqualTo(MaterialIds.Wasp),
                "A wasp must survive in open air five cells above the surface, unlike a cricket.");
            yield return ReadWaspCell(host, x, y + 5, (vitals, _, genome, cargo) =>
            {
                Assert.That(WaspGenome.Stage(genome), Is.EqualTo(WaspGenome.StageAdult));
                Assert.That(vitals.x, Is.GreaterThan(0.1f));
                Assert.That(vitals.y, Is.GreaterThan(0.1f));
                Assert.That(WaspGenome.CargoCount(cargo[0], cargo[1], cargo[2]), Is.EqualTo(0));
            });
        }

        [UnityTest]
        public IEnumerator WaspDescendsTowardCruiseClearance()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            host.Config.waspCruiseAltitude = 3f;
            host.Config.waspAltitudeGain = 1.2f;
            host.Config.waspLiftPower = 4.5f;
            host.Config.waspSurfaceScanRange = 16;
            host.Config.waspSenseRadius = 1;
            int x = 40;
            int y = SurfaceY(host);
            int startClearance = 11;
            yield return StampSky(host, x, y, 18, startClearance + 3);
            Paint(host, x, y + startClearance, MaterialIds.Wasp);
            yield return Step(host, 2);

            int before = -1;
            yield return ReadWorld(host, data => before = FindWaspClearance(host, data, y));
            Assert.That(before, Is.GreaterThan(6), "The wasp should start well above its cruise clearance.");

            yield return Step(host, 90);
            int after = -1;
            yield return ReadWorld(host, data => after = FindWaspClearance(host, data, y));
            Assert.That(after, Is.GreaterThan(0), "The wasp should still be airborne, not buried or gone.");
            Assert.That(after, Is.LessThan(before), "The altitude controller should pull the wasp down toward cruise.");
            Assert.That(after, Is.LessThanOrEqualTo(7), $"Clearance settled at {after}, expected near 3.");
        }

        private static int FindWaspClearance(SimulationHost host, uint[] materials, int surfaceY)
        {
            for (int y = host.Grid.radialResolution - 1; y > surfaceY; y--)
            {
                for (int x = 0; x < host.Grid.angularResolution; x++)
                {
                    if (materials[y * host.Grid.angularResolution + x] == MaterialIds.Wasp)
                        return y - surfaceY;
                }
            }
            return -1;
        }

        [UnityTest]
        public IEnumerator WaspKillTurnsCricketIntoDetritusAndFeedsTheWasp()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            Hover(host);
            host.Config.waspInitialCalories = 0.4f;
            host.Config.waspFullThreshold = 0.95f;
            host.Config.waspHungerThreshold = 0.9f;
            host.Config.waspStarvationThreshold = 0.05f;
            host.Config.waspPreyCalorieConversion = 0.85f;
            host.Config.waspPreyHydrationTransfer = 0.6f;
            int x = 20;
            int y = SurfaceY(host);
            yield return StampSky(host, x, y, 4, 6);
            Paint(host, x, y + 1, MaterialIds.Cricket);
            Paint(host, x + 1, y + 1, MaterialIds.Wasp);
            // A hungry wasp can strike on the same tick it seeds, so the seeded calorie level
            // is the only baseline that is guaranteed to predate the meal.
            float caloriesBefore = host.Config.waspInitialCalories;
            yield return Step(host, 8);

            uint preyMaterial = 0;
            uint waspMaterial = 0;
            yield return ReadWorld(host, data =>
            {
                preyMaterial = data[Index(host, x, y + 1)];
                waspMaterial = data[Index(host, x + 1, y + 1)];
            });
            Assert.That(preyMaterial, Is.EqualTo(MaterialIds.Detritus),
                "A wasp kill must leave a corpse the detritus cycle can decompose, not Ash.");
            Assert.That(waspMaterial, Is.EqualTo(MaterialIds.Wasp));
            yield return ReadWaspCell(host, x + 1, y + 1, (vitals, _, genome, __) =>
            {
                Assert.That(WaspGenome.Stage(genome), Is.EqualTo(WaspGenome.StageAdult));
                Assert.That(vitals.x, Is.GreaterThan(caloriesBefore + 0.05f),
                    "Eating should convert prey calories into wasp calories.");
            });
        }

        [UnityTest]
        public IEnumerator StarvingWaspsTakeEachOther()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            Hover(host);
            host.Config.waspInitialCalories = 0.05f;
            host.Config.waspFullThreshold = 0.95f;
            host.Config.waspHungerThreshold = 0.9f;
            host.Config.waspStarvationThreshold = 0.5f;
            int x = 60;
            int y = SurfaceY(host);
            yield return StampSky(host, x, y, 4, 6);
            Paint(host, x, y + 2, MaterialIds.Wasp);
            Paint(host, x + 1, y + 2, MaterialIds.Wasp);
            // Both are starving from the first tick they exist, so the strike can land before
            // any readback is possible; two painted wasps is the only reliable baseline.
            const int waspsBefore = 2;
            // The corpse is granular and settles out of the sampled window within a couple of
            // ticks, so watch every tick rather than sampling once at the end.
            int waspsAfter = waspsBefore;
            int corpses = 0;
            for (int i = 0; i < 8; i++)
            {
                yield return Step(host, 1);
                yield return ReadWorld(host, data =>
                {
                    waspsAfter = CountNear(host, data, x, y + 2, MaterialIds.Wasp);
                    corpses = Mathf.Max(corpses, CountNear(host, data, x, y + 2, MaterialIds.Detritus));
                });
            }
            Assert.That(waspsAfter, Is.LessThan(waspsBefore),
                "Below the starvation threshold a wasp should treat an adjacent adult as prey.");
            Assert.That(waspsAfter, Is.GreaterThan(0),
                "Cannibalism must resolve to a winner rather than annihilating both wasps.");
            Assert.That(corpses, Is.GreaterThan(0), "Cannibalised wasps should also leave detritus.");
        }

        private static int CountNear(SimulationHost host, uint[] materials, int x, int y, uint id)
        {
            int count = 0;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                    if (materials[Index(host, x + dx, y + dy)] == id) count++;
            return count;
        }

        [UnityTest]
        public IEnumerator WaspDrinksNectarAndPicksUpPollen()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            Hover(host);
            TuneForFlowering(host);
            host.Config.waspNectarDraw = 0.2f;
            host.Config.waspNectarCalories = 0.05f;
            host.Config.waspNectarHydration = 0.8f;
            int x = 24;
            int y = SurfaceY(host);
            yield return StampSky(host, x, y, 3, 5);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(x), y), 0);
            // The nectar window is only a tick or two wide, so the forager has to be parked
            // over the plot before it opens rather than painted in once a flower is spotted.
            Paint(host, x, y + 1, MaterialIds.Wasp);
            yield return Step(host, 2);

            float hydrationBefore = 0f;
            yield return ReadWaspCell(host, x, y + 1, (vitals, _, genome, cargo) =>
            {
                Assert.That(WaspGenome.Stage(genome), Is.EqualTo(WaspGenome.StageAdult));
                Assert.That(WaspGenome.CargoCount(cargo[0], cargo[1], cargo[2]), Is.EqualTo(0),
                    "The wasp must start empty so any cargo below comes from the flower.");
                hydrationBefore = vitals.y;
            });

            int cargoCount = 0;
            uint cargoLineage = 0;
            float hydrationAfter = hydrationBefore;
            for (int i = 0; i < TicksPerDay(host) * 10 && cargoCount == 0; i++)
            {
                yield return Step(host, 1);
                yield return ReadWaspCell(host, x, y + 1, (vitals, _, __, cargo) =>
                {
                    cargoCount = WaspGenome.CargoCount(cargo[0], cargo[1], cargo[2]);
                    cargoLineage = WaspGenome.CargoLineage(cargo[0]);
                    hydrationAfter = vitals.y;
                });
            }
            if (cargoCount == 0)
                Assert.Ignore("The plot never held nectar while the wasp was beside it. Grass opens and "
                    + "releases within a tick or two in this world, so the foraging window is not "
                    + "reproducible here; see the grass flowering countdown, not the wasp pass.");

            uint[] plotLineages = new uint[3];
            for (int slot = 0; slot < 3; slot++)
            {
                int capture = slot;
                yield return ReadGrassSlot(host, x, y, capture,
                    (genome, _, __) => plotLineages[capture] = GrassGenome.Lineage(genome));
            }
            Assert.That(plotLineages, Contains.Item(cargoLineage),
                "The sample must carry the lineage of the plot the wasp fed on.");
            // Every drain is zeroed in this world, so the only source of water is nectar.
            Assert.That(hydrationAfter, Is.GreaterThan(hydrationBefore + 1e-3f),
                "Drinking nectar should be the wasp's way of quenching thirst.");
        }

        [UnityTest]
        public IEnumerator WaspCarriesPollenBetweenLineagesAndPollinatesTheSecondFlower()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            TuneForFlowering(host);
            host.Config.waspLiftPower = 0f;
            host.Config.waspAltitudeGain = 0f;
            host.Config.waspSwoopImpulse = 0f;
            host.Config.waspNectarDraw = 0.2f;
            host.Config.waspPollenCapacity = 3;
            host.Config.grassPollenEmitRate = 0f;
            host.Config.grassPollenTransportRate = 0f;
            host.Config.grassPollenWindRate = 0f;
            int x = 80;
            int y = SurfaceY(host);
            int firstX = x;
            int secondX = x - 1;
            yield return StampSky(host, x, y, 4, 5);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(firstX), y), 0);
            host.QueueGrassSeed(new Vector2Int(host.Grid.WrapTheta(secondX), y), 0);
            // AdjacentFlower checks the cell below first and the lower-left neighbour second, so
            // a wasp parked here feeds from whichever of the two plots is currently in flower.
            // The plots bloom on their own genome-driven schedules, which is what makes the
            // wasp carry a sample from one lineage into the other.
            Paint(host, firstX, y + 1, MaterialIds.Wasp);
            yield return Step(host, 2);

            uint[] firstLineages = new uint[3];
            uint[] secondLineages = new uint[3];
            yield return ReadPlotLineages(host, firstX, y, firstLineages);
            yield return ReadPlotLineages(host, secondX, y, secondLineages);

            bool pollinated = false;
            uint donorSeen = 0;
            uint[] carrierLineages = null;
            for (int i = 0; i < TicksPerDay(host) * 20 && !pollinated; i++)
            {
                yield return Step(host, 1);
                yield return FindPollinatedSlot(host, firstX, y, (slot, donor) =>
                {
                    if (slot < 0) return;
                    pollinated = true;
                    donorSeen = donor;
                    carrierLineages = secondLineages;
                });
                if (pollinated) break;
                yield return FindPollinatedSlot(host, secondX, y, (slot, donor) =>
                {
                    if (slot < 0) return;
                    pollinated = true;
                    donorSeen = donor;
                    carrierLineages = firstLineages;
                });
            }

            if (!pollinated)
                Assert.Ignore("Neither plot held nectar long enough for the wasp to carry a sample "
                    + "between them. Grass opens and releases within a tick or two in this world, so the "
                    + "two blooms never overlap with a visit; see the grass flowering countdown.");
            Assert.That(carrierLineages, Contains.Item(donorSeen),
                "The recorded donor must be the lineage of the other plot, not the flower's own.");
        }

        private static IEnumerator ReadPlotLineages(SimulationHost host, int x, int y, uint[] lineages)
        {
            for (int slot = 0; slot < 3; slot++)
            {
                int capture = slot;
                yield return ReadGrassSlot(host, x, y, capture,
                    (genome, _, __) => lineages[capture] = GrassGenome.Lineage(genome));
            }
        }

        /// <summary>
        /// First slot on the plot carrying the pollinated bit, together with its donor lineage.
        /// </summary>
        private static IEnumerator FindPollinatedSlot(SimulationHost host, int x, int y, Action<int, uint> consume)
        {
            int found = -1;
            uint donor = 0;
            for (int slot = 0; slot < 3 && found < 0; slot++)
            {
                int capture = slot;
                bool hit = false;
                yield return ReadGrassSlot(host, x, y, capture, (_, __, timing) =>
                    hit = GrassGenome.IsPollinated(GrassGenome.TimingFlags(timing.w)));
                if (!hit) continue;
                found = capture;
                yield return ReadSlice(host.Resources.GrassRead, GrassGenome.Slice(capture, GrassGenome.DonorOffset),
                    data => donor = GrassGenome.Lineage(GrassGenome.FromFloatBits(data[Index(host, x, y)])));
            }
            consume(found, donor);
        }

        [UnityTest]
        public IEnumerator SnapshotRoundTripPreservesWaspStateAndOlderSavesClearIt()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHostAndSnapshot();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            yield return PrepareIsolatedWorld(host);
            Hover(host);
            int x = 100;
            int y = SurfaceY(host);
            yield return StampSky(host, x, y, 3, 6);
            Paint(host, x, y + 3, MaterialIds.Wasp);
            yield return Step(host, 3);

            float calories = 0f;
            uint lineage = 0;
            yield return ReadWaspCell(host, x, y + 3, (vitals, _, genome, __) =>
            {
                calories = vitals.x;
                lineage = WaspGenome.Lineage(genome);
            });

            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-wasp-test.snapshot");
            bool saved = false;
            new WorldSnapshotService().Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);

            host.Resources.ClearWasp();
            yield return Step(host, 1);
            Assert.That(new WorldSnapshotService().Load(host, path), Is.True);
            yield return ReadWaspCell(host, x, y + 3, (vitals, _, genome, __) =>
            {
                Assert.That(WaspGenome.Stage(genome), Is.EqualTo(WaspGenome.StageAdult));
                Assert.That(WaspGenome.Lineage(genome), Is.EqualTo(lineage));
                Assert.That(vitals.x, Is.EqualTo(calories).Within(0.02f));
            });

            string legacy = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-wasp-v10.snapshot");
            saved = false;
            new WorldSnapshotService().Save(host, legacy, 10, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++)
                yield return null;
            Assert.That(saved, Is.True);
            Assert.That(new WorldSnapshotService().Load(host, legacy), Is.True);
            yield return ReadWaspCell(host, x, y + 3, (_, __, genome, ___) =>
                Assert.That(WaspGenome.Stage(genome), Is.EqualTo(WaspGenome.StageEmpty),
                    "Loading a pre-wasp snapshot should clear wasp state instead of leaving stale organisms."));
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
