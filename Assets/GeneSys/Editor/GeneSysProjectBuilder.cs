using System.Collections.Generic;
using System.IO;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Tools;
using GeneSys.UI;
using GeneSys.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using MaterialDefinition = GeneSys.Materials.MaterialDefinition;

namespace GeneSys.Editor
{
    public static class GeneSysProjectBuilder
    {
        private const string Root = "Assets/GeneSys";
        private const string DataRoot = Root + "/Data";
        private const string MaterialRoot = DataRoot + "/Materials";
        private const string ConfigRoot = DataRoot + "/Configs";

        [MenuItem("Tools/GeneSys/Rebuild Phase 1 Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(Root + "/Scenes");

            SimulationConfig config = LoadOrCreate<SimulationConfig>(ConfigRoot + "/SimulationConfig.asset");
            config.ApplyPreset(SimulationPreset.Standard);
            config.seed = 12345;
            config.ticksPerSecond = 20f;
            config.targetOceanCoverage = 0.5f;
            config.minOceanBasins = 2;
            config.maxOceanBasins = 3;
            config.initialGroundwaterSaturation = 0.65f;
            config.initialAtmosphericHumidity = 0.08f;
            EditorUtility.SetDirty(config);

            var definitions = new List<MaterialDefinition>
            {
                Define(0, "Void", MaterialCategory.Empty, new Color(0,0,0,0), 0, 0, 0, 1, 0, 0.01f, 0.01f, 0, 0, 0, -273, 10000, 0, 0, 0, 0, 0, false),
                Define(1, "Air", MaterialCategory.Gas, new Color(0.07f,0.1f,0.16f,1), 0.001f, 0, 0, 1, 0.2f, 0.02f, 1f, 0.001f, 0, 1, -220, -190, 0.01f, 0, 1, 1, 1, false),
                Define(2, "Core", MaterialCategory.Solid, new Color(0.9f,0.16f,0.03f,1), 8, 1, 80, 4, 0, 0.8f, 2f, 0.7f, 0, 0, 1200, 3000, 0.02f, 0.01f, 2, 3, 11, false),
                Define(3, "Mantle", MaterialCategory.Solid, new Color(0.72f,0.08f,0.02f,1), 4.5f, 0.8f, 60, 3, 0, 0.5f, 1.8f, 0.3f, 0.02f, 0.05f, 850, 2600, 0.03f, 0.01f, 3, 6, 11, false),
                Define(4, "Rock", MaterialCategory.Solid, new Color(0.24f,0.25f,0.28f,1), 3, 0.95f, 55, 2, 0, 0.25f, 1.5f, 0.15f, 0.08f, 0.08f, 900, 2400, 0.01f, 0.005f, 4, 6, 11, true, true),
                Define(5, "Basalt", MaterialCategory.Solid, new Color(0.12f,0.11f,0.13f,1), 3.2f, 0.9f, 50, 1.5f, 0, 0.3f, 1.4f, 0.18f, 0.05f, 0.04f, 780, 2400, 0.01f, 0.005f, 5, 6, 11, true, true),
                Define(6, "Magma", MaterialCategory.Magma, new Color(1f,0.22f,0.01f,1), 2.7f, 0.05f, 5, 1, 0.7f, 0.7f, 1.2f, 0.25f, 0, 0, 700, 2200, 0.08f, 0.02f, 5, 6, 11, false, true),
                Define(7, "Soil", MaterialCategory.Granular, new Color(0.28f,0.14f,0.055f,1), 1.5f, 0.15f, 34, 0.8f, 0, 0.12f, 1.1f, 0.04f, 0.75f, 0.55f, 200, 900, 0.02f, 0, 7, 7, 11, true, true, 0.02f, 0.6f),
                Define(8, "Sediment", MaterialCategory.Granular, new Color(0.46f,0.29f,0.13f,1), 1.3f, 0.05f, 25, 0.35f, 0, 0.1f, 1f, 0.03f, 0.65f, 0.65f, 160, 850, 0.02f, 0, 8, 8, 11, true, true, 0.01f, 0.4f),
                Define(9, "Water", MaterialCategory.Liquid, new Color(0.02f,0.32f,0.9f,1), 1, 0, 0, 1, 0.5f, 0.55f, 4.2f, 0.05f, 1, 0, 0, 100, 0.02f, 0, 10, 9, 11, false, true),
                Define(10, "Ice", MaterialCategory.Solid, new Color(0.55f,0.88f,1f,1), 0.92f, 0.65f, 45, 1, 0.3f, 0.35f, 2.1f, 0.01f, 0.1f, 0.05f, 0, 100, 0.02f, 0, 10, 9, 11, true, true),
                Define(11, "Vapor", MaterialCategory.Gas, new Color(0.75f,0.82f,0.9f,1), 0.0006f, 0, 0, 1, 1, 0.025f, 1.9f, 0, 0, 0, 0, 100, 0.05f, 0, 10, 9, 11, false),
                Define(12, "Ash", MaterialCategory.Granular, new Color(0.45f,0.42f,0.38f,1), 0.55f, 0.03f, 22, 0.15f, 0.95f, 0.08f, 0.9f, 0.02f, 0.55f, 0.7f, 1100, 2600, 0.01f, 0, 12, 12, 12, true, false, 0.08f, 0.55f)
            };

            MaterialRegistry registry = LoadOrCreate<MaterialRegistry>(DataRoot + "/MaterialRegistry.asset");
            registry.materials = definitions;
            EditorUtility.SetDirty(registry);

            PanelSettings panelSettings = LoadOrCreate<PanelSettings>(Root + "/UI/GeneSysPanelSettings.asset");
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/GeneSysTheme.tss");
            EditorUtility.SetDirty(panelSettings);

            AssetDatabase.SaveAssets();
            BuildScene(config, registry, panelSettings);
            EditorApplication.delayCall += RepairOpenSceneReferences;
            Debug.Log("GeneSys Phase 1 assets and Terrarium scene rebuilt.");
        }

        private static MaterialDefinition Define(int id, string name, MaterialCategory category, Color color,
            float density, float rigidity, float repose, float grain, float buoyancy,
            float thermal, float heatCapacity, float electrical, float absorbency, float porosity,
            float melt, float boil, float thermalExpansion, float electricalExpansion,
            int solid, int liquid, int gas, bool bioModifiable, bool densityDisplaceable = false,
            float toxicity = 0, float calories = 0)
        {
            string path = $"{MaterialRoot}/{id:D3}_{name}.asset";
            MaterialDefinition asset = LoadOrCreate<MaterialDefinition>(path);
            asset.stableId = id; asset.displayName = name; asset.category = category; asset.displayColor = color;
            asset.density = density; asset.rigidity = rigidity; asset.angleOfRepose = repose; asset.grainSize = grain; asset.buoyancyBias = buoyancy;
            asset.densityDisplaceable = densityDisplaceable;
            asset.thermalConductivity = thermal; asset.heatCapacity = heatCapacity; asset.electricalConductivity = electrical;
            asset.absorbency = absorbency; asset.porosity = porosity; asset.meltingTemperature = melt; asset.boilingTemperature = boil;
            asset.thermalExpansion = thermalExpansion; asset.electricalExpansion = electricalExpansion;
            asset.solidPhaseId = solid; asset.liquidPhaseId = liquid; asset.gasPhaseId = gas;
            asset.bioModifiable = bioModifiable; asset.toxicity = toxicity; asset.caloricContent = calories;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void BuildScene(SimulationConfig config, MaterialRegistry registry, PanelSettings panelSettings)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            config = AssetDatabase.LoadAssetAtPath<SimulationConfig>(ConfigRoot + "/SimulationConfig.asset");
            registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(DataRoot + "/MaterialRegistry.asset");
            panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/UI/GeneSysPanelSettings.asset");
            if (config == null || registry == null || panelSettings == null)
                throw new System.InvalidOperationException("GeneSys data assets could not be reloaded before scene construction.");

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.005f, 0.008f, 0.014f, 1f);
            camera.transform.position = new Vector3(0, 0, -10);

            GameObject displayObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            displayObject.name = "Planetoid Display";
            displayObject.transform.localScale = Vector3.one * 10f;
            Object.DestroyImmediate(displayObject.GetComponent<Collider>());
            PlanetoidDisplayRenderer display = displayObject.AddComponent<PlanetoidDisplayRenderer>();
            SetObject(display, "targetCamera", camera);
            SetObject(display, "displayShader", Shader.Find("GeneSys/Planetoid Display"));

            var uiObject = new GameObject("Simulation UI");
            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/UI/SimulationShell.uxml");
            document.sortingOrder = 10;
            SimulationUIController ui = uiObject.AddComponent<SimulationUIController>();
            SetObject(ui, "document", document);

            var root = new GameObject("GeneSys Simulation");
            SimulationHost host = root.AddComponent<SimulationHost>();
            TerrariumVisualController visuals = root.AddComponent<TerrariumVisualController>();
            SimulationTools tools = root.AddComponent<SimulationTools>();
            SimulationValidator validator = root.AddComponent<SimulationValidator>();
            SetObject(host, "config", config);
            SetObject(host, "materialRegistry", registry);
            SetObject(host, "worldGeneration", AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Compute/WorldGen/WorldGeneration.compute"));
            SetObject(host, "materialSimulation", AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Compute/Simulation/MaterialSimulation.compute"));
            SetObject(host, "geology", AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Compute/Simulation/Geology.compute"));
            SetObject(host, "hydrology", AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Compute/Simulation/Hydrology.compute"));
            SetObject(host, "weather", AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Compute/Simulation/Weather.compute"));
            SetObject(host, "display", display);
            SetObject(host, "visuals", visuals);
            SetObject(host, "ui", ui);
            SetObject(host, "tools", tools);
            SetObject(host, "validator", validator);
            SetObject(visuals, "host", host);
            SetObject(visuals, "display", display);
            SetObject(visuals, "targetCamera", camera);
            SetObject(visuals, "spaceParticleShader", Shader.Find("GeneSys/Space Particle"));
            SetObject(visuals, "atmosphereGlowShader", Shader.Find("GeneSys/Atmosphere Glow"));
            SetObject(visuals, "solarBodyShader", Shader.Find("GeneSys/Solar Body"));
            SetObject(tools, "host", host);
            SetObject(tools, "display", display);
            SetObject(tools, "ui", ui);
            SetObject(ui, "validator", validator);
            SetObject(validator, "host", host);

            string scenePath = Root + "/Scenes/Terrarium.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            Selection.activeObject = root;
        }

        private static void RepairOpenSceneReferences()
        {
            SimulationHost host = Object.FindFirstObjectByType<SimulationHost>();
            UIDocument document = Object.FindFirstObjectByType<UIDocument>();
            if (host == null || document == null) return;

            SimulationConfig config = AssetDatabase.LoadAssetAtPath<SimulationConfig>(ConfigRoot + "/SimulationConfig.asset");
            MaterialRegistry registry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>(DataRoot + "/MaterialRegistry.asset");
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/UI/GeneSysPanelSettings.asset");
            SetObject(host, "config", config);
            SetObject(host, "materialRegistry", registry);
            document.panelSettings = panelSettings;
            EditorUtility.SetDirty(document);
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            EditorSceneManager.SaveScene(host.gameObject.scene);
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) throw new System.InvalidOperationException($"Missing serialized field {target.GetType().Name}.{propertyName}");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
