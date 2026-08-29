using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class HydrologyIntegrationTests
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

        private static IEnumerator ReadGpuFields(SimulationHost host, Action<uint[], Vector4[], Vector4[]> consume)
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
                        Vector4[] aux = auxRequest.GetData<Vector4>().ToArray();
                        consume(materials, states, aux);
                        done = true;
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator MeasureMetrics(SimulationHost host, Action<WorldWaterMetrics> assign)
        {
            bool metricsReady = false;
            WorldWaterMetrics metrics = default;
            SimulationMetrics.MeasureAsync(host, result =>
            {
                metrics = result;
                metricsReady = true;
            });
            for (int i = 0; i < 240 && !metricsReady; i++)
                yield return null;
            assign(metrics);
        }

        [UnityTest]
        public IEnumerator GeneratedWorldHasOceansGroundwaterAndVapor()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;

            WorldWaterMetrics metrics = default;
            yield return MeasureMetrics(host, result => metrics = result);

            Assert.That(metrics.OceanCoverage, Is.InRange(0.25f, 0.75f));
            Assert.That(metrics.BasinCount, Is.InRange(2, 6));
            Assert.That(metrics.SurfaceWaterMass, Is.GreaterThan(1d));
            Assert.That(metrics.GroundwaterMass, Is.GreaterThan(1d));
            Assert.That(metrics.VaporMass, Is.GreaterThan(0d));
        }

        [UnityTest]
        public IEnumerator SameSeedProducesIdenticalInitialFields()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materialsA = null;
            yield return ReadGpuFields(host, (materials, _, __) => materialsA = (uint[])materials.Clone());

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;
            uint[] materialsB = null;
            yield return ReadGpuFields(host, (materials, _, __) => materialsB = (uint[])materials.Clone());

            Assert.That(materialsB, Is.EqualTo(materialsA));
        }

        [UnityTest]
        public IEnumerator DifferentSeedsChangeBasinLayout()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;

            host.Config.seed = 1001;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            double hashA = 0d;
            yield return ReadGpuFields(host, (materials, _, __) =>
            {
                for (int i = 0; i < materials.Length; i++) hashA += materials[i];
            });

            host.Config.seed = 9001;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            double hashB = 0d;
            yield return ReadGpuFields(host, (materials, _, __) =>
            {
                for (int i = 0; i < materials.Length; i++) hashB += materials[i];
            });

            Assert.That(hashB, Is.Not.EqualTo(hashA));
        }

        [UnityTest]
        public IEnumerator WaterCycleDepositsRainOntoSurface()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.evaporationRate = 0.5f;
            host.Config.precipitationRate = 0.6f;
            host.Config.condensationRate = 0.4f;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Regenerate();
            host.Clock.SetRunning(false);

            double surfaceBefore = 0d;
            double totalBefore = 0d;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                {
                    surfaceBefore += states[i].z;
                    totalBefore += states[i].z + aux[i].x + aux[i].y;
                    Assert.That(mats[i], Is.Not.EqualTo(MaterialIds.Vapor));
                }
            });

            for (int i = 0; i < 120; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            double surfaceAfter = 0d;
            double totalAfter = 0d;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                {
                    surfaceAfter += states[i].z;
                    totalAfter += states[i].z + aux[i].x + aux[i].y;
                    Assert.That(mats[i], Is.Not.EqualTo(MaterialIds.Vapor));
                }
            });

            Assert.That(surfaceAfter, Is.Not.EqualTo(surfaceBefore).Within(0.01d));
            Assert.That(totalAfter, Is.EqualTo(totalBefore).Within(Math.Max(1d, totalBefore * 0.05d)));
        }

        [UnityTest]
        public IEnumerator FullWaterLoopHoldsItsMassOverManyTicks()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.seed = 90210;
            // Every path that moves water runs at once: rain, runoff, ponding, infiltration,
            // percolation, springs, evaporation, and both pixel materialization rules.
            host.Config.evaporationRate = 0.3f;
            host.Config.condensationRate = 0.4f;
            host.Config.precipitationRate = 0.5f;
            host.Config.infiltrationRate = 0.6f;
            host.Config.groundwaterRate = 0.4f;
            host.Config.runoffRate = 1f;
            host.Config.pondingRate = 0.5f;
            host.Config.springDischargeRate = 0.5f;
            host.Config.rainPixelFormationThreshold = 0.35f;
            host.Config.surfaceWaterPixelThreshold = 0.55f;
            host.Config.slowPassInterval = 2;
            host.Config.floraGrowthRate = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Regenerate();
            host.Clock.SetRunning(false);
            for (int i = 0; i < 5; i++) yield return null;

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                    waterBefore += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
            });
            Assert.That(waterBefore, Is.GreaterThan(1d));

            yield return Step(host, 200);

            double waterAfter = 0d;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                {
                    waterAfter += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
                    Assert.That(mats[i], Is.Not.EqualTo(MaterialIds.Vapor));
                }
            });

            double drift = Math.Abs(waterAfter - waterBefore) / waterBefore;
            Assert.That(drift, Is.LessThan(0.01d),
                $"Tracked water drifted {drift:P3} over 200 ticks ({waterBefore:F2} to {waterAfter:F2}).");
        }

        [UnityTest]
        public IEnumerator SnapshotV2RoundTripPreservesTickAndWaterState()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.seed = 777;
            host.Regenerate();
            host.Clock.SetRunning(false);
            for (int i = 0; i < 25; i++) { host.Clock.RequestStep(); yield return null; }

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                    waterBefore += states[i].z + aux[i].x + aux[i].y;
            });
            long tickBefore = host.Clock.TickCount;
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-water-test.snapshot");
            var snapshots = new WorldSnapshotService();
            bool saved = false;
            snapshots.Save(host, path, ok => saved = ok);
            for (int i = 0; i < 240 && !saved; i++) yield return null;
            Assert.That(saved, Is.True);

            host.Regenerate();
            Assert.That(snapshots.Load(host, path), Is.True);
            Assert.That(host.Clock.TickCount, Is.EqualTo(tickBefore));

            double waterAfter = 0d;
            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                for (int i = 0; i < states.Length; i++)
                    waterAfter += states[i].z + aux[i].x + aux[i].y;
            });
            Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(0.01d));
        }

        private static IEnumerator Step(SimulationHost host, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }
        }

        private static void Paint(SimulationHost host, int x, int y, uint materialId)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = materialId,
                values = Vector4.zero
            });
        }

        private static void PaintField(SimulationHost host, int x, int y, float mode, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(mode, amount, 0f, 0f)
            });
        }

        private static void ConfigureSoakIsolation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 100000;
            host.Config.transportPassInterval = 100000;
            host.Config.validationIntervalTicks = 100000;
            host.Config.gravityStrength = 0f;
            host.Config.thermalRate = 0f;
            host.Config.electricalRate = 0f;
            host.Config.solarIntensity = 0f;
            host.Config.radiativeCooling = 0f;
            host.Config.windStrength = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.runoffRate = 0f;
            host.Config.pondingRate = 0f;
            host.Config.springDischargeRate = 0f;
            host.Config.geyserDischargeRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.mantlePressure = 0f;
            host.Config.magmaEruption = 0f;
            host.Config.pressureRate = 0f;
            host.Config.pressureDiffusionRate = 0f;
            host.Config.vaporPressureScale = 0f;
            host.Config.mycologyGrowthRate = 0f;
            host.Config.mycologyDecayRate = 0f;
            host.Config.mycologySettlingRate = 0f;
            host.Config.fieldCapacityFraction = 0.45f;
            host.Config.densityExchangeRate = 0f;
            host.Config.rainPixelFormationThreshold = 0f;
            host.Config.surfaceWaterPixelThreshold = 0f;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.floraGrowthRate = 0f;
            host.Config.surfaceAirHeatExchange = 0f;
            host.Config.temperatureAdvectionRate = 0f;
        }

        private static void PaintSoakColumn(SimulationHost host, int x, int y)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Mantle);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
        }

        [UnityTest]
        public IEnumerator FilmSoakMovesSurfaceWaterIntoSoilGroundwater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 4f;
            host.Config.groundwaterRate = 0f;
            host.Config.seed = 22222;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            PaintSoakColumn(host, x, y);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 2f, 0.4f);
            yield return Step(host, 1);

            float surfaceBefore = 0f;
            float groundBefore = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                surfaceBefore = states[y * width + x].z;
                groundBefore = aux[y * width + x].y;
            });
            Assert.That(surfaceBefore, Is.GreaterThan(0.2f));

            yield return Step(host, 24);

            float surfaceAfter = 0f;
            float groundAfter = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                surfaceAfter = states[y * width + x].z;
                groundAfter = aux[y * width + x].y;
                Assert.That(surfaceAfter + groundAfter, Is.EqualTo(surfaceBefore + groundBefore).Within(0.05f));
            });
            Assert.That(groundAfter, Is.GreaterThan(groundBefore + 0.1f));
            Assert.That(surfaceAfter, Is.LessThan(surfaceBefore - 0.1f));
            Assert.That(groundAfter, Is.LessThanOrEqualTo(0.56f));
        }

        [UnityTest]
        public IEnumerator SaturatedSoilStopsTakingFilmWater()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 4f;
            host.Config.groundwaterRate = 0f;
            host.Config.seed = 22223;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            PaintSoakColumn(host, x, y);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 5f, 0.55f);
            PaintField(host, x, y, 2f, 0.3f);
            yield return Step(host, 12);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(aux[y * width + x].y, Is.LessThanOrEqualTo(0.56f));
                Assert.That(states[y * width + x].z, Is.GreaterThan(0.1f));
            });
        }

        [UnityTest]
        public IEnumerator WaterPixelDrainsIntoSoilUntilSaturated()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 4f;
            host.Config.groundwaterRate = 0f;
            host.Config.seed = 22224;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            int waterY = y + 1;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Mantle);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, waterY, MaterialIds.Air);
                Paint(host, x + dx, waterY + 1, MaterialIds.Air);
            }
            Paint(host, x, waterY, MaterialIds.Water);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, waterY, 2f, -100f);
            PaintField(host, x, waterY, 2f, 0.85f);
            yield return Step(host, 1);

            float waterBefore = 0f;
            float groundBefore = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[waterY * width + x], Is.EqualTo(MaterialIds.Water));
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                waterBefore = states[waterY * width + x].z;
                groundBefore = aux[y * width + x].y;
            });

            yield return Step(host, 30);

            uint waterMat = 0;
            float waterAfter = 0f;
            float groundAfter = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                waterMat = mats[waterY * width + x];
                waterAfter = states[waterY * width + x].z;
                groundAfter = aux[y * width + x].y;
            });
            Assert.That(groundAfter, Is.GreaterThan(groundBefore + 0.15f));
            Assert.That(groundAfter, Is.LessThanOrEqualTo(0.56f));
            Assert.That(waterMat, Is.EqualTo(MaterialIds.Water));
            Assert.That(waterAfter, Is.GreaterThan(0.05f));
            Assert.That(waterAfter + groundAfter, Is.EqualTo(waterBefore + groundBefore).Within(0.08f));
        }

        [UnityTest]
        public IEnumerator ExcessGroundwaterPercolatesDownSoilStack()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 4f;
            host.Config.fieldCapacityFraction = 0.45f;
            host.Config.seed = 22225;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            int lowerY = y - 1;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 2, MaterialIds.Mantle);
                if (dx == 0)
                {
                    Paint(host, x + dx, lowerY, MaterialIds.Soil);
                    Paint(host, x + dx, y, MaterialIds.Soil);
                }
                else
                {
                    Paint(host, x + dx, lowerY, MaterialIds.Mantle);
                    Paint(host, x + dx, y, MaterialIds.Mantle);
                }
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, lowerY, 5f, -100f);
            PaintField(host, x, y, 5f, 0.5f);
            yield return Step(host, 1);

            float topBefore = 0f;
            float lowBefore = 0f;
            yield return ReadGpuFields(host, (_, __, aux) =>
            {
                topBefore = aux[y * width + x].y;
                lowBefore = aux[lowerY * width + x].y;
            });
            Assert.That(topBefore, Is.GreaterThan(0.35f));

            yield return Step(host, 40);

            float topAfter = 0f;
            float lowAfter = 0f;
            yield return ReadGpuFields(host, (_, __, aux) =>
            {
                topAfter = aux[y * width + x].y;
                lowAfter = aux[lowerY * width + x].y;
            });
            Assert.That(lowAfter, Is.GreaterThan(lowBefore + 0.08f));
            Assert.That(topAfter, Is.LessThan(topBefore));
            Assert.That(topAfter, Is.GreaterThan(0.15f));
            Assert.That(topAfter + lowAfter, Is.EqualTo(topBefore + lowBefore).Within(0.08f));
        }

        [UnityTest]
        public IEnumerator GroundwaterDoesNotLeakIntoAir()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 4f;
            host.Config.seed = 22226;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Mantle);
                Paint(host, x + dx, y, dx == 0 ? MaterialIds.Soil : MaterialIds.Air);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 5f, 0.4f);
            yield return Step(host, 20);

            yield return ReadGpuFields(host, (mats, _, aux) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                Assert.That(aux[y * width + x].y, Is.GreaterThan(0.2f));
                Assert.That(aux[y * width + (x + 1)].y, Is.LessThan(0.02f));
                Assert.That(aux[y * width + (x - 1)].y, Is.LessThan(0.02f));
            });
        }

        [UnityTest]
        public IEnumerator CoolWaterSoakLowersHotSoilTemperature()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 4f;
            host.Config.groundwaterRate = 0f;
            host.Config.seed = 22227;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            int waterY = y + 1;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Mantle);
                Paint(host, x + dx, y, MaterialIds.Soil);
                Paint(host, x + dx, waterY, MaterialIds.Air);
            }
            Paint(host, x, waterY, MaterialIds.Water);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, waterY, 2f, -100f);
            PaintField(host, x, waterY, 2f, 0.8f);
            PaintField(host, x, y, 1f, 70f);
            PaintField(host, x, waterY, 1f, 10f);
            yield return Step(host, 1);

            float soilTempBefore = 0f;
            yield return ReadGpuFields(host, (_, states, __) =>
            {
                soilTempBefore = states[y * width + x].x;
            });
            Assert.That(soilTempBefore, Is.GreaterThan(40f));

            yield return Step(host, 24);

            float soilTempAfter = 0f;
            float ground = 0f;
            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                soilTempAfter = states[y * width + x].x;
                ground = aux[y * width + x].y;
            });
            Assert.That(ground, Is.GreaterThan(0.1f));
            Assert.That(soilTempAfter, Is.LessThan(soilTempBefore - 1f));
        }

        [UnityTest]
        public IEnumerator WaterPixelEvaporationCoolsTheCell()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.seed = 22228;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, dx == 0 ? MaterialIds.Water : MaterialIds.Rock);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 2f, 0.8f);
            PaintField(host, x, y, 1f, 50f);
            yield return Step(host, 1);

            float tempBefore = 0f;
            float waterBefore = 0f;
            yield return ReadGpuFields(host, (mats, states, _) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Water));
                tempBefore = states[y * width + x].x;
                waterBefore = states[y * width + x].z;
            });
            Assert.That(tempBefore, Is.GreaterThan(30f));

            // Latent cooling is 0.25 per evaporated mass, so a 0.8 cell can drop ~0.2°C
            // before the pixel dries. Measure while liquid remains instead of after it is gone.
            host.Config.evaporationRate = 4f;
            yield return Step(host, 6);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Water));
                Assert.That(states[y * width + x].x, Is.LessThan(tempBefore - 0.08f));
                Assert.That(states[y * width + x].z, Is.LessThan(waterBefore));
                float vapor = aux[y * width + x].x + aux[(y + 1) * width + x].x;
                Assert.That(vapor, Is.GreaterThan(0.02f));
            });
        }

        [UnityTest]
        public IEnumerator SustainedSurfaceFilmSpawnsStandingWaterPixelAboveSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.densityExchangeRate = 0f;
            host.Config.rainPixelFormationThreshold = 0f;
            host.Config.surfaceWaterPixelThreshold = 0.5f;
            host.Config.seed = 4242;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            PaintSoakColumn(host, x, y);
            PaintField(host, x, y, 1f, 20f);
            PaintField(host, x, y + 1, 1f, 20f);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 5f, -100f);
            PaintField(host, x, y, 2f, 0.3f);
            yield return Step(host, 1);

            float waterBefore = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                Assert.That(mats[(y + 1) * width + x], Is.EqualTo(MaterialIds.Air));
                Assert.That(states[y * width + x].z, Is.GreaterThan(0.2f).And.LessThan(0.45f));
                waterBefore = states[y * width + x].z + states[(y + 1) * width + x].z + aux[y * width + x].x + aux[y * width + x].y
                    + aux[(y + 1) * width + x].x + aux[(y + 1) * width + x].y;
            });

            yield return Step(host, 4);
            yield return ReadGpuFields(host, (mats, _, __) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil), "Sub-threshold film must stay on soil.");
                Assert.That(mats[(y + 1) * width + x], Is.EqualTo(MaterialIds.Air));
            });

            PaintField(host, x, y, 2f, 0.6f);
            yield return Step(host, 1);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                uint above = mats[(y + 1) * width + x];
                Assert.That(above == MaterialIds.Water || above == MaterialIds.Ice, Is.True,
                    "Sustained film should spawn a standing water pixel in the open cell above.");
                Assert.That(states[y * width + x].z, Is.LessThan(0.45f));
                Assert.That(states[(y + 1) * width + x].z, Is.GreaterThan(0.45f));
                float waterAfter = states[y * width + x].z + states[(y + 1) * width + x].z + aux[y * width + x].x + aux[y * width + x].y
                    + aux[(y + 1) * width + x].x + aux[(y + 1) * width + x].y;
                Assert.That(waterAfter, Is.EqualTo(waterBefore + 0.6f).Within(0.08f));
                for (int i = 0; i < mats.Length; i++)
                    Assert.That(mats[i], Is.Not.EqualTo(MaterialIds.Vapor));
            });
        }

        [UnityTest]
        public IEnumerator ZeroSurfacePixelThresholdKeepsFilmOnSoil()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.surfaceWaterPixelThreshold = 0f;
            host.Config.rainPixelFormationThreshold = 0f;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            PaintSoakColumn(host, x, y);
            PaintField(host, x, y, 2f, -100f);
            PaintField(host, x, y, 2f, 0.8f);
            yield return Step(host, 6);

            yield return ReadGpuFields(host, (mats, states, _) =>
            {
                Assert.That(mats[y * width + x], Is.EqualTo(MaterialIds.Soil));
                Assert.That(mats[(y + 1) * width + x], Is.EqualTo(MaterialIds.Air));
                Assert.That(states[y * width + x].z, Is.GreaterThan(0.5f));
            });
        }

        private static void ConfigureHydrostaticIsolation(SimulationHost host)
        {
            ConfigureSoakIsolation(host);
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.runoffRate = 2f;
            host.Config.pondingRate = 0f;
            host.Config.surfaceWaterPixelThreshold = 0f;
            host.Config.rainPixelFormationThreshold = 0f;
            host.Config.materialSubsteps = 1;
            host.Config.grassWaterUptakeRate = 0f;
            host.Config.floraGrowthRate = 0f;
        }

        private static double TrackedWater(Vector4[] states, Vector4[] aux)
        {
            double total = 0d;
            for (int i = 0; i < states.Length; i++)
                total += Math.Max(0d, states[i].z) + Math.Max(0d, aux[i].x) + Math.Max(0d, aux[i].y);
            return total;
        }

        private static bool IsLiquidPixel(uint material) =>
            material == MaterialIds.Water || material == MaterialIds.Ice;

        private static bool IsOpenCell(uint material) =>
            material == MaterialIds.Air || material == MaterialIds.Void || material == MaterialIds.Vapor;

        private static int TallestLiquidRun(uint[] mats, int width, int height, int column, int fromY)
        {
            int tallest = 0;
            int run = 0;
            for (int y = Mathf.Max(0, fromY); y < height; y++)
            {
                run = IsLiquidPixel(mats[y * width + column]) ? run + 1 : 0;
                tallest = Math.Max(tallest, run);
            }
            return tallest;
        }

        // Tallest run of liquid pixels whose lowest member hangs over an open cell. Standing
        // water rests on terrain, so a tall airborne run means precipitation was misread as a
        // surface column and packed into midair.
        private static int TallestAirborneLiquidRun(uint[] mats, int width, int height, int column)
        {
            int tallest = 0;
            for (int y = 1; y < height; y++)
            {
                if (!IsLiquidPixel(mats[y * width + column])) continue;
                if (IsLiquidPixel(mats[(y - 1) * width + column])) continue;
                if (!IsOpenCell(mats[(y - 1) * width + column])) continue;
                int run = 0;
                while (y + run < height && IsLiquidPixel(mats[(y + run) * width + column]))
                    run++;
                tallest = Math.Max(tallest, run);
            }
            return tallest;
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
        public IEnumerator FilmMoundOnFlatShelfDecreasesHeadVariance()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.seed = 33001;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 10);
            int x0 = x - 6;
            int x1 = x + 6;
            PaintRockShelf(host, x0, x1, bedY, 4);
            for (int xx = x0; xx <= x1; xx++)
            {
                PaintField(host, xx, bedY, 2f, -100f);
                PaintField(host, xx, bedY, 5f, -100f);
                float film = Mathf.Abs(xx - x) <= 1 ? 0.9f : 0.15f;
                PaintField(host, xx, bedY, 2f, film);
            }
            // The brush only lands on a tick, and the column solver relaxes far enough within
            // one tick to flatten a mound this narrow. Hold leveling off for the settling tick
            // so the baseline measured below is the mound, not its answer.
            float runoff = host.Config.runoffRate;
            host.Config.runoffRate = 0f;
            yield return Step(host, 1);
            host.Config.runoffRate = runoff;

            double waterBefore = 0d;
            float varianceBefore = 0f;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                waterBefore = TrackedWater(states, aux);
                float sum = 0f;
                float sumSq = 0f;
                int count = 0;
                for (int xx = x0; xx <= x1; xx++)
                {
                    Assert.That(mats[bedY * width + xx], Is.EqualTo(MaterialIds.Rock));
                    float film = states[bedY * width + xx].z;
                    sum += film;
                    sumSq += film * film;
                    count++;
                }
                float mean = sum / count;
                varianceBefore = sumSq / count - mean * mean;
                Assert.That(varianceBefore, Is.GreaterThan(0.02f));
            });

            yield return Step(host, 80);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                double waterAfter = TrackedWater(states, aux);
                Assert.That(waterAfter, Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                float sum = 0f;
                float sumSq = 0f;
                float min = 1e9f;
                float max = -1e9f;
                int count = 0;
                for (int xx = x0; xx <= x1; xx++)
                {
                    float film = states[bedY * width + xx].z;
                    sum += film;
                    sumSq += film * film;
                    min = Mathf.Min(min, film);
                    max = Mathf.Max(max, film);
                    count++;
                }
                float mean = sum / count;
                float varianceAfter = sumSq / count - mean * mean;
                Assert.That(varianceAfter, Is.LessThan(varianceBefore * 0.35f));
                Assert.That(max - min, Is.LessThan(0.2f));
            });
        }

        [UnityTest]
        public IEnumerator UnequalStandingWaterColumnsConvergeToCommonSurface()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33002;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 12);
            PaintRockShelf(host, x - 2, x + 3, bedY, 8);
            for (int xx = x - 2; xx <= x + 3; xx++)
            {
                PaintField(host, xx, bedY, 2f, -100f);
                PaintField(host, xx, bedY, 5f, -100f);
            }
            for (int dy = 1; dy <= 4; dy++)
            {
                Paint(host, x, bedY + dy, MaterialIds.Water);
                PaintField(host, x, bedY + dy, 2f, -100f);
                PaintField(host, x, bedY + dy, 2f, 1f);
            }
            Paint(host, x + 1, bedY + 1, MaterialIds.Water);
            PaintField(host, x + 1, bedY + 1, 2f, -100f);
            PaintField(host, x + 1, bedY + 1, 2f, 1f);
            yield return Step(host, 1);

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) => waterBefore = TrackedWater(states, aux));

            yield return Step(host, 50);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                int topA = -1;
                int topB = -1;
                for (int y = bedY + 8; y > bedY; y--)
                {
                    if (topA < 0 && mats[y * width + x] == MaterialIds.Water) topA = y;
                    if (topB < 0 && mats[y * width + (x + 1)] == MaterialIds.Water) topB = y;
                }
                Assert.That(topA, Is.GreaterThan(bedY));
                Assert.That(topB, Is.GreaterThan(bedY));
                Assert.That(Math.Abs(topA - topB), Is.LessThanOrEqualTo(1));
                float headA = topA + states[topA * width + x].z;
                float headB = topB + states[topB * width + (x + 1)].z;
                Assert.That(headA, Is.EqualTo(headB).Within(0.35f));
            });
        }

        // Every other hydrostatic test builds a shelf six to thirteen columns wide, which one
        // flux pass can cross in a handful of ticks. A basin the width of a real ocean cannot:
        // a single pass moves head exactly one column per tick, so the solver has to relax
        // repeatedly within a tick or wide water keeps its worldgen slope indefinitely.
        [UnityTest]
        public IEnumerator WideBasinLevelsAtTheShippingRunoffRate()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            // Deliberately the shipping values rather than the tuned ones the other tests use.
            host.Config.runoffRate = 0.45f;
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33009;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            const int halfWidth = 20;
            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int centre = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(height * 0.62f), 16, height - 20);
            int top = height - 1;

            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                int xx = host.Grid.WrapTheta(centre + dx);
                Paint(host, xx, bedY - 1, MaterialIds.Mantle);
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int y = bedY + 1; y <= top; y++)
                    Paint(host, xx, y, MaterialIds.Air);
                PaintField(host, xx, bedY, 2f, -100f);
                PaintField(host, xx, bedY, 5f, -100f);
            }
            foreach (int wall in new[] { centre - halfWidth - 1, centre + halfWidth + 1 })
            {
                int xx = host.Grid.WrapTheta(wall);
                Paint(host, xx, bedY - 1, MaterialIds.Mantle);
                for (int y = bedY; y <= top; y++)
                    Paint(host, xx, y, MaterialIds.Rock);
            }
            for (int dx = -halfWidth; dx < 0; dx++)
            {
                int xx = host.Grid.WrapTheta(centre + dx);
                for (int dy = 1; dy <= 12; dy++)
                {
                    Paint(host, xx, bedY + dy, MaterialIds.Water);
                    PaintField(host, xx, bedY + dy, 2f, -100f);
                    PaintField(host, xx, bedY + dy, 2f, 1f);
                }
            }
            yield return Step(host, 1);

            double waterBefore = 0d;
            int reliefBefore = 0;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                waterBefore = TrackedWater(states, aux);
                reliefBefore = SurfaceRelief(mats, host, width, height, centre, halfWidth, bedY);
                Assert.That(reliefBefore, Is.GreaterThan(6),
                    "The tank must start with a genuine step for the solver to remove.");
            });

            yield return Step(host, 200);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                int relief = SurfaceRelief(mats, host, width, height, centre, halfWidth, bedY);
                Assert.That(relief, Is.LessThanOrEqualTo(2),
                    $"A sealed {halfWidth * 2 + 1} column basin should be level; relief went {reliefBefore} -> {relief}.");
            });
        }

        // Highest minus lowest standing-water surface across the tank, measured in cells.
        private static int SurfaceRelief(uint[] mats, SimulationHost host, int width, int height, int centre, int halfWidth, int bedY)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                int xx = host.Grid.WrapTheta(centre + dx);
                int surface = bedY;
                for (int y = height - 1; y > bedY; y--)
                {
                    if (!IsLiquidPixel(mats[y * width + xx])) continue;
                    surface = y;
                    break;
                }
                min = Math.Min(min, surface);
                max = Math.Max(max, surface);
            }
            return max - min;
        }

        [UnityTest]
        public IEnumerator WaterSpillsDownAStepButNotOverAWall()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33003;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 12, host.Grid.radialResolution - 12);

            int top = host.Grid.radialResolution - 1;
            for (int xx = x - 4; xx <= x + 6; xx++)
            {
                Paint(host, xx, bedY - 2, MaterialIds.Mantle);
                Paint(host, xx, bedY - 1, MaterialIds.Rock);
                Paint(host, xx, bedY, MaterialIds.Rock);
                for (int y = bedY + 1; y <= top; y++)
                    Paint(host, xx, y, MaterialIds.Air);
            }
            Paint(host, x - 5, bedY - 2, MaterialIds.Mantle);
            Paint(host, x + 7, bedY - 2, MaterialIds.Mantle);
            for (int y = bedY - 1; y <= top; y++)
            {
                Paint(host, x - 5, y, MaterialIds.Rock);
                Paint(host, x + 7, y, MaterialIds.Rock);
            }

            // Left terrace sits two cells above the right basin.
            for (int xx = x - 4; xx <= x - 1; xx++)
            {
                Paint(host, xx, bedY + 1, MaterialIds.Rock);
                Paint(host, xx, bedY + 2, MaterialIds.Rock);
            }
            for (int dy = 3; dy <= 5; dy++)
            {
                Paint(host, x - 2, bedY + dy, MaterialIds.Water);
                PaintField(host, x - 2, bedY + dy, 2f, -100f);
                PaintField(host, x - 2, bedY + dy, 2f, 1f);
            }

            // Wall isolates a dry pocket on the far right.
            for (int y = bedY; y <= top; y++)
                Paint(host, x + 3, y, MaterialIds.Rock);
            Paint(host, x + 5, bedY + 1, MaterialIds.Water);
            PaintField(host, x + 5, bedY + 1, 2f, -100f);
            PaintField(host, x + 5, bedY + 1, 2f, 1f);

            yield return Step(host, 1);
            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) => waterBefore = TrackedWater(states, aux));
            yield return Step(host, 60);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                bool spilled = false;
                for (int y = bedY + 1; y <= bedY + 5; y++)
                {
                    if (mats[y * width + (x + 1)] == MaterialIds.Water)
                        spilled = true;
                }
                Assert.That(spilled, Is.True, "Water should spill from the high terrace onto the lower basin.");
                Assert.That(mats[(bedY + 1) * width + (x + 5)], Is.EqualTo(MaterialIds.Water),
                    "A rock wall must keep the isolated pocket from draining.");
                for (int y = bedY + 2; y <= bedY + 6; y++)
                    Assert.That(mats[y * width + (x + 5)], Is.Not.EqualTo(MaterialIds.Water));
            });
        }

        [UnityTest]
        public IEnumerator HydrostaticFluxWrapsTheAngularSeam()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33004;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 10);
            PaintRockShelf(host, width - 3, width + 2, bedY, 6);
            int left = width - 1;
            int right = 0;
            for (int dy = 1; dy <= 3; dy++)
            {
                Paint(host, left, bedY + dy, MaterialIds.Water);
                PaintField(host, left, bedY + dy, 2f, -100f);
                PaintField(host, left, bedY + dy, 2f, 1f);
            }
            Paint(host, right, bedY + 1, MaterialIds.Water);
            PaintField(host, right, bedY + 1, 2f, -100f);
            PaintField(host, right, bedY + 1, 2f, 1f);
            yield return Step(host, 1);

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) => waterBefore = TrackedWater(states, aux));
            yield return Step(host, 50);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                int topL = -1;
                int topR = -1;
                for (int y = bedY + 6; y > bedY; y--)
                {
                    if (topL < 0 && mats[y * width + left] == MaterialIds.Water) topL = y;
                    if (topR < 0 && mats[y * width + right] == MaterialIds.Water) topR = y;
                }
                Assert.That(topL, Is.GreaterThan(bedY));
                Assert.That(topR, Is.GreaterThan(bedY));
                Assert.That(Math.Abs(topL - topR), Is.LessThanOrEqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator EnclosedCaveWaterIsIgnoredBySurfaceLeveling()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33005;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 12, host.Grid.radialResolution - 12);
            int caveY = bedY - 4;
            PaintRockShelf(host, x - 3, x + 3, bedY, 6);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                    Paint(host, x + dx, caveY + dy, MaterialIds.Rock);
            }
            Paint(host, x, caveY, MaterialIds.Water);
            PaintField(host, x, caveY, 2f, -100f);
            PaintField(host, x, caveY, 2f, 0.8f);

            for (int dy = 1; dy <= 3; dy++)
            {
                Paint(host, x - 1, bedY + dy, MaterialIds.Water);
                PaintField(host, x - 1, bedY + dy, 2f, -100f);
                PaintField(host, x - 1, bedY + dy, 2f, 1f);
            }
            yield return Step(host, 1);

            float caveBefore = 0f;
            uint caveMatBefore = 0;
            double waterBefore = 0d;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                caveMatBefore = mats[caveY * width + x];
                caveBefore = states[caveY * width + x].z;
                waterBefore = TrackedWater(states, aux);
                Assert.That(caveMatBefore, Is.EqualTo(MaterialIds.Water));
                Assert.That(caveBefore, Is.GreaterThan(0.5f));
            });

            yield return Step(host, 40);

            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
                Assert.That(mats[caveY * width + x], Is.EqualTo(MaterialIds.Water));
                Assert.That(states[caveY * width + x].z, Is.EqualTo(caveBefore).Within(0.04f));
                Assert.That(mats[(caveY + 1) * width + x], Is.EqualTo(MaterialIds.Rock));
            });
        }

        [UnityTest]
        public IEnumerator SaturatedCloudColumnRainsOutWithoutBuildingATower()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            // Precipitation is the only water source here. A vertically saturated cloud must
            // shed separated drops instead of converting every cell in one dispatch, which
            // used to drop a solid bar that re-piled into a one-wide tower on the shelf.
            host.Config.precipitationRate = 1f;
            host.Config.rainPixelFormationThreshold = 0.5f;
            host.Config.cloudPrecipitationThreshold = 1f;
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.atmosphericAdvectionRate = 0f;
            host.Config.vaporDiffusionRate = 0f;
            host.Config.seed = 33006;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);
            const int cloudBase = 3;
            const int cloudCells = 10;
            PaintRockShelf(host, x - 4, x + 4, bedY, cloudBase + cloudCells + 2);
            for (int xx = x - 4; xx <= x + 4; xx++)
            {
                PaintField(host, xx, bedY, 2f, -100f);
                PaintField(host, xx, bedY, 5f, -100f);
            }
            // Clear air under the cloud base leaves the drops somewhere to fall.
            for (int dy = cloudBase; dy < cloudBase + cloudCells; dy++)
            {
                PaintField(host, x, bedY + dy, 6f, -100f);
                PaintField(host, x, bedY + dy, 2f, -100f);
                PaintField(host, x, bedY + dy, 2f, 0.8f);
            }
            yield return Step(host, 1);

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (mats, states, aux) =>
            {
                Assert.That(mats[bedY * width + x], Is.EqualTo(MaterialIds.Rock));
                waterBefore = TrackedWater(states, aux);
            });

            int tallest = 0;
            for (int i = 0; i < 24; i++)
            {
                yield return Step(host, 1);
                yield return ReadGpuFields(host, (mats, _, __) =>
                {
                    tallest = Math.Max(tallest, TallestLiquidRun(mats, width, height, x, bedY + 1));
                });
            }

            Assert.That(tallest, Is.LessThanOrEqualTo(3),
                $"Saturated cloud column produced a {tallest}-cell water stack; rain must arrive as separated drops.");

            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
            });
        }

        [UnityTest]
        public IEnumerator AirborneDropStackIsNotTreatedAsASurfaceColumn()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureHydrostaticIsolation(host);
            host.Config.surfaceWaterPixelThreshold = 0.2f;
            host.Config.seed = 33007;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int width = host.Grid.angularResolution;
            int height = host.Grid.radialResolution;
            int x = width / 2;
            int bedY = Mathf.Clamp(Mathf.RoundToInt(height * 0.55f), 10, height - 20);
            PaintRockShelf(host, x - 2, x + 3, bedY, 14);
            for (int xx = x - 2; xx <= x + 3; xx++)
            {
                PaintField(host, xx, bedY, 2f, -100f);
                PaintField(host, xx, bedY, 5f, -100f);
            }

            // A tall standing column beside a clump that hangs in the air. Reading the clump as
            // a surface column gives it an air cell for a bed, and the tall neighbour then packs
            // hydrostatic flux into midair above it.
            for (int dy = 1; dy <= 10; dy++)
            {
                Paint(host, x, bedY + dy, MaterialIds.Water);
                PaintField(host, x, bedY + dy, 2f, -100f);
                PaintField(host, x, bedY + dy, 2f, 1f);
            }
            const int clumpBottom = 2;
            const int clumpTop = 5;
            for (int dy = clumpBottom; dy <= clumpTop; dy++)
            {
                Paint(host, x + 1, bedY + dy, MaterialIds.Water);
                PaintField(host, x + 1, bedY + dy, 2f, -100f);
                PaintField(host, x + 1, bedY + dy, 2f, 1f);
            }
            yield return Step(host, 1);

            double waterBefore = 0d;
            yield return ReadGpuFields(host, (_, states, aux) => waterBefore = TrackedWater(states, aux));

            for (int i = 0; i < 10; i++)
            {
                yield return ReadGpuFields(host, (mats, _, __) =>
                {
                    for (int y = bedY + clumpTop + 1; y < height; y++)
                    {
                        Assert.That(IsLiquidPixel(mats[y * width + (x + 1)]), Is.False,
                            $"Hydrostatic flux packed water at y={y}, above the airborne clump at {bedY + clumpTop}.");
                    }
                    Assert.That(TallestAirborneLiquidRun(mats, width, height, x + 1),
                        Is.LessThanOrEqualTo(clumpTop - clumpBottom + 1));
                });
                yield return Step(host, 1);
            }

            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                Assert.That(TrackedWater(states, aux), Is.EqualTo(waterBefore).Within(Math.Max(0.05d, waterBefore * 0.01d)));
            });
        }
    }
}
