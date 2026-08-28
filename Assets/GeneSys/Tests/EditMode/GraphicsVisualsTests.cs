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
    }
}
