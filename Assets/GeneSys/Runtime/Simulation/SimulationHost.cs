using GeneSys.Configuration;
using GeneSys.Integration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using GeneSys.Tools;
using GeneSys.UI;
using GeneSys.Validation;
using UnityEngine;

namespace GeneSys.Simulation
{
    public sealed class SimulationHost : MonoBehaviour, IWorldSimulationBridge
    {
        [Header("Data")]
        [SerializeField] private SimulationConfig config;
        [SerializeField] private MaterialRegistry materialRegistry;
        [Header("Compute")]
        [SerializeField] private ComputeShader worldGeneration;
        [SerializeField] private ComputeShader materialSimulation;
        [SerializeField] private ComputeShader geology;
        [SerializeField] private ComputeShader hydrology;
        [SerializeField] private ComputeShader weather;
        [Header("Scene")]
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private TerrariumVisualController visuals;
        [SerializeField] private SimulationUIController ui;
        [SerializeField] private SimulationTools tools;
        [SerializeField] private SimulationValidator validator;

        private GpuPassScheduler scheduler;
        private long lastPerformanceTick;
        private double dispatchMilliseconds;
        private int dispatchSamples;

        public SimulationConfig Config => config;
        public MaterialRegistry MaterialRegistry => materialRegistry;
        public SimulationResources Resources { get; private set; }
        public SimulationClock Clock { get; } = new();
        public bool IsReady => Resources != null && Resources.IsCreated && scheduler != null;
        public int TickIndex => scheduler?.TickIndex ?? 0;
        public float SolarAngle01 => scheduler?.SolarAngle01 ?? 0f;
        public double LastTickMilliseconds => scheduler?.LastTickMilliseconds ?? 0d;
        public PolarGridDefinition Grid => Resources?.Grid ?? config.grid;
        public RenderTexture MaterialField => Resources?.MaterialRead;
        public RenderTexture EnvironmentalField => Resources?.StateRead;
        public RenderTexture FlowField => Resources?.FlowRead;
        public RenderTexture ChemicalAndGroundwaterField => Resources?.AuxRead;

        private void Start()
        {
            Initialize();
        }

        public void RestoreDefaultSettings()
        {
            config.RestoreDefaults();
            Initialize(false);
        }

        public void Initialize() => Initialize(true);

        private void Initialize(bool bindUi)
        {
            Shutdown();
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("GeneSys requires compute shader support.", this);
                enabled = false;
                return;
            }
            if (config == null || materialRegistry == null || worldGeneration == null || materialSimulation == null || geology == null || hydrology == null || weather == null)
            {
                Debug.LogError("GeneSys bootstrap references are incomplete. Run Tools/GeneSys/Rebuild Phase 1 Scene.", this);
                enabled = false;
                return;
            }
            config.grid.Validate();
            Resources = new SimulationResources(config.grid);
            scheduler = new GpuPassScheduler(config, Resources, materialRegistry, worldGeneration, materialSimulation, geology, hydrology, weather);
            scheduler.GenerateWorld();
            Clock.Reset();
            lastPerformanceTick = 0;
            dispatchMilliseconds = 0d;
            dispatchSamples = 0;
            Clock.SetSpeed(config.simulationSpeed);
            if (display != null) display.Initialize(Resources, materialRegistry, config.grid, config, this);
            if (visuals != null) visuals.Initialize(this, display);
            if (tools != null) { tools.Radius = config.brushRadius; tools.Strength = config.brushStrength; }
            if (bindUi && ui != null) ui.Initialize(this, display, tools);
            if (validator != null) validator.Initialize(this);
            Debug.Log($"GENESYS_INITIALIZED preset={config.preset} grid={Grid.angularResolution}x{Grid.radialResolution} targetTicks={config.ticksPerSecond:F1}", this);
        }

        private void Update()
        {
            if (!IsReady) return;
            int advanced = Clock.Advance(Time.unscaledDeltaTime, config.ticksPerSecond, scheduler.Step);
            if (advanced <= 0) return;
            dispatchMilliseconds += scheduler.LastTickMilliseconds;
            dispatchSamples++;
            if (Clock.TickCount - lastPerformanceTick >= 100)
            {
                double average = dispatchMilliseconds / Mathf.Max(1, dispatchSamples);
                Debug.Log($"GENESYS_PERF preset={config.preset} grid={Grid.angularResolution}x{Grid.radialResolution} avgCpuDispatchMs={average:F3} targetTicks={config.ticksPerSecond:F1}", this);
                lastPerformanceTick = Clock.TickCount;
                dispatchMilliseconds = 0d;
                dispatchSamples = 0;
            }
        }

        public void Regenerate()
        {
            if (!IsReady) return;
            scheduler.GenerateWorld();
            Clock.Reset();
            validator?.ResetBaseline();
        }

        public void RestoreSimulationTick(long tick)
        {
            Clock.SetTickCount(tick);
            scheduler?.SetTickIndex((int)Mathf.Min(int.MaxValue, tick));
            validator?.ResetBaseline();
        }

        public void ApplyPreset(SimulationPreset preset)
        {
            config.ApplyPreset(preset);
            Initialize();
        }

        public void QueueBrush(GpuPassScheduler.BrushCommand command)
        {
            if (IsReady) scheduler.QueueBrush(command);
        }

        public void RefreshMaterialDefinitions()
        {
            if (!IsReady) return;
            scheduler.RefreshMaterialDefinitions(materialRegistry);
            if (display != null) display.Initialize(Resources, materialRegistry, config.grid, config, this);
            if (visuals != null) visuals.Initialize(this, display);
        }

        public SurfaceFrame GetSurfaceFrame(Vector2Int cell)
        {
            Vector2 position = PolarCoordinateTransforms.CellToSimulationPosition(Grid, cell);
            float theta = Mathf.Atan2(position.y, position.x);
            PolarCoordinateTransforms.GetSurfaceFrame(theta, out Vector2 tangent, out Vector2 normal);
            return new SurfaceFrame(position, tangent, normal, Grid.Radius01(cell.y) - Grid.playableInnerRadius);
        }

        public void QueueFieldDeposit(Vector2Int cell, int radius, int channel, float amount)
        {
            QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = cell,
                radius = Mathf.Max(1, radius),
                materialId = MaterialIds.Void,
                values = new Vector4(Mathf.Clamp(channel, 1, 6), amount, 0f, 0f)
            });
        }

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            scheduler?.Dispose();
            scheduler = null;
            Resources?.Dispose();
            Resources = null;
        }
    }
}
