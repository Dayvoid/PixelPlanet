using GeneSys.Configuration;
using GeneSys.Simulation;
using GeneSys.Simulation.Topology;
using GeneSys.UI;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class ProbeTests
    {
        [Test]
        public void ProbeDefaultsMatchGlowOrbit()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(config.probeOrbitRadius, Is.EqualTo(1.28f).Within(0.001f));
            Assert.That(config.probeSpriteScale, Is.EqualTo(0.08f).Within(0.001f));
            Assert.That(config.probeSpriteRotationOffset, Is.EqualTo(0f).Within(0.001f));
            Assert.That(config.probeOrbitPeriodSeconds, Is.EqualTo(180f).Within(0.001f));
            Assert.That(config.probeVaporRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeWaterRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeHeatRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeCoolRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeDepositRadius, Is.EqualTo(4));
            Assert.That(config.probeLeadDegrees, Is.EqualTo(2f).Within(0.001f));
            Assert.That(config.probeFollowZoom, Is.EqualTo(2.5f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RestoreDefaultsResetsProbeFields()
        {
            var config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.probeOrbitRadius = 2f;
            config.probeSpriteScale = 0.5f;
            config.probeSpriteRotationOffset = 45f;
            config.probeOrbitPeriodSeconds = 12f;
            config.probeVaporRate = 8f;
            config.probeWaterRate = 8f;
            config.probeHeatRate = 8f;
            config.probeCoolRate = 8f;
            config.probeDepositRadius = 32;
            config.probeLeadDegrees = 20f;
            config.probeFollowZoom = 8f;
            config.RestoreDefaults();
            Assert.That(config.probeOrbitRadius, Is.EqualTo(1.28f).Within(0.001f));
            Assert.That(config.probeSpriteScale, Is.EqualTo(0.08f).Within(0.001f));
            Assert.That(config.probeSpriteRotationOffset, Is.EqualTo(0f).Within(0.001f));
            Assert.That(config.probeOrbitPeriodSeconds, Is.EqualTo(180f).Within(0.001f));
            Assert.That(config.probeVaporRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeWaterRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeHeatRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeCoolRate, Is.EqualTo(1f).Within(0.001f));
            Assert.That(config.probeDepositRadius, Is.EqualTo(4));
            Assert.That(config.probeLeadDegrees, Is.EqualTo(2f).Within(0.001f));
            Assert.That(config.probeFollowZoom, Is.EqualTo(2.5f).Within(0.001f));
            Object.DestroyImmediate(config);
        }

        [Test]
        public void ProbeDirectionIsOppositeTheSun()
        {
            Vector2 zero = ProbeController.ProbeDirectionFromAngle01(0f);
            Assert.That(zero.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(zero.y, Is.EqualTo(0f).Within(0.001f));

            Vector2 quarter = ProbeController.ProbeDirectionFromAngle01(0.25f);
            Assert.That(quarter.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(quarter.y, Is.EqualTo(-1f).Within(0.001f));

            Vector2 wrapped = ProbeController.ProbeDirectionFromAngle01(1.25f);
            Assert.That(wrapped.x, Is.EqualTo(quarter.x).Within(0.001f));
            Assert.That(wrapped.y, Is.EqualTo(quarter.y).Within(0.001f));
        }

        [Test]
        public void AimCellUsesOuterRingAndLead()
        {
            PolarGridDefinition grid = PolarGridDefinition.Validation;
            var probe = new GameObject("Probe").AddComponent<ProbeController>();
            Vector2Int cell = probe.AimCell(grid);
            Assert.That(cell.y, Is.EqualTo(grid.radialResolution - 1));

            float leadTurns = ProbeController.DefaultLeadDegrees / 360f;
            float theta01 = Mathf.Repeat(-probe.ProbeAngle01 - leadTurns, 1f);
            int expectedAngular = grid.WrapTheta(Mathf.FloorToInt(theta01 * grid.angularResolution));
            Assert.That(cell.x, Is.EqualTo(expectedAngular));
            Object.DestroyImmediate(probe.gameObject);
        }

        [Test]
        public void SpriteRotationAddsOffsetToClockwiseTangent()
        {
            float baseline = ProbeController.SpriteRotationZ(0.25f, 0f);
            Assert.That(ProbeController.SpriteRotationZ(0.25f, 45f), Is.EqualTo(baseline + 45f).Within(0.001f));
            Assert.That(ProbeController.SpriteRotationZ(0.25f, -30f), Is.EqualTo(baseline - 30f).Within(0.001f));
        }

        [Test]
        public void FollowLockRotationPinsProbeAtTop()
        {
            Assert.That(ProbeController.FollowLockRotationZ(0f), Is.EqualTo(90f).Within(0.001f));
            Assert.That(ProbeController.FollowLockRotationZ(0.25f), Is.EqualTo(180f).Within(0.001f));

            float[] samples = { 0f, 0.125f, 0.25f, 0.5f, 0.75f, 1.25f };
            foreach (float angle01 in samples)
            {
                Vector2 local = ProbeController.ProbeDirectionFromAngle01(angle01);
                float z = ProbeController.FollowLockRotationZ(angle01) * Mathf.Deg2Rad;
                float cos = Mathf.Cos(z);
                float sin = Mathf.Sin(z);
                Vector2 world = new(cos * local.x - sin * local.y, sin * local.x + cos * local.y);
                Assert.That(world.x, Is.EqualTo(0f).Within(0.001f), $"angle {angle01}");
                Assert.That(world.y, Is.EqualTo(1f).Within(0.001f), $"angle {angle01}");
            }
        }

        [Test]
        public void UiToolkitScreenOriginFlipsYFromInputSystem()
        {
            Vector2 flipped = SimulationUIController.ToUiToolkitScreenPosition(new Vector2(100f, 40f), 1080f);
            Assert.That(flipped.x, Is.EqualTo(100f).Within(0.001f));
            Assert.That(flipped.y, Is.EqualTo(1040f).Within(0.001f));
        }

        [Test]
        public void ProbeHudInteractionBlocksWorldBrush()
        {
            Assert.That(SimulationUIController.ShouldBlockWorldBrush(false, ProbeAction.None), Is.False);
            Assert.That(SimulationUIController.ShouldBlockWorldBrush(true, ProbeAction.None), Is.True);
            Assert.That(SimulationUIController.ShouldBlockWorldBrush(false, ProbeAction.Heat), Is.True);
        }
    }
}
