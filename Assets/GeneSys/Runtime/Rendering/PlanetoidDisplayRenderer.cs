using System;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Simulation;
using GeneSys.Simulation.Climate;
using GeneSys.Simulation.Geodynamics;
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
        private Camera visionCamera;
        private RenderTexture visionTarget;

        public const float MinOrthographicSize = 0.75f;
        public const float MaxOrthographicSize = 20f;
        public static float VisionFollowOrthographicSize => Mathf.Lerp(MinOrthographicSize, MaxOrthographicSize, 0.5f);

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
            meshRenderer.enabled = true;
            RefreshTextures();
        }

        public void SetOverlay(int mode)
        {
            OverlayMode = Mathf.Clamp(mode, 0, GeodynamicsVisuals.MaxOverlayMode);
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
                    targetCamera.orthographicSize = Mathf.Clamp(config.probeFollowZoom, MinOrthographicSize, MaxOrthographicSize);
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
            if (resources == null || !resources.IsCreated || displayMaterial == null) return;
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
            if (config != null)
            {
                displayMaterial.SetFloat("_VaporCapacityScale", config.vaporCapacityScale);
            }
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
            displayMaterial.SetTexture("_FloraTex", resources.FloraRead);
            displayMaterial.SetTexture("_GrassTex", resources.GrassRead);
            displayMaterial.SetTexture("_WaspTex", resources.WaspRead);
            displayMaterial.SetTexture("_TreeTex", resources.TreeRead);
            displayMaterial.SetTexture("_AcousticTex", resources.AcousticRead);
            displayMaterial.SetTexture("_LightTex", resources.LightField);
            if (resources.ClimateState != null)
                displayMaterial.SetBuffer("_ClimateState", resources.ClimateState);
            displayMaterial.SetInt("_ClimateBins", config != null ? ClimateGrid.ClampBinCount(config.climateBinCount) : ClimateGrid.DefaultBins);
            if (resources.GeodynamicsStateRead != null)
                displayMaterial.SetBuffer("_GeodynamicsState", resources.GeodynamicsStateRead);
            if (resources.GeodynamicsEvents != null)
                displayMaterial.SetBuffer("_GeodynamicsEvents", resources.GeodynamicsEvents);
            displayMaterial.SetInt("_GeodynamicsAngularBins", config != null ? GeodynamicsGrid.ClampAngularBins(config.geodynamicsAngularBins) : GeodynamicsGrid.DefaultAngularBins);
            displayMaterial.SetInt("_GeodynamicsRadialBins", config != null ? GeodynamicsGrid.ClampRadialBins(config.geodynamicsRadialBins) : GeodynamicsGrid.DefaultRadialBins);
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
                    targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * (1f - scroll * zoomSpeed), MinOrthographicSize, MaxOrthographicSize);
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

        public bool TryCaptureProbeFollowVision(int width, int height, out byte[] jpeg, out string error)
        {
            jpeg = null;
            error = null;
            width = Mathf.Clamp(width, 64, 1920);
            height = Mathf.Clamp(height, 64, 1080);
            EnsureVisionCamera(width, height);
            if (visionCamera == null)
            {
                error = "Vision camera is unavailable.";
                return false;
            }

            PoseVisionCamera(VisionFollowOrthographicSize);
            RenderTexture previous = RenderTexture.active;
            Texture2D frame = null;
            try
            {
                visionCamera.targetTexture = visionTarget;
                visionCamera.enabled = true;
                visionCamera.Render();
                visionCamera.enabled = false;
                RenderTexture.active = visionTarget;
                frame = new Texture2D(width, height, TextureFormat.RGB24, false)
                {
                    name = "GeneSys AI Vision Frame",
                    filterMode = FilterMode.Bilinear
                };
                frame.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                frame.Apply(false, false);
                jpeg = frame.EncodeToJPG(78);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                jpeg = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (visionCamera != null)
                {
                    visionCamera.targetTexture = null;
                    visionCamera.enabled = false;
                }
                if (frame != null) Destroy(frame);
            }

            if (jpeg == null || jpeg.Length == 0)
            {
                error = "Vision capture produced no image.";
                return false;
            }

            return true;
        }

        private void EnsureVisionCamera(int width, int height)
        {
            if (visionTarget != null && (visionTarget.width != width || visionTarget.height != height))
            {
                visionTarget.Release();
                Destroy(visionTarget);
                visionTarget = null;
            }

            if (visionTarget == null)
            {
                visionTarget = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    name = "GeneSys AI Vision Target",
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear
                };
                visionTarget.Create();
            }

            if (visionCamera != null) return;
            var cameraObject = new GameObject("AI Vision Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            visionCamera = cameraObject.AddComponent<Camera>();
            visionCamera.enabled = false;
            visionCamera.orthographic = true;
            visionCamera.allowHDR = false;
            visionCamera.allowMSAA = false;
            visionCamera.depth = -100;
            if (targetCamera != null)
            {
                visionCamera.clearFlags = targetCamera.clearFlags;
                visionCamera.backgroundColor = targetCamera.backgroundColor;
                visionCamera.cullingMask = targetCamera.cullingMask;
                visionCamera.nearClipPlane = targetCamera.nearClipPlane;
                visionCamera.farClipPlane = targetCamera.farClipPlane;
                visionCamera.orthographicSize = targetCamera.orthographicSize;
            }
            else
            {
                visionCamera.clearFlags = CameraClearFlags.SolidColor;
                visionCamera.backgroundColor = new Color(0.005f, 0.008f, 0.014f, 1f);
                visionCamera.nearClipPlane = 0.1f;
                visionCamera.farClipPlane = 100f;
            }
        }

        private void PoseVisionCamera(float orthographicSize)
        {
            if (visionCamera == null) return;
            if (FollowProbe == null)
                FollowProbe = FindFirstObjectByType<ProbeController>();
            FollowProbe?.SyncPose();

            Vector3 world = FollowProbe != null ? FollowProbe.WorldPosition : Vector3.zero;
            if (world.sqrMagnitude < 0.0001f && FollowProbe != null)
                world = transform.TransformPoint(FollowProbe.LocalOrbitPosition);

            Vector3 up = world - transform.position;
            up.z = 0f;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 forward = targetCamera != null ? targetCamera.transform.forward : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f)
                forward = Vector3.forward;
            visionCamera.orthographicSize = orthographicSize;
            visionCamera.transform.rotation = Quaternion.LookRotation(forward, up);
            float topAnchor = orthographicSize * 0.72f;
            float z = targetCamera != null ? targetCamera.transform.position.z : -10f;
            Vector3 offset = up * topAnchor;
            visionCamera.transform.position = new Vector3(world.x - offset.x, world.y - offset.y, z);
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

        public void ClearResources()
        {
            resources = null;
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = null;
                meshRenderer.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (meshRenderer != null)
                meshRenderer.enabled = false;
        }

        private void OnEnable()
        {
            if (meshRenderer != null && displayMaterial != null && resources != null && resources.IsCreated)
                meshRenderer.enabled = true;
        }

        private void OnDestroy()
        {
            DestroyRuntimeAssets();
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private void DestroyRuntimeAssets()
        {
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = null;
                meshRenderer.enabled = false;
            }
            resources = null;
            SafeDestroy(displayMaterial);
            SafeDestroy(palette);
            SafeDestroy(properties);
            SafeDestroy(categories);
            if (visionCamera != null) SafeDestroy(visionCamera.gameObject);
            if (visionTarget != null)
            {
                visionTarget.Release();
                SafeDestroy(visionTarget);
            }
            displayMaterial = null;
            palette = null;
            properties = null;
            categories = null;
            visionCamera = null;
            visionTarget = null;
        }
    }
}
