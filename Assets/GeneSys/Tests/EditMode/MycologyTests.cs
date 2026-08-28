using System.Reflection;
using GeneSys.Configuration;
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
            Assert.That(resources.CombustionRead, Is.Not.Null);
            Assert.That(resources.CombustionRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.CombustionWrite.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.StormRead, Is.Not.Null);
            Assert.That(resources.StormRead.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.StormWrite.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
        }
    }
}
