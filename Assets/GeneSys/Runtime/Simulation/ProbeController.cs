using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation.Topology;
using UnityEngine;

namespace GeneSys.Simulation
{
    public enum ProbeAction
    {
        None,
        Vapor,
        Water,
        Soil,
        Cool,
        Heat
    }

    /// <summary>
    /// Clockwise orbiter opposite the sun. HUD hold actions deposit at the outer sim ring ahead of its path.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ProbeController : MonoBehaviour
    {
        public const float DefaultLeadDegrees = 2f;
        public const int DefaultDepositRadius = 4;
        public const float FollowLockBearingDegrees = 90f;

        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private Sprite probeSprite;

        private Transform probeRoot;
        private SpriteRenderer probeRenderer;
        private ProbeAction action;
        private bool warnedMissingSprite;

        public ProbeAction ActiveAction => action;
        public Vector3 WorldPosition => probeRenderer != null ? probeRenderer.transform.position : Vector3.zero;

        public float ProbeAngle01
        {
            get
            {
                if (host == null || host.Config == null) return 0f;
                float periodTicks = host.Config.ticksPerSecond * Mathf.Max(1f, host.Config.probeOrbitPeriodSeconds);
                return Mathf.Repeat(host.Clock.TickCount / Mathf.Max(1f, periodTicks), 1f);
            }
        }

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer planetoidDisplay)
        {
            host = simulationHost;
            display = planetoidDisplay;
            SyncPose();
        }

        public void SyncPose()
        {
            EnsureBuilt();
            UpdateProbePose();
        }

        public void SetAction(ProbeAction probeAction) => action = probeAction;

        public static Vector2 ProbeDirectionFromAngle01(float probeAngle01)
        {
            float angle = -Mathf.Repeat(probeAngle01, 1f) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static float FollowLockRotationZ(float probeAngle01)
        {
            float probeLocalDegrees = -Mathf.Repeat(probeAngle01, 1f) * 360f;
            return FollowLockBearingDegrees - probeLocalDegrees;
        }

        public static float SpriteRotationZ(float probeAngle01, float rotationOffsetDegrees)
        {
            float angle = -Mathf.Repeat(probeAngle01, 1f) * Mathf.PI * 2f;
            // Clockwise tangent: d/dt of (cos(-ωt), sin(-ωt)) points along (sin(a), -cos(a)).
            float heading = Mathf.Atan2(-Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Rad2Deg;
            return heading - 90f + rotationOffsetDegrees;
        }

        public Vector3 LocalOrbitPosition
        {
            get
            {
                if (host == null || host.Config == null) return Vector3.zero;
                float orbit = Mathf.Clamp(host.Config.probeOrbitRadius, 0.8f, 2f);
                float radius = 0.5f * orbit;
                Vector2 direction = ProbeDirectionFromAngle01(ProbeAngle01);
                return new Vector3(direction.x * radius, direction.y * radius, -0.05f);
            }
        }

        public Vector2Int AimCell(PolarGridDefinition grid)
        {
            float leadDegrees = host != null && host.Config != null ? host.Config.probeLeadDegrees : DefaultLeadDegrees;
            float leadTurns = leadDegrees / 360f;
            float theta01 = Mathf.Repeat(-ProbeAngle01 - leadTurns, 1f);
            int angular = Mathf.FloorToInt(theta01 * Mathf.Max(1, grid.angularResolution));
            return new Vector2Int(grid.WrapTheta(angular), Mathf.Max(0, grid.radialResolution - 1));
        }

        private void Start()
        {
            if (host == null) host = GetComponent<SimulationHost>();
            if (display == null) display = FindFirstObjectByType<PlanetoidDisplayRenderer>();
            EnsureBuilt();
        }

        private void Update()
        {
            if (action == ProbeAction.None || host == null || !host.IsReady || host.Config == null) return;
            if (!host.Clock.IsRunning) return;

            SimulationConfig config = host.Config;
            int radius = Mathf.Max(1, config.probeDepositRadius);
            Vector2Int cell = AimCell(host.Grid);
            switch (action)
            {
                case ProbeAction.Vapor:
                    host.QueueFieldDeposit(cell, radius, 6, config.probeVaporRate);
                    break;
                case ProbeAction.Water:
                    host.QueueFieldDeposit(cell, radius, 2, config.probeWaterRate);
                    break;
                case ProbeAction.Soil:
                    host.QueueMaterialPaint(cell, radius, MaterialIds.Soil);
                    break;
                case ProbeAction.Heat:
                    host.QueueFieldDeposit(cell, radius, 1, config.probeHeatRate);
                    break;
                case ProbeAction.Cool:
                    host.QueueFieldDeposit(cell, radius, 1, -config.probeCoolRate);
                    break;
            }
        }

        private void LateUpdate()
        {
            SyncPose();
        }

        private void EnsureBuilt()
        {
            if (probeRenderer != null) return;
            if (display == null) display = FindFirstObjectByType<PlanetoidDisplayRenderer>();
            if (display == null) return;

            probeRoot = new GameObject("Probe Orbit").transform;
            probeRoot.SetParent(display.transform, false);

            var probeObject = new GameObject("Probe");
            probeObject.transform.SetParent(probeRoot, false);
            probeRenderer = probeObject.AddComponent<SpriteRenderer>();
            probeRenderer.sprite = probeSprite;
            probeRenderer.sortingOrder = 60;
            probeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            probeRenderer.receiveShadows = false;

            if (probeSprite == null && !warnedMissingSprite)
            {
                warnedMissingSprite = true;
                Debug.LogWarning("GeneSys probe: Probe-Sprite is not assigned.", this);
            }
        }

        private void UpdateProbePose()
        {
            if (probeRoot == null || probeRenderer == null || display == null) return;
            if (host == null || host.Config == null) return;

            SimulationConfig config = host.Config;
            float orbit = Mathf.Clamp(config.probeOrbitRadius, 0.8f, 2f);
            float radius = 0.5f * orbit;
            Vector2 direction = ProbeDirectionFromAngle01(ProbeAngle01);

            probeRoot.localPosition = Vector3.zero;
            probeRoot.localRotation = Quaternion.identity;
            probeRenderer.sprite = probeSprite;
            probeRenderer.enabled = probeSprite != null;
            probeRenderer.transform.localPosition = new Vector3(direction.x * radius, direction.y * radius, -0.05f);
            probeRenderer.transform.localScale = Vector3.one * Mathf.Clamp(config.probeSpriteScale, 0.01f, 1f);
            probeRenderer.transform.localRotation = Quaternion.Euler(0f, 0f,
                SpriteRotationZ(ProbeAngle01, config.probeSpriteRotationOffset));
        }

        private void OnDestroy()
        {
            if (probeRoot != null) Destroy(probeRoot.gameObject);
        }
    }
}
