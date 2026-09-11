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
    public sealed class MaceKarstSoluteTests
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

        private static IEnumerator ReadMaterialsAndAux(SimulationHost host, Action<uint[], Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            UnityEngine.Rendering.AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                UnityEngine.Rendering.AsyncGPUReadback.Request(host.Resources.AuxRead, 0, auxRequest =>
                {
                    if (auxRequest.hasError) { failed = true; done = true; return; }
                    consume(materials, auxRequest.GetData<Vector4>().ToArray());
                    done = true;
                });
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

        private static void PaintGroundwater(SimulationHost host, int x, int y, float amount)
        {
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 0,
                materialId = MaterialIds.Void,
                values = new Vector4(5f, amount, 0f, 0f)
            });
        }

        private static void ConfigureKarst(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.slowPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.maceSedimentPilot = true;
            host.Config.maceSolute = true;
            host.Config.maceEntrainment = false;
            host.Config.maceHardWear = false;
            host.Config.dissolutionRate = 2f;
            host.Config.erosionRate = 0f;
            host.Config.collapseRate = 0f;
            host.Config.infiltrationRate = 0f;
            host.Config.groundwaterRate = 0f;
            host.Config.evaporationRate = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.condensationRate = 0f;
            host.Config.maceStructuralDeplete = 0.05f;
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
            host.Config.dissolutionRate = 0.03f;
            host.Config.validationIntervalTicks = 1000;
            host.Config.slowPassInterval = 4;
        }

        [UnityTest]
        public IEnumerator CombinedSolidMassIsConserved()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureKarst(host);
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Limestone);
                PaintGroundwater(host, x + dx, y, 1f);
            }
            yield return Step(host, 1);
            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            yield return Step(host, 16);
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);

            double solidBefore = before.Structural + before.CoarseSediment + before.FineSediment + before.Solute;
            double solidAfter = after.Structural + after.CoarseSediment + after.FineSediment + after.Solute;
            Assert.That(after.HasNegative, Is.False);
            Assert.That(solidAfter, Is.EqualTo(solidBefore).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator GroundwaterUnchangedBySoluteBookkeeping()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureKarst(host);
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Limestone);
            PaintGroundwater(host, x, y, 0.9f);
            yield return Step(host, 1);

            int index = y * host.Grid.angularResolution + x;
            float groundBefore = 0f;
            WorldMobileMetrics mobileBefore = default;
            yield return ReadMaterialsAndAux(host, (_, aux) => groundBefore = aux[index].y);
            yield return MeasureMobile(host, result => mobileBefore = result);
            yield return Step(host, 12);
            float groundAfter = 0f;
            WorldMobileMetrics mobileAfter = default;
            yield return ReadMaterialsAndAux(host, (_, aux) => groundAfter = aux[index].y);
            yield return MeasureMobile(host, result => mobileAfter = result);
            Assert.That(mobileAfter.Solute, Is.GreaterThanOrEqualTo(mobileBefore.Solute));
            Assert.That(groundAfter - groundBefore, Is.GreaterThan(-0.15f));
            Assert.That(groundAfter - groundBefore, Is.LessThan(0.05f));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator LimestoneDissolvesFasterThanGranite()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureKarst(host);
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x + 4, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Limestone);
            Paint(host, x + 4, y, MaterialIds.Granite);
            PaintGroundwater(host, x, y, 1f);
            PaintGroundwater(host, x + 4, y, 1f);
            yield return Step(host, 1);
            yield return Step(host, 20);

            WorldMobileMetrics metrics = default;
            yield return MeasureMobile(host, result => metrics = result);
            Assert.That(metrics.Solute, Is.GreaterThan(0.01d));

            uint limestone = 0;
            uint granite = 0;
            yield return ReadMaterialsAndAux(host, (mats, _) =>
            {
                int width = host.Grid.angularResolution;
                limestone = mats[y * width + x];
                granite = mats[y * width + x + 4];
            });
            Assert.That(granite, Is.EqualTo(MaterialIds.Granite));
            Assert.That(limestone == MaterialIds.Limestone || limestone == MaterialIds.Air || limestone == MaterialIds.Sediment || limestone == MaterialIds.Void, Is.True);
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator CavityAppearsOnlyAfterStructuralDepletion()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            ConfigureKarst(host);
            host.Config.maceStructuralDeplete = 0.2f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Limestone);
            Paint(host, x, y + 1, MaterialIds.Air);
            PaintGroundwater(host, x, y, 1f);
            yield return Step(host, 1);

            uint early = 0;
            yield return ReadMaterialsAndAux(host, (mats, _) => early = mats[y * host.Grid.angularResolution + x]);
            Assert.That(early, Is.EqualTo(MaterialIds.Limestone));

            yield return Step(host, 40);
            uint late = 0;
            yield return ReadMaterialsAndAux(host, (mats, _) => late = mats[y * host.Grid.angularResolution + x]);
            WorldMobileMetrics metrics = default;
            yield return MeasureMobile(host, result => metrics = result);
            if (late != MaterialIds.Limestone)
                Assert.That(metrics.Solute, Is.GreaterThan(0.05d));
            RestoreFlags(host);
        }
    }
}
