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
            Assert.That(config.probeEnergyMax, Is.EqualTo(100f).Within(0.001f));
            Assert.That(config.probeEnergyActionDrain, Is.EqualTo(3f).Within(0.001f));
            Assert.That(config.probeEnergyRegenPerSecond, Is.EqualTo(10f).Within(0.001f));
            Assert.That(config.probeLifeSeedIntervalSeconds, Is.EqualTo(3f).Within(0.001f));
            Assert.That(config.probeLifeSeedMinCount, Is.EqualTo(1));
            Assert.That(config.probeLifeSeedMaxCount, Is.EqualTo(5));
            Assert.That(config.probeLifeSeedSporeLoad, Is.EqualTo(0.5f).Within(0.001f));
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
            config.probeEnergyMax = 40f;
            config.probeEnergyActionDrain = 9f;
            config.probeEnergyRegenPerSecond = 1f;
            config.probeLifeSeedIntervalSeconds = 12f;
            config.probeLifeSeedMinCount = 4;
            config.probeLifeSeedMaxCount = 4;
            config.probeLifeSeedSporeLoad = 0.1f;
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
            Assert.That(config.probeEnergyMax, Is.EqualTo(100f).Within(0.001f));
            Assert.That(config.probeEnergyActionDrain, Is.EqualTo(3f).Within(0.001f));
            Assert.That(config.probeEnergyRegenPerSecond, Is.EqualTo(10f).Within(0.001f));
            Assert.That(config.probeLifeSeedIntervalSeconds, Is.EqualTo(3f).Within(0.001f));
            Assert.That(config.probeLifeSeedMinCount, Is.EqualTo(1));
            Assert.That(config.probeLifeSeedMaxCount, Is.EqualTo(5));
            Assert.That(config.probeLifeSeedSporeLoad, Is.EqualTo(0.5f).Within(0.001f));
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

        [Test]
        public void ProbeStartsFullEnergyOnClockwiseFlight()
        {
            var probe = new GameObject("Probe").AddComponent<ProbeController>();
            Assert.That(probe.Energy, Is.EqualTo(ProbeController.DefaultEnergyMax).Within(0.001f));
            Assert.That(probe.EnergyNormalized, Is.EqualTo(1f).Within(0.001f));
            Assert.That(probe.FlightMode, Is.EqualTo(ProbeFlightMode.Clockwise));
            Assert.That(probe.LifeSeedActive, Is.False);
            Object.DestroyImmediate(probe.gameObject);
        }

        [Test]
        public void EnergyDrainsWhileActiveAndClampsAtZero()
        {
            float drained = ProbeController.ApplyEnergyTick(100f, true, true, 3f, 10f, 0.05f, 100f);
            Assert.That(drained, Is.EqualTo(97f).Within(0.001f));

            float empty = ProbeController.ApplyEnergyTick(2f, true, true, 3f, 10f, 0.05f, 100f);
            Assert.That(empty, Is.EqualTo(0f).Within(0.001f));

            float stillEmpty = ProbeController.ApplyEnergyTick(0f, true, true, 3f, 10f, 0.05f, 100f);
            Assert.That(stillEmpty, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void EnergyRegensWhenIdleUnlessStopped()
        {
            float regen = ProbeController.ApplyEnergyTick(50f, false, true, 3f, 10f, 0.05f, 100f);
            Assert.That(regen, Is.EqualTo(50.5f).Within(0.001f));

            float stopped = ProbeController.ApplyEnergyTick(50f, false, false, 3f, 10f, 0.05f, 100f);
            Assert.That(stopped, Is.EqualTo(50f).Within(0.001f));

            float capped = ProbeController.ApplyEnergyTick(99.9f, false, true, 3f, 10f, 1f, 100f);
            Assert.That(capped, Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void FlightModeAdvancesReversesAndHoldsOrbitPhase()
        {
            const float periodTicks = 3600f;
            float clockwise = ProbeController.AdvanceOrbitAngle01(0f, ProbeFlightMode.Clockwise, 1, periodTicks);
            Assert.That(clockwise, Is.EqualTo(1f / periodTicks).Within(1e-6f));

            float reversed = ProbeController.AdvanceOrbitAngle01(clockwise, ProbeFlightMode.Counterclockwise, 1, periodTicks);
            Assert.That(reversed, Is.EqualTo(0f).Within(1e-6f));

            float held = ProbeController.AdvanceOrbitAngle01(0.4f, ProbeFlightMode.Stopped, 10, periodTicks);
            Assert.That(held, Is.EqualTo(0.4f).Within(1e-6f));

            float wrapped = ProbeController.AdvanceOrbitAngle01(0.99f, ProbeFlightMode.Clockwise, 1, 10f);
            Assert.That(wrapped, Is.EqualTo(0.09f).Within(1e-5f));
        }

        [Test]
        public void ReverseFlightFlipsLeadAndSpriteFacing()
        {
            Assert.That(ProbeController.AimLeadSign(ProbeFlightMode.Clockwise), Is.EqualTo(1f));
            Assert.That(ProbeController.AimLeadSign(ProbeFlightMode.Stopped), Is.EqualTo(1f));
            Assert.That(ProbeController.AimLeadSign(ProbeFlightMode.Counterclockwise), Is.EqualTo(-1f));

            float clockwise = ProbeController.SpriteRotationZ(0.25f, 0f);
            Assert.That(ProbeController.SpriteRotationZ(0.25f, 0f, true), Is.EqualTo(clockwise + 180f).Within(0.001f));

            PolarGridDefinition grid = PolarGridDefinition.Validation;
            var probe = new GameObject("Probe").AddComponent<ProbeController>();
            probe.SetFlightMode(ProbeFlightMode.Counterclockwise);
            Vector2Int cell = probe.AimCell(grid);
            float leadTurns = ProbeController.AimLeadSign(probe.TravelMode) * ProbeController.DefaultLeadDegrees / 360f;
            float theta01 = Mathf.Repeat(-probe.ProbeAngle01 - leadTurns, 1f);
            int expectedAngular = grid.WrapTheta(Mathf.FloorToInt(theta01 * grid.angularResolution));
            Assert.That(cell.x, Is.EqualTo(expectedAngular));
            Assert.That(probe.TravelMode, Is.EqualTo(ProbeFlightMode.Counterclockwise));

            probe.SetFlightMode(ProbeFlightMode.Stopped);
            Assert.That(probe.FlightMode, Is.EqualTo(ProbeFlightMode.Stopped));
            Assert.That(probe.TravelMode, Is.EqualTo(ProbeFlightMode.Counterclockwise));
            Object.DestroyImmediate(probe.gameObject);
        }

        [Test]
        public void LifeSeedBurstMixesFloraAndFaunaWithoutClumpingCounts()
        {
            for (int i = 0; i < 64; i++)
            {
                int count = ProbeController.LifeSeedBurstCount(i, 1, 5);
                Assert.That(count, Is.InRange(1, 5));
            }

            bool sawFlora = false;
            bool sawFauna = false;
            for (int i = 0; i < 32; i++)
            {
                if (ProbeController.LifeSeedSpawnsFlora(i, 1)) sawFlora = true;
                else sawFauna = true;
            }
            Assert.That(sawFlora, Is.True);
            Assert.That(sawFauna, Is.True);
        }
    }
}
