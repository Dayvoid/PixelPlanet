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
        public IEnumerator ConservationValidatorPassesAfterLongRun()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.validationIntervalTicks = 100000;
            host.Config.atmosphericAdvectionRate = 0.85f;
            host.Config.vaporDiffusionRate = 0.08f;
            host.Regenerate();
            host.Clock.SetRunning(false);

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            for (int i = 0; i < 5; i++) yield return null;

            for (int i = 0; i < 120; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;

            Assert.That(validator.LastValidationPassed, Is.True, validator.LastMessage);
            Assert.That(validator.LastMessage, Does.Not.Contain("Non-finite"));
            Assert.That(validator.LastMessage, Does.Not.Contain("Negative"));

            yield return ReadGpuFields(host, (mats, _, __) =>
            {
                for (int i = 0; i < mats.Length; i++)
                    Assert.That(mats[i], Is.Not.EqualTo(MaterialIds.Vapor));
            });
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
            host.Config.slowPassInterval = 1;
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
                Paint(host, x + dx, lowerY, MaterialIds.Soil);
                Paint(host, x + dx, y, MaterialIds.Soil);
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
            PaintField(host, x, waterY, 1f, -20f);
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
            host.Config.evaporationRate = 4f;
            host.Config.seed = 22228;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 8, host.Grid.radialResolution - 8);
            int width = host.Grid.angularResolution;
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Water);
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

            yield return Step(host, 30);

            yield return ReadGpuFields(host, (_, states, aux) =>
            {
                Assert.That(states[y * width + x].x, Is.LessThan(tempBefore - 0.5f));
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
    }
}
