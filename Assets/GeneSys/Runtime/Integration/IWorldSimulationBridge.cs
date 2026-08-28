using GeneSys.Simulation.Topology;
using UnityEngine;

namespace GeneSys.Integration
{
    public readonly struct SurfaceFrame
    {
        public readonly Vector2 position;
        public readonly Vector2 tangent;
        public readonly Vector2 normal;
        public readonly float altitude;

        public SurfaceFrame(Vector2 position, Vector2 tangent, Vector2 normal, float altitude)
        {
            this.position = position;
            this.tangent = tangent;
            this.normal = normal;
            this.altitude = altitude;
        }
    }

    public interface IWorldSimulationBridge
    {
        PolarGridDefinition Grid { get; }
        RenderTexture MaterialField { get; }
        RenderTexture EnvironmentalField { get; }
        RenderTexture FlowField { get; }
        RenderTexture ChemicalAndGroundwaterField { get; }
        RenderTexture EcologyField { get; }
        RenderTexture CombustionField { get; }
        RenderTexture StormField { get; }
        RenderTexture LifeField { get; }
        RenderTexture GenomeField { get; }
        RenderTexture LightField { get; }
        SurfaceFrame GetSurfaceFrame(Vector2Int cell);
        /// <summary>
        /// Deposit into a field channel: 1 heat, 2 moisture/cloud, 3 pressure, 4 nutrients, 5 groundwater, 6 vapor, 7 spores, 8 charge.
        /// </summary>
        void QueueFieldDeposit(Vector2Int cell, int radius, int channel, float amount);
        void QueueSporeSeed(Vector2Int cell, int radius, float sporeLoad, float mycoValue, uint traitFlags);
        void QueueFloraSeed(Vector2Int cell, int radius, float sporeLoad);
        void QueueGrassSeed(Vector2Int cell, int radius, float biomass);
        void QueueIgnition(Vector2Int cell, int radius, float intensity);
    }
}
