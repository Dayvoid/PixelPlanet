using GeneSys.Configuration;

namespace GeneSys.Simulation.Scenarios
{
    public readonly struct WorldScenarioWorldgen
    {
        public readonly float TargetOceanCoverage;
        public readonly float InitialGroundwaterSaturation;
        public readonly float InitialAtmosphericHumidity;
        public readonly float BorderNoise;
        public readonly float SoilRatio;
        public readonly int ClayDepositCount;
        public readonly float MycologyInitialSporeLoad;
        public readonly bool SkipBiologySeed;

        public WorldScenarioWorldgen(
            float targetOceanCoverage,
            float initialGroundwaterSaturation,
            float initialAtmosphericHumidity,
            float borderNoise,
            float soilRatio,
            int clayDepositCount,
            float mycologyInitialSporeLoad,
            bool skipBiologySeed)
        {
            TargetOceanCoverage = targetOceanCoverage;
            InitialGroundwaterSaturation = initialGroundwaterSaturation;
            InitialAtmosphericHumidity = initialAtmosphericHumidity;
            BorderNoise = borderNoise;
            SoilRatio = soilRatio;
            ClayDepositCount = clayDepositCount;
            MycologyInitialSporeLoad = mycologyInitialSporeLoad;
            SkipBiologySeed = skipBiologySeed;
        }
    }

    public static class WorldScenarioOverlays
    {
        public const float WorldWideWaterOceanCoverage = 1f;
        public const float WorldWideWaterGroundwaterSaturation = 0.95f;

        public const float CometMoonOceanCoverage = 0f;
        public const float CometMoonHumidity = 0f;
        public const float CometMoonGroundwaterSaturation = 0.08f;
        public const float CometMoonBorderNoise = 0.005f;
        public const float CometMoonSoilRatio = 0.006f;
        public const int CometMoonClayDepositCount = 0;
        public const float CometMoonMycologySporeLoad = 0f;

        public static WorldScenarioWorldgen Resolve(SimulationConfig config)
        {
            if (config == null)
                return default;

            switch (config.worldScenario)
            {
                case WorldScenario.WorldWideWater:
                    return new WorldScenarioWorldgen(
                        WorldWideWaterOceanCoverage,
                        WorldWideWaterGroundwaterSaturation,
                        config.initialAtmosphericHumidity,
                        config.borderNoise,
                        config.soilRatio,
                        config.clayDepositCount,
                        config.mycologyInitialSporeLoad,
                        false);
                case WorldScenario.CometStruckMoon:
                    return new WorldScenarioWorldgen(
                        CometMoonOceanCoverage,
                        CometMoonGroundwaterSaturation,
                        CometMoonHumidity,
                        CometMoonBorderNoise,
                        CometMoonSoilRatio,
                        CometMoonClayDepositCount,
                        CometMoonMycologySporeLoad,
                        true);
                default:
                    return new WorldScenarioWorldgen(
                        config.targetOceanCoverage,
                        config.initialGroundwaterSaturation,
                        config.initialAtmosphericHumidity,
                        config.borderNoise,
                        config.soilRatio,
                        config.clayDepositCount,
                        config.mycologyInitialSporeLoad,
                        false);
            }
        }
    }
}
