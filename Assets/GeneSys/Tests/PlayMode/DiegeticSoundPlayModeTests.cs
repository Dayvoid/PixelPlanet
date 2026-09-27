using System.Collections;
using GeneSys.Audio;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GeneSys.Tests
{
    public sealed class DiegeticSoundPlayModeTests
    {
        [UnityTest]
        public IEnumerator DirectorAttachesAndReadsTheView()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Object.FindFirstObjectByType<SimulationHost>() == null)
            {
                SceneManager.LoadScene("Terrarium");
                yield return null;
            }

            SimulationHost host = Object.FindFirstObjectByType<SimulationHost>();
            for (int i = 0; i < 180 && host == null; i++)
            {
                yield return null;
                host = Object.FindFirstObjectByType<SimulationHost>();
            }

            Assert.That(host, Is.Not.Null);
            for (int i = 0; i < 120 && !host.IsReady; i++)
                yield return null;
            Assert.That(host.IsReady, Is.True);
            host.Clock.SetRunning(false);
            LogAssert.ignoreFailingMessages = false;
            DiegeticSoundDirector director = host.EnsureDiegeticSound();
            Assert.That(director, Is.Not.Null);
            for (int i = 0; i < 30 && !director.OutputReady; i++)
                yield return null;
            Assert.That(director.OutputReady, Is.True);
            Assert.That(director.Listener, Is.Not.Null);
            Assert.That(director.Mixer, Is.Not.Null);

            for (int i = 0; i < 360 && (!director.HasAmbienceSample || director.LatestSample.Samples <= 0); i++)
                yield return null;

            Assert.That(director.HasAmbienceSample, Is.True);
            Assert.That(director.LatestSample.Samples, Is.GreaterThan(0));
        }
    }
}
