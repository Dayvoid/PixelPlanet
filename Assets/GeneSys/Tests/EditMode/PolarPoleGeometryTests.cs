using GeneSys.Simulation.Topology;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class PolarPoleGeometryTests
    {
        [Test]
        public void PoleAngleMatchesWorldgenFormulaAndAntipode()
        {
            const int seed = 12345;
            float pole = PolarPoleGeometry.PoleAngle01(seed);
            Assert.That(pole, Is.InRange(0f, 1f));
            float antipode = pole + 0.5f;
            if (antipode >= 1f) antipode -= 1f;
            Assert.That(PolarPoleGeometry.AngularDistance01(pole, antipode), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PolarPoleGeometry.PoleAngle01(seed), Is.EqualTo(pole));
        }

        [Test]
        public void NearestPoleStepIsZeroOnThePoleColumn()
        {
            const int width = 256;
            const int seed = 2026;
            int zeros = 0;
            for (int theta = 0; theta < width; theta++)
            {
                if (PolarPoleGeometry.NearestPoleStep(theta, width, seed) == 0)
                    zeros++;
            }
            Assert.That(zeros, Is.GreaterThan(0));
        }

        [Test]
        public void NearestPoleStepIsSymmetricAboutBothCaps()
        {
            const int width = 256;
            const int seed = 2026;
            for (int theta = 0; theta < width; theta++)
            {
                if (PolarPoleGeometry.NearestPoleStep(theta, width, seed) != 0) continue;
                int plus = (theta + 1) % width;
                int minus = (theta - 1 + width) % width;
                Assert.That(PolarPoleGeometry.NearestPoleStep(plus, width, seed), Is.EqualTo(-1), $"theta {theta}");
                Assert.That(PolarPoleGeometry.NearestPoleStep(minus, width, seed), Is.EqualTo(1), $"theta {theta}");
            }
        }

        [Test]
        public void NearestPoleStepTakesTheShortWayIncludingTheSeam()
        {
            const int width = 256;
            const int seed = 9829;
            float pole = PolarPoleGeometry.PoleAngle01(seed);
            float antipode = pole + 0.5f;
            if (antipode >= 1f) antipode -= 1f;

            for (int theta = 0; theta < width; theta++)
            {
                int step = PolarPoleGeometry.NearestPoleStep(theta, width, seed);
                float angular = (theta + 0.5f) / width;
                float nearer = PolarPoleGeometry.AngularDistance01(angular, pole)
                    <= PolarPoleGeometry.AngularDistance01(angular, antipode)
                    ? pole
                    : antipode;
                float before = PolarPoleGeometry.AngularDistance01(angular, nearer);
                if (step == 0)
                {
                    Assert.That(before * width, Is.LessThan(0.5f + 1e-4f), $"theta {theta}");
                    continue;
                }

                int next = (theta + step + width) % width;
                float after = PolarPoleGeometry.AngularDistance01((next + 0.5f) / width, nearer);
                Assert.That(after, Is.LessThan(before), $"theta {theta} step {step} across seam={theta == 0 || theta == width - 1}");
            }
        }
    }
}
