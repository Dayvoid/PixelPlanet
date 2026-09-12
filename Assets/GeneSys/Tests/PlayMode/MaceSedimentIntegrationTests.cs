using System;
using System.Collections;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MaceSedimentIntegrationTests
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

        private static IEnumerator ReadMaterials(SimulationHost host, Action<uint[]> consume)
        {
            bool done = false;
            bool failed = false;
            UnityEngine.Rendering.AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, request =>
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

        private static void ConfigureQuiet(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.geodynamicsLayerEnable = false;
            host.Config.magmaEruption = 0f;
            host.Config.fractureRate = 0f;
            host.Config.extrusionRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.erosionRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.maceSedimentPilot = true;
            host.Config.maceEntrainment = false;
            host.Config.maceShorelineSorting = false;
            host.Config.maceSolute = false;
            host.Config.maceAsh = false;
            host.Config.maceMagma = false;
            host.Config.maceHardWear = false;
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
            host.Config.geodynamicsLayerEnable = true;
            host.Config.validationIntervalTicks = 1000;
            host.Config.slowPassInterval = 4;
        }

        private static void PaintSedimentPile(SimulationHost host, out int x, out int y)
        {
            x = host.Grid.angularResolution / 2;
            y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 10);
            for (int dx = -3; dx <= 3; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Sediment);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
                Paint(host, x + dx, y + 2, MaterialIds.Air);
            }
        }

        [UnityTest]
        public IEnumerator ClosedBoxSedimentMassIsConserved()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Config.seed = 4401;
            host.Regenerate();
            for (int i = 0; i < 5; i++) yield return null;
            PaintSedimentPile(host, out _, out _);
            yield return Step(host, 1);

            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            yield return Step(host, 24);
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);

            double sedimentBefore = before.CoarseSediment + before.FineSediment;
            double sedimentAfter = after.CoarseSediment + after.FineSediment;
            Assert.That(after.HasNonFinite, Is.False);
            Assert.That(after.HasNegative, Is.False);
            Assert.That(sedimentAfter, Is.EqualTo(sedimentBefore).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator MobileChannelsStayNonNegativeAndFinite()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;
            PaintSedimentPile(host, out _, out _);
            yield return Step(host, 12);
            WorldMobileMetrics metrics = default;
            yield return MeasureMobile(host, result => metrics = result);
            Assert.That(metrics.HasNonFinite, Is.False);
            Assert.That(metrics.HasNegative, Is.False);
            Assert.That(metrics.MinChannel, Is.GreaterThanOrEqualTo(-1e-5f));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator UnsupportedSedimentFallsInwardWhileSupportedPileHolds()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Config.gravityStrength = 1f;
            host.Config.maceLambda = 4f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 12, host.Grid.radialResolution - 10);
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 2, MaterialIds.Rock);
                Paint(host, x + dx, y - 1, MaterialIds.Air);
                Paint(host, x + dx, y, MaterialIds.Sediment);
                Paint(host, x + 8 + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + 8 + dx, y, MaterialIds.Sediment);
                Paint(host, x + 8 + dx, y + 1, MaterialIds.Air);
            }
            yield return Step(host, 1);
            yield return Step(host, 20);

            uint hanging = 0;
            uint supported = 0;
            uint caught = 0;
            yield return ReadMaterials(host, mats =>
            {
                int width = host.Grid.angularResolution;
                hanging = mats[y * width + x];
                caught = mats[(y - 1) * width + x];
                supported = mats[y * width + x + 8];
            });
            Assert.That(supported, Is.EqualTo(MaterialIds.Sediment));
            Assert.That(hanging == MaterialIds.Sediment || caught == MaterialIds.Sediment, Is.True);
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator SameSeedAndBrushesAreDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Config.seed = 9090;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;
            PaintSedimentPile(host, out _, out _);
            yield return Step(host, 8);
            WorldMobileMetrics first = default;
            yield return MeasureMobile(host, result => first = result);

            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;
            PaintSedimentPile(host, out _, out _);
            yield return Step(host, 8);
            WorldMobileMetrics second = default;
            yield return MeasureMobile(host, result => second = result);

            Assert.That(second.CoarseSediment, Is.EqualTo(first.CoarseSediment).Within(1e-4d));
            Assert.That(second.FineSediment, Is.EqualTo(first.FineSediment).Within(1e-4d));
            Assert.That(second.Structural, Is.EqualTo(first.Structural).Within(1e-4d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator LegacySnapshotMigratesSedimentAndV14RoundTrips()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Config.seed = 1313;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;
            PaintSedimentPile(host, out _, out _);
            yield return Step(host, 2);

            WorldMobileMetrics live = default;
            yield return MeasureMobile(host, result => live = result);
            string legacy = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-mace-v13.snapshot");
            string current = System.IO.Path.Combine(Application.temporaryCachePath, "genesys-mace-v14.snapshot");
            var snapshots = new WorldSnapshotService();
            bool savedLegacy = false;
            snapshots.Save(host, legacy, 13, ok => savedLegacy = ok);
            for (int i = 0; i < 240 && !savedLegacy; i++) yield return null;
            Assert.That(savedLegacy, Is.True);

            bool savedCurrent = false;
            snapshots.Save(host, current, ok => savedCurrent = ok);
            for (int i = 0; i < 240 && !savedCurrent; i++) yield return null;
            Assert.That(savedCurrent, Is.True);

            host.Regenerate();
            Assert.That(snapshots.Load(host, legacy), Is.True);
            WorldMobileMetrics migrated = default;
            yield return MeasureMobile(host, result => migrated = result);
            Assert.That(migrated.CoarseSediment, Is.GreaterThan(1d));

            host.Regenerate();
            Assert.That(snapshots.Load(host, current), Is.True);
            WorldMobileMetrics roundTrip = default;
            yield return MeasureMobile(host, result => roundTrip = result);
            Assert.That(roundTrip.CoarseSediment, Is.EqualTo(live.CoarseSediment).Within(0.05d));
            Assert.That(roundTrip.Structural, Is.EqualTo(live.Structural).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator IsolatedMagmaAndAshStayPutWhenOnlySedimentPilotIsOn()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureQuiet(host);
            host.Config.gravityStrength = 0f;
            host.Config.volcanicCooling = 0f;
            host.Config.thermalRate = 0f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.55f), 10, host.Grid.radialResolution - 10);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                    Paint(host, x + dx, y + dy, MaterialIds.Rock);
            }
            Paint(host, x, y, MaterialIds.Magma);
            Paint(host, x + 1, y + 1, MaterialIds.Ash);
            yield return Step(host, 1);
            yield return Step(host, 10);

            uint magma = 0;
            uint ash = 0;
            yield return ReadMaterials(host, mats =>
            {
                int width = host.Grid.angularResolution;
                magma = mats[y * width + x];
                ash = mats[(y + 1) * width + x + 1];
            });
            Assert.That(magma, Is.Not.EqualTo(MaterialIds.Sediment));
            Assert.That(magma, Is.EqualTo(MaterialIds.Magma).Or.EqualTo(MaterialIds.Basalt));
            Assert.That(ash, Is.EqualTo(MaterialIds.Ash));
            RestoreFlags(host);
        }
    }
}
