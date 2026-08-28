using System;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GeneSys.Rendering
{
    public enum CameraViewMode
    {
        Globe,
        ProbeFollow
    }

    [RequireComponent(typeof(MeshRenderer))]
    public sealed class PlanetoidDisplayRenderer : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Shader displayShader;
        [SerializeField] private float zoomSpeed = 0.02f;
        [SerializeField] private float panSpeed = 0.002f;
        [SerializeField] private float rotateSpeed = 40f;

        private MeshRenderer meshRenderer;
        private Material displayMaterial;
        private Texture2D palette;
        private Texture2D properties;
        private Texture2D categories;
        private SimulationResources resources;
        private PolarGridDefinition grid;
        private SimulationConfig config;
        private SimulationHost host;
        private Vector2 lastPointer;
        private bool dragging;
        private CameraViewMode cameraViewMode = CameraViewMode.Globe;
        private Vector3 savedGlobeCameraPosition;
        private float savedGlobeOrthoSize = 5.5f;
        private Quaternion savedGlobeDisplayRotation = Quaternion.identity;
        private bool hasGlobeCameraState;

        public int OverlayMode { get; private set; }
        public Camera TargetCamera => targetCamera;
        public ProbeController FollowProbe { get; set; }
        public CameraViewMode ViewMode => cameraViewMode;
        public Material RuntimeMaterial => displayMaterial;
        /// <summary>When set, returns true if world zoom/pan should ignore the pointer (e.g. over UI).</summary>
        public Func<Vector2, bool> ShouldBlockWorldInput { get; set; }

        public void Initialize(SimulationResources state, MaterialRegistry registry, PolarGridDefinition definition)
            => Initialize(state, registry, definition, null, null);

        public void Initialize(SimulationResources state, MaterialRegistry registry, PolarGridDefinition definition,
            SimulationConfig simulationConfig, SimulationHost simulationHost)
        {
            DestroyRuntimeAssets();
            resources = state;
            grid = definition;
            config = simulationConfig;
            host = simulationHost;
            meshRenderer = GetComponent<MeshRenderer>();
            if (targetCamera == null) targetCamera = Camera.main;
            displayMaterial = new Material(displayShader) { name = "GeneSys Planetoid Runtime" };
            const int shadeRows = 3;
            palette = new Texture2D(MaterialRegistry.MaxMaterials, shadeRows, TextureFormat.RGBA32, false, true)
            {
                name = "GeneSys Runtime Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color[MaterialRegistry.MaxMaterials * shadeRows];
            foreach (MaterialDefinition definitionAsset in registry.materials)
            {
                if (definitionAsset == null || definitionAsset.stableId >= MaterialRegistry.MaxMaterials) continue;
                int id = definitionAsset.stableId;
                float variation = Mathf.Clamp(definitionAsset.shadeVariation, 0f, 0.5f);
                Color baseColor = definitionAsset.displayColor;
                // Lighten less aggressively than darken — lerp-to-white reads harsher than scale-down.
                Color light = Color.Lerp(baseColor, Color.white, variation * 0.1f);
                light.a = baseColor.a;
                Color dark = baseColor * (1f - variation);
                dark.a = baseColor.a;
                colors[id] = baseColor;
                colors[id + MaterialRegistry.MaxMaterials] = light;
                colors[id + MaterialRegistry.MaxMaterials * 2] = dark;
            }
            palette.SetPixels(colors);
            palette.Apply(false, true);
            properties = new Texture2D(MaterialRegistry.MaxMaterials, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "GeneSys Runtime Material Properties",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var propertyColors = new Color[MaterialRegistry.MaxMaterials];
            categories = new Texture2D(MaterialRegistry.MaxMaterials, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "GeneSys Runtime Material Categories",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var categoryColors = new Color[MaterialRegistry.MaxMaterials];
            foreach (MaterialDefinition definitionAsset in registry.materials)
            {
                if (definitionAsset == null || definitionAsset.stableId >= MaterialRegistry.MaxMaterials) continue;
                propertyColors[definitionAsset.stableId] = new Color(
                    definitionAsset.toxicity,
                    definitionAsset.caloricContent,
                    definitionAsset.porosity,
                    definitionAsset.density);
                categoryColors[definitionAsset.stableId] = new Color((float)definitionAsset.category, 0f, 0f, 0f);
            }
            properties.SetPixels(propertyColors);
            properties.Apply(false, true);
            categories.SetPixels(categoryColors);
            categories.Apply(false, true);
            displayMaterial.SetTexture("_Palette", palette);
            displayMaterial.SetTexture("_Properties", properties);
            displayMaterial.SetTexture("_Categories", categories);
            displayMaterial.SetFloat("_VisualCoreRadius", grid.visualCoreRadius);
            displayMaterial.SetFloat("_VisualCoreSquash", grid.visualCoreSquash);
            displayMaterial.SetFloat("_AtmosphereStartRadius", grid.atmosphereStartRadius);
            displayMaterial.SetInt("_OverlayMode", OverlayMode);
            PushGraphicsUniforms();
            meshRenderer.sharedMaterial = displayMaterial;
            meshRenderer.sortingOrder = 50;
            RefreshTextures();
        }

        public void SetOverlay(int mode)
        {
            OverlayMode = Mathf.Clamp(mode, 0, 25);
            if (displayMaterial != null) displayMaterial.SetInt("_OverlayMode", OverlayMode);
        }

        public void SetCameraViewMode(CameraViewMode mode)
        {
            if (mode == cameraViewMode) return;
            if (mode == CameraViewMode.ProbeFollow)
            {
                SaveGlobeCameraState();
                cameraViewMode = CameraViewMode.ProbeFollow;
                if (targetCamera != null && config != null)
                    targetCamera.orthographicSize = Mathf.Clamp(config.probeFollowZoom, 0.75f, 20f);
                ApplyProbeFollow();
            }
            else
            {
                cameraViewMode = CameraViewMode.Globe;
                RestoreGlobeCameraState();
            }
        }

        private void LateUpdate()
        {
            if (resources == null || displayMaterial == null) return;
            RefreshTextures();
            PushGraphicsUniforms();
            HandleCamera();
            if (cameraViewMode == CameraViewMode.ProbeFollow)
                ApplyProbeFollow();
        }

        private void PushGraphicsUniforms()
        {
            float solarAngle = host != null ? host.SolarAngle01 : 0f;
            float lightingStrength = config != null ? config.dayNightLightingStrength : 0f;
            displayMaterial.SetFloat("_SolarAngle01", solarAngle);
            displayMaterial.SetFloat("_DayNightLightingStrength", lightingStrength);
        }

        private void RefreshTextures()
        {
            displayMaterial.SetTexture("_MaterialTex", resources.MaterialRead);
            displayMaterial.SetTexture("_StateTex", resources.StateRead);
            displayMaterial.SetTexture("_FlowTex", resources.FlowRead);
            displayMaterial.SetTexture("_AuxTex", resources.AuxRead);
            displayMaterial.SetTexture("_ShadeTex", resources.ShadeRead);
            displayMaterial.SetTexture("_EcologyTex", resources.EcologyRead);
            displayMaterial.SetTexture("_CombustionTex", resources.CombustionRead);
            displayMaterial.SetTexture("_StormTex", resources.StormRead);
            displayMaterial.SetTexture("_LifeGenomeTex", resources.LifeGenomeRead);
            displayMaterial.SetTexture("_FaunaTex", resources.FaunaRead);
            displayMaterial.SetTexture("_GrassTex", resources.GrassRead);
            displayMaterial.SetTexture("_AcousticTex", resources.AcousticRead);
            displayMaterial.SetTexture("_LightTex", resources.LightField);
        }

        private void HandleCamera()
        {
            if (targetCamera == null || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            bool overUi = ShouldBlockWorldInput != null && ShouldBlockWorldInput(pointer);

            bool follow = cameraViewMode == CameraViewMode.ProbeFollow;

            if (!follow)
            {
                if (Mouse.current.middleButton.wasPressedThisFrame && !overUi)
                {
                    dragging = true;
                    lastPointer = pointer;
                }
                if (Mouse.current.middleButton.wasReleasedThisFrame) dragging = false;
                if (dragging)
                {
                    Vector2 delta = pointer - lastPointer;
                    targetCamera.transform.position -= new Vector3(delta.x, delta.y, 0f) * (panSpeed * targetCamera.orthographicSize);
                    lastPointer = pointer;
                }
            }
            else if (Mouse.current.middleButton.wasReleasedThisFrame)
            {
                dragging = false;
            }

            if (!overUi)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * (1f - scroll * zoomSpeed), 0.75f, 20f);
            }

            if (!follow && Keyboard.current != null)
            {
                float rotation = 0f;
                if (Keyboard.current.qKey.isPressed) rotation += rotateSpeed * Time.unscaledDeltaTime;
                if (Keyboard.current.eKey.isPressed) rotation -= rotateSpeed * Time.unscaledDeltaTime;
                transform.Rotate(0f, 0f, rotation);
            }
        }

        private void ApplyProbeFollow()
        {
            if (targetCamera == null) return;
            if (FollowProbe == null)
                FollowProbe = FindFirstObjectByType<ProbeController>();
            if (FollowProbe == null) return;

            FollowProbe.SyncPose();
            transform.rotation = Quaternion.Euler(0f, 0f, ProbeController.FollowLockRotationZ(FollowProbe.ProbeAngle01));
            Vector3 world = FollowProbe.WorldPosition;
            if (world.sqrMagnitude < 0.0001f)
                world = transform.TransformPoint(FollowProbe.LocalOrbitPosition);
            Vector3 cameraPosition = targetCamera.transform.position;
            // Pin the probe near the top of the screen: follow lock holds it on +Y, so shift the camera down.
            float topAnchor = targetCamera.orthographicSize * 0.72f;
            targetCamera.transform.position = new Vector3(world.x, world.y - topAnchor, cameraPosition.z);
        }

        private void SaveGlobeCameraState()
        {
            if (targetCamera == null) return;
            savedGlobeCameraPosition = targetCamera.transform.position;
            savedGlobeOrthoSize = targetCamera.orthographicSize;
            savedGlobeDisplayRotation = transform.rotation;
            hasGlobeCameraState = true;
        }

        private void RestoreGlobeCameraState()
        {
            if (!hasGlobeCameraState || targetCamera == null) return;
            targetCamera.transform.position = savedGlobeCameraPosition;
            targetCamera.orthographicSize = savedGlobeOrthoSize;
            transform.rotation = savedGlobeDisplayRotation;
        }

        public bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (targetCamera == null || meshRenderer == null) return false;
            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
            Vector2 uv = new(local.x + 0.5f, local.y + 0.5f);
            return PolarCoordinateTransforms.TryDisplayUvToCell(grid, uv, out cell);
        }

        private void OnDestroy()
        {
            DestroyRuntimeAssets();
        }

        private void DestroyRuntimeAssets()
        {
            if (displayMaterial != null) Destroy(displayMaterial);
            if (palette != null) Destroy(palette);
            if (properties != null) Destroy(properties);
            if (categories != null) Destroy(categories);
            displayMaterial = null;
            palette = null;
            properties = null;
            categories = null;
        }
    }
}
