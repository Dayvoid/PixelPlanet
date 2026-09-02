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
        public SimulationPreset preset = SimulationPreset.Standard;
        public PolarGridDefinition grid = PolarGridDefinition.Standard;
        [Min(1f)] public float ticksPerSecond = 20f;
        [Range(0.05f, 16f)] public float simulationSpeed = 1f;
        [Range(1, 8)] public int materialSubsteps = 1;
        [Range(1, 32)] public int slowPassInterval = 4;
        [Range(1, 8)] public int transportPassInterval = 2;
        public int seed = 12345;
        public bool useOgWorldgen = false;

        [Header("World generation")]
        [Range(0.05f, 0.5f)] public float coreRatio = 0.24f;
        [Range(0.05f, 0.6f)] public float mantleRatio = 0.38f;
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
        public bool frozenOceans = false;
        [Range(0f, 1f)] public float initialGroundwaterSaturation = 0.65f;
        [Range(0f, 1f)] public float initialAtmosphericHumidity = 0.7f;
        [Range(0, 64)] public int metalVeinCount = 12;
        [Range(0.001f, 0.08f)] public float metalVeinMinSize = 0.002f;
        [Range(0.01f, 0.15f)] public float metalVeinMaxSize = 0.01f;
        [Range(0f, 1f)] public float metalVeinProtrusionChance = 0f;
        [Range(0f, 0.08f)] public float metalVeinProtrusionDistance = 0.02f;
        [Range(0.001f, 0.25f)] public float iceCapRadius = 0.001f;
        [Range(0.001f, 0.06f)] public float iceCapHeight = 0.003f;
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
        [Range(0f, 4f)] public float extrusionRate = 0.4f;
        [Range(0f, 2f)] public float volcanicCooling = 0.15f;
        [Range(0f, 2f)] public float magmaViscosity = 0.5f;
        [Range(0f, 4f)] public float hydrothermalStrength = 0.35f;
        [Range(0f, 4f)] public float ventChemicalRate = 0.12f;
        [Range(0f, 1f)] public float magmaEruption = 1f;
        [Range(0f, 4f)] public float eruptionPressureStrength = 3f;
        [Range(0f, 8f)] public float eruptionFlowStrength = 5f;
        [Range(1, 16)] public int eruptionBurdenDepth = 10;
        [Range(0.05f, 4f)] public float eruptionBlastThreshold = 1.25f;
        [Range(0f, 4f)] public float ashUpdraftStrength = 0.4f;
        [Range(0f, 4f)] public float ashSettlingStrength = 2f;
        [Range(0f, 4f)] public float ashFertilityStrength = 1f;
        [Range(0, 10000)] public int coreReactionFrequency = 2000;
        [Range(0f, 500f)] public float coreReactionMagnitude = 8f;

        [Header("Hydrology and erosion")]
        [Range(0f, 4f)] public float infiltrationRate = 0.5f;
        [Range(0f, 4f)] public float groundwaterRate = 0.35f;
        [Range(0f, 1f)] public float fieldCapacityFraction = 0.7f;
        [Range(0f, 2f)] public float dissolutionRate = 0.03f;
        [Range(0f, 2f)] public float collapseRate = 0.03f;
        [Range(0f, 2f)] public float erosionRate = 0.04f;
        [Range(0f, 2f)] public float baseSoilCohesion = 0.75f;
        [Range(0f, 2f)] public float stressDecayRate = 0.02f;
        [Range(0.01f, 1f)] public float dryMoistureThreshold = 0.08f;
        [Range(0f, 2f)] public float moistureCohesionStrength = 0.85f;
        [Range(0f, 1f)] public float capillaryEvaporationFraction = 0.5f;
        [Range(0f, 4f)] public float runoffRate = 0.45f;
        [Range(1, 64)] public int hydrostaticIterations = 16;
        [Range(0f, 4f)] public float pondingRate = 0.85f;
        [Range(0f, 1f)] public float springHeadThreshold = 0.9f;
        [Range(0f, 4f)] public float springDischargeRate = 0.9f;
        [Range(0f, 500f)] public float geyserHeatThreshold = 320f;
        [Range(0f, 4f)] public float geyserDischargeRate = 0.5f;
        [Range(0f, 8f)] public float geyserCooldownSeconds = 2.5f;

        [Header("Solar and weather")]
        [Min(1f)] public float dayLengthSeconds = 180f;
        [Range(0f, 4f)] public float terrainSolarHeating = 0.8f;
        [FormerlySerializedAs("solarIntensity")]
        [Range(0f, 4f)] public float atmosphereSolarHeating = 0.8f;
        [Range(0.05f, 1f)] public float solarPolarOutputMin = 0.66f;
        [Range(0f, 1f)] public float solarTerrainPenetration = 1f;
        [Range(-100f, 100f)] public float spaceTemperature = 0f;
        [FormerlySerializedAs("radiativeCooling")]
        [Range(0f, 4f)] public float terrainRadiativeCooling = 0.25f;
        [Range(0f, 4f)] public float atmosphereRadiativeCooling = 0.25f;
        [Range(0f, 4f)] public float windStrength = 2f;
        [Range(0f, 1f)] public float windDamping = 0.001f;
        [Range(0f, 4f)] public float evaporationRate = 0.5f;
        [Range(0f, 4f)] public float condensationRate = 0.0125f;
        [Range(0f, 4f)] public float precipitationRate = 0.125f;
        [Range(0f, 4f)] public float vaporPressureScale = 0.25f;
        [Range(0f, 4f)] public float pressureRate = 0.4f;
        [Range(0f, 4f)] public float pressureDiffusionRate = 0.5f;
        [Range(0f, 4f)] public float gasPressureDiffusivity = 1f;
        [Range(0f, 4f)] public float fluidPressureDiffusivity = 0.35f;
        [Range(0f, 4f)] public float porousPressureDiffusivity = 0.12f;
        [Range(0f, 4f)] public float rigidPressureDiffusivity = 0.02f;
        [Range(0f, 8f)] public float pressureEquilibriumGradient = 2f;
        [Range(0f, 8f)] public float pressureEquilibriumMaximum = 2f;
        [Range(0f, 4f)] public float atmosphericAdvectionRate = 0.8f;
        [Range(0f, 2f)] public float vaporDiffusionRate = 0.33f;
        [Range(0f, 4f)] public float atmosphericBuoyancy = 0.9f;
        [Range(0f, 4f)] public float humidityBuoyancy = 0.8f;
        [Range(0f, 4f)] public float verticalBuoyancyStrength = 1f;
        [Range(0.01f, 2f)] public float saturationCapacityScale = 0.01f;
        [Range(0.01f, 1f)] public float cloudPrecipitationThreshold = 0.9f;
        [Range(0f, 4f)] public float waterPressureResponse = 0.6f;
        [Range(0f, 4f)] public float latentHeatScale = 0.35f;
        [Range(0f, 4f)] public float surfaceAirHeatExchange = 1f;
        [Range(0f, 4f)] public float temperatureAdvectionRate = 0.8f;
        [Range(0f, 4f)] public float pressureCompressibility = 0.6f;
        [Range(0.05f, 1f)] public float atmosphericCflLimit = 0.4f;
        [Range(-20f, 40f)] public float surfaceAirTemperature = 25f;
        [Range(0f, 40f)] public float atmosphericLapseRate = 10f;

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
        [Range(-40f, 120f)] public float mycologyGrowthTempMax = 90f;
        [Range(0f, 2f)] public float mycologyGrowthMoistureMin = 0.08f;
        [Range(0f, 2f)] public float mycologyGrowthMoistureMax = 0.85f;
        [Range(-80f, 80f)] public float mycologySurvivalTempMin = -5f;
        [Range(-40f, 160f)] public float mycologySurvivalTempMax = 100f;
        [Range(0f, 2f)] public float mycologySurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float mycologySurvivalMoistureMax = 1.2f;
        [Range(0f, 8f)] public float mycologyElectricalTolerance = 0.65f;
        [Range(0f, 1f)] public float mycologyTraitEffectStrength = 0.35f;

        [Header("Ecology - Algae")]
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
        [Range(-40f, 120f)] public float floraGrowthTempMax = 75f;
        [Range(0f, 2f)] public float floraGrowthMoistureMin = 0.1f;
        [Range(0f, 2f)] public float floraGrowthMoistureMax = 1.15f;
        [Range(-80f, 80f)] public float floraSurvivalTempMin = -8f;
        [Range(-40f, 160f)] public float floraSurvivalTempMax = 80f;
        [Range(0f, 2f)] public float floraSurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float floraSurvivalMoistureMax = 1.5f;
        [Range(0f, 2f)] public float floraMinLight = 0.08f;
        [Range(0.001f, 1f)] public float floraGerminationSporeThreshold = 0.08f;
        [Range(0f, 4f)] public float floraMaintenanceRate = 0.06f;
        [Range(0f, 4f)] public float floraNightDrain = 0.04f;
        [Range(0f, 1f)] public float floraDormancyMetabolicScale = 0.12f;
        [Range(0f, 4f)] public float floraPoleDriftRate = 0.35f;
        [Range(0f, 4f)] public float floraWindShearRate = 0.5f;
        [Range(0f, 4f)] public float floraRainShearRate = 0.4f;
        [Range(0f, 1f)] public float floraFragmentYield = 0.25f;
        [Range(0f, 4f)] public float floraAnchorGrip = 1f;

        [Header("Ecology - Cricket")]
        public bool faunaSeedAtWorldgen = false;
        [Range(0f, 1f)] public float faunaInitialCalories = 0.45f;
        [Range(0f, 1f)] public float faunaInitialHydration = 0.7f;
        [Range(1, 20000)] public int faunaMaturityTicks = 2000;
        [Range(1, 16)] public int faunaDecisionInterval = 2;
        [Range(0f, 4f)] public float faunaMaintenanceRate = 0.04f;
        [Range(0f, 4f)] public float faunaHydrationDrain = 0.03f;
        [Range(0.05f, 2f)] public float faunaCalorieCapacity = 1f;
        [Range(0.05f, 1f)] public float faunaFullThreshold = 0.7f;
        [Range(0.05f, 1f)] public float faunaHungerThreshold = 0.35f;
        [Range(0.05f, 1f)] public float faunaReproductionCalorieThreshold = 0.55f;
        [Range(0f, 16f)] public float faunaHopImpulse = 10f;
        [Range(0f, 1f)] public float faunaHopCost = 0.04f;
        [Range(0f, 1f)] public float faunaFeedCost = 0.015f;
        [Range(0.05f, 4f)] public float faunaDryMass = 0.35f;
        [Range(0f, 4f)] public float faunaDrag = 0.45f;
        [Range(0f, 4f)] public float faunaWindResistance = 0.5f;
        [Range(0f, 4f)] public float faunaMoistureMass = 0.4f;
        [Range(0f, 2f)] public float faunaSupportBoost = 0.35f;
        [Range(0f, 2f)] public float faunaWetPenalty = 0.4f;
        [Range(0f, 1f)] public float faunaGeneExpressionRange = 0.45f;
        [Range(0f, 1f)] public float faunaBaseMutationRate = 0.05f;
        [Range(1, 16)] public int faunaSenseRadius = 6;
        [Range(0f, 16f)] public float faunaHearingRange = 8f;
        [Range(20f, 200f)] public float faunaThreatTemperature = 90f;
        [Range(0f, 2f)] public float faunaAcousticSpeed = 0.45f;
        [Range(0f, 2f)] public float faunaAcousticDamping = 0.12f;
        [Range(0f, 2f)] public float faunaFeedCallAmplitude = 0.55f;
        [Range(0f, 2f)] public float faunaMateCallAmplitude = 0.65f;
        [Range(0, 8000)] public int faunaMateCooldownTicks = 400;
        [Range(0, 8000)] public int faunaReproduceCooldownTicks = 800;
        [Range(1, 8)] public int faunaClutchMin = 2;
        [Range(1, 8)] public int faunaClutchMax = 4;
        [Range(1, 8000)] public int faunaHatchTicksMin = 1000;
        [Range(1, 8000)] public int faunaHatchTicksMax = 1500;
        [Range(0f, 1f)] public float faunaEggDesiccationMoisture = 0.04f;
        [Range(20f, 200f)] public float faunaEggHeatDeath = 90f;
        [Range(0f, 4f)] public float faunaEggDisplacement = 0.25f;
        [Range(0f, 4f)] public float faunaWanderRate = 0.35f;
        [Range(-80f, 80f)] public float faunaSurvivalTempMin = -12f;
        [Range(-40f, 160f)] public float faunaSurvivalTempMax = 75f;

        [Header("Ecology - Wasp")]
        public bool waspSeedAtWorldgen = false;
        [Range(0f, 1f)] public float waspInitialCalories = 0.5f;
        [Range(0f, 1f)] public float waspInitialHydration = 0.7f;
        [Range(1, 20000)] public int waspMaturityTicks = 1600;
        [Range(1, 16)] public int waspDecisionInterval = 2;
        [Range(0f, 4f)] public float waspMaintenanceRate = 0.035f;
        [Range(0f, 4f)] public float waspFlightDrain = 0.05f;
        [Range(0f, 4f)] public float waspHydrationDrain = 0.04f;
        [Range(0.05f, 2f)] public float waspCalorieCapacity = 1f;
        [Range(0.05f, 1f)] public float waspFullThreshold = 0.75f;
        [Range(0.05f, 1f)] public float waspHungerThreshold = 0.45f;
        [Range(0f, 1f)] public float waspStarvationThreshold = 0.12f;
        [Range(1f, 16f)] public float waspCruiseAltitude = 5f;
        [Range(0f, 4f)] public float waspAltitudeGain = 1.2f;
        [Range(0f, 16f)] public float waspLiftPower = 4.5f;
        [Range(1, 24)] public int waspSurfaceScanRange = 12;
        [Range(0.05f, 4f)] public float waspBodyMass = 0.22f;
        [Range(0f, 4f)] public float waspDrag = 1.4f;
        [Range(0f, 4f)] public float waspWindCoupling = 0.6f;
        [Range(0f, 4f)] public float waspUpdraftCoupling = 0.9f;
        [Range(0f, 16f)] public float waspSwoopImpulse = 3.5f;
        [Range(1, 16)] public int waspSenseRadius = 7;
        [Range(0f, 2f)] public float waspPreyCalorieConversion = 0.85f;
        [Range(0f, 2f)] public float waspPreyHydrationTransfer = 0.6f;
        [Range(0f, 1f)] public float waspNectarDraw = 0.2f;
        [Range(0f, 1f)] public float waspNectarCalories = 0.05f;
        [Range(0f, 2f)] public float waspNectarHydration = 0.8f;
        [Range(1, 3)] public int waspPollenCapacity = 3;
        [Range(0f, 1f)] public float waspGeneExpressionRange = 0.45f;
        [Range(0f, 1f)] public float waspBaseMutationRate = 0.05f;
        [Range(0, 8000)] public int waspMateCooldownTicks = 300;
        [Range(0, 8000)] public int waspReproduceCooldownTicks = 700;
        [Range(1, 8)] public int waspClutchMin = 1;
        [Range(1, 8)] public int waspClutchMax = 3;
        [Range(1, 8000)] public int waspHatchTicksMin = 900;
        [Range(1, 8000)] public int waspHatchTicksMax = 1400;
        [Range(0.05f, 1f)] public float waspReproductionCalorieThreshold = 0.6f;
        [Range(0f, 1f)] public float waspEggDesiccationMoisture = 0.04f;
        [Range(20f, 200f)] public float waspEggHeatDeath = 90f;
        [Range(-80f, 80f)] public float waspSurvivalTempMin = -8f;
        [Range(-40f, 160f)] public float waspSurvivalTempMax = 78f;
        [Range(20f, 200f)] public float waspThreatTemperature = 90f;

        [Header("Ecology - Grass")]
        public bool grassSeedAtWorldgen = false;
        [Range(0f, 1f)] public float grassInitialBiomass = 0.4f;
        [Range(0f, 1f)] public float grassInitialEnergy = 0.55f;
        [Range(0f, 4f)] public float grassPhotosynthesisRate = 0.3f;
        [Range(0f, 4f)] public float grassGrowthRate = 0.14f;
        [Range(0f, 4f)] public float grassDecayRate = 0.1f;
        [Range(0f, 4f)] public float grassMaintenanceRate = 0.05f;
        [Range(0f, 4f)] public float grassNightDrain = 0.03f;
        [Range(0f, 4f)] public float grassWaterUptakeRate = 0.25f;
        [Range(0f, 4f)] public float grassNutrientUptakeRate = 0.18f;
        [Range(0f, 1f)] public float grassRootCohesionBonus = 0.1f;
        [Range(0.05f, 1f)] public float grassFlowerEnergyThreshold = 0.45f;
        [Range(0f, 1f)] public float grassGeneExpressionRange = 0.45f;
        [Range(-40f, 80f)] public float grassGrowthTempMin = 6f;
        [Range(-40f, 120f)] public float grassGrowthTempMax = 75f;
        [Range(0f, 2f)] public float grassGrowthMoistureMin = 0.08f;
        [Range(0f, 2f)] public float grassGrowthMoistureMax = 1.2f;
        [Range(-80f, 80f)] public float grassSurvivalTempMin = -10f;
        [Range(-40f, 160f)] public float grassSurvivalTempMax = 80f;
        [Range(0f, 2f)] public float grassSurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float grassSurvivalMoistureMax = 1.5f;
        [Range(0f, 2f)] public float grassMinLight = 0.06f;
        [Range(0f, 1f)] public float grassAdultBiomass = 0.35f;
        [Range(0f, 0.5f)] public float grassPollenEmitRate = 0.04f;
        [Range(0f, 0.5f)] public float grassPollenTransportRate = 0.08f;
        [Range(0f, 0.5f)] public float grassSeedTransportRate = 0.06f;
        [Range(0f, 4f)] public float grassPollenWindRate = 0.12f;
        [Range(0f, 4f)] public float grassPollenWaterRate = 0.1f;
        [Range(0f, 4f)] public float grassPollenSettlingRate = 0.15f;
        [Range(0f, 4f)] public float grassSeedWindRate = 0.08f;
        [Range(0f, 4f)] public float grassSeedWaterRate = 0.12f;
        [Range(0f, 4f)] public float grassSeedSettlingRate = 0.2f;
        [Range(0f, 1f)] public float grassNectarAmount = 0.35f;
        [Range(0f, 4f)] public float grassCanopyOpacity = 0.12f;

        [Header("Ecology - Tree")]
        public bool treeSeedAtWorldgen = false;
        [Range(0f, 1f)] public float treeInitialEnergy = 0.55f;
        [Range(0f, 1f)] public float treeInitialHydration = 0.6f;
        [Range(0f, 1f)] public float treeInitialNutrient = 0.45f;
        [Range(0f, 1f)] public float treeInitialHealth = 1f;
        [Range(0f, 4f)] public float treePhotosynthesisRate = 0.28f;
        [Range(0f, 4f)] public float treeGrowthRate = 0.12f;
        [Range(0f, 4f)] public float treeDecayRate = 0.08f;
        [Range(0f, 4f)] public float treeMaintenanceRate = 0.04f;
        [Range(0f, 4f)] public float treeNightDrain = 0.025f;
        [Range(0f, 4f)] public float treeWaterUptakeRate = 0.22f;
        [Range(0f, 4f)] public float treeNutrientUptakeRate = 0.16f;
        [Range(0f, 4f)] public float treeVascularRate = 0.35f;
        [Range(0f, 1f)] public float treeGrowthCost = 0.18f;
        [Range(0f, 1f)] public float treeWindBias = 0.35f;
        [Range(0f, 1f)] public float treeGeneExpressionRange = 0.45f;
        [Range(-40f, 80f)] public float treeGrowthTempMin = 6f;
        [Range(-40f, 120f)] public float treeGrowthTempMax = 72f;
        [Range(0f, 2f)] public float treeGrowthMoistureMin = 0.08f;
        [Range(0f, 2f)] public float treeGrowthMoistureMax = 1.2f;
        [Range(-80f, 80f)] public float treeSurvivalTempMin = -12f;
        [Range(-40f, 160f)] public float treeSurvivalTempMax = 82f;
        [Range(0f, 2f)] public float treeSurvivalMoistureMin = 0.02f;
        [Range(0f, 2f)] public float treeSurvivalMoistureMax = 1.5f;
        [Range(0f, 2f)] public float treeMinLight = 0.06f;
        [Range(1, 8)] public int treeSproutHeight = 4;
        [Range(6, 24)] public int treeSaplingHeight = 15;
        [Range(12, 48)] public int treeMaxHeight = 30;
        [Range(1, 8)] public int treeMaxTrunkWidth = 6;
        [Range(1, 6)] public int treeSaplingBranchMin = 2;
        [Range(1, 8)] public int treeSaplingBranchMax = 4;
        [Range(0f, 1f)] public float treeRootCohesionBonus = 0.12f;
        [Range(0f, 4f)] public float treeCanopyOpacity = 0.28f;
        [Range(0f, 4f)] public float treeExposureDamage = 0.08f;
        [Range(20, 8000)] public int treeLeafLifeTicks = 400;
        [Range(20, 8000)] public int treeRotTicks = 500;
        [Range(1, 64)] public int treeDisconnectTicks = 8;

        [Header("Detritus")]
        [Range(0f, 4f)] public float detritusVaporAbsorbRate = 0.08f;
        [Range(0f, 4f)] public float detritusEvaporationRate = 0.015f;
        [Range(0f, 4f)] public float detritusMoistureDistributeRate = 0.15f;
        [Range(0f, 4f)] public float detritusNutrientLeachRate = 0.12f;
        [Range(0f, 4f)] public float detritusDecompositionRate = 0.04f;
        [Range(0f, 1f)] public float detritusInitialNutrient = 0.45f;
        [Range(0f, 1f)] public float detritusInitialMoisture = 0.25f;

        [Header("Combustion")]
        [Range(0f, 2f)] public float combustionAmbientOxygen = 1f;
        [Range(0f, 4f)] public float combustionOxygenReplenishRate = 0.15f;
        [Range(0f, 4f)] public float combustionOxygenDiffusionRate = 0.35f;
        [Range(0f, 8f)] public float combustionIgnitionAccumulationRate = 2.5f;
        [Range(0f, 8f)] public float combustionIgnitionDecayRate = 1.2f;
        [Range(0f, 1f)] public float combustionSeedIntensity = 0.25f;
        [Range(0f, 4f)] public float combustionBurnRate = 0.2f;
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
        [Range(-40f, 400f)] public float stormRimingTempMax = 300f;
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
        [Range(0f, 1f)] public float stormSheetBranchChance = 0.65f;
        [Range(0, 2048)] public int stormMinimumHeight = 425;

        [Header("Graphics")]
        [Range(0, 1)] public int enableStarfield = 1;
        [Range(32, 512)] public int starCount = 300;
        [Range(0f, 2f)] public float starfieldStrength = 0.1f;
        [Range(0f, 2f)] public float starTwinkleStrength = 0.65f;
        [Range(0, 1)] public int enableNebula = 1;
        [Range(4, 48)] public int nebulaCount = 12;
        [Range(0f, 2f)] public float nebulaStrength = 0.45f;
        [Range(0, 1)] public int enableAtmosphereGlow = 1;
        [Range(0f, 2f)] public float atmosphereGlowStrength = 0.7f;
        [Range(4f, 48f)] public float atmosphereGlowPixelScale = 48f;
        [Range(1f, 24f)] public float atmosphereGlowRayCount = 7f;
        [Range(0, 1)] public int enableSolarBody = 1;
        [Range(0f, 2f)] public float solarBodyStrength = 1f;
        [Range(0f, 2f)] public float solarCoronaStrength = 0.85f;
        [Range(0.5f, 2f)] public float solarOrbitRadius = 1.35f;
        [Range(0f, 2f)] public float dayNightLightingStrength = 1f;

        [Header("Tools and validation")]
        [Range(1, 64)] public int brushRadius = 5;
        [Range(0.01f, 10f)] public float brushStrength = 1f;
        [Min(1)] public int validationIntervalTicks = 1000;
        [Range(0.0001f, 0.1f)] public float conservationTolerance = 0.02f;

        [Header("Probe")]
        [Range(0.8f, 2f)] public float probeOrbitRadius = 1.012f;
        [Range(0.01f, 1f)] public float probeSpriteScale = 0.025f;
        [Range(-180f, 180f)] public float probeSpriteRotationOffset = 93f;
        [Min(1f)] public float probeOrbitPeriodSeconds = 600f;
        [Range(0f, 10f)] public float probeVaporRate = 2f;
        [Range(0f, 10f)] public float probeWaterRate = 1f;
        [Range(0f, 10f)] public float probeHeatRate = 1f;
        [Range(0f, 10f)] public float probeCoolRate = 1f;
        [Range(1, 64)] public int probeDepositRadius = 4;
        [Range(0f, 45f)] public float probeLeadDegrees = 2f;
        [Range(0.75f, 20f)] public float probeFollowZoom = 2.5f;
        [Min(1f)] public float probeEnergyMax = 100f;
        [Range(0f, 20f)] public float probeEnergyActionDrain = 3f;
        [Min(0f)] public float probeEnergyRegenPerSecond = 15f;
        [Min(0.1f)] public float probeLifeSeedIntervalSeconds = 3f;
        [Range(1, 8)] public int probeLifeSeedMinCount = 1;
        [Range(1, 8)] public int probeLifeSeedMaxCount = 5;
        [Range(0f, 1f)] public float probeLifeSeedSporeLoad = 0.5f;

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
            slowPassInterval = Mathf.Clamp(slowPassInterval, 1, 32);
            transportPassInterval = Mathf.Clamp(transportPassInterval, 1, 8);
            dayLengthSeconds = Mathf.Max(1f, dayLengthSeconds);
            solarPolarOutputMin = Mathf.Clamp(solarPolarOutputMin, 0.05f, 1f);
            solarTerrainPenetration = Mathf.Clamp01(solarTerrainPenetration);
            minOceanBasins = Mathf.Clamp(minOceanBasins, 2, 3);
            maxOceanBasins = Mathf.Clamp(maxOceanBasins, minOceanBasins, 3);
            metalVeinCount = Mathf.Clamp(metalVeinCount, 0, 64);
            metalVeinMinSize = Mathf.Clamp(metalVeinMinSize, 0.001f, metalVeinMaxSize);
            metalVeinMaxSize = Mathf.Max(metalVeinMinSize, metalVeinMaxSize);
            metalVeinProtrusionChance = Mathf.Clamp01(metalVeinProtrusionChance);
            metalVeinProtrusionDistance = Mathf.Max(0f, metalVeinProtrusionDistance);
            iceCapRadius = Mathf.Clamp(iceCapRadius, 0.001f, 0.35f);
            iceCapHeight = Mathf.Clamp(iceCapHeight, 0.001f, 0.1f);
            iceCapRadiusVariation = Mathf.Clamp01(iceCapRadiusVariation);
            iceCapHeightVariation = Mathf.Clamp01(iceCapHeightVariation);
            fieldCapacityFraction = Mathf.Clamp01(fieldCapacityFraction);
            atmosphericAdvectionRate = Mathf.Max(0f, atmosphericAdvectionRate);
            vaporDiffusionRate = Mathf.Max(0f, vaporDiffusionRate);
            atmosphericBuoyancy = Mathf.Max(0f, atmosphericBuoyancy);
            humidityBuoyancy = Mathf.Max(0f, humidityBuoyancy);
            verticalBuoyancyStrength = Mathf.Max(0f, verticalBuoyancyStrength);
            saturationCapacityScale = Mathf.Max(0.01f, saturationCapacityScale);
            cloudPrecipitationThreshold = Mathf.Max(0.01f, cloudPrecipitationThreshold);
            waterPressureResponse = Mathf.Max(0f, waterPressureResponse);
            latentHeatScale = Mathf.Max(0f, latentHeatScale);
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
            floraPoleDriftRate = Mathf.Max(0f, floraPoleDriftRate);
            floraWindShearRate = Mathf.Max(0f, floraWindShearRate);
            floraRainShearRate = Mathf.Max(0f, floraRainShearRate);
            floraFragmentYield = Mathf.Clamp01(floraFragmentYield);
            floraAnchorGrip = Mathf.Max(0f, floraAnchorGrip);
            faunaInitialCalories = Mathf.Clamp01(faunaInitialCalories);
            faunaInitialHydration = Mathf.Clamp01(faunaInitialHydration);
            faunaMaturityTicks = Mathf.Clamp(faunaMaturityTicks, 1, 20000);
            faunaDecisionInterval = Mathf.Clamp(faunaDecisionInterval, 1, 16);
            faunaMaintenanceRate = Mathf.Max(0f, faunaMaintenanceRate);
            faunaHydrationDrain = Mathf.Max(0f, faunaHydrationDrain);
            faunaCalorieCapacity = Mathf.Max(0.05f, faunaCalorieCapacity);
            faunaFullThreshold = Mathf.Clamp(faunaFullThreshold, 0.05f, 1f);
            faunaHungerThreshold = Mathf.Clamp(faunaHungerThreshold, 0.05f, faunaFullThreshold);
            faunaReproductionCalorieThreshold = Mathf.Clamp(faunaReproductionCalorieThreshold, 0.05f, 1f);
            faunaHopImpulse = Mathf.Max(0f, faunaHopImpulse);
            faunaHopCost = Mathf.Clamp01(faunaHopCost);
            faunaFeedCost = Mathf.Clamp01(faunaFeedCost);
            faunaDryMass = Mathf.Max(0.05f, faunaDryMass);
            faunaDrag = Mathf.Max(0f, faunaDrag);
            faunaWindResistance = Mathf.Max(0f, faunaWindResistance);
            faunaMoistureMass = Mathf.Max(0f, faunaMoistureMass);
            faunaSupportBoost = Mathf.Max(0f, faunaSupportBoost);
            faunaWetPenalty = Mathf.Max(0f, faunaWetPenalty);
            faunaGeneExpressionRange = Mathf.Clamp01(faunaGeneExpressionRange);
            faunaBaseMutationRate = Mathf.Clamp01(faunaBaseMutationRate);
            faunaSenseRadius = Mathf.Clamp(faunaSenseRadius, 1, 16);
            faunaHearingRange = Mathf.Max(0f, faunaHearingRange);
            faunaThreatTemperature = Mathf.Max(0f, faunaThreatTemperature);
            faunaAcousticSpeed = Mathf.Max(0f, faunaAcousticSpeed);
            faunaAcousticDamping = Mathf.Max(0f, faunaAcousticDamping);
            faunaFeedCallAmplitude = Mathf.Max(0f, faunaFeedCallAmplitude);
            faunaMateCallAmplitude = Mathf.Max(0f, faunaMateCallAmplitude);
            faunaMateCooldownTicks = Mathf.Max(0, faunaMateCooldownTicks);
            faunaReproduceCooldownTicks = Mathf.Max(0, faunaReproduceCooldownTicks);
            faunaClutchMin = Mathf.Clamp(faunaClutchMin, 1, 8);
            faunaClutchMax = Mathf.Clamp(faunaClutchMax, faunaClutchMin, 8);
            faunaHatchTicksMin = Mathf.Clamp(faunaHatchTicksMin, 1, 8000);
            faunaHatchTicksMax = Mathf.Max(faunaHatchTicksMin, faunaHatchTicksMax);
            faunaEggDesiccationMoisture = Mathf.Clamp01(faunaEggDesiccationMoisture);
            faunaEggHeatDeath = Mathf.Max(0f, faunaEggHeatDeath);
            faunaEggDisplacement = Mathf.Max(0f, faunaEggDisplacement);
            faunaWanderRate = Mathf.Max(0f, faunaWanderRate);
            if (faunaSurvivalTempMax < faunaSurvivalTempMin)
            {
                float swap = faunaSurvivalTempMin;
                faunaSurvivalTempMin = faunaSurvivalTempMax;
                faunaSurvivalTempMax = swap;
            }
            waspInitialCalories = Mathf.Clamp01(waspInitialCalories);
            waspInitialHydration = Mathf.Clamp01(waspInitialHydration);
            waspMaturityTicks = Mathf.Clamp(waspMaturityTicks, 1, 20000);
            waspDecisionInterval = Mathf.Clamp(waspDecisionInterval, 1, 16);
            waspMaintenanceRate = Mathf.Max(0f, waspMaintenanceRate);
            waspFlightDrain = Mathf.Max(0f, waspFlightDrain);
            waspHydrationDrain = Mathf.Max(0f, waspHydrationDrain);
            waspCalorieCapacity = Mathf.Max(0.05f, waspCalorieCapacity);
            waspFullThreshold = Mathf.Clamp(waspFullThreshold, 0.05f, 1f);
            waspHungerThreshold = Mathf.Clamp(waspHungerThreshold, 0.05f, waspFullThreshold);
            waspStarvationThreshold = Mathf.Clamp(waspStarvationThreshold, 0f, waspHungerThreshold);
            waspCruiseAltitude = Mathf.Clamp(waspCruiseAltitude, 1f, 16f);
            waspAltitudeGain = Mathf.Max(0f, waspAltitudeGain);
            waspLiftPower = Mathf.Max(0f, waspLiftPower);
            waspSurfaceScanRange = Mathf.Clamp(waspSurfaceScanRange, 1, 24);
            waspBodyMass = Mathf.Max(0.05f, waspBodyMass);
            waspDrag = Mathf.Max(0f, waspDrag);
            waspWindCoupling = Mathf.Max(0f, waspWindCoupling);
            waspUpdraftCoupling = Mathf.Max(0f, waspUpdraftCoupling);
            waspSwoopImpulse = Mathf.Max(0f, waspSwoopImpulse);
            waspSenseRadius = Mathf.Clamp(waspSenseRadius, 1, 16);
            waspPreyCalorieConversion = Mathf.Max(0f, waspPreyCalorieConversion);
            waspPreyHydrationTransfer = Mathf.Max(0f, waspPreyHydrationTransfer);
            waspNectarDraw = Mathf.Clamp01(waspNectarDraw);
            waspNectarCalories = Mathf.Clamp01(waspNectarCalories);
            waspNectarHydration = Mathf.Max(0f, waspNectarHydration);
            waspPollenCapacity = Mathf.Clamp(waspPollenCapacity, 1, 3);
            waspGeneExpressionRange = Mathf.Clamp01(waspGeneExpressionRange);
            waspBaseMutationRate = Mathf.Clamp01(waspBaseMutationRate);
            waspMateCooldownTicks = Mathf.Max(0, waspMateCooldownTicks);
            waspReproduceCooldownTicks = Mathf.Max(0, waspReproduceCooldownTicks);
            waspClutchMin = Mathf.Clamp(waspClutchMin, 1, 8);
            waspClutchMax = Mathf.Clamp(waspClutchMax, waspClutchMin, 8);
            waspHatchTicksMin = Mathf.Clamp(waspHatchTicksMin, 1, 8000);
            waspHatchTicksMax = Mathf.Max(waspHatchTicksMin, waspHatchTicksMax);
            waspReproductionCalorieThreshold = Mathf.Clamp(waspReproductionCalorieThreshold, 0.05f, 1f);
            waspEggDesiccationMoisture = Mathf.Clamp01(waspEggDesiccationMoisture);
            waspEggHeatDeath = Mathf.Max(0f, waspEggHeatDeath);
            waspThreatTemperature = Mathf.Max(0f, waspThreatTemperature);
            if (waspSurvivalTempMax < waspSurvivalTempMin)
            {
                float swap = waspSurvivalTempMin;
                waspSurvivalTempMin = waspSurvivalTempMax;
                waspSurvivalTempMax = swap;
            }
            grassInitialBiomass = Mathf.Clamp01(grassInitialBiomass);
            grassInitialEnergy = Mathf.Clamp01(grassInitialEnergy);
            grassPhotosynthesisRate = Mathf.Max(0f, grassPhotosynthesisRate);
            grassGrowthRate = Mathf.Max(0f, grassGrowthRate);
            grassDecayRate = Mathf.Max(0f, grassDecayRate);
            grassMaintenanceRate = Mathf.Max(0f, grassMaintenanceRate);
            grassNightDrain = Mathf.Max(0f, grassNightDrain);
            grassWaterUptakeRate = Mathf.Max(0f, grassWaterUptakeRate);
            grassNutrientUptakeRate = Mathf.Max(0f, grassNutrientUptakeRate);
            grassRootCohesionBonus = Mathf.Clamp01(grassRootCohesionBonus);
            grassFlowerEnergyThreshold = Mathf.Clamp(grassFlowerEnergyThreshold, 0.05f, 1f);
            grassGeneExpressionRange = Mathf.Clamp01(grassGeneExpressionRange);
            if (grassGrowthTempMax < grassGrowthTempMin)
            {
                float swap = grassGrowthTempMin;
                grassGrowthTempMin = grassGrowthTempMax;
                grassGrowthTempMax = swap;
            }
            if (grassSurvivalTempMax < grassSurvivalTempMin)
            {
                float swap = grassSurvivalTempMin;
                grassSurvivalTempMin = grassSurvivalTempMax;
                grassSurvivalTempMax = swap;
            }
            grassSurvivalTempMin = Mathf.Min(grassSurvivalTempMin, grassGrowthTempMin);
            grassSurvivalTempMax = Mathf.Max(grassSurvivalTempMax, grassGrowthTempMax);
            if (grassGrowthMoistureMax < grassGrowthMoistureMin)
            {
                float swap = grassGrowthMoistureMin;
                grassGrowthMoistureMin = grassGrowthMoistureMax;
                grassGrowthMoistureMax = swap;
            }
            if (grassSurvivalMoistureMax < grassSurvivalMoistureMin)
            {
                float swap = grassSurvivalMoistureMin;
                grassSurvivalMoistureMin = grassSurvivalMoistureMax;
                grassSurvivalMoistureMax = swap;
            }
            grassSurvivalMoistureMin = Mathf.Min(grassSurvivalMoistureMin, grassGrowthMoistureMin);
            grassSurvivalMoistureMax = Mathf.Max(grassSurvivalMoistureMax, grassGrowthMoistureMax);
            grassMinLight = Mathf.Max(0f, grassMinLight);
            grassAdultBiomass = Mathf.Clamp01(grassAdultBiomass);
            grassPollenEmitRate = Mathf.Clamp(grassPollenEmitRate, 0f, 0.5f);
            grassPollenTransportRate = Mathf.Clamp(grassPollenTransportRate, 0f, 0.5f);
            grassSeedTransportRate = Mathf.Clamp(grassSeedTransportRate, 0f, 0.5f);
            grassPollenWindRate = Mathf.Max(0f, grassPollenWindRate);
            grassPollenWaterRate = Mathf.Max(0f, grassPollenWaterRate);
            grassPollenSettlingRate = Mathf.Max(0f, grassPollenSettlingRate);
            grassSeedWindRate = Mathf.Max(0f, grassSeedWindRate);
            grassSeedWaterRate = Mathf.Max(0f, grassSeedWaterRate);
            grassSeedSettlingRate = Mathf.Max(0f, grassSeedSettlingRate);
            grassNectarAmount = Mathf.Clamp01(grassNectarAmount);
            grassCanopyOpacity = Mathf.Max(0f, grassCanopyOpacity);
            treeInitialEnergy = Mathf.Clamp01(treeInitialEnergy);
            treeInitialHydration = Mathf.Clamp01(treeInitialHydration);
            treeInitialNutrient = Mathf.Clamp01(treeInitialNutrient);
            treeInitialHealth = Mathf.Clamp01(treeInitialHealth);
            treePhotosynthesisRate = Mathf.Max(0f, treePhotosynthesisRate);
            treeGrowthRate = Mathf.Max(0f, treeGrowthRate);
            treeDecayRate = Mathf.Max(0f, treeDecayRate);
            treeMaintenanceRate = Mathf.Max(0f, treeMaintenanceRate);
            treeNightDrain = Mathf.Max(0f, treeNightDrain);
            treeWaterUptakeRate = Mathf.Max(0f, treeWaterUptakeRate);
            treeNutrientUptakeRate = Mathf.Max(0f, treeNutrientUptakeRate);
            treeVascularRate = Mathf.Max(0f, treeVascularRate);
            treeGrowthCost = Mathf.Clamp01(treeGrowthCost);
            treeWindBias = Mathf.Clamp01(treeWindBias);
            treeGeneExpressionRange = Mathf.Clamp01(treeGeneExpressionRange);
            if (treeGrowthTempMax < treeGrowthTempMin)
            {
                float swap = treeGrowthTempMin;
                treeGrowthTempMin = treeGrowthTempMax;
                treeGrowthTempMax = swap;
            }
            if (treeSurvivalTempMax < treeSurvivalTempMin)
            {
                float swap = treeSurvivalTempMin;
                treeSurvivalTempMin = treeSurvivalTempMax;
                treeSurvivalTempMax = swap;
            }
            treeSurvivalTempMin = Mathf.Min(treeSurvivalTempMin, treeGrowthTempMin);
            treeSurvivalTempMax = Mathf.Max(treeSurvivalTempMax, treeGrowthTempMax);
            if (treeGrowthMoistureMax < treeGrowthMoistureMin)
            {
                float swap = treeGrowthMoistureMin;
                treeGrowthMoistureMin = treeGrowthMoistureMax;
                treeGrowthMoistureMax = swap;
            }
            if (treeSurvivalMoistureMax < treeSurvivalMoistureMin)
            {
                float swap = treeSurvivalMoistureMin;
                treeSurvivalMoistureMin = treeSurvivalMoistureMax;
                treeSurvivalMoistureMax = swap;
            }
            treeSurvivalMoistureMin = Mathf.Min(treeSurvivalMoistureMin, treeGrowthMoistureMin);
            treeSurvivalMoistureMax = Mathf.Max(treeSurvivalMoistureMax, treeGrowthMoistureMax);
            treeMinLight = Mathf.Max(0f, treeMinLight);
            treeSproutHeight = Mathf.Clamp(treeSproutHeight, 1, 8);
            treeSaplingHeight = Mathf.Clamp(treeSaplingHeight, Mathf.Max(6, treeSproutHeight + 2), 24);
            treeMaxHeight = Mathf.Clamp(treeMaxHeight, Mathf.Max(12, treeSaplingHeight + 2), 48);
            treeMaxTrunkWidth = Mathf.Clamp(treeMaxTrunkWidth, 1, 8);
            treeSaplingBranchMin = Mathf.Clamp(treeSaplingBranchMin, 1, 6);
            treeSaplingBranchMax = Mathf.Clamp(treeSaplingBranchMax, treeSaplingBranchMin, 8);
            treeRootCohesionBonus = Mathf.Clamp01(treeRootCohesionBonus);
            treeCanopyOpacity = Mathf.Max(0f, treeCanopyOpacity);
            treeExposureDamage = Mathf.Max(0f, treeExposureDamage);
            treeLeafLifeTicks = Mathf.Clamp(treeLeafLifeTicks, 20, 8000);
            treeRotTicks = Mathf.Clamp(treeRotTicks, 20, 8000);
            treeDisconnectTicks = Mathf.Clamp(treeDisconnectTicks, 1, 64);
            detritusVaporAbsorbRate = Mathf.Max(0f, detritusVaporAbsorbRate);
            detritusEvaporationRate = Mathf.Max(0f, detritusEvaporationRate);
            detritusMoistureDistributeRate = Mathf.Max(0f, detritusMoistureDistributeRate);
            detritusNutrientLeachRate = Mathf.Max(0f, detritusNutrientLeachRate);
            detritusDecompositionRate = Mathf.Max(0f, detritusDecompositionRate);
            detritusInitialNutrient = Mathf.Clamp01(detritusInitialNutrient);
            detritusInitialMoisture = Mathf.Clamp01(detritusInitialMoisture);
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
            probeEnergyMax = Mathf.Max(1f, probeEnergyMax);
            probeEnergyActionDrain = Mathf.Max(0f, probeEnergyActionDrain);
            probeEnergyRegenPerSecond = Mathf.Max(0f, probeEnergyRegenPerSecond);
            probeLifeSeedIntervalSeconds = Mathf.Max(0.1f, probeLifeSeedIntervalSeconds);
            probeLifeSeedMinCount = Mathf.Clamp(probeLifeSeedMinCount, 1, 8);
            probeLifeSeedMaxCount = Mathf.Clamp(probeLifeSeedMaxCount, probeLifeSeedMinCount, 8);
            probeLifeSeedSporeLoad = Mathf.Clamp01(probeLifeSeedSporeLoad);
        }
    }
}
