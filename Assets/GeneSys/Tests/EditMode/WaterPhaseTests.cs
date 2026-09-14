using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using GeneSys.Configuration;
using GeneSys.UI;
using GeneSys.Validation;
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
            Assert.That(config.cloudRetainMass, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(config.vaporCapacityScale, Is.EqualTo(0.01f).Within(0.001f));
            Assert.That(config.waterPressureResponse, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(config.latentHeatScale, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(config.hydrostaticIterations, Is.EqualTo(32));
            Assert.That(typeof(SimulationConfig).GetField("rainPixelFormationThreshold"), Is.Null);
            Assert.That(typeof(SimulationConfig).GetField("geyserDischargeRate"), Is.Null);
            Assert.That(typeof(SimulationConfig).GetField("humidityBuoyancy"), Is.Null);

            config.waterPressureResponse = -2f;
            config.latentHeatScale = -1f;
            config.cloudRetainMass = 0f;
            config.vaporCapacityScale = 0f;
            typeof(SimulationConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(config, null);
            Assert.That(config.waterPressureResponse, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.latentHeatScale, Is.GreaterThanOrEqualTo(0f));
            Assert.That(config.cloudRetainMass, Is.GreaterThanOrEqualTo(0.01f));
            Assert.That(config.vaporCapacityScale, Is.GreaterThanOrEqualTo(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void WeatherUniformsAndTooltipsMatchRealignment()
        {
            string scheduler = File.ReadAllText("Assets/GeneSys/Runtime/Simulation/Gpu/GpuPassScheduler.cs");
            Assert.That(scheduler, Does.Contain("config.vaporCapacityScale"));
            Assert.That(scheduler, Does.Contain("config.cloudRetainMass"));
            Assert.That(scheduler, Does.Contain("config.solarIntensity"));
            Assert.That(scheduler, Does.Contain("config.atmosphereAbsorption"));
            Assert.That(scheduler, Does.Contain("FindKernel(\"Precipitation\")"));
            Assert.That(scheduler, Does.Contain("LightAttenuation"));
            Assert.That(scheduler, Does.Not.Contain("GeothermalDischarge"));
            Assert.That(SimulationSettingTooltips.TryGet(nameof(SimulationConfig.vaporCapacityScale), out string capacity), Is.True);
            Assert.That(capacity.Length, Is.GreaterThan(40));
            Assert.That(SimulationSettingTooltips.TryGet(nameof(SimulationConfig.solarIntensity), out string solar), Is.True);
            Assert.That(solar.Length, Is.GreaterThan(40));
        }

        [Test]
        public void SharedHelpersAndKernelsAreWired()
        {
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Assert.That(structs, Does.Contain("VaporSaturation"));
            Assert.That(structs, Does.Contain("DewPoint"));
            Assert.That(structs, Does.Contain("VirtualTemperature"));
            Assert.That(structs, Does.Contain("EvaporateToAir"));
            Assert.That(structs, Does.Contain("GroundwaterBoilMass"));
            Assert.That(structs, Does.Contain("WaterLatentHeatDelta"));
            Assert.That(structs, Does.Contain("PRECIP_CLOUD_BASE"));
            Assert.That(structs, Does.Contain("MaterialPhaseLatentDelta"));
            Assert.That(structs, Does.Contain("EffectiveCellHeatCapacity"));
            string phase = File.ReadAllText("Assets/GeneSys/Compute/Simulation/MaterialSimulation.compute");
            Assert.That(phase, Does.Contain("material != liquidId"));
            Assert.That(phase, Does.Contain("material != solidId"));
            string weather = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weather, Does.Contain("DewTransfer"));
            Assert.That(weather, Does.Contain("SurfaceEvaporation"));
            Assert.That(weather, Does.Contain("EvaporateToAir"));
            Assert.That(weather, Does.Contain("_WeatherH.y"));
            Assert.That(weather, Does.Contain("PrecipitationClearAirDest"));
            Assert.That(weather, Does.Contain("PRECIP_CLOUD_BASE"));
            Assert.That(weather, Does.Not.Contain("S(dest).z) > retain"));
            string hydrologySrc = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Hydrology.compute");
            Assert.That(hydrologySrc, Does.Contain("EvaporateToAir"));
            Assert.That(hydrologySrc, Does.Contain("Residue stays on this Water pixel"));
            Assert.That(hydrologySrc, Does.Not.Contain("aux.x += leftover"));
            Assert.That(hydrologySrc, Does.Not.Contain("aux.x += state.z;"));
            ComputeShader weatherShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weatherShader.FindKernel("Precipitation"), Is.GreaterThanOrEqualTo(0));
            Assert.That(weatherShader.FindKernel("WaterCycle"), Is.GreaterThanOrEqualTo(0));
            ComputeShader hydrology = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GeneSys/Compute/Simulation/Hydrology.compute");
            Assert.That(hydrology.FindKernel("Groundwater"), Is.GreaterThanOrEqualTo(0));
            Assert.That(hydrologySrc, Does.Not.Contain("GeothermalDischarge"));
        }

        [Test]
        public void MagnusConstantsMatchHlslAndCsharp()
        {
            string structs = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SimulationStructs.hlsl");
            Match a = Regex.Match(structs, @"#define WATER_MAGNUS_A ([0-9.]+)");
            Match b = Regex.Match(structs, @"#define WATER_MAGNUS_B ([0-9.]+)");
            Assert.That(a.Success && b.Success, Is.True);
            Assert.That(float.Parse(a.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(SimulationMetrics.WaterMagnusA).Within(0.0001f));
            Assert.That(float.Parse(b.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(SimulationMetrics.WaterMagnusB).Within(0.0001f));

            float[] temps = { -20f, 0f, 15f, 30f, 60f };
            foreach (float t in temps)
            {
                float es = Mathf.Exp(SimulationMetrics.WaterMagnusA * t / Mathf.Max(1e-3f, SimulationMetrics.WaterMagnusB + t));
                float sat = Mathf.Max(1e-4f, 0.01f * es);
                Assert.That(SimulationMetrics.RelativeHumidity(sat * 0.5f, t, 0.01f), Is.EqualTo(0.5f).Within(0.02f));
            }
        }
    }
}
