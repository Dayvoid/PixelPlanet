using System;
using System.Collections;
using System.Linq;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class MargolusWorldGenStabilityTests
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

        private static IEnumerator ReadStates(SimulationHost host, Action<Vector4[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.StateRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector4>().ToArray());
                done = true;
            });
            for (int i = 0; i < 240 && !done; i++)
                yield return null;
            Assert.That(failed, Is.False);
            Assert.That(done, Is.True);
        }

        private static IEnumerator ReadFlows(SimulationHost host, Action<Vector2[]> consume)
        {
            bool done = false;
            bool failed = false;
            AsyncGPUReadback.Request(host.Resources.FlowRead, 0, request =>
            {
                if (request.hasError) { failed = true; done = true; return; }
                consume(request.GetData<Vector2>().ToArray());
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
        public IEnumerator MargolusWorldGenSeedsTalusApronsAndShorelines()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.enableMaterialTransport = true;
            host.Config.seed = 6150;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] materials = null;
            yield return ReadMaterials(host, result => materials = result);

            int sedimentCount = CountMaterial(materials, MaterialIds.Sediment);
            int soilCount = CountMaterial(materials, MaterialIds.Soil);
            int rockCount = CountMaterial(materials, MaterialIds.Rock);

            Assert.That(sedimentCount, Is.GreaterThan(0), "Margolus WorldGen retuning should seed initial sediment talus aprons and shorelines.");
            Assert.That(soilCount, Is.GreaterThan(0), "WorldGen should maintain cohesive soil mantles.");
            Assert.That(rockCount, Is.GreaterThan(0), "WorldGen should maintain crystalline bedrock.");

            Vector4[] states = null;
            yield return ReadStates(host, result => states = result);
            bool hasNan = false;
            for (int i = 0; i < states.Length; i++)
            {
                if (float.IsNaN(states[i].x) || float.IsNaN(states[i].y) || float.IsNaN(states[i].z) || float.IsNaN(states[i].w) ||
                    float.IsInfinity(states[i].x) || float.IsInfinity(states[i].y) || float.IsInfinity(states[i].z) || float.IsInfinity(states[i].w))
                {
                    hasNan = true;
                    break;
                }
            }
            Assert.That(hasNan, Is.False, "WorldGen states must contain no NaNs or Infinities.");

            // Restore defaults
            host.Config.enableMaterialTransport = true;
        }

        [UnityTest]
        public IEnumerator MargolusLongRunEndurancePreservesFiniteFieldsAndBasementIntegrity()
        {
            SceneManager.LoadScene("Terrarium");
            yield return WaitForHost();
            SimulationHost host = UnityEngine.Object.FindFirstObjectByType<SimulationHost>();
            host.Config.ApplyPreset(SimulationPreset.Validation);
            host.Config.useOgWorldgen = false;
            host.Config.enableMaterialTransport = true;
            host.Config.margolusSubsteps = 1;
            host.Config.margolusReposeFriction = 1f;
            host.Config.margolusMetricEnable = true;
            host.Config.margolusFluidEnable = true;
            host.Config.seed = 8282;
            host.Regenerate();
            for (int i = 0; i < 8; i++) yield return null;

            uint[] initialMats = null;
            yield return ReadMaterials(host, result => initialMats = (uint[])result.Clone());
            int initialCore = CountMaterial(initialMats, MaterialIds.Core);

            // Execute 120 simulation steps of multi-process physics with Margolus CA active
            yield return Step(host, 120);

            uint[] postMats = null;
            yield return ReadMaterials(host, result => postMats = result);
            int postCore = CountMaterial(postMats, MaterialIds.Core);
            int postSediment = CountMaterial(postMats, MaterialIds.Sediment);

            // Planetary core is pinned and deep; must remain strictly conserved
            Assert.That(postCore, Is.EqualTo(initialCore), "Planetary core mass must remain strictly conserved.");
            // Sediment is active, mobile, and produced by weathering/stress detachment
            Assert.That(postSediment, Is.GreaterThan(0), "Sediment must remain active and present on the surface.");

            Vector4[] postStates = null;
            yield return ReadStates(host, result => postStates = result);
            bool stateNan = false;
            for (int i = 0; i < postStates.Length; i++)
            {
                if (float.IsNaN(postStates[i].x) || float.IsNaN(postStates[i].y) || float.IsNaN(postStates[i].z) || float.IsNaN(postStates[i].w) ||
                    float.IsInfinity(postStates[i].x) || float.IsInfinity(postStates[i].y) || float.IsInfinity(postStates[i].z) || float.IsInfinity(postStates[i].w))
                {
                    stateNan = true;
                    break;
                }
            }
            Assert.That(stateNan, Is.False, "State texture must remain strictly finite with zero NaNs/Infinities after endurance run.");

            Vector2[] postFlows = null;
            yield return ReadFlows(host, result => postFlows = result);
            bool flowNan = false;
            for (int i = 0; i < postFlows.Length; i++)
            {
                if (float.IsNaN(postFlows[i].x) || float.IsNaN(postFlows[i].y) ||
                    float.IsInfinity(postFlows[i].x) || float.IsInfinity(postFlows[i].y))
                {
                    flowNan = true;
                    break;
                }
            }
            Assert.That(flowNan, Is.False, "Flow velocity texture must remain strictly finite after endurance run.");

            // Restore defaults
            host.Config.enableMaterialTransport = true;
        }
    }
}
