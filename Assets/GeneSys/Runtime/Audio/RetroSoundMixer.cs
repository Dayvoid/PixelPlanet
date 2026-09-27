using UnityEngine;

namespace GeneSys.Audio
{
    /// <summary>
    /// One software voice. Beds and one-shots are mixed here so the planet never stacks clips.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class RetroSoundMixer : MonoBehaviour
    {
        private const float BedTrim = 0.16f;
        private const float ShotTrim = 0.42f;
        private const int BitSteps = 64;
        private const int HoldSamples = 3;

        private struct MixSnapshot
        {
            public float Wind;
            public float Rain;
            public float Fire;
            public float Quake;
            public float Eruption;
            public float Hydrothermal;
            public float Lowpass;
            public float Master;
            public int ThunderSerial;
            public float ThunderIntensity;
            public float ThunderPan;
            public float ThunderDelay;
            public bool ThunderRestart;
            public int QuakeSerial;
            public float QuakeIntensity;
            public float QuakePan;
            public float QuakeDelay;
            public bool QuakeRestart;
            public int EruptionSerial;
            public float EruptionIntensity;
            public float EruptionPan;
            public float EruptionDelay;
            public bool EruptionRestart;
        }

        private struct ShotVoice
        {
            public bool Busy;
            public int Serial;
            public float Amp;
            public float Pan;
            public float DelaySamples;
            public float AgeSamples;
            public float DurationSamples;
            public float Phase;
        }

        private readonly object gate = new object();
        private MixSnapshot snapshot;
        private AudioSource source;
        private AudioClip carrier;
        private int sampleRate = 48000;
        private float smoothedMaster;
        private float windState;
        private float rainState;
        private float held;
        private int holdCount;
        private int noiseA = 0x7fff;
        private int noiseB = 0x2c91;
        private int noiseC = 0x51a3;
        private int windTimer;
        private int rainTimer;
        private int fireTimer;
        private int hydroTimer;
        private int thunderTimer;
        private int eruptShotTimer;
        private int eruptBedTimer;
        private float quakePhase;
        private float eruptPhase;
        private ShotVoice thunder;
        private ShotVoice quakeShot;
        private ShotVoice eruptionShot;

        public void ApplyBeds(BedTargets beds, float lowpass, float master)
        {
            lock (gate)
            {
                snapshot.Wind = beds.Wind;
                snapshot.Rain = beds.Rain;
                snapshot.Fire = beds.Fire;
                snapshot.Quake = beds.Quake;
                snapshot.Eruption = beds.Eruption;
                snapshot.Hydrothermal = beds.Hydrothermal;
                snapshot.Lowpass = Mathf.Clamp01(lowpass);
                snapshot.Master = Mathf.Clamp01(master);
            }
        }

        public void Trigger(OneShotKind kind, float intensity, float pan, float delay, bool restart)
        {
            lock (gate)
            {
                switch (kind)
                {
                    case OneShotKind.Quake:
                        snapshot.QuakeSerial++;
                        snapshot.QuakeIntensity = intensity;
                        snapshot.QuakePan = pan;
                        snapshot.QuakeDelay = delay;
                        snapshot.QuakeRestart = restart;
                        break;
                    case OneShotKind.Eruption:
                        snapshot.EruptionSerial++;
                        snapshot.EruptionIntensity = intensity;
                        snapshot.EruptionPan = pan;
                        snapshot.EruptionDelay = delay;
                        snapshot.EruptionRestart = restart;
                        break;
                    default:
                        snapshot.ThunderSerial++;
                        snapshot.ThunderIntensity = intensity;
                        snapshot.ThunderPan = pan;
                        snapshot.ThunderDelay = delay;
                        snapshot.ThunderRestart = restart;
                        break;
                }
            }
        }

        private void OnEnable()
        {
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            source.priority = 64;
            int rate = AudioSettings.outputSampleRate;
            if (rate < 8000) rate = 48000;
            sampleRate = rate;
            if (carrier == null || carrier.frequency != rate)
            {
                if (carrier != null)
                {
                    if (Application.isPlaying) Destroy(carrier);
                    else DestroyImmediate(carrier);
                }

                carrier = AudioClip.Create("GeneSysCarrier", rate, 1, rate, false);
                carrier.SetData(new float[rate], 0);
            }

            source.clip = carrier;
            if (!source.isPlaying) source.Play();
        }

        private void OnDestroy()
        {
            if (carrier == null) return;
            if (Application.isPlaying) Destroy(carrier);
            else DestroyImmediate(carrier);
            carrier = null;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || channels <= 0) return;
            MixSnapshot mix;
            lock (gate) mix = snapshot;

            int rate = sampleRate > 0 ? sampleRate : 48000;
            float masterStep = 1f / (rate * 0.12f);
            AcceptShot(ref thunder, mix.ThunderSerial, mix.ThunderIntensity, mix.ThunderPan, mix.ThunderDelay, mix.ThunderRestart, DiegeticSoundPolicy.ThunderSeconds, rate);
            AcceptShot(ref quakeShot, mix.QuakeSerial, mix.QuakeIntensity, mix.QuakePan, mix.QuakeDelay, mix.QuakeRestart, DiegeticSoundPolicy.QuakeSeconds, rate);
            AcceptShot(ref eruptionShot, mix.EruptionSerial, mix.EruptionIntensity, mix.EruptionPan, mix.EruptionDelay, mix.EruptionRestart, DiegeticSoundPolicy.EruptionSeconds, rate);

            for (int i = 0; i < data.Length; i += channels)
            {
                smoothedMaster = Mathf.MoveTowards(smoothedMaster, mix.Master, masterStep);
                float thunderSample = AdvanceThunder(ref thunder, rate);
                float quakeHit = AdvanceQuake(ref quakeShot, rate);
                float eruptHit = AdvanceEruption(ref eruptionShot, rate);
                float shotEnv = Mathf.Max(Mathf.Abs(thunderSample), Mathf.Max(Mathf.Abs(quakeHit), Mathf.Abs(eruptHit)));
                float duck = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(shotEnv * 1.6f));

                float beds = 0f;
                beds += Wind(mix.Wind, mix.Lowpass) * duck;
                beds += Rain(mix.Rain, mix.Lowpass) * duck;
                beds += Fire(mix.Fire) * duck * 0.8f;
                beds += QuakeBed(mix.Quake, rate) * duck;
                beds += EruptionBed(mix.Eruption, rate) * duck;
                beds += Hydro(mix.Hydrothermal, mix.Lowpass) * duck;

                float mono = BitCrush((beds * BedTrim + (thunderSample + quakeHit + eruptHit) * ShotTrim) * smoothedMaster);
                float pan = ShotPan(thunderSample, quakeHit, eruptHit);
                Pan(mono, pan, out float left, out float right);
                data[i] = left;
                if (channels > 1) data[i + 1] = right;
                for (int c = 2; c < channels; c++) data[i + c] = left;
            }
        }

        private static void AcceptShot(ref ShotVoice voice, int serial, float intensity, float pan, float delay, bool restart, float duration, int rate)
        {
            if (serial == voice.Serial) return;
            voice.Serial = serial;
            if (intensity <= 0f)
            {
                voice.Busy = false;
                return;
            }

            if (voice.Busy && !restart)
            {
                if (intensity >= voice.Amp) voice.Pan = pan;
                voice.Amp = Mathf.Max(voice.Amp, intensity);
                return;
            }

            voice.Busy = true;
            voice.Amp = intensity;
            voice.Pan = pan;
            voice.DelaySamples = Mathf.Max(0f, delay) * rate;
            voice.AgeSamples = 0f;
            voice.DurationSamples = duration * rate;
            voice.Phase = 0f;
        }

        private float AdvanceThunder(ref ShotVoice voice, int rate)
        {
            if (!ConsumeDelay(ref voice)) return 0f;
            float age = voice.AgeSamples / rate;
            float sample;
            if (age < 0.045f)
            {
                float n = ClockNoise(ref noiseB, ref thunderTimer, 1);
                sample = n * (1f - age / 0.045f) * voice.Amp;
            }
            else
            {
                float t = Mathf.Clamp01((age - 0.045f) / 0.7f);
                float freq = Mathf.Lerp(78f, 36f, t);
                sample = Triangle(ref voice.Phase, freq, rate) * (1f - t) * (1f - t) * voice.Amp;
            }

            FinishShot(ref voice);
            return sample;
        }

        private float AdvanceQuake(ref ShotVoice voice, int rate)
        {
            if (!ConsumeDelay(ref voice)) return 0f;
            float t = Mathf.Clamp01(voice.AgeSamples / Mathf.Max(1f, voice.DurationSamples));
            float freq = Mathf.Lerp(68f, 28f, t);
            float sample = Triangle(ref voice.Phase, freq, rate) * (1f - t) * voice.Amp;
            FinishShot(ref voice);
            return sample;
        }

        private float AdvanceEruption(ref ShotVoice voice, int rate)
        {
            if (!ConsumeDelay(ref voice)) return 0f;
            float age = voice.AgeSamples / rate;
            float env = 1f - Mathf.Clamp01(age / 0.5f);
            float sample;
            if (age < 0.14f)
            {
                float freq = Mathf.Lerp(60f, 120f, age / 0.14f);
                sample = Pulse(ref voice.Phase, freq, rate, 0.25f) * env * voice.Amp;
            }
            else
            {
                float n = ClockNoise(ref noiseA, ref eruptShotTimer, 2);
                sample = n * env * voice.Amp * 0.8f;
            }

            FinishShot(ref voice);
            return sample;
        }

        private static bool ConsumeDelay(ref ShotVoice voice)
        {
            if (!voice.Busy) return false;
            if (voice.DelaySamples > 0f)
            {
                voice.DelaySamples -= 1f;
                return false;
            }

            return true;
        }

        private static void FinishShot(ref ShotVoice voice)
        {
            voice.AgeSamples += 1f;
            if (voice.AgeSamples >= voice.DurationSamples)
                voice.Busy = false;
        }

        private float Wind(float gain, float lowpass)
        {
            if (gain <= 0.0001f) return 0f;
            int period = Mathf.Max(1, (int)Mathf.Lerp(28f, 70f, lowpass));
            float n = ClockNoise(ref noiseA, ref windTimer, period);
            float coeff = Mathf.Lerp(0.22f, 0.035f, lowpass);
            windState += (n - windState) * coeff;
            return windState * gain;
        }

        private float Rain(float gain, float lowpass)
        {
            if (gain <= 0.0001f) return 0f;
            int period = Mathf.Max(1, (int)Mathf.Lerp(3f, 16f, 1f - gain));
            period = Mathf.Max(1, (int)(period * Mathf.Lerp(1f, 2.4f, lowpass)));
            float n = ClockNoise(ref noiseB, ref rainTimer, period);
            float coeff = Mathf.Lerp(0.55f, 0.12f, lowpass);
            rainState += (n - rainState) * coeff;
            return rainState * gain;
        }

        private float Fire(float gain)
        {
            if (gain <= 0.0001f) return 0f;
            fireTimer++;
            int spacing = Mathf.Max(40, (int)Mathf.Lerp(900f, 80f, gain));
            if (fireTimer < spacing) return 0f;
            fireTimer = 0;
            int bit = (noiseC ^ (noiseC >> 1)) & 1;
            noiseC = (noiseC >> 1) | (bit << 14);
            if (noiseC == 0) noiseC = 0x7fff;
            return bit == 1 ? gain : -gain;
        }

        private float QuakeBed(float gain, int rate)
        {
            if (gain <= 0.0001f) return 0f;
            return Triangle(ref quakePhase, 42f, rate) * gain;
        }

        private float EruptionBed(float gain, int rate)
        {
            if (gain <= 0.0001f) return 0f;
            float pulse = Pulse(ref eruptPhase, 62f, rate, 0.3f);
            float n = ClockNoise(ref noiseB, ref eruptBedTimer, 6);
            return (pulse * 0.65f + n * 0.35f) * gain;
        }

        private float Hydro(float gain, float lowpass)
        {
            if (gain <= 0.0001f) return 0f;
            int period = Mathf.Max(8, (int)Mathf.Lerp(18f, 48f, lowpass));
            return ClockNoise(ref noiseB, ref hydroTimer, period) * gain * 0.55f;
        }

        private float BitCrush(float sample)
        {
            holdCount++;
            if (holdCount >= HoldSamples)
            {
                holdCount = 0;
                held = Mathf.Round(Mathf.Clamp(sample, -1f, 1f) * BitSteps) / BitSteps;
            }

            return held;
        }

        private float ShotPan(float thunderSample, float quakeSample, float eruptionSample)
        {
            float thunderWeight = Mathf.Abs(thunderSample);
            float quakeWeight = Mathf.Abs(quakeSample);
            float eruptionWeight = Mathf.Abs(eruptionSample);
            float weight = thunderWeight + quakeWeight + eruptionWeight;
            if (weight < 0.0001f) return 0f;
            return (thunder.Pan * thunderWeight + quakeShot.Pan * quakeWeight + eruptionShot.Pan * eruptionWeight) / weight;
        }

        private static void Pan(float mono, float pan, out float left, out float right)
        {
            float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.5f;
            left = mono * Mathf.Cos(angle * Mathf.PI * 0.5f);
            right = mono * Mathf.Sin(angle * Mathf.PI * 0.5f);
        }

        private static float ClockNoise(ref int reg, ref int timer, int period)
        {
            timer++;
            if (timer >= Mathf.Max(1, period))
            {
                timer = 0;
                int bit = (reg ^ (reg >> 1)) & 1;
                reg = (reg >> 1) | (bit << 14);
                if (reg == 0) reg = 0x7fff;
            }

            return (reg & 1) == 1 ? 1f : -1f;
        }

        private static float Triangle(ref float phase, float freq, int rate)
        {
            phase += freq / Mathf.Max(1, rate);
            phase -= Mathf.Floor(phase);
            float t = phase;
            return t < 0.5f ? t * 4f - 1f : 3f - t * 4f;
        }

        private static float Pulse(ref float phase, float freq, int rate, float width)
        {
            phase += freq / Mathf.Max(1, rate);
            phase -= Mathf.Floor(phase);
            return phase < width ? 1f : -1f;
        }
    }
}
