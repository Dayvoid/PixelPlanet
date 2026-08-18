using System;
using System.Collections.Generic;
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
        public double IceMass;
        public double TotalTrackedWaterMass;
        public double InnerWaterMass;
        public int MantleCells;
        public int MagmaCells;
        public int RockCells;
        public double MeanMantleTemperature;
        public double MeanInnerTotalPressure;
        public double MeanOuterTotalPressure;
        public double MaxDynamicOverpressure;
        public double MeanFault;
        public bool PressureIncreasesInward;
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
                        AsyncGPUReadback.Request(host.Resources.WaterRead, 0, waterRequest =>
                        {
                            if (waterRequest.hasError) { completed?.Invoke(default); return; }
                            Vector4[] water = waterRequest.GetData<Vector4>().ToArray();
                            AsyncGPUReadback.Request(host.Resources.Hydrostatic, 0, hydroRequest =>
                            {
                                if (hydroRequest.hasError) { completed?.Invoke(default); return; }
                                Vector4[] hydro = hydroRequest.GetData<Vector4>().ToArray();
                                var hydroScalar = new float[hydro.Length];
                                for (int i = 0; i < hydro.Length; i++) hydroScalar[i] = hydro[i].x;
                                completed?.Invoke(ComputeMetrics(grid, materials, states, aux, water, hydroScalar));
                            });
                        });
                    });
                });
            });
        }

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux) =>
            ComputeMetrics(grid, materials, states, aux, null, null);

        public static WorldWaterMetrics ComputeMetrics(PolarGridDefinition grid, uint[] materials, Vector4[] states, Vector4[] aux, Vector4[] water, float[] hydrostatic)
        {
            int width = grid.angularResolution;
            int height = grid.radialResolution;
            int cellCount = Math.Min(materials.Length, Math.Min(states.Length, aux.Length));
            var metrics = new WorldWaterMetrics();
            bool[] oceanAngles = new bool[width];
            bool[] oceanMask = new bool[width * height];
            double[] radialPressure = new double[height];
            int[] radialCount = new int[height];
            double mantleTemp = 0d;
            int mantleTempCount = 0;
            int playableInner = Mathf.Clamp(Mathf.FloorToInt(grid.playableInnerRadius * height), 1, height - 2);

            for (int x = 0; x < width; x++)
            {
                for (int y = height - 1; y >= 0; y--)
                {
                    int index = y * width + x;
                    if (index >= cellCount) break;
                    uint material = materials[index];
                    Vector4 state = states[index];
                    if (material == MaterialIds.Air || material == MaterialIds.Void) continue;
                    float surfaceMass = water != null && index < water.Length ? water[index].x : state.z;
                    if (material == MaterialIds.Water || material == MaterialIds.Ice || surfaceMass > 0.25f)
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
                    Vector4 waterValue = water != null && index < water.Length
                        ? water[index]
                        : new Vector4(state.z, auxValue.y, material == MaterialIds.Ice ? state.z : 0f, auxValue.x);
                    float hydro = hydrostatic != null && index < hydrostatic.Length ? hydrostatic[index] : 0f;
                    double liquid = Math.Max(0d, waterValue.x);
                    double ground = Math.Max(0d, waterValue.y);
                    double ice = Math.Max(0d, waterValue.z);
                    double vapor = Math.Max(0d, waterValue.w);

                    metrics.SurfaceWaterMass += liquid;
                    metrics.GroundwaterMass += ground;
                    metrics.IceMass += ice;
                    metrics.VaporMass += vapor;
                    if (y < playableInner)
                        metrics.InnerWaterMass += liquid + ground + ice + vapor;

                    if (material == MaterialIds.Mantle)
                    {
                        metrics.MantleCells++;
                        mantleTemp += state.x;
                        mantleTempCount++;
                    }
                    else if (material == MaterialIds.Magma) metrics.MagmaCells++;
                    else if (material == MaterialIds.Rock) metrics.RockCells++;

                    metrics.MaxDynamicOverpressure = Math.Max(metrics.MaxDynamicOverpressure, Math.Max(0d, state.y));
                    metrics.MeanFault += Math.Max(0d, auxValue.w);
                    double totalP = Math.Max(0d, hydro) + Math.Max(0d, state.y);
                    radialPressure[y] += totalP;
                    radialCount[y]++;
                    if (radius < grid.playableInnerRadius + 0.15f) metrics.MeanInnerTotalPressure += totalP;
                    if (radius > grid.atmosphereStartRadius * 0.85f) metrics.MeanOuterTotalPressure += totalP;

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
            metrics.TotalTrackedWaterMass = metrics.SurfaceWaterMass + metrics.GroundwaterMass + metrics.VaporMass + metrics.IceMass;
            metrics.MeanMantleTemperature = mantleTempCount > 0 ? mantleTemp / mantleTempCount : 0d;
            int cells = Math.Max(1, cellCount);
            metrics.MeanFault /= cells;
            metrics.MeanInnerTotalPressure /= Math.Max(1, width * Math.Max(1, playableInner));
            metrics.MeanOuterTotalPressure /= Math.Max(1, width * Math.Max(1, height / 8));
            metrics.PressureIncreasesInward = RadialPressureIncreasesInward(radialPressure, radialCount);
            return metrics;
        }

        public static bool RadialPressureIncreasesInward(double[] radialPressure, int[] radialCount)
        {
            int height = radialPressure.Length;
            double outer = 0d;
            double inner = 0d;
            int outerCount = 0;
            int innerCount = 0;
            int outerStart = Math.Max(0, height * 3 / 4);
            int innerEnd = Math.Max(1, height / 4);
            for (int y = 0; y < height; y++)
            {
                if (radialCount[y] <= 0) continue;
                double mean = radialPressure[y] / radialCount[y];
                if (y >= outerStart)
                {
                    outer += mean;
                    outerCount++;
                }
                if (y < innerEnd)
                {
                    inner += mean;
                    innerCount++;
                }
            }
            if (innerCount == 0 || outerCount == 0) return false;
            return inner / innerCount > outer / outerCount + 0.02d;
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
