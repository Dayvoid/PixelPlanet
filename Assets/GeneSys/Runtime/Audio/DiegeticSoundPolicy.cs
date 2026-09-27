using UnityEngine;

namespace GeneSys.Audio
{
    public enum OneShotKind
    {
        Thunder = 0,
        Quake = 1,
        Eruption = 2
    }

    /// <summary>
    /// Keep in sync with the slot order in ViewAmbience.compute.
    /// </summary>
    public static class ViewAmbienceLayout
    {
        public const int Samples = 0;
        public const int AtmosphereSamples = 1;
        public const int WindMilli = 2;
        public const int RainMilli = 3;
        public const int FireMilli = 4;
        public const int TephraCount = 5;
        public const int SurfaceSamples = 6;
        public const int SurfaceWaterCount = 7;
        public const int MagmaCount = 8;
        public const int Slots = 9;
    }

    public struct ViewAmbienceSample
    {
        public int Samples;
        public float MeanWind;
        public float MeanRain;
        public float MeanFire;
        public float TephraFraction;
        public float SurfaceWaterFraction;
        public float MagmaFraction;
    }

    public struct BedTargets
    {
        public float Wind;
        public float Rain;
        public float Fire;
        public float Quake;
        public float Eruption;
        public float Hydrothermal;

        public void Scale(float gain)
        {
            Wind *= gain;
            Rain *= gain;
            Fire *= gain;
            Quake *= gain;
            Eruption *= gain;
            Hydrothermal *= gain;
        }
    }

    public struct PunctualCue
    {
        public float Intensity;
        public float ScreenX01;
        public float ScreenDistance01;
        public float ScreenFraction;
        public bool InView;
    }

    public struct OneShotSlotState
    {
        public bool Playing;
        public float Intensity;
        public float Pan;
        public float Delay;
        public float Age;
        public float Duration;

        public bool IsBusy => Playing && (Delay > 0f || Age < Duration);

        public void Advance(float dt)
        {
            if (!Playing || dt <= 0f) return;
            if (Delay > 0f)
            {
                Delay -= dt;
                if (Delay > 0f) return;
                dt = -Delay;
                Delay = 0f;
            }

            Age += dt;
            if (Age >= Duration)
                Playing = false;
        }

        /// <returns>True when an in-progress voice was raised instead of restarted.</returns>
        public bool Receive(float intensity, float pan, float delay, float duration)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= 0f) return false;
            if (IsBusy)
            {
                if (intensity >= Intensity)
                    Pan = Mathf.Clamp(pan, -1f, 1f);
                Intensity = Mathf.Max(Intensity, intensity);
                return true;
            }

            Playing = true;
            Intensity = intensity;
            Pan = Mathf.Clamp(pan, -1f, 1f);
            Delay = Mathf.Max(0f, delay);
            Age = 0f;
            Duration = Mathf.Max(0.05f, duration);
            return false;
        }
    }

    public sealed class BedSlew
    {
        public BedTargets Current { get; private set; }

        public void Advance(BedTargets target, float dt, float seconds = DiegeticSoundPolicy.SlewSeconds)
        {
            float step = dt / Mathf.Max(0.01f, seconds);
            BedTargets current = Current;
            current.Wind = Mathf.MoveTowards(current.Wind, target.Wind, step);
            current.Rain = Mathf.MoveTowards(current.Rain, target.Rain, step);
            current.Fire = Mathf.MoveTowards(current.Fire, target.Fire, step);
            current.Quake = Mathf.MoveTowards(current.Quake, target.Quake, step);
            current.Eruption = Mathf.MoveTowards(current.Eruption, target.Eruption, step);
            current.Hydrothermal = Mathf.MoveTowards(current.Hydrothermal, target.Hydrothermal, step);
            Current = current;
        }

        public void Clear() => Current = default;
    }

    public static class DiegeticSoundPolicy
    {
        public const float MinOrtho = 0.75f;
        public const float MaxOrtho = 20f;
        public const float SilenceFloor = 0.04f;
        public const float SlewSeconds = 0.5f;
        public const float CoalesceSeconds = 0.15f;
        public const float ThunderSeconds = 0.75f;
        public const float QuakeSeconds = 0.45f;
        public const float EruptionSeconds = 0.5f;
        public const float ActiveEvent = 0.05f;
        public const float WindFullScale = 0.75f;
        public const float WindFloor = 0.06f;
        public const float RainFullScale = 0.35f;
        public const float FireFullScale = 0.2f;
        public const int MaxBeds = 3;

        public static float Zoom01(float orthoSize) =>
            Mathf.InverseLerp(MinOrtho, MaxOrtho, orthoSize);

        public static float ZoomGain(float orthoSize) =>
            Mathf.Lerp(1f, 0.28f, Zoom01(orthoSize));

        public static float StereoWidth(float orthoSize) =>
            1f - Zoom01(orthoSize);

        public static float LowpassMix(float orthoSize) =>
            Zoom01(orthoSize);

        public static bool SuppressOneShots(float simSpeed) => simSpeed > 1.01f;

        public static float MasterGain(float volume, bool userMuted, bool planetHidden)
        {
            if (userMuted || planetHidden) return 0f;
            return Mathf.Clamp01(volume);
        }

        public static float NormalizeWind(float meanFlow)
        {
            if (meanFlow < WindFloor) return 0f;
            return Mathf.Clamp01((meanFlow - WindFloor) / (WindFullScale - WindFloor));
        }

        public static float NormalizeRain(float mean) =>
            Mathf.Clamp01(mean / RainFullScale);

        public static float NormalizeFire(float mean) =>
            Mathf.Clamp01(mean / FireFullScale);

        public static float NormalizeCoverage(float fraction) =>
            Mathf.Clamp01(fraction / 0.05f);

        public static float EruptionColumn(float eventIntensity, float magmaFraction, float tephraFraction)
        {
            bool visible = magmaFraction > 0.0005f || tephraFraction > 0.0005f;
            return visible ? Mathf.Clamp01(eventIntensity) : 0f;
        }

        public static float ViewportDistance01(float x, float y)
        {
            float dx = x - 0.5f;
            float dy = y - 0.5f;
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 1.41421356f);
        }

        public static bool PassesZoomGate(float orthoSize, float screenDistance01, float screenFraction)
        {
            float zoom = Zoom01(orthoSize);
            float centerLimit = Mathf.Lerp(0.95f, 0.18f, zoom);
            float coverLimit = Mathf.Lerp(0f, 0.08f, zoom);
            return screenDistance01 <= centerLimit || screenFraction >= coverLimit;
        }

        public static float ThunderDelay(float screenDistance01) =>
            Mathf.Lerp(0.04f, 0.7f, Mathf.Clamp01(screenDistance01));

        public static float ScreenPan(float screenX01, float stereoWidth)
        {
            float side = Mathf.Clamp(screenX01, 0f, 1f) * 2f - 1f;
            return side * Mathf.Clamp01(stereoWidth);
        }

        public static float Duration(OneShotKind kind)
        {
            switch (kind)
            {
                case OneShotKind.Quake: return QuakeSeconds;
                case OneShotKind.Eruption: return EruptionSeconds;
                default: return ThunderSeconds;
            }
        }

        public static BedTargets SelectBeds(
            float wind,
            float rain,
            float fire,
            float quake,
            float eruption,
            float hydrothermal,
            float tephra)
        {
            wind = Floor(wind);
            if (wind > 0f)
                wind = Mathf.Clamp01(wind + Mathf.Clamp01(tephra) * 0.35f);

            rain = Floor(rain);
            fire = Floor(fire);
            float quakeV = Floor(quake);
            float eruptV = Floor(eruption);
            float hydroV = Floor(hydrothermal);

            float geo = Mathf.Max(quakeV, eruptV);
            int geoId = eruptV >= quakeV ? 3 : 2;
            if (geo <= 0f)
            {
                geo = hydroV;
                geoId = 4;
            }

            var result = new BedTargets { Wind = wind };
            float rainSlot = rain;
            float fireSlot = fire;
            float geoSlot = geo;
            int budget = wind > 0f ? MaxBeds - 1 : MaxBeds;
            for (int n = 0; n < budget; n++)
            {
                int best = -1;
                float bestValue = 0f;
                if (rainSlot > bestValue) { bestValue = rainSlot; best = 0; }
                if (fireSlot > bestValue) { bestValue = fireSlot; best = 1; }
                if (geoSlot > bestValue) { bestValue = geoSlot; best = 2; }
                if (best < 0 || bestValue <= 0f) break;
                if (best == 0)
                {
                    result.Rain = rainSlot;
                    rainSlot = 0f;
                }
                else if (best == 1)
                {
                    result.Fire = fireSlot;
                    fireSlot = 0f;
                }
                else
                {
                    if (geoId == 3) result.Eruption = geoSlot;
                    else if (geoId == 4) result.Hydrothermal = geoSlot;
                    else result.Quake = geoSlot;
                    geoSlot = 0f;
                }
            }

            return result;
        }

        public static bool TryCoalescePunctual(
            PunctualCue[] cues,
            float orthoSize,
            out float intensity,
            out float pan,
            out float delay)
        {
            intensity = 0f;
            pan = 0f;
            delay = 0f;
            if (cues == null || cues.Length == 0) return false;

            float stereo = StereoWidth(orthoSize);
            float best = 0f;
            float nearest = 2f;
            int count = 0;
            float bestPan = 0f;
            for (int i = 0; i < cues.Length; i++)
            {
                PunctualCue cue = cues[i];
                if (!cue.InView) continue;
                if (!PassesZoomGate(orthoSize, cue.ScreenDistance01, cue.ScreenFraction)) continue;
                count++;
                if (cue.ScreenDistance01 < nearest)
                    nearest = cue.ScreenDistance01;
                if (cue.Intensity >= best)
                {
                    best = cue.Intensity;
                    bestPan = ScreenPan(cue.ScreenX01, stereo);
                }
            }

            if (count == 0 || best <= 0f) return false;
            intensity = Mathf.Clamp01(best + 0.12f * (count - 1));
            pan = bestPan;
            delay = ThunderDelay(nearest);
            return true;
        }

        public static ViewAmbienceSample Decode(uint[] accum)
        {
            if (accum == null || accum.Length < ViewAmbienceLayout.Slots || accum[ViewAmbienceLayout.Samples] == 0)
                return default;

            float samples = accum[ViewAmbienceLayout.Samples];
            float atmo = accum[ViewAmbienceLayout.AtmosphereSamples];
            return new ViewAmbienceSample
            {
                Samples = (int)accum[ViewAmbienceLayout.Samples],
                MeanWind = atmo > 0f ? accum[ViewAmbienceLayout.WindMilli] / 1000f / atmo : 0f,
                MeanRain = atmo > 0f ? accum[ViewAmbienceLayout.RainMilli] / 1000f / atmo : 0f,
                MeanFire = accum[ViewAmbienceLayout.FireMilli] / 1000f / samples,
                TephraFraction = atmo > 0f ? accum[ViewAmbienceLayout.TephraCount] / atmo : 0f,
                SurfaceWaterFraction = accum[ViewAmbienceLayout.SurfaceSamples] > 0
                    ? accum[ViewAmbienceLayout.SurfaceWaterCount] / (float)accum[ViewAmbienceLayout.SurfaceSamples]
                    : 0f,
                MagmaFraction = accum[ViewAmbienceLayout.MagmaCount] / samples
            };
        }

        private static float Floor(float value) => value < SilenceFloor ? 0f : value;
    }
}
