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
    public sealed class MaceMobileGeologyTests
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
            host.Config.fractureRate = 0.2f;
            host.Config.erosionRate = 0.04f;
        }

        [UnityTest]
        public IEnumerator AshChannelConservesWhenFlagged()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.maceAsh = true;
            host.Config.maceSedimentPilot = true;
            host.Config.validationIntervalTicks = 100000;
            host.Config.magmaEruption = 0f;
            host.Config.precipitationRate = 0f;
            host.Config.ashFertilityStrength = 0f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.62f), 10, host.Grid.radialResolution - 10);
            for (int dx = -2; dx <= 2; dx++)
            {
                Paint(host, x + dx, y - 1, MaterialIds.Rock);
                Paint(host, x + dx, y, MaterialIds.Ash);
                Paint(host, x + dx, y + 1, MaterialIds.Air);
            }
            yield return Step(host, 1);
            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            yield return Step(host, 16);
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);
            Assert.That(after.HasNegative, Is.False);
            Assert.That(after.Ash + after.Structural, Is.EqualTo(before.Ash + before.Structural).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator MagmaAndStructuralMassStayConserved()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.maceMagma = true;
            host.Config.maceSedimentPilot = true;
            host.Config.validationIntervalTicks = 100000;
            host.Config.magmaEruption = 0f;
            host.Config.coreHeatRate = 0f;
            host.Config.thermalRate = 0f;
            host.Config.volcanicCooling = 0.2f;
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
            yield return Step(host, 1);
            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            yield return Step(host, 16);
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);
            Assert.That(after.Magma + after.Structural, Is.EqualTo(before.Magma + before.Structural).Within(0.05d));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator HardWearDoesNotFireBelowThreshold()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.maceHardWear = true;
            host.Config.maceSedimentPilot = true;
            host.Config.maceWearThreshold = 1.25f;
            host.Config.erosionRate = 0f;
            host.Config.dissolutionRate = 0f;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Granite);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);
            WorldMobileMetrics before = default;
            yield return MeasureMobile(host, result => before = result);
            yield return Step(host, 20);
            WorldMobileMetrics after = default;
            yield return MeasureMobile(host, result => after = result);
            Assert.That(after.CoarseSediment + after.FineSediment, Is.EqualTo(before.CoarseSediment + before.FineSediment).Within(0.02d));
            uint granite = 0;
            yield return ReadMaterialsAndAux(host, (mats, _) => granite = mats[y * host.Grid.angularResolution + x]);
            Assert.That(granite, Is.EqualTo(MaterialIds.Granite));
            RestoreFlags(host);
        }

        [UnityTest]
        public IEnumerator TectonicStressDoesNotAccrueOnCrust()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.maceHardWear = true;
            host.Config.maceSedimentPilot = true;
            host.Config.slowPassInterval = 1;
            host.Config.validationIntervalTicks = 100000;
            host.Config.fractureRate = 2f;
            host.Config.mantlePressure = 2f;
            host.Config.erosionRate = 0f;
            host.Config.windStrength = 0f;
            host.Regenerate();
            for (int i = 0; i < 4; i++) yield return null;

            int x = host.Grid.angularResolution / 2;
            int y = Mathf.Clamp(Mathf.RoundToInt(host.Grid.radialResolution * 0.6f), 10, host.Grid.radialResolution - 10);
            Paint(host, x, y - 1, MaterialIds.Rock);
            Paint(host, x, y, MaterialIds.Granite);
            Paint(host, x, y + 1, MaterialIds.Air);
            yield return Step(host, 1);
            yield return Step(host, 12);
            float stress = -1f;
            yield return ReadMaterialsAndAux(host, (mats, aux) =>
            {
                int index = y * host.Grid.angularResolution + x;
                Assert.That(mats[index], Is.EqualTo(MaterialIds.Granite));
                stress = aux[index].w;
            });
            Assert.That(stress, Is.LessThan(0.05f));
            RestoreFlags(host);
        }
    }
}
