using System.Collections.Generic;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class SimulationClockTests
    {
        [Test]
        public void NormalSpeedExecutesFixedDeltaSteps()
        {
            var clock = new SimulationClock();
            clock.SetSpeed(1f);
            var deltas = new List<float>();

            // Simulate 1 second at 60 FPS (60 frames of 0.0166667s) with 20 ticksPerSecond
            float frameDt = 1f / 60f;
            for (int i = 0; i < 60; i++)
            {
                clock.Advance(frameDt, 20f, dt => deltas.Add(dt));
            }

            Assert.That(deltas.Count, Is.EqualTo(20));
            Assert.That(clock.TickCount, Is.EqualTo(20));
            foreach (float dt in deltas)
            {
                Assert.That(dt, Is.EqualTo(0.05f).Within(1e-5f));
            }
        }

        [Test]
        public void EightTimesSpeedDispatchesMultipleFixedDeltaStepsWithoutDilatingDeltaTime()
        {
            var clock = new SimulationClock();
            clock.SetSpeed(8f);
            var deltas = new List<float>();

            // Simulate 1 second at 60 FPS (60 frames of 0.0166667s) with 20 ticksPerSecond
            float frameDt = 1f / 60f;
            for (int i = 0; i < 60; i++)
            {
                clock.Advance(frameDt, 20f, dt => deltas.Add(dt));
            }

            // 8x speed over 1 real second = 160 physical ticks
            Assert.That(deltas.Count, Is.EqualTo(160));
            Assert.That(clock.TickCount, Is.EqualTo(160));
            // Crucial: Every single step MUST be exactly 0.05s, not dilated to 0.40s
            foreach (float dt in deltas)
            {
                Assert.That(dt, Is.EqualTo(0.05f).Within(1e-5f));
            }
        }

        [Test]
        public void RequestStepRunsSingleFixedDeltaStep()
        {
            var clock = new SimulationClock();
            clock.SetSpeed(8f);
            clock.SetRunning(false);
            clock.RequestStep();

            float dispatchedDt = 0f;
            int count = clock.Advance(0.1f, 20f, dt => dispatchedDt = dt);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(clock.TickCount, Is.EqualTo(1));
            Assert.That(dispatchedDt, Is.EqualTo(0.05f).Within(1e-5f));
        }
    }
}
