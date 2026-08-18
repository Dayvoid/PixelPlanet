using System;
using System.Collections.Generic;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeneSys.Validation
{
    public struct WorldWaterMetrics
    {
        public float OceanCoverage;
        public int BasinCount;
        public double SurfaceWaterMass;
        public double GroundwaterMass;
        public double VaporMass;
        public double TotalTrackedWaterMass;
    }

    public static class SimulationMetrics
    {
        public static void MeasureAsync(SimulationHost host, Action<WorldWaterMetrics> completed)
        {
            if (host == null || !host.IsReady)
            {
                completed?.Invoke(default);
                return;
            }

            PolarGridDefinition grid = host.Grid;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { completed?.Invoke(default); return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { completed?.Invoke(default); return; }
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                    {
                        if (auxRequest.hasError) { completed?.Invoke(default); return; }
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        completed?.Invoke(ComputeMetrics(grid, materials, states, aux));
                    });
                });
            });
        }

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux)
        {
            int width = grid.angularResolution;
            int height = grid.radialResolution;
            int cellCount = Math.Min(materials.Length, Math.Min(states.Length, aux.Length));
            var metrics = new WorldWaterMetrics();
            bool[] oceanAngles = new bool[width];
            bool[] oceanMask = new bool[width * height];

            for (int x = 0; x < width; x++)
            {
                for (int y = height - 1; y >= 0; y--)
                {
                    int index = y * width + x;
                    if (index >= cellCount) break;
                    uint material = materials[index];
                    Vector4 state = states[index];
                    if (material == MaterialIds.Air || material == MaterialIds.Void) continue;
                    if (material == MaterialIds.Water || material == MaterialIds.Ice || state.z > 0.25f)
                        oceanAngles[x] = true;
                    break;
                }
            }

            for (int y = 0; y < height; y++)
            {
                float radius = grid.Radius01(y);
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (index >= cellCount) break;
                    uint material = materials[index];
                    Vector4 state = states[index];
                    Vector4 auxValue = aux[index];

                    metrics.SurfaceWaterMass += Math.Max(0d, state.z);
                    metrics.GroundwaterMass += Math.Max(0d, auxValue.y);
                    metrics.VaporMass += Math.Max(0d, auxValue.x);

                    if (material != MaterialIds.Water && material != MaterialIds.Ice) continue;
                    if (radius < grid.atmosphereStartRadius * 0.95f)
                        oceanMask[index] = true;
                }
            }

            int oceanAngleCount = 0;
            for (int x = 0; x < width; x++)
                if (oceanAngles[x]) oceanAngleCount++;
            metrics.OceanCoverage = oceanAngleCount / (float)Math.Max(1, width);
            metrics.BasinCount = CountOceanAngleBasins(oceanAngles);
            metrics.TotalTrackedWaterMass = metrics.SurfaceWaterMass + metrics.GroundwaterMass + metrics.VaporMass;
            return metrics;
        }

        public static int CountOceanAngleBasins(bool[] oceanAngles)
        {
            int width = oceanAngles.Length;
            int basins = 0;
            for (int x = 0; x < width; x++)
            {
                if (!oceanAngles[x]) continue;
                int prev = (x - 1 + width) % width;
                if (oceanAngles[prev]) continue;
                basins++;
            }
            return basins;
        }

        public static int CountWrapAwareBasins(bool[] oceanMask, int width, int height)
        {
            var visited = new bool[oceanMask.Length];
            int basins = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (!oceanMask[index] || visited[index]) continue;
                    FloodFillBasin(oceanMask, visited, width, height, x, y);
                    basins++;
                }
            }
            return basins;
        }

        private static void FloodFillBasin(bool[] oceanMask, bool[] visited, int width, int height, int startX, int startY)
        {
            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            while (stack.Count > 0)
            {
                (int x, int y) = stack.Pop();
                int index = y * width + x;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                if (!oceanMask[index] || visited[index]) continue;
                visited[index] = true;
                stack.Push((x - 1, y));
                stack.Push((x + 1, y));
                stack.Push((x, y - 1));
                stack.Push((x, y + 1));
                if (x == 0) stack.Push((width - 1, y));
                if (x == width - 1) stack.Push((0, y));
            }
        }
    }
}
