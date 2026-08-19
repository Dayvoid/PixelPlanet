using System.Reflection;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Serialization;

namespace GeneSys.Configuration
{
    public enum SimulationPreset { Validation, Standard, Stress }

    /// <summary>
    /// Canonical water mass contract: state.z = surface liquid/ice (or atmospheric cloud condensate on Air),
    /// aux.x = vapor humidity on Air carriers, aux.y = groundwater.
    /// Material IDs describe phase/appearance only; every transfer subtracts from one reservoir before adding to another.
    /// </summary>
    [CreateAssetMenu(menuName = "GeneSys/Simulation Config", fileName = "SimulationConfig")]
    public sealed class SimulationConfig : ScriptableObject
    {
        [Header("Grid and timing")]
        public SimulationPreset preset = SimulationPreset.Validation;
        public PolarGridDefinition grid = PolarGridDefinition.Validation;
        [Min(1f)] public float ticksPerSecond = 20f;
        [Range(0.05f, 16f)] public float simulationSpeed = 1f;
        [Range(1, 8)] public int materialSubsteps = 1;
        [Range(1, 32)] public int slowPassInterval = 4;
        public int seed = 12345;

        [Header("World generation")]
        [Range(0.05f, 0.5f)] public float coreRatio = 0.24f;
        [Range(0.05f, 0.6f)] public float mantleRatio = 0.42f;
        [Range(0.01f, 0.25f)] public float crustRatio = 0.13f;
        [Range(0.001f, 0.1f)] public float soilRatio = 0.025f;
        [Range(0f, 0.2f)] public float borderNoise = 0.035f;
        [Range(0f, 1f)] public float protrusionChance = 0.12f;
        [FormerlySerializedAs("initialWaterTable")]
        [Range(0f, 1f)] public float groundwaterDepth = 0.55f;
        [Range(0, 64)] public int faultCount = 12;
        [Range(0.2f, 0.8f)] public float targetOceanCoverage = 0.5f;
        [Range(2, 3)] public int minOceanBasins = 2;
        [Range(2, 3)] public int maxOceanBasins = 3;
        [Range(0f, 1f)] public float seaLevelRadius = 0f;
        [Range(0.01f, 0.2f)] public float basinDepth = 0.08f;
        [Range(0f, 0.15f)] public float terrainRelief = 0.045f;
        [Range(0f, 0.08f)] public float coastRoughness = 0.025f;
        [Range(0f, 1f)] public float initialGroundwaterSaturation = 0.65f;
        [Range(0f, 0.5f)] public float initialAtmosphericHumidity = 0.08f;

        [Header("Material mechanics")]
        [Range(0f, 5f)] public float gravityStrength = 1f;
        [Range(0f, 4f)] public float thermalRate = 0.35f;
        [Range(0f, 4f)] public float electricalRate = 0.3f;
        [Range(0f, 1f)] public float phaseHysteresis = 0.02f;
        [Range(0f, 64f)] public float densityExchangeRate = 4f;
        [Range(0.001f, 0.25f)] public float densityExchangeEpsilon = 0.02f;

        [Header("Geology")]
        [Range(0f, 4f)] public float mantlePressure = 0.7f;
        [Range(0f, 4f)] public float fractureRate = 0.2f;
        [Range(0f, 4f)] public float extrusionRate = 0.3f;
        [Range(0f, 2f)] public float volcanicCooling = 0.15f;
        [Range(0f, 2f)] public float magmaViscosity = 0.5f;
        [Range(0f, 4f)] public float hydrothermalStrength = 0.35f;
        [Range(0f, 4f)] public float ventChemicalRate = 0.12f;
        [Range(0f, 1f)] public float magmaEruption = 0f;
        [Range(0f, 4f)] public float eruptionPressureStrength = 1f;
        [Range(0f, 4f)] public float eruptionFlowStrength = 1f;
        [Range(1, 16)] public int eruptionBurdenDepth = 4;
        [Range(0.05f, 4f)] public float eruptionBlastThreshold = 1.25f;
        [Range(0f, 4f)] public float ashUpdraftStrength = 1f;
        [Range(0f, 4f)] public float ashSettlingStrength = 1f;
        [Range(0f, 4f)] public float ashFertilityStrength = 1f;

        [Header("Hydrology and erosion")]
        [Range(0f, 4f)] public float infiltrationRate = 0.3f;
        [Range(0f, 4f)] public float groundwaterRate = 0.18f;
        [Range(0f, 2f)] public float dissolutionRate = 0.03f;
        [Range(0f, 2f)] public float collapseRate = 0.03f;
        [Range(0f, 2f)] public float erosionRate = 0.06f;
        [Range(0f, 2f)] public float baseSoilCohesion = 0.45f;
        [Range(0f, 2f)] public float stressDecayRate = 0.02f;
        [Range(0.01f, 1f)] public float dryMoistureThreshold = 0.08f;
        [Range(0f, 2f)] public float moistureCohesionStrength = 0.85f;
        [Range(0f, 1f)] public float capillaryEvaporationFraction = 0.35f;
        [Range(0f, 4f)] public float runoffRate = 0.45f;
        [Range(0f, 4f)] public float pondingRate = 0.25f;
        [Range(0f, 1f)] public float springHeadThreshold = 0.55f;
        [Range(0f, 4f)] public float springDischargeRate = 0.35f;
        [Range(0f, 4f)] public float geyserHeatThreshold = 120f;
        [Range(0f, 4f)] public float geyserDischargeRate = 0.5f;
        [Range(0f, 8f)] public float geyserCooldownSeconds = 2.5f;

        [Header("Solar and weather")]
        [Min(1f)] public float dayLengthSeconds = 180f;
        [Range(0f, 4f)] public float solarIntensity = 0.8f;
        [Range(-100f, 100f)] public float spaceTemperature = -25f;
        [Range(0f, 4f)] public float radiativeCooling = 0.2f;
        [Range(0f, 4f)] public float windStrength = 0.35f;
        [Range(0f, 1f)] public float windDamping = 0.06f;
        [Range(0f, 4f)] public float evaporationRate = 0.1f;
        [Range(0f, 4f)] public float condensationRate = 0.12f;
        [Range(0f, 4f)] public float precipitationRate = 0.2f;
        [Range(0f, 4f)] public float vaporPressureScale = 0.25f;
        [Range(0f, 4f)] public float pressureRate = 0.4f;
        [Range(0f, 4f)] public float pressureDiffusionRate = 0.5f;
        [Range(0f, 4f)] public float gasPressureDiffusivity = 1f;
        [Range(0f, 4f)] public float fluidPressureDiffusivity = 0.35f;
        [Range(0f, 4f)] public float porousPressureDiffusivity = 0.12f;
        [Range(0f, 4f)] public float rigidPressureDiffusivity = 0.02f;
        [Range(0f, 8f)] public float pressureEquilibriumGradient = 2f;
        [Range(0f, 8f)] public float pressureEquilibriumMaximum = 2f;
        [Range(0f, 4f)] public float atmosphericAdvectionRate = 0.85f;
        [Range(0f, 2f)] public float vaporDiffusionRate = 0.05f;
        [Range(0f, 4f)] public float atmosphericBuoyancy = 0.4f;
        [Range(0f, 4f)] public float humidityBuoyancy = 0.25f;
        [Range(0.01f, 2f)] public float saturationCapacityScale = 0.55f;
        [Range(0.01f, 1f)] public float cloudPrecipitationThreshold = 0.05f;
        [Range(0f, 4f)] public float surfaceAirHeatExchange = 0.45f;
        [Range(0f, 4f)] public float temperatureAdvectionRate = 0.55f;
        [Range(0f, 4f)] public float pressureCompressibility = 0.45f;
        [Range(0.05f, 1f)] public float atmosphericCflLimit = 0.4f;
        [Range(-20f, 40f)] public float surfaceAirTemperature = 18f;
        [Range(0f, 40f)] public float atmosphericLapseRate = 12f;

        [Header("Graphics")]
        [Range(0, 1)] public int enableStarfield = 1;
        [Range(32, 512)] public int starCount = 160;
        [Range(0f, 2f)] public float starfieldStrength = 0.85f;
        [Range(0f, 2f)] public float starTwinkleStrength = 0.65f;
        [Range(0, 1)] public int enableNebula = 1;
        [Range(4, 48)] public int nebulaCount = 12;
        [Range(0f, 2f)] public float nebulaStrength = 0.45f;
        [Range(0, 1)] public int enableAtmosphereGlow = 1;
        [Range(0f, 2f)] public float atmosphereGlowStrength = 0.7f;
        [Range(4f, 48f)] public float atmosphereGlowPixelScale = 18f;
        [Range(1f, 24f)] public float atmosphereGlowRayCount = 7f;
        [Range(0, 1)] public int enableSolarBody = 1;
        [Range(0f, 2f)] public float solarBodyStrength = 1f;
        [Range(0f, 2f)] public float solarCoronaStrength = 0.85f;
        [Range(0.5f, 2f)] public float solarOrbitRadius = 1.35f;
        [Range(0f, 2f)] public float dayNightLightingStrength = 0.85f;

        [Header("Tools and validation")]
        [Range(1, 64)] public int brushRadius = 5;
        [Range(0.01f, 10f)] public float brushStrength = 1f;
        [Min(1)] public int validationIntervalTicks = 1000;
        [Range(0.0001f, 0.1f)] public float conservationTolerance = 0.02f;

        public void RestoreDefaults()
        {
            SimulationConfig defaults = CreateInstance<SimulationConfig>();
            foreach (FieldInfo field in typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(this, field.GetValue(defaults));
            DestroyImmediate(defaults);
            ApplyPreset(preset);
        }

        public void ApplyPreset(SimulationPreset value)
        {
            preset = value;
            grid = value switch
            {
                SimulationPreset.Standard => PolarGridDefinition.Standard,
                SimulationPreset.Stress => PolarGridDefinition.Stress,
                _ => PolarGridDefinition.Validation
            };
            grid.Validate();
        }

        private void OnValidate()
        {
            grid.Validate();
            ticksPerSecond = Mathf.Max(1f, ticksPerSecond);
            dayLengthSeconds = Mathf.Max(1f, dayLengthSeconds);
            minOceanBasins = Mathf.Clamp(minOceanBasins, 2, 3);
            maxOceanBasins = Mathf.Clamp(maxOceanBasins, minOceanBasins, 3);
            atmosphericAdvectionRate = Mathf.Max(0f, atmosphericAdvectionRate);
            vaporDiffusionRate = Mathf.Max(0f, vaporDiffusionRate);
            atmosphericBuoyancy = Mathf.Max(0f, atmosphericBuoyancy);
            humidityBuoyancy = Mathf.Max(0f, humidityBuoyancy);
            saturationCapacityScale = Mathf.Max(0.01f, saturationCapacityScale);
            cloudPrecipitationThreshold = Mathf.Max(0.01f, cloudPrecipitationThreshold);
            surfaceAirHeatExchange = Mathf.Max(0f, surfaceAirHeatExchange);
            temperatureAdvectionRate = Mathf.Max(0f, temperatureAdvectionRate);
            pressureCompressibility = Mathf.Max(0f, pressureCompressibility);
            atmosphericCflLimit = Mathf.Clamp(atmosphericCflLimit, 0.05f, 1f);
            atmosphericLapseRate = Mathf.Max(0f, atmosphericLapseRate);
            enableStarfield = enableStarfield != 0 ? 1 : 0;
            starCount = Mathf.Clamp(starCount, 32, 512);
            starfieldStrength = Mathf.Max(0f, starfieldStrength);
            starTwinkleStrength = Mathf.Max(0f, starTwinkleStrength);
            enableNebula = enableNebula != 0 ? 1 : 0;
            nebulaCount = Mathf.Clamp(nebulaCount, 4, 48);
            nebulaStrength = Mathf.Max(0f, nebulaStrength);
            enableAtmosphereGlow = enableAtmosphereGlow != 0 ? 1 : 0;
            atmosphereGlowStrength = Mathf.Max(0f, atmosphereGlowStrength);
            atmosphereGlowPixelScale = Mathf.Clamp(atmosphereGlowPixelScale, 4f, 48f);
            atmosphereGlowRayCount = Mathf.Clamp(atmosphereGlowRayCount, 1f, 24f);
            enableSolarBody = enableSolarBody != 0 ? 1 : 0;
            solarBodyStrength = Mathf.Max(0f, solarBodyStrength);
            solarCoronaStrength = Mathf.Max(0f, solarCoronaStrength);
            solarOrbitRadius = Mathf.Clamp(solarOrbitRadius, 0.5f, 2f);
            dayNightLightingStrength = Mathf.Max(0f, dayNightLightingStrength);
        }
    }
}
