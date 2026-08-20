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
            { "Ecology - Mycology", "ecology" },
            { "Graphics", "performance" },
            { "Tools and validation", "performance" }
        };

        private static readonly HashSet<string> ToggleSettingsFields = new()
        {
            nameof(SimulationConfig.enableStarfield),
            nameof(SimulationConfig.enableNebula),
            nameof(SimulationConfig.enableAtmosphereGlow),
            nameof(SimulationConfig.enableSolarBody)
        };

        private static readonly HashSet<string> SkipSettingsFields = new()
        {
            nameof(SimulationConfig.brushRadius),
            nameof(SimulationConfig.brushStrength),
            nameof(SimulationConfig.grid)
        };

        [SerializeField] private UIDocument document;
        [SerializeField] private SimulationValidator validator;
        private SimulationHost host;
        private PlanetoidDisplayRenderer display;
        private SimulationTools tools;
        private readonly WorldSnapshotService snapshots = new();
        private readonly SettingsPresetService presets = new();
        private VisualElement presetOverlay;
        private VisualElement presetSaveGroup;
        private VisualElement presetLoadGroup;
        private Label presetDialogTitle;
        private Label presetError;
        private Label presetEmpty;
        private TextField presetFilename;
        private ListView presetList;
        private Button presetConfirm;
        private bool presetDialogIsSave;
        private List<string> presetNames = new();
        private Label statusLabel;
        private Label inspectLabel;
        private Label worldMetricsLabel;
        private Label simulationStatusLabel;
        private Button playButton;
        private Button toolsHeader;
        private Button settingsHeader;
        private Button statusHeader;
        private VisualElement toolsDrawer;
        private VisualElement settingsDrawer;
        private VisualElement statusDrawer;
        private VisualElement toolsBody;
        private VisualElement settingsBody;
        private VisualElement statusBody;
        private bool initialized;
        private bool metricsReadbackPending;
        private float metricsRefreshTimer;

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer renderer, SimulationTools simulationTools)
        {
            host = simulationHost;
            display = renderer;
            tools = simulationTools;
            if (document == null) document = GetComponent<UIDocument>();
            if (display != null) display.ShouldBlockWorldInput = IsPointerOverUi;
            VisualElement root = document.rootVisualElement;
            toolsDrawer = root.Q("tools-drawer");
            settingsDrawer = root.Q("settings-drawer");
            statusDrawer = root.Q("status-drawer");
            toolsHeader = root.Q<Button>("tools-header");
            settingsHeader = root.Q<Button>("settings-header");
            statusHeader = root.Q<Button>("status-header");
            toolsBody = root.Q("tools-body");
            settingsBody = root.Q("settings-body");
            statusBody = root.Q("status-body");
            statusLabel = root.Q<Label>("status");
            inspectLabel = root.Q<Label>("inspection");
            worldMetricsLabel = root.Q<Label>("world-metrics");
            simulationStatusLabel = root.Q<Label>("simulation-status");
            playButton = root.Q<Button>("play");

            root.Q<Button>("play")?.RegisterCallback<ClickEvent>(_ => { host.Clock.Toggle(); RefreshPlayLabel(); });
            root.Q<Button>("step")?.RegisterCallback<ClickEvent>(_ => host.Clock.RequestStep());
            root.Q<Button>("regenerate")?.RegisterCallback<ClickEvent>(_ => { host.Regenerate(); RefreshWorldMetrics(force: true); });
            root.Q<Button>("save")?.RegisterCallback<ClickEvent>(_ => SaveSnapshot());
            root.Q<Button>("load")?.RegisterCallback<ClickEvent>(_ => LoadSnapshot());
            root.Q<Button>("validate")?.RegisterCallback<ClickEvent>(_ => validator?.ValidateNow());
            root.Q<Button>("restore-defaults")?.RegisterCallback<ClickEvent>(_ => RestoreDefaultSettings(root));
            root.Q<Button>("save-preset")?.RegisterCallback<ClickEvent>(_ => ShowSavePresetDialog());
            root.Q<Button>("load-preset")?.RegisterCallback<ClickEvent>(_ => ShowLoadPresetDialog());
            SetupPresetDialog(root);

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
            metricsRefreshTimer = 0f;
            RefreshPlayLabel();
            RefreshWorldMetrics(force: true);
        }

        private void SetupDrawers()
        {
            toolsHeader?.RegisterCallback<ClickEvent>(_ => ToggleDrawer(toolsHeader, toolsBody, toolsDrawer, "Tools"));
            settingsHeader?.RegisterCallback<ClickEvent>(_ => ToggleDrawer(settingsHeader, settingsBody, settingsDrawer, "Simulation Settings"));
            statusHeader?.RegisterCallback<ClickEvent>(_ =>
            {
                ToggleDrawer(statusHeader, statusBody, statusDrawer, "Simulation Status");
                if (statusBody != null && !statusBody.ClassListContains("collapsed"))
                    RefreshWorldMetrics(force: true);
            });
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
                overlay.choices = new List<string>
                {
                    "Material", "Temperature", "Pressure", "Moisture", "Charge", "Wind", "Vapor",
                    "Groundwater", "Nutrient/Soil Quality", "Fault/Stress", "Toxicity/Calories", "Composite Water",
                    "Vertical Velocity", "Pressure Anomaly", "Saturation", "Cloud Only", "Mycology"
                };
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
        }

        private void RefreshPresetDropdown(VisualElement root)
        {
            var preset = root.Q<DropdownField>("preset");
            if (preset != null && preset.choices != null)
            {
                int index = (int)host.Config.preset;
                if (index >= 0 && index < preset.choices.Count)
                    preset.SetValueWithoutNotify(preset.choices[index]);
            }
        }

        private static void SetupTabs(VisualElement root)
        {
            string[] names = { "world", "geology", "hydrology", "weather", "performance", "ecology" };
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

        private void SetupPresetDialog(VisualElement root)
        {
            presetOverlay = root.Q("preset-overlay");
            presetSaveGroup = root.Q("preset-save-group");
            presetLoadGroup = root.Q("preset-load-group");
            presetDialogTitle = root.Q<Label>("preset-dialog-title");
            presetError = root.Q<Label>("preset-error");
            presetEmpty = root.Q<Label>("preset-empty");
            presetFilename = root.Q<TextField>("preset-filename");
            presetList = root.Q<ListView>("preset-list");
            presetConfirm = root.Q<Button>("preset-confirm");
            root.Q<Button>("preset-cancel")?.RegisterCallback<ClickEvent>(_ => HidePresetDialog());
            presetConfirm?.RegisterCallback<ClickEvent>(_ => ConfirmPresetDialog(root));
            if (presetList != null)
            {
                presetList.selectionType = SelectionType.Single;
                presetList.fixedItemHeight = 22;
                presetList.makeItem = () => new Label();
                presetList.bindItem = (element, index) =>
                {
                    if (element is Label label && index >= 0 && index < presetNames.Count)
                        label.text = presetNames[index];
                };
            }
        }

        private void ShowSavePresetDialog()
        {
            presetDialogIsSave = true;
            if (presetDialogTitle != null) presetDialogTitle.text = "Save Preset";
            if (presetConfirm != null) presetConfirm.text = "Save";
            presetSaveGroup?.RemoveFromClassList("hidden");
            presetLoadGroup?.AddToClassList("hidden");
            if (presetFilename != null) presetFilename.value = string.Empty;
            SetPresetError(null);
            ShowPresetOverlay();
        }

        private void ShowLoadPresetDialog()
        {
            presetDialogIsSave = false;
            if (presetDialogTitle != null) presetDialogTitle.text = "Load Preset";
            if (presetConfirm != null) presetConfirm.text = "OK";
            presetSaveGroup?.AddToClassList("hidden");
            presetLoadGroup?.RemoveFromClassList("hidden");
            SetPresetError(null);
            presetNames = presets.ListPresets();
            bool empty = presetNames.Count == 0;
            presetEmpty?.EnableInClassList("hidden", !empty);
            if (presetList != null)
            {
                presetList.itemsSource = presetNames;
                presetList.Rebuild();
                presetList.selectedIndex = empty ? -1 : 0;
            }
            if (presetConfirm != null) presetConfirm.SetEnabled(!empty);
            ShowPresetOverlay();
        }

        private void ShowPresetOverlay()
        {
            presetOverlay?.RemoveFromClassList("hidden");
        }

        private void HidePresetDialog()
        {
            presetOverlay?.AddToClassList("hidden");
            if (presetConfirm != null) presetConfirm.SetEnabled(true);
        }

        private void SetPresetError(string message)
        {
            if (presetError == null) return;
            bool hasError = !string.IsNullOrEmpty(message);
            presetError.text = message ?? string.Empty;
            presetError.EnableInClassList("hidden", !hasError);
        }

        private void ConfirmPresetDialog(VisualElement root)
        {
            if (presetDialogIsSave)
            {
                if (!presets.Save(host.Config, presetFilename != null ? presetFilename.value : string.Empty, out string error))
                {
                    SetPresetError(error);
                    return;
                }
                HidePresetDialog();
                return;
            }

            int index = presetList != null ? presetList.selectedIndex : -1;
            if (index < 0 || index >= presetNames.Count)
            {
                SetPresetError("Select a preset to load.");
                return;
            }

            if (!presets.Load(host.Config, presetNames[index], out string loadError))
            {
                SetPresetError(loadError);
                return;
            }

            host.ApplyLoadedSettings();
            HidePresetDialog();
            RefreshBoundControls(root);
            BuildSettings(root);
            RefreshPlayLabel();
            RefreshWorldMetrics(force: true);
        }

        private void RestoreDefaultSettings(VisualElement root)
        {
            host.RestoreDefaultSettings();
            RefreshBoundControls(root);
            BuildSettings(root);
            RefreshPlayLabel();
            RefreshWorldMetrics(force: true);
        }

        private void RefreshBoundControls(VisualElement root)
        {
            var speed = root.Q<Slider>("speed");
            if (speed != null) speed.SetValueWithoutNotify(host.Clock.Speed);
            var radius = root.Q<SliderInt>("brush-radius");
            if (radius != null) radius.SetValueWithoutNotify(tools.Radius);
            var strength = root.Q<Slider>("brush-strength");
            if (strength != null) strength.SetValueWithoutNotify(tools.Strength);
            RefreshPresetDropdown(root);
        }

        private void BuildSettings(VisualElement root)
        {
            var containers = new Dictionary<string, ScrollView>
            {
                { "world", root.Q<ScrollView>("settings-container") },
                { "geology", root.Q<ScrollView>("settings-geology") },
                { "hydrology", root.Q<ScrollView>("settings-hydrology") },
                { "weather", root.Q<ScrollView>("settings-weather") },
                { "performance", root.Q<ScrollView>("settings-performance") },
                { "ecology", root.Q<ScrollView>("settings-ecology") }
            };
            foreach (ScrollView container in containers.Values)
                container?.Clear();

            string currentTab = "world";
            foreach (FieldInfo field in typeof(SimulationConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
                if (header != null && HeaderToTab.TryGetValue(header.header, out string tab))
                {
                    currentTab = tab;
                    if (containers.TryGetValue(currentTab, out ScrollView sectionContainer) && sectionContainer != null)
                    {
                        string title = header.header == "Tools and validation" ? "Validation" : header.header;
                        var sectionLabel = new Label(title);
                        sectionLabel.AddToClassList("section-title");
                        sectionContainer.Add(sectionLabel);
                    }
                }
                if (SkipSettingsFields.Contains(field.Name)) continue;
                if (!containers.TryGetValue(currentTab, out ScrollView container) || container == null) continue;

                if (field.FieldType == typeof(SimulationPreset))
                {
                    var control = new DropdownField(Humanize(field.Name))
                    {
                        name = field.Name,
                        choices = new List<string>(Enum.GetNames(typeof(SimulationPreset))),
                        index = (int)field.GetValue(host.Config)
                    };
                    control.RegisterValueChangedCallback(evt =>
                    {
                        int index = control.choices.IndexOf(evt.newValue);
                        if (index >= 0) host.ApplyPreset((SimulationPreset)index);
                        RefreshPresetDropdown(root);
                    });
                    container.Add(control);
                }
                else if (field.Name == nameof(SimulationConfig.useOgWorldgen) && field.FieldType == typeof(bool))
                {
                    var control = new Toggle("Use OG Worldgen") { value = (bool)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    container.Add(control);
                }
                else if (ToggleSettingsFields.Contains(field.Name) && field.FieldType == typeof(int))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (int)field.GetValue(host.Config) != 0 };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue ? 1 : 0));
                    container.Add(control);
                }
                else if (field.FieldType == typeof(float))
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
                else if (field.FieldType == typeof(bool))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (bool)field.GetValue(host.Config) };
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

            if (statusBody == null || statusBody.ClassListContains("collapsed")) return;
            metricsRefreshTimer -= Time.unscaledDeltaTime;
            if (metricsRefreshTimer <= 0f)
                RefreshWorldMetrics(force: false);
        }

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                return false;
            IPanel panel = document.rootVisualElement.panel;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            VisualElement picked = panel.Pick(panelPoint);
            return picked != null && picked != document.rootVisualElement;
        }

        private void SetInspection(CellInspection inspection)
        {
            if (inspectLabel == null) return;
            GeneSys.Materials.MaterialDefinition definition = host.MaterialRegistry.Get((int)inspection.materialId);
            inspectLabel.text = $"Cell θ:{inspection.cell.x} r:{inspection.cell.y}\nMaterial: {(definition != null ? definition.displayName : inspection.materialId.ToString())}\nT {inspection.state.x:F2}  P {inspection.state.y:F3}\nWater {inspection.state.z:F3}  Charge {inspection.state.w:F3}\nVapor {inspection.aux.x:F3}  Ground {inspection.aux.y:F3}\nNutrient {inspection.aux.z:F3}  Stress {inspection.aux.w:F3}\nSpores {inspection.ecology.x:F3}  Myco {inspection.ecology.y:F3}\nStrain {MycologyTraits.Describe(MycologyTraits.FromFloat(inspection.ecology.z))}\nWind θ {inspection.flow.x:F3}  r {inspection.flow.y:F3}";
        }

        private void RefreshPlayLabel()
        {
            if (playButton != null) playButton.text = host.Clock.IsRunning ? "Pause" : "Play";
        }

        private float MetricsRefreshIntervalSeconds()
        {
            if (host == null || !host.IsReady) return 1.5f;
            int cells = host.Grid.CellCount;
            if (cells >= 1_500_000) return 4f;
            if (cells >= 400_000) return 2.5f;
            return 1.5f;
        }

        private void RefreshWorldMetrics(bool force)
        {
            if (host == null || !host.IsReady) return;
            if (!force && (statusBody == null || statusBody.ClassListContains("collapsed"))) return;
            if (metricsReadbackPending) return;

            metricsReadbackPending = true;
            metricsRefreshTimer = MetricsRefreshIntervalSeconds();
            SimulationMetrics.MeasureAsync(host, metrics =>
            {
                metricsReadbackPending = false;
                if (worldMetricsLabel != null)
                {
                    worldMetricsLabel.text =
                        $"Ocean coverage {metrics.OceanCoverage:P1} | Basins {metrics.BasinCount}\n" +
                        $"Surface {metrics.SurfaceWaterMass:F1} | Ground {metrics.GroundwaterMass:F1} | Vapor {metrics.VaporMass:F1}";
                }
                if (simulationStatusLabel != null)
                {
                    simulationStatusLabel.text =
                        $"Grid {metrics.AngularResolution}×{metrics.RadialResolution}\n" +
                        $"Ocean {metrics.OceanCoverage:P1}  |  Basins {metrics.BasinCount}\n" +
                        $"Water  surface {metrics.SurfaceWaterMass:F1}  ground {metrics.GroundwaterMass:F1}  vapor {metrics.VaporMass:F1}\n" +
                        $"Total tracked water {metrics.TotalTrackedWaterMass:F1}\n" +
                        $"Mean T {metrics.MeanTemperature:F2}  P {metrics.MeanPressure:F3}  moisture {metrics.MeanMoisture:F3}\n" +
                        $"Mean wind speed {metrics.MeanWindSpeed:F3}\n" +
                        $"Organisms {metrics.OrganismCount}  (phase 2)";
                }
            });
        }

        private string SnapshotPath => System.IO.Path.Combine(Application.persistentDataPath, "genesys-phase1.snapshot");
        private void SaveSnapshot() => snapshots.Save(host, SnapshotPath, ok => Debug.Log(ok ? $"Saved {SnapshotPath}" : "Snapshot save failed."));
        private void LoadSnapshot() { if (!snapshots.Load(host, SnapshotPath)) Debug.LogWarning("Snapshot load failed or grid preset differs."); }
    }
}
