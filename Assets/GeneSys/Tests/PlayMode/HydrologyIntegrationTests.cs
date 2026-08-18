using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Persistence;
using GeneSys.Simulation;
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
            host.Regenerate();
            host.Clock.SetRunning(false);

            double surfaceBefore = 0d;
            yield return ReadGpuFields(host, (_, states, __) =>
            {
                for (int i = 0; i < states.Length; i++)
                    surfaceBefore += states[i].z;
            });

            for (int i = 0; i < 120; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            double surfaceAfter = 0d;
            yield return ReadGpuFields(host, (_, states, __) =>
            {
                for (int i = 0; i < states.Length; i++)
                    surfaceAfter += states[i].z;
            });

            Assert.That(surfaceAfter, Is.Not.EqualTo(surfaceBefore).Within(0.01d));
        }

        [UnityTest]
        public IEnumerator ConservationValidatorPassesAfterLongRun()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.validationIntervalTicks = 100000;
            host.Regenerate();
            host.Clock.SetRunning(false);

            SimulationValidator validator = UnityEngine.Object.FindFirstObjectByType<SimulationValidator>();
            Assert.That(validator, Is.Not.Null);
            validator.ResetBaseline();
            for (int i = 0; i < 5; i++) yield return null;

            for (int i = 0; i < 60; i++)
            {
                host.Clock.RequestStep();
                yield return null;
            }

            bool validationDone = false;
            validator.ValidationCompleted += (_, __) => validationDone = true;
            validator.ValidateNow();
            for (int i = 0; i < 240 && !validationDone; i++)
                yield return null;

            Assert.That(validator.LastMessage, Does.Not.Contain("Non-finite"));
            Assert.That(validator.LastMessage, Does.Not.Contain("Negative"));
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
    }
}
