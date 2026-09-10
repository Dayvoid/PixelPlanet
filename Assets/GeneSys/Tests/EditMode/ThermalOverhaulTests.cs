using System.IO;
using GeneSys.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class ThermalOverhaulTests
    {
        [Test]
        public void ThermalUniformsAndKernelsAreWired()
        {
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("config.thermalMoistureBoost"));
            Assert.That(scheduler, Does.Contain("config.thermalPressureEffect"));
            Assert.That(scheduler, Does.Contain("config.coreTemperature"));
            Assert.That(scheduler, Does.Contain("config.coreHeatRate"));
            Assert.That(scheduler, Does.Contain("config.coreHeatRate > 0f"));
            Assert.That(scheduler, Does.Contain("config.stormStrikeAirHeatFraction"));
            Assert.That(scheduler, Does.Contain("_WorldGenV3A"));
            Assert.That(scheduler, Does.Not.Contain("GeothermalDischarge"));

            string thermal = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute");
            Assert.That(thermal, Does.Contain("MoistureAdjustedConductivity"));
            Assert.That(thermal, Does.Contain("FaceGeometryWeight"));
            Assert.That(thermal, Does.Contain("MaterialPhaseLatentDelta"));
            Assert.That(thermal, Does.Contain("material != liquidId"));
            Assert.That(thermal, Does.Contain("material != solidId"));
            Assert.That(thermal, Does.Contain("material == 2u"));

            string combustion = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Combustion.compute");
            Assert.That(combustion, Does.Contain("EffectiveCellHeatCapacity"));
            Assert.That(combustion, Does.Contain("/ cpEff"));

            string storm = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Storm.compute");
            Assert.That(storm, Does.Contain("airScale"));
            Assert.That(storm, Does.Contain("_StormG.y"));

            string worldgen = File.ReadAllText("Assets/GeneSys/Compute/WorldGen/WorldGeneration.compute");
            Assert.That(worldgen, Does.Contain("SampleDeposit"));
            Assert.That(worldgen, Does.Contain("LIMESTONE_ID"));
            Assert.That(worldgen, Does.Contain("CLAY_ID"));
            Assert.That(worldgen, Does.Contain("WorldgenCoreTemperature"));
        }

        [Test]
        public void ConfigDefaultsMatchThermalOverhaul()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.limestoneDepositCount, Is.EqualTo(20));
            Assert.That(config.clayDepositCount, Is.EqualTo(20));
            Assert.That(config.coreTemperature, Is.EqualTo(1500f).Within(0.01f));
            Assert.That(config.coreHeatRate, Is.GreaterThan(0f));
            Assert.That(config.thermalMoistureBoost, Is.EqualTo(1f).Within(0.01f));
            Assert.That(config.thermalPressureEffect, Is.GreaterThan(0f));
            Assert.That(config.combustionHeatYield, Is.EqualTo(3.8f).Within(0.01f));
            Assert.That(config.stormStrikeAirHeatFraction, Is.EqualTo(0.3f).Within(0.01f));
            Object.DestroyImmediate(config);
        }
    }
}
