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
            Assert.That(CountMaterial(materials, MaterialIds.Limestone), Is.GreaterThan(0), "V2 worldgen should seed limestone deposits.");
            Assert.That(CountMaterial(materials, MaterialIds.Clay), Is.GreaterThan(0), "V2 worldgen should seed clay lenses.");
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
            Assert.That(CountMaterial(materials, MaterialIds.Limestone), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Clay), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Ice), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Water), Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator NewPipelineSameSeedIsDeterministic()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
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

        [UnityTest]
        public IEnumerator RockCellsReceiveGroundwaterWhenWaterTableConfigured()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.groundwaterDepth = 0.65f;
            host.Config.initialGroundwaterSaturation = 0.65f;
            host.Config.seed = 5150;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            Vector4[] aux = null;
            yield return ReadGpuFields(host, (m, _, a) =>
            {
                materials = m;
                aux = a;
            });

            int wetGranite = 0;
            int wetLimestone = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                uint mat = materials[i];
                float gw = aux[i].y;
                if (mat == MaterialIds.Granite && gw > 0.005f)
                {
                    wetGranite++;
                    Assert.That(gw, Is.LessThanOrEqualTo(0.081f), "Granite groundwater must not exceed capacity.");
                }
                else if (mat == MaterialIds.Limestone && gw > 0.005f)
                {
                    wetLimestone++;
                    Assert.That(gw, Is.LessThanOrEqualTo(0.401f), "Limestone groundwater must not exceed capacity.");
                }
            }

            Assert.That(wetGranite, Is.GreaterThan(0), "Granite in the crust water table must receive initial groundwater.");
            Assert.That(wetLimestone, Is.GreaterThan(0), "Limestone in the crust water table must receive initial groundwater.");
        }

        [UnityTest]
        public IEnumerator DryGroundwaterSettingLeavesRockDry()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Clock.SetRunning(false);
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.groundwaterDepth = 0f;
            host.Config.initialGroundwaterSaturation = 0f;
            host.Config.seed = 5150;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            Vector4[] aux = null;
            yield return ReadGpuFields(host, (m, _, a) =>
            {
                materials = m;
                aux = a;
            });

            for (int i = 0; i < materials.Length; i++)
            {
                uint mat = materials[i];
                if (mat == MaterialIds.Granite || mat == MaterialIds.Limestone || mat == MaterialIds.Basalt)
                {
                    Assert.That(aux[i].y, Is.EqualTo(0f), "Zero water table setting must generate dry rock.");
                }
            }
        }
    }
}
