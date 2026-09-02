using System;
using System.Collections.Generic;
using System.Diagnostics;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

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
        private const int MaxBrushCommands = 1024;
        private readonly List<BrushCommand> brushCommands = new(MaxBrushCommands);
        private readonly List<BrushCommand> grassPaintCommands = new(32);
        private readonly List<BrushCommand> treePaintCommands = new(32);
        private readonly ComputeShader worldGeneration;
        private readonly ComputeShader materialSimulation;
        private readonly ComputeShader geology;
        private readonly ComputeShader hydrology;
        private readonly ComputeShader hydrostatic;
        private readonly ComputeShader weather;
        private readonly ComputeShader mycology;
        private readonly ComputeShader flora;
        private readonly ComputeShader fauna;
        private readonly ComputeShader grass;
        private readonly ComputeShader plantResources;
        private readonly ComputeShader tree;
        private readonly ComputeShader wasp;
        private readonly ComputeShader combustion;
        private readonly ComputeShader storm;
        private readonly GraphicsBuffer strikeSeedBuffer;
        private readonly GraphicsBuffer strikeCounterBuffer;
        private readonly uint[] strikeCounterZero = new uint[1];
        private readonly GraphicsBuffer organismHistoryBuffer;
        private readonly GraphicsBuffer organismHistoryCounterBuffer;
        private readonly uint[] organismHistoryCounterZero = new uint[1];
        private readonly uint[] organismHistoryCounterRead = new uint[1];
        private readonly OrganismHistoryLog.GpuEvent[] organismHistoryScratch;
        private const int MaxStrikeSeeds = 32;
        private const int StrikeSeedStride = 16;
        public const int OrganismHistoryGpuCapacity = 2048;
        private int tick;

        public int TickIndex => tick;
        public void SetTickIndex(int value) => tick = Math.Max(0, value);
        public double LastTickMilliseconds { get; private set; }
        public float SolarAngle01 => Mathf.Repeat(tick / Mathf.Max(1f, config.ticksPerSecond * config.dayLengthSeconds), 1f);

        public GpuPassScheduler(SimulationConfig config, SimulationResources resources, MaterialRegistry registry,
            ComputeShader worldGeneration, ComputeShader materialSimulation, ComputeShader geology,
            ComputeShader hydrology, ComputeShader hydrostatic, ComputeShader weather, ComputeShader mycology, ComputeShader flora,
            ComputeShader fauna, ComputeShader grass, ComputeShader combustion, ComputeShader storm,
            ComputeShader wasp = null, ComputeShader plantResources = null, ComputeShader tree = null)
        {
            this.config = config;
            this.resources = resources;
            this.worldGeneration = worldGeneration;
            this.materialSimulation = materialSimulation;
            this.geology = geology;
            this.hydrology = hydrology;
            this.hydrostatic = hydrostatic != null ? hydrostatic : hydrology;
            this.weather = weather;
            this.mycology = mycology;
            this.flora = flora;
            this.fauna = fauna;
            this.grass = grass;
            this.plantResources = plantResources;
            this.tree = tree;
            this.wasp = wasp;
            this.combustion = combustion;
            this.storm = storm;
            materialBuffer = registry.CreateGpuBuffer();
            brushBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxBrushCommands, BrushCommand.Stride);
            strikeSeedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxStrikeSeeds, StrikeSeedStride);
            strikeCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            organismHistoryBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, OrganismHistoryGpuCapacity, OrganismHistoryLog.GpuEvent.Stride);
            organismHistoryCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            organismHistoryScratch = new OrganismHistoryLog.GpuEvent[OrganismHistoryGpuCapacity];
            ResetOrganismHistoryCounter();
        }

        public void ResetOrganismHistoryCounter()
        {
            organismHistoryCounterZero[0] = 0;
            organismHistoryCounterBuffer.SetData(organismHistoryCounterZero);
        }

        private bool historyReadbackPending;

        public void DrainOrganismHistory(OrganismHistoryLog log)
        {
            if (log == null || historyReadbackPending) return;
            historyReadbackPending = true;
            AsyncGPUReadback.Request(organismHistoryCounterBuffer, countRequest =>
            {
                if (countRequest.hasError)
                {
                    historyReadbackPending = false;
                    return;
                }
                var countData = countRequest.GetData<uint>();
                uint rawCount = countData.Length > 0 ? countData[0] : 0u;
                int count = (int)Math.Min(rawCount, (uint)OrganismHistoryGpuCapacity);
                if (count <= 0)
                {
                    historyReadbackPending = false;
                    return;
                }
                AsyncGPUReadback.Request(organismHistoryBuffer, count * OrganismHistoryLog.GpuEvent.Stride, 0, eventRequest =>
                {
                    historyReadbackPending = false;
                    if (eventRequest.hasError || log == null) return;
                    var eventData = eventRequest.GetData<OrganismHistoryLog.GpuEvent>();
                    int copyCount = Math.Min(count, eventData.Length);
                    for (int i = 0; i < copyCount; i++)
                        organismHistoryScratch[i] = eventData[i];
                    if (copyCount > 0)
                        log.AppendFromGpu(organismHistoryScratch, copyCount);
                    ResetOrganismHistoryCounter();
                });
            });
        }

        public void GenerateWorld()
        {
            tick = 0;
            ResetOrganismHistoryCounter();
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
                    BindOrganismHistory(flora, seedFlora);
                    Dispatch(flora, seedFlora);
                }
            }
            resources.ClearFaunaAndAcoustic();
            resources.ClearGrass();
            resources.ClearWasp();
            resources.ClearTree();
            if (fauna != null)
            {
                int seedFauna = fauna.FindKernel("SeedFauna");
                if (seedFauna >= 0)
                {
                    SetCommon(fauna, seedFauna, 0f);
                    fauna.SetTexture(seedFauna, "_MaterialRead", resources.MaterialRead);
                    fauna.SetTexture(seedFauna, "_FaunaRead", resources.FaunaRead);
                    fauna.SetTexture(seedFauna, "_FaunaWrite", resources.FaunaRead);
                    BindOrganismHistory(fauna, seedFauna);
                    Dispatch(fauna, seedFauna);
                }
            }
            if (grass != null)
            {
                int seedGrass = grass.FindKernel("SeedGrass");
                if (seedGrass >= 0)
                {
                    SetCommon(grass, seedGrass, 0f);
                    grass.SetBuffer(seedGrass, "_MaterialDefinitions", materialBuffer);
                    grass.SetTexture(seedGrass, "_MaterialRead", resources.MaterialRead);
                    grass.SetTexture(seedGrass, "_GrassRead", resources.GrassRead);
                    grass.SetTexture(seedGrass, "_GrassWrite", resources.GrassWrite);
                    BindOrganismHistory(grass, seedGrass);
                    Dispatch(grass, seedGrass);
                    resources.SwapGrass();
                }
            }
            if (tree != null)
            {
                int seedTree = tree.FindKernel("SeedTree");
                if (seedTree >= 0)
                {
                    SetCommon(tree, seedTree, 0f);
                    tree.SetTexture(seedTree, "_MaterialRead", resources.MaterialRead);
                    tree.SetTexture(seedTree, "_TreeRead", resources.TreeRead);
                    tree.SetTexture(seedTree, "_TreeWrite", resources.TreeWrite);
                    BindOrganismHistory(tree, seedTree);
                    Dispatch(tree, seedTree);
                    resources.SwapTree();
                    if (config.treeSeedAtWorldgen)
                        CommitTreeWorld();
                }
            }
            if (wasp != null)
            {
                int scatterWasp = wasp.FindKernel("ScatterWasp");
                if (scatterWasp >= 0 && config.waspSeedAtWorldgen)
                {
                    SetCommon(wasp, scatterWasp, 0f);
                    wasp.SetTexture(scatterWasp, "_MaterialRead", resources.MaterialRead);
                    wasp.SetTexture(scatterWasp, "_MaterialWrite", resources.MaterialRead);
                    Dispatch(wasp, scatterWasp);
                }
                int seedWasp = wasp.FindKernel("SeedWasp");
                if (seedWasp >= 0)
                {
                    SetCommon(wasp, seedWasp, 0f);
                    wasp.SetTexture(seedWasp, "_MaterialRead", resources.MaterialRead);
                    wasp.SetTexture(seedWasp, "_WaspRead", resources.WaspRead);
                    wasp.SetTexture(seedWasp, "_WaspWrite", resources.WaspRead);
                    BindOrganismHistory(wasp, seedWasp);
                    Dispatch(wasp, seedWasp);
                }
            }
            resources.CopyReadToWrite();
        }

        public void QueueBrush(BrushCommand command)
        {
            if (brushCommands.Count >= MaxBrushCommands)
                FlushPendingBrushes();
            if (brushCommands.Count < MaxBrushCommands)
                brushCommands.Add(command);
        }

        private void FlushPendingBrushes()
        {
            if (brushCommands.Count == 0) return;
            brushBuffer.SetData(brushCommands);
            int brushKernel = materialSimulation.FindKernel("ApplyEdits");
            materialSimulation.SetInt("_BrushCount", brushCommands.Count);
            materialSimulation.SetBuffer(brushKernel, "_BrushCommands", brushBuffer);
            DispatchPass(materialSimulation, brushKernel, 0f);
            brushCommands.Clear();
        }

        public void QueueGrassSeed(BrushCommand command)
        {
            if (grassPaintCommands.Count < 32) grassPaintCommands.Add(command);
        }

        public void QueueTreeSprout(BrushCommand command)
        {
            if (treePaintCommands.Count < 32) treePaintCommands.Add(command);
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
                DispatchPass(materialSimulation, materialSimulation.FindKernel("LiquidDensityExchange"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("MaterialMotion"), subDt);
                DispatchPass(geology, geology.FindKernel("EruptionMotion"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("Electrical"), subDt);
            }

            DispatchPass(materialSimulation, materialSimulation.FindKernel("PhaseChange"), deltaTime);

            if (Due(config.slowPassInterval))
                DispatchPass(geology, geology.FindKernel("Volcanism"), CadenceDt(deltaTime, config.slowPassInterval));

            if (config.coreReactionFrequency > 0
                && config.coreReactionMagnitude > 0f
                && tick % config.coreReactionFrequency == 0)
                DispatchPass(geology, geology.FindKernel("CoreReaction"), deltaTime);

            if (combustion != null)
                DispatchPass(combustion, combustion.FindKernel("Combustion"), deltaTime);

            // Atmospheric loop: forcing → continuity → pressure diffusion → dynamics → transport → water cycle → precipitation.
            DispatchPass(weather, weather.FindKernel("AtmosphericForcing"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericContinuity"), deltaTime);
            DispatchPass(materialSimulation, materialSimulation.FindKernel("PressureDiffusion"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericDynamics"), deltaTime);
            DispatchPass(geology, geology.FindKernel("AshTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("AtmosphericTransport"), deltaTime);
            DispatchPass(weather, weather.FindKernel("WaterCycle"), deltaTime);
            DispatchPass(weather, weather.FindKernel("Precipitation"), deltaTime);

            if (storm != null)
                DispatchStorm(deltaTime);

            // Soak this tick's rain/ponding, then springs/geysers see the updated water table.
            DispatchPass(hydrology, hydrology.FindKernel("Groundwater"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("GeothermalDischarge"), deltaTime);
            DispatchHydrostaticLeveling(deltaTime);

            // Erosion sees the current tick's moisture, exposure, and flow after weather/runoff.
            if (Due(config.slowPassInterval))
            {
                float slowDt = CadenceDt(deltaTime, config.slowPassInterval);
                DispatchPass(hydrology, hydrology.FindKernel("ErosionAndCollapse"), slowDt);
                DispatchPass(hydrology, hydrology.FindKernel("AshFertilization"), slowDt);
                DispatchPass(hydrology, hydrology.FindKernel("DetritusExchange"), slowDt);
            }

            if (mycology != null)
            {
                if (Due(config.transportPassInterval))
                    DispatchPass(mycology, mycology.FindKernel("SporeTransport"), CadenceDt(deltaTime, config.transportPassInterval));
                if (Due(config.slowPassInterval))
                    DispatchPass(mycology, mycology.FindKernel("ColonyLifecycle"), CadenceDt(deltaTime, config.slowPassInterval));
            }

            if (flora != null)
            {
                if (Due(config.transportPassInterval))
                {
                    float transportDt = CadenceDt(deltaTime, config.transportPassInterval);
                    DispatchLight(flora, flora.FindKernel("LightAttenuation"), transportDt);
                    DispatchPass(flora, flora.FindKernel("SporeTransport"), transportDt);
                    DispatchPass(flora, flora.FindKernel("Photosynthesis"), transportDt);
                }
                if (Due(config.slowPassInterval))
                {
                    float slowDt = CadenceDt(deltaTime, config.slowPassInterval);
                    DispatchPass(flora, flora.FindKernel("FloraLifecycle"), slowDt);
                    if (config.floraPoleDriftRate > 1e-8f || config.floraWindShearRate > 1e-8f || config.floraRainShearRate > 1e-8f)
                        DispatchPass(flora, flora.FindKernel("FloraMigration"), slowDt);
                }
            }

            if (plantResources != null && Due(config.transportPassInterval))
                DispatchPlantRoots(CadenceDt(deltaTime, config.transportPassInterval));

            if (grass != null)
                DispatchGrass(deltaTime);

            if (tree != null)
                DispatchTree(deltaTime);

            if (fauna != null)
                DispatchFauna(deltaTime);

            // Wasps run last so predation resolves against settled cricket state.
            if (wasp != null)
                DispatchWasp(deltaTime);

            tick++;
            stopwatch.Stop();
            LastTickMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private static int Interval(int value) => Mathf.Max(1, value);
        private bool Due(int interval)
        {
            int n = Interval(interval);
            if (n <= 1) return true;
            return tick > 0 && tick % n == 0;
        }
        private static float CadenceDt(float deltaTime, int interval) => deltaTime * Interval(interval);

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
            BindOrganismHistory(shader, kernel);
            Dispatch(shader, kernel);
            resources.Swap();
        }

        // Column solver profiles the surface once, relaxes the face exchange against the tiny
        // column buffer several times, then writes the settled result back to the grid once.
        // Only the apply pass touches every cell, so extra iterations buy reach for almost
        // nothing: a single flux pass can only move water one column per tick, which leaves
        // basin-scale slopes and rain-made mounds standing for thousands of ticks.
        private void DispatchHydrostaticLeveling(float deltaTime)
        {
            int build = hydrostatic.FindKernel("BuildSurfaceWaterColumns");
            int flux = hydrostatic.FindKernel("ComputeHydrostaticFaceFlux");
            int integrate = hydrostatic.FindKernel("IntegrateHydrostaticColumns");
            int apply = hydrology.FindKernel("ApplyHydrostaticColumns");
            if (build < 0 || flux < 0 || integrate < 0 || apply < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing hydrostatic hydrology kernel. Skipping surface leveling.");
                return;
            }

            SetCommon(hydrostatic, build, deltaTime);
            hydrostatic.SetBuffer(build, "_MaterialDefinitions", materialBuffer);
            hydrostatic.SetTexture(build, "_MaterialRead", resources.MaterialRead);
            hydrostatic.SetTexture(build, "_StateRead", resources.StateRead);
            hydrostatic.SetBuffer(build, "_WaterColumns", resources.WaterColumn);
            hydrostatic.SetBuffer(build, "_WaterFaceFlux", resources.WaterFaceFlux);
            DispatchColumns(hydrostatic, build);

            SetCommon(hydrostatic, flux, deltaTime);
            hydrostatic.SetBuffer(flux, "_MaterialDefinitions", materialBuffer);
            hydrostatic.SetTexture(flux, "_MaterialRead", resources.MaterialRead);
            hydrostatic.SetTexture(flux, "_StateRead", resources.StateRead);
            hydrostatic.SetBuffer(flux, "_WaterColumns", resources.WaterColumn);
            hydrostatic.SetBuffer(flux, "_WaterFaceFlux", resources.WaterFaceFlux);
            SetCommon(hydrostatic, integrate, deltaTime);
            hydrostatic.SetBuffer(integrate, "_MaterialDefinitions", materialBuffer);
            hydrostatic.SetBuffer(integrate, "_WaterColumns", resources.WaterColumn);
            hydrostatic.SetBuffer(integrate, "_WaterFaceFlux", resources.WaterFaceFlux);
            int iterations = Mathf.Clamp(config.hydrostaticIterations, 1, 64);
            for (int i = 0; i < iterations; i++)
            {
                DispatchColumns(hydrostatic, flux);
                DispatchColumns(hydrostatic, integrate);
            }
            Graphics.ClearRandomWriteTargets();

            SetCommon(hydrology, apply, deltaTime);
            hydrology.SetBuffer(apply, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(hydrology, apply);
            hydrology.SetBuffer(apply, "_WaterColumns", resources.WaterColumn);
            DispatchColumns(hydrology, apply);
            resources.Swap();
        }

        private void DispatchColumns(ComputeShader shader, int kernel)
        {
            int groupsX = Mathf.Max(1, Mathf.CeilToInt(resources.Grid.angularResolution / 64f));
            shader.Dispatch(kernel, groupsX, 1, 1);
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
            float polarOutput = PolarPoleGeometry.SolarPolarOutput(
                SolarAngle01, PolarPoleGeometry.PoleAngle01(config.seed), config.solarPolarOutputMin);
            shader.SetVector("_WeatherA", new Vector4(config.atmosphereSolarHeating * polarOutput, config.spaceTemperature, config.atmosphereRadiativeCooling, config.windStrength));
            shader.SetVector("_WeatherB", new Vector4(config.windDamping, config.evaporationRate, config.condensationRate, config.precipitationRate));
            shader.SetVector("_WeatherC", new Vector4(config.vaporPressureScale, SolarAngle01, config.phaseHysteresis, config.magmaViscosity));
            shader.SetVector("_WeatherD", new Vector4(config.atmosphericAdvectionRate, config.vaporDiffusionRate, config.atmosphericBuoyancy, config.humidityBuoyancy));
            shader.SetVector("_WeatherE", new Vector4(config.saturationCapacityScale, config.cloudPrecipitationThreshold, config.waterPressureResponse, config.latentHeatScale));
            shader.SetVector("_WeatherF", new Vector4(config.surfaceAirHeatExchange, config.temperatureAdvectionRate, config.pressureCompressibility, config.atmosphericCflLimit));
            shader.SetVector("_WeatherG", new Vector4(config.surfaceAirTemperature, config.atmosphericLapseRate, config.terrainRadiativeCooling, config.verticalBuoyancyStrength));
            shader.SetVector("_WeatherH", new Vector4(config.solarTerrainPenetration, config.terrainSolarHeating * polarOutput, 0f, 0f));
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
            shader.SetVector("_FaunaA", new Vector4(config.faunaInitialCalories, config.faunaInitialHydration, config.faunaMaturityTicks, config.faunaDecisionInterval));
            shader.SetVector("_FaunaB", new Vector4(config.faunaMaintenanceRate, config.faunaHydrationDrain, config.faunaCalorieCapacity, config.faunaFullThreshold));
            shader.SetVector("_FaunaC", new Vector4(config.faunaHungerThreshold, config.faunaReproductionCalorieThreshold, config.faunaHopImpulse, config.faunaHopCost));
            shader.SetVector("_FaunaD", new Vector4(config.faunaFeedCost, config.faunaDryMass, config.faunaDrag, config.faunaWindResistance));
            shader.SetVector("_FaunaE", new Vector4(config.faunaMoistureMass, config.faunaSupportBoost, config.faunaWetPenalty, config.faunaGeneExpressionRange));
            shader.SetVector("_FaunaF", new Vector4(config.faunaBaseMutationRate, config.faunaSenseRadius, config.faunaHearingRange, config.faunaThreatTemperature));
            shader.SetVector("_FaunaG", new Vector4(config.faunaAcousticSpeed, config.faunaAcousticDamping, config.faunaFeedCallAmplitude, config.faunaMateCallAmplitude));
            shader.SetVector("_FaunaH", new Vector4(config.faunaMateCooldownTicks, config.faunaReproduceCooldownTicks, config.faunaClutchMin, config.faunaClutchMax));
            shader.SetVector("_FaunaI", new Vector4(config.faunaHatchTicksMin, config.faunaHatchTicksMax, config.faunaEggDesiccationMoisture, config.faunaEggHeatDeath));
            shader.SetVector("_FaunaJ", new Vector4(config.faunaEggDisplacement, config.faunaWanderRate, config.faunaSurvivalTempMin, config.faunaSurvivalTempMax));
            shader.SetVector("_FaunaK", new Vector4(config.faunaSeedAtWorldgen ? 1f : 0f, 0f, 0f, 0f));
            shader.SetFloat("_TicksPerDay", Mathf.Max(1f, config.ticksPerSecond * config.dayLengthSeconds));
            shader.SetFloat("_TicksPerSecond", Mathf.Max(1f, config.ticksPerSecond));
            shader.SetVector("_GrassA", new Vector4(config.grassWaterUptakeRate, config.grassNutrientUptakeRate, config.grassPhotosynthesisRate, config.grassGrowthRate));
            shader.SetVector("_GrassB", new Vector4(config.grassDecayRate, config.grassMaintenanceRate, config.grassNightDrain, config.grassFlowerEnergyThreshold));
            shader.SetVector("_GrassC", new Vector4(config.grassGeneExpressionRange, config.grassGrowthTempMin, config.grassGrowthTempMax, config.grassGrowthMoistureMin));
            shader.SetVector("_GrassD", new Vector4(config.grassGrowthMoistureMax, config.grassSurvivalTempMin, config.grassSurvivalTempMax, config.grassSurvivalMoistureMin));
            shader.SetVector("_GrassE", new Vector4(config.grassSurvivalMoistureMax, config.grassMinLight, config.grassAdultBiomass, config.grassNectarAmount));
            shader.SetVector("_GrassF", new Vector4(config.grassPollenEmitRate, config.grassPollenTransportRate, config.grassSeedTransportRate, config.grassCanopyOpacity));
            shader.SetVector("_GrassG", new Vector4(config.grassPollenWindRate, config.grassPollenWaterRate, config.grassPollenSettlingRate, config.grassSeedWindRate));
            shader.SetVector("_GrassH", new Vector4(config.grassSeedWaterRate, config.grassSeedSettlingRate, config.grassInitialBiomass, config.grassInitialEnergy));
            shader.SetVector("_GrassI", new Vector4(config.grassSeedAtWorldgen ? 1f : 0f, config.grassRootCohesionBonus, config.detritusInitialNutrient, config.detritusInitialMoisture));
            shader.SetVector("_TreeA", new Vector4(config.treeWaterUptakeRate, config.treeNutrientUptakeRate, config.treePhotosynthesisRate, config.treeGrowthRate));
            shader.SetVector("_TreeB", new Vector4(config.treeDecayRate, config.treeMaintenanceRate, config.treeNightDrain, config.treeVascularRate));
            shader.SetVector("_TreeC", new Vector4(config.treeGeneExpressionRange, config.treeGrowthTempMin, config.treeGrowthTempMax, config.treeGrowthMoistureMin));
            shader.SetVector("_TreeD", new Vector4(config.treeGrowthMoistureMax, config.treeSurvivalTempMin, config.treeSurvivalTempMax, config.treeSurvivalMoistureMin));
            shader.SetVector("_TreeE", new Vector4(config.treeSurvivalMoistureMax, config.treeMinLight, config.treeGrowthCost, config.treeWindBias));
            shader.SetVector("_TreeF", new Vector4(config.treeSproutHeight, config.treeSaplingHeight, config.treeMaxHeight, config.treeMaxTrunkWidth));
            shader.SetVector("_TreeG", new Vector4(config.treeInitialEnergy, config.treeInitialHydration, config.treeInitialNutrient, config.treeInitialHealth));
            shader.SetVector("_TreeH", new Vector4(config.treeSeedAtWorldgen ? 1f : 0f, config.treeRootCohesionBonus, config.treeCanopyOpacity, config.treeExposureDamage));
            shader.SetVector("_TreeI", new Vector4(config.treeLeafLifeTicks, config.treeRotTicks, config.treeDisconnectTicks, config.treeSaplingBranchMin));
            shader.SetVector("_TreeJ", new Vector4(config.treeSaplingBranchMax, 0f, 0f, 0f));
            shader.SetVector("_WaspA", new Vector4(config.waspInitialCalories, config.waspInitialHydration, config.waspMaturityTicks, config.waspDecisionInterval));
            shader.SetVector("_WaspB", new Vector4(config.waspMaintenanceRate, config.waspFlightDrain, config.waspHydrationDrain, config.waspCalorieCapacity));
            shader.SetVector("_WaspC", new Vector4(config.waspFullThreshold, config.waspHungerThreshold, config.waspStarvationThreshold, config.waspReproductionCalorieThreshold));
            shader.SetVector("_WaspD", new Vector4(config.waspCruiseAltitude, config.waspAltitudeGain, config.waspLiftPower, config.waspSurfaceScanRange));
            shader.SetVector("_WaspE", new Vector4(config.waspBodyMass, config.waspDrag, config.waspWindCoupling, config.waspUpdraftCoupling));
            shader.SetVector("_WaspF", new Vector4(config.waspSwoopImpulse, config.waspSenseRadius, config.waspPreyCalorieConversion, config.waspPreyHydrationTransfer));
            shader.SetVector("_WaspG", new Vector4(config.waspNectarDraw, config.waspNectarCalories, config.waspNectarHydration, config.waspPollenCapacity));
            shader.SetVector("_WaspH", new Vector4(config.waspGeneExpressionRange, config.waspBaseMutationRate, config.waspMateCooldownTicks, config.waspReproduceCooldownTicks));
            shader.SetVector("_WaspI", new Vector4(config.waspClutchMin, config.waspClutchMax, config.waspHatchTicksMin, config.waspHatchTicksMax));
            shader.SetVector("_WaspJ", new Vector4(config.waspEggDesiccationMoisture, config.waspEggHeatDeath, config.waspSurvivalTempMin, config.waspSurvivalTempMax));
            shader.SetVector("_WaspK", new Vector4(config.waspThreatTemperature, config.waspSeedAtWorldgen ? 1f : 0f, 0f, 0f));
            shader.SetVector("_DetritusA", new Vector4(config.detritusVaporAbsorbRate, config.detritusEvaporationRate, config.detritusMoistureDistributeRate, config.detritusNutrientLeachRate));
            shader.SetVector("_DetritusB", new Vector4(config.detritusDecompositionRate, 0f, 0f, 0f));
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
            if (shader == combustion)
            {
                shader.SetTexture(kernel, "_FaunaRead", resources.FaunaRead);
                shader.SetTexture(kernel, "_TreeRead", resources.TreeRead);
            }
            if (shader == flora || shader == hydrology)
            {
                shader.SetTexture(kernel, "_GrassRead", resources.GrassRead);
                shader.SetTexture(kernel, "_TreeRead", resources.TreeRead);
            }
        }

        private void BindOrganismHistory(ComputeShader shader, int kernel)
        {
            if (shader != flora && shader != fauna && shader != grass && shader != combustion && shader != wasp && shader != tree)
                return;
            shader.SetBuffer(kernel, "_OrganismHistory", organismHistoryBuffer);
            shader.SetBuffer(kernel, "_OrganismHistoryCounter", organismHistoryCounterBuffer);
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
            BindOrganismHistory(shader, kernel);
            DispatchColumns(shader, kernel);
        }

        private void Dispatch(ComputeShader shader, int kernel)
        {
            int groupsX = Mathf.Max(1, Mathf.CeilToInt(resources.Grid.angularResolution / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(resources.Grid.radialResolution / 8f));
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
            int groups = Mathf.Max(1, Mathf.CeilToInt(MaxStrikeSeeds / 32f));
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

        private void DispatchGrass(float deltaTime)
        {
            bool paintedThisTick = grassPaintCommands.Count > 0;

            if (Due(config.transportPassInterval) && !paintedThisTick)
            {
                float transportDt = CadenceDt(deltaTime, config.transportPassInterval);
                int demand = grass.FindKernel("RootDemand");
                int debit = grass.FindKernel("SoilDebit");
                int life = grass.FindKernel("PhotosynthesisLifecycle");
                int pollen = grass.FindKernel("PollenTransport");
                int seeds = grass.FindKernel("SeedTransport");
                if (demand < 0 || debit < 0 || life < 0 || pollen < 0 || seeds < 0)
                {
                    UnityEngine.Debug.LogError("GeneSys: missing grass compute kernel. Skipping grass transport.");
                }
                else
                {
                    if (plantResources == null)
                    {
                        SetCommon(grass, demand, transportDt);
                        BindGrassWorldReads(demand);
                        grass.SetTexture(demand, "_GrassRead", resources.GrassRead);
                        grass.SetTexture(demand, "_GrassRootFlux", resources.GrassRootFlux);
                        Dispatch(grass, demand);

                        SetCommon(grass, debit, transportDt);
                        grass.SetTexture(debit, "_StateRead", resources.StateRead);
                        grass.SetTexture(debit, "_AuxRead", resources.AuxRead);
                        grass.SetTexture(debit, "_StateWrite", resources.StateWrite);
                        grass.SetTexture(debit, "_AuxWrite", resources.AuxWrite);
                        grass.SetTexture(debit, "_GrassRootFlux", resources.GrassRootFlux);
                        Dispatch(grass, debit);
                        Graphics.CopyTexture(resources.StateWrite, resources.StateRead);
                        Graphics.CopyTexture(resources.AuxWrite, resources.AuxRead);
                    }

                SetCommon(grass, life, transportDt);
                grass.SetBuffer(life, "_MaterialDefinitions", materialBuffer);
                BindGrassWorldReads(life);
                grass.SetTexture(life, "_LightRead", resources.LightField);
                grass.SetTexture(life, "_GrassRead", resources.GrassRead);
                grass.SetTexture(life, "_GrassWrite", resources.GrassWrite);
                grass.SetTexture(life, "_PropaguleRead", resources.PropaguleRead);
                grass.SetTexture(life, "_PropaguleWrite", resources.PropaguleWrite);
                grass.SetTexture(life, "_GrassRootFlux", resources.GrassRootFlux);
                grass.SetTexture(life, "_GrassVisit", resources.GrassVisit);
                BindOrganismHistory(grass, life);
                Dispatch(grass, life);
                resources.SwapGrass();
                resources.SwapPropagule();

                int clearVisit = grass.FindKernel("ClearGrassVisit");
                if (clearVisit >= 0)
                {
                    SetCommon(grass, clearVisit, transportDt);
                    grass.SetTexture(clearVisit, "_GrassVisitWrite", resources.GrassVisit);
                    Dispatch(grass, clearVisit);
                }

                SetCommon(grass, pollen, transportDt);
                BindGrassWorldReads(pollen);
                grass.SetTexture(pollen, "_PropaguleRead", resources.PropaguleRead);
                grass.SetTexture(pollen, "_PropaguleWrite", resources.PropaguleWrite);
                Dispatch(grass, pollen);
                resources.SwapPropagule();

                SetCommon(grass, seeds, transportDt);
                BindGrassWorldReads(seeds);
                grass.SetTexture(seeds, "_PropaguleRead", resources.PropaguleRead);
                grass.SetTexture(seeds, "_PropaguleWrite", resources.PropaguleWrite);
                Dispatch(grass, seeds);
                resources.SwapPropagule();
                }
            }

            if (Due(config.slowPassInterval) && !paintedThisTick)
            {
                float slowDt = CadenceDt(deltaTime, config.slowPassInterval);
                int germ = grass.FindKernel("Germination");
                int clear = grass.FindKernel("ClearDropClaims");
                int claim = grass.FindKernel("ClaimFlowerDrop");
                int apply = grass.FindKernel("ApplyFlowerDrop");
                if (germ < 0 || clear < 0 || claim < 0 || apply < 0)
                {
                    UnityEngine.Debug.LogError("GeneSys: missing grass compute kernel. Skipping grass slow pass.");
                }
                else
                {

                SetCommon(grass, germ, slowDt);
                BindGrassWorldReads(germ);
                grass.SetTexture(germ, "_GrassRead", resources.GrassRead);
                grass.SetTexture(germ, "_GrassWrite", resources.GrassWrite);
                grass.SetTexture(germ, "_PropaguleRead", resources.PropaguleRead);
                grass.SetTexture(germ, "_PropaguleWrite", resources.PropaguleWrite);
                BindOrganismHistory(grass, germ);
                Dispatch(grass, germ);
                resources.SwapGrass();
                resources.SwapPropagule();

                SetCommon(grass, clear, slowDt);
                grass.SetTexture(clear, "_GrassDropClaimsWrite", resources.GrassDropClaims);
                Dispatch(grass, clear);

                SetCommon(grass, claim, slowDt);
                BindGrassWorldReads(claim);
                grass.SetTexture(claim, "_GrassRead", resources.GrassRead);
                grass.SetTexture(claim, "_GrassDropClaimsWrite", resources.GrassDropClaims);
                Dispatch(grass, claim);

                int dropWorld = hydrology.FindKernel("ApplyFlowerDrop");
                if (dropWorld >= 0)
                {
                    SetCommon(hydrology, dropWorld, slowDt);
                    hydrology.SetBuffer(dropWorld, "_MaterialDefinitions", materialBuffer);
                    BindPassTextures(hydrology, dropWorld);
                    hydrology.SetTexture(dropWorld, "_GrassDropClaims", resources.GrassDropClaims);
                    Dispatch(hydrology, dropWorld);
                    resources.Swap();
                }

                SetCommon(grass, apply, slowDt);
                grass.SetTexture(apply, "_GrassRead", resources.GrassRead);
                grass.SetTexture(apply, "_GrassWrite", resources.GrassWrite);
                Dispatch(grass, apply);
                resources.SwapGrass();
                }
            }

            if (grassPaintCommands.Count > 0)
            {
                int paint = grass.FindKernel("PaintGrass");
                if (paint >= 0)
                {
                    foreach (BrushCommand command in grassPaintCommands)
                    {
                        SetCommon(grass, paint, deltaTime);
                        grass.SetVector("_GrassPaintCell", new Vector4(command.center.x, command.center.y, command.radius, 0f));
                        grass.SetTexture(paint, "_MaterialRead", resources.MaterialRead);
                        grass.SetTexture(paint, "_GrassRead", resources.GrassRead);
                        grass.SetTexture(paint, "_GrassWrite", resources.GrassWrite);
                        BindOrganismHistory(grass, paint);
                        Dispatch(grass, paint);
                        resources.SwapGrass();
                    }
                }
                grassPaintCommands.Clear();
            }
        }

        private void BindGrassWorldReads(int kernel)
        {
            grass.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            grass.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            grass.SetTexture(kernel, "_StateRead", resources.StateRead);
            grass.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            grass.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            grass.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            grass.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
        }

        private void DispatchPlantRoots(float deltaTime)
        {
            int demand = plantResources.FindKernel("RootDemand");
            int debit = plantResources.FindKernel("SoilDebit");
            if (demand < 0 || debit < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing plant resource kernel. Skipping shared root allocation.");
                return;
            }

            SetCommon(plantResources, demand, deltaTime);
            plantResources.SetTexture(demand, "_MaterialRead", resources.MaterialRead);
            plantResources.SetTexture(demand, "_StateRead", resources.StateRead);
            plantResources.SetTexture(demand, "_AuxRead", resources.AuxRead);
            plantResources.SetTexture(demand, "_GrassRead", resources.GrassRead);
            plantResources.SetTexture(demand, "_TreeRead", resources.TreeRead);
            plantResources.SetTexture(demand, "_GrassRootFlux", resources.GrassRootFlux);
            Dispatch(plantResources, demand);

            SetCommon(plantResources, debit, deltaTime);
            plantResources.SetTexture(debit, "_MaterialRead", resources.MaterialRead);
            plantResources.SetTexture(debit, "_StateRead", resources.StateRead);
            plantResources.SetTexture(debit, "_AuxRead", resources.AuxRead);
            plantResources.SetTexture(debit, "_StateWrite", resources.StateWrite);
            plantResources.SetTexture(debit, "_AuxWrite", resources.AuxWrite);
            plantResources.SetTexture(debit, "_GrassRootFlux", resources.GrassRootFlux);
            Dispatch(plantResources, debit);
            Graphics.CopyTexture(resources.StateWrite, resources.StateRead);
            Graphics.CopyTexture(resources.AuxWrite, resources.AuxRead);
        }

        private void DispatchTree(float deltaTime)
        {
            bool paintedThisTick = treePaintCommands.Count > 0;
            int physiology = tree.FindKernel("Physiology");
            int clear = tree.FindKernel("ClearGrowthClaims");
            int claim = tree.FindKernel("ClaimGrowth");
            int applyWorld = tree.FindKernel("ApplyWorld");
            int applyState = tree.FindKernel("ApplyState");
            if (physiology < 0 || clear < 0 || claim < 0 || applyWorld < 0 || applyState < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing tree compute kernel. Skipping tree pass.");
                treePaintCommands.Clear();
                return;
            }

            if (!paintedThisTick)
            {
                SetCommon(tree, physiology, deltaTime);
                tree.SetBuffer(physiology, "_MaterialDefinitions", materialBuffer);
                BindTreeWorldReads(physiology);
                tree.SetTexture(physiology, "_LightRead", resources.LightField);
                tree.SetTexture(physiology, "_GrassRootFlux", resources.GrassRootFlux);
                tree.SetTexture(physiology, "_TreeRead", resources.TreeRead);
                tree.SetTexture(physiology, "_TreeWrite", resources.TreeWrite);
                BindOrganismHistory(tree, physiology);
                Dispatch(tree, physiology);
                resources.SwapTree();

                SetCommon(tree, clear, deltaTime);
                tree.SetTexture(clear, "_TreeGrowthClaimsWrite", resources.TreeGrowthClaims);
                Dispatch(tree, clear);

                SetCommon(tree, claim, deltaTime);
                tree.SetBuffer(claim, "_MaterialDefinitions", materialBuffer);
                BindTreeWorldReads(claim);
                tree.SetTexture(claim, "_TreeRead", resources.TreeRead);
                tree.SetTexture(claim, "_TreeGrowthClaimsWrite", resources.TreeGrowthClaims);
                Dispatch(tree, claim);

                CommitTreeWorld();
            }

            if (treePaintCommands.Count > 0)
            {
                int paint = tree.FindKernel("PaintTree");
                if (paint >= 0)
                {
                    SetCommon(tree, clear, deltaTime);
                    tree.SetTexture(clear, "_TreeGrowthClaimsWrite", resources.TreeGrowthClaims);
                    Dispatch(tree, clear);
                    foreach (BrushCommand command in treePaintCommands)
                    {
                        SetCommon(tree, paint, deltaTime);
                        tree.SetVector("_TreePaintCell", new Vector4(command.center.x, command.center.y, command.radius, 0f));
                        tree.SetTexture(paint, "_MaterialRead", resources.MaterialRead);
                        tree.SetTexture(paint, "_TreeRead", resources.TreeRead);
                        tree.SetTexture(paint, "_TreeWrite", resources.TreeWrite);
                        BindOrganismHistory(tree, paint);
                        Dispatch(tree, paint);
                        resources.SwapTree();
                    }
                    CommitTreeWorld();
                }
                treePaintCommands.Clear();
            }
        }

        private void CommitTreeWorld()
        {
            int applyWorld = tree.FindKernel("ApplyWorld");
            int applyState = tree.FindKernel("ApplyState");
            if (applyWorld < 0 || applyState < 0) return;

            SetCommon(tree, applyWorld, 0f);
            tree.SetBuffer(applyWorld, "_MaterialDefinitions", materialBuffer);
            BindTreeApplyWorld(applyWorld);
            BindOrganismHistory(tree, applyWorld);
            Dispatch(tree, applyWorld);
            resources.Swap();

            SetCommon(tree, applyState, 0f);
            tree.SetBuffer(applyState, "_MaterialDefinitions", materialBuffer);
            BindTreeWorldReads(applyState);
            tree.SetTexture(applyState, "_TreeRead", resources.TreeRead);
            tree.SetTexture(applyState, "_TreeWrite", resources.TreeWrite);
            tree.SetTexture(applyState, "_TreeGrowthClaims", resources.TreeGrowthClaims);
            BindOrganismHistory(tree, applyState);
            Dispatch(tree, applyState);
            resources.SwapTree();
        }

        private void BindTreeWorldReads(int kernel)
        {
            tree.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            tree.SetTexture(kernel, "_StateRead", resources.StateRead);
            tree.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            tree.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            tree.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            tree.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            tree.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
        }

        private void BindTreeApplyWorld(int kernel)
        {
            tree.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            tree.SetTexture(kernel, "_MaterialWrite", resources.MaterialWrite);
            tree.SetTexture(kernel, "_StateRead", resources.StateRead);
            tree.SetTexture(kernel, "_StateWrite", resources.StateWrite);
            tree.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            tree.SetTexture(kernel, "_FlowWrite", resources.FlowWrite);
            tree.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            tree.SetTexture(kernel, "_AuxWrite", resources.AuxWrite);
            tree.SetTexture(kernel, "_ShadeRead", resources.ShadeRead);
            tree.SetTexture(kernel, "_ShadeWrite", resources.ShadeWrite);
            tree.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            tree.SetTexture(kernel, "_EcologyWrite", resources.EcologyWrite);
            tree.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            tree.SetTexture(kernel, "_CombustionWrite", resources.CombustionWrite);
            tree.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
            tree.SetTexture(kernel, "_LifeGenomeWrite", resources.LifeGenomeWrite);
            tree.SetTexture(kernel, "_TreeRead", resources.TreeRead);
            tree.SetTexture(kernel, "_TreeGrowthClaims", resources.TreeGrowthClaims);
        }

        private void DispatchFauna(float deltaTime)
        {
            int seed = fauna.FindKernel("SeedFauna");
            int acoustic = fauna.FindKernel("AcousticPropagate");
            int metabolism = fauna.FindKernel("FaunaMetabolism");
            int clear = fauna.FindKernel("ClearFaunaClaims");
            int claim = fauna.FindKernel("ClaimFaunaActions");
            int applyWorld = fauna.FindKernel("ApplyFaunaWorld");
            int applyState = fauna.FindKernel("ApplyFaunaState");
            if (seed < 0 || acoustic < 0 || metabolism < 0 || clear < 0 || claim < 0 || applyWorld < 0 || applyState < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing fauna compute kernel. Skipping fauna pass.");
                return;
            }

            SetCommon(fauna, seed, deltaTime);
            fauna.SetTexture(seed, "_MaterialRead", resources.MaterialRead);
            fauna.SetTexture(seed, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(seed, "_FaunaWrite", resources.FaunaWrite);
            BindOrganismHistory(fauna, seed);
            Dispatch(fauna, seed);
            resources.SwapFauna();

            SetCommon(fauna, acoustic, deltaTime);
            fauna.SetBuffer(acoustic, "_MaterialDefinitions", materialBuffer);
            fauna.SetTexture(acoustic, "_MaterialRead", resources.MaterialRead);
            fauna.SetTexture(acoustic, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(acoustic, "_AcousticRead", resources.AcousticRead);
            fauna.SetTexture(acoustic, "_AcousticPrev", resources.AcousticPrev);
            fauna.SetTexture(acoustic, "_AcousticWrite", resources.AcousticWrite);
            Dispatch(fauna, acoustic);
            resources.SwapAcoustic();

            SetCommon(fauna, metabolism, deltaTime);
            fauna.SetBuffer(metabolism, "_MaterialDefinitions", materialBuffer);
            BindFaunaWorldReads(metabolism);
            fauna.SetTexture(metabolism, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(metabolism, "_FaunaWrite", resources.FaunaWrite);
            fauna.SetTexture(metabolism, "_AcousticRead", resources.AcousticRead);
            BindOrganismHistory(fauna, metabolism);
            Dispatch(fauna, metabolism);
            resources.SwapFauna();

            SetCommon(fauna, clear, deltaTime);
            fauna.SetTexture(clear, "_FaunaClaimsWrite", resources.FaunaClaims);
            Dispatch(fauna, clear);

            SetCommon(fauna, claim, deltaTime);
            fauna.SetBuffer(claim, "_MaterialDefinitions", materialBuffer);
            BindFaunaWorldReads(claim);
            fauna.SetTexture(claim, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(claim, "_FaunaClaimsWrite", resources.FaunaClaims);
            Dispatch(fauna, claim);

            SetCommon(fauna, applyWorld, deltaTime);
            fauna.SetBuffer(applyWorld, "_MaterialDefinitions", materialBuffer);
            fauna.SetTexture(applyWorld, "_MaterialRead", resources.MaterialRead);
            fauna.SetTexture(applyWorld, "_MaterialWrite", resources.MaterialWrite);
            fauna.SetTexture(applyWorld, "_StateRead", resources.StateRead);
            fauna.SetTexture(applyWorld, "_StateWrite", resources.StateWrite);
            fauna.SetTexture(applyWorld, "_FlowRead", resources.FlowRead);
            fauna.SetTexture(applyWorld, "_FlowWrite", resources.FlowWrite);
            fauna.SetTexture(applyWorld, "_AuxRead", resources.AuxRead);
            fauna.SetTexture(applyWorld, "_AuxWrite", resources.AuxWrite);
            fauna.SetTexture(applyWorld, "_ShadeRead", resources.ShadeRead);
            fauna.SetTexture(applyWorld, "_ShadeWrite", resources.ShadeWrite);
            fauna.SetTexture(applyWorld, "_EcologyRead", resources.EcologyRead);
            fauna.SetTexture(applyWorld, "_EcologyWrite", resources.EcologyWrite);
            fauna.SetTexture(applyWorld, "_CombustionRead", resources.CombustionRead);
            fauna.SetTexture(applyWorld, "_CombustionWrite", resources.CombustionWrite);
            fauna.SetTexture(applyWorld, "_LifeGenomeRead", resources.LifeGenomeRead);
            fauna.SetTexture(applyWorld, "_LifeGenomeWrite", resources.LifeGenomeWrite);
            fauna.SetTexture(applyWorld, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(applyWorld, "_FaunaClaims", resources.FaunaClaims);
            BindOrganismHistory(fauna, applyWorld);
            Dispatch(fauna, applyWorld);

            SetCommon(fauna, applyState, deltaTime);
            fauna.SetBuffer(applyState, "_MaterialDefinitions", materialBuffer);
            BindFaunaWorldReads(applyState);
            fauna.SetTexture(applyState, "_FaunaRead", resources.FaunaRead);
            fauna.SetTexture(applyState, "_FaunaWrite", resources.FaunaWrite);
            fauna.SetTexture(applyState, "_FaunaClaims", resources.FaunaClaims);
            BindOrganismHistory(fauna, applyState);
            Dispatch(fauna, applyState);

            resources.Swap();
            resources.SwapFauna();
        }

        private void DispatchWasp(float deltaTime)
        {
            int seed = wasp.FindKernel("SeedWasp");
            int metabolism = wasp.FindKernel("WaspMetabolism");
            int clear = wasp.FindKernel("ClearWaspClaims");
            int claim = wasp.FindKernel("ClaimWaspActions");
            int flower = wasp.FindKernel("ApplyWaspFlower");
            int applyWorld = wasp.FindKernel("ApplyWaspWorld");
            int applyState = wasp.FindKernel("ApplyWaspState");
            if (seed < 0 || metabolism < 0 || clear < 0 || claim < 0 || flower < 0 || applyWorld < 0 || applyState < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: missing wasp compute kernel. Skipping wasp pass.");
                return;
            }

            SetCommon(wasp, seed, deltaTime);
            wasp.SetTexture(seed, "_MaterialRead", resources.MaterialRead);
            wasp.SetTexture(seed, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(seed, "_WaspWrite", resources.WaspWrite);
            BindOrganismHistory(wasp, seed);
            Dispatch(wasp, seed);
            resources.SwapWasp();

            SetCommon(wasp, metabolism, deltaTime);
            wasp.SetBuffer(metabolism, "_MaterialDefinitions", materialBuffer);
            BindWaspWorldReads(metabolism);
            wasp.SetTexture(metabolism, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(metabolism, "_WaspWrite", resources.WaspWrite);
            wasp.SetTexture(metabolism, "_FaunaRead", resources.FaunaRead);
            wasp.SetTexture(metabolism, "_GrassRead", resources.GrassRead);
            wasp.SetTexture(metabolism, "_AcousticRead", resources.AcousticRead);
            BindOrganismHistory(wasp, metabolism);
            Dispatch(wasp, metabolism);
            resources.SwapWasp();

            SetCommon(wasp, clear, deltaTime);
            wasp.SetTexture(clear, "_WaspClaimsWrite", resources.WaspClaims);
            Dispatch(wasp, clear);

            SetCommon(wasp, claim, deltaTime);
            wasp.SetBuffer(claim, "_MaterialDefinitions", materialBuffer);
            BindWaspWorldReads(claim);
            wasp.SetTexture(claim, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(claim, "_FaunaRead", resources.FaunaRead);
            wasp.SetTexture(claim, "_GrassRead", resources.GrassRead);
            wasp.SetTexture(claim, "_AcousticRead", resources.AcousticRead);
            wasp.SetTexture(claim, "_WaspClaimsWrite", resources.WaspClaims);
            Dispatch(wasp, claim);

            // Records the visit for the next grass pass. Runs before the world pass so it
            // still sees the pre-kill material grid.
            SetCommon(wasp, flower, deltaTime);
            wasp.SetBuffer(flower, "_MaterialDefinitions", materialBuffer);
            wasp.SetTexture(flower, "_MaterialRead", resources.MaterialRead);
            wasp.SetTexture(flower, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(flower, "_GrassRead", resources.GrassRead);
            wasp.SetTexture(flower, "_WaspClaims", resources.WaspClaims);
            wasp.SetTexture(flower, "_GrassVisitWrite", resources.GrassVisit);
            Dispatch(wasp, flower);

            SetCommon(wasp, applyWorld, deltaTime);
            wasp.SetBuffer(applyWorld, "_MaterialDefinitions", materialBuffer);
            wasp.SetTexture(applyWorld, "_MaterialRead", resources.MaterialRead);
            wasp.SetTexture(applyWorld, "_MaterialWrite", resources.MaterialWrite);
            wasp.SetTexture(applyWorld, "_StateRead", resources.StateRead);
            wasp.SetTexture(applyWorld, "_StateWrite", resources.StateWrite);
            wasp.SetTexture(applyWorld, "_FlowRead", resources.FlowRead);
            wasp.SetTexture(applyWorld, "_FlowWrite", resources.FlowWrite);
            wasp.SetTexture(applyWorld, "_AuxRead", resources.AuxRead);
            wasp.SetTexture(applyWorld, "_AuxWrite", resources.AuxWrite);
            wasp.SetTexture(applyWorld, "_ShadeRead", resources.ShadeRead);
            wasp.SetTexture(applyWorld, "_ShadeWrite", resources.ShadeWrite);
            wasp.SetTexture(applyWorld, "_EcologyRead", resources.EcologyRead);
            wasp.SetTexture(applyWorld, "_EcologyWrite", resources.EcologyWrite);
            wasp.SetTexture(applyWorld, "_CombustionRead", resources.CombustionRead);
            wasp.SetTexture(applyWorld, "_CombustionWrite", resources.CombustionWrite);
            wasp.SetTexture(applyWorld, "_LifeGenomeRead", resources.LifeGenomeRead);
            wasp.SetTexture(applyWorld, "_LifeGenomeWrite", resources.LifeGenomeWrite);
            wasp.SetTexture(applyWorld, "_FaunaRead", resources.FaunaRead);
            wasp.SetTexture(applyWorld, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(applyWorld, "_WaspClaims", resources.WaspClaims);
            BindOrganismHistory(wasp, applyWorld);
            Dispatch(wasp, applyWorld);

            SetCommon(wasp, applyState, deltaTime);
            wasp.SetBuffer(applyState, "_MaterialDefinitions", materialBuffer);
            BindWaspWorldReads(applyState);
            wasp.SetTexture(applyState, "_WaspRead", resources.WaspRead);
            wasp.SetTexture(applyState, "_WaspWrite", resources.WaspWrite);
            wasp.SetTexture(applyState, "_FaunaRead", resources.FaunaRead);
            wasp.SetTexture(applyState, "_GrassRead", resources.GrassRead);
            wasp.SetTexture(applyState, "_AcousticRead", resources.AcousticRead);
            wasp.SetTexture(applyState, "_WaspClaims", resources.WaspClaims);
            BindOrganismHistory(wasp, applyState);
            Dispatch(wasp, applyState);

            resources.Swap();
            resources.SwapWasp();
        }

        private void BindWaspWorldReads(int kernel)
        {
            wasp.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            wasp.SetTexture(kernel, "_StateRead", resources.StateRead);
            wasp.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            wasp.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            wasp.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            wasp.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            wasp.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
        }

        private void BindFaunaWorldReads(int kernel)
        {
            fauna.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            fauna.SetTexture(kernel, "_StateRead", resources.StateRead);
            fauna.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            fauna.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            fauna.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            fauna.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            fauna.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
        }

        public void Dispose()
        {
            materialBuffer?.Dispose();
            brushBuffer?.Dispose();
            strikeSeedBuffer?.Dispose();
            strikeCounterBuffer?.Dispose();
            organismHistoryBuffer?.Dispose();
            organismHistoryCounterBuffer?.Dispose();
        }
    }
}
