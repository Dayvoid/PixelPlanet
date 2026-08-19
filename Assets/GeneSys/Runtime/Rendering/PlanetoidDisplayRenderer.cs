using GeneSys.Materials;
using GeneSys.Simulation.Gpu;
using GeneSys.Simulation.Topology;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GeneSys.Rendering
{
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
        private SimulationResources resources;
        private PolarGridDefinition grid;
        private Vector2 lastPointer;
        private bool dragging;

        public int OverlayMode { get; private set; }
        public Camera TargetCamera => targetCamera;

        public void Initialize(SimulationResources state, MaterialRegistry registry, PolarGridDefinition definition)
        {
            DestroyRuntimeAssets();
            resources = state;
            grid = definition;
            meshRenderer = GetComponent<MeshRenderer>();
            if (targetCamera == null) targetCamera = Camera.main;
            displayMaterial = new Material(displayShader) { name = "GeneSys Planetoid Runtime" };
            palette = new Texture2D(MaterialRegistry.MaxMaterials, 1, TextureFormat.RGBA32, false, true)
            {
                name = "GeneSys Runtime Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color[MaterialRegistry.MaxMaterials];
            foreach (MaterialDefinition definitionAsset in registry.materials)
                if (definitionAsset != null && definitionAsset.stableId < colors.Length) colors[definitionAsset.stableId] = definitionAsset.displayColor;
            palette.SetPixels(colors);
            palette.Apply(false, true);
            properties = new Texture2D(MaterialRegistry.MaxMaterials, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "GeneSys Runtime Material Properties",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var propertyColors = new Color[MaterialRegistry.MaxMaterials];
            foreach (MaterialDefinition definitionAsset in registry.materials)
                if (definitionAsset != null && definitionAsset.stableId < propertyColors.Length)
                    propertyColors[definitionAsset.stableId] = new Color(
                        definitionAsset.toxicity,
                        definitionAsset.caloricContent,
                        definitionAsset.porosity,
                        definitionAsset.density);
            properties.SetPixels(propertyColors);
            properties.Apply(false, true);
            displayMaterial.SetTexture("_Palette", palette);
            displayMaterial.SetTexture("_Properties", properties);
            displayMaterial.SetFloat("_VisualCoreRadius", grid.visualCoreRadius);
            displayMaterial.SetFloat("_VisualCoreSquash", grid.visualCoreSquash);
            displayMaterial.SetFloat("_AtmosphereStartRadius", grid.atmosphereStartRadius);
            meshRenderer.sharedMaterial = displayMaterial;
            RefreshTextures();
        }

        public void SetOverlay(int mode)
        {
            OverlayMode = Mathf.Clamp(mode, 0, 15);
            if (displayMaterial != null) displayMaterial.SetInt("_OverlayMode", OverlayMode);
        }

        private void LateUpdate()
        {
            if (resources == null || displayMaterial == null) return;
            RefreshTextures();
            HandleCamera();
        }

        private void RefreshTextures()
        {
            displayMaterial.SetTexture("_MaterialTex", resources.MaterialRead);
            displayMaterial.SetTexture("_StateTex", resources.StateRead);
            displayMaterial.SetTexture("_FlowTex", resources.FlowRead);
            displayMaterial.SetTexture("_AuxTex", resources.AuxRead);
        }

        private void HandleCamera()
        {
            if (targetCamera == null || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (Mouse.current.middleButton.wasPressedThisFrame) { dragging = true; lastPointer = pointer; }
            if (Mouse.current.middleButton.wasReleasedThisFrame) dragging = false;
            if (dragging)
            {
                Vector2 delta = pointer - lastPointer;
                targetCamera.transform.position -= new Vector3(delta.x, delta.y, 0f) * (panSpeed * targetCamera.orthographicSize);
                lastPointer = pointer;
            }
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize * (1f - scroll * zoomSpeed), 0.75f, 20f);
            if (Keyboard.current != null)
            {
                float rotation = 0f;
                if (Keyboard.current.qKey.isPressed) rotation += rotateSpeed * Time.unscaledDeltaTime;
                if (Keyboard.current.eKey.isPressed) rotation -= rotateSpeed * Time.unscaledDeltaTime;
                transform.Rotate(0f, 0f, rotation);
            }
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
            displayMaterial = null;
            palette = null;
            properties = null;
        }
    }
}
