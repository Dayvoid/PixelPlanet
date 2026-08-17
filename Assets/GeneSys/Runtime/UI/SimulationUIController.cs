using System;
using System.Collections.Generic;
using System.Reflection;
using GeneSys.Configuration;
using GeneSys.Materials;
using GeneSys.Persistence;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Tools;
using GeneSys.Validation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GeneSys.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class SimulationUIController : MonoBehaviour
    {
        private static readonly Dictionary<string, string> HeaderToTab = new()
        {
            { "Grid and timing", "world" },
            { "World generation", "world" },
            { "Material mechanics", "world" },
            { "Geology", "geology" },
            { "Hydrology and erosion", "hydrology" },
            { "Solar and weather", "weather" },
            { "Tools and validation", "performance" }
        };

        private static readonly HashSet<string> SkipSettingsFields = new()
        {
            nameof(SimulationConfig.brushRadius),
            nameof(SimulationConfig.brushStrength)
        };

        [SerializeField] private UIDocument document;
        [SerializeField] private SimulationValidator validator;
        private SimulationHost host;
        private PlanetoidDisplayRenderer display;
        private SimulationTools tools;
        private readonly WorldSnapshotService snapshots = new();
        private Label statusLabel;
        private Label inspectLabel;
        private Button playButton;
        private Button toolsHeader;
        private Button settingsHeader;
        private VisualElement clockPanel;
        private VisualElement inspectorPanel;
        private VisualElement toolsDrawer;
        private VisualElement settingsDrawer;
        private VisualElement toolsBody;
        private VisualElement settingsBody;
        private bool initialized;

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer renderer, SimulationTools simulationTools)
        {
            host = simulationHost;
            display = renderer;
            tools = simulationTools;
            if (document == null) document = GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;
            clockPanel = root.Q("clock-panel");
            inspectorPanel = root.Q("inspector-panel");
            toolsDrawer = root.Q("tools-drawer");
            settingsDrawer = root.Q("settings-drawer");
            toolsHeader = root.Q<Button>("tools-header");
            settingsHeader = root.Q<Button>("settings-header");
            toolsBody = root.Q("tools-body");
            settingsBody = root.Q("settings-body");
            statusLabel = root.Q<Label>("status");
            inspectLabel = root.Q<Label>("inspection");
            playButton = root.Q<Button>("play");

            root.Q<Button>("play")?.RegisterCallback<ClickEvent>(_ => { host.Clock.Toggle(); RefreshPlayLabel(); });
            root.Q<Button>("step")?.RegisterCallback<ClickEvent>(_ => host.Clock.RequestStep());
            root.Q<Button>("regenerate")?.RegisterCallback<ClickEvent>(_ => host.Regenerate());
            root.Q<Button>("save")?.RegisterCallback<ClickEvent>(_ => SaveSnapshot());
            root.Q<Button>("load")?.RegisterCallback<ClickEvent>(_ => LoadSnapshot());
            root.Q<Button>("validate")?.RegisterCallback<ClickEvent>(_ => validator?.ValidateNow());
            root.Q<Button>("restore-defaults")?.RegisterCallback<ClickEvent>(_ => RestoreDefaultSettings(root));

            var speed = root.Q<Slider>("speed");
            if (speed != null) { speed.value = host.Clock.Speed; speed.RegisterValueChangedCallback(evt => host.Clock.SetSpeed(evt.newValue)); }
            var radius = root.Q<SliderInt>("brush-radius");
            if (radius != null) { radius.value = tools.Radius; radius.RegisterValueChangedCallback(evt => tools.Radius = evt.newValue); }
            var strength = root.Q<Slider>("brush-strength");
            if (strength != null) { strength.value = tools.Strength; strength.RegisterValueChangedCallback(evt => tools.Strength = evt.newValue); }

            SetupDrawers();
            SetupDropdowns(root);
            SetupTabs(root);
            BuildSettings(root);
            tools.Inspected -= SetInspection;
            tools.Inspected += SetInspection;
            initialized = true;
            RefreshPlayLabel();
        }

        private void SetupDrawers()
        {
            toolsHeader?.RegisterCallback<ClickEvent>(_ => ToggleDrawer(toolsHeader, toolsBody, toolsDrawer, "Tools"));
            settingsHeader?.RegisterCallback<ClickEvent>(_ => ToggleDrawer(settingsHeader, settingsBody, settingsDrawer, "Simulation Settings"));
        }

        private static void ToggleDrawer(Button header, VisualElement body, VisualElement drawer, string title)
        {
            if (header == null || body == null) return;
            bool collapsed = body.ClassListContains("collapsed");
            if (collapsed)
            {
                body.RemoveFromClassList("collapsed");
                drawer?.RemoveFromClassList("collapsed");
                header.text = title + " ▾";
            }
            else
            {
                body.AddToClassList("collapsed");
                drawer?.AddToClassList("collapsed");
                header.text = title + " ▸";
            }
        }

        private void SetupDropdowns(VisualElement root)
        {
            var overlay = root.Q<DropdownField>("overlay");
            if (overlay != null)
            {
                overlay.choices = new List<string> { "Material", "Temperature", "Pressure", "Moisture", "Charge", "Wind", "Vapor", "Groundwater", "Chemical/Bio", "Fault/Stress", "Toxicity/Calories" };
                overlay.index = 0;
                overlay.RegisterValueChangedCallback(_ => display.SetOverlay(overlay.index));
            }
            var mode = root.Q<DropdownField>("brush-mode");
            if (mode != null)
            {
                mode.choices = new List<string>(Enum.GetNames(typeof(BrushMode)));
                mode.index = 0;
                mode.RegisterValueChangedCallback(_ => tools.Mode = (BrushMode)mode.index);
            }
            var material = root.Q<DropdownField>("material");
            if (material != null)
            {
                var names = new List<string>();
                foreach (GeneSys.Materials.MaterialDefinition definition in host.MaterialRegistry.materials) names.Add($"{definition.stableId}: {definition.displayName}");
                material.choices = names;
                material.index = Mathf.Min(7, names.Count - 1);
                if (material.index >= 0)
                    BuildMaterialSettings(root.Q<ScrollView>("material-settings"), host.MaterialRegistry.materials[material.index]);
                material.RegisterValueChangedCallback(_ =>
                {
                    if (material.index >= 0 && material.index < host.MaterialRegistry.materials.Count)
                    {
                        tools.SelectedMaterialId = (uint)host.MaterialRegistry.materials[material.index].stableId;
                        BuildMaterialSettings(root.Q<ScrollView>("material-settings"), host.MaterialRegistry.materials[material.index]);
                    }
                });
            }
            var preset = root.Q<DropdownField>("preset");
            if (preset != null)
            {
                preset.choices = new List<string>(Enum.GetNames(typeof(SimulationPreset)));
                preset.index = (int)host.Config.preset;
                preset.RegisterValueChangedCallback(_ => host.ApplyPreset((SimulationPreset)preset.index));
            }
        }

        private static void SetupTabs(VisualElement root)
        {
            string[] names = { "world", "geology", "hydrology", "weather", "performance" };
            void Show(string name)
            {
                foreach (string pageName in names)
                {
                    VisualElement page = root.Q($"page-{pageName}");
                    if (page != null) page.style.display = pageName == name ? DisplayStyle.Flex : DisplayStyle.None;
                    Button tab = root.Q<Button>($"tab-{pageName}");
                    tab?.EnableInClassList("tab-button--active", pageName == name);
                }
            }

            foreach (string name in names)
            {
                Button button = root.Q<Button>($"tab-{name}");
                string captured = name;
                button?.RegisterCallback<ClickEvent>(_ => Show(captured));
            }

            Show("world");
        }

        private void RestoreDefaultSettings(VisualElement root)
        {
            host.RestoreDefaultSettings();
            RefreshBoundControls(root);
            BuildSettings(root);
            RefreshPlayLabel();
        }

        private void RefreshBoundControls(VisualElement root)
        {
            var speed = root.Q<Slider>("speed");
            if (speed != null) speed.SetValueWithoutNotify(host.Clock.Speed);
            var radius = root.Q<SliderInt>("brush-radius");
            if (radius != null) radius.SetValueWithoutNotify(tools.Radius);
            var strength = root.Q<Slider>("brush-strength");
            if (strength != null) strength.SetValueWithoutNotify(tools.Strength);
            var preset = root.Q<DropdownField>("preset");
            if (preset != null && preset.choices != null)
            {
                int index = (int)host.Config.preset;
                if (index >= 0 && index < preset.choices.Count)
                    preset.SetValueWithoutNotify(preset.choices[index]);
            }
        }

        private void BuildSettings(VisualElement root)
        {
            var containers = new Dictionary<string, ScrollView>
            {
                { "world", root.Q<ScrollView>("settings-container") },
                { "geology", root.Q<ScrollView>("settings-geology") },
                { "hydrology", root.Q<ScrollView>("settings-hydrology") },
                { "weather", root.Q<ScrollView>("settings-weather") },
                { "performance", root.Q<ScrollView>("settings-performance") }
            };
            foreach (ScrollView container in containers.Values)
                container?.Clear();

            string currentTab = "world";
            foreach (FieldInfo field in typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null && HeaderToTab.TryGetValue(header.header, out string tab))
                    currentTab = tab;
                if (SkipSettingsFields.Contains(field.Name)) continue;
                if (!containers.TryGetValue(currentTab, out ScrollView container) || container == null) continue;

                if (field.FieldType == typeof(float))
                {
                    var control = new FloatField(Humanize(field.Name)) { value = (float)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    container.Add(control);
                }
                else if (field.FieldType == typeof(int))
                {
                    var control = new IntegerField(Humanize(field.Name)) { value = (int)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    container.Add(control);
                }
            }
        }

        private void BuildMaterialSettings(ScrollView container, GeneSys.Materials.MaterialDefinition definition)
        {
            if (container == null || definition == null) return;
            container.Clear();
            foreach (FieldInfo field in typeof(GeneSys.Materials.MaterialDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name == nameof(GeneSys.Materials.MaterialDefinition.stableId) || field.Name == nameof(GeneSys.Materials.MaterialDefinition.displayColor))
                    continue;
                if (field.FieldType == typeof(float))
                {
                    var control = new FloatField(Humanize(field.Name)) { value = (float)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    container.Add(control);
                }
                else if (field.FieldType == typeof(int))
                {
                    var control = new IntegerField(Humanize(field.Name)) { value = (int)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    container.Add(control);
                }
                else if (field.FieldType == typeof(bool))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (bool)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    container.Add(control);
                }
            }
        }

        private static string Humanize(string value)
        {
            var result = new System.Text.StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && char.IsUpper(c)) result.Append(' ');
                result.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return result.ToString();
        }

        private void Update()
        {
            if (!initialized || host == null || statusLabel == null) return;
            statusLabel.text = $"Tick {host.Clock.TickCount:N0} | {(host.Clock.IsRunning ? "Running" : "Paused")} | {host.LastTickMilliseconds:F2} ms CPU dispatch";
        }

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (document == null || document.rootVisualElement.panel == null) return false;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(document.rootVisualElement.panel, screenPosition);
            return Contains(clockPanel, panelPoint)
                || Contains(inspectorPanel, panelPoint)
                || Contains(toolsDrawer, panelPoint)
                || Contains(settingsDrawer, panelPoint);
        }

        private static bool Contains(VisualElement element, Vector2 panelPoint)
        {
            return element != null && element.worldBound.Contains(panelPoint);
        }

        private void SetInspection(CellInspection inspection)
        {
            if (inspectLabel == null) return;
            GeneSys.Materials.MaterialDefinition definition = host.MaterialRegistry.Get((int)inspection.materialId);
            inspectLabel.text = $"Cell θ:{inspection.cell.x} r:{inspection.cell.y}\nMaterial: {(definition != null ? definition.displayName : inspection.materialId.ToString())}\nT {inspection.state.x:F2}  P {inspection.state.y:F3}\nWater {inspection.state.z:F3}  Charge {inspection.state.w:F3}\nVapor {inspection.aux.x:F3}  Ground {inspection.aux.y:F3}\nChemical {inspection.aux.z:F3}  Stress {inspection.aux.w:F3}";
        }

        private void RefreshPlayLabel()
        {
            if (playButton != null) playButton.text = host.Clock.IsRunning ? "Pause" : "Play";
        }

        private string SnapshotPath => System.IO.Path.Combine(Application.persistentDataPath, "genesys-phase1.snapshot");
        private void SaveSnapshot() => snapshots.Save(host, SnapshotPath, ok => Debug.Log(ok ? $"Saved {SnapshotPath}" : "Snapshot save failed."));
        private void LoadSnapshot() { if (!snapshots.Load(host, SnapshotPath)) Debug.LogWarning("Snapshot load failed or grid preset differs."); }
    }
}
