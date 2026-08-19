using GeneSys.Configuration;
using GeneSys.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class GraphicsVisualsTests
    {
        [Test]
        public void GraphicsDefaultsAreConfigured()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.enableStarfield, Is.EqualTo(1));
            Assert.That(config.starCount, Is.InRange(32, 512));
            Assert.That(config.starfieldStrength, Is.GreaterThan(0f));
            Assert.That(config.starTwinkleStrength, Is.GreaterThan(0f));
            Assert.That(config.enableNebula, Is.EqualTo(1));
            Assert.That(config.nebulaCount, Is.InRange(4, 48));
            Assert.That(config.nebulaStrength, Is.GreaterThan(0f));
            Assert.That(config.enableAtmosphereGlow, Is.EqualTo(1));
            Assert.That(config.atmosphereGlowStrength, Is.GreaterThan(0f));
            Assert.That(config.enableSolarBody, Is.EqualTo(1));
            Assert.That(config.solarBodyStrength, Is.GreaterThan(0f));
            Assert.That(config.solarCoronaStrength, Is.GreaterThan(0f));
            Assert.That(config.solarOrbitRadius, Is.InRange(0.5f, 2f));
            Assert.That(config.dayNightLightingStrength, Is.GreaterThan(0f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RestoreDefaultsResetsGraphicsFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.enableStarfield = 0;
            config.starCount = 32;
            config.starfieldStrength = 0f;
            config.nebulaStrength = 0f;
            config.atmosphereGlowStrength = 0f;
            config.solarBodyStrength = 0f;
            config.dayNightLightingStrength = 0f;
            config.RestoreDefaults();
            Assert.That(config.enableStarfield, Is.EqualTo(1));
            Assert.That(config.starCount, Is.EqualTo(160));
            Assert.That(config.starfieldStrength, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(config.nebulaStrength, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(config.atmosphereGlowStrength, Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(config.solarBodyStrength, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.dayNightLightingStrength, Is.EqualTo(0.85f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void SolarDirectionMatchesAngleConvention()
        {
            Vector2 zero = TerrariumVisualController.SolarDirectionFromAngle01(0f);
            Assert.That(zero.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(zero.y, Is.EqualTo(0f).Within(0.001f));

            Vector2 quarter = TerrariumVisualController.SolarDirectionFromAngle01(0.25f);
            Assert.That(quarter.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(quarter.y, Is.EqualTo(1f).Within(0.001f));

            Vector2 wrapped = TerrariumVisualController.SolarDirectionFromAngle01(1.25f);
            Assert.That(wrapped.x, Is.EqualTo(quarter.x).Within(0.001f));
            Assert.That(wrapped.y, Is.EqualTo(quarter.y).Within(0.001f));
        }
    }
}
