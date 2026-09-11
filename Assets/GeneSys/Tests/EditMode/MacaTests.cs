using System.IO;
using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GeneSys.Tests
{
    public sealed class MacaTests
    {
        [Test]
        public void LifeGenomeGainsMobileSliceAndAffinityScratch()
        {
            using var resources = new SimulationResources(PolarGridDefinition.Validation);
            Assert.That(resources.LifeGenomeRead.volumeDepth, Is.EqualTo(SimulationResources.LifeGenomeSliceCount));
            Assert.That(resources.LifeGenomeWrite.volumeDepth, Is.EqualTo(3));
            Assert.That(resources.Affinity, Is.Not.Null);
            Assert.That(resources.Affinity.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32B32A32_SFloat));
            Assert.That(resources.Affinity.enableRandomWrite, Is.True);
        }

        [Test]
        public void PolarValidateRoundsAngularWidthToEven()
        {
            var grid = PolarGridDefinition.Validation;
            grid.angularResolution = 33;
            grid.Validate();
            Assert.That(grid.angularResolution % 2, Is.EqualTo(0));
            Assert.That(grid.angularResolution, Is.GreaterThanOrEqualTo(34));
        }

        [Test]
        public void MaceConfigDefaultsAreEnabledWithPhaseTwo()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.maceEnabled, Is.True);
            Assert.That(config.macePhasesPerTick, Is.EqualTo(2));
            Assert.That(config.maceOccupyLow, Is.LessThan(config.maceOccupyHigh));
            Assert.That(config.maceSuspendCap, Is.GreaterThan(0f));
        }

        [Test]
        public void MobileContractAndMaceShaderAreWired()
        {
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs.Contains("#define MOBILE_SLICE 2"));
            Assert.That(structs.Contains("SeedMobileFromMaterial"));
            Assert.That(structs.Contains("MobileCapacity"));

            string compute = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaceTransport.compute");
            Assert.That(compute.Contains("#pragma kernel SeedMobile"));
            Assert.That(compute.Contains("#pragma kernel MacaBlock"));
            Assert.That(compute.Contains("#pragma kernel Reconcile"));

            string builder = File.ReadAllText("Assets/GeneSys/Editor/GeneSysProjectBuilder.cs");
            Assert.That(builder.Contains("MaceTransport.compute"));

            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("_OverlayMode == 27"));
            Assert.That(SedimentVisuals.OverlayMode, Is.EqualTo(27));
            Assert.That(FaunaVisuals.MaxOverlayMode, Is.EqualTo(27));
        }
    }
}
