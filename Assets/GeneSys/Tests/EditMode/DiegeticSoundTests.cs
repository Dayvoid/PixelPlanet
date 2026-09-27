using GeneSys.Audio;
using NUnit.Framework;

namespace GeneSys.Tests
{
    public sealed class DiegeticSoundTests
    {
        [Test]
        public void SilenceFloorDropsQuietBeds()
        {
            BedTargets beds = DiegeticSoundPolicy.SelectBeds(0.03f, 0.02f, 0.01f, 0.03f, 0f, 0f, 0f);
            Assert.That(beds.Wind, Is.EqualTo(0f));
            Assert.That(beds.Rain, Is.EqualTo(0f));
            Assert.That(beds.Fire, Is.EqualTo(0f));
            Assert.That(beds.Quake, Is.EqualTo(0f));
        }

        [Test]
        public void WindAndTwoStrongestBedsStay()
        {
            BedTargets beds = DiegeticSoundPolicy.SelectBeds(0.8f, 0.5f, 0.4f, 0.9f, 0.2f, 0.7f, 0f);
            Assert.That(beds.Wind, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(beds.Quake, Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(beds.Rain, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(beds.Fire, Is.EqualTo(0f));
            Assert.That(beds.Eruption, Is.EqualTo(0f));
            Assert.That(beds.Hydrothermal, Is.EqualTo(0f));
        }

        [Test]
        public void HydrothermalLosesToQuakeOrEruption()
        {
            BedTargets beds = DiegeticSoundPolicy.SelectBeds(0f, 0f, 0f, 0.1f, 0f, 0.9f, 0f);
            Assert.That(beds.Quake, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(beds.Hydrothermal, Is.EqualTo(0f));
            Assert.That(beds.Eruption, Is.EqualTo(0f));
        }

        [Test]
        public void TephraDirtiensWindWithoutAFifthBed()
        {
            BedTargets beds = DiegeticSoundPolicy.SelectBeds(0.2f, 0f, 0f, 0f, 0f, 0f, 1f);
            Assert.That(beds.Wind, Is.EqualTo(0.55f).Within(1e-4f));
            Assert.That(beds.Rain, Is.EqualTo(0f));
            Assert.That(beds.Fire, Is.EqualTo(0f));
            Assert.That(beds.Quake, Is.EqualTo(0f));
        }

        [Test]
        public void TephraDoesNotInventWind()
        {
            BedTargets silent = DiegeticSoundPolicy.SelectBeds(0f, 0f, 0f, 0f, 0f, 0f, 1f);
            Assert.That(silent.Wind, Is.EqualTo(0f));
        }

        [Test]
        public void ZoomGateKeepsNearStrikesAndDropsLimbCracks()
        {
            Assert.That(DiegeticSoundPolicy.PassesZoomGate(0.75f, 0.9f, 0f), Is.True);
            Assert.That(DiegeticSoundPolicy.PassesZoomGate(20f, 0.5f, 0f), Is.False);
            Assert.That(DiegeticSoundPolicy.PassesZoomGate(20f, 0.1f, 0f), Is.True);
            Assert.That(DiegeticSoundPolicy.PassesZoomGate(20f, 0.9f, 0.2f), Is.True);
            Assert.That(DiegeticSoundPolicy.StereoWidth(0.75f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.StereoWidth(20f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.ZoomGain(0.75f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.ZoomGain(20f), Is.EqualTo(0.28f).Within(1e-4f));
        }

        [Test]
        public void ThunderDelayGrowsWithScreenDistance()
        {
            Assert.That(DiegeticSoundPolicy.ThunderDelay(0f), Is.EqualTo(0.04f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.ThunderDelay(1f), Is.EqualTo(0.7f).Within(1e-4f));
        }

        [Test]
        public void CoalesceKeepsTheNearerLouderStrike()
        {
            var close = new[]
            {
                new PunctualCue { InView = true, Intensity = 0.4f, ScreenDistance01 = 0.8f, ScreenX01 = 0.2f, ScreenFraction = 0f },
                new PunctualCue { InView = true, Intensity = 0.5f, ScreenDistance01 = 0.2f, ScreenX01 = 0.8f, ScreenFraction = 0f }
            };
            Assert.That(DiegeticSoundPolicy.TryCoalescePunctual(close, 0.75f, out float intensity, out float pan, out float delay), Is.True);
            Assert.That(intensity, Is.EqualTo(0.62f).Within(1e-4f));
            Assert.That(pan, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(delay, Is.EqualTo(DiegeticSoundPolicy.ThunderDelay(0.2f)).Within(1e-4f));

            var globe = new[]
            {
                new PunctualCue { InView = true, Intensity = 0.9f, ScreenDistance01 = 0.5f, ScreenX01 = 0.1f, ScreenFraction = 0f },
                new PunctualCue { InView = true, Intensity = 0.4f, ScreenDistance01 = 0.05f, ScreenX01 = 0.5f, ScreenFraction = 0f }
            };
            Assert.That(DiegeticSoundPolicy.TryCoalescePunctual(globe, 20f, out float farIntensity, out float farPan, out float farDelay), Is.True);
            Assert.That(farIntensity, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(farPan, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(farDelay, Is.EqualTo(DiegeticSoundPolicy.ThunderDelay(0.05f)).Within(1e-4f));
        }

        [Test]
        public void OneShotSlotRaisesInsteadOfStacking()
        {
            var slot = new OneShotSlotState();
            Assert.That(slot.Receive(0.4f, -0.2f, 0.1f, 0.75f), Is.False);
            Assert.That(slot.Receive(0.7f, 0.5f, 0f, 0.75f), Is.True);
            Assert.That(slot.Intensity, Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(slot.Pan, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(slot.IsBusy, Is.True);

            slot.Advance(0.1f + 0.75f + 0.01f);
            Assert.That(slot.IsBusy, Is.False);
            Assert.That(slot.Receive(0.3f, 0f, 0f, 0.45f), Is.False);
            Assert.That(slot.Intensity, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void MuteAndHiddenPlanetSilenceTheMaster()
        {
            Assert.That(DiegeticSoundPolicy.MasterGain(0.7f, true, false), Is.EqualTo(0f));
            Assert.That(DiegeticSoundPolicy.MasterGain(0.7f, false, true), Is.EqualTo(0f));
            Assert.That(DiegeticSoundPolicy.MasterGain(0.7f, false, false), Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.SuppressOneShots(1f), Is.False);
            Assert.That(DiegeticSoundPolicy.SuppressOneShots(5f), Is.True);
        }

        [Test]
        public void BedsSlewOverHalfASecond()
        {
            var slew = new BedSlew();
            var target = new BedTargets { Wind = 1f };
            slew.Advance(target, 0.25f);
            Assert.That(slew.Current.Wind, Is.EqualTo(0.5f).Within(1e-4f));
            slew.Advance(target, 0.25f);
            Assert.That(slew.Current.Wind, Is.EqualTo(1f).Within(1e-4f));
            slew.Advance(target, 0.25f);
            Assert.That(slew.Current.Wind, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EruptionColumnNeedsMagmaOrTephraInView()
        {
            Assert.That(DiegeticSoundPolicy.EruptionColumn(0.8f, 0f, 0f), Is.EqualTo(0f));
            Assert.That(DiegeticSoundPolicy.EruptionColumn(0.8f, 0.01f, 0f), Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(DiegeticSoundPolicy.NormalizeWind(0.02f), Is.EqualTo(0f));
            Assert.That(DiegeticSoundPolicy.NormalizeWind(0.75f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void DecodeAveragesWindOverAtmosphere()
        {
            var accum = new uint[ViewAmbienceLayout.Slots];
            accum[ViewAmbienceLayout.Samples] = 8;
            accum[ViewAmbienceLayout.AtmosphereSamples] = 4;
            accum[ViewAmbienceLayout.WindMilli] = 2000;
            accum[ViewAmbienceLayout.FireMilli] = 4000;
            accum[ViewAmbienceLayout.SurfaceSamples] = 4;
            accum[ViewAmbienceLayout.SurfaceWaterCount] = 2;
            ViewAmbienceSample sample = DiegeticSoundPolicy.Decode(accum);
            Assert.That(sample.Samples, Is.EqualTo(8));
            Assert.That(sample.MeanWind, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(sample.MeanFire, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(sample.SurfaceWaterFraction, Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}
