using System.IO;
using GeneSys.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class GraphicsVisualsTests
    {
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

        [Test]
        public void SolarInsolationIsLinearFromSubsolarToTerminators()
        {
            const float sun = 0.1f;
            Assert.That(TerrariumVisualController.SolarInsolation(sun, sun), Is.EqualTo(1f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun - 0.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.25f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun - 0.25f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 0.5f, sun), Is.EqualTo(0f).Within(0.001f));
            Assert.That(TerrariumVisualController.SolarInsolation(sun + 1.125f, sun), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void SharedSolarInsolationHelperIsWiredThroughSimAndDisplay()
        {
            string helper = File.ReadAllText("Assets/GeneSys/Shaders/Simulation/Common/SolarInsolation.hlsl");
            Assert.That(helper.Contains("float SolarInsolation(float theta01, float solarAngle01)"));
            Assert.That(helper.Contains("saturate(1.0 - dist * 4.0)"));

            string weather = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Weather.compute");
            Assert.That(weather.Contains("SolarInsolation.hlsl"));
            Assert.That(weather.Contains("SolarInsolation(theta01, _WeatherC.y)"));

            string flora = File.ReadAllText("Assets/GeneSys/Compute/Simulation/Flora.compute");
            Assert.That(flora.Contains("SolarInsolation.hlsl"));
            Assert.That(flora.Contains("SolarInsolation(theta01, _WeatherC.y)"));

            string display = File.ReadAllText("Assets/GeneSys/Shaders/Rendering/PlanetoidDisplay.shader");
            Assert.That(display.Contains("SolarInsolation.hlsl"));
            Assert.That(display.Contains("SolarInsolation(theta01, _SolarAngle01)"));
            Assert.That(display.Contains("float3(0.62, 0.70, 0.95)"));
        }
    }
}
