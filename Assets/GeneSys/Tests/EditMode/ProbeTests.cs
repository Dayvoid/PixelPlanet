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
        public void HudFadeAlphaHoldsThenFadesToZero()
        {
            const float delay = 8f;
            const float duration = 1f;
            Assert.That(SimulationUIController.HudFadeAlpha(0f, delay, duration), Is.EqualTo(1f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay, delay, duration), Is.EqualTo(1f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay + duration * 0.5f, delay, duration), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay + duration, delay, duration), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay + duration + 4f, delay, duration), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void HudFadeAlphaCanSettleAtIdleOpacity()
        {
            const float delay = 8f;
            const float duration = 1f;
            const float idle = 0.1f;
            Assert.That(SimulationUIController.HudFadeAlpha(delay, delay, duration, idle), Is.EqualTo(1f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay + duration * 0.5f, delay, duration, idle), Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(SimulationUIController.HudFadeAlpha(delay + duration, delay, duration, idle), Is.EqualTo(idle).Within(0.001f));
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

            Vector3 clockwiseScale = ProbeController.SpriteLocalScale(0.025f, false);
            Assert.That(clockwiseScale.x, Is.EqualTo(0.025f).Within(0.001f));
            Assert.That(clockwiseScale.y, Is.EqualTo(0.025f).Within(0.001f));

            Vector3 reverseScale = ProbeController.SpriteLocalScale(0.025f, true);
            Assert.That(reverseScale.x, Is.EqualTo(0.025f).Within(0.001f));
            Assert.That(reverseScale.y, Is.EqualTo(-0.025f).Within(0.001f));
            Assert.That(reverseScale.z, Is.EqualTo(clockwiseScale.z).Within(0.001f));

            float heading = ProbeController.SpriteRotationZ(0.25f, 0f);
            Assert.That(ProbeController.SpriteRotationZ(0.25f, 10f), Is.EqualTo(heading + 10f).Within(0.001f));
            Assert.That(ProbeController.SpriteRotationZ(0.25f, 10f, true), Is.EqualTo(heading - 10f).Within(0.001f));
            Assert.That(ProbeController.SpriteRotationZ(0.25f, 0f, true), Is.EqualTo(heading).Within(0.001f));

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
