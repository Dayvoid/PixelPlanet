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
            Assert.That(config.climateSlabRadiativeCooling, Is.EqualTo(config.atmosphereRadiativeCooling).Within(0.01f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void PhaseChangeOwnsMagmaFreezeWithBasaltHysteresis()
        {
            string volcanism = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Geology.compute");
            Assert.That(volcanism, Does.Contain("state.x -= _Volcanic.y"));
            Assert.That(volcanism, Does.Not.Contain("material = 5u;"));
            Assert.That(volcanism, Does.Not.Contain("state.x < 780"));

            string phase = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute");
            Assert.That(phase, Does.Contain("MaterialPhaseLatentDelta"));

            var magma = UnityEditor.AssetDatabase.LoadAssetAtPath<GeneSys.Materials.MaterialDefinition>(
                "Assets/GeneSys/Data/Materials/006_Magma.asset");
            var basalt = UnityEditor.AssetDatabase.LoadAssetAtPath<GeneSys.Materials.MaterialDefinition>(
                "Assets/GeneSys/Data/Materials/005_Basalt.asset");
            Assert.That(magma, Is.Not.Null);
            Assert.That(basalt, Is.Not.Null);
            Assert.That(magma.solidPhaseId, Is.EqualTo(5));
            Assert.That(basalt.liquidPhaseId, Is.EqualTo(6));
            Assert.That(basalt.meltingTemperature, Is.GreaterThan(magma.meltingTemperature),
                "Basalt 780 vs Magma 700 is intentional PhaseChange hysteresis.");

            string weather = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weather, Does.Contain("ClimateInsolationScale(cell.x)"));
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("config.climateSlabRadiativeCooling"));
        }
    }
}
