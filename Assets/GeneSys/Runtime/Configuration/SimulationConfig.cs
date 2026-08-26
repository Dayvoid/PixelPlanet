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
        public bool useOgWorldgen = false;

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
        [Range(0, 64)] public int metalVeinCount = 12;
        [Range(0.005f, 0.08f)] public float metalVeinMinSize = 0.012f;
        [Range(0.01f, 0.15f)] public float metalVeinMaxSize = 0.045f;
        [Range(0f, 1f)] public float metalVeinProtrusionChance = 0.35f;
        [Range(0f, 0.08f)] public float metalVeinProtrusionDistance = 0.025f;
        [Range(0.02f, 0.25f)] public float iceCapRadius = 0.08f;
        [Range(0.005f, 0.06f)] public float iceCapHeight = 0.018f;
        [Range(0f, 0.5f)] public float iceCapRadiusVariation = 0.25f;
        [Range(0f, 0.5f)] public float iceCapHeightVariation = 0.3f;

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
        [Range(0, 10000)] public int coreReactionFrequency = 400;
        [Range(0f, 500f)] public float coreReactionMagnitude = 8f;

        [Header("Hydrology and erosion")]
        [Range(0f, 4f)] public float infiltrationRate = 0.3f;
        [Range(0f, 4f)] public float groundwaterRate = 0.18f;
        [Range(0f, 1f)] public float fieldCapacityFraction = 0.45f;
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
        [Range(0f, 1f)] public float surfaceWaterPixelThreshold = 0.55f;
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
        [Range(0f, 1f)] public float rainPixelFormationThreshold = 0.35f;
        [Range(0f, 4f)] public float surfaceAirHeatExchange = 0.45f;
        [Range(0f, 4f)] public float temperatureAdvectionRate = 0.55f;
        [Range(0f, 4f)] public float pressureCompressibility = 0.45f;
        [Range(0.05f, 1f)] public float atmosphericCflLimit = 0.4f;
        [Range(-20f, 40f)] public float surfaceAirTemperature = 18f;
        [Range(0f, 40f)] public float atmosphericLapseRate = 12f;

        [Header("Ecology - Mycology")]
        [Range(0f, 1f)] public float mycologyInitialSporeLoad = 0.08f;
        [Range(0f, 1f)] public float mycologyRareStrainChance = 0.04f;
        [Range(0f, 4f)] public float mycologyAirTransportRate = 0.55f;
        [Range(0f, 4f)] public float mycologyWaterTransportRate = 0.7f;
        [Range(0f, 2f)] public float mycologyDiffusionRate = 0.08f;
        [Range(0f, 4f)] public float mycologySettlingRate = 0.35f;
        [Range(0f, 4f)] public float mycologySporulationRate = 0.12f;
        [Range(0f, 4f)] public float mycologyGrowthRate = 0.18f;
        [Range(0f, 4f)] public float mycologyDecayRate = 0.22f;
        [Range(-40f, 80f)] public float mycologyGrowthTempMin = 5f;
        [Range(-40f, 120f)] public float mycologyGrowthTempMax = 32f;
        [Range(0f, 2f)] public float mycologyGrowthMoistureMin = 0.08f;
        [Range(0f, 2f)] public float mycologyGrowthMoistureMax = 0.85f;
        [Range(-80f, 80f)] public float mycologySurvivalTempMin = -5f;
        [Range(-40f, 160f)] public float mycologySurvivalTempMax = 45f;
        [Range(0f, 2f)] public float mycologySurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float mycologySurvivalMoistureMax = 1.2f;
        [Range(0f, 8f)] public float mycologyElectricalTolerance = 0.65f;
        [Range(0f, 1f)] public float mycologyTraitEffectStrength = 0.35f;

        [Header("Ecology - Flora")]
        public bool floraSeedAtWorldgen = false;
        [Range(0f, 1f)] public float floraInitialSporeLoad = 0.06f;
        [Range(0f, 4f)] public float floraAirTransportRate = 0.5f;
        [Range(0f, 4f)] public float floraWaterTransportRate = 0.65f;
        [Range(0f, 2f)] public float floraDiffusionRate = 0.07f;
        [Range(0f, 4f)] public float floraSettlingRate = 0.4f;
        [Range(0f, 4f)] public float floraSporulationRate = 0.1f;
        [Range(0f, 4f)] public float floraGrowthRate = 0.16f;
        [Range(0f, 4f)] public float floraDecayRate = 0.12f;
        [Range(0f, 4f)] public float floraPhotosynthesisRate = 0.35f;
        [Range(0f, 4f)] public float floraOxygenYield = 0.2f;
        [Range(0f, 4f)] public float floraExudationRate = 0.12f;
        [Range(0.05f, 1f)] public float floraReproductionThreshold = 0.55f;
        [Range(0f, 1f)] public float floraBaseMutationRate = 0.04f;
        [Range(0f, 4f)] public float floraToxinMutationScale = 1.2f;
        [Range(0f, 1f)] public float floraGeneExpressionRange = 0.45f;
        [Range(-40f, 80f)] public float floraGrowthTempMin = 8f;
        [Range(-40f, 120f)] public float floraGrowthTempMax = 34f;
        [Range(0f, 2f)] public float floraGrowthMoistureMin = 0.1f;
        [Range(0f, 2f)] public float floraGrowthMoistureMax = 1.15f;
        [Range(-80f, 80f)] public float floraSurvivalTempMin = -8f;
        [Range(-40f, 160f)] public float floraSurvivalTempMax = 72f;
        [Range(0f, 2f)] public float floraSurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float floraSurvivalMoistureMax = 1.5f;
        [Range(0f, 2f)] public float floraMinLight = 0.08f;
        [Range(0.001f, 1f)] public float floraGerminationSporeThreshold = 0.08f;
        [Range(0f, 4f)] public float floraMaintenanceRate = 0.06f;
        [Range(0f, 4f)] public float floraNightDrain = 0.04f;
        [Range(0f, 1f)] public float floraDormancyMetabolicScale = 0.12f;
        [Range(0f, 8f)] public float floraWindDispersalRate = 0.35f;
        [Range(0f, 8f)] public float floraRainDispersalRate = 0.45f;
        [Range(0f, 8f)] public float floraStackMigrationRate = 0.2f;

        [Header("Combustion")]
        [Range(0f, 2f)] public float combustionAmbientOxygen = 1f;
        [Range(0f, 4f)] public float combustionOxygenReplenishRate = 0.15f;
        [Range(0f, 4f)] public float combustionOxygenDiffusionRate = 0.35f;
        [Range(0f, 8f)] public float combustionIgnitionAccumulationRate = 2.5f;
        [Range(0f, 8f)] public float combustionIgnitionDecayRate = 1.2f;
        [Range(0f, 1f)] public float combustionSeedIntensity = 0.55f;
        [Range(0f, 4f)] public float combustionBurnRate = 0.45f;
        [Range(0f, 8f)] public float combustionHeatYield = 2.8f;
        [Range(0f, 8f)] public float combustionPressureScale = 2.5f;
        [Range(0f, 12f)] public float combustionUpdraftStrength = 6f;
        [Range(0f, 4f)] public float combustionSmokeYield = 0.55f;
        [Range(0f, 4f)] public float combustionSootSettlingRate = 0.2f;
        [Range(0f, 4f)] public float combustionPyroFertilityYield = 0.18f;
        [Range(0f, 8f)] public float combustionMoistureIgnitionPenalty = 2.5f;
        [Range(0f, 4f)] public float combustionSteamSuppression = 0.85f;
        [Range(0f, 4f)] public float combustionFlameDecay = 0.35f;
        [Range(0f, 4f)] public float combustionFlashVaporizationRate = 0.55f;
        [Range(0f, 1f)] public float combustionMinFuel = 0.02f;
        [Range(0f, 1f)] public float combustionMinOxygen = 0.05f;
        [Range(0f, 2f)] public float combustionSuppressionMoisture = 0.55f;

        [Header("Storm and lightning")]
        [Range(0f, 4f)] public float stormChargeSeparationRate = 0.35f;
        [Range(0f, 4f)] public float stormChargeLeakRate = 0.08f;
        [Range(0f, 4f)] public float stormChargeDiffusionRate = 0.12f;
        [Range(0f, 4f)] public float stormChargeAdvectionRate = 0.45f;
        [Range(-80f, 40f)] public float stormRimingTempMin = -25f;
        [Range(-40f, 40f)] public float stormRimingTempMax = 5f;
        [Range(0.01f, 4f)] public float stormBreakdownThreshold = 1.6f;
        [Range(0f, 8f)] public float stormBreakdownAccumulationRate = 0.85f;
        [Range(0f, 8f)] public float stormChannelDecay = 2.5f;
        [Range(0f, 8f)] public float stormFlashDecay = 1.8f;
        [Range(0f, 4f)] public float stormFlashDiffusion = 0.45f;
        [Range(0f, 4f)] public float stormCooldownRate = 0.4f;
        [Range(0f, 400f)] public float stormStrikeHeat = 90f;
        [Range(0f, 8f)] public float stormThunderPressure = 0.55f;
        [Range(0f, 8f)] public float stormChargeDeposit = 0.85f;
        [Range(0f, 4f)] public float stormIgnitionImpulse = 1f;
        [Range(0f, 4f)] public float stormFlashVaporization = 0.35f;
        [Range(0f, 4f)] public float stormChannelChargeDrain = 0.55f;
        [Range(0f, 1f)] public float stormTortuosity = 0.35f;
        [Range(1, 64)] public int stormTargetRange = 18;
        [Range(4, 128)] public int stormMaxChannelLength = 48;
        [Range(0, 32)] public int stormMaxStrikesPerTick = 4;
        [Range(0f, 1f)] public float stormStrikeBranchChance = 0.12f;
        [Range(0f, 1f)] public float stormSheetBranchChance = 0.45f;
        [Range(0, 2048)] public int stormMinimumHeight = 415;

        [Header("Graphics")]
        [Range(0, 1)] public int enableStarfield = 1;
        [Range(32, 512)] public int starCount = 300;
        [Range(0f, 2f)] public float starfieldStrength = 0.15f;
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

        [Header("Probe")]
        [Range(0.8f, 2f)] public float probeOrbitRadius = 1.28f;
        [Range(0.01f, 1f)] public float probeSpriteScale = 0.08f;
        [Range(-180f, 180f)] public float probeSpriteRotationOffset = 0f;
        [Min(1f)] public float probeOrbitPeriodSeconds = 180f;
        [Range(0f, 10f)] public float probeVaporRate = 1f;
        [Range(0f, 10f)] public float probeWaterRate = 1f;
        [Range(0f, 10f)] public float probeHeatRate = 1f;
        [Range(0f, 10f)] public float probeCoolRate = 1f;
        [Range(1, 64)] public int probeDepositRadius = 4;
        [Range(0f, 45f)] public float probeLeadDegrees = 2f;
        [Range(0.75f, 20f)] public float probeFollowZoom = 2.5f;

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
            metalVeinCount = Mathf.Clamp(metalVeinCount, 0, 64);
            metalVeinMinSize = Mathf.Clamp(metalVeinMinSize, 0.001f, metalVeinMaxSize);
            metalVeinMaxSize = Mathf.Max(metalVeinMinSize, metalVeinMaxSize);
            metalVeinProtrusionChance = Mathf.Clamp01(metalVeinProtrusionChance);
            metalVeinProtrusionDistance = Mathf.Max(0f, metalVeinProtrusionDistance);
            iceCapRadius = Mathf.Clamp(iceCapRadius, 0.01f, 0.35f);
            iceCapHeight = Mathf.Clamp(iceCapHeight, 0.001f, 0.1f);
            iceCapRadiusVariation = Mathf.Clamp01(iceCapRadiusVariation);
            iceCapHeightVariation = Mathf.Clamp01(iceCapHeightVariation);
            fieldCapacityFraction = Mathf.Clamp01(fieldCapacityFraction);
            atmosphericAdvectionRate = Mathf.Max(0f, atmosphericAdvectionRate);
            vaporDiffusionRate = Mathf.Max(0f, vaporDiffusionRate);
            atmosphericBuoyancy = Mathf.Max(0f, atmosphericBuoyancy);
            humidityBuoyancy = Mathf.Max(0f, humidityBuoyancy);
            saturationCapacityScale = Mathf.Max(0.01f, saturationCapacityScale);
            cloudPrecipitationThreshold = Mathf.Max(0.01f, cloudPrecipitationThreshold);
            rainPixelFormationThreshold = Mathf.Clamp01(rainPixelFormationThreshold);
            surfaceWaterPixelThreshold = Mathf.Clamp01(surfaceWaterPixelThreshold);
            surfaceAirHeatExchange = Mathf.Max(0f, surfaceAirHeatExchange);
            temperatureAdvectionRate = Mathf.Max(0f, temperatureAdvectionRate);
            pressureCompressibility = Mathf.Max(0f, pressureCompressibility);
            atmosphericCflLimit = Mathf.Clamp(atmosphericCflLimit, 0.05f, 1f);
            atmosphericLapseRate = Mathf.Max(0f, atmosphericLapseRate);
            mycologyInitialSporeLoad = Mathf.Max(0f, mycologyInitialSporeLoad);
            mycologyRareStrainChance = Mathf.Clamp01(mycologyRareStrainChance);
            mycologyAirTransportRate = Mathf.Max(0f, mycologyAirTransportRate);
            mycologyWaterTransportRate = Mathf.Max(0f, mycologyWaterTransportRate);
            mycologyDiffusionRate = Mathf.Max(0f, mycologyDiffusionRate);
            mycologySettlingRate = Mathf.Max(0f, mycologySettlingRate);
            mycologySporulationRate = Mathf.Max(0f, mycologySporulationRate);
            mycologyGrowthRate = Mathf.Max(0f, mycologyGrowthRate);
            mycologyDecayRate = Mathf.Max(0f, mycologyDecayRate);
            if (mycologyGrowthTempMax < mycologyGrowthTempMin)
            {
                float swap = mycologyGrowthTempMin;
                mycologyGrowthTempMin = mycologyGrowthTempMax;
                mycologyGrowthTempMax = swap;
            }
            if (mycologySurvivalTempMax < mycologySurvivalTempMin)
            {
                float swap = mycologySurvivalTempMin;
                mycologySurvivalTempMin = mycologySurvivalTempMax;
                mycologySurvivalTempMax = swap;
            }
            mycologySurvivalTempMin = Mathf.Min(mycologySurvivalTempMin, mycologyGrowthTempMin);
            mycologySurvivalTempMax = Mathf.Max(mycologySurvivalTempMax, mycologyGrowthTempMax);
            if (mycologyGrowthMoistureMax < mycologyGrowthMoistureMin)
            {
                float swap = mycologyGrowthMoistureMin;
                mycologyGrowthMoistureMin = mycologyGrowthMoistureMax;
                mycologyGrowthMoistureMax = swap;
            }
            if (mycologySurvivalMoistureMax < mycologySurvivalMoistureMin)
            {
                float swap = mycologySurvivalMoistureMin;
                mycologySurvivalMoistureMin = mycologySurvivalMoistureMax;
                mycologySurvivalMoistureMax = swap;
            }
            mycologySurvivalMoistureMin = Mathf.Min(mycologySurvivalMoistureMin, mycologyGrowthMoistureMin);
            mycologySurvivalMoistureMax = Mathf.Max(mycologySurvivalMoistureMax, mycologyGrowthMoistureMax);
            mycologyElectricalTolerance = Mathf.Max(0f, mycologyElectricalTolerance);
            mycologyTraitEffectStrength = Mathf.Clamp01(mycologyTraitEffectStrength);
            floraInitialSporeLoad = Mathf.Max(0f, floraInitialSporeLoad);
            floraAirTransportRate = Mathf.Max(0f, floraAirTransportRate);
            floraWaterTransportRate = Mathf.Max(0f, floraWaterTransportRate);
            floraDiffusionRate = Mathf.Max(0f, floraDiffusionRate);
            floraSettlingRate = Mathf.Max(0f, floraSettlingRate);
            floraSporulationRate = Mathf.Max(0f, floraSporulationRate);
            floraGrowthRate = Mathf.Max(0f, floraGrowthRate);
            floraDecayRate = Mathf.Max(0f, floraDecayRate);
            floraPhotosynthesisRate = Mathf.Max(0f, floraPhotosynthesisRate);
            floraOxygenYield = Mathf.Max(0f, floraOxygenYield);
            floraExudationRate = Mathf.Max(0f, floraExudationRate);
            floraReproductionThreshold = Mathf.Clamp(floraReproductionThreshold, 0.05f, 1f);
            floraBaseMutationRate = Mathf.Clamp01(floraBaseMutationRate);
            floraToxinMutationScale = Mathf.Max(0f, floraToxinMutationScale);
            floraGeneExpressionRange = Mathf.Clamp01(floraGeneExpressionRange);
            if (floraGrowthTempMax < floraGrowthTempMin)
            {
                float swap = floraGrowthTempMin;
                floraGrowthTempMin = floraGrowthTempMax;
                floraGrowthTempMax = swap;
            }
            if (floraSurvivalTempMax < floraSurvivalTempMin)
            {
                float swap = floraSurvivalTempMin;
                floraSurvivalTempMin = floraSurvivalTempMax;
                floraSurvivalTempMax = swap;
            }
            floraSurvivalTempMin = Mathf.Min(floraSurvivalTempMin, floraGrowthTempMin);
            floraSurvivalTempMax = Mathf.Max(floraSurvivalTempMax, floraGrowthTempMax);
            if (floraGrowthMoistureMax < floraGrowthMoistureMin)
            {
                float swap = floraGrowthMoistureMin;
                floraGrowthMoistureMin = floraGrowthMoistureMax;
                floraGrowthMoistureMax = swap;
            }
            if (floraSurvivalMoistureMax < floraSurvivalMoistureMin)
            {
                float swap = floraSurvivalMoistureMin;
                floraSurvivalMoistureMin = floraSurvivalMoistureMax;
                floraSurvivalMoistureMax = swap;
            }
            floraSurvivalMoistureMin = Mathf.Min(floraSurvivalMoistureMin, floraGrowthMoistureMin);
            floraSurvivalMoistureMax = Mathf.Max(floraSurvivalMoistureMax, floraGrowthMoistureMax);
            floraMinLight = Mathf.Max(0f, floraMinLight);
            floraGerminationSporeThreshold = Mathf.Clamp(floraGerminationSporeThreshold, 0.001f, 1f);
            floraMaintenanceRate = Mathf.Max(0f, floraMaintenanceRate);
            floraNightDrain = Mathf.Max(0f, floraNightDrain);
            floraDormancyMetabolicScale = Mathf.Clamp01(floraDormancyMetabolicScale);
            floraWindDispersalRate = Mathf.Max(0f, floraWindDispersalRate);
            floraRainDispersalRate = Mathf.Max(0f, floraRainDispersalRate);
            floraStackMigrationRate = Mathf.Max(0f, floraStackMigrationRate);
            combustionAmbientOxygen = Mathf.Max(0f, combustionAmbientOxygen);
            combustionOxygenReplenishRate = Mathf.Max(0f, combustionOxygenReplenishRate);
            combustionOxygenDiffusionRate = Mathf.Max(0f, combustionOxygenDiffusionRate);
            combustionIgnitionAccumulationRate = Mathf.Max(0f, combustionIgnitionAccumulationRate);
            combustionIgnitionDecayRate = Mathf.Max(0f, combustionIgnitionDecayRate);
            combustionSeedIntensity = Mathf.Clamp01(combustionSeedIntensity);
            combustionBurnRate = Mathf.Max(0f, combustionBurnRate);
            combustionHeatYield = Mathf.Max(0f, combustionHeatYield);
            combustionPressureScale = Mathf.Max(0f, combustionPressureScale);
            combustionUpdraftStrength = Mathf.Max(0f, combustionUpdraftStrength);
            combustionSmokeYield = Mathf.Max(0f, combustionSmokeYield);
            combustionSootSettlingRate = Mathf.Max(0f, combustionSootSettlingRate);
            combustionPyroFertilityYield = Mathf.Max(0f, combustionPyroFertilityYield);
            combustionMoistureIgnitionPenalty = Mathf.Max(0f, combustionMoistureIgnitionPenalty);
            combustionSteamSuppression = Mathf.Max(0f, combustionSteamSuppression);
            combustionFlameDecay = Mathf.Max(0f, combustionFlameDecay);
            combustionFlashVaporizationRate = Mathf.Max(0f, combustionFlashVaporizationRate);
            combustionMinFuel = Mathf.Clamp01(combustionMinFuel);
            combustionMinOxygen = Mathf.Clamp01(combustionMinOxygen);
            combustionSuppressionMoisture = Mathf.Max(0f, combustionSuppressionMoisture);
            stormChargeSeparationRate = Mathf.Max(0f, stormChargeSeparationRate);
            stormChargeLeakRate = Mathf.Max(0f, stormChargeLeakRate);
            stormChargeDiffusionRate = Mathf.Max(0f, stormChargeDiffusionRate);
            stormChargeAdvectionRate = Mathf.Max(0f, stormChargeAdvectionRate);
            if (stormRimingTempMax < stormRimingTempMin)
            {
                float swap = stormRimingTempMin;
                stormRimingTempMin = stormRimingTempMax;
                stormRimingTempMax = swap;
            }
            stormBreakdownThreshold = Mathf.Max(0.01f, stormBreakdownThreshold);
            stormBreakdownAccumulationRate = Mathf.Max(0f, stormBreakdownAccumulationRate);
            stormChannelDecay = Mathf.Max(0f, stormChannelDecay);
            stormFlashDecay = Mathf.Max(0f, stormFlashDecay);
            stormFlashDiffusion = Mathf.Max(0f, stormFlashDiffusion);
            stormCooldownRate = Mathf.Max(0f, stormCooldownRate);
            stormStrikeHeat = Mathf.Max(0f, stormStrikeHeat);
            stormThunderPressure = Mathf.Max(0f, stormThunderPressure);
            stormChargeDeposit = Mathf.Max(0f, stormChargeDeposit);
            stormIgnitionImpulse = Mathf.Max(0f, stormIgnitionImpulse);
            stormFlashVaporization = Mathf.Max(0f, stormFlashVaporization);
            stormChannelChargeDrain = Mathf.Max(0f, stormChannelChargeDrain);
            stormTortuosity = Mathf.Clamp01(stormTortuosity);
            stormTargetRange = Mathf.Clamp(stormTargetRange, 1, 64);
            stormMaxChannelLength = Mathf.Clamp(stormMaxChannelLength, 4, 128);
            stormMaxStrikesPerTick = Mathf.Clamp(stormMaxStrikesPerTick, 0, 32);
            stormStrikeBranchChance = Mathf.Clamp01(stormStrikeBranchChance);
            stormSheetBranchChance = Mathf.Clamp01(stormSheetBranchChance);
            stormMinimumHeight = Mathf.Clamp(stormMinimumHeight, 0, 2048);
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
            coreReactionFrequency = Mathf.Max(0, coreReactionFrequency);
            coreReactionMagnitude = Mathf.Max(0f, coreReactionMagnitude);
            probeOrbitRadius = Mathf.Clamp(probeOrbitRadius, 0.8f, 2f);
            probeSpriteScale = Mathf.Clamp(probeSpriteScale, 0.01f, 1f);
            probeSpriteRotationOffset = Mathf.Clamp(probeSpriteRotationOffset, -180f, 180f);
            probeOrbitPeriodSeconds = Mathf.Max(1f, probeOrbitPeriodSeconds);
            probeVaporRate = Mathf.Max(0f, probeVaporRate);
            probeWaterRate = Mathf.Max(0f, probeWaterRate);
            probeHeatRate = Mathf.Max(0f, probeHeatRate);
            probeCoolRate = Mathf.Max(0f, probeCoolRate);
            probeDepositRadius = Mathf.Clamp(probeDepositRadius, 1, 64);
            probeLeadDegrees = Mathf.Clamp(probeLeadDegrees, 0f, 45f);
            probeFollowZoom = Mathf.Clamp(probeFollowZoom, 0.75f, 20f);
        }
    }
}
