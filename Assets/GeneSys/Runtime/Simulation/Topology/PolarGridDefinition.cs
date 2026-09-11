using System;
using UnityEngine;

namespace GeneSys.Simulation.Topology
{
    [Serializable]
    public struct PolarGridDefinition
    {
        [Min(32)] public int angularResolution;
        [Min(16)] public int radialResolution;
        [Range(0.01f, 0.95f)] public float playableInnerRadius;
        [Range(0.01f, 1f)] public float atmosphereStartRadius;
        [Range(0f, 0.95f)] public float visualCoreRadius;
        [Range(0.05f, 1f)] public float visualCoreSquash;

        public int CellCount => Mathf.Max(1, angularResolution) * Mathf.Max(1, radialResolution);
        public Vector2Int Size => new(Mathf.Max(1, angularResolution), Mathf.Max(1, radialResolution));

        public static PolarGridDefinition Validation => new()
        {
            angularResolution = 256,
            radialResolution = 128,
            playableInnerRadius = 0.22f,
            atmosphereStartRadius = 0.9f,
            visualCoreRadius = 0.28f,
            visualCoreSquash = 0.45f
        };

        public static PolarGridDefinition Standard => new()
        {
            angularResolution = 1024,
            radialResolution = 512,
            playableInnerRadius = 0.22f,
            atmosphereStartRadius = 0.9f,
            visualCoreRadius = 0.28f,
            visualCoreSquash = 0.45f
        };

        public static PolarGridDefinition Stress => new()
        {
            angularResolution = 2048,
            radialResolution = 1024,
            playableInnerRadius = 0.22f,
            atmosphereStartRadius = 0.9f,
            visualCoreRadius = 0.28f,
            visualCoreSquash = 0.45f
        };

        public readonly int WrapTheta(int theta)
        {
            int width = Mathf.Max(1, angularResolution);
            int wrapped = theta % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        public readonly bool Contains(int theta, int radius) =>
            theta >= 0 && theta < angularResolution && radius >= 0 && radius < radialResolution;

        public readonly float Radius01(int radialIndex) =>
            (Mathf.Clamp(radialIndex, 0, radialResolution - 1) + 0.5f) / Mathf.Max(1, radialResolution);

        public readonly float CellAreaWeight(int radialIndex) =>
            Mathf.Max(0.5f / Mathf.Max(1, radialResolution), Radius01(radialIndex));

        public readonly float TangentialEdgeWeight(int radialIndex) =>
            1f / Mathf.Max(CellAreaWeight(radialIndex), 1e-4f);

        public void Validate()
        {
            angularResolution = Mathf.Max(32, angularResolution);
            if ((angularResolution & 1) != 0)
                angularResolution += 1;
            radialResolution = Mathf.Max(16, radialResolution);
            playableInnerRadius = Mathf.Clamp(playableInnerRadius, 0.01f, 0.95f);
            atmosphereStartRadius = Mathf.Clamp(atmosphereStartRadius, playableInnerRadius + 0.01f, 1f);
            visualCoreRadius = Mathf.Clamp(visualCoreRadius, 0f, playableInnerRadius);
            visualCoreSquash = Mathf.Clamp(visualCoreSquash, 0.05f, 1f);
        }
    }
}
