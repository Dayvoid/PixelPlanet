using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GeneSys.Configuration
{
    /// <summary>
    /// One simulation setting rewritten by a Game Settings slider.
    /// Low is the left end, high is the right end, and the field's C# default is the center.
    /// Bool fields use 0 and 1. They become true only when the blended value is at least 0.5.
    /// </summary>
    public sealed class GameSettingBinding
    {
        public GameSettingBinding(string fieldName, float low, float high)
        {
            FieldName = fieldName;
            Low = low;
            High = high;
        }

        public string FieldName { get; }
        public float Low { get; }
        public float High { get; }
    }

    public sealed class GameSettingsAxis
    {
        public GameSettingsAxis(string id, string title, string lowLabel, string highLabel, string tooltip, GameSettingBinding[] bindings)
        {
            Id = id;
            Title = title;
            LowLabel = lowLabel;
            HighLabel = highLabel;
            Tooltip = tooltip;
            Bindings = bindings;
        }

        public string Id { get; }
        public string Title { get; }
        public string LowLabel { get; }
        public string HighLabel { get; }
        public string Tooltip { get; }
        public IReadOnlyList<GameSettingBinding> Bindings { get; }
    }

    /// <summary>
    /// Bipolar themes that rewrite groups of <see cref="SimulationConfig"/> fields.
    /// Center matches the current defaults. Axes do not share fields.
    /// </summary>
    public static class GameSettingsAxes
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new();

        public static readonly GameSettingsAxis[] All =
        {
            new GameSettingsAxis(
                "temperature",
                "Temperature",
                "Cold",
                "Hot",
                "Baseline air temperature, sunlight, cooling toward space, ice albedo, and polar ice. The cold end can freeze generated oceans. Ice caps and frozen oceans apply the next time you regenerate; temperature and sunlight change on the running world.",
                new[]
                {
                    new GameSettingBinding(nameof(SimulationConfig.surfaceAirTemperature), -15f, 40f),
                    new GameSettingBinding(nameof(SimulationConfig.solarIntensity), 0.25f, 1.8f),
                    new GameSettingBinding(nameof(SimulationConfig.spaceTemperature), -50f, 40f),
                    new GameSettingBinding(nameof(SimulationConfig.terrainRadiativeCooling), 0.7f, 0.08f),
                    new GameSettingBinding(nameof(SimulationConfig.atmosphereRadiativeCooling), 0.7f, 0.08f),
                    new GameSettingBinding(nameof(SimulationConfig.climateSlabRadiativeCooling), 0.7f, 0.08f),
                    new GameSettingBinding(nameof(SimulationConfig.climateIceAlbedo), 0.85f, 0.25f),
                    new GameSettingBinding(nameof(SimulationConfig.iceCapRadius), 0.12f, 0.001f),
                    new GameSettingBinding(nameof(SimulationConfig.iceCapHeight), 0.02f, 0.003f),
                    new GameSettingBinding(nameof(SimulationConfig.frozenOceans), 1f, 0f)
                }),
            new GameSettingsAxis(
                "moisture",
                "Moisture",
                "Dry",
                "Wet",
                "Ocean coverage, basin depth, starting humidity and groundwater, and evaporation, condensation, rain, and dew. Drier settings deepen the water table. Oceans and starting water apply the next time you regenerate.",
                new[]
                {
                    new GameSettingBinding(nameof(SimulationConfig.targetOceanCoverage), 0.22f, 0.75f),
                    new GameSettingBinding(nameof(SimulationConfig.basinDepth), 0.03f, 0.14f),
                    new GameSettingBinding(nameof(SimulationConfig.initialGroundwaterSaturation), 0.15f, 0.95f),
                    new GameSettingBinding(nameof(SimulationConfig.initialAtmosphericHumidity), 0.12f, 0.95f),
                    new GameSettingBinding(nameof(SimulationConfig.groundwaterDepth), 0.85f, 0.28f),
                    new GameSettingBinding(nameof(SimulationConfig.evaporationRate), 0.15f, 1.4f),
                    new GameSettingBinding(nameof(SimulationConfig.condensationRate), 0.004f, 0.04f),
                    new GameSettingBinding(nameof(SimulationConfig.precipitationRate), 0.03f, 0.4f),
                    new GameSettingBinding(nameof(SimulationConfig.dewRate), 0.003f, 0.04f)
                }),
            new GameSettingsAxis(
                "wind",
                "Wind",
                "Still",
                "Windy",
                "Wind strength, damping, heat and vapor advection, thermal wind, and frontal lift. The windy end adds a zonal drift and lets gusts carry their momentum. These change on the running world.",
                new[]
                {
                    new GameSettingBinding(nameof(SimulationConfig.windStrength), 0.15f, 3.6f),
                    new GameSettingBinding(nameof(SimulationConfig.windDamping), 0.45f, 0.02f),
                    new GameSettingBinding(nameof(SimulationConfig.prevailingWind), 0f, 1.2f),
                    new GameSettingBinding(nameof(SimulationConfig.velocityAdvectionRate), 0f, 1.2f),
                    new GameSettingBinding(nameof(SimulationConfig.atmosphericAdvectionRate), 0.15f, 2.2f),
                    new GameSettingBinding(nameof(SimulationConfig.temperatureAdvectionRate), 0.1f, 2.2f),
                    new GameSettingBinding(nameof(SimulationConfig.climateThermalWindGain), 0f, 0.35f),
                    new GameSettingBinding(nameof(SimulationConfig.frontalLiftStrength), 0.15f, 2.2f)
                }),
            new GameSettingsAxis(
                "volcanism",
                "Volcanism",
                "Calm",
                "Volcanic",
                "Extrusion, melting, plume heat, eruption drive, geothermal warming, core pulses, ash lofting, and how easily a sector erupts. The volcanic end keeps lava fluid. These change on the running world. The interior layers stay enabled.",
                new[]
                {
                    new GameSettingBinding(nameof(SimulationConfig.extrusionRate), 0.05f, 1.6f),
                    new GameSettingBinding(nameof(SimulationConfig.volcanicMeltRate), 0.04f, 1.2f),
                    new GameSettingBinding(nameof(SimulationConfig.mantlePlumeHeat), 0.02f, 0.8f),
                    new GameSettingBinding(nameof(SimulationConfig.eruptionDriveScale), 0.05f, 0.95f),
                    new GameSettingBinding(nameof(SimulationConfig.geothermalSurfaceGain), 0.02f, 0.6f),
                    new GameSettingBinding(nameof(SimulationConfig.geothermalClimateGain), 0.01f, 0.35f),
                    new GameSettingBinding(nameof(SimulationConfig.corePulseHeat), 1f, 40f),
                    new GameSettingBinding(nameof(SimulationConfig.ashUpdraftStrength), 0.05f, 1.6f),
                    new GameSettingBinding(nameof(SimulationConfig.geodynamicsPressureBuildRate), 0.05f, 1.2f),
                    new GameSettingBinding(nameof(SimulationConfig.volcanicReleaseThreshold), 1.55f, 0.35f),
                    new GameSettingBinding(nameof(SimulationConfig.magmaViscosity), 1.2f, 0.15f),
                    new GameSettingBinding(nameof(SimulationConfig.volcanicCoolingRate), 0.45f, 0.05f)
                }),
            new GameSettingsAxis(
                "surface",
                "Surface",
                "Stable",
                "Shifting",
                "Fault seeds, terrain relief, border noise, limestone, strain, quakes, uplift, cave dissolution, collapse, and erosion. The stable end lets faults heal. Faults, relief, and limestone apply the next time you regenerate; strain, caves, and erosion change on the running world.",
                new[]
                {
                    new GameSettingBinding(nameof(SimulationConfig.tectonicFaultSeedCount), 2f, 36f),
                    new GameSettingBinding(nameof(SimulationConfig.terrainRelief), 0.01f, 0.12f),
                    new GameSettingBinding(nameof(SimulationConfig.borderNoise), 0.008f, 0.12f),
                    new GameSettingBinding(nameof(SimulationConfig.protrusionChance), 0.02f, 0.45f),
                    new GameSettingBinding(nameof(SimulationConfig.limestoneDepositCount), 6f, 48f),
                    new GameSettingBinding(nameof(SimulationConfig.limestoneDepositMaxSize), 0.03f, 0.12f),
                    new GameSettingBinding(nameof(SimulationConfig.tectonicStrainGain), 0.02f, 0.8f),
                    new GameSettingBinding(nameof(SimulationConfig.tectonicEarthquakeThreshold), 1.5f, 0.35f),
                    new GameSettingBinding(nameof(SimulationConfig.tectonicUpliftScale), 0.15f, 2.4f),
                    new GameSettingBinding(nameof(SimulationConfig.dissolutionRate), 0.005f, 0.6f),
                    new GameSettingBinding(nameof(SimulationConfig.collapseRate), 0.005f, 0.45f),
                    new GameSettingBinding(nameof(SimulationConfig.erosionRate), 0.008f, 0.25f),
                    new GameSettingBinding(nameof(SimulationConfig.tectonicFaultHealing), 0.06f, 0.005f)
                })
        };

        static GameSettingsAxes()
        {
            foreach (GameSettingsAxis axis in All)
            {
                foreach (GameSettingBinding binding in axis.Bindings)
                {
                    if (Fields.ContainsKey(binding.FieldName))
                        throw new InvalidOperationException($"Game settings field '{binding.FieldName}' is assigned to more than one axis.");
                    FieldInfo field = typeof(SimulationConfig).GetField(binding.FieldName, BindingFlags.Instance | BindingFlags.Public);
                    if (field == null)
                        throw new InvalidOperationException($"Game settings axis field '{binding.FieldName}' was not found on SimulationConfig.");
                    if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && field.FieldType != typeof(bool))
                        throw new InvalidOperationException($"Game settings axis field '{binding.FieldName}' must be a float, int, or bool.");
                    Fields.Add(binding.FieldName, field);
                }
            }
        }

        public static float Blend(float low, float mid, float high, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0.5f)
                return Mathf.Lerp(low, mid, t * 2f);
            return Mathf.Lerp(mid, high, (t - 0.5f) * 2f);
        }

        public static void Apply(SimulationConfig target, GameSettingsAxis axis, float t, SimulationConfig defaults)
        {
            if (target == null || axis == null || defaults == null) return;
            t = Mathf.Clamp01(t);
            foreach (GameSettingBinding binding in axis.Bindings)
            {
                FieldInfo field = Fields[binding.FieldName];
                float mid = ToFloat(field.GetValue(defaults));
                float blended = ClampToRange(field, Blend(binding.Low, mid, binding.High, t));
                if (field.FieldType == typeof(bool))
                    field.SetValue(target, blended >= 0.5f);
                else if (field.FieldType == typeof(int))
                    field.SetValue(target, Mathf.RoundToInt(blended));
                else
                    field.SetValue(target, blended);
            }
        }

        private static float ToFloat(object value)
        {
            if (value is bool flag) return flag ? 1f : 0f;
            if (value is int number) return number;
            return (float)value;
        }

        private static float ClampToRange(FieldInfo field, float value)
        {
            RangeAttribute range = field.GetCustomAttribute<RangeAttribute>();
            if (range == null) return value;
            return Mathf.Clamp(value, range.min, range.max);
        }
    }
}
