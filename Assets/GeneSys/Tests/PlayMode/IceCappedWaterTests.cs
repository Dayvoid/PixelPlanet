using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class IceCappedWaterTests
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

        private static IEnumerator ReadMaterials(SimulationHost host, Action<uint[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<uint>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static int Index(SimulationHost host, int x, int y) => y * host.Grid.angularResolution + host.Grid.WrapTheta(x);

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(host.Grid.WrapTheta(x), y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(mode, amount, 0f, 0f)
            });
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void ConfigureHydrostaticIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.gravityStrength = 1f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 2f;
            host.Config.pondingRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.dewRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.windStrength = 0f;
            host.Config.atmosphericBuoyancy = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.terrainRadiativeCooling = 0f;
            host.Config.atmosphereRadiativeCooling = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.ashSettlingStrength = 0f;
            host.Config.ashUpdraftStrength = 0f;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.mantlePressure = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.phaseHysteresis = 50f;
            host.Config.materialSubsteps = 1;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.floraGrowthRate = 0f;
        }

        private static void PaintRockShelf(SimulationHost host, int x0, int x1, int bedY, int airHeight)
        {
            int top = host.Grid.radialResolution - 1;
            int airTop = Mathf.Max(bedY + airHeight, top);
            for (int x = x0; x <= x1; x++)
            {
                int xx = host.Grid.WrapTheta(x);
                Paint(host, xx, bedY - 1, MaterialIds.Mantle);
                Paint(host, xx, bedY, MaterialIds.Rock);
                PaintField(host, xx, bedY, 1f, 15f);
                for (int y = bedY + 1; y <= airTop; y++)
                    Paint(host, xx, y, MaterialIds.Air);
            }
            int left = host.Grid.WrapTheta(x0 - 1);
            int right = host.Grid.WrapTheta(x1 + 1);
            Paint(host, left, bedY - 1, MaterialIds.Mantle);
            Paint(host, right, bedY - 1, MaterialIds.Mantle);
            for (int y = bedY; y <= airTop; y++)
            {
                Paint(host, left, y, MaterialIds.Rock);
                Paint(host, right, y, MaterialIds.Rock);
            }
        }

        [UnityTest]
        public IEnumerator SubIceWaterLevelsWithAdjacentBasin()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.seed = 44021;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(height * 0.5f), 10, height - 30);

            // Confined rock basin from x-4 to x+4 with boundary walls at x-5 and x+5
            PaintRockShelf(host, x - 4, x + 4, bedY, 20);

            // High water with ice cap on x-4 to x-1 (water at bedY+1..bedY+5, ice at bedY+6)
            for (int xx = x - 4; xx <= x - 1; xx++)
            {
                for (int yy = bedY + 1; yy <= bedY + 5; yy++)
                {
                    Paint(host, xx, yy, MaterialIds.Water);
                    PaintField(host, xx, yy, 1f, 10f); // 10 deg C
                    PaintField(host, xx, yy, 2f, 1f);  // mass = 1
                }
                Paint(host, xx, bedY + 6, MaterialIds.Ice);
                PaintField(host, xx, bedY + 6, 1f, -5f); // -5 deg C
                PaintField(host, xx, bedY + 6, 2f, 1f);
            }

            // Low water on x to x+4 (water at bedY+1, bedY+2)
            for (int xx = x; xx <= x + 4; xx++)
            {
                for (int yy = bedY + 1; yy <= bedY + 2; yy++)
                {
                    Paint(host, xx, yy, MaterialIds.Water);
                    PaintField(host, xx, yy, 1f, 10f);
                    PaintField(host, xx, yy, 2f, 1f);
                }
            }

            host.Config.runoffRate = 0f;
            yield return Step(host, 1);

            int leftWaterBefore = 0;
            int rightWaterBefore = 0;
            yield return ReadMaterials(host, mats =>
            {
                for (int xx = x - 4; xx <= x - 1; xx++)
                    for (int yy = bedY + 1; yy <= bedY + 10; yy++)
                        if (mats[Index(host, xx, yy)] == MaterialIds.Water) leftWaterBefore++;

                for (int xx = x; xx <= x + 4; xx++)
                    for (int yy = bedY + 1; yy <= bedY + 10; yy++)
                        if (mats[Index(host, xx, yy)] == MaterialIds.Water) rightWaterBefore++;
            });

            Assert.That(leftWaterBefore + rightWaterBefore, Is.EqualTo(30),
                "Total water before leveling must be conserved.");
            Assert.That(leftWaterBefore, Is.GreaterThan(rightWaterBefore),
                "Initial water configuration has higher water under the ice on the left.");

            // Enable runoff and run hydrostatic leveling steps
            host.Config.runoffRate = 2f;
            yield return Step(host, 30);

            yield return ReadMaterials(host, mats =>
            {
                int leftWaterAfter = 0;
                int rightWaterAfter = 0;
                for (int xx = x - 4; xx <= x - 1; xx++)
                    for (int yy = bedY + 1; yy <= bedY + 10; yy++)
                        if (mats[Index(host, xx, yy)] == MaterialIds.Water) leftWaterAfter++;

                for (int xx = x; xx <= x + 4; xx++)
                    for (int yy = bedY + 1; yy <= bedY + 10; yy++)
                        if (mats[Index(host, xx, yy)] == MaterialIds.Water) rightWaterAfter++;

                // Water under the ice should have flowed to the right basin!
                Assert.That(leftWaterAfter, Is.LessThan(leftWaterBefore),
                    "Water from under the ice cap should have flowed to the lower adjacent basin.");
                Assert.That(rightWaterAfter, Is.GreaterThan(rightWaterBefore),
                    "Right basin should have received water from under the ice cap.");
            });
        }

        [UnityTest]
        public IEnumerator UnsupportedIceFallsStraightDownIntoAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.seed = 44022;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(height * 0.5f), 10, height - 20);

            // Rock at y, Air at y+1..y+4, Ice at y+5
            Paint(host, x, y, MaterialIds.Rock);
            for (int yy = y + 1; yy <= y + 4; yy++)
                Paint(host, x, yy, MaterialIds.Air);
            Paint(host, x, y + 5, MaterialIds.Ice);
            PaintField(host, x, y + 5, 2f, 1f);
            PaintField(host, x, y + 5, 1f, -5f);

            // Run simulation steps so gravity pulls ice down
            yield return Step(host, 10);

            yield return ReadMaterials(host, mats =>
            {
                // Ice should have fallen down onto the rock at y+1
                Assert.That(mats[Index(host, x, y + 1)], Is.EqualTo(MaterialIds.Ice),
                    "Unsupported ice should fall straight down into air until landing on rock.");
                Assert.That(mats[Index(host, x, y + 5)], Is.EqualTo(MaterialIds.Air),
                    "Original high cell should now be Air.");
            });
        }

        [UnityTest]
        public IEnumerator LaterallyExposedWaterSpillsIntoOpenAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.seed = 44023;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(height * 0.5f), 10, height - 30);

            // Rock foundation from x-3 to x+3
            for (int xx = x - 3; xx <= x + 3; xx++)
            {
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int yy = bedY + 1; yy <= bedY + 8; yy++)
                    Paint(host, xx, yy, MaterialIds.Air);
            }

            // An isolated pillar of water at x (from bedY+1 to bedY+5), air at x-1 and x+1
            for (int yy = bedY + 1; yy <= bedY + 5; yy++)
            {
                Paint(host, x, yy, MaterialIds.Water);
                PaintField(host, x, yy, 2f, 1f);
                PaintField(host, x, yy, 1f, 15f);
            }

            // Run steps allowing laterally exposed water to spill
            yield return Step(host, 15);

            yield return ReadMaterials(host, mats =>
            {
                // The isolated pillar at x should no longer be at height bedY+5
                Assert.That(mats[Index(host, x, bedY + 5)], Is.EqualTo(MaterialIds.Air),
                    "The top of the exposed water tower should have collapsed.");

                // Neighbor columns should now contain water that spilled laterally
                bool leftHasWater = mats[Index(host, x - 1, bedY + 1)] == MaterialIds.Water;
                bool rightHasWater = mats[Index(host, x + 1, bedY + 1)] == MaterialIds.Water;
                Assert.That(leftHasWater || rightHasWater, Is.True,
                    "Laterally unconfined water should have spilled into adjacent open air.");
            });
        }
    }
}
