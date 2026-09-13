using System.IO;
using GeneSys.Configuration;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class MargolusContractTests
    {
        [Test]
        public void MargolusComputeShaderHasRequiredKernels()
        {
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(shader, Is.Not.Null, "MargolusTransport.compute could not be loaded.");
            Assert.That(shader.FindKernel("MargolusPhaseEven"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("MargolusPhaseOdd"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void MargolusConfigDefaultsAndClamps()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.enableMaterialTransport, Is.True);
            Assert.That(config.margolusSubsteps, Is.EqualTo(1));
            Assert.That(config.margolusGravityBias, Is.EqualTo(1f));
            Assert.That(config.margolusReposeFriction, Is.EqualTo(1f));
            Assert.That(config.margolusMetricEnable, Is.True);

            config.margolusSubsteps = 10;
            config.margolusGravityBias = -2f;
            config.margolusReposeFriction = 0f;
            typeof(SimulationConfig).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(config, null);

            Assert.That(config.margolusSubsteps, Is.EqualTo(4));
            Assert.That(config.margolusGravityBias, Is.EqualTo(0f));
            Assert.That(config.margolusReposeFriction, Is.GreaterThanOrEqualTo(0.1f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void SimulationHostAndSchedulerWireMargolusTransport()
        {
            string host = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/SimulationHost.cs");
            Assert.That(host.Contains("margolusTransport"), "SimulationHost.cs should declare margolusTransport");
            Assert.That(host.Contains("MargolusTransport.compute"), "SimulationHost.cs should reference MargolusTransport.compute");

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler.Contains("margolusTransport"), "GpuPassScheduler.cs should declare margolusTransport");
            Assert.That(scheduler.Contains("DispatchMargolus"), "GpuPassScheduler.cs should implement DispatchMargolus");
        }

        [Test]
        public void MargolusContractExcludesLegacyMaceAndMobileDisplay()
        {
            string displayShader = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(displayShader, Does.Not.Contain("_MaceMobileDisplay"), "PlanetoidDisplay.shader should not declare _MaceMobileDisplay");
            Assert.That(displayShader, Does.Not.Contain("_MobileMassTex"), "PlanetoidDisplay.shader should not reference _MobileMassTex");

            string renderer = File.ReadAllText("Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs");
            Assert.That(renderer, Does.Not.Contain("_MaceMobileDisplay"), "PlanetoidDisplayRenderer.cs should not set _MaceMobileDisplay");

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Not.Contain("maceTransport"), "GpuPassScheduler.cs should not contain maceTransport");
            Assert.That(scheduler, Does.Not.Contain("_MaceFlags"), "GpuPassScheduler.cs should not declare _MaceFlags");
            Assert.That(scheduler, Does.Contain("config.enableMaterialTransport"), "GpuPassScheduler.cs should guard transport passes with enableMaterialTransport");
        }
    }
}
