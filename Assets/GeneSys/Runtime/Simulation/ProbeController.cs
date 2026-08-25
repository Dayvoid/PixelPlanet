using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation.Topology;
using UnityEngine;

namespace GeneSys.Simulation
{
    /// <summary>
    /// Clockwise orbiter opposite the sun. Seeds atmospheric vapor at the outer sim ring while the UI water icon is held.
    /// </summary>
    public sealed class ProbeController : MonoBehaviour
    {
        public const float VaporLeadDegrees = 2f;
        public const int VaporBrushRadius = 4;

        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private Sprite probeSprite;
        [SerializeField] private Sprite vaporIcon;

        private Transform probeRoot;
        private SpriteRenderer probeRenderer;
        private bool vaporSeeding;
        private bool warnedMissingSprite;

        public Sprite VaporIcon => vaporIcon;
        public bool IsVaporSeeding => vaporSeeding;

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
            EnsureBuilt();
            UpdateProbePose();
        }

        public void SetVaporSeeding(bool seeding) => vaporSeeding = seeding;

        public static Vector2 ProbeDirectionFromAngle01(float probeAngle01)
        {
            float angle = -Mathf.Repeat(probeAngle01, 1f) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public Vector2Int AimCell(PolarGridDefinition grid)
        {
            float leadTurns = VaporLeadDegrees / 360f;
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
            if (!vaporSeeding || host == null || !host.IsReady || host.Config == null) return;
            if (!host.Clock.IsRunning) return;
            host.QueueFieldDeposit(AimCell(host.Grid), VaporBrushRadius, 6, host.Config.probeVaporRate);
        }

        private void LateUpdate()
        {
            EnsureBuilt();
            UpdateProbePose();
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
            float angle = -ProbeAngle01 * Mathf.PI * 2f;
            Vector2 direction = ProbeDirectionFromAngle01(ProbeAngle01);

            probeRoot.localPosition = Vector3.zero;
            probeRoot.localRotation = Quaternion.identity;
            probeRenderer.sprite = probeSprite;
            probeRenderer.enabled = probeSprite != null;
            probeRenderer.transform.localPosition = new Vector3(direction.x * radius, direction.y * radius, -0.05f);
            probeRenderer.transform.localScale = Vector3.one * Mathf.Clamp(config.probeSpriteScale, 0.01f, 1f);

            // Clockwise tangent: d/dt of (cos(-ωt), sin(-ωt)) points along (sin(a), -cos(a)).
            float heading = Mathf.Atan2(-Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Rad2Deg;
            probeRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, heading - 90f);
        }

        private void OnDestroy()
        {
            if (probeRoot != null) Destroy(probeRoot.gameObject);
        }
    }
}
