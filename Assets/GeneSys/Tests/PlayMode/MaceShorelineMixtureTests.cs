using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MaceShorelineMixtureTests
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

        private static IEnumerator MeasureMobile(SimulationHost host, Action<WorldMobileMetrics> assign)
        {
            bool ready = false;
            WorldMobileMetrics metrics = default;
            SimulationMetrics.MeasureMobileMassAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
        }

        private static IEnumerator MeasureWater(SimulationHost host, Action<WorldWaterMetrics> assign)
        {
            bool ready = false;
            WorldWaterMetrics metrics = default;
            SimulationMetrics.MeasureAsync(host, result =>
            {
                metrics = result;
                ready = true;
            });
            for (int i = 0; i < 240 && !ready; i++)
                yield return null;
            Assert.That(ready, Is.True);
            assign(metrics);
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

        private static void DriveWind(SimulationHost host, int x, int y)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x - 1, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(3f, 8f, 0f, 0f)
            });
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x + 1, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(3f, -2f, 0f, 0f)
            });
        }

        private static void RestoreFlags(SimulationHost host)
        {
            host.Config.maceSedimentPilot = false;
            host.Config.maceEntrainment = false;
            host.Config.maceShorelineSorting = false;
            host.Config.maceSolute = false;
            host.Config.maceAsh = false;
            host.Config.maceMagma = false;
            host.Config.maceHardWear = false;
            host.Config.validationIntervalTicks = 1000;
            host.Config.slowPassInterval = 4;
            host.Config.erosionRate = 0.04f;
            host.Config.windStrength = 2f;
        }

        [UnityTest]
        public IEnumerator CoarseAndFineChannelsConserveWhileSorting()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.maceSedimentPilot = true;
            host.Config.maceEntrainment = true;
            host.Config.maceShorelineSorting = true;
            host.Config.erosionRate = 2f;
            host.Config.baseSoilCohesion = 0f;
            host.Config.stressDecayRate = 0.01f;
            host.Config.windStrength = 3f;
            host.Config.maceExtractRate = 1f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 10);
            for (int dx = -4; dx <= 4; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, dx < 0 ? MaterialIds.Soil : MaterialIds.Clay);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
            Paint(host, x - 1, y + 1, MaterialIds.Sediment);
            Paint(host, x, y + 1, MaterialIds.Sediment);
            Paint(host, x + 1, y + 1, MaterialIds.Sediment);
            yield return Step(host, 1);
            for (int i = 0; i < 20; i++)
            {
                DriveWind(host, x, y);
                yield return Step(host, 1);
            }

            host.Config.maceEntrainment = false;
            host.Config.erosionRate = 0f;
            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            for (int i = 0; i < 16; i++)
            {
                DriveWind(host, x, y);
                yield return Step(host, 1);
            }
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);

            Assert.That(before.CoarseSediment + before.FineSediment, Is.GreaterThan(1d));
            Assert.That(after.CoarseSediment, Is.EqualTo(before.CoarseSediment).Within(0.05d));
            Assert.That(after.FineSediment, Is.EqualTo(before.FineSediment).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator TrackedWaterStaysConservedWithShorelineSorting()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.maceSedimentPilot = true;
            host.Config.maceShorelineSorting = true;
            host.Config.validationIntervalTicks = 100000;
            host.Config.seed = 5151;
            host.Regenerate();
            for (int i = 0; i < 6; i++) yield return null;

            WorldWaterMetrics before = default;
            yield return MeasureWater(host, result => before = result);
            yield return Step(host, 20);
            WorldWaterMetrics after = default;
            yield return MeasureWater(host, result => after = result);
            Assert.That(after.TotalTrackedWaterMass, Is.EqualTo(before.TotalTrackedWaterMass).Within(Math.Max(0.25d, before.TotalTrackedWaterMass * 0.02d)));
            RestoreFlags(host);
        }
    }
}
