using UnityEngine;

namespace GeneSys.Simulation.Topology
{
    public static class PolarCoordinateTransforms
    {
        public const float Tau = Mathf.PI * 2f;

        public static Vector2 CellToSimulationPosition(PolarGridDefinition grid, Vector2Int cell)
        {
            float theta = ((grid.WrapTheta(cell.x) + 0.5f) / grid.angularResolution) * Tau;
            float radius = grid.Radius01(cell.y);
            return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * radius;
        }

        public static Vector2Int SimulationPositionToCell(PolarGridDefinition grid, Vector2 position)
        {
            float theta = Mathf.Atan2(position.y, position.x);
            if (theta < 0f) theta += Tau;
            int angular = Mathf.FloorToInt(theta / Tau * grid.angularResolution);
            int radial = Mathf.FloorToInt(position.magnitude * grid.radialResolution);
            return new Vector2Int(grid.WrapTheta(angular), Mathf.Clamp(radial, 0, grid.radialResolution - 1));
        }

        public static float SimulationRadiusToDisplayRadius(PolarGridDefinition grid, float simulationRadius)
        {
            float core = Mathf.Max(1e-5f, grid.visualCoreRadius);
            if (simulationRadius >= core)
            {
                float displayedCore = core * grid.visualCoreSquash;
                return Mathf.Lerp(displayedCore, 1f, (simulationRadius - core) / (1f - core));
            }
            return simulationRadius * grid.visualCoreSquash;
        }

        public static float DisplayRadiusToSimulationRadius(PolarGridDefinition grid, float displayRadius)
        {
            float core = Mathf.Max(1e-5f, grid.visualCoreRadius);
            float displayedCore = core * grid.visualCoreSquash;
            if (displayRadius >= displayedCore)
                return Mathf.Lerp(core, 1f, (displayRadius - displayedCore) / Mathf.Max(1e-5f, 1f - displayedCore));
            return displayRadius / Mathf.Max(0.05f, grid.visualCoreSquash);
        }

        public static void GetSurfaceFrame(float thetaRadians, out Vector2 tangent, out Vector2 normal)
        {
            normal = new Vector2(Mathf.Cos(thetaRadians), Mathf.Sin(thetaRadians));
            tangent = new Vector2(-normal.y, normal.x);
        }

        public static bool TryDisplayUvToCell(PolarGridDefinition grid, Vector2 uv, out Vector2Int cell)
        {
            Vector2 p = uv * 2f - Vector2.one;
            float displayRadius = p.magnitude;
            if (displayRadius > 1f)
            {
                cell = default;
                return false;
            }

            float simRadius = DisplayRadiusToSimulationRadius(grid, displayRadius);
            cell = SimulationPositionToCell(grid, p.normalized * simRadius);
            return simRadius >= grid.playableInnerRadius;
        }
    }
}
