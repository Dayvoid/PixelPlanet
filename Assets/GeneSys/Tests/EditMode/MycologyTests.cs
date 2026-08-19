using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class MycologyTests
    {
        [Test]
        public void MycologyDefaultsAreConfiguredAndOrdered()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.mycologyInitialSporeLoad, Is.GreaterThan(0f));
            Assert.That(config.mycologyRareStrainChance, Is.InRange(0f, 0.2f));
            Assert.That(config.mycologyAirTransportRate, Is.GreaterThan(0f));
            Assert.That(config.mycologyWaterTransportRate, Is.GreaterThan(0f));
            Assert.That(config.mycologyDiffusionRate, Is.GreaterThan(0f));
            Assert.That(config.mycologySettlingRate, Is.GreaterThan(0f));
            Assert.That(config.mycologySporulationRate, Is.GreaterThan(0f));
            Assert.That(config.mycologyGrowthRate, Is.GreaterThan(0f));
            Assert.That(config.mycologyDecayRate, Is.GreaterThan(0f));
            Assert.That(config.mycologyGrowthTempMin, Is.LessThan(config.mycologyGrowthTempMax));
            Assert.That(config.mycologySurvivalTempMin, Is.LessThanOrEqualTo(config.mycologyGrowthTempMin));
            Assert.That(config.mycologySurvivalTempMax, Is.GreaterThanOrEqualTo(config.mycologyGrowthTempMax));
            Assert.That(config.mycologyGrowthMoistureMin, Is.LessThan(config.mycologyGrowthMoistureMax));
            Assert.That(config.mycologySurvivalMoistureMin, Is.LessThanOrEqualTo(config.mycologyGrowthMoistureMin));
            Assert.That(config.mycologySurvivalMoistureMax, Is.GreaterThanOrEqualTo(config.mycologyGrowthMoistureMax));
            Assert.That(config.mycologyElectricalTolerance, Is.GreaterThan(0f));
            Assert.That(config.mycologyTraitEffectStrength, Is.InRange(0f, 1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void MycologyOnValidateEnforcesSurvivalOutsideGrowth()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.mycologyGrowthTempMin = 20f;
            config.mycologyGrowthTempMax = 10f;
            config.mycologySurvivalTempMin = 12f;
            config.mycologySurvivalTempMax = 15f;
            config.mycologyGrowthMoistureMin = 0.8f;
            config.mycologyGrowthMoistureMax = 0.2f;
            config.mycologySurvivalMoistureMin = 0.4f;
            config.mycologySurvivalMoistureMax = 0.5f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.mycologyGrowthTempMin, Is.LessThan(config.mycologyGrowthTempMax));
            Assert.That(config.mycologySurvivalTempMin, Is.LessThanOrEqualTo(config.mycologyGrowthTempMin));
            Assert.That(config.mycologySurvivalTempMax, Is.GreaterThanOrEqualTo(config.mycologyGrowthTempMax));
            Assert.That(config.mycologySurvivalMoistureMin, Is.LessThanOrEqualTo(config.mycologyGrowthMoistureMin));
            Assert.That(config.mycologySurvivalMoistureMax, Is.GreaterThanOrEqualTo(config.mycologyGrowthMoistureMax));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RestoreDefaultsResetsMycologyFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.mycologyGrowthRate = 0f;
            config.mycologyDecayRate = 0f;
            config.mycologyInitialSporeLoad = 0f;
            config.RestoreDefaults();
            Assert.That(config.mycologyGrowthRate, Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(config.mycologyDecayRate, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(config.mycologyInitialSporeLoad, Is.EqualTo(0.08f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void TraitSanitizeRejectsContradictionsAndExtraBits()
        {
            Assert.That(MycologyTraits.IsValid(MycologyTraits.Basic), Is.True);
            Assert.That(MycologyTraits.IsValid(MycologyTraits.HeatResistant | MycologyTraits.DroughtProne), Is.True);
            Assert.That(MycologyTraits.IsValid(MycologyTraits.HeatProne | MycologyTraits.HeatResistant), Is.False);
            Assert.That(MycologyTraits.Sanitize(MycologyTraits.HeatProne | MycologyTraits.HeatResistant), Is.EqualTo(MycologyTraits.HeatResistant));
            Assert.That(MycologyTraits.IsValid(1u << 10), Is.False);
            Assert.That(MycologyTraits.Describe(MycologyTraits.Basic), Is.EqualTo("Basic"));
            Assert.That(MycologyTraits.Describe(MycologyTraits.DroughtResistant), Does.Contain("Drought"));
        }

        [Test]
        public void EcologyResourcesUseDedicatedRgbaFloatField()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.EcologyRead, Is.Not.Null);
            Assert.That(resources.EcologyRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.EcologyWrite.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.EcologyRead.width, Is.EqualTo(PolarGridDefinition.Validation.angularResolution));
        }

        [Test]
        public void OverlayModeIncludesMycology()
        {
            var go = new GameObject("Mycology Overlay Test");
            var renderer = go.AddComponent<PlanetoidDisplayRenderer>();
            renderer.SetOverlay(16);
            Assert.That(renderer.OverlayMode, Is.EqualTo(MycologyVisuals.OverlayMode));
            renderer.SetOverlay(99);
            Assert.That(renderer.OverlayMode, Is.EqualTo(MycologyVisuals.OverlayMode));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ColonizedSoilAndSedimentTintTowardMossAndSage()
        {
            Color soil = new(0.28f, 0.14f, 0.055f, 1f);
            Color sediment = new(0.46f, 0.29f, 0.13f, 1f);
            Assert.That(MycologyVisuals.BlendSoil(soil, 0f), Is.EqualTo(soil));
            Assert.That(MycologyVisuals.BlendSediment(sediment, 0f), Is.EqualTo(sediment));
            Color moss = MycologyVisuals.BlendSoil(soil, 1f);
            Color sage = MycologyVisuals.BlendSediment(sediment, 1f);
            Assert.That(moss, Is.EqualTo(MycologyVisuals.SoilColonized));
            Assert.That(sage, Is.EqualTo(MycologyVisuals.SedimentColonized));
            Assert.That(moss.g, Is.GreaterThan(moss.r));
            Assert.That(sage.g, Is.GreaterThan(sage.b));
        }
    }
}
