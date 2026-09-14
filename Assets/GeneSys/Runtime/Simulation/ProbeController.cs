using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeneSys.Simulation
{
    public enum ProbeAction
    {
        None,
        Humidity,
        Water,
        Soil,
        Cool,
        Heat
    }

    public enum ProbeFlightMode
    {
        Clockwise,
        Counterclockwise,
        Stopped
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
        public const float DefaultEnergyMax = 100f;
        public static readonly Vector3 ThrusterLocalOffset = new(-0.12f, 0f, 0.01f);

        private const int ThrusterSortingOrder = 59;
        private const float ThrusterFlightRate = 56f;
        private const float ThrusterIdleRate = 8f;

        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private Sprite probeSprite;

        private Transform probeRoot;
        private SpriteRenderer probeRenderer;
        private ParticleSystem thrusterSystem;
        private Material thrusterMaterial;
        private Texture2D thrusterTexture;
        private ProbeAction action;
        private ProbeFlightMode flightMode = ProbeFlightMode.Clockwise;
        private ProbeFlightMode lastTravelMode = ProbeFlightMode.Clockwise;
        private bool lifeSeedActive;
        private float orbitAngle01;
        private float energy = DefaultEnergyMax;
        private long lastProcessedTick;
        private int pendingLifeSeeds;
        private int ticksUntilLifeBurst;
        private bool warnedMissingSprite;
        private bool thrusterUnavailable;

        public ProbeAction ActiveAction => action;
        public ProbeFlightMode FlightMode => flightMode;
        public ProbeFlightMode TravelMode => lastTravelMode;
        public bool LifeSeedActive => lifeSeedActive;
        public float Energy => energy;
        public Vector3 WorldPosition => probeRenderer != null ? probeRenderer.transform.position : Vector3.zero;

        public float EnergyNormalized
        {
            get
            {
                float max = host != null && host.Config != null ? Mathf.Max(1f, host.Config.probeEnergyMax) : DefaultEnergyMax;
                return Mathf.Clamp01(energy / max);
            }
        }

        public float ProbeAngle01 => orbitAngle01;

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer planetoidDisplay)
        {
            host = simulationHost;
            display = planetoidDisplay;
            ResetRuntimeState();
            SyncPose();
        }

        public void SyncPose()
        {
            EnsureBuilt();
            UpdateProbePose();
        }

        public void SetAction(ProbeAction probeAction) => action = probeAction;

        public void SetFlightMode(ProbeFlightMode mode)
        {
            flightMode = mode;
            if (mode != ProbeFlightMode.Stopped)
                lastTravelMode = mode;
        }

        public void SetLifeSeedActive(bool active)
        {
            lifeSeedActive = active;
            if (active)
            {
                pendingLifeSeeds = 0;
                ticksUntilLifeBurst = 0;
            }
            else
            {
                pendingLifeSeeds = 0;
            }
        }

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

        public static float SpriteRotationZ(float probeAngle01, float rotationOffsetDegrees) =>
            SpriteRotationZ(probeAngle01, rotationOffsetDegrees, reverseTravel: false);

        public static float SpriteRotationZ(float probeAngle01, float rotationOffsetDegrees, bool reverseTravel)
        {
            float angle = -Mathf.Repeat(probeAngle01, 1f) * Mathf.PI * 2f;
            // Clockwise tangent: d/dt of (cos(-ωt), sin(-ωt)) points along (sin(a), -cos(a)).
            float heading = Mathf.Atan2(-Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Rad2Deg;
            float artOffset = reverseTravel ? -rotationOffsetDegrees : rotationOffsetDegrees;
            return heading - 90f + artOffset;
        }

        public static Vector3 SpriteLocalScale(float spriteScale, bool reverseTravel)
        {
            float scale = Mathf.Clamp(spriteScale, 0.01f, 1f);
            return new Vector3(scale, reverseTravel ? -scale : scale, scale);
        }

        public static float AdvanceOrbitAngle01(float currentAngle01, ProbeFlightMode mode, int ticks, float periodTicks)
        {
            if (mode == ProbeFlightMode.Stopped || ticks == 0 || periodTicks <= 0f)
                return Mathf.Repeat(currentAngle01, 1f);
            float delta = ticks / periodTicks;
            if (mode == ProbeFlightMode.Counterclockwise) delta = -delta;
            return Mathf.Repeat(currentAngle01 + delta, 1f);
        }

        public static float ApplyEnergyTick(
            float currentEnergy,
            bool actionActive,
            bool regenAllowed,
            float drainPerTick,
            float regenPerSecond,
            float tickDuration,
            float maxEnergy)
        {
            float next = currentEnergy;
            if (actionActive)
                next -= Mathf.Max(0f, drainPerTick);
            else if (regenAllowed)
                next += Mathf.Max(0f, regenPerSecond) * Mathf.Max(0f, tickDuration);
            return Mathf.Clamp(next, 0f, Mathf.Max(0f, maxEnergy));
        }

        public static float AimLeadSign(ProbeFlightMode travelMode) =>
            travelMode == ProbeFlightMode.Counterclockwise ? -1f : 1f;

        public static int LifeSeedBurstCount(long tick, int minCount, int maxCount)
        {
            int min = Mathf.Max(1, minCount);
            int max = Mathf.Max(min, maxCount);
            uint x = HashTick(tick, 0x9E3779B9u);
            return min + (int)(x % (uint)(max - min + 1));
        }

        public static bool LifeSeedSpawnsFlora(long tick, int spawnIndex)
        {
            uint x = HashTick(tick, (uint)spawnIndex * 0x85EBCA6Bu + 1u);
            return (x & 1u) == 0u;
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
            float leadTurns = AimLeadSign(lastTravelMode) * leadDegrees / 360f;
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
                case ProbeAction.Humidity:
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
            ProcessSimTicks();
            SyncPose();
        }

        private void ResetRuntimeState()
        {
            flightMode = ProbeFlightMode.Clockwise;
            lastTravelMode = ProbeFlightMode.Clockwise;
            lifeSeedActive = false;
            pendingLifeSeeds = 0;
            ticksUntilLifeBurst = 0;
            action = ProbeAction.None;
            if (host != null && host.Config != null)
            {
                float periodTicks = host.Config.ticksPerSecond * Mathf.Max(1f, host.Config.probeOrbitPeriodSeconds);
                lastProcessedTick = host.Clock != null ? host.Clock.TickCount : 0L;
                orbitAngle01 = Mathf.Repeat(lastProcessedTick / Mathf.Max(1f, periodTicks), 1f);
                energy = Mathf.Max(0f, host.Config.probeEnergyMax);
            }
            else
            {
                lastProcessedTick = 0L;
                orbitAngle01 = 0f;
                energy = DefaultEnergyMax;
            }
        }

        private void ProcessSimTicks()
        {
            if (host == null || !host.IsReady || host.Config == null || host.Clock == null) return;

            long current = host.Clock.TickCount;
            if (current < lastProcessedTick)
            {
                ResetRuntimeState();
                return;
            }

            long delta = current - lastProcessedTick;
            if (delta <= 0) return;
            if (delta > 8)
            {
                lastProcessedTick = current;
                return;
            }

            SimulationConfig config = host.Config;
            float periodTicks = config.ticksPerSecond * Mathf.Max(1f, config.probeOrbitPeriodSeconds);
            float tickDuration = 1f / Mathf.Max(1f, config.ticksPerSecond);
            bool actionActive = action != ProbeAction.None || lifeSeedActive;
            bool regenAllowed = flightMode != ProbeFlightMode.Stopped;
            for (int i = 0; i < delta; i++)
            {
                lastProcessedTick++;
                orbitAngle01 = AdvanceOrbitAngle01(orbitAngle01, flightMode, 1, periodTicks);
                energy = ApplyEnergyTick(
                    energy,
                    actionActive,
                    regenAllowed,
                    config.probeEnergyActionDrain,
                    config.probeEnergyRegenPerSecond,
                    tickDuration,
                    config.probeEnergyMax);
                if (lifeSeedActive)
                    StepLifeSeed(config);
            }
        }

        private void StepLifeSeed(SimulationConfig config)
        {
            if (host == null || !host.IsReady) return;

            if (pendingLifeSeeds <= 0 && ticksUntilLifeBurst <= 0)
            {
                pendingLifeSeeds = LifeSeedBurstCount(
                    lastProcessedTick,
                    config.probeLifeSeedMinCount,
                    config.probeLifeSeedMaxCount);
                ticksUntilLifeBurst = Mathf.Max(1, Mathf.RoundToInt(config.ticksPerSecond * Mathf.Max(0.1f, config.probeLifeSeedIntervalSeconds)));
            }

            if (pendingLifeSeeds > 0)
            {
                Vector2Int cell = AimCell(host.Grid);
                if (LifeSeedSpawnsFlora(lastProcessedTick, pendingLifeSeeds))
                    host.QueueFloraSeed(cell, 0, config.probeLifeSeedSporeLoad);
                else
                    host.QueueFaunaSeed(cell, 0, MaterialIds.CricketEgg);
                pendingLifeSeeds--;
            }

            if (ticksUntilLifeBurst > 0)
                ticksUntilLifeBurst--;
        }

        private void EnsureBuilt()
        {
            if (probeRenderer != null && probeRoot != null)
            {
                EnsureThrusterBuilt();
                return;
            }
            if (display == null) display = FindFirstObjectByType<PlanetoidDisplayRenderer>();
            if (display == null) return;

            Teardown();

            probeRoot = new GameObject("Probe Orbit") { hideFlags = HideFlags.DontSave }.transform;
            probeRoot.SetParent(display.transform, false);

            var probeObject = new GameObject("Probe") { hideFlags = HideFlags.DontSave };
            probeObject.transform.SetParent(probeRoot, false);
            probeRenderer = probeObject.AddComponent<SpriteRenderer>();
            probeRenderer.sprite = probeSprite;
            probeRenderer.sortingOrder = 60;
            probeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            probeRenderer.receiveShadows = false;

            if (probeSprite == null && !warnedMissingSprite)
            {
                warnedMissingSprite = true;
                Debug.LogWarning("GeneSys probe: Probe-Sprite is not assigned.", this);
            }

            EnsureThrusterBuilt();
        }

        private void EnsureThrusterBuilt()
        {
            if (thrusterSystem != null) return;
            if (probeRenderer == null || thrusterUnavailable) return;

            SafeDestroy(thrusterMaterial);
            SafeDestroy(thrusterTexture);
            thrusterMaterial = null;
            thrusterTexture = null;

            Shader shader = Shader.Find("GeneSys/Space Particle");
            if (shader == null)
            {
                thrusterUnavailable = true;
                Debug.LogWarning("GeneSys probe: Space Particle shader is missing; thruster disabled.", this);
                return;
            }

            thrusterTexture = CreateSoftDiscTexture(64);
            thrusterMaterial = new Material(shader)
            {
                name = "GeneSys Probe Thruster",
                mainTexture = thrusterTexture,
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            thrusterMaterial.SetFloat("_Mode", 0f);
            thrusterMaterial.SetFloat("_Softness", 2.2f);
            thrusterMaterial.SetFloat("_TwinkleStrength", 0.25f);
            thrusterMaterial.SetColor("_BaseColor", Color.white);

            var go = new GameObject("Probe Thruster") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(probeRenderer.transform, false);
            go.transform.localPosition = ThrusterLocalOffset;
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            go.transform.localScale = Vector3.one;

            thrusterSystem = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = thrusterMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.allowRoll = true;
            renderer.enableGPUInstancing = true;
            renderer.sortingOrder = ThrusterSortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var main = thrusterSystem.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 96;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.useUnscaledTime = true;

            var emission = thrusterSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = ThrusterFlightRate;

            var shape = thrusterSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.03f;
            shape.radiusThickness = 1f;
            shape.length = 0.02f;
            shape.rotation = Vector3.zero;

            var colorOverLifetime = thrusterSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.25f, 0.85f, 1f), 0.35f),
                    new GradientColorKey(new Color(0.15f, 0.45f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.75f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = thrusterSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.12f)));

            thrusterSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
            bool reverseTravel = lastTravelMode == ProbeFlightMode.Counterclockwise;
            probeRenderer.transform.localPosition = new Vector3(direction.x * radius, direction.y * radius, -0.05f);
            probeRenderer.transform.localScale = SpriteLocalScale(config.probeSpriteScale, reverseTravel);
            probeRenderer.transform.localRotation = Quaternion.Euler(0f, 0f,
                SpriteRotationZ(ProbeAngle01, config.probeSpriteRotationOffset, reverseTravel));
            UpdateThruster(config);
        }

        private void UpdateThruster(SimulationConfig config)
        {
            if (thrusterSystem == null) return;

            bool enabled = config.enableProbeThruster != 0 &&
                           config.probeThrusterStrength > 0.001f &&
                           probeSprite != null;
            thrusterSystem.gameObject.SetActive(enabled);
            if (!enabled) return;

            if (thrusterMaterial != null)
            {
                float alpha = Mathf.Clamp01(config.probeThrusterStrength);
                thrusterMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            }

            var emission = thrusterSystem.emission;
            float rate = flightMode == ProbeFlightMode.Stopped ? ThrusterIdleRate : ThrusterFlightRate;
            emission.rateOverTime = rate * Mathf.Max(0f, config.probeThrusterStrength);

            if (!thrusterSystem.isPlaying)
                thrusterSystem.Play(true);
        }

        private static Texture2D CreateSoftDiscTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GeneSys Probe Thruster Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            float inv = 1f / (size - 1);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x * inv * 2f - 1f;
                float ny = y * inv * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            texture.Apply(false, true);
            return texture;
        }

        private static uint HashTick(long tick, uint salt)
        {
            unchecked
            {
                uint x = (uint)tick * 747796405u + salt;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                return x;
            }
        }

        public void Teardown()
        {
            SafeDestroy(thrusterMaterial);
            SafeDestroy(thrusterTexture);
            thrusterMaterial = null;
            thrusterTexture = null;
            thrusterSystem = null;
            thrusterUnavailable = false;

            if (probeRenderer != null)
            {
                SafeDestroy(probeRenderer.gameObject);
                probeRenderer = null;
            }
            if (probeRoot != null)
            {
                SafeDestroy(probeRoot.gameObject);
                probeRoot = null;
            }

            if (display != null)
            {
                Transform orbitChild = display.transform.Find("Probe Orbit");
                if (orbitChild != null) SafeDestroy(orbitChild.gameObject);
            }
            else
            {
                var existingProbeOrbit = GameObject.Find("Probe Orbit");
                if (existingProbeOrbit != null) SafeDestroy(existingProbeOrbit);
            }
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void OnDestroy()
        {
            Teardown();
        }
    }
}
