using UnityEngine;

namespace GeneSys.Simulation.Topology
{
    /// <summary>
    /// CPU mirror of PoleAngle01 / NearestPoleStep in SimulationStructs.hlsl.
    /// Ice caps sit at Hash01(seed * 9829) and that angle plus one half-turn.
    /// </summary>
    public static class PolarPoleGeometry
    {
        public static float PoleAngle01(int seed) => Hash01(unchecked((uint)seed * 9829u));

        public static int NearestPoleStep(int theta, int width, int seed)
        {
            int w = Mathf.Max(1, width);
            float angular = (theta + 0.5f) / w;
            float a = Frac(PoleAngle01(seed));
            float b = Frac(a + 0.5f);
            float pole = AngularDistance01(angular, a) <= AngularDistance01(angular, b) ? a : b;
            float delta = pole - angular;
            if (delta > 0.5f) delta -= 1f;
            else if (delta < -0.5f) delta += 1f;
            float cells = delta * w;
            return Mathf.Abs(cells) < 0.5f ? 0 : (cells > 0f ? 1 : -1);
        }

        public static float AngularDistance01(float a, float b)
        {
            float d = Mathf.Abs(a - b);
            return Mathf.Min(d, 1f - d);
        }

        /// <summary>
        /// Global solar energy scale for an elliptical-orbit approximation: 1 at the
        /// equators (midway between ice-cap poles) and <paramref name="polarMin"/> when
        /// the sun is over either cap. Two cosine minima per day.
        /// </summary>
        public static float SolarPolarOutput(float solarAngle01, float poleAngle01, float polarMin)
        {
            float rel = Frac(solarAngle01 - poleAngle01);
            float t = 0.5f * (1f - Mathf.Cos(rel * 4f * Mathf.PI));
            return Mathf.Lerp(Mathf.Clamp01(polarMin), 1f, t);
        }

        private static float Frac(float value) => value - Mathf.Floor(value);

        private static float Hash01(uint value) => (Hash(value) & 0x00ffffff) / 16777215f;

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }
    }
}
