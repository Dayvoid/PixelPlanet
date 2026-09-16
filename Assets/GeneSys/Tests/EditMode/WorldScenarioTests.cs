using GeneSys.Configuration;
using GeneSys.Simulation.Scenarios;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class WorldScenarioTests
    {
        [Test]
        public void SandboxResolveCopiesMenuFieldsAndDoesNotMutateConfig()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.worldScenario = WorldScenario.Sandbox;
            config.targetOceanCoverage = 0.5f;
            config.initialGroundwaterSaturation = 0.65f;
            config.initialAtmosphericHumidity = 0.7f;
            config.borderNoise = 0.035f;
            config.soilRatio = 0.025f;
            config.clayDepositCount = 20;
            config.mycologyInitialSporeLoad = 0.08f;

            WorldScenarioWorldgen worldgen = WorldScenarioOverlays.Resolve(config);

            Assert.That(worldgen.TargetOceanCoverage, Is.EqualTo(0.5f));
            Assert.That(worldgen.InitialGroundwaterSaturation, Is.EqualTo(0.65f));
            Assert.That(worldgen.SkipBiologySeed, Is.False);
            AssertUnchangedMenuFields(config);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void WorldWideWaterOverlaysOceanAndGroundwaterWithoutMutatingConfig()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.worldScenario = WorldScenario.WorldWideWater;
            config.targetOceanCoverage = 0.5f;
            config.initialGroundwaterSaturation = 0.65f;
            config.initialAtmosphericHumidity = 0.7f;
            config.borderNoise = 0.035f;
            config.soilRatio = 0.025f;
            config.clayDepositCount = 20;
            config.mycologyInitialSporeLoad = 0.08f;

            WorldScenarioWorldgen worldgen = WorldScenarioOverlays.Resolve(config);

            Assert.That(worldgen.TargetOceanCoverage, Is.EqualTo(WorldScenarioOverlays.WorldWideWaterOceanCoverage));
            Assert.That(worldgen.InitialGroundwaterSaturation, Is.EqualTo(WorldScenarioOverlays.WorldWideWaterGroundwaterSaturation));
            Assert.That(worldgen.InitialAtmosphericHumidity, Is.EqualTo(0.7f));
            Assert.That(worldgen.SkipBiologySeed, Is.False);
            AssertUnchangedMenuFields(config);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void CometStruckMoonOverlaysDryBarrenWorldWithoutMutatingConfig()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.worldScenario = WorldScenario.CometStruckMoon;
            config.targetOceanCoverage = 0.5f;
            config.initialGroundwaterSaturation = 0.65f;
            config.initialAtmosphericHumidity = 0.7f;
            config.borderNoise = 0.035f;
            config.soilRatio = 0.025f;
            config.clayDepositCount = 20;
            config.mycologyInitialSporeLoad = 0.08f;

            WorldScenarioWorldgen worldgen = WorldScenarioOverlays.Resolve(config);

            Assert.That(worldgen.TargetOceanCoverage, Is.EqualTo(WorldScenarioOverlays.CometMoonOceanCoverage));
            Assert.That(worldgen.InitialGroundwaterSaturation, Is.EqualTo(WorldScenarioOverlays.CometMoonGroundwaterSaturation));
            Assert.That(worldgen.InitialAtmosphericHumidity, Is.EqualTo(WorldScenarioOverlays.CometMoonHumidity));
            Assert.That(worldgen.BorderNoise, Is.EqualTo(WorldScenarioOverlays.CometMoonBorderNoise));
            Assert.That(worldgen.SoilRatio, Is.EqualTo(WorldScenarioOverlays.CometMoonSoilRatio));
            Assert.That(worldgen.ClayDepositCount, Is.EqualTo(WorldScenarioOverlays.CometMoonClayDepositCount));
            Assert.That(worldgen.MycologyInitialSporeLoad, Is.EqualTo(WorldScenarioOverlays.CometMoonMycologySporeLoad));
            Assert.That(worldgen.SkipBiologySeed, Is.True);
            AssertUnchangedMenuFields(config);
            Object.DestroyImmediate(config);
        }

        [Test]
        public void TickCueFiresOnceAtTargetTickAndSyncSkipsPastBeats()
        {
            int fired = 0;
            var timeline = new ScenarioTimeline();
            timeline.Add(new TickCue(500, _ => fired++));

            timeline.Advance(null, 499);
            Assert.That(fired, Is.EqualTo(0));

            timeline.Advance(null, 500);
            Assert.That(fired, Is.EqualTo(1));

            timeline.Advance(null, 500);
            Assert.That(fired, Is.EqualTo(1));

            timeline.Reset();
            timeline.Sync(505);
            timeline.Advance(null, 500);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ConditionCueFiresWhenPredicateIsMet()
        {
            int fired = 0;
            var timeline = new ScenarioTimeline();
            timeline.Add(new ConditionCue((_, tick) => tick >= 3, _ => fired++));

            timeline.Advance(null, 2);
            Assert.That(fired, Is.EqualTo(0));
            timeline.Advance(null, 3);
            Assert.That(fired, Is.EqualTo(1));
            timeline.Advance(null, 4);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void CometCatalogRegistersApproachAndImpactCues()
        {
            ScenarioTimeline timeline = WorldScenarioDirector.CreateTimeline(WorldScenario.CometStruckMoon);
            Assert.That(timeline.CueCount, Is.EqualTo(WorldScenarioDirector.CometApproachSteps + 1));
            Assert.That(WorldScenarioDirector.CreateTimeline(WorldScenario.Sandbox).CueCount, Is.EqualTo(0));
            Assert.That(WorldScenarioDirector.CreateTimeline(WorldScenario.WorldWideWater).CueCount, Is.EqualTo(0));
        }

        private static void AssertUnchangedMenuFields(SimulationConfig config)
        {
            Assert.That(config.targetOceanCoverage, Is.EqualTo(0.5f));
            Assert.That(config.initialGroundwaterSaturation, Is.EqualTo(0.65f));
            Assert.That(config.initialAtmosphericHumidity, Is.EqualTo(0.7f));
            Assert.That(config.borderNoise, Is.EqualTo(0.035f));
            Assert.That(config.soilRatio, Is.EqualTo(0.025f));
            Assert.That(config.clayDepositCount, Is.EqualTo(20));
            Assert.That(config.mycologyInitialSporeLoad, Is.EqualTo(0.08f));
        }
    }
}
