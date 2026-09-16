using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Scenarios;
using GeneSys.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class WorldScenarioIntegrationTests
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

        private static IEnumerator ReadGpuFields(SimulationHost host, Action<uint[], Vector4[], Vector4[], Vector4[]> consume)
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
                        AsyncGPUReadback.Request(host.Resources.EcologyRead, 0, ecologyRequest =>
                        {
                            if (ecologyRequest.hasError) { failed = true; done = true; return; }
                            consume(materials, states, aux, ecologyRequest.GetData<Vector4>().ToArray());
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

        private static IEnumerator MeasureMetrics(SimulationHost host, Action<WorldWaterMetrics> assign)
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

        private static int CountMaterial(uint[] materials, uint id)
        {
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == id) count++;
            return count;
        }

        private static void PrepareValidation(SimulationHost host)
        {
            host.Clock.SetRunning(false);
            host.Config.worldScenario = WorldScenario.Sandbox;
            host.ApplyPreset(SimulationPreset.Validation);
            host.Clock.SetRunning(false);
            host.Config.useOgWorldgen = false;
            host.Config.seed = 5150;
            host.Config.targetOceanCoverage = 0.5f;
            host.Config.initialGroundwaterSaturation = 0.65f;
            host.Config.initialAtmosphericHumidity = 0.7f;
            host.Config.borderNoise = 0.035f;
            host.Config.soilRatio = 0.025f;
            host.Config.clayDepositCount = 20;
            host.Config.mycologyInitialSporeLoad = 0.08f;
            host.Config.frozenOceans = false;
            host.Config.enableRockChunks = false;
        }

        private static void RestoreSandbox(SimulationHost host)
        {
            host.Config.worldScenario = WorldScenario.Sandbox;
            host.Config.enableRockChunks = true;
            host.ApplyPreset(SimulationPreset.Standard);
            host.Clock.SetRunning(false);
        }

        private static float MeanWetHostGroundwater(uint[] materials, Vector4[] aux)
        {
            double sum = 0d;
            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                uint mat = materials[i];
                if (mat != MaterialIds.Soil && mat != MaterialIds.Granite && mat != MaterialIds.Basalt
                    && mat != MaterialIds.Limestone && mat != MaterialIds.Clay && mat != MaterialIds.Sediment)
                    continue;
                float groundwater = Mathf.Max(0f, aux[i].y);
                if (groundwater <= 0.001f) continue;
                sum += groundwater;
                count++;
            }

            return count == 0 ? 0f : (float)(sum / count);
        }

        [UnityTest]
        public IEnumerator WorldWideWaterFloodsSurfaceAndPresoaksGroundWithoutRewritingMenu()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            PrepareValidation(host);
            host.Config.worldScenario = WorldScenario.Sandbox;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            WorldWaterMetrics sandbox = default;
            yield return MeasureMetrics(host, result => sandbox = result);
            uint[] sandboxMaterials = null;
            Vector4[] sandboxAux = null;
            yield return ReadGpuFields(host, (m, _, a, __) =>
            {
                sandboxMaterials = m;
                sandboxAux = a;
            });
            float sandboxHostGroundwater = MeanWetHostGroundwater(sandboxMaterials, sandboxAux);

            host.Config.worldScenario = WorldScenario.WorldWideWater;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            WorldWaterMetrics waterWorld = default;
            yield return MeasureMetrics(host, result => waterWorld = result);
            uint[] waterMaterials = null;
            Vector4[] waterAux = null;
            yield return ReadGpuFields(host, (m, _, a, __) =>
            {
                waterMaterials = m;
                waterAux = a;
            });
            float waterHostGroundwater = MeanWetHostGroundwater(waterMaterials, waterAux);

            Assert.That(host.Config.targetOceanCoverage, Is.EqualTo(0.5f));
            Assert.That(host.Config.initialGroundwaterSaturation, Is.EqualTo(0.65f));
            Assert.That(waterWorld.OceanCoverage, Is.GreaterThan(0.9f));
            Assert.That(CountMaterial(waterMaterials, MaterialIds.Water), Is.GreaterThan(CountMaterial(sandboxMaterials, MaterialIds.Water)));
            Assert.That(waterHostGroundwater, Is.GreaterThan(sandboxHostGroundwater));

            RestoreSandbox(host);
        }

        [UnityTest]
        public IEnumerator CometStruckMoonStartsDrySterileAndLeavesMenuFieldsAlone()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            PrepareValidation(host);
            host.Config.worldScenario = WorldScenario.CometStruckMoon;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            Vector4[] aux = null;
            Vector4[] ecology = null;
            yield return ReadGpuFields(host, (m, _, a, e) =>
            {
                materials = m;
                aux = a;
                ecology = e;
            });

            Assert.That(host.Config.targetOceanCoverage, Is.EqualTo(0.5f));
            Assert.That(host.Config.initialAtmosphericHumidity, Is.EqualTo(0.7f));
            Assert.That(host.Config.initialGroundwaterSaturation, Is.EqualTo(0.65f));
            Assert.That(host.Config.clayDepositCount, Is.EqualTo(20));
            Assert.That(host.Config.mycologyInitialSporeLoad, Is.EqualTo(0.08f));
            Assert.That(CountMaterial(materials, MaterialIds.Water), Is.EqualTo(0));
            Assert.That(CountMaterial(materials, MaterialIds.Clay), Is.EqualTo(0));

            float vapor = 0f;
            float spores = 0f;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == MaterialIds.Air)
                    vapor += aux[i].x;
                spores += ecology[i].x;
            }

            Assert.That(vapor, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(spores, Is.EqualTo(0f).Within(1e-4f));

            RestoreSandbox(host);
        }

        [UnityTest]
        public IEnumerator CometStruckMoonPaintsIceThenImpactsNearTick500()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            PrepareValidation(host);
            host.Config.worldScenario = WorldScenario.CometStruckMoon;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            yield return ReadMaterials(host, result => materials = result);
            int iceBefore = CountMaterial(materials, MaterialIds.Ice);

            yield return Step(host, (int)WorldScenarioDirector.CometApproachStartTick);
            yield return ReadMaterials(host, result => materials = result);
            int iceAfterApproach = CountMaterial(materials, MaterialIds.Ice);
            Assert.That(iceAfterApproach, Is.GreaterThan(iceBefore + 100));

            Vector2Int impact = WorldScenarioDirector.PredictImpactCell(host.Config, host.Grid);
            int index = impact.y * host.Grid.angularResolution + impact.x;
            Vector4[] states = null;
            yield return ReadGpuFields(host, (_, s, __, ___) => states = s);
            float heatBefore = states[index].x;
            float pressureBefore = states[index].y;

            yield return Step(host, (int)(WorldScenarioDirector.CometImpactTick - host.Clock.TickCount));
            yield return ReadGpuFields(host, (_, s, __, ___) => states = s);

            Assert.That(host.Clock.TickCount, Is.EqualTo(WorldScenarioDirector.CometImpactTick));
            Assert.That(states[index].x, Is.GreaterThan(heatBefore + 20f));
            Assert.That(states[index].y, Is.GreaterThan(pressureBefore + 0.5f));

            RestoreSandbox(host);
        }
    }
}
