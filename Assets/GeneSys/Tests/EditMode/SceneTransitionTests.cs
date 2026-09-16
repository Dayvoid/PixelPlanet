using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class SceneTransitionTests
    {
        [Test]
        public void TransitionSceneConfigDefaultsAreOffAtUnitSpeed()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.enableTransitionScenes, Is.EqualTo(0));
            Assert.That(config.transitionSpeed, Is.EqualTo(1f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void TransitionDurationScalesWithSpeedAndClampsTheDivisor()
        {
            Assert.That(SceneTransitionDirector.TransitionDuration(1f, 1.4f), Is.EqualTo(1.4f).Within(0.001f));
            Assert.That(SceneTransitionDirector.TransitionDuration(2f, 1.4f), Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(SceneTransitionDirector.TransitionDuration(0.25f, 1.4f), Is.EqualTo(5.6f).Within(0.001f));
            Assert.That(SceneTransitionDirector.TransitionDuration(0f, 1.4f), Is.EqualTo(5.6f).Within(0.001f));
            Assert.That(SceneTransitionDirector.TransitionDuration(-4f, SceneTransitionDirector.DefaultDepartSeconds),
                Is.EqualTo(SceneTransitionDirector.DefaultDepartSeconds / 0.25f).Within(0.001f));
        }

        [Test]
        public void Smooth01ClampsAndEasesThroughTheMidpoint()
        {
            Assert.That(SceneTransitionDirector.Smooth01(0f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SceneTransitionDirector.Smooth01(1f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(SceneTransitionDirector.Smooth01(0.5f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(SceneTransitionDirector.Smooth01(-1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SceneTransitionDirector.Smooth01(2f), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void LerpPoseAndLerpFloatUseSmooth01()
        {
            Vector3 from = Vector3.zero;
            Vector3 to = new(10f, 0f, 0f);
            Assert.That(SceneTransitionDirector.LerpPose(from, to, 0f), Is.EqualTo(from));
            Assert.That(SceneTransitionDirector.LerpPose(from, to, 1f), Is.EqualTo(to));
            Assert.That(SceneTransitionDirector.LerpPose(from, to, 0.5f).x, Is.EqualTo(5f).Within(0.001f));
            Assert.That(SceneTransitionDirector.LerpFloat(2f, 8f, 0.5f), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void CinematicOrbitPositionScalesRadiusAlongTheHeading()
        {
            Vector3 orbit = ProbeController.CinematicOrbitPosition(0f, 1f, 1f);
            Assert.That(orbit.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(orbit.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(orbit.z, Is.EqualTo(-0.05f).Within(0.001f));

            Vector3 travel = ProbeController.CinematicOrbitPosition(0f, 1f, SceneTransitionDirector.OrbitBreakScale);
            Assert.That(travel.x, Is.EqualTo(0.5f * SceneTransitionDirector.OrbitBreakScale).Within(0.001f));
            Assert.That(travel.y, Is.EqualTo(0f).Within(0.001f));

            Vector3 south = ProbeController.CinematicOrbitPosition(0.25f, 1f, 1f);
            Assert.That(south.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(south.y, Is.EqualTo(-0.5f).Within(0.001f));
        }
    }
}
