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
            Assert.That(config.useMargolusTransport, Is.False);
            Assert.That(config.useLegacyTransport, Is.True);
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
        public void MargolusSedimentDisplayContractGuardsGhostSediment()
        {
            string displayShader = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(displayShader.Contains("_MaceMobileDisplay"), "PlanetoidDisplay.shader should declare _MaceMobileDisplay");
            Assert.That(displayShader.Contains("float mobileSediment = _MaceMobileDisplay > 0.5 ?"), "PlanetoidDisplay.shader should guard mobileSediment by _MaceMobileDisplay");

            string renderer = File.ReadAllText("Assets/GeneSys/Runtime/Rendering/PlanetoidDisplayRenderer.cs");
            Assert.That(renderer.Contains("_MaceMobileDisplay"), "PlanetoidDisplayRenderer.cs should set _MaceMobileDisplay");
            Assert.That(renderer.Contains("config.useLegacyTransport && (config.maceSedimentPilot || config.maceEntrainment)"), "PlanetoidDisplayRenderer.cs should only enable mobile display in legacy mode");

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler.Contains("resources.ClearMobileMass()"), "GpuPassScheduler.cs should clear mobile mass when legacy transport is disabled");
            Assert.That(scheduler.Contains("if (config.useLegacyTransport)"), "GpuPassScheduler.cs should guard legacy transport passes");
        }
    }
}
