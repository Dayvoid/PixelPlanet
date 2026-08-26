using System;
using System.Collections.Generic;
using System.Diagnostics;
using GeneSys.Configuration;
using GeneSys.Materials;
using UnityEngine;

namespace GeneSys.Simulation.Gpu
{
    public sealed class GpuPassScheduler : IDisposable
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct BrushCommand
        {
            public Vector2Int center;
            public int radius;
            public uint materialId;
            public Vector4 values;
            public const int Stride = 32;
        }

        private readonly SimulationConfig config;
        private readonly SimulationResources resources;
        private readonly GraphicsBuffer materialBuffer;
        private readonly GraphicsBuffer brushBuffer;
        private readonly List<BrushCommand> brushCommands = new(128);
        private readonly ComputeShader worldGeneration;
        private readonly ComputeShader materialSimulation;
        private readonly ComputeShader geology;
        private readonly ComputeShader hydrology;
        private readonly ComputeShader weather;
        private readonly ComputeShader mycology;
        private readonly ComputeShader flora;
        private readonly ComputeShader combustion;
        private readonly ComputeShader storm;
        private readonly GraphicsBuffer strikeSeedBuffer;
        private readonly GraphicsBuffer strikeCounterBuffer;
        private readonly uint[] strikeCounterZero = new uint[1];
        private const int MaxStrikeSeeds = 32;
        private const int StrikeSeedStride = 16;
        private int tick;

        public int TickIndex => tick;
        public void SetTickIndex(int value) => tick = Math.Max(0, value);
        public double LastTickMilliseconds { get; private set; }
        public float SolarAngle01 => Mathf.Repeat(tick / Mathf.Max(1f, config.ticksPerSecond * config.dayLengthSeconds), 1f);

        public GpuPassScheduler(SimulationConfig config, SimulationResources resources, MaterialRegistry registry,
            ComputeShader worldGeneration, ComputeShader materialSimulation, ComputeShader geology,
            ComputeShader hydrology, ComputeShader weather, ComputeShader mycology, ComputeShader flora,
            ComputeShader combustion, ComputeShader storm)
        {
            this.config = config;
            this.resources = resources;
            this.worldGeneration = worldGeneration;
            this.materialSimulation = materialSimulation;
            this.geology = geology;
            this.hydrology = hydrology;
            this.weather = weather;
            this.mycology = mycology;
            this.flora = flora;
            this.combustion = combustion;
            this.storm = storm;
            materialBuffer = registry.CreateGpuBuffer();
            brushBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, BrushCommand.Stride);
            strikeSeedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxStrikeSeeds, StrikeSeedStride);
            strikeCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
        }

        public void GenerateWorld()
        {
            tick = 0;
            string kernelName = config.useOgWorldgen ? "GenerateWorld" : "GenerateWorldV2";
            int kernel = worldGeneration.FindKernel(kernelName);
            if (kernel < 0)
            {
                UnityEngine.Debug.LogError($"GeneSys: {kernelName} kernel missing. Reimport WorldGeneration.compute and fix shader compile errors.");
                return;
            }
            SetCommon(worldGeneration, kernel, 0f);
            worldGeneration.SetInt("_Seed", config.seed);
            worldGeneration.SetVector("_LayerRatios", new Vector4(config.coreRatio, config.mantleRatio, config.crustRatio, config.soilRatio));
            worldGeneration.SetVector("_WorldGenParams", new Vector4(config.borderNoise, config.protrusionChance, config.groundwaterDepth, config.faultCount));
            worldGeneration.SetVector("_WorldWaterA", new Vector4(config.targetOceanCoverage, config.minOceanBasins, config.maxOceanBasins, config.seaLevelRadius));
            worldGeneration.SetVector("_WorldWaterB", new Vector4(config.basinDepth, config.terrainRelief, config.coastRoughness, config.initialGroundwaterSaturation));
            worldGeneration.SetVector("_WorldWaterC", new Vector4(config.initialAtmosphericHumidity, config.groundwaterDepth, 0f, 0f));
            if (!config.useOgWorldgen)
            {
                worldGeneration.SetVector("_WorldGenV2A", new Vector4(config.metalVeinCount, config.metalVeinMinSize, config.metalVeinMaxSize, config.metalVeinProtrusionChance));
                worldGeneration.SetVector("_WorldGenV2B", new Vector4(config.metalVeinProtrusionDistance, config.iceCapRadius, config.iceCapHeight, config.iceCapRadiusVariation));
                worldGeneration.SetVector("_WorldGenV2C", new Vector4(config.iceCapHeightVariation, 0f, 0f, 0f));
            }
            worldGeneration.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindWorldgenOutputs(worldGeneration, kernel);
            Dispatch(worldGeneration, kernel);
            if (flora != null)
            {
                int seedFlora = flora.FindKernel("SeedFlora");
                if (seedFlora >= 0)
                {
                    SetCommon(flora, seedFlora, 0f);
                    flora.SetTexture(seedFlora, "_MaterialRead", resources.MaterialRead);
                    flora.SetTexture(seedFlora, "_LifeGenomeWrite", resources.LifeGenomeRead);
                    Dispatch(flora, seedFlora);
                }
            }
            resources.CopyReadToWrite();
        }

        public void QueueBrush(BrushCommand command)
        {
            if (brushCommands.Count < 128) brushCommands.Add(command);
        }

        public void RefreshMaterialDefinitions(MaterialRegistry registry)
        {
            materialBuffer.SetData(registry.BuildGpuData());
        }

        public void Step(float deltaTime)
        {
            var stopwatch = Stopwatch.StartNew();
            if (brushCommands.Count > 0)
            {
                brushBuffer.SetData(brushCommands);
                int brushKernel = materialSimulation.FindKernel("ApplyEdits");
                materialSimulation.SetInt("_BrushCount", brushCommands.Count);
                materialSimulation.SetBuffer(brushKernel, "_BrushCommands", brushBuffer);
                DispatchPass(materialSimulation, brushKernel, deltaTime);
                if (storm != null)
                {
                    int stormBrush = storm.FindKernel("ApplyStormEdits");
                    storm.SetInt("_BrushCount", brushCommands.Count);
                    storm.SetBuffer(stormBrush, "_BrushCommands", brushBuffer);
                    DispatchStormInPlace(storm, stormBrush, deltaTime);
                }
                brushCommands.Clear();
            }

            for (int i = 0; i < config.materialSubsteps; i++)
            {
                float subDt = deltaTime / config.materialSubsteps;
                DispatchPass(materialSimulation, materialSimulation.FindKernel("ThermalAndPressure"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("PhaseChange"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("LiquidDensityExchange"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("MaterialMotion"), subDt);
                DispatchPass(geology, geology.FindKernel("EruptionMotion"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("Electrical"), subDt);
            }

            if (tick % Mathf.Max(1, config.slowPassInterval) == 0)
                DispatchPass(geology, geology.FindKernel("Volcanism"), deltaTime * config.slowPassInterval);

            if (config.coreReactionFrequency > 0
                && config.coreReactionMagnitude > 0f
                && tick % config.coreReactionFrequency == 0)
                DispatchPass(geology, geology.FindKernel("CoreReaction"), deltaTime);

            if (combustion != null)
                DispatchPass(combustion, combustion.FindKernel("Combustion"), deltaTime);

            // Atmospheric loop: forcing → continuity → pressure diffusion → dynamics → transport → water cycle.
            DispatchPass(weather, weather.FindKernel("AtmosphericForcing"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericContinuity"), deltaTime);
            DispatchPass(materialSimulation, materialSimulation.FindKernel("PressureDiffusion"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericDynamics"), deltaTime);
            DispatchPass(geology, geology.FindKernel("AshTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("WaterCycle"), deltaTime);

            if (storm != null)
                DispatchStorm(deltaTime);

            DispatchPass(hydrology, hydrology.FindKernel("RunoffAndDeposition"), deltaTime);
            // Soak this tick's rain/ponding, then springs/geysers see the updated water table.
            DispatchPass(hydrology, hydrology.FindKernel("Groundwater"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("GeothermalDischarge"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("WaterMaterialization"), deltaTime);

            // Erosion sees the current tick's moisture, exposure, and flow after weather/runoff.
            if (tick % Mathf.Max(1, config.slowPassInterval) == 0)
            {
                DispatchPass(hydrology, hydrology.FindKernel("ErosionAndCollapse"), deltaTime * config.slowPassInterval);
                DispatchPass(hydrology, hydrology.FindKernel("AshFertilization"), deltaTime * config.slowPassInterval);
            }

            if (mycology != null)
            {
                DispatchPass(mycology, mycology.FindKernel("SporeTransport"), deltaTime);
                if (tick % Mathf.Max(1, config.slowPassInterval) == 0)
                    DispatchPass(mycology, mycology.FindKernel("ColonyLifecycle"), deltaTime * config.slowPassInterval);
            }

            if (flora != null)
            {
                DispatchLight(flora, flora.FindKernel("LightAttenuation"), deltaTime);
                DispatchPass(flora, flora.FindKernel("SporeTransport"), deltaTime);
                DispatchPass(flora, flora.FindKernel("Photosynthesis"), deltaTime);
                if (tick % Mathf.Max(1, config.slowPassInterval) == 0)
                    DispatchPass(flora, flora.FindKernel("FloraLifecycle"), deltaTime * config.slowPassInterval);
                if (config.floraPoleDriftRate > 1e-8f || config.floraWindShearRate > 1e-8f || config.floraRainShearRate > 1e-8f)
                    DispatchPass(flora, flora.FindKernel("FloraMigration"), deltaTime);
            }

            tick++;
            stopwatch.Stop();
            LastTickMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private void DispatchPass(ComputeShader shader, int kernel, float deltaTime)
        {
            if (kernel < 0)
            {
                UnityEngine.Debug.LogError($"GeneSys: missing compute kernel on {shader.name}. Skipping pass.");
                return;
            }
            SetCommon(shader, kernel, deltaTime);
            shader.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(shader, kernel);
            Dispatch(shader, kernel);
            resources.Swap();
        }

        private void SetCommon(ComputeShader shader, int kernel, float deltaTime)
        {
            shader.SetInts("_GridSize", resources.Grid.angularResolution, resources.Grid.radialResolution);
            shader.SetFloat("_DeltaTime", deltaTime);
            shader.SetInt("_Tick", tick);
            shader.SetInt("_Seed", config.seed);
            shader.SetFloat("_PlayableInnerRadius", resources.Grid.playableInnerRadius);
            shader.SetFloat("_AtmosphereStartRadius", resources.Grid.atmosphereStartRadius);
            shader.SetVector("_Mechanics", new Vector4(config.gravityStrength, config.thermalRate, config.electricalRate, config.pressureRate));
            shader.SetVector("_Geology", new Vector4(config.mantlePressure, config.fractureRate, config.extrusionRate, config.volcanicCooling));
            shader.SetVector("_GeologyB", new Vector4(config.hydrothermalStrength, config.ventChemicalRate, config.coreReactionFrequency, config.coreReactionMagnitude));
            shader.SetVector("_EruptionA", new Vector4(config.magmaEruption, config.eruptionPressureStrength, config.eruptionFlowStrength, config.eruptionBurdenDepth));
            shader.SetVector("_EruptionB", new Vector4(config.eruptionBlastThreshold, config.ashUpdraftStrength, config.ashSettlingStrength, config.ashFertilityStrength));
            shader.SetVector("_Hydrology", new Vector4(config.infiltrationRate, config.groundwaterRate, config.dissolutionRate, config.collapseRate));
            shader.SetVector("_HydrologyB", new Vector4(config.springHeadThreshold, config.springDischargeRate, config.geyserHeatThreshold, config.geyserDischargeRate));
            shader.SetVector("_HydrologyC", new Vector4(config.runoffRate, config.pondingRate, config.fieldCapacityFraction, config.geyserCooldownSeconds));
            shader.SetVector("_Erosion", new Vector4(config.erosionRate, 0f, config.baseSoilCohesion, config.stressDecayRate));
            shader.SetVector("_MoistureErosion", new Vector4(config.dryMoistureThreshold, config.moistureCohesionStrength, config.capillaryEvaporationFraction, 0f));
            shader.SetVector("_WeatherA", new Vector4(config.solarIntensity, config.spaceTemperature, config.radiativeCooling, config.windStrength));
            shader.SetVector("_WeatherB", new Vector4(config.windDamping, config.evaporationRate, config.condensationRate, config.precipitationRate));
            shader.SetVector("_WeatherC", new Vector4(config.vaporPressureScale, SolarAngle01, config.phaseHysteresis, config.magmaViscosity));
            shader.SetVector("_WeatherD", new Vector4(config.atmosphericAdvectionRate, config.vaporDiffusionRate, config.atmosphericBuoyancy, config.humidityBuoyancy));
            shader.SetVector("_WeatherE", new Vector4(config.saturationCapacityScale, config.cloudPrecipitationThreshold, config.rainPixelFormationThreshold, config.surfaceWaterPixelThreshold));
            shader.SetVector("_WeatherF", new Vector4(config.surfaceAirHeatExchange, config.temperatureAdvectionRate, config.pressureCompressibility, config.atmosphericCflLimit));
            shader.SetVector("_WeatherG", new Vector4(config.surfaceAirTemperature, config.atmosphericLapseRate, 0f, 0f));
            shader.SetVector("_PressureA", new Vector4(config.pressureDiffusionRate, config.pressureEquilibriumGradient, config.pressureEquilibriumMaximum, 0f));
            shader.SetVector("_PressureB", new Vector4(config.gasPressureDiffusivity, config.fluidPressureDiffusivity, config.porousPressureDiffusivity, config.rigidPressureDiffusivity));
            shader.SetVector("_DensityExchange", new Vector4(config.densityExchangeRate, config.densityExchangeEpsilon, 0f, 0f));
            shader.SetVector("_MycologyA", new Vector4(config.mycologyInitialSporeLoad, config.mycologyRareStrainChance, config.mycologyAirTransportRate, config.mycologyWaterTransportRate));
            shader.SetVector("_MycologyB", new Vector4(config.mycologyDiffusionRate, config.mycologySettlingRate, config.mycologySporulationRate, config.mycologyGrowthRate));
            shader.SetVector("_MycologyC", new Vector4(config.mycologyDecayRate, config.mycologyGrowthTempMin, config.mycologyGrowthTempMax, config.mycologyGrowthMoistureMin));
            shader.SetVector("_MycologyD", new Vector4(config.mycologyGrowthMoistureMax, config.mycologySurvivalTempMin, config.mycologySurvivalTempMax, config.mycologySurvivalMoistureMin));
            shader.SetVector("_MycologyE", new Vector4(config.mycologySurvivalMoistureMax, config.mycologyElectricalTolerance, config.mycologyTraitEffectStrength, 0f));
            shader.SetVector("_FloraA", new Vector4(config.floraAirTransportRate, config.floraWaterTransportRate, config.floraDiffusionRate, config.floraSettlingRate));
            shader.SetVector("_FloraB", new Vector4(config.floraSporulationRate, config.floraGrowthRate, config.floraDecayRate, config.floraPhotosynthesisRate));
            shader.SetVector("_FloraC", new Vector4(config.floraOxygenYield, config.floraExudationRate, config.floraReproductionThreshold, config.floraBaseMutationRate));
            shader.SetVector("_FloraD", new Vector4(config.floraToxinMutationScale, config.floraGeneExpressionRange, config.floraGrowthTempMin, config.floraGrowthTempMax));
            shader.SetVector("_FloraE", new Vector4(config.floraGrowthMoistureMin, config.floraGrowthMoistureMax, config.floraSurvivalTempMin, config.floraSurvivalTempMax));
            shader.SetVector("_FloraF", new Vector4(config.floraSurvivalMoistureMin, config.floraSurvivalMoistureMax, config.floraMinLight, config.floraGerminationSporeThreshold));
            shader.SetVector("_FloraG", new Vector4(config.floraMaintenanceRate, config.floraNightDrain, config.floraDormancyMetabolicScale, config.floraInitialSporeLoad));
            shader.SetVector("_FloraH", new Vector4(config.floraSeedAtWorldgen ? 1f : 0f, config.floraAnchorGrip, 0f, 0f));
            shader.SetVector("_FloraI", new Vector4(config.floraPoleDriftRate, config.floraWindShearRate, config.floraRainShearRate, config.floraFragmentYield));
            shader.SetVector("_CombustionA", new Vector4(config.combustionAmbientOxygen, config.combustionOxygenReplenishRate, config.combustionOxygenDiffusionRate, config.combustionIgnitionAccumulationRate));
            shader.SetVector("_CombustionB", new Vector4(config.combustionIgnitionDecayRate, config.combustionSeedIntensity, config.combustionBurnRate, config.combustionHeatYield));
            shader.SetVector("_CombustionC", new Vector4(config.combustionPressureScale, config.combustionUpdraftStrength, config.combustionSmokeYield, config.combustionSootSettlingRate));
            shader.SetVector("_CombustionD", new Vector4(config.combustionPyroFertilityYield, config.combustionMoistureIgnitionPenalty, config.combustionSteamSuppression, config.combustionFlameDecay));
            shader.SetVector("_CombustionE", new Vector4(config.combustionFlashVaporizationRate, config.combustionMinFuel, config.combustionMinOxygen, config.combustionSuppressionMoisture));
            shader.SetVector("_StormA", new Vector4(config.stormChargeSeparationRate, config.stormChargeLeakRate, config.stormChargeDiffusionRate, config.stormChargeAdvectionRate));
            shader.SetVector("_StormB", new Vector4(config.stormRimingTempMin, config.stormRimingTempMax, config.stormBreakdownThreshold, config.stormBreakdownAccumulationRate));
            shader.SetVector("_StormC", new Vector4(config.stormChannelDecay, config.stormFlashDecay, config.stormFlashDiffusion, config.stormCooldownRate));
            shader.SetVector("_StormD", new Vector4(config.stormStrikeHeat, config.stormThunderPressure, config.stormChargeDeposit, config.stormIgnitionImpulse));
            shader.SetVector("_StormE", new Vector4(config.stormFlashVaporization, config.stormChannelChargeDrain, config.stormTargetRange, config.stormMaxChannelLength));
            shader.SetVector("_StormF", new Vector4(config.stormStrikeBranchChance, config.stormSheetBranchChance, config.stormMaxStrikesPerTick, config.stormTortuosity));
            shader.SetVector("_StormG", new Vector4(config.stormMinimumHeight, 0f, 0f, 0f));
        }

        private void BindPassTextures(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            shader.SetTexture(kernel, "_MaterialWrite", resources.MaterialWrite);
            shader.SetTexture(kernel, "_StateRead", resources.StateRead);
            shader.SetTexture(kernel, "_StateWrite", resources.StateWrite);
            shader.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            shader.SetTexture(kernel, "_FlowWrite", resources.FlowWrite);
            shader.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            shader.SetTexture(kernel, "_AuxWrite", resources.AuxWrite);
            shader.SetTexture(kernel, "_ShadeRead", resources.ShadeRead);
            shader.SetTexture(kernel, "_ShadeWrite", resources.ShadeWrite);
            shader.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            shader.SetTexture(kernel, "_EcologyWrite", resources.EcologyWrite);
            shader.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            shader.SetTexture(kernel, "_CombustionWrite", resources.CombustionWrite);
            shader.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
            shader.SetTexture(kernel, "_LifeGenomeWrite", resources.LifeGenomeWrite);
            shader.SetTexture(kernel, "_LightRead", resources.LightField);
        }

        private void BindWorldgenOutputs(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, "_MaterialWrite", resources.MaterialRead);
            shader.SetTexture(kernel, "_StateWrite", resources.StateRead);
            shader.SetTexture(kernel, "_FlowWrite", resources.FlowRead);
            shader.SetTexture(kernel, "_AuxWrite", resources.AuxRead);
            shader.SetTexture(kernel, "_ShadeWrite", resources.ShadeRead);
            shader.SetTexture(kernel, "_EcologyWrite", resources.EcologyRead);
            shader.SetTexture(kernel, "_CombustionWrite", resources.CombustionRead);
            shader.SetTexture(kernel, "_StormWrite", resources.StormRead);
        }

        /// <summary>
        /// Rebuild per-cell shade indices from MaterialRead (used when loading pre-v3 snapshots).
        /// </summary>
        public void FillShadesFromMaterials()
        {
            int kernel = worldGeneration.FindKernel("FillShades");
            if (kernel < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: FillShades kernel missing. Reimport WorldGeneration.compute.");
                return;
            }
            worldGeneration.SetInts("_GridSize", resources.Grid.angularResolution, resources.Grid.radialResolution);
            worldGeneration.SetInt("_Seed", config.seed);
            worldGeneration.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            worldGeneration.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            worldGeneration.SetTexture(kernel, "_ShadeWrite", resources.ShadeRead);
            Dispatch(worldGeneration, kernel);
            Graphics.CopyTexture(resources.ShadeRead, resources.ShadeWrite);
        }

        private void DispatchLight(ComputeShader shader, int kernel, float deltaTime)
        {
            if (kernel < 0)
            {
                UnityEngine.Debug.LogError($"GeneSys: missing compute kernel on {shader.name}. Skipping light pass.");
                return;
            }
            SetCommon(shader, kernel, deltaTime);
            shader.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(shader, kernel);
            shader.SetTexture(kernel, "_LightWrite", resources.LightField);
            Dispatch(shader, kernel);
        }

        private void Dispatch(ComputeShader shader, int kernel)
        {
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out _);
            int groupsX = Mathf.CeilToInt(resources.Grid.angularResolution / (float)x);
            int groupsY = Mathf.CeilToInt(resources.Grid.radialResolution / (float)y);
            shader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        private void DispatchStorm(float deltaTime)
        {
            int separate = storm.FindKernel("ChargeSeparation");
            int select = storm.FindKernel("DischargeSelect");
            int walk = storm.FindKernel("DischargeWalk");
            if (separate < 0 || select < 0 || walk < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing storm compute kernel. Skipping storm pass.");
                return;
            }

            SetCommon(storm, separate, deltaTime);
            storm.SetBuffer(separate, "_MaterialDefinitions", materialBuffer);
            BindStormChargeTextures(separate);
            Dispatch(storm, separate);
            resources.SwapStorm();

            strikeCounterZero[0] = 0;
            strikeCounterBuffer.SetData(strikeCounterZero);
            SetCommon(storm, select, deltaTime);
            storm.SetBuffer(select, "_MaterialDefinitions", materialBuffer);
            storm.SetBuffer(select, "_StrikeSeeds", strikeSeedBuffer);
            storm.SetBuffer(select, "_StrikeCounter", strikeCounterBuffer);
            BindStormSelectTextures(select);
            Dispatch(storm, select);

            SetCommon(storm, walk, deltaTime);
            storm.SetBuffer(walk, "_MaterialDefinitions", materialBuffer);
            storm.SetBuffer(walk, "_StrikeSeeds", strikeSeedBuffer);
            storm.SetBuffer(walk, "_StrikeCounter", strikeCounterBuffer);
            BindStormWalkTextures(walk);
            storm.GetKernelThreadGroupSizes(walk, out uint walkX, out _, out _);
            int groups = Mathf.Max(1, Mathf.CeilToInt(MaxStrikeSeeds / (float)walkX));
            storm.Dispatch(walk, groups, 1, 1);
        }

        private void DispatchStormInPlace(ComputeShader shader, int kernel, float deltaTime)
        {
            if (kernel < 0) return;
            SetCommon(shader, kernel, deltaTime);
            shader.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindStormWalkTextures(kernel);
            Dispatch(shader, kernel);
        }

        private void BindStormChargeTextures(int kernel)
        {
            storm.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            storm.SetTexture(kernel, "_StateRead", resources.StateRead);
            storm.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            storm.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            storm.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            storm.SetTexture(kernel, "_StormRead", resources.StormRead);
            storm.SetTexture(kernel, "_StormWrite", resources.StormWrite);
            storm.SetTexture(kernel, "_StateWrite", resources.StateWrite);
            storm.SetTexture(kernel, "_AuxWrite", resources.AuxWrite);
            storm.SetTexture(kernel, "_CombustionWrite", resources.CombustionWrite);
            storm.SetBuffer(kernel, "_StrikeSeeds", strikeSeedBuffer);
            storm.SetBuffer(kernel, "_StrikeCounter", strikeCounterBuffer);
        }

        private void BindStormSelectTextures(int kernel)
        {
            storm.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            storm.SetTexture(kernel, "_StateRead", resources.StateRead);
            storm.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            storm.SetTexture(kernel, "_StormRead", resources.StormRead);
            storm.SetBuffer(kernel, "_StrikeSeeds", strikeSeedBuffer);
            storm.SetBuffer(kernel, "_StrikeCounter", strikeCounterBuffer);
        }

        private void BindStormWalkTextures(int kernel)
        {
            storm.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            storm.SetTexture(kernel, "_StateRead", resources.StateRead);
            storm.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            storm.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            storm.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            storm.SetTexture(kernel, "_StormRead", resources.StormRead);
            storm.SetTexture(kernel, "_StateWrite", resources.StateRead);
            storm.SetTexture(kernel, "_AuxWrite", resources.AuxRead);
            storm.SetTexture(kernel, "_CombustionWrite", resources.CombustionRead);
            storm.SetTexture(kernel, "_StormWrite", resources.StormRead);
            storm.SetBuffer(kernel, "_StrikeSeeds", strikeSeedBuffer);
            storm.SetBuffer(kernel, "_StrikeCounter", strikeCounterBuffer);
        }

        public void Dispose()
        {
            materialBuffer?.Dispose();
            brushBuffer?.Dispose();
            strikeSeedBuffer?.Dispose();
            strikeCounterBuffer?.Dispose();
        }
    }
}
