using System;
using System.Collections.Generic;
using System.Diagnostics;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Geodynamics;
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
        private readonly List<Vector2> globalAdjusts = new(8);
        private readonly List<BrushCommand> floraPaintCommands = new(32);
        private readonly ComputeShader worldGeneration;
        private readonly ComputeShader materialSimulation;
        private readonly ComputeShader geology;
        private readonly ComputeShader hydrology;
        private readonly ComputeShader hydrostatic;
        private readonly ComputeShader weather;
        private readonly ComputeShader mycology;
        private readonly ComputeShader flora;
        private readonly ComputeShader fauna;
        private readonly ComputeShader combustion;
        private readonly ComputeShader storm;
        private readonly ComputeShader climate;
        private readonly ComputeShader geodynamics;
        private readonly ComputeShader margolusTransport;
        private bool climateInit;
        private bool geodynamicsInit;
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
            ComputeShader fauna, ComputeShader grass = null, ComputeShader combustion = null, ComputeShader storm = null,
            ComputeShader wasp = null, ComputeShader plantResources = null, ComputeShader tree = null,
            ComputeShader climate = null, ComputeShader geodynamics = null,
            ComputeShader margolusTransport = null)
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
            this.combustion = combustion;
            this.storm = storm;
            this.climate = climate;
            this.geodynamics = geodynamics;
            this.margolusTransport = margolusTransport;
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
            worldGeneration.SetVector("_WorldGenParams", new Vector4(config.borderNoise, config.protrusionChance, 0f, config.tectonicFaultSeedCount));
            worldGeneration.SetVector("_WorldWaterA", new Vector4(config.targetOceanCoverage, config.minOceanBasins, config.maxOceanBasins, config.seaLevelRadius));
            worldGeneration.SetVector("_WorldWaterB", new Vector4(config.basinDepth, config.terrainRelief, config.coastRoughness, config.initialGroundwaterSaturation));
            worldGeneration.SetVector("_WorldWaterC", new Vector4(config.initialAtmosphericHumidity, config.groundwaterDepth, config.frozenOceans ? 1f : 0f, 0f));
            if (!config.useOgWorldgen)
            {
                worldGeneration.SetVector("_WorldGenV2A", new Vector4(config.metalVeinCount, config.metalVeinMinSize, config.metalVeinMaxSize, config.metalVeinProtrusionChance));
                worldGeneration.SetVector("_WorldGenV2B", new Vector4(config.metalVeinProtrusionDistance, config.iceCapRadius, config.iceCapHeight, config.iceCapRadiusVariation));
                worldGeneration.SetVector("_WorldGenV2C", new Vector4(config.iceCapHeightVariation, 0f, 0f, 0f));
                worldGeneration.SetVector("_WorldGenV3A", new Vector4(config.limestoneDepositCount, config.limestoneDepositMinSize, config.limestoneDepositMaxSize, config.limestoneDepositProtrusionChance));
                worldGeneration.SetVector("_WorldGenV3B", new Vector4(config.limestoneDepositProtrusionDistance, config.clayDepositCount, config.clayDepositMinSize, config.clayDepositMaxSize));
                worldGeneration.SetVector("_WorldGenV3C", new Vector4(config.clayDepositProtrusionChance, config.clayDepositProtrusionDistance, 0f, 0f));
            }
            worldGeneration.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindWorldgenOutputs(worldGeneration, kernel);
            InitializeGeodynamicsFaults();
            BindGeodynamicsState(worldGeneration, kernel);
            Dispatch(worldGeneration, kernel);
            if (flora != null)
            {
                resources.ClearFlora();
                int seedFlora = flora.FindKernel("SeedFlora");
                if (seedFlora >= 0)
                {
                    SetCommon(flora, seedFlora, 0f);
                    flora.SetBuffer(seedFlora, "_MaterialDefinitions", materialBuffer);
                    flora.SetTexture(seedFlora, "_MaterialRead", resources.MaterialRead);
                    flora.SetTexture(seedFlora, "_FloraWrite", resources.FloraWrite);
                    BindOrganismHistory(flora, seedFlora);
                    Dispatch(flora, seedFlora);
                    resources.SwapFlora();
                    CommitFloraWorld();
                }
            }

            resources.ClearFaunaAndAcoustic();
            if (fauna != null)
            {
                int seedFauna = fauna.FindKernel("SeedFauna");
                if (seedFauna >= 0)
                {
                    SetCommon(fauna, seedFauna, 0f);
                    fauna.SetTexture(seedFauna, "_MaterialRead", resources.MaterialRead);
                    fauna.SetTexture(seedFauna, "_FaunaRead", resources.FaunaRead);
                    fauna.SetTexture(seedFauna, "_FaunaWrite", resources.FaunaWrite);
                    BindOrganismHistory(fauna, seedFauna);
                    Dispatch(fauna, seedFauna);
                    resources.SwapFauna();
                }
            }
            resources.CopyReadToWrite();
            RebuildClimate();
            RebuildGeodynamics(true);
        }

        public void QueueBrush(BrushCommand command)
        {
            if (brushCommands.Count >= MaxBrushCommands)
                FlushPendingBrushes();
            if (brushCommands.Count < MaxBrushCommands)
                brushCommands.Add(command);
        }

        public void QueueGlobalAdjust(int channel, float amount)
        {
            if (globalAdjusts.Count < 32)
                globalAdjusts.Add(new Vector2(channel, amount));
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
            command.materialId = FloraGenome.ArchetypeGrass;
            if (floraPaintCommands.Count < 32) floraPaintCommands.Add(command);
        }

        public void QueueTreeSprout(BrushCommand command)
        {
            command.materialId = FloraGenome.ArchetypeTree;
            if (floraPaintCommands.Count < 32) floraPaintCommands.Add(command);
        }

        public void QueueFloraPaint(BrushCommand command)
        {
            if (floraPaintCommands.Count < 32) floraPaintCommands.Add(command);
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

            if (globalAdjusts.Count > 0)
            {
                int adjustKernel = materialSimulation.FindKernel("GlobalFieldAdjust");
                if (adjustKernel >= 0)
                {
                    for (int i = 0; i < globalAdjusts.Count; i++)
                    {
                        materialSimulation.SetInt("_GlobalAdjustChannel", Mathf.RoundToInt(globalAdjusts[i].x));
                        materialSimulation.SetFloat("_GlobalAdjustAmount", globalAdjusts[i].y);
                        DispatchPass(materialSimulation, adjustKernel, deltaTime);
                    }
                }
                else
                    UnityEngine.Debug.LogError("GeneSys: GlobalFieldAdjust kernel missing. Reimport MaterialSimulation.compute.");
                globalAdjusts.Clear();
            }

            if (config.geodynamicsLayerEnable && Due(config.geodynamicsPeriodTicks))
            {
                float geoDt = CadenceDt(deltaTime, config.geodynamicsPeriodTicks);
                DispatchGeodynamics(geoDt);
                DispatchTectonicKinematics(geoDt);
            }

            if (config.coreHeatRate > 0f
                || (config.corePulsePeriodTicks > 0 && config.corePulseHeat > 0f))
                DispatchPass(geology, geology.FindKernel("CoreHeatSource"), deltaTime);

            if (Due(config.slowPassInterval))
                DispatchPass(geology, geology.FindKernel("Volcanism"), CadenceDt(deltaTime, config.slowPassInterval));

            for (int i = 0; i < config.materialSubsteps; i++)
            {
                float subDt = deltaTime / config.materialSubsteps;
                DispatchPass(materialSimulation, materialSimulation.FindKernel("ThermalAndPressure"), subDt);
                DispatchPass(geology, geology.FindKernel("EruptionMotion"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("Electrical"), subDt);
            }

            DispatchPass(materialSimulation, materialSimulation.FindKernel("PhaseChange"), deltaTime);
            if (config.enableMaterialTransport)
            {
                for (int m = 0; m < config.margolusSubsteps; m++)
                    DispatchMargolus(deltaTime / config.margolusSubsteps);
            }

            if (combustion != null)
                DispatchPass(combustion, combustion.FindKernel("Combustion"), deltaTime);

            if (config.climateLayerEnable && Due(config.climateCouplePeriod))
                DispatchClimate(CadenceDt(deltaTime, config.climateCouplePeriod));

            // Atmospheric loop: light → forcing → continuity → pressure diffusion → dynamics → transport → water cycle → precipitation.
            if (flora != null)
                DispatchLight(flora, flora.FindKernel("LightAttenuation"), deltaTime);
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

            // New hydrometeors exist after Precipitation. Fall them before soak/leveling
            // so hydrostatic profiles landed vs airborne instead of a mid-air rain shaft.
            // Skip when nothing can materialize this tick so standing ponds are not
            // given an extra Margolus pass.
            if (config.enableMaterialTransport && config.precipitationRate > 1e-8f)
                DispatchMargolus(deltaTime, liquidOnly: true);

            // Soak this tick's rain/ponding, then saturation seeps see the updated water table.
            DispatchPass(hydrology, hydrology.FindKernel("Groundwater"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("HydrothermalRelease"), deltaTime);
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
                DispatchFlora(deltaTime);
            }

            if (fauna != null)
            {
                DispatchFauna(deltaTime);
            }

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

        private void DispatchMargolus(float deltaTime, bool liquidOnly = false)
        {
            if (margolusTransport == null) return;
            int phaseEven = margolusTransport.FindKernel("MargolusPhaseEven");
            int phaseOdd = margolusTransport.FindKernel("MargolusPhaseOdd");
            int grassEven = margolusTransport.FindKernel("MargolusGrassEven");
            int grassOdd = margolusTransport.FindKernel("MargolusGrassOdd");
            if (phaseEven < 0 || phaseOdd < 0 || grassEven < 0 || grassOdd < 0) return;

            BindMargolusPhase(phaseEven, deltaTime, liquidOnly);
            DispatchMargolusBlocks(margolusTransport, phaseEven);
            if (!liquidOnly)
            {
                BindMargolusGrass(grassEven, deltaTime);
                DispatchMargolusBlocks(margolusTransport, grassEven);
            }
            resources.Swap();
            if (!liquidOnly)
                resources.SwapGrass();

            BindMargolusPhase(phaseOdd, deltaTime, liquidOnly);
            DispatchMargolusBlocks(margolusTransport, phaseOdd);
            if (!liquidOnly)
            {
                BindMargolusGrass(grassOdd, deltaTime);
                DispatchMargolusBlocks(margolusTransport, grassOdd);
                resources.Swap();
                resources.SwapGrass();
            }
            else
            {
                resources.Swap();
            }
        }

        private void BindMargolusPhase(int kernel, float deltaTime, bool liquidOnly = false)
        {
            SetCommon(margolusTransport, kernel, deltaTime);
            BindMargolusParams(kernel, liquidOnly);
            BindPassTextures(margolusTransport, kernel);
            margolusTransport.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            margolusTransport.SetTexture(kernel, "_GrassRead", resources.GrassRead);
            margolusTransport.SetTexture(kernel, "_TreeRead", resources.TreeRead);
        }

        private void BindMargolusGrass(int kernel, float deltaTime)
        {
            SetCommon(margolusTransport, kernel, deltaTime);
            BindMargolusParams(kernel);
            BindPassTextures(margolusTransport, kernel);
            margolusTransport.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            margolusTransport.SetTexture(kernel, "_GrassRead", resources.GrassRead);
            margolusTransport.SetTexture(kernel, "_GrassWrite", resources.GrassWrite);
            margolusTransport.SetTexture(kernel, "_TreeRead", resources.TreeRead);
        }

        private void BindMargolusParams(int kernel, bool liquidOnly = false)
        {
            margolusTransport.SetVector("_MargolusParams", new Vector4(
                1f,
                config.margolusReposeFriction,
                config.margolusMetricEnable ? 1f : 0f,
                liquidOnly ? 0f : (config.margolusFluidEnable ? config.margolusMagmaLevelingBias : 0f)));
            margolusTransport.SetInt("_MargolusLiquidOnly", liquidOnly ? 1 : 0);
        }

        private void DispatchMargolusBlocks(ComputeShader shader, int kernel)
        {
            int numBlocksX = resources.Grid.angularResolution / 2;
            int numBlocksY = resources.Grid.radialResolution / 2;
            int groupsX = Mathf.Max(1, Mathf.CeilToInt(numBlocksX / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(numBlocksY / 8f));
            shader.Dispatch(kernel, groupsX, groupsY, 1);
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

        public void RebuildClimate()
        {
            if (climate == null || resources.ClimateState == null) return;
            DispatchClimate(CadenceDt(1f / Mathf.Max(1f, config.ticksPerSecond), config.climateCouplePeriod), true);
        }

        private void DispatchClimate(float deltaTime, bool init = false)
        {
            if (climate == null || resources.ClimateState == null || resources.ClimateColumns == null)
                return;
            if (!climate.HasKernel("ClimateAggregateColumns") || !climate.HasKernel("ClimateStep"))
            {
                UnityEngine.Debug.LogError("GeneSys: missing climate compute kernel. Skipping climate pass.");
                return;
            }
            int aggregate = climate.FindKernel("ClimateAggregateColumns");
            int step = climate.FindKernel("ClimateStep");

            climateInit = init;
            BindClimatePass(aggregate, deltaTime);
            DispatchColumns(climate, aggregate);
            BindClimatePass(step, deltaTime);
            int bins = ClimateGrid.ClampBinCount(config.climateBinCount);
            climate.Dispatch(step, Mathf.Max(1, Mathf.CeilToInt(bins / 64f)), 1, 1);
            climateInit = false;
        }

        private void BindClimatePass(int kernel, float deltaTime)
        {
            SetCommon(climate, kernel, deltaTime);
            climate.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            climate.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            climate.SetTexture(kernel, "_StateRead", resources.StateRead);
            climate.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            climate.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
            climate.SetTexture(kernel, "_GrassRead", resources.GrassRead);
            climate.SetTexture(kernel, "_TreeRead", resources.TreeRead);
            climate.SetBuffer(kernel, "_ClimateColumns", resources.ClimateColumns);
            climate.SetBuffer(kernel, "_ClimateState", resources.ClimateState);
        }

        private void BindClimateState(ComputeShader shader, int kernel)
        {
            if (resources.ClimateState == null) return;
            if (shader != weather && shader != flora && shader != hydrology && shader != climate)
                return;
            shader.SetBuffer(kernel, "_ClimateState", resources.ClimateState);
        }

        public void ClearDeepTectonicStress()
        {
            if (geology == null || !geology.HasKernel("ClearDeepTectonicStress")) return;
            DispatchPass(geology, geology.FindKernel("ClearDeepTectonicStress"), 0f);
        }

        public void RebuildGeodynamics(bool init = false)
        {
            if (geodynamics == null || resources.GeodynamicsStateRead == null) return;
            if (init)
                InitializeGeodynamicsFaults();
            DispatchGeodynamics(CadenceDt(1f / Mathf.Max(1f, config.ticksPerSecond), config.geodynamicsPeriodTicks), init);
        }

        private void InitializeGeodynamicsFaults()
        {
            if (geodynamics == null || !geodynamics.HasKernel("InitializeFaults") || resources.GeodynamicsStateWrite == null)
                return;
            int kernel = geodynamics.FindKernel("InitializeFaults");
            geodynamicsInit = true;
            BindGeodynamicsPass(kernel, 0f);
            DispatchGeodynamicsGrid(geodynamics, kernel);
            resources.SwapGeodynamics();
            resources.CopyGeodynamicsReadToWrite();
            geodynamicsInit = false;
        }

        private void DispatchTectonicKinematics(float deltaTime)
        {
            if (geology == null || config.tectonicKinematicCoupling <= 0f)
                return;
            if (geology.HasKernel("TectonicDisplacement"))
                DispatchPass(geology, geology.FindKernel("TectonicDisplacement"), deltaTime);
            if (geology.HasKernel("TectonicVertical"))
                DispatchPass(geology, geology.FindKernel("TectonicVertical"), deltaTime);
        }

        private void DispatchGeodynamics(float deltaTime, bool init = false)
        {
            if (geodynamics == null || resources.GeodynamicsStateRead == null)
                return;
            if (!geodynamics.HasKernel("AggregateInterior") || !geodynamics.HasKernel("StepGeodynamics") || !geodynamics.HasKernel("SelectEvents"))
            {
                UnityEngine.Debug.LogError("GeneSys: missing geodynamics compute kernel. Skipping geodynamics pass.");
                return;
            }

            int aggregate = geodynamics.FindKernel("AggregateInterior");
            int step = geodynamics.FindKernel("StepGeodynamics");
            int select = geodynamics.FindKernel("SelectEvents");
            geodynamicsInit = init;
            BindGeodynamicsPass(aggregate, deltaTime);
            geodynamics.SetTexture(aggregate, "_MaterialRead", resources.MaterialRead);
            geodynamics.SetTexture(aggregate, "_StateRead", resources.StateRead);
            geodynamics.SetTexture(aggregate, "_AuxRead", resources.AuxRead);
            DispatchGeodynamicsGrid(geodynamics, aggregate);

            BindGeodynamicsPass(step, deltaTime);
            DispatchGeodynamicsGrid(geodynamics, step);

            if (!init && config.geodynamicsLayerEnable)
            {
                resources.GeodynamicsEventCounter.SetData(new uint[1]);
                BindGeodynamicsPass(select, deltaTime);
                DispatchGeodynamicsGrid(geodynamics, select);
                if (geodynamics.HasKernel("CommitEvent"))
                {
                    int commit = geodynamics.FindKernel("CommitEvent");
                    BindGeodynamicsPass(commit, deltaTime);
                    geodynamics.Dispatch(commit, 1, 1, 1);
                }
            }

            resources.SwapGeodynamics();
            geodynamicsInit = false;
        }

        private void BindGeodynamicsPass(int kernel, float deltaTime)
        {
            SetCommon(geodynamics, kernel, deltaTime);
            geodynamics.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            geodynamics.SetBuffer(kernel, "_GeodynamicsState", resources.GeodynamicsStateRead);
            geodynamics.SetBuffer(kernel, "_GeodynamicsStateWrite", resources.GeodynamicsStateWrite);
            geodynamics.SetBuffer(kernel, "_GeodynamicsEvents", resources.GeodynamicsEvents);
            geodynamics.SetBuffer(kernel, "_GeodynamicsColumns", resources.GeodynamicsColumns);
            geodynamics.SetBuffer(kernel, "_GeodynamicsEventCounter", resources.GeodynamicsEventCounter);
        }

        private void BindGeodynamicsState(ComputeShader shader, int kernel)
        {
            if (resources.GeodynamicsStateRead == null || resources.GeodynamicsEvents == null) return;
            if (shader != geology && shader != hydrology && shader != materialSimulation && shader != worldGeneration && shader != geodynamics)
                return;
            shader.SetBuffer(kernel, "_GeodynamicsState", resources.GeodynamicsStateRead);
            shader.SetBuffer(kernel, "_GeodynamicsEvents", resources.GeodynamicsEvents);
        }

        private void DispatchGeodynamicsGrid(ComputeShader shader, int kernel)
        {
            int angular = GeodynamicsGrid.ClampAngularBins(config.geodynamicsAngularBins);
            int radial = GeodynamicsGrid.ClampRadialBins(config.geodynamicsRadialBins);
            int groupsX = Mathf.Max(1, Mathf.CeilToInt(angular / 8f));
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(radial / 8f));
            shader.Dispatch(kernel, groupsX, groupsY, 1);
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
            shader.SetVector("_WorldGenParams", new Vector4(config.borderNoise, config.protrusionChance, 0f, config.tectonicFaultSeedCount));
            shader.SetVector("_ThermalA", new Vector4(config.thermalMoistureBoost, config.thermalPressureEffect, config.coreTemperature, config.coreHeatRate));
            shader.SetVector("_Geology", new Vector4(config.geodynamicsPressureBuildRate, config.tectonicStrainGain, config.extrusionRate, config.volcanicCoolingRate));
            shader.SetVector("_GeologyB", new Vector4(config.hydrothermalNutrientRate, config.hydrothermalNutrientYield, config.corePulsePeriodTicks, config.corePulseHeat));
            shader.SetVector("_EruptionA", new Vector4(config.eruptionDriveScale, config.eruptionPressureStrength, config.eruptionFlowStrength, config.eruptionBurdenDepth));
            shader.SetVector("_EruptionB", new Vector4(config.eruptionBlastThreshold, config.ashUpdraftStrength, config.ashSettlingStrength, config.ashFertilityStrength));
            shader.SetVector("_GeodynamicsFlags", new Vector4(
                config.geodynamicsLayerEnable ? 1f : 0f,
                geodynamicsInit ? 1f : 0f,
                GeodynamicsGrid.ClampAngularBins(config.geodynamicsAngularBins),
                GeodynamicsGrid.ClampRadialBins(config.geodynamicsRadialBins)));
            shader.SetVector("_GeodynamicsA", new Vector4(
                Mathf.Max(1, config.geodynamicsPeriodTicks),
                config.geodynamicsConvectionStrength,
                config.geodynamicsPressureBuildRate,
                config.geodynamicsPressureLeakage));
            shader.SetVector("_GeodynamicsB", new Vector4(
                config.geodynamicsHeatCoupling,
                config.tectonicStrainGain,
                config.tectonicStrainTransfer,
                config.tectonicFaultHealing));
            shader.SetVector("_GeodynamicsC", new Vector4(
                config.tectonicEarthquakeThreshold,
                config.tectonicReleaseFraction,
                config.tectonicEventFootprint,
                config.tectonicCooldownTicks));
            shader.SetVector("_GeodynamicsD", new Vector4(
                config.tectonicMaxConcurrentEvents,
                config.tectonicSurfaceCoupling,
                config.volcanicReleaseThreshold,
                config.volcanicReleaseFraction));
            shader.SetVector("_GeodynamicsK", new Vector4(
                config.tectonicKinematicCoupling,
                config.tectonicUpliftScale,
                config.tectonicConvergenceScale,
                config.tectonicDisplacementScale));
            shader.SetVector("_GeodynamicsL", new Vector4(
                config.tectonicCoseismicScale,
                config.crustRatio,
                config.tectonicIsostasyScale,
                0f));
            shader.SetFloat("_TectonicCrustRatio", config.crustRatio);
            shader.SetFloat("_TectonicIsostasyScale", config.tectonicIsostasyScale);
            shader.SetVector("_Volcanic", new Vector4(
                config.extrusionRate,
                config.volcanicCoolingRate,
                config.magmaViscosity,
                0f));
            shader.SetVector("_Hydrothermal", new Vector4(
                config.hydrothermalNutrientRate,
                config.hydrothermalNutrientYield,
                config.hydrothermalReleaseThreshold,
                config.tectonicSurfaceCoupling));
            shader.SetVector("_Hydrology", new Vector4(config.infiltrationRate, config.groundwaterRate, config.dissolutionRate, config.collapseRate));
            shader.SetVector("_HydrologyB", new Vector4(config.springDischargeRate, 0f, 0f, 0f));
            shader.SetVector("_HydrologyC", new Vector4(config.runoffRate, config.pondingRate, config.fieldCapacityFraction, 0f));
            shader.SetVector("_Erosion", new Vector4(config.erosionRate, config.dryMoistureThreshold, config.baseSoilCohesion, config.surfaceStressRecoveryRate));
            shader.SetVector("_MoistureErosion", new Vector4(config.dryMoistureThreshold, config.moistureCohesionStrength, 0f, 0f));
            float polarOutput = PolarPoleGeometry.SolarPolarOutput(
                SolarAngle01, PolarPoleGeometry.PoleAngle01(config.seed), config.solarPolarOutputMin);
            shader.SetVector("_WeatherA", new Vector4(config.solarIntensity * polarOutput, config.spaceTemperature, config.atmosphereRadiativeCooling, config.windStrength));
            shader.SetVector("_WeatherB", new Vector4(config.windDamping, config.evaporationRate, config.condensationRate, config.precipitationRate));
            shader.SetVector("_WeatherC", new Vector4(config.vaporPressureScale, SolarAngle01, config.phaseHysteresis, 0f));
            shader.SetVector("_WeatherD", new Vector4(config.atmosphericAdvectionRate, config.vaporDiffusionRate, config.atmosphericBuoyancy, 0f));
            shader.SetVector("_WeatherE", new Vector4(config.vaporCapacityScale, config.cloudRetainMass, config.waterPressureResponse, config.latentHeatScale));
            shader.SetVector("_WeatherF", new Vector4(config.surfaceAirHeatExchange, config.temperatureAdvectionRate, config.pressureCompressibility, config.atmosphericCflLimit));
            shader.SetVector("_WeatherG", new Vector4(config.surfaceAirTemperature, config.atmosphericLapseRate, config.terrainRadiativeCooling, config.verticalBuoyancyStrength));
            shader.SetVector("_WeatherH", new Vector4(config.atmosphereAbsorption, config.dewRate, 0f, 0f));
            shader.SetVector("_WeatherI", new Vector4(config.coriolisStrength, config.velocityAdvectionRate, config.prevailingWind, 0f));
            shader.SetVector("_WeatherJ", new Vector4(config.frontalLiftStrength, config.frontalCollisionPressure, config.frontalDensityDrive, config.frontalSubsidenceScale));
            shader.SetVector("_PressureA", new Vector4(config.pressureDiffusionRate, config.pressureEquilibriumGradient, config.pressureEquilibriumMaximum, 0f));
            shader.SetVector("_PressureB", new Vector4(config.gasPressureDiffusivity, config.fluidPressureDiffusivity, config.porousPressureDiffusivity, config.rigidPressureDiffusivity));
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
            shader.SetVector("_StormG", new Vector4(config.stormMinimumHeight, config.stormStrikeAirHeatFraction, 0f, 0f));
            shader.SetVector("_ClimateFlags", new Vector4(
                config.climateLayerEnable ? 1f : 0f,
                climateInit ? 1f : 0f,
                config.climatePrevailingInject ? 1f : 0f,
                config.climateAlbedoFeedback ? 1f : 0f));
            shader.SetVector("_ClimateA", new Vector4(
                ClimateGrid.ClampBinCount(config.climateBinCount),
                Mathf.Max(1, config.climateCouplePeriod),
                config.climateSlabHeatCapacity,
                config.climateHeatTransport));
            shader.SetVector("_ClimateB", new Vector4(
                config.climateSeasonLengthDays,
                config.climateSeasonalAmplitude,
                config.climateThermalWindGain,
                config.climateIceAlbedo));
            shader.SetVector("_ClimateC", new Vector4(
                config.climateCanopyAlbedoDrop,
                config.climateRoughnessGain,
                config.climateBucketGain,
                config.climateMemoryRate));
            shader.SetVector("_ClimateD", new Vector4(
                config.climateBiomeFeedback ? 1f : 0f,
                config.climateBaseAlbedo,
                config.climateAshAlbedo,
                config.climateBurnBucketPenalty));
            shader.SetVector("_ClimateE", new Vector4(config.climateSlabRadiativeCooling, 0f, 0f, 0f));
            shader.SetVector("_MargolusParams", new Vector4(
                1f,
                config.margolusReposeFriction,
                config.margolusMetricEnable ? 1f : 0f,
                config.margolusFluidEnable ? config.margolusMagmaLevelingBias : 0f));
            shader.SetVector("_WorldGenFlags", new Vector4(config.enableMaterialTransport ? 1f : 0f, 0f, 0f, 0f));
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
                if (shader == flora && resources.FloraRead != null)
                    shader.SetTexture(kernel, "_FloraRead", resources.FloraRead);
                shader.SetTexture(kernel, "_GrassRead", resources.GrassRead);
                shader.SetTexture(kernel, "_TreeRead", resources.TreeRead);
            }
            BindClimateState(shader, kernel);
            BindGeodynamicsState(shader, kernel);
        }

        private void BindOrganismHistory(ComputeShader shader, int kernel)
        {
            if (shader != flora && shader != fauna && shader != combustion)
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
        }

        private void DispatchFlora(float deltaTime)
        {
            bool paintedThisTick = floraPaintCommands.Count > 0;

            if (Due(config.transportPassInterval) && !paintedThisTick)
            {
                float transportDt = CadenceDt(deltaTime, config.transportPassInterval);
                int rootUptake = flora.FindKernel("RootUptake");
                int photo = flora.FindKernel("Photosynthesis");
                int propTransport = flora.FindKernel("PropaguleTransport");

                if (rootUptake >= 0)
                {
                    SetCommon(flora, rootUptake, transportDt);
                    flora.SetBuffer(rootUptake, "_MaterialDefinitions", materialBuffer);
                    BindFloraWorldReads(rootUptake);
                    flora.SetTexture(rootUptake, "_StateRead", resources.StateRead);
                    flora.SetTexture(rootUptake, "_StateWrite", resources.StateWrite);
                    flora.SetTexture(rootUptake, "_AuxRead", resources.AuxRead);
                    flora.SetTexture(rootUptake, "_AuxWrite", resources.AuxWrite);
                    flora.SetTexture(rootUptake, "_FloraRead", resources.FloraRead);
                    flora.SetTexture(rootUptake, "_FloraWrite", resources.FloraWrite);
                    Dispatch(flora, rootUptake);
                    Graphics.CopyTexture(resources.StateWrite, resources.StateRead);
                    Graphics.CopyTexture(resources.AuxWrite, resources.AuxRead);
                    resources.SwapFlora();
                }

                if (photo >= 0)
                {
                    SetCommon(flora, photo, transportDt);
                    flora.SetBuffer(photo, "_MaterialDefinitions", materialBuffer);
                    BindFloraWorldReads(photo);
                    flora.SetTexture(photo, "_StateRead", resources.StateRead);
                    flora.SetTexture(photo, "_AuxRead", resources.AuxRead);
                    flora.SetTexture(photo, "_AuxWrite", resources.AuxWrite);
                    flora.SetTexture(photo, "_CombustionRead", resources.CombustionRead);
                    flora.SetTexture(photo, "_CombustionWrite", resources.CombustionWrite);
                    flora.SetTexture(photo, "_LifeGenomeRead", resources.LifeGenomeRead);
                    flora.SetTexture(photo, "_LifeGenomeWrite", resources.LifeGenomeWrite);
                    flora.SetTexture(photo, "_LightRead", resources.LightField);
                    flora.SetTexture(photo, "_FloraRead", resources.FloraRead);
                    flora.SetTexture(photo, "_FloraWrite", resources.FloraWrite);
                    flora.SetTexture(photo, "_PropaguleRead", resources.PropaguleRead);
                    flora.SetTexture(photo, "_PropaguleWrite", resources.PropaguleWrite);
                    BindOrganismHistory(flora, photo);
                    Dispatch(flora, photo);
                    Graphics.CopyTexture(resources.AuxWrite, resources.AuxRead);
                    Graphics.CopyTexture(resources.CombustionWrite, resources.CombustionRead);
                    Graphics.CopyTexture(resources.LifeGenomeWrite, resources.LifeGenomeRead);
                    resources.SwapFlora();
                    resources.SwapPropagule();
                }

                if (propTransport >= 0)
                {
                    SetCommon(flora, propTransport, transportDt);
                    flora.SetTexture(propTransport, "_FlowRead", resources.FlowRead);
                    flora.SetTexture(propTransport, "_MaterialRead", resources.MaterialRead);
                    flora.SetTexture(propTransport, "_PropaguleRead", resources.PropaguleRead);
                    flora.SetTexture(propTransport, "_PropaguleWrite", resources.PropaguleWrite);
                    Dispatch(flora, propTransport);
                    resources.SwapPropagule();
                }
            }

            if (Due(config.slowPassInterval) && !paintedThisTick)
            {
                float slowDt = CadenceDt(deltaTime, config.slowPassInterval);
                int germ = flora.FindKernel("PropaguleGerminate");
                int clear = flora.FindKernel("ClearFloraClaims");
                int claim = flora.FindKernel("ClaimGrowth");

                if (germ >= 0)
                {
                    SetCommon(flora, germ, slowDt);
                    flora.SetBuffer(germ, "_MaterialDefinitions", materialBuffer);
                    BindFloraWorldReads(germ);
                    flora.SetTexture(germ, "_StateRead", resources.StateRead);
                    flora.SetTexture(germ, "_AuxRead", resources.AuxRead);
                    flora.SetTexture(germ, "_FloraRead", resources.FloraRead);
                    flora.SetTexture(germ, "_FloraWrite", resources.FloraWrite);
                    flora.SetTexture(germ, "_PropaguleRead", resources.PropaguleRead);
                    flora.SetTexture(germ, "_PropaguleWrite", resources.PropaguleWrite);
                    BindOrganismHistory(flora, germ);
                    Dispatch(flora, germ);
                    resources.SwapFlora();
                    resources.SwapPropagule();
                }

                if (clear >= 0 && claim >= 0)
                {
                    SetCommon(flora, clear, slowDt);
                    flora.SetTexture(clear, "_FloraClaimsWrite", resources.FloraClaims);
                    Dispatch(flora, clear);

                    SetCommon(flora, claim, slowDt);
                    flora.SetBuffer(claim, "_MaterialDefinitions", materialBuffer);
                    BindFloraWorldReads(claim);
                    flora.SetTexture(claim, "_FloraRead", resources.FloraRead);
                    flora.SetTexture(claim, "_FloraClaimsWrite", resources.FloraClaims);
                    Dispatch(flora, claim);

                    CommitFloraWorld();
                }
            }

            if (!paintedThisTick)
                DispatchFloraMigration(deltaTime);

            if (floraPaintCommands.Count > 0)
            {
                int paint = flora.FindKernel("PaintFlora");
                int clear = flora.FindKernel("ClearFloraClaims");
                if (paint >= 0)
                {
                    if (clear >= 0)
                    {
                        SetCommon(flora, clear, deltaTime);
                        flora.SetTexture(clear, "_FloraClaimsWrite", resources.FloraClaims);
                        Dispatch(flora, clear);
                    }

                    foreach (BrushCommand command in floraPaintCommands)
                    {
                        SetCommon(flora, paint, deltaTime);
                        flora.SetBuffer(paint, "_MaterialDefinitions", materialBuffer);
                        flora.SetVector("_FloraPaintCell", new Vector4(command.center.x, command.center.y, command.radius, (float)command.materialId));
                        flora.SetTexture(paint, "_MaterialRead", resources.MaterialRead);
                        flora.SetTexture(paint, "_FloraRead", resources.FloraRead);
                        flora.SetTexture(paint, "_FloraWrite", resources.FloraWrite);
                        BindOrganismHistory(flora, paint);
                        Dispatch(flora, paint);
                        resources.SwapFlora();
                    }
                    CommitFloraWorld();
                }
                floraPaintCommands.Clear();
            }
        }

        private void CommitFloraWorld()
        {
            if (flora == null) return;
            int applyWorld = flora.FindKernel("ApplyFloraWorld");
            int applyState = flora.FindKernel("ApplyFloraState");
            if (applyWorld < 0 || applyState < 0) return;

            SetCommon(flora, applyWorld, 0f);
            flora.SetBuffer(applyWorld, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(flora, applyWorld);
            flora.SetTexture(applyWorld, "_FloraRead", resources.FloraRead);
            flora.SetTexture(applyWorld, "_FloraClaims", resources.FloraClaims);
            BindOrganismHistory(flora, applyWorld);
            Dispatch(flora, applyWorld);
            resources.Swap();

            SetCommon(flora, applyState, 0f);
            flora.SetBuffer(applyState, "_MaterialDefinitions", materialBuffer);
            BindFloraWorldReads(applyState);
            flora.SetTexture(applyState, "_FloraRead", resources.FloraRead);
            flora.SetTexture(applyState, "_FloraWrite", resources.FloraWrite);
            flora.SetTexture(applyState, "_FloraClaims", resources.FloraClaims);
            BindOrganismHistory(flora, applyState);
            Dispatch(flora, applyState);
            resources.SwapFlora();
        }

        private void DispatchFloraMigration(float deltaTime)
        {
            if (config.floraPoleDriftRate <= 1e-8f
                && config.floraWindShearRate <= 1e-8f
                && config.floraRainShearRate <= 1e-8f)
                return;

            int migrate = flora.FindKernel("FloraMigration");
            int follow = flora.FindKernel("FloraMigrationFollow");
            if (migrate < 0 || follow < 0) return;

            SetCommon(flora, migrate, deltaTime);
            flora.SetBuffer(migrate, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(flora, migrate);
            Dispatch(flora, migrate);

            SetCommon(flora, follow, deltaTime);
            flora.SetBuffer(follow, "_MaterialDefinitions", materialBuffer);
            BindFloraWorldReads(follow);
            flora.SetTexture(follow, "_FloraRead", resources.FloraRead);
            flora.SetTexture(follow, "_FloraWrite", resources.FloraWrite);
            Dispatch(flora, follow);

            resources.Swap();
            resources.SwapFlora();
        }

        private void BindFloraWorldReads(int kernel)
        {
            flora.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            flora.SetTexture(kernel, "_StateRead", resources.StateRead);
            flora.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            flora.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            flora.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            flora.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            flora.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
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
            BindPassTextures(fauna, applyWorld);
            fauna.SetTexture(applyWorld, "_FloraRead", resources.FloraRead);
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

        private void BindFaunaWorldReads(int kernel)
        {
            fauna.SetTexture(kernel, "_MaterialRead", resources.MaterialRead);
            fauna.SetTexture(kernel, "_StateRead", resources.StateRead);
            fauna.SetTexture(kernel, "_FlowRead", resources.FlowRead);
            fauna.SetTexture(kernel, "_AuxRead", resources.AuxRead);
            fauna.SetTexture(kernel, "_EcologyRead", resources.EcologyRead);
            fauna.SetTexture(kernel, "_CombustionRead", resources.CombustionRead);
            fauna.SetTexture(kernel, "_LifeGenomeRead", resources.LifeGenomeRead);
            fauna.SetTexture(kernel, "_FloraRead", resources.FloraRead);
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
