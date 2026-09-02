using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class WorldGenIntegrationTests
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

        private static int CountMaterial(uint[] materials, uint id) =>
            Array.FindAll(materials, value => value == id).Length;

        [UnityTest]
        public IEnumerator NewPipelinePlacesMetalIceAndOceans()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.metalVeinCount = 12;
            host.Config.seed = 5150;
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.frozenOceans = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            yield return ReadMaterials(host, result => materials = result);

            Assert.That(CountMaterial(materials, MaterialIds.Metal), Is.GreaterThan(0), "V2 worldgen should seed metal veins.");
            Assert.That(CountMaterial(materials, MaterialIds.Ice), Is.GreaterThan(0), "V2 worldgen should seed polar ice caps.");
            Assert.That(CountMaterial(materials, MaterialIds.Water), Is.GreaterThan(0), "V2 worldgen should still fill oceans.");

            WorldWaterMetrics metrics = default;
            bool metricsReady = false;
            SimulationMetrics.MeasureAsync(host, result =>
            {
                metrics = result;
                metricsReady = true;
            });
            for (int i = 0; i < 240 && !metricsReady; i++)
                yield return null;

            Assert.That(metrics.OceanCoverage, Is.InRange(0.2f, 0.8f));
        }

        [UnityTest]
        public IEnumerator FrozenOceansFillsBasinsWithIce()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.metalVeinCount = 12;
            host.Config.seed = 5150;
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.frozenOceans = true;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            yield return ReadMaterials(host, result => materials = result);

            Assert.That(CountMaterial(materials, MaterialIds.Water), Is.EqualTo(0), "Frozen oceans should not place liquid water.");
            Assert.That(CountMaterial(materials, MaterialIds.Ice), Is.GreaterThan(100), "Frozen oceans should fill basins with ice.");

            WorldWaterMetrics metrics = default;
            bool metricsReady = false;
            SimulationMetrics.MeasureAsync(host, result =>
            {
                metrics = result;
                metricsReady = true;
            });
            for (int i = 0; i < 240 && !metricsReady; i++)
                yield return null;

            Assert.That(metrics.OceanCoverage, Is.InRange(0.2f, 0.8f));
        }

        [UnityTest]
        public IEnumerator OgPipelineDoesNotSeedMetalOrIceAtGeneration()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = true;
            host.Config.seed = 5150;
            host.Config.frozenOceans = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            yield return ReadMaterials(host, result => materials = result);

            Assert.That(CountMaterial(materials, MaterialIds.Metal), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Ice), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Water), Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator NewPipelineSameSeedIsDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.seed = 9090;
            host.Config.frozenOceans = false;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] first = null;
            yield return ReadMaterials(host, result => first = (uint[])result.Clone());

            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] second = null;
            yield return ReadMaterials(host, result => second = result);

            Assert.That(second, Is.EqualTo(first));
        }
    }
}
