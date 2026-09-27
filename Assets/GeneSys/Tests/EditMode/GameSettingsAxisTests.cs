using System.Collections.Generic;
using System.Reflection;
using GeneSys.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class GameSettingsAxisTests
    {
        [Test]
        public void BlendUsesDefaultAsMidpoint()
        {
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 0f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 0.25f), Is.EqualTo(5f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 0.5f), Is.EqualTo(10f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 0.75f), Is.EqualTo(20f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 1f), Is.EqualTo(30f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, -1f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(GameSettingsAxes.Blend(0f, 10f, 30f, 2f), Is.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void CenterRestoresMappedFieldsToDefaults()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig config = CreateConfig();
            try
            {
                foreach (GameSettingBinding binding in AllBindings())
                {
                    FieldInfo field = Field(binding.FieldName);
                    object value = field.GetValue(defaults);
                    if (value is bool flag)
                        field.SetValue(config, !flag);
                    else if (value is int number)
                        field.SetValue(config, number + 7);
                    else
                        field.SetValue(config, (float)value + 1f);
                }

                foreach (GameSettingsAxis axis in GameSettingsAxes.All)
                    GameSettingsAxes.Apply(config, axis, 0.5f, defaults);

                foreach (GameSettingBinding binding in AllBindings())
                    AssertEqual(Field(binding.FieldName).GetValue(defaults), Field(binding.FieldName).GetValue(config), binding.FieldName);
            }
            finally
            {
                Destroy(defaults, config);
            }
        }

        [Test]
        public void ColdAndHotMoveTemperatureAndIce()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig cold = CreateConfig();
            SimulationConfig nearCold = CreateConfig();
            SimulationConfig mildCold = CreateConfig();
            SimulationConfig hot = CreateConfig();
            try
            {
                GameSettingsAxis axis = Axis("temperature");
                GameSettingsAxes.Apply(cold, axis, 0f, defaults);
                GameSettingsAxes.Apply(nearCold, axis, 0.2f, defaults);
                GameSettingsAxes.Apply(mildCold, axis, 0.4f, defaults);
                GameSettingsAxes.Apply(hot, axis, 1f, defaults);

                Assert.That(cold.surfaceAirTemperature, Is.LessThan(defaults.surfaceAirTemperature));
                Assert.That(hot.surfaceAirTemperature, Is.GreaterThan(defaults.surfaceAirTemperature));
                Assert.That(cold.solarIntensity, Is.LessThan(defaults.solarIntensity));
                Assert.That(hot.solarIntensity, Is.GreaterThan(defaults.solarIntensity));
                Assert.That(cold.terrainRadiativeCooling, Is.GreaterThan(defaults.terrainRadiativeCooling));
                Assert.That(hot.terrainRadiativeCooling, Is.LessThan(defaults.terrainRadiativeCooling));
                Assert.That(cold.iceCapRadius, Is.GreaterThan(defaults.iceCapRadius));
                Assert.That(hot.iceCapRadius, Is.EqualTo(defaults.iceCapRadius).Within(0.0001f));
                Assert.That(cold.frozenOceans, Is.True);
                Assert.That(nearCold.frozenOceans, Is.True);
                Assert.That(mildCold.frozenOceans, Is.False);
                Assert.That(hot.frozenOceans, Is.False);
                Assert.That(defaults.frozenOceans, Is.False);
            }
            finally
            {
                Destroy(defaults, cold, nearCold, mildCold, hot);
            }
        }

        [Test]
        public void DryAndWetMoveOceansAndWaterTable()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig dry = CreateConfig();
            SimulationConfig wet = CreateConfig();
            try
            {
                GameSettingsAxis axis = Axis("moisture");
                GameSettingsAxes.Apply(dry, axis, 0f, defaults);
                GameSettingsAxes.Apply(wet, axis, 1f, defaults);

                Assert.That(dry.targetOceanCoverage, Is.LessThan(defaults.targetOceanCoverage));
                Assert.That(wet.targetOceanCoverage, Is.GreaterThan(defaults.targetOceanCoverage));
                Assert.That(dry.basinDepth, Is.LessThan(defaults.basinDepth));
                Assert.That(wet.basinDepth, Is.GreaterThan(defaults.basinDepth));
                Assert.That(dry.initialAtmosphericHumidity, Is.LessThan(defaults.initialAtmosphericHumidity));
                Assert.That(wet.initialAtmosphericHumidity, Is.GreaterThan(defaults.initialAtmosphericHumidity));
                Assert.That(dry.groundwaterDepth, Is.GreaterThan(defaults.groundwaterDepth));
                Assert.That(wet.groundwaterDepth, Is.LessThan(defaults.groundwaterDepth));
                Assert.That(dry.evaporationRate, Is.LessThan(defaults.evaporationRate));
                Assert.That(wet.precipitationRate, Is.GreaterThan(defaults.precipitationRate));
            }
            finally
            {
                Destroy(defaults, dry, wet);
            }
        }

        [Test]
        public void StillStaysCalmUntilTheWindyEndAddsDrift()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig still = CreateConfig();
            SimulationConfig windy = CreateConfig();
            try
            {
                GameSettingsAxis axis = Axis("wind");
                GameSettingsAxes.Apply(still, axis, 0f, defaults);
                GameSettingsAxes.Apply(windy, axis, 1f, defaults);

                Assert.That(still.windStrength, Is.LessThan(defaults.windStrength));
                Assert.That(windy.windStrength, Is.GreaterThan(defaults.windStrength));
                Assert.That(still.windDamping, Is.GreaterThan(windy.windDamping));
                Assert.That(still.prevailingWind, Is.EqualTo(defaults.prevailingWind).Within(0.0001f));
                Assert.That(still.velocityAdvectionRate, Is.EqualTo(defaults.velocityAdvectionRate).Within(0.0001f));
                Assert.That(windy.prevailingWind, Is.GreaterThan(defaults.prevailingWind));
                Assert.That(windy.velocityAdvectionRate, Is.GreaterThan(defaults.velocityAdvectionRate));
                Assert.That(windy.climateThermalWindGain, Is.GreaterThan(defaults.climateThermalWindGain));
                Assert.That(windy.frontalLiftStrength, Is.GreaterThan(defaults.frontalLiftStrength));
            }
            finally
            {
                Destroy(defaults, still, windy);
            }
        }

        [Test]
        public void CalmAndVolcanicMoveEruptionRatesWithoutDisablingLayers()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig calm = CreateConfig();
            SimulationConfig volcanic = CreateConfig();
            try
            {
                GameSettingsAxis axis = Axis("volcanism");
                GameSettingsAxes.Apply(calm, axis, 0f, defaults);
                GameSettingsAxes.Apply(volcanic, axis, 1f, defaults);

                Assert.That(calm.extrusionRate, Is.LessThan(defaults.extrusionRate));
                Assert.That(volcanic.extrusionRate, Is.GreaterThan(defaults.extrusionRate));
                Assert.That(calm.volcanicReleaseThreshold, Is.GreaterThan(defaults.volcanicReleaseThreshold));
                Assert.That(volcanic.volcanicReleaseThreshold, Is.LessThan(defaults.volcanicReleaseThreshold));
                Assert.That(calm.magmaViscosity, Is.GreaterThan(defaults.magmaViscosity));
                Assert.That(volcanic.magmaViscosity, Is.LessThan(defaults.magmaViscosity));
                Assert.That(calm.geodynamicsLayerEnable, Is.EqualTo(defaults.geodynamicsLayerEnable));
                Assert.That(volcanic.mantleLayerEnable, Is.EqualTo(defaults.mantleLayerEnable));
                Assert.That(volcanic.volcanicMagmaFractionLimit, Is.EqualTo(defaults.volcanicMagmaFractionLimit).Within(0.0001f));
            }
            finally
            {
                Destroy(defaults, calm, volcanic);
            }
        }

        [Test]
        public void StableAndShiftingMoveFaultsCavesAndHealing()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig stable = CreateConfig();
            SimulationConfig shifting = CreateConfig();
            try
            {
                GameSettingsAxis axis = Axis("surface");
                GameSettingsAxes.Apply(stable, axis, 0f, defaults);
                GameSettingsAxes.Apply(shifting, axis, 1f, defaults);

                Assert.That(stable.tectonicFaultSeedCount, Is.LessThan(defaults.tectonicFaultSeedCount));
                Assert.That(shifting.tectonicFaultSeedCount, Is.GreaterThan(defaults.tectonicFaultSeedCount));
                Assert.That(stable.terrainRelief, Is.LessThan(defaults.terrainRelief));
                Assert.That(shifting.terrainRelief, Is.GreaterThan(defaults.terrainRelief));
                Assert.That(stable.limestoneDepositCount, Is.LessThan(defaults.limestoneDepositCount));
                Assert.That(shifting.limestoneDepositCount, Is.GreaterThan(defaults.limestoneDepositCount));
                Assert.That(stable.dissolutionRate, Is.LessThan(defaults.dissolutionRate));
                Assert.That(shifting.dissolutionRate, Is.GreaterThan(defaults.dissolutionRate));
                Assert.That(stable.collapseRate, Is.LessThan(defaults.collapseRate));
                Assert.That(shifting.erosionRate, Is.GreaterThan(defaults.erosionRate));
                Assert.That(stable.tectonicEarthquakeThreshold, Is.GreaterThan(defaults.tectonicEarthquakeThreshold));
                Assert.That(shifting.tectonicEarthquakeThreshold, Is.LessThan(defaults.tectonicEarthquakeThreshold));
                Assert.That(stable.tectonicFaultHealing, Is.GreaterThan(defaults.tectonicFaultHealing));
                Assert.That(shifting.tectonicFaultHealing, Is.LessThan(defaults.tectonicFaultHealing));
                Assert.That(stable.crustRatio, Is.EqualTo(defaults.crustRatio).Within(0.0001f));
                Assert.That(shifting.crustRatio, Is.EqualTo(defaults.crustRatio).Within(0.0001f));
            }
            finally
            {
                Destroy(defaults, stable, shifting);
            }
        }

        [Test]
        public void AxesDoNotShareFields()
        {
            var seen = new HashSet<string>();
            foreach (GameSettingBinding binding in AllBindings())
                Assert.That(seen.Add(binding.FieldName), Is.True, binding.FieldName);
        }

        [Test]
        public void BlendedValuesStayInsideRange()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig config = CreateConfig();
            try
            {
                float[] samples = { 0f, 0.25f, 0.5f, 0.75f, 1f };
                foreach (float t in samples)
                {
                    foreach (GameSettingsAxis axis in GameSettingsAxes.All)
                        GameSettingsAxes.Apply(config, axis, t, defaults);
                    foreach (GameSettingBinding binding in AllBindings())
                    {
                        FieldInfo field = Field(binding.FieldName);
                        UnityEngine.RangeAttribute range = field.GetCustomAttribute<UnityEngine.RangeAttribute>();
                        if (range == null) continue;
                        float value = ToFloat(field.GetValue(config));
                        Assert.That(value, Is.GreaterThanOrEqualTo(range.min - 0.0001f), $"{binding.FieldName} at {t}");
                        Assert.That(value, Is.LessThanOrEqualTo(range.max + 0.0001f), $"{binding.FieldName} at {t}");
                    }
                }
            }
            finally
            {
                Destroy(defaults, config);
            }
        }

        [Test]
        public void ApplyingOneAxisLeavesOtherAxesUnchanged()
        {
            SimulationConfig defaults = CreateConfig();
            try
            {
                foreach (GameSettingsAxis axis in GameSettingsAxes.All)
                {
                    foreach (float t in new[] { 0f, 1f })
                    {
                        SimulationConfig config = CreateConfig();
                        try
                        {
                            GameSettingsAxes.Apply(config, axis, t, defaults);
                            foreach (GameSettingsAxis other in GameSettingsAxes.All)
                            {
                                if (other.Id == axis.Id) continue;
                                foreach (GameSettingBinding binding in other.Bindings)
                                {
                                    FieldInfo field = Field(binding.FieldName);
                                    AssertEqual(field.GetValue(defaults), field.GetValue(config), $"{axis.Id} t={t} changed {binding.FieldName}");
                                }
                            }
                        }
                        finally
                        {
                            Destroy(config);
                        }
                    }
                }
            }
            finally
            {
                Destroy(defaults);
            }
        }

        [Test]
        public void AxesDoNotChangeStructuralSettings()
        {
            SimulationConfig defaults = CreateConfig();
            SimulationConfig config = CreateConfig();
            try
            {
                config.seed = 4242;
                config.crustRatio = 0.19f;
                config.geodynamicsLayerEnable = false;
                config.mantleLayerEnable = false;
                config.volcanicMagmaFractionLimit = 0.77f;
                config.mycologyGrowthRate = 0.5f;
                foreach (GameSettingsAxis axis in GameSettingsAxes.All)
                {
                    GameSettingsAxes.Apply(config, axis, 0f, defaults);
                    GameSettingsAxes.Apply(config, axis, 1f, defaults);
                }

                Assert.That(config.seed, Is.EqualTo(4242));
                Assert.That(config.crustRatio, Is.EqualTo(0.19f).Within(0.0001f));
                Assert.That(config.geodynamicsLayerEnable, Is.False);
                Assert.That(config.mantleLayerEnable, Is.False);
                Assert.That(config.volcanicMagmaFractionLimit, Is.EqualTo(0.77f).Within(0.0001f));
                Assert.That(config.mycologyGrowthRate, Is.EqualTo(0.5f).Within(0.0001f));
            }
            finally
            {
                Destroy(defaults, config);
            }
        }

        private static GameSettingsAxis Axis(string id)
        {
            foreach (GameSettingsAxis axis in GameSettingsAxes.All)
            {
                if (axis.Id == id) return axis;
            }

            Assert.Fail($"Missing game settings axis '{id}'.");
            return null;
        }

        private static IEnumerable<GameSettingBinding> AllBindings()
        {
            foreach (GameSettingsAxis axis in GameSettingsAxes.All)
            {
                foreach (GameSettingBinding binding in axis.Bindings)
                    yield return binding;
            }
        }

        private static FieldInfo Field(string fieldName)
        {
            return typeof(SimulationConfig).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
        }

        private static float ToFloat(object value)
        {
            if (value is bool flag) return flag ? 1f : 0f;
            if (value is int number) return number;
            return (float)value;
        }

        private static void AssertEqual(object expected, object actual, string message)
        {
            if (expected is float number)
                Assert.That(actual, Is.EqualTo(number).Within(0.0001f), message);
            else
                Assert.That(actual, Is.EqualTo(expected), message);
        }

        private static SimulationConfig CreateConfig()
        {
            return ScriptableObject.CreateInstance<SimulationConfig>();
        }

        private static void Destroy(params Object[] objects)
        {
            foreach (Object item in objects)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }
        }
    }
}
