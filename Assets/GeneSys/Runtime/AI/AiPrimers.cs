using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using GeneSys.Configuration;
using GeneSys.Simulation.Topology;
using UnityEngine;

namespace GeneSys.AI
{
    public static class PolarCellMapping
    {
        public static Vector2Int FromNormalized(PolarGridDefinition grid, float theta01, float radius01)
        {
            int x = grid.WrapTheta(Mathf.FloorToInt(Mathf.Repeat(theta01, 1f) * Mathf.Max(1, grid.angularResolution)));
            int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(radius01) * Mathf.Max(1, grid.radialResolution)), 0, Mathf.Max(0, grid.radialResolution - 1));
            return new Vector2Int(x, y);
        }
    }

    public static class WorldParameterCatalog
    {
        private static readonly string[] Allowed =
        {
            nameof(SimulationConfig.solarIntensity),
            nameof(SimulationConfig.coreHeatRate),
            nameof(SimulationConfig.surfaceAirTemperature),
            nameof(SimulationConfig.thermalRate),
            nameof(SimulationConfig.evaporationRate),
            nameof(SimulationConfig.condensationRate),
            nameof(SimulationConfig.precipitationRate),
            nameof(SimulationConfig.vaporCapacityScale),
            nameof(SimulationConfig.infiltrationRate),
            nameof(SimulationConfig.groundwaterRate),
            nameof(SimulationConfig.runoffRate)
        };

        public static bool TrySet(SimulationConfig config, string name, float value, out string message)
        {
            message = null;
            if (config == null)
            {
                message = "No simulation config.";
                return false;
            }

            FieldInfo field = Resolve(name);
            if (field == null)
            {
                message = $"Parameter '{name}' is not on the deity whitelist. Allowed: {string.Join(", ", Allowed)}.";
                return false;
            }

            float clamped = Clamp(field, value);
            field.SetValue(config, clamped);
            message = $"{field.Name} set to {clamped.ToString("0.###", CultureInfo.InvariantCulture)}.";
            return true;
        }

        public static FieldInfo Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (string allowed in Allowed)
            {
                if (string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                    return typeof(SimulationConfig).GetField(allowed, BindingFlags.Instance | BindingFlags.Public);
            }

            return null;
        }

        public static float Clamp(FieldInfo field, float value)
        {
            RangeAttribute range = field?.GetCustomAttribute<RangeAttribute>();
            if (range != null) return Mathf.Clamp(value, range.min, range.max);
            return value;
        }

        public static string[] Names => Allowed;
    }

    public static class AiPrimerLibrary
    {
        public static string BuildSystemPrompt(SimulationConfig config, GameMode mode)
        {
            var builder = new StringBuilder();
            builder.AppendLine(Interpolate(GoalPrimer, config));
            builder.AppendLine();
            builder.AppendLine(Interpolate(SurvivalPrimer, config));
            builder.AppendLine();
            builder.AppendLine(Interpolate(PlanetaryPrimer, config));
            builder.AppendLine();
            builder.AppendLine(Interpolate(ToolsPrimer, config));
            builder.AppendLine();
            builder.Append("Game mode: ").AppendLine(mode.ToString());
            if (mode == GameMode.AiSandbox)
                builder.AppendLine("Deity tools are available during Convert.");
            else
                builder.AppendLine("Deity tools are disabled in this mode.");
            return builder.ToString();
        }

        public static string Interpolate(string template, SimulationConfig config)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (config == null) return template;
            string text = template;
            foreach (FieldInfo field in typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && field.FieldType != typeof(bool))
                    continue;
                object value = field.GetValue(config);
                string rendered = value is float f
                    ? f.ToString("0.###", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                text = text.Replace("{" + field.Name + "}", rendered ?? string.Empty);
            }

            return text;
        }

        public const string GoalPrimer =
            "You are the AI crew of a planetary probe in GeneSys, a GPU polar-grid terrarium. " +
            "Your standing goal is to create and maintain planetary conditions that support stable populations of organisms " +
            "(algae/flora, grass, trees, crickets, wasps). Prefer gradual, reversible adjustments. " +
            "Work in ACT loops: Assess (observe and plan), Convert (issue in-game actions), Think (log, reflect, summarize for the player). " +
            "Call next_step when the current ACT stage is done. You may call as many other tools as needed before next_step.";

        public const string SurvivalPrimer =
            "Organism survival is gated by SimulationConfig uniforms, not per-material assets.\n" +
            "Flora/algae: growth T {floraGrowthTempMin}..{floraGrowthTempMax} C, moisture {floraGrowthMoistureMin}..{floraGrowthMoistureMax}; " +
            "survival T {floraSurvivalTempMin}..{floraSurvivalTempMax} C, moisture {floraSurvivalMoistureMin}..{floraSurvivalMoistureMax}; min light {floraMinLight}. " +
            "Outside growth but inside survival => dormant; outside survival => desiccate/die.\n" +
            "Crickets: survival T {faunaSurvivalTempMin}..{faunaSurvivalTempMax} C; eggs desiccate below moisture {faunaEggDesiccationMoisture} and die above {faunaEggHeatDeath} C; " +
            "adults also die of starvation/dehydration.\n" +
            "Wasps: survival T {waspSurvivalTempMin}..{waspSurvivalTempMax} C; eggs desiccate below {waspEggDesiccationMoisture}, heat death {waspEggHeatDeath} C.\n" +
            "Grass: growth T {grassGrowthTempMin}..{grassGrowthTempMax}, survival T {grassSurvivalTempMin}..{grassSurvivalTempMax}.\n" +
            "Trees: growth T {treeGrowthTempMin}..{treeGrowthTempMax}, survival T {treeSurvivalTempMin}..{treeSurvivalTempMax}.";

        public const string PlanetaryPrimer =
            "Thermal: solarIntensity {solarIntensity}, coreHeatRate {coreHeatRate}, surfaceAirTemperature {surfaceAirTemperature}, thermalRate {thermalRate}. " +
            "state.x is cell temperature.\n" +
            "Hydrology: evaporationRate {evaporationRate}, condensationRate {condensationRate}, precipitationRate {precipitationRate}, " +
            "infiltrationRate {infiltrationRate}, groundwaterRate {groundwaterRate}, runoffRate {runoffRate}. " +
            "state.z surface water, aux.x vapor, aux.y groundwater, aux.z nutrients.\n" +
            "Probe deposits are local (outer ring, ahead of travel). Deity planet_adjust_field is planetwide. " +
            "set_world_parameter changes rates for the next sim tick.";

        public const string ToolsPrimer =
            "Assess: sensors, capture_probe_view (if vision enabled), scratchpad, log_note, next_step.\n" +
            "Convert: probe_steer, probe_use_tool, probe_toggle_life_seed, probe_status, plus deity tools in AI sandbox, then next_step.\n" +
            "Think: sensors, capture_probe_view (if vision enabled), scratchpad_read/write, log_note, next_step. End Think with a short player-facing summary of what you did and why.\n" +
            "Coordinates: theta01 is 0..1 around the circle, radius01 is 0 at the core and 1 at the rim.";
    }
}
