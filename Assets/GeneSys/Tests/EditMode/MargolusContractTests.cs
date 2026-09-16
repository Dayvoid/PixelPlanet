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
            Assert.That(shader.FindKernel("MargolusGrassEven"), Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.FindKernel("MargolusGrassOdd"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void MargolusConfigDefaultsAndClamps()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.enableMaterialTransport, Is.True);
            Assert.That(config.margolusSubsteps, Is.EqualTo(1));
            Assert.That(config.margolusReposeFriction, Is.EqualTo(1f));
            Assert.That(config.margolusMetricEnable, Is.True);

            config.margolusSubsteps = 10;
            config.margolusReposeFriction = 0f;
            typeof(SimulationConfig).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(config, null);

            Assert.That(config.margolusSubsteps, Is.EqualTo(4));
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
            Assert.That(scheduler.Contains("liquidOnly: true"), "GpuPassScheduler.cs should fall hydrometeors after precipitation");
            string transport = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(transport, Does.Contain("_MargolusLiquidOnly"));
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

        [Test]
        public void MargolusContractExcludesRetiredDensityExchangeAndMotionKernels()
        {
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Not.Contain("_DensityExchange"), "GpuPassScheduler.cs should not set _DensityExchange");
            Assert.That(scheduler, Does.Not.Contain("LiquidDensityExchange"));
            Assert.That(scheduler, Does.Not.Contain("MaterialMotion"));

            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs, Does.Not.Contain("LiquidDensityExchange"));
            Assert.That(structs, Does.Not.Contain("MaterialMotion"));
            Assert.That(structs, Does.Not.Contain("densityDisplaceable"));

            string materialSim = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute");
            Assert.That(materialSim, Does.Not.Contain("LiquidDensityExchange"));
            Assert.That(materialSim, Does.Not.Contain("#pragma kernel MaterialMotion"));

            Assert.That(File.Exists("Assets/GeneSys/Compute/Simulation/MaceTransport.compute"), Is.False);
        }

        [Test]
        public void MargolusSwapsGrassSlotsWithSoilCells()
        {
            string shader = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(shader, Does.Contain("_GrassWrite"));
            Assert.That(shader, Does.Contain("MargolusGrassEven"));
            Assert.That(shader, Does.Contain("SwapI2"));

            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("resources.SwapGrass()"));
            Assert.That(scheduler, Does.Contain("_TreeRead"));
        }

        [Test]
        public void MargolusPinsFaunaOrganismsSeparatelyFromAlgae()
        {
            string common = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/MargolusCommon.hlsl");
            Assert.That(common, Does.Contain("IsAnyFaunaMaterial(c.material)"));
            Assert.That(common, Does.Contain("FLORA_ALGAE_ID) return false"));

            string shader = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(shader, Does.Contain("IsAnyFaunaMaterial(c.material)"));
            Assert.That(shader, Does.Not.Contain("_FaunaRead"));
            Assert.That(shader, Does.Not.Contain("_FaunaWrite"));
        }

        [Test]
        public void MargolusUnpinsAlgaeAndCopiesUnifiedFloraSlices()
        {
            string common = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/MargolusCommon.hlsl");
            Assert.That(common, Does.Contain("FLORA_ALGAE_ID) return false"));
            Assert.That(common, Does.Contain("material == FLORA_ALGAE_ID"));

            string shader = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MargolusTransport.compute");
            Assert.That(shader, Does.Contain("for (int i = 0; i < FLORA_SLICE_COUNT; i++)"));
            Assert.That(shader, Does.Not.Contain("for (int i = 0; i < GRASS_SLICE_COUNT; i++)"));
        }
    }
}
