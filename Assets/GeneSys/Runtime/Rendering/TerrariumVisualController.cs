using System.Collections.Generic;
using GeneSys.Configuration;
using GeneSys.Simulation;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeneSys.Rendering
{
    /// <summary>
    /// Owns non-simulation ambient visuals: starfield, nebula, atmospheric rim, and orbiting solar body.
    /// </summary>
    public sealed class TerrariumVisualController : MonoBehaviour
    {
        private static readonly Color[] StarPalette =
        {
            new(0.72f, 0.82f, 1f, 1f),
            new(1f, 0.95f, 0.82f, 1f),
            new(0.85f, 0.92f, 1f, 1f),
            new(1f, 0.78f, 0.55f, 1f),
            new(0.7f, 0.95f, 1f, 1f)
        };

        private static readonly Color[] NebulaPalette =
        {
            new(0.35f, 0.18f, 0.55f, 0.22f),
            new(0.15f, 0.28f, 0.55f, 0.18f),
            new(0.45f, 0.12f, 0.28f, 0.16f),
            new(0.12f, 0.35f, 0.4f, 0.14f)
        };

        [SerializeField] private SimulationHost host;
        [SerializeField] private PlanetoidDisplayRenderer display;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Shader spaceParticleShader;
        [SerializeField] private Shader atmosphereGlowShader;
        [SerializeField] private Shader solarBodyShader;
        [SerializeField] private Shader moltenCoreShader;

        private Transform visualsRoot;
        private Transform solarRoot;
        private Transform coreRoot;
        private ParticleSystem starSystem;
        private ParticleSystem nebulaSystem;
        private ParticleSystemRenderer starRenderer;
        private ParticleSystemRenderer nebulaRenderer;
        private MeshRenderer atmosphereRenderer;
        private MeshRenderer atmosphereRendererSecondary;
        private MeshRenderer solarRenderer;
        private MeshRenderer coreRenderer;
        private Material starMaterial;
        private Material nebulaMaterial;
        private Material atmosphereMaterial;
        private Material atmosphereMaterialSecondary;
        private Material solarMaterial;
        private Material coreMaterial;
        private Texture2D softParticleTexture;
        private readonly List<Vector4> customDataScratch = new(512);
        private readonly List<ParticleSystemVertexStream> starStreams = new(8);
        private int lastStarCount = -1;
        private int lastNebulaCount = -1;
        private Vector2 starSpawnExtents;
        private float starfieldReferenceOrtho = -1f;
        private bool built;

        // 0 = screen-locked backdrop, 1 = stars zoom 1:1 with the camera.
        private const float StarfieldZoomFollow = 0.12f;
        private const float StarfieldSpawnMargin = 2.75f;

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer planetoidDisplay)
        {
            host = simulationHost;
            display = planetoidDisplay;
            if (targetCamera == null)
                targetCamera = display != null ? display.TargetCamera : Camera.main;
            EnsureBuilt();
            ApplySettings(forceRebuildParticles: true);
        }

        private void LateUpdate()
        {
            if (host == null || !host.IsReady) return;
            EnsureBuilt();
            if (!built) return;
            ApplySettings(forceRebuildParticles: false);
            UpdateSolarPose();
            FollowCameraFrustum();
        }

        private void EnsureBuilt()
        {
            if (built && visualsRoot != null) return;
            Teardown();

            if (spaceParticleShader == null) spaceParticleShader = Shader.Find("GeneSys/Space Particle");
            if (atmosphereGlowShader == null) atmosphereGlowShader = Shader.Find("GeneSys/Atmosphere Glow");
            if (solarBodyShader == null) solarBodyShader = Shader.Find("GeneSys/Solar Body");
            if (moltenCoreShader == null) moltenCoreShader = Shader.Find("GeneSys/Molten Core");
            if (spaceParticleShader == null || atmosphereGlowShader == null || solarBodyShader == null || moltenCoreShader == null)
            {
                Debug.LogError("GeneSys visuals: one or more shaders are missing.", this);
                return;
            }

            softParticleTexture = CreateSoftDiscTexture(64);
            visualsRoot = new GameObject("Terrarium Visuals") { hideFlags = HideFlags.DontSave }.transform;
            visualsRoot.SetParent(null, false);

            starMaterial = CreateParticleMaterial("GeneSys Star Material", 0f);
            nebulaMaterial = CreateParticleMaterial("GeneSys Nebula Material", 1f);
            atmosphereMaterial = new Material(atmosphereGlowShader) { name = "GeneSys Atmosphere Glow Runtime", hideFlags = HideFlags.DontSave };
            atmosphereMaterialSecondary = new Material(atmosphereGlowShader) { name = "GeneSys Atmosphere Glow Secondary Runtime", hideFlags = HideFlags.DontSave };
            solarMaterial = new Material(solarBodyShader) { name = "GeneSys Solar Body Runtime", hideFlags = HideFlags.DontSave };
            coreMaterial = new Material(moltenCoreShader) { name = "GeneSys Molten Core Runtime", hideFlags = HideFlags.DontSave };

            starSystem = CreateParticleLayer("Starfield", visualsRoot, starMaterial, out starRenderer, 512);
            nebulaSystem = CreateParticleLayer("Nebula", visualsRoot, nebulaMaterial, out nebulaRenderer, 64);
            ConfigureRenderer(starRenderer, 10);
            ConfigureRenderer(nebulaRenderer, 5);
            SetupStarVertexStreams(starRenderer);

            atmosphereRenderer = CreateQuad("Atmosphere Glow", visualsRoot, atmosphereMaterial, 15);
            atmosphereRendererSecondary = CreateQuad("Atmosphere Glow Secondary", visualsRoot, atmosphereMaterialSecondary, 14);

            Transform solarParent = display != null ? display.transform : visualsRoot;
            solarRoot = new GameObject("Solar Orbit") { hideFlags = HideFlags.DontSave }.transform;
            solarRoot.SetParent(solarParent, false);
            solarRenderer = CreateQuad("Solar Body", solarRoot, solarMaterial, 20);
            solarRenderer.transform.localScale = Vector3.one * 0.12f;

            Transform coreParent = display != null ? display.transform : visualsRoot;
            coreRoot = new GameObject("Planetary Core") { hideFlags = HideFlags.DontSave }.transform;
            coreRoot.SetParent(coreParent, false);
            coreRoot.localPosition = new Vector3(0f, 0f, -0.01f);
            coreRenderer = CreateQuad("Molten Core", coreRoot, coreMaterial, 55);
            coreRenderer.transform.localPosition = Vector3.zero;
            coreRenderer.transform.localRotation = Quaternion.identity;

            built = true;
        }

        private Material CreateParticleMaterial(string name, float mode)
        {
            var material = new Material(spaceParticleShader)
            {
                name = name,
                mainTexture = softParticleTexture,
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat("_Mode", mode);
            material.SetFloat("_Softness", mode < 0.5f ? 2.8f : 1.35f);
            material.SetFloat("_NoiseScale", 2.75f);
            return material;
        }

        private static ParticleSystem CreateParticleLayer(string name, Transform parent, Material material,
            out ParticleSystemRenderer renderer, int maxParticles)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.allowRoll = true;
            renderer.enableGPUInstancing = true;

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = maxParticles;
            main.startLifetime = Mathf.Infinity;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.useUnscaledTime = true;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var customData = system.customData;
            customData.enabled = true;
            customData.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            customData.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private void SetupStarVertexStreams(ParticleSystemRenderer renderer)
        {
            starStreams.Clear();
            starStreams.Add(ParticleSystemVertexStream.Position);
            starStreams.Add(ParticleSystemVertexStream.Color);
            starStreams.Add(ParticleSystemVertexStream.UV);
            starStreams.Add(ParticleSystemVertexStream.Custom1XYZW);
            renderer.SetActiveVertexStreams(starStreams);
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, int sortingOrder)
        {
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static MeshRenderer CreateQuad(string name, Transform parent, Material material, int sortingOrder)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.sortingOrder = sortingOrder;
            return meshRenderer;
        }

        private void ApplySettings(bool forceRebuildParticles)
        {
            SimulationConfig config = host.Config;
            bool starsEnabled = config.enableStarfield != 0 && config.starfieldStrength > 0.001f;
            bool nebulaEnabled = config.enableNebula != 0 && config.nebulaStrength > 0.001f;
            bool glowEnabled = config.enableAtmosphereGlow != 0 && config.atmosphereGlowStrength > 0.001f;
            bool solarEnabled = config.enableSolarBody != 0 &&
                                (config.solarBodyStrength > 0.001f || config.solarCoronaStrength > 0.001f);

            if (starSystem != null)
            {
                starSystem.gameObject.SetActive(starsEnabled);
                if (starsEnabled && (forceRebuildParticles || lastStarCount != config.starCount))
                {
                    RebuildStars(config.starCount);
                    lastStarCount = config.starCount;
                }
                if (starMaterial != null)
                {
                    starMaterial.SetFloat("_TwinkleStrength", config.starTwinkleStrength);
                    starMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, Mathf.Clamp01(config.starfieldStrength)));
                }
            }

            if (nebulaSystem != null)
            {
                nebulaSystem.gameObject.SetActive(nebulaEnabled);
                if (nebulaEnabled && (forceRebuildParticles || lastNebulaCount != config.nebulaCount))
                {
                    RebuildNebula(config.nebulaCount);
                    lastNebulaCount = config.nebulaCount;
                }
                if (nebulaMaterial != null)
                    nebulaMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, Mathf.Clamp01(config.nebulaStrength)));
            }

            if (atmosphereRenderer != null)
            {
                atmosphereRenderer.gameObject.SetActive(glowEnabled);
                if (glowEnabled && atmosphereMaterial != null && display != null)
                {
                    float planetScale = display.transform.lossyScale.x;
                    Vector3 planetPos = display.transform.position;
                    atmosphereRenderer.transform.SetPositionAndRotation(
                        new Vector3(planetPos.x, planetPos.y, planetPos.z + 0.08f),
                        display.transform.rotation);
                    atmosphereRenderer.transform.localScale = Vector3.one * (planetScale * 1.28f);
                    atmosphereMaterial.SetColor("_GlowColor", new Color(0.4f, 0.72f, 1f, 1f));
                    atmosphereMaterial.SetFloat("_InnerRadius", 0.78f);
                    atmosphereMaterial.SetFloat("_OuterRadius", 1f);
                    atmosphereMaterial.SetFloat("_Intensity", config.atmosphereGlowStrength);
                    atmosphereMaterial.SetFloat("_Softness", 1.55f);
                    atmosphereMaterial.SetFloat("_PixelScale", config.atmosphereGlowPixelScale);
                    atmosphereMaterial.SetFloat("_RayCount", config.atmosphereGlowRayCount);
                    atmosphereMaterial.SetFloat("_Speed", 1.0f);
                }
            }

            if (atmosphereRendererSecondary != null)
            {
                atmosphereRendererSecondary.gameObject.SetActive(glowEnabled);
                if (glowEnabled && atmosphereMaterialSecondary != null && display != null)
                {
                    float planetScale = display.transform.lossyScale.x;
                    Vector3 planetPos = display.transform.position;
                    atmosphereRendererSecondary.transform.SetPositionAndRotation(
                        new Vector3(planetPos.x, planetPos.y, planetPos.z + 0.082f),
                        display.transform.rotation);
                    atmosphereRendererSecondary.transform.localScale = Vector3.one * (planetScale * 1.32f);
                    atmosphereMaterialSecondary.SetColor("_GlowColor", new Color(0.45f, 0.8f, 1f, 1f));
                    atmosphereMaterialSecondary.SetFloat("_InnerRadius", 0.76f);
                    atmosphereMaterialSecondary.SetFloat("_OuterRadius", 1.02f);
                    atmosphereMaterialSecondary.SetFloat("_Intensity", config.atmosphereGlowStrength * 0.75f);
                    atmosphereMaterialSecondary.SetFloat("_Softness", 1.75f);
                    atmosphereMaterialSecondary.SetFloat("_PixelScale", config.atmosphereGlowPixelScale * 0.85f);
                    atmosphereMaterialSecondary.SetFloat("_RayCount", Mathf.Max(3f, config.atmosphereGlowRayCount - 2f));
                    atmosphereMaterialSecondary.SetFloat("_Speed", 0.5f);
                }
            }

            if (solarRoot != null)
            {
                solarRoot.gameObject.SetActive(solarEnabled);
                if (solarEnabled && solarMaterial != null)
                {
                    solarMaterial.SetFloat("_CoreIntensity", config.solarBodyStrength);
                    solarMaterial.SetFloat("_CoronaIntensity", config.solarCoronaStrength);
                    solarMaterial.SetFloat("_CoreRadius", 0.28f);
                    solarMaterial.SetFloat("_CoronaRadius", 1f);
                    float sunScale = 0.12f * Mathf.Lerp(0.8f, 1.4f,
                        Mathf.Clamp01(config.solarBodyStrength * 0.5f + config.solarCoronaStrength * 0.5f));
                    solarRenderer.transform.localScale = Vector3.one * sunScale;
                }
            }

            bool coreEnabled = config.enableCoreVisual != 0 && config.coreVisualStrength > 0.001f;
            if (coreRoot != null)
            {
                coreRoot.gameObject.SetActive(coreEnabled);
                if (coreEnabled && coreMaterial != null && display != null)
                {
                    PolarGridDefinition gridDef = host.Grid;
                    float displayedCore = gridDef.visualCoreRadius * gridDef.visualCoreSquash;
                    // Quad spans [-0.5, 0.5] in local display space; diameter of core disc is 2.0 * displayedCore
                    float coreScale = (displayedCore * 2.0f) * config.coreVisualScale;
                    coreRenderer.transform.localScale = Vector3.one * coreScale;
                    coreMaterial.SetFloat("_Intensity", config.coreVisualStrength);
                    coreMaterial.SetFloat("_CirculationSpeed", config.coreCirculationSpeed * (0.65f + config.geodynamicsConvectionStrength));
                    coreMaterial.SetFloat("_HeatGlow", config.coreHeatGlow);
                    coreMaterial.SetFloat("_SimHeatGlow", 0.7f + config.geodynamicsHeatCoupling + config.coreHeatRate * 0.4f);
                    coreMaterial.SetFloat("_SimCirculation", 0.5f + config.geodynamicsConvectionStrength * 2f);
                    coreMaterial.SetFloat("_CoreRadius", 0.85f);
                    coreMaterial.SetFloat("_EdgeSoftness", 1.6f);
                }
            }
        }

        private void RebuildStars(int count)
        {
            count = Mathf.Clamp(count, 32, 512);
            var particles = new ParticleSystem.Particle[count];
            Vector2 extents = GetViewExtents();
            starSpawnExtents = new Vector2(extents.x * (1f + StarfieldSpawnMargin), extents.y * (1f + StarfieldSpawnMargin));
            if (targetCamera != null && targetCamera.orthographic)
                starfieldReferenceOrtho = targetCamera.orthographicSize;
            customDataScratch.Clear();
            for (int i = 0; i < count; i++)
            {
                Color tint = StarPalette[i % StarPalette.Length];
                float size = Random.Range(0.02f, 0.075f);
                particles[i].position = RandomPointInView(extents, StarfieldSpawnMargin);
                particles[i].startSize3D = new Vector3(size, size, size);
                particles[i].startColor = Color.Lerp(tint, Color.white, Random.Range(0f, 0.35f));
                particles[i].remainingLifetime = float.PositiveInfinity;
                particles[i].startLifetime = float.PositiveInfinity;
                particles[i].rotation = Random.Range(0f, 360f);
                customDataScratch.Add(new Vector4(Random.Range(0f, Mathf.PI * 2f), Random.Range(0.35f, 2.4f), 0f, 0f));
            }

            starSystem.Clear(true);
            var main = starSystem.main;
            if (main.maxParticles < count) main.maxParticles = count;
            starSystem.SetParticles(particles, count);
            int alive = starSystem.particleCount;
            if (customDataScratch.Count > alive)
                customDataScratch.RemoveRange(alive, customDataScratch.Count - alive);
            starSystem.SetCustomParticleData(customDataScratch, ParticleSystemCustomData.Custom1);
            starSystem.Play(true);
            UpdateStarfieldZoom();
        }

        private void RebuildNebula(int count)
        {
            count = Mathf.Clamp(count, 4, 48);
            var particles = new ParticleSystem.Particle[count];
            Vector2 extents = GetViewExtents();
            for (int i = 0; i < count; i++)
            {
                Color tint = NebulaPalette[i % NebulaPalette.Length];
                float size = Random.Range(2.5f, 6.5f);
                particles[i].position = RandomPointInView(extents, 0.55f);
                particles[i].startSize3D = new Vector3(size, size * Random.Range(0.65f, 1.2f), size);
                particles[i].startColor = tint;
                particles[i].remainingLifetime = float.PositiveInfinity;
                particles[i].startLifetime = float.PositiveInfinity;
                particles[i].rotation = Random.Range(0f, 360f);
                particles[i].angularVelocity = Random.Range(-4f, 4f);
                particles[i].velocity = new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(-0.015f, 0.015f), 0f);
            }

            nebulaSystem.Clear(true);
            nebulaSystem.SetParticles(particles, count);
            nebulaSystem.Play(true);
        }

        private void UpdateSolarPose()
        {
            if (solarRoot == null || display == null || solarRenderer == null || !solarRoot.gameObject.activeSelf)
                return;

            float orbit = Mathf.Max(0.5f, host.Config.solarOrbitRadius);
            // Local units: planetoid quad spans [-0.5, 0.5], so radius 0.5 is the disc edge.
            float radius = 0.5f * orbit;
            float angle = host.SolarAngle01 * Mathf.PI * 2f;
            solarRoot.localPosition = Vector3.zero;
            solarRoot.localRotation = Quaternion.identity;
            // Sit slightly behind the disc so the corona never overdraws simulation cells.
            solarRenderer.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.12f);
            solarRenderer.transform.localRotation = Quaternion.identity;

            if (solarMaterial != null)
            {
                float polar = PolarPoleGeometry.SolarPolarOutput(
                    host.SolarAngle01,
                    PolarPoleGeometry.PoleAngle01(host.Config.seed),
                    host.Config.solarPolarOutputMin);
                solarMaterial.SetFloat("_CoreIntensity", host.Config.solarBodyStrength * polar);
                solarMaterial.SetFloat("_CoronaIntensity", host.Config.solarCoronaStrength * polar);
            }
        }

        private void FollowCameraFrustum()
        {
            if (targetCamera == null || visualsRoot == null) return;
            Transform cam = targetCamera.transform;
            float backdropZ = display != null ? display.transform.position.z + 0.35f : 0.35f;
            visualsRoot.position = new Vector3(cam.position.x, cam.position.y, backdropZ);
            visualsRoot.rotation = Quaternion.identity;

            UpdateStarfieldZoom();
            if (nebulaSystem != null && nebulaSystem.gameObject.activeSelf)
                ClampParticlesToView(nebulaSystem, 0.65f);
        }

        private void UpdateStarfieldZoom()
        {
            if (starSystem == null || !starSystem.gameObject.activeSelf) return;
            if (targetCamera == null || !targetCamera.orthographic) return;
            if (starSpawnExtents.y <= 0.001f) return;

            float ortho = targetCamera.orthographicSize;
            if (starfieldReferenceOrtho <= 0.001f)
                starfieldReferenceOrtho = ortho;

            float follow = Mathf.Pow(ortho / starfieldReferenceOrtho, StarfieldZoomFollow);
            float cover = ortho / starSpawnExtents.y;
            starSystem.transform.localScale = Vector3.one * Mathf.Max(follow, cover);
        }

        private void ClampParticlesToView(ParticleSystem system, float margin)
        {
            int count = system.particleCount;
            if (count <= 0) return;
            var particles = new ParticleSystem.Particle[count];
            system.GetParticles(particles, count);
            Vector2 extents = GetViewExtents();
            bool dirty = false;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = particles[i].position;
                bool wrapped = false;
                if (p.x < -extents.x - margin) { p.x = extents.x + margin; wrapped = true; }
                else if (p.x > extents.x + margin) { p.x = -extents.x - margin; wrapped = true; }
                if (p.y < -extents.y - margin) { p.y = extents.y + margin; wrapped = true; }
                else if (p.y > extents.y + margin) { p.y = -extents.y - margin; wrapped = true; }
                if (wrapped)
                {
                    particles[i].position = p;
                    dirty = true;
                }
            }
            if (dirty) system.SetParticles(particles, count);
        }

        private Vector2 GetViewExtents()
        {
            if (targetCamera == null || !targetCamera.orthographic)
                return new Vector2(8f, 5f);
            float height = targetCamera.orthographicSize;
            float width = height * Mathf.Max(0.1f, targetCamera.aspect);
            return new Vector2(width, height);
        }

        private Vector3 RandomPointInView(Vector2 extents, float marginScale)
        {
            float mx = extents.x * (1f + marginScale);
            float my = extents.y * (1f + marginScale);
            return new Vector3(Random.Range(-mx, mx), Random.Range(-my, my), Random.Range(-0.2f, 0.2f));
        }

        private static Texture2D CreateSoftDiscTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GeneSys Soft Particle",
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

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        public void Teardown()
        {
            built = false;
            SafeDestroy(starMaterial);
            SafeDestroy(nebulaMaterial);
            SafeDestroy(atmosphereMaterial);
            SafeDestroy(atmosphereMaterialSecondary);
            SafeDestroy(solarMaterial);
            SafeDestroy(coreMaterial);
            SafeDestroy(softParticleTexture);
            starMaterial = null;
            nebulaMaterial = null;
            atmosphereMaterial = null;
            atmosphereMaterialSecondary = null;
            solarMaterial = null;
            coreMaterial = null;
            softParticleTexture = null;

            if (visualsRoot != null)
            {
                SafeDestroy(visualsRoot.gameObject);
                visualsRoot = null;
            }
            if (solarRoot != null)
            {
                SafeDestroy(solarRoot.gameObject);
                solarRoot = null;
            }
            if (coreRoot != null)
            {
                SafeDestroy(coreRoot.gameObject);
                coreRoot = null;
            }

            if (display != null)
            {
                Transform solarChild = display.transform.Find("Solar Orbit");
                if (solarChild != null) SafeDestroy(solarChild.gameObject);
                Transform coreChild = display.transform.Find("Planetary Core");
                if (coreChild != null) SafeDestroy(coreChild.gameObject);
            }
            else
            {
                var solarChild = GameObject.Find("Solar Orbit");
                if (solarChild != null) SafeDestroy(solarChild);
                var coreChild = GameObject.Find("Planetary Core");
                if (coreChild != null) SafeDestroy(coreChild);
            }

            var existingVisuals = GameObject.Find("Terrarium Visuals");
            if (existingVisuals != null)
                SafeDestroy(existingVisuals);
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void OnDestroy()
        {
            Teardown();
        }

        public static Vector2 SolarDirectionFromAngle01(float solarAngle01)
        {
            float angle = Mathf.Repeat(solarAngle01, 1f) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static float SolarInsolation(float theta01, float solarAngle01)
        {
            float wrapped = Mathf.Repeat(theta01 - solarAngle01, 1f);
            float dist = Mathf.Min(wrapped, 1f - wrapped);
            return Mathf.Clamp01(1f - dist * 4f);
        }
    }
}
