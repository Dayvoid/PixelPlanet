using System.Collections;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class SimulationScenarioTests
    {
        [UnityTest]
        public IEnumerator TerrariumInitializesAndSingleStepIsExact()
        {
            SceneManager.LoadScene("Terrarium");
            SimulationHost host = null;
            for (int i = 0; i < 120 && host == null; i++)
            {
                host = Object.FindFirstObjectByType<SimulationHost>();
                yield return null;
            }
            Assert.That(host, Is.Not.Null);
            Assert.That(host.IsReady, Is.True);
            Assert.That(host.Resources.MaterialRead, Is.Not.Null);
            host.Clock.SetRunning(false);
            long before = host.Clock.TickCount;
            host.Clock.RequestStep();
            yield return null;
            Assert.That(host.Clock.TickCount, Is.EqualTo(before + 1));
        }

        [UnityTest]
        public IEnumerator RegenerationResetsClockAndPreservesGpuResources()
        {
            SimulationHost host = Object.FindFirstObjectByType<SimulationHost>();
            if (host == null)
            {
                SceneManager.LoadScene("Terrarium");
                yield return null;
                host = Object.FindFirstObjectByType<SimulationHost>();
            }
            Assert.That(host, Is.Not.Null);
            host.Regenerate();
            Assert.That(host.Clock.TickCount, Is.EqualTo(0));
            Assert.That(host.Resources.IsCreated, Is.True);
        }
    }
}
