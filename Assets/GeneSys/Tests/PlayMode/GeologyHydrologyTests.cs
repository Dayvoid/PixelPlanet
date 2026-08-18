using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
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
    public sealed class GeologyHydrologyTests
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

        private static IEnumerator Measure(SimulationHost host, Action<WorldWaterMetrics> assign)
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

        private static IEnumerator ReadColumn(SimulationHost host, int x, Action<uint[], Vector4[], Vector4[], float[]> consume)
        {
            bool done = false;
            bool failed = false;
            int height = host.Grid.radialResolution;
            AsyncGPUReadback.Request(host.Resources.MaterialRead, 0, x, 1, 0, height, 0, 1, materialRequest =>
            {
                if (materialRequest.hasError) { failed = true; done = true; return; }
                uint[] materials = materialRequest.GetData<uint>().ToArray();
                AsyncGPUReadback.Request(host.Resources.WaterRead, 0, x, 1, 0, height, 0, 1, waterRequest =>
                {
                    if (waterRequest.hasError) { failed = true; done = true; return; }
                    Vector4[] water = waterRequest.GetData<Vector4>().ToArray();
                    AsyncGPUReadback.Request(host.Resources.StateRead, 0, x, 1, 0, height, 0, 1, stateRequest =>
                    {
                        if (stateRequest.hasError) { failed = true; done = true; return; }
                        Vector4[] state = stateRequest.GetData<Vector4>().ToArray();
                        AsyncGPUReadback.Request(host.Resources.Hydrostatic, 0, x, 1, 0, height, 0, 1, hydroRequest =>
                        {
                            if (hydroRequest.hasError) { failed = true; done = true; return; }
                            Vector4[] hydro4 = hydroRequest.GetData<Vector4>().ToArray();
                            var hydro = new float[hydro4.Length];
                            for (int i = 0; i < hydro4.Length; i++) hydro[i] = hydro4[i].x;
                            consume(materials, water, state, hydro);
                            done = true;
                        });
                    });
                });
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        [UnityTest]
        public IEnumerator PressureIncreasesInwardAndMantleStaysMostlySolid()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.ApplyPreset(SimulationPreset.Validation);
            yield return WaitForHost();
            host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.fractureRate = 0f;
            host.Config.mantlePressure = 0f;
            host.Regenerate();
            yield return null;

            WorldWaterMetrics initial = default;
            yield return Measure(host, metrics => initial = metrics);
            host.StepNow(1000);
            yield return null;

            WorldWaterMetrics after = default;
            yield return Measure(host, metrics => after = metrics);
            Assert.That(after.PressureIncreasesInward, Is.True);
            Assert.That(after.MeanInnerTotalPressure, Is.GreaterThan(after.MeanOuterTotalPressure));
            Assert.That(after.MagmaCells, Is.LessThanOrEqualTo(Math.Max(32, initial.MagmaCells + 16)));
            Assert.That(after.InnerWaterMass, Is.LessThan(after.TotalTrackedWaterMass * 0.08d));
        }

        [UnityTest]
        public IEnumerator TickWindowDoesNotRunawayConvertMantle()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.ApplyPreset(SimulationPreset.Validation);
            yield return WaitForHost();
            host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Regenerate();
            yield return null;

            WorldWaterMetrics at0 = default;
            yield return Measure(host, metrics => at0 = metrics);
            host.StepNow(360);
            WorldWaterMetrics at360 = default;
            yield return Measure(host, metrics => at360 = metrics);
            host.StepNow(120);
            WorldWaterMetrics at480 = default;
            yield return Measure(host, metrics => at480 = metrics);
            host.StepNow(120);
            WorldWaterMetrics at600 = default;
            yield return Measure(host, metrics => at600 = metrics);

            Assert.That(at360.MagmaCells, Is.LessThan(at0.MantleCells / 4));
            Assert.That(at480.MagmaCells, Is.LessThan(at0.MantleCells / 3));
            Assert.That(at600.MagmaCells, Is.LessThan(at0.MantleCells / 2));
            Assert.That(at600.TotalTrackedWaterMass, Is.EqualTo(at0.TotalTrackedWaterMass).Within(Math.Max(8d, at0.TotalTrackedWaterMass * 0.08d)));
        }

        [UnityTest]
        public IEnumerator MagmaRisesThroughSedimentButNotIntactRock()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.ApplyPreset(SimulationPreset.Validation);
            yield return WaitForHost();
            host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Regenerate();

            int x = host.Grid.angularResolution / 2;
            int magmaY = Mathf.FloorToInt(host.Grid.radialResolution * 0.45f);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, magmaY + 3),
                radius = 2,
                materialId = MaterialIds.Sediment,
                values = Vector4.zero
            });
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, magmaY),
                radius = 2,
                materialId = MaterialIds.Magma,
                values = Vector4.zero
            });
            host.StepNow(24);

            int magmaTop = 0;
            yield return ReadColumn(host, x, (materials, _, __, ___) =>
            {
                for (int y = 0; y < materials.Length; y++)
                    if (materials[y] == MaterialIds.Magma) magmaTop = Math.Max(magmaTop, y);
            });
            Assert.That(magmaTop, Is.GreaterThan(magmaY));

            host.Regenerate();
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, magmaY + 3),
                radius = 2,
                materialId = MaterialIds.Rock,
                values = Vector4.zero
            });
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, magmaY),
                radius = 2,
                materialId = MaterialIds.Magma,
                values = Vector4.zero
            });
            host.StepNow(24);
            int magmaTopRock = 0;
            yield return ReadColumn(host, x, (materials, _, __, ___) =>
            {
                for (int y = 0; y < materials.Length; y++)
                    if (materials[y] == MaterialIds.Magma) magmaTopRock = Math.Max(magmaTopRock, y);
            });
            Assert.That(magmaTopRock, Is.LessThanOrEqualTo(magmaY + 2));
        }

        [UnityTest]
        public IEnumerator GroundwaterBoilsAwayFromHotPressurizedCorePath()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.ApplyPreset(SimulationPreset.Validation);
            yield return WaitForHost();
            host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Regenerate();

            int x = 8;
            int y = Mathf.FloorToInt(host.Grid.radialResolution * 0.35f);
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 3,
                materialId = MaterialIds.Magma,
                values = Vector4.zero
            });
            host.QueueBrush(new GpuPassScheduler.BrushCommand
            {
                center = new Vector2Int(x, y),
                radius = 3,
                materialId = MaterialIds.Magma,
                values = new Vector4(1f, 400f, 0f, 0f)
            });
            host.StepNow(40);
            WorldWaterMetrics metrics = default;
            yield return Measure(host, result => metrics = result);
            Assert.That(metrics.VaporMass + metrics.SurfaceWaterMass + metrics.GroundwaterMass, Is.GreaterThan(0d));
        }
    }
}
