using System.IO;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class WaterPhaseTests
    {
        [Test]
        public void ConfigDefaultsAndValidationReplacePixelThresholds()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.cloudPrecipitationThreshold, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(config.waterPressureResponse, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(config.latentHeatScale, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(typeof(SimulationConfig).GetField("rainPixelFormationThreshold"), Is.Null);
            Assert.That(typeof(SimulationConfig).GetField("surfaceWaterPixelThreshold"), Is.Null);

            config.waterPressureResponse = -2f;
            config.latentHeatScale = -1f;
            config.cloudPrecipitationThreshold = 0f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.waterPressureResponse, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.latentHeatScale, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.cloudPrecipitationThreshold, Is.GreaterThanOrEqualTo(0.01f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void WeatherEPackingAndTooltipsMatchNewControls()
        {
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("config.waterPressureResponse"));
            Assert.That(scheduler, Does.Contain("config.latentHeatScale"));
            Assert.That(scheduler, Does.Contain("FindKernel(\"Precipitation\")"));
            Assert.That(scheduler, Does.Not.Contain("WaterMaterialization"));
            Assert.That(SimulationSettingTooltips.TryGet(nameof(SimulationConfig.waterPressureResponse), out string pressure), Is.True);
            Assert.That(pressure.Length, Is.GreaterThan(40));
            Assert.That(SimulationSettingTooltips.TryGet(nameof(SimulationConfig.latentHeatScale), out string latent), Is.True);
            Assert.That(latent.Length, Is.GreaterThan(40));
        }

        [Test]
        public void SharedHelpersAndKernelsAreWired()
        {
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs, Does.Contain("WaterVaporCapacity"));
            Assert.That(structs, Does.Contain("WaterBoilTemperature"));
            Assert.That(structs, Does.Contain("PrecipitationMass"));
            Assert.That(structs, Does.Contain("PrecipitationEmitMass"));
            Assert.That(structs, Does.Contain("PRECIP_MIN_DROP"));
            Assert.That(structs, Does.Contain("WaterLatentHeatDelta"));
            string surface = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SurfaceWater.hlsl");
            Assert.That(surface, Does.Contain("PartitionSurfaceVolume"));
            Assert.That(surface, Does.Contain("KeepLandedRainPixel"));
            ComputeShader weather = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute"), Does.Contain("CloudPrecipReceiver"));
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute"), Does.Contain("AirbornePrecipDestination"));
            Assert.That(weather.FindKernel("Precipitation"), Is.GreaterThanOrEqualTo(0));
            Assert.That(weather.FindKernel("WaterCycle"), Is.GreaterThanOrEqualTo(0));
            ComputeShader hydrology = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Hydrology.compute");
            Assert.That(File.ReadAllText("Assets/GeneSys/Compute/Simulation/Hydrology.compute"), Does.Not.Contain("WaterMaterialization"));
            Assert.That(hydrology.FindKernel("ApplyHydrostaticColumns"), Is.GreaterThanOrEqualTo(0));
        }
    }
}
