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
        private int tick;

        public int TickIndex => tick;
        public void SetTickIndex(int value) => tick = Math.Max(0, value);
        public double LastTickMilliseconds { get; private set; }
        public float SolarAngle01 => Mathf.Repeat(tick / Mathf.Max(1f, config.ticksPerSecond * config.dayLengthSeconds), 1f);

        public GpuPassScheduler(SimulationConfig config, SimulationResources resources, MaterialRegistry registry,
            ComputeShader worldGeneration, ComputeShader materialSimulation, ComputeShader geology,
            ComputeShader hydrology, ComputeShader weather, ComputeShader mycology)
        {
            this.config = config;
            this.resources = resources;
            this.worldGeneration = worldGeneration;
            this.materialSimulation = materialSimulation;
            this.geology = geology;
            this.hydrology = hydrology;
            this.weather = weather;
            this.mycology = mycology;
            materialBuffer = registry.CreateGpuBuffer();
            brushBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, BrushCommand.Stride);
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

            DispatchPass(hydrology, hydrology.FindKernel("Groundwater"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("GeothermalDischarge"), deltaTime);
            // Atmospheric loop: forcing → continuity → pressure diffusion → dynamics → transport → water cycle.
            DispatchPass(weather, weather.FindKernel("AtmosphericForcing"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericContinuity"), deltaTime);
            DispatchPass(materialSimulation, materialSimulation.FindKernel("PressureDiffusion"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericDynamics"), deltaTime);
            DispatchPass(geology, geology.FindKernel("AshTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("WaterCycle"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("RunoffAndDeposition"), deltaTime);

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
            shader.SetVector("_GeologyB", new Vector4(config.hydrothermalStrength, config.ventChemicalRate, 0f, 0f));
            shader.SetVector("_EruptionA", new Vector4(config.magmaEruption, config.eruptionPressureStrength, config.eruptionFlowStrength, config.eruptionBurdenDepth));
            shader.SetVector("_EruptionB", new Vector4(config.eruptionBlastThreshold, config.ashUpdraftStrength, config.ashSettlingStrength, config.ashFertilityStrength));
            shader.SetVector("_Hydrology", new Vector4(config.infiltrationRate, config.groundwaterRate, config.dissolutionRate, config.collapseRate));
            shader.SetVector("_HydrologyB", new Vector4(config.springHeadThreshold, config.springDischargeRate, config.geyserHeatThreshold, config.geyserDischargeRate));
            shader.SetVector("_HydrologyC", new Vector4(config.runoffRate, config.pondingRate, 0f, config.geyserCooldownSeconds));
            shader.SetVector("_Erosion", new Vector4(config.erosionRate, 0f, config.baseSoilCohesion, config.stressDecayRate));
            shader.SetVector("_MoistureErosion", new Vector4(config.dryMoistureThreshold, config.moistureCohesionStrength, config.capillaryEvaporationFraction, 0f));
            shader.SetVector("_WeatherA", new Vector4(config.solarIntensity, config.spaceTemperature, config.radiativeCooling, config.windStrength));
            shader.SetVector("_WeatherB", new Vector4(config.windDamping, config.evaporationRate, config.condensationRate, config.precipitationRate));
            shader.SetVector("_WeatherC", new Vector4(config.vaporPressureScale, SolarAngle01, config.phaseHysteresis, config.magmaViscosity));
            shader.SetVector("_WeatherD", new Vector4(config.atmosphericAdvectionRate, config.vaporDiffusionRate, config.atmosphericBuoyancy, config.humidityBuoyancy));
            shader.SetVector("_WeatherE", new Vector4(config.saturationCapacityScale, config.cloudPrecipitationThreshold, 0f, 0f));
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
        }

        private void BindWorldgenOutputs(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, "_MaterialWrite", resources.MaterialRead);
            shader.SetTexture(kernel, "_StateWrite", resources.StateRead);
            shader.SetTexture(kernel, "_FlowWrite", resources.FlowRead);
            shader.SetTexture(kernel, "_AuxWrite", resources.AuxRead);
            shader.SetTexture(kernel, "_ShadeWrite", resources.ShadeRead);
            shader.SetTexture(kernel, "_EcologyWrite", resources.EcologyRead);
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

        private void Dispatch(ComputeShader shader, int kernel)
        {
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out _);
            int groupsX = Mathf.CeilToInt(resources.Grid.angularResolution / (float)x);
            int groupsY = Mathf.CeilToInt(resources.Grid.radialResolution / (float)y);
            shader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        public void Dispose()
        {
            materialBuffer?.Dispose();
            brushBuffer?.Dispose();
        }
    }
}
