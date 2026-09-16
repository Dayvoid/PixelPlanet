using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using UnityEngine;

namespace GeneSys.Simulation.Scenarios
{
    public sealed class WorldScenarioDirector
    {
        public const long CometApproachStartTick = 500;
        public const int CometApproachSteps = 5;
        public const long CometImpactTick = CometApproachStartTick + CometApproachSteps;

        private ScenarioTimeline timeline = new();

        public void Bind(WorldScenario scenario)
        {
            timeline = CreateTimeline(scenario);
        }

        public void Reset() => timeline.Reset();

        public void Sync(long tick) => timeline.Sync(tick);

        public void Advance(SimulationHost host, long tick) => timeline.Advance(host, tick);

        public static ScenarioTimeline CreateTimeline(WorldScenario scenario)
        {
            var timeline = new ScenarioTimeline();
            if (scenario == WorldScenario.CometStruckMoon)
                RegisterCometStruckMoon(timeline);
            return timeline;
        }

        public static Vector2Int PredictImpactCell(SimulationConfig config, PolarGridDefinition grid)
        {
            ResolveCometPath(config, grid, out int theta, out _, out int surfaceY, out _);
            return new Vector2Int(theta, surfaceY);
        }

        public static int CometBrushRadius(PolarGridDefinition grid) =>
            Mathf.Clamp(Mathf.Max(1, grid.angularResolution) / 16, 8, 64);

        private static void RegisterCometStruckMoon(ScenarioTimeline timeline)
        {
            for (int i = 0; i < CometApproachSteps; i++)
            {
                int step = i;
                timeline.Add(new TickCue(CometApproachStartTick + i, host => PaintCometApproach(host, step)));
            }

            timeline.Add(new TickCue(CometImpactTick, PaintCometImpact));
        }

        private static void PaintCometApproach(SimulationHost host, int step)
        {
            if (host == null || !host.IsReady) return;
            ResolveCometPath(host.Config, host.Grid, out int theta, out int atmosphereY, out int surfaceY, out int radius);
            float t = CometApproachSteps <= 1 ? 1f : step / (float)(CometApproachSteps - 1);
            int y = Mathf.RoundToInt(Mathf.Lerp(atmosphereY, surfaceY, t));
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(theta, y),
                radius = radius,
                materialId = MaterialIds.Ice,
                values = new Vector4(0f, 1f, 0f, 0f)
            });
        }

        private static void PaintCometImpact(SimulationHost host)
        {
            if (host == null || !host.IsReady) return;
            ResolveCometPath(host.Config, host.Grid, out int theta, out _, out int surfaceY, out int radius);
            var cell = new Vector2Int(theta, surfaceY);
            int craterRadius = Mathf.Max(2, radius / 3);
            host.QueueFieldDeposit(cell, radius, 1, 180f);
            host.QueueFieldDeposit(cell, radius, 3, 4f);
            host.QueueMaterialPaint(cell, craterRadius, MaterialIds.Air);
            InjectImpactGeodynamics(host, theta, host.Grid.Radius01(surfaceY));
        }

        private static void ResolveCometPath(
            SimulationConfig config,
            PolarGridDefinition grid,
            out int theta,
            out int atmosphereY,
            out int surfaceY,
            out int radius)
        {
            int width = Mathf.Max(1, grid.angularResolution);
            int height = Mathf.Max(1, grid.radialResolution);
            uint seed = config != null ? (uint)config.seed : 0u;
            theta = (int)(Hash01(seed * 4177u + 1301u) * width);
            theta = grid.WrapTheta(theta);
            atmosphereY = RadialIndex(grid.atmosphereStartRadius, height);
            float terrain = config != null
                ? config.coreRatio + config.mantleRatio + config.crustRatio + config.soilRatio
                : 0.75f;
            surfaceY = RadialIndex(terrain, height);
            if (surfaceY >= atmosphereY)
                surfaceY = Mathf.Max(0, atmosphereY - 1);
            radius = CometBrushRadius(grid);
        }

        private static void InjectImpactGeodynamics(SimulationHost host, int theta, float radius01)
        {
            if (host.Resources?.GeodynamicsEvents == null) return;
            SimulationConfig config = host.Config;
            PolarGridDefinition grid = host.Grid;
            int angularBins = GeodynamicsGrid.ClampAngularBins(config.geodynamicsAngularBins);
            int radialBins = GeodynamicsGrid.ClampRadialBins(config.geodynamicsRadialBins);
            int angularBin = GeodynamicsGrid.AngularBinOf(theta, grid.angularResolution, angularBins);
            int radialBin = GeodynamicsGrid.RadialBinOf(radius01, grid.atmosphereStartRadius, radialBins);
            var events = new Vector4[GeodynamicsGrid.EventBufferCount()];
            host.Resources.GeodynamicsEvents.GetData(events);
            int volcanicIndex = GeodynamicsGrid.EventIndex(angularBin, radialBin, angularBins, radialBins);
            int quakeBin = GeodynamicsGrid.WrapBin(angularBin + 1, angularBins);
            int quakeIndex = GeodynamicsGrid.EventIndex(quakeBin, radialBin, angularBins, radialBins);
            events[volcanicIndex] = new Vector4(GeodynamicsGrid.EventTypeVolcanic, 1f, 0.35f, 0.35f);
            events[quakeIndex] = new Vector4(GeodynamicsGrid.EventTypeEarthquake, 1f, 0.28f, 0.28f);
            host.Resources.GeodynamicsEvents.SetData(events);
        }

        private static int RadialIndex(float radius01, int height)
        {
            height = Mathf.Max(1, height);
            return Mathf.Clamp(Mathf.RoundToInt(radius01 * (height - 1)), 0, height - 1);
        }

        private static float Hash01(uint seed)
        {
            seed ^= 2747636419u;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            seed ^= seed >> 16;
            seed *= 2654435769u;
            return (seed & 0x00FFFFFFu) / 16777215f;
        }
    }
}
