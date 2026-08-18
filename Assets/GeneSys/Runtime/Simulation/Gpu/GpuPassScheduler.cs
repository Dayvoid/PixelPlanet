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
        private int tick;

        public int TickIndex => tick;
        public void SetTickIndex(int value) => tick = Math.Max(0, value);
        public double LastTickMilliseconds { get; private set; }
        public float SolarAngle01 => Mathf.Repeat(tick / Mathf.Max(1f, config.ticksPerSecond * config.dayLengthSeconds), 1f);

        public GpuPassScheduler(SimulationConfig config, SimulationResources resources, MaterialRegistry registry,
            ComputeShader worldGeneration, ComputeShader materialSimulation, ComputeShader geology,
            ComputeShader hydrology, ComputeShader weather)
        {
            this.config = config;
            this.resources = resources;
            this.worldGeneration = worldGeneration;
            this.materialSimulation = materialSimulation;
            this.geology = geology;
            this.hydrology = hydrology;
            this.weather = weather;
            materialBuffer = registry.CreateGpuBuffer();
            brushBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, BrushCommand.Stride);
        }

        public void GenerateWorld()
        {
            tick = 0;
            int kernel = worldGeneration.FindKernel("GenerateWorld");
            if (kernel < 0)
            {
                UnityEngine.Debug.LogError("GeneSys: GenerateWorld kernel missing. Reimport WorldGeneration.compute and fix shader compile errors.");
                return;
            }
            SetCommon(worldGeneration, kernel, 0f);
            worldGeneration.SetInt("_Seed", config.seed);
            worldGeneration.SetVector("_LayerRatios", new Vector4(config.coreRatio, config.mantleRatio, config.crustRatio, config.soilRatio));
            worldGeneration.SetVector("_WorldGenParams", new Vector4(config.borderNoise, config.protrusionChance, config.groundwaterDepth, config.faultCount));
            worldGeneration.SetVector("_WorldWaterA", new Vector4(config.targetOceanCoverage, config.minOceanBasins, config.maxOceanBasins, config.seaLevelRadius));
            worldGeneration.SetVector("_WorldWaterB", new Vector4(config.basinDepth, config.terrainRelief, config.coastRoughness, config.initialGroundwaterSaturation));
            worldGeneration.SetVector("_WorldWaterC", new Vector4(config.initialAtmosphericHumidity, config.groundwaterDepth, 0f, 0f));
            worldGeneration.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindWorldgenOutputs(worldGeneration, kernel);
            Dispatch(worldGeneration, kernel);
            resources.CopyReadToWrite();
            RecomputeHydrostatic();
        }

        public void QueueBrush(BrushCommand command)
        {
            if (brushCommands.Count < 128) brushCommands.Add(command);
        }

        public void RefreshMaterialDefinitions(MaterialRegistry registry)
        {
            materialBuffer.SetData(registry.BuildGpuData());
        }

        public void RecomputeHydrostatic()
        {
            int kernel = materialSimulation.FindKernel("HydrostaticPressure");
            if (kernel < 0) return;
            DispatchColumns(materialSimulation, kernel, 0f);
        }

        public void MigrateLegacyWater()
        {
            int kernel = materialSimulation.FindKernel("MigrateLegacyWater");
            if (kernel < 0) return;
            DispatchPass(materialSimulation, kernel, 0f);
            RecomputeHydrostatic();
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
                DispatchNoSwap(materialSimulation, materialSimulation.FindKernel("MaterialMotionIntent"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("MaterialMotionCommit"), subDt);
                DispatchPass(materialSimulation, materialSimulation.FindKernel("Electrical"), subDt);
            }

            RecomputeHydrostatic();

            if (tick % Mathf.Max(1, config.slowPassInterval) == 0)
            {
                DispatchPass(geology, geology.FindKernel("Volcanism"), deltaTime * config.slowPassInterval);
                DispatchPass(hydrology, hydrology.FindKernel("ErosionAndCollapse"), deltaTime * config.slowPassInterval);
            }

            DispatchNoSwap(hydrology, hydrology.FindKernel("GroundwaterFlux"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("GroundwaterApply"), deltaTime);
            DispatchNoSwap(hydrology, hydrology.FindKernel("VaporFlux"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("VaporApply"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("GeothermalDischarge"), deltaTime);
            DispatchPass(weather, weather.FindKernel("SolarAndWind"), deltaTime);
            DispatchPass(weather, weather.FindKernel("WaterCycle"), deltaTime);
            DispatchNoSwap(hydrology, hydrology.FindKernel("RunoffFlux"), deltaTime);
            DispatchPass(hydrology, hydrology.FindKernel("RunoffApply"), deltaTime);

            tick++;
            stopwatch.Stop();
            LastTickMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private void DispatchPass(ComputeShader shader, int kernel, float deltaTime)
        {
            BindAndDispatch(shader, kernel, deltaTime);
            resources.Swap();
        }

        private void DispatchNoSwap(ComputeShader shader, int kernel, float deltaTime)
        {
            BindAndDispatch(shader, kernel, deltaTime);
        }

        private void BindAndDispatch(ComputeShader shader, int kernel, float deltaTime)
        {
            if (shader == null || kernel < 0)
            {
                UnityEngine.Debug.LogError($"GeneSys: missing compute kernel on {shader}.");
                return;
            }
            SetCommon(shader, kernel, deltaTime);
            shader.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(shader, kernel);
            Dispatch(shader, kernel);
        }

        private void DispatchColumns(ComputeShader shader, int kernel, float deltaTime)
        {
            if (shader == null || kernel < 0) return;
            SetCommon(shader, kernel, deltaTime);
            shader.SetBuffer(kernel, "_MaterialDefinitions", materialBuffer);
            BindPassTextures(shader, kernel);
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out _, out _);
            int groupsX = Mathf.CeilToInt(resources.Grid.angularResolution / (float)x);
            shader.Dispatch(kernel, groupsX, 1, 1);
        }

        private void SetCommon(ComputeShader shader, int kernel, float deltaTime)
        {
            shader.SetInts("_GridSize", resources.Grid.angularResolution, resources.Grid.radialResolution);
            shader.SetFloat("_DeltaTime", deltaTime);
            shader.SetInt("_Tick", tick);
            shader.SetFloat("_PlayableInnerRadius", resources.Grid.playableInnerRadius);
            shader.SetFloat("_AtmosphereStartRadius", resources.Grid.atmosphereStartRadius);
            shader.SetVector("_Mechanics", new Vector4(config.gravityStrength, config.thermalRate, config.electricalRate, config.pressureRate));
            shader.SetVector("_Geology", new Vector4(config.mantlePressure, config.fractureRate, config.extrusionRate, config.volcanicCooling));
            shader.SetVector("_GeologyB", new Vector4(config.hydrothermalStrength, config.ventChemicalRate, config.faultRelaxation, config.magmaDisplacementThreshold));
            shader.SetVector("_Hydrology", new Vector4(config.infiltrationRate, config.groundwaterRate, config.dissolutionRate, config.collapseRate));
            shader.SetVector("_HydrologyB", new Vector4(config.springHeadThreshold, config.springDischargeRate, config.geyserHeatThreshold, config.geyserDischargeRate));
            shader.SetVector("_HydrologyC", new Vector4(config.runoffRate, config.pondingRate, config.vaporTransportRate, config.geyserCooldownSeconds));
            shader.SetVector("_Erosion", new Vector4(config.erosionRate, config.depositionRate, config.baseSoilCohesion, config.conservationTolerance));
            shader.SetVector("_WeatherA", new Vector4(config.solarIntensity, config.spaceTemperature, config.radiativeCooling, config.windStrength));
            shader.SetVector("_WeatherB", new Vector4(config.windDamping, config.evaporationRate, config.condensationRate, config.precipitationRate));
            shader.SetVector("_WeatherC", new Vector4(config.vaporPressureScale, SolarAngle01, config.phaseHysteresis, config.magmaViscosity));
            shader.SetVector("_Phase", new Vector4(config.meltPressureSlope, config.boilPressureSlope, 0f, config.atmosphericPressure));
            shader.SetVector("_Pressure", new Vector4(config.overpressureDiffusion, config.pressureRate, config.hydrostaticGravity, config.maxOverpressure));
        }

        private void BindPassTextures(ComputeShader shader, int kernel)
        {
            TrySetTexture(shader, kernel, "_MaterialRead", resources.MaterialRead);
            TrySetTexture(shader, kernel, "_MaterialWrite", resources.MaterialWrite);
            TrySetTexture(shader, kernel, "_StateRead", resources.StateRead);
            TrySetTexture(shader, kernel, "_StateWrite", resources.StateWrite);
            TrySetTexture(shader, kernel, "_FlowRead", resources.FlowRead);
            TrySetTexture(shader, kernel, "_FlowWrite", resources.FlowWrite);
            TrySetTexture(shader, kernel, "_AuxRead", resources.AuxRead);
            TrySetTexture(shader, kernel, "_AuxWrite", resources.AuxWrite);
            TrySetTexture(shader, kernel, "_WaterRead", resources.WaterRead);
            TrySetTexture(shader, kernel, "_WaterWrite", resources.WaterWrite);
            TrySetTexture(shader, kernel, "_Hydrostatic", resources.Hydrostatic);
            TrySetTexture(shader, kernel, "_MotionIntent", resources.MotionIntent);
            TrySetTexture(shader, kernel, "_WaterFlux", resources.WaterFlux);
        }

        private static void TrySetTexture(ComputeShader shader, int kernel, string name, RenderTexture texture)
        {
            if (texture == null) return;
            shader.SetTexture(kernel, name, texture);
        }

        private void BindWorldgenOutputs(ComputeShader shader, int kernel)
        {
            shader.SetTexture(kernel, "_MaterialWrite", resources.MaterialRead);
            shader.SetTexture(kernel, "_StateWrite", resources.StateRead);
            shader.SetTexture(kernel, "_FlowWrite", resources.FlowRead);
            shader.SetTexture(kernel, "_AuxWrite", resources.AuxRead);
            shader.SetTexture(kernel, "_WaterWrite", resources.WaterRead);
        }

        private void Dispatch(ComputeShader shader, int kernel)
        {
            if (kernel < 0) return;
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out _);
            int groupsX = Mathf.CeilToInt(resources.Grid.angularResolution / (float)x);
            int groupsY = Mathf.Max(1, Mathf.CeilToInt(resources.Grid.radialResolution / (float)y));
            shader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        public void Dispose()
        {
            materialBuffer?.Dispose();
            brushBuffer?.Dispose();
        }
    }
}
