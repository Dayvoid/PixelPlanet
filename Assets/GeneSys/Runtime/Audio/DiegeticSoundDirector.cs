using System.Collections.Generic;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Simulation.Geodynamics;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeneSys.Audio
{
    /// <summary>
    /// Ears follow the orthographic camera. Beds come from a throttled view reduction;
    /// thunder, quakes, and eruptions are coalesced one-shots.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class DiegeticSoundDirector : MonoBehaviour
    {
        public const string VolumeKey = "genesys.sound.volume";
        public const string MutedKey = "genesys.sound.muted";
        public const float AmbienceInterval = 0.25f;
        private const int SampleStride = 4;

        private SimulationHost host;
        private PlanetoidDisplayRenderer display;
        private ComputeShader ambience;
        private RetroSoundMixer mixer;
        private AudioListener listener;
        private GraphicsBuffer accumBuffer;
        private readonly uint[] accumZeros = new uint[ViewAmbienceLayout.Slots];
        private readonly uint[] accumRead = new uint[ViewAmbienceLayout.Slots];
        private int ambienceKernel = -1;
        private int ambienceEpoch;
        private bool ambiencePending;
        private float ambienceTimer = AmbienceInterval;
        private Vector4[] geoScratch;
        private Vector4[] geoPrevious;
        private bool geoPrimed;
        private bool geoPending;
        private readonly List<PunctualCue> thunderCues = new List<PunctualCue>(32);
        private readonly List<PunctualCue> quakeCues = new List<PunctualCue>(8);
        private readonly List<PunctualCue> eruptionCues = new List<PunctualCue>(8);
        private float thunderWindow;
        private bool thunderWindowOpen;
        private float quakeSustain;
        private float eruptionSustain;
        private float hydroSustain;
        private readonly BedSlew slew = new BedSlew();
        private OneShotSlotState thunderSlot;
        private OneShotSlotState quakeSlot;
        private OneShotSlotState eruptionSlot;

        public RetroSoundMixer Mixer => mixer;
        public AudioListener Listener => listener;
        public bool OutputReady => mixer != null && listener != null;
        public bool HasAmbienceSample { get; private set; }
        public ViewAmbienceSample LatestSample { get; private set; }
        public float Volume { get; private set; } = 0.7f;
        public bool Muted { get; private set; }

        public void Bind(SimulationHost simulationHost, PlanetoidDisplayRenderer planetoid, ComputeShader ambienceShader)
        {
            if (host != null)
                host.UnsubscribeStrikes(OnStrikes);
            host = simulationHost;
            display = planetoid;
            ambience = ambienceShader;
            ambienceKernel = -1;
            if (host != null)
                host.SubscribeStrikes(OnStrikes);
            LoadPrefs();
            ResetListening();
            EnsureOutput();
        }

        public void ResetListening()
        {
            geoPrimed = false;
            geoPrevious = null;
            thunderCues.Clear();
            quakeCues.Clear();
            eruptionCues.Clear();
            thunderWindowOpen = false;
            thunderWindow = 0f;
            quakeSustain = 0f;
            eruptionSustain = 0f;
            hydroSustain = 0f;
            slew.Clear();
            thunderSlot = default;
            quakeSlot = default;
            eruptionSlot = default;
        }

        public void InvalidateReadbacks()
        {
            ambienceEpoch++;
            ambiencePending = false;
            geoPending = false;
        }

        public void SetVolume(float volume)
        {
            Volume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolumeKey, Volume);
            PlayerPrefs.Save();
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ToggleMuted() => SetMuted(!Muted);

        private void LoadPrefs()
        {
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 0.7f));
            Muted = PlayerPrefs.GetInt(MutedKey, 0) != 0;
        }

        private void OnDestroy()
        {
            ambienceEpoch++;
            if (host != null)
                host.UnsubscribeStrikes(OnStrikes);
            accumBuffer?.Dispose();
            accumBuffer = null;
        }

        private void Update()
        {
            if (host == null || !host.IsReady) return;
            EnsureOutput();
            float dt = Time.unscaledDeltaTime;
            bool suppress = host.Clock != null && DiegeticSoundPolicy.SuppressOneShots(host.Clock.Speed);
            if (suppress)
            {
                thunderCues.Clear();
                thunderWindowOpen = false;
            }
            else
            {
                GatherThunderWindow(dt);
            }

            AdvanceSlots(dt);
            ambienceTimer += dt;
            if (ambienceTimer >= AmbienceInterval)
            {
                ambienceTimer = 0f;
                DispatchAmbience();
                RequestGeodynamics();
            }

            PushMix();
        }

        private void OnStrikes(GpuPassScheduler.StrikeSeedGpu[] seeds, int count)
        {
            if (seeds == null || count <= 0 || display == null) return;
            if (host != null && host.Clock != null && DiegeticSoundPolicy.SuppressOneShots(host.Clock.Speed))
                return;

            float ortho = OrthoSize();
            int added = 0;
            int n = Mathf.Min(count, seeds.Length);
            for (int i = 0; i < n; i++)
            {
                if (!TryProjectCell(new Vector2Int(seeds[i].CellX, seeds[i].CellY), out Vector2 viewport, out _))
                    continue;
                thunderCues.Add(new PunctualCue
                {
                    Intensity = Mathf.Clamp01(Mathf.Abs(seeds[i].Charge) / 4f),
                    ScreenX01 = viewport.x,
                    ScreenDistance01 = DiegeticSoundPolicy.ViewportDistance01(viewport.x, viewport.y),
                    ScreenFraction = StrikeScreenFraction(),
                    InView = true
                });
                added++;
            }

            if (added > 0)
                thunderWindowOpen = true;
        }

        private void GatherThunderWindow(float dt)
        {
            if (!thunderWindowOpen) return;
            thunderWindow += dt;
            if (thunderWindow < DiegeticSoundPolicy.CoalesceSeconds) return;
            FlushPunctual(OneShotKind.Thunder, thunderCues, thunderDelay: true);
            thunderCues.Clear();
            thunderWindow = 0f;
            thunderWindowOpen = false;
        }

        private void AdvanceSlots(float dt)
        {
            thunderSlot.Advance(dt);
            quakeSlot.Advance(dt);
            eruptionSlot.Advance(dt);
        }

        private void PushMix()
        {
            if (mixer == null) return;
            float ortho = OrthoSize();
            ViewAmbienceSample sample = LatestSample;
            float eruption = DiegeticSoundPolicy.EruptionColumn(eruptionSustain, sample.MagmaFraction, sample.TephraFraction);
            BedTargets targets = DiegeticSoundPolicy.SelectBeds(
                DiegeticSoundPolicy.NormalizeWind(sample.MeanWind),
                DiegeticSoundPolicy.NormalizeRain(sample.MeanRain),
                DiegeticSoundPolicy.NormalizeFire(sample.MeanFire),
                quakeSustain,
                eruption,
                hydroSustain,
                DiegeticSoundPolicy.NormalizeCoverage(sample.TephraFraction));
            targets.Scale(DiegeticSoundPolicy.ZoomGain(ortho));
            slew.Advance(targets, Time.unscaledDeltaTime);
            bool hidden = display == null || !display.IsPlanetVisible;
            float master = DiegeticSoundPolicy.MasterGain(Volume, Muted, hidden);
            mixer.ApplyBeds(slew.Current, DiegeticSoundPolicy.LowpassMix(ortho), master);
        }

        private void FlushPunctual(OneShotKind kind, List<PunctualCue> cues, bool thunderDelay)
        {
            if (cues == null || cues.Count == 0 || mixer == null) return;
            if (!DiegeticSoundPolicy.TryCoalescePunctual(cues.ToArray(), OrthoSize(), out float intensity, out float pan, out float delay))
                return;
            if (!thunderDelay) delay = 0f;
            ref OneShotSlotState slot = ref SlotFor(kind);
            bool raised = slot.Receive(intensity, pan, delay, DiegeticSoundPolicy.Duration(kind));
            mixer.Trigger(kind, slot.Intensity, slot.Pan, raised ? 0f : slot.Delay, !raised);
        }

        private ref OneShotSlotState SlotFor(OneShotKind kind)
        {
            switch (kind)
            {
                case OneShotKind.Quake: return ref quakeSlot;
                case OneShotKind.Eruption: return ref eruptionSlot;
                default: return ref thunderSlot;
            }
        }

        private void DispatchAmbience()
        {
            if (ambiencePending || ambience == null || display == null || host.Resources == null || !host.Resources.IsCreated)
                return;
            Camera camera = display.TargetCamera;
            if (camera == null) return;
            if (ambienceKernel < 0)
            {
                ambienceKernel = ambience.FindKernel("ReduceView");
                if (ambienceKernel < 0)
                {
                    Debug.LogError("GeneSys: ReduceView kernel missing. Reimport ViewAmbience.compute.");
                    return;
                }
            }

            if (accumBuffer == null || !accumBuffer.IsValid())
            {
                accumBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ViewAmbienceLayout.Slots, sizeof(uint));
            }

            accumBuffer.SetData(accumZeros);
            PolarGridDefinition grid = host.Grid;
            Matrix4x4 objectToCamera = camera.worldToCameraMatrix * display.transform.localToWorldMatrix;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.01f, camera.aspect);
            ambience.SetInts("_GridSize", grid.angularResolution, grid.radialResolution);
            ambience.SetInt("_Stride", SampleStride);
            ambience.SetFloat("_AtmosphereStart", grid.atmosphereStartRadius);
            ambience.SetFloat("_VisualCoreRadius", grid.visualCoreRadius);
            ambience.SetFloat("_VisualCoreSquash", grid.visualCoreSquash);
            ambience.SetFloat("_CloudRetain", host.Config != null ? host.Config.cloudRetainMass : 0.9f);
            ambience.SetMatrix("_ObjectToCamera", objectToCamera);
            ambience.SetVector("_ViewHalf", new Vector4(halfWidth, halfHeight, 0f, 0f));
            ambience.SetTexture(ambienceKernel, "_MaterialRead", host.Resources.MaterialRead);
            ambience.SetTexture(ambienceKernel, "_StateRead", host.Resources.StateRead);
            ambience.SetTexture(ambienceKernel, "_FlowRead", host.Resources.FlowRead);
            ambience.SetTexture(ambienceKernel, "_CombustionRead", host.Resources.CombustionRead);
            ambience.SetBuffer(ambienceKernel, "_Accum", accumBuffer);

            int cellsX = Mathf.CeilToInt(grid.angularResolution / (float)SampleStride);
            int cellsY = Mathf.CeilToInt(grid.radialResolution / (float)SampleStride);
            ambience.Dispatch(ambienceKernel, Mathf.Max(1, Mathf.CeilToInt(cellsX / 8f)), Mathf.Max(1, Mathf.CeilToInt(cellsY / 8f)), 1);

            ambiencePending = true;
            int epoch = ambienceEpoch;
            AsyncGPUReadback.Request(accumBuffer, request =>
            {
                if (epoch != ambienceEpoch) return;
                ambiencePending = false;
                if (request.hasError) return;
                var data = request.GetData<uint>();
                int n = Mathf.Min(accumRead.Length, data.Length);
                for (int i = 0; i < n; i++) accumRead[i] = data[i];
                for (int i = n; i < accumRead.Length; i++) accumRead[i] = 0;
                LatestSample = DiegeticSoundPolicy.Decode(accumRead);
                HasAmbienceSample = true;
            });
        }

        private void RequestGeodynamics()
        {
            if (geoPending || host.Resources?.GeodynamicsEvents == null) return;
            ComputeBuffer events = host.Resources.GeodynamicsEvents;
            if (!events.IsValid()) return;
            geoPending = true;
            int epoch = ambienceEpoch;
            AsyncGPUReadback.Request(events, request =>
            {
                if (epoch != ambienceEpoch) return;
                geoPending = false;
                if (request.hasError) return;
                var data = request.GetData<Vector4>();
                if (geoScratch == null || geoScratch.Length < data.Length)
                    geoScratch = new Vector4[data.Length];
                for (int i = 0; i < data.Length; i++) geoScratch[i] = data[i];
                ApplyGeodynamics(geoScratch, data.Length);
            });
        }

        private void ApplyGeodynamics(Vector4[] events, int length)
        {
            if (host?.Config == null) return;
            int angular = GeodynamicsGrid.ClampAngularBins(host.Config.geodynamicsAngularBins);
            int radial = GeodynamicsGrid.ClampRadialBins(host.Config.geodynamicsRadialBins);
            int cells = angular * radial;
            if (geoPrevious == null || geoPrevious.Length != cells)
            {
                geoPrevious = new Vector4[cells];
                geoPrimed = false;
            }

            quakeCues.Clear();
            eruptionCues.Clear();
            float quake = 0f;
            float eruption = 0f;
            float hydro = 0f;
            float ortho = OrthoSize();
            bool suppress = host.Clock != null && DiegeticSoundPolicy.SuppressOneShots(host.Clock.Speed);
            for (int a = 0; a < angular; a++)
            {
                for (int r = 0; r < radial; r++)
                {
                    int index = GeodynamicsGrid.EventIndex(a, r, angular, radial);
                    Vector4 ev = index >= 0 && index < length ? events[index] : Vector4.zero;
                    int type = Mathf.RoundToInt(ev.x);
                    bool active = ev.y > DiegeticSoundPolicy.ActiveEvent && type > 0;
                    Vector4 previous = geoPrevious[a * radial + r];
                    int previousType = Mathf.RoundToInt(previous.x);
                    bool wasActive = previous.y > DiegeticSoundPolicy.ActiveEvent && previousType == type;
                    if (active
                        && TryHearBin(a, r, angular, radial, ev.y, out PunctualCue cue)
                        && DiegeticSoundPolicy.PassesZoomGate(ortho, cue.ScreenDistance01, cue.ScreenFraction))
                    {
                        if (type == GeodynamicsGrid.EventTypeEarthquake)
                        {
                            quake = Mathf.Max(quake, ev.y);
                            if (geoPrimed && !wasActive && !suppress) quakeCues.Add(cue);
                        }
                        else if (type == GeodynamicsGrid.EventTypeVolcanic)
                        {
                            eruption = Mathf.Max(eruption, ev.y);
                            if (geoPrimed && !wasActive && !suppress) eruptionCues.Add(cue);
                        }
                        else if (type == GeodynamicsGrid.EventTypeHydrothermal)
                        {
                            hydro = Mathf.Max(hydro, ev.y);
                        }
                    }

                    geoPrevious[a * radial + r] = ev;
                }
            }

            geoPrimed = true;
            quakeSustain = quake;
            eruptionSustain = eruption;
            hydroSustain = hydro;
            FlushPunctual(OneShotKind.Quake, quakeCues, thunderDelay: false);
            FlushPunctual(OneShotKind.Eruption, eruptionCues, thunderDelay: false);
            quakeCues.Clear();
            eruptionCues.Clear();
        }

        private bool TryHearBin(int angularBin, int radialBin, int angularBins, int radialBins, float intensity, out PunctualCue cue)
        {
            cue = default;
            PolarGridDefinition grid = host.Grid;
            GeodynamicsGrid.ThetaRange(angularBin, grid.angularResolution, angularBins, out int thetaStart, out int thetaEnd);
            GeodynamicsGrid.RadialRange(radialBin, grid.radialResolution, grid.atmosphereStartRadius, radialBins, out int y0, out int y1);
            int theta = (thetaStart + Mathf.Max(thetaStart + 1, thetaEnd)) / 2;
            int y = (y0 + Mathf.Max(y0 + 1, y1)) / 2;
            if (!TryProjectCell(new Vector2Int(theta, y), out Vector2 center, out _))
                return false;

            float fraction = 0f;
            int edge = Mathf.Max(thetaStart, thetaEnd - 1);
            if (TryProjectCell(new Vector2Int(thetaStart, y), out Vector2 left, out bool leftIn)
                && TryProjectCell(new Vector2Int(edge, y), out Vector2 right, out bool rightIn)
                && leftIn && rightIn)
            {
                fraction = Mathf.Abs(left.x - right.x);
            }

            cue = new PunctualCue
            {
                Intensity = Mathf.Clamp01(intensity),
                ScreenX01 = center.x,
                ScreenDistance01 = DiegeticSoundPolicy.ViewportDistance01(center.x, center.y),
                ScreenFraction = fraction,
                InView = true
            };
            return true;
        }

        private bool TryProjectCell(Vector2Int cell, out Vector2 viewport01, out bool inside)
        {
            viewport01 = default;
            inside = false;
            Camera camera = display != null ? display.TargetCamera : null;
            if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return false;
            if (!display.TryCellToScreen(cell, out Vector2 screen)) return false;
            viewport01 = new Vector2(screen.x / camera.pixelWidth, screen.y / camera.pixelHeight);
            inside = viewport01.x >= 0f && viewport01.x <= 1f && viewport01.y >= 0f && viewport01.y <= 1f;
            return inside;
        }

        private float StrikeScreenFraction()
        {
            Camera camera = display != null ? display.TargetCamera : null;
            if (camera == null || camera.pixelHeight <= 0) return 0f;
            return 4f / camera.pixelHeight;
        }

        private float OrthoSize()
        {
            Camera camera = display != null ? display.TargetCamera : null;
            if (camera == null) return 5.5f;
            return camera.orthographicSize;
        }

        private void EnsureOutput()
        {
            Camera camera = display != null ? display.TargetCamera : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            listener = camera.GetComponent<AudioListener>();
            if (listener == null) listener = camera.gameObject.AddComponent<AudioListener>();

            // OnAudioFilterRead cannot live on a GameObject that has both a source and a listener.
            Transform child = camera.transform.Find("Diegetic Sound");
            GameObject soundObject = child != null ? child.gameObject : null;
            if (soundObject == null)
            {
                soundObject = new GameObject("Diegetic Sound");
                soundObject.transform.SetParent(camera.transform, false);
            }

            mixer = soundObject.GetComponent<RetroSoundMixer>();
            if (mixer == null) mixer = soundObject.AddComponent<RetroSoundMixer>();
        }
    }
}
