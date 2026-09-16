using System;
using System.Collections;
using GeneSys.Configuration;
using GeneSys.Simulation;
using UnityEngine;

namespace GeneSys.Rendering
{
    /// <summary>
    /// In-scene probe travel hold used while worldgen or snapshot load runs offscreen.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SceneTransitionDirector : MonoBehaviour
    {
        public const float DefaultDepartSeconds = 1.4f;
        public const float DefaultApproachSeconds = 1.8f;
        public const float HoldOrthographicSize = 1f;
        public const float ThrusterHoldBoost = 2.25f;
        public const float OrbitBreakScale = 2.4f;
        public const float HidePlanetAt = 0.78f;
        public static readonly Vector2 StarJourneyVelocity = new(-1.2f, 0.15f);
        public static readonly Vector2 NebulaJourneyVelocity = new(-0.35f, 0.04f);

        private SimulationHost host;
        private PlanetoidDisplayRenderer display;
        private ProbeController probe;
        private TerrariumVisualController visuals;
        private bool savedClockRunning;
        private CameraViewMode savedViewMode;
        private Vector3 savedCameraPosition;
        private float savedOrthographicSize = 5.5f;
        private Quaternion savedDisplayRotation = Quaternion.identity;
        private ProbeFlightMode savedFlightMode = ProbeFlightMode.Clockwise;
        private Func<Vector2, bool> savedBlockInput;

        public bool IsActive { get; private set; }

        public void Bind(
            SimulationHost simulationHost,
            PlanetoidDisplayRenderer planetoidDisplay,
            ProbeController probeController,
            TerrariumVisualController visualController)
        {
            host = simulationHost;
            display = planetoidDisplay;
            probe = probeController;
            visuals = visualController;
        }

        public static float TransitionDuration(float speed, float baseSeconds)
        {
            return Mathf.Max(0.01f, baseSeconds) / Mathf.Max(0.25f, speed);
        }

        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static Vector3 LerpPose(Vector3 from, Vector3 to, float t) =>
            Vector3.LerpUnclamped(from, to, Smooth01(t));

        public static float LerpFloat(float from, float to, float t) =>
            Mathf.LerpUnclamped(from, to, Smooth01(t));

        public IEnumerator PlayAround(Action work)
        {
            if (IsActive)
                yield break;
            if (work == null)
                yield break;
            if (!Begin())
            {
                work();
                yield break;
            }

            yield return Depart();
            yield return null;
            yield return null;
            try
            {
                work();
            }
            catch
            {
                EndImmediate();
                throw;
            }

            yield return Approach();
            EndImmediate();
        }

        public IEnumerator PlayBoot(Action generateWorld)
        {
            if (IsActive)
                yield break;
            if (generateWorld == null)
                yield break;
            if (!Begin())
            {
                generateWorld();
                yield break;
            }

            SnapToHold();
            yield return null;
            yield return null;
            try
            {
                generateWorld();
            }
            catch
            {
                EndImmediate();
                throw;
            }

            yield return Approach();
            EndImmediate();
        }

        private bool Begin()
        {
            if (host == null || display == null || probe == null)
                return false;

            IsActive = true;
            savedClockRunning = host.Clock != null && host.Clock.IsRunning;
            host.Clock?.SetRunning(false);
            savedViewMode = display.ViewMode;
            savedDisplayRotation = display.transform.rotation;
            savedFlightMode = probe.FlightMode;
            savedBlockInput = display.ShouldBlockWorldInput;
            if (display.TargetCamera != null)
            {
                savedCameraPosition = display.TargetCamera.transform.position;
                savedOrthographicSize = display.TargetCamera.orthographicSize;
            }

            display.ShouldBlockWorldInput = _ => true;
            display.CinematicCameraActive = true;
            return true;
        }

        private void SnapToHold()
        {
            ApplyProbeTravel(1f, OrbitLocal(), HoldLocal(), OrbitRotation(), OrbitRotation(), 1f, ThrusterHoldBoost, 1f);
            HidePlanetContext();
            display.SetCinematicCamera(ProbeCenteredCamera(CurrentCameraPosition().z), HoldOrthographicSize);
        }

        private IEnumerator Depart()
        {
            Vector3 startLocal = OrbitLocal();
            Vector3 endLocal = HoldLocal();
            float rotation = OrbitRotation();
            Vector3 startCam = CurrentCameraPosition();
            float startOrtho = savedOrthographicSize;
            float duration = CurrentDuration(DefaultDepartSeconds);
            yield return Animate(duration, t =>
            {
                ApplyProbeTravel(t, startLocal, endLocal, rotation, rotation, 1f, ThrusterHoldBoost, t);
                display.SetCinematicCamera(
                    Vector3.LerpUnclamped(startCam, ProbeCenteredCamera(startCam.z), t),
                    Mathf.LerpUnclamped(startOrtho, HoldOrthographicSize, t));
                if (t >= HidePlanetAt)
                    HidePlanetContext();
            });
        }

        private IEnumerator Approach()
        {
            ShowPlanetContext();
            Vector3 startLocal = HoldLocal();
            Vector3 endLocal = OrbitLocal();
            float rotation = OrbitRotation();
            float cameraZ = CurrentCameraPosition().z;
            float duration = CurrentDuration(DefaultApproachSeconds);
            yield return Animate(duration, t =>
            {
                ApplyProbeTravel(t, startLocal, endLocal, rotation, rotation, ThrusterHoldBoost, 1f, 1f - t);
                display.SetCinematicCamera(
                    Vector3.LerpUnclamped(ProbeCenteredCamera(cameraZ), RestoreCameraPosition(cameraZ), t),
                    Mathf.LerpUnclamped(HoldOrthographicSize, savedOrthographicSize, t));
                display.transform.rotation = Quaternion.Slerp(display.transform.rotation, savedDisplayRotation, t);
            });
        }

        private IEnumerator Animate(float duration, Action<float> step)
        {
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.01f, duration);
            while (elapsed < safeDuration)
            {
                step(Smooth01(elapsed / safeDuration));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            step(1f);
        }

        private void ApplyProbeTravel(
            float t,
            Vector3 startLocal,
            Vector3 endLocal,
            float startRotation,
            float endRotation,
            float startBoost,
            float endBoost,
            float drift)
        {
            probe.SetCinematicPose(
                Vector3.LerpUnclamped(startLocal, endLocal, t),
                Mathf.LerpUnclamped(startRotation, endRotation, t),
                Mathf.LerpUnclamped(startBoost, endBoost, t));
            probe.SyncPose();
            visuals?.SetJourneyDrift(StarJourneyVelocity * drift, NebulaJourneyVelocity * drift);
        }

        private void HidePlanetContext()
        {
            display.SetPlanetVisible(false);
            visuals?.SetPlanetContextVisible(false);
        }

        private void ShowPlanetContext()
        {
            display.SetPlanetVisible(true);
            visuals?.SetPlanetContextVisible(true);
        }

        private void EndImmediate()
        {
            if (probe != null)
            {
                probe.ClearCinematicOverride();
                probe.SetFlightMode(savedFlightMode);
                probe.SyncPose();
            }

            visuals?.SetJourneyDrift(Vector2.zero, Vector2.zero);
            ShowPlanetContext();
            if (display != null)
            {
                display.ShouldBlockWorldInput = savedBlockInput;
                display.CinematicCameraActive = false;
                display.transform.rotation = savedDisplayRotation;
                if (savedViewMode == CameraViewMode.Globe && display.TargetCamera != null)
                {
                    display.TargetCamera.transform.position = savedCameraPosition;
                    display.TargetCamera.orthographicSize = savedOrthographicSize;
                }
            }

            host?.Clock?.SetRunning(savedClockRunning);
            IsActive = false;
        }

        private float CurrentDuration(float baseSeconds)
        {
            float speed = host != null && host.Config != null ? host.Config.transitionSpeed : 1f;
            return TransitionDuration(speed, baseSeconds);
        }

        private Vector3 OrbitLocal()
        {
            SimulationConfig config = host != null ? host.Config : null;
            float radius = config != null ? config.probeOrbitRadius : 1.012f;
            float angle = probe != null ? probe.ProbeAngle01 : 0f;
            return ProbeController.CinematicOrbitPosition(angle, radius, 1f);
        }

        private Vector3 HoldLocal()
        {
            SimulationConfig config = host != null ? host.Config : null;
            float radius = config != null ? config.probeOrbitRadius : 1.012f;
            float angle = probe != null ? probe.ProbeAngle01 : 0f;
            return ProbeController.CinematicOrbitPosition(angle, radius, OrbitBreakScale);
        }

        private float OrbitRotation()
        {
            SimulationConfig config = host != null ? host.Config : null;
            float offset = config != null ? config.probeSpriteRotationOffset : 0f;
            bool reverse = probe != null && probe.TravelMode == ProbeFlightMode.Counterclockwise;
            float angle = probe != null ? probe.ProbeAngle01 : 0f;
            return ProbeController.SpriteRotationZ(angle, offset, reverse);
        }

        private Vector3 CurrentCameraPosition()
        {
            if (display != null && display.TargetCamera != null)
                return display.TargetCamera.transform.position;
            return savedCameraPosition;
        }

        private Vector3 ProbeCenteredCamera(float cameraZ)
        {
            Vector3 world = probe != null ? probe.WorldPosition : Vector3.zero;
            return new Vector3(world.x, world.y, cameraZ);
        }

        private Vector3 RestoreCameraPosition(float cameraZ)
        {
            if (savedViewMode == CameraViewMode.ProbeFollow && probe != null)
            {
                Vector3 world = probe.WorldPosition;
                float topAnchor = savedOrthographicSize * 0.72f;
                return new Vector3(world.x, world.y - topAnchor, cameraZ);
            }

            return new Vector3(savedCameraPosition.x, savedCameraPosition.y, cameraZ);
        }
    }
}
