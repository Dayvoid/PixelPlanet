using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class StressVaporReproTests
    {
        private static IEnumerator WaitForHost()
        {
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            for (int i = 0; i < 180 && host == null; i++)
            {
                yield return null;
                host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            }
            Assert.That(host, Is.Not.Null);
            for (int i = 0; i < 60 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Clock.SetRunning(false);
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static IEnumerator ReadMaterialsStateAux(SimulationHost host, Action<uint[], Vector4[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.StateRead, 0, stateRequest =>
                {
                    if (stateRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] states = stateRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                    {
                        if (auxRequest.hasError) { failed = true; done = true; return; }
                        consume(materials, states, auxRequest.GetData<Vector4>().ToArray());
                        done = true;
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        [UnityTest]
        public IEnumerator StressPresetVaporBehaviorUnderDefaultSettings()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.RestoreDefaultSettings();
            host.ApplyPreset(SimulationPreset.Stress);
            for (int i = 0; i < 5; i++) yield return null;

            double surfaceWater0 = 0d;
            double vaporAir0 = 0d;
            double vaporNonAir0 = 0d;
            double groundwater0 = 0d;
            int airCellCount0 = 0;
            int waterCellCount0 = 0;

            yield return ReadMaterialsStateAux(host, (mats, states, aux) =>
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    surfaceWater0 += Math.Max(0d, states[i].z);
                    groundwater0 += Math.Max(0d, aux[i].y);
                    if (mats[i] == MaterialIds.Air)
                    {
                        vaporAir0 += Math.Max(0d, aux[i].x);
                        airCellCount0++;
                    }
                    else
                    {
                        vaporNonAir0 += Math.Max(0d, aux[i].x);
                        if (mats[i] == MaterialIds.Water) waterCellCount0++;
                    }
                }
            });

            Debug.Log($"STRESS_REPRO_BEFORE: Grid={host.Grid.angularResolution}x{host.Grid.radialResolution} " +
                $"SurfaceWater={surfaceWater0:F2} GroundWater={groundwater0:F2} " +
                $"VaporAir={vaporAir0:F2} VaporNonAir={vaporNonAir0:F2} " +
                $"TotalWater={surfaceWater0 + groundwater0 + vaporAir0 + vaporNonAir0:F2} " +
                $"AirCells={airCellCount0} WaterCells={waterCellCount0}");

            int[] checkTicks = new int[] { 40, 45, 50, 55, 60, 65, 70, 75, 80 };
            int currentTick = 0;

            for (int t = 0; t < checkTicks.Length; t++)
            {
                int targetTick = checkTicks[t];
                int stepsNeeded = targetTick - currentTick;
                yield return Step(host, stepsNeeded);
                currentTick = targetTick;

                double surfaceWater = 0d;
                double vaporAir = 0d;
                double vaporNonAir = 0d;
                double groundwater = 0d;
                int airCells = 0;
                int waterCells = 0;
                double vaporWater = 0d;

                double vaporSoil = 0d;
                double vaporRock = 0d;
                double vaporOther = 0d;
                float minWaterTemp = 9999f;
                float maxWaterTemp = -9999f;
                double sumWaterTemp = 0d;
                int boilingWaterCells = 0;
                var otherMats = new System.Collections.Generic.Dictionary<uint, double>();

                yield return ReadMaterialsStateAux(host, (mats, states, aux) =>
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        surfaceWater += Math.Max(0d, states[i].z);
                        groundwater += Math.Max(0d, aux[i].y);
                        if (mats[i] == MaterialIds.Air)
                        {
                            vaporAir += Math.Max(0d, aux[i].x);
                            airCells++;
                        }
                        else
                        {
                            double v = Math.Max(0d, aux[i].x);
                            vaporNonAir += v;
                            if (mats[i] == MaterialIds.Water)
                            {
                                waterCells++;
                                vaporWater += v;
                                float tWater = states[i].x;
                                if (tWater < minWaterTemp) minWaterTemp = tWater;
                                if (tWater > maxWaterTemp) maxWaterTemp = tWater;
                                sumWaterTemp += tWater;
                                if (tWater >= 100f) boilingWaterCells++;
                            }
                            else if (mats[i] == MaterialIds.Soil) { vaporSoil += v; }
                            else if (mats[i] == MaterialIds.Rock || mats[i] == MaterialIds.Basalt) { vaporRock += v; }
                            else
                            {
                                vaporOther += v;
                                if (v > 0.1)
                                {
                                    uint m = mats[i];
                                    if (!otherMats.ContainsKey(m)) otherMats[m] = 0d;
                                    otherMats[m] += v;
                                }
                            }
                        }
                    }
                });

                string otherDesc = "";
                foreach (var kv in otherMats) otherDesc += $"[Mat{kv.Key}={kv.Value:F1}] ";
                double avgWaterTemp = waterCells > 0 ? sumWaterTemp / waterCells : 0d;
                double tot = surfaceWater + groundwater + vaporAir + vaporNonAir;
                double d = (tot - (surfaceWater0 + groundwater0 + vaporAir0 + vaporNonAir0)) / (surfaceWater0 + groundwater0 + vaporAir0 + vaporNonAir0);
                Debug.Log($"STRESS_TICK_{currentTick}: Surface={surfaceWater:F1} (cells={waterCells}, temp=[{minWaterTemp:F1}..{avgWaterTemp:F1}..{maxWaterTemp:F1}], boilCells={boilingWaterCells}) GW={groundwater:F1} V_Air={vaporAir:F1} V_NonAir={vaporNonAir:F1} [V_Water={vaporWater:F1}, V_Soil={vaporSoil:F1}, V_Rock={vaporRock:F1}, V_Other={vaporOther:F1} {otherDesc}] Total={tot:F1} (drift={d:P2})");

                if (currentTick == 80)
                {
                    Assert.That(Math.Abs(d), Is.LessThanOrEqualTo(0.02f), "Tracked water drift must be <= 2%.");
                    Assert.That(vaporWater, Is.LessThan(50f), "Submerged liquid water must not hold runaway vapor.");
                }
            }

            // Restore preset back to Standard
            host.ApplyPreset(SimulationPreset.Standard);
            yield return null;
        }
    }
}
