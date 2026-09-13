using UnityEngine;

namespace GeneSys.Simulation.Topology
{
    public readonly struct SurfaceFrame
    {
        public readonly Vector2 position;
        public readonly Vector2 tangent;
        public readonly Vector2 normal;
        public readonly float heightAboveCore;

        public Vector2 Position => position;
        public Vector2 Tangent => tangent;
        public Vector2 Normal => normal;
        public float HeightAboveCore => heightAboveCore;

        public SurfaceFrame(Vector2 position, Vector2 tangent, Vector2 normal, float heightAboveCore)
        {
            this.position = position;
            this.tangent = tangent;
            this.normal = normal;
            this.heightAboveCore = heightAboveCore;
        }
    }
}
