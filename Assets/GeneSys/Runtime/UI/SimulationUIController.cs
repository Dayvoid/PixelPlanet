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
            { "Ecology - Flora", "ecology" },
            { "Combustion", "combustion" },
            { "Storm and lightning", "storm" },
            { "Graphics", "performance" },
            { "Tools and validation", "performance" },
            { "Probe", "probe" }
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
        [SerializeField] private ProbeController probe;
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
        private Button followCameraButton;
        private Button globeCameraButton;
        private bool initialized;
        private bool metricsReadbackPending;
        private float metricsRefreshTimer;
        private VisualElement settingTooltip;
        private Label settingTooltipTitle;
        private Label settingTooltipLabel;
        private VisualElement tooltipAnchor;
        private IVisualElementScheduledItem tooltipShow;
        private EventCallback<GeometryChangedEvent> tooltipLaidOut;

        public void Initialize(SimulationHost simulationHost, PlanetoidDisplayRenderer renderer, SimulationTools simulationTools)
        {
            host = simulationHost;
            display = renderer;
            tools = simulationTools;
            if (document == null) document = GetComponent<UIDocument>();
            if (display != null)
            {
                display.ShouldBlockWorldInput = IsPointerOverUi;
                display.FollowProbe = probe;
            }
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

            SetupSettingTooltip(root);
            SetupDrawers();
            SetupDropdowns(root);
            SetupTabs(root);
            BuildSettings(root);
            SetupProbeHud(root);
            AttachStaticSettingTooltips(root);
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

        private void ToggleDrawer(Button header, VisualElement body, VisualElement drawer, string title)
        {
            HideSettingTooltip();
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
                    "Vertical Velocity", "Pressure Anomaly", "Saturation", "Cloud Only", "Mycology",
                    "Fire", "Oxygen", "Storm Charge", "Flora", "Light", "Genome"
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

        private void SetupTabs(VisualElement root)
        {
            string[] names = { "world", "geology", "hydrology", "weather", "performance", "ecology", "combustion", "storm", "probe" };
            void Show(string name)
            {
                HideSettingTooltip();
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

        private void SetupProbeHud(VisualElement root)
        {
            if (probe == null && host != null)
                probe = host.GetComponent<ProbeController>();
            if (display != null) display.FollowProbe = probe;

            VisualElement toolsCard = root.Q("probe-tools-card");
            VisualElement cameraCard = root.Q("probe-camera-card");
            if (toolsCard != null) toolsCard.pickingMode = PickingMode.Position;
            if (cameraCard != null) cameraCard.pickingMode = PickingMode.Position;

            BindProbeActionButton(root.Q<Button>("probe-action-vapor"), ProbeAction.Vapor);
            BindProbeActionButton(root.Q<Button>("probe-action-water"), ProbeAction.Water);
            BindProbeActionButton(root.Q<Button>("probe-action-soil"), ProbeAction.Soil);
            BindProbeActionButton(root.Q<Button>("probe-action-cool"), ProbeAction.Cool);
            BindProbeActionButton(root.Q<Button>("probe-action-heat"), ProbeAction.Heat);

            followCameraButton = root.Q<Button>("probe-camera-follow");
            globeCameraButton = root.Q<Button>("probe-camera-globe");
            followCameraButton?.UnregisterCallback<ClickEvent>(OnFollowCameraClicked);
            globeCameraButton?.UnregisterCallback<ClickEvent>(OnGlobeCameraClicked);
            followCameraButton?.RegisterCallback<ClickEvent>(OnFollowCameraClicked);
            globeCameraButton?.RegisterCallback<ClickEvent>(OnGlobeCameraClicked);
            RefreshCameraModeButtons();
        }

        private void OnFollowCameraClicked(ClickEvent _) => SetCameraViewMode(CameraViewMode.ProbeFollow);

        private void OnGlobeCameraClicked(ClickEvent _) => SetCameraViewMode(CameraViewMode.Globe);

        private void BindProbeActionButton(Button button, ProbeAction action)
        {
            if (button == null) return;
            button.userData = action;
            button.UnregisterCallback<PointerDownEvent>(OnProbeActionPointerDown);
            button.UnregisterCallback<PointerUpEvent>(OnProbeActionPointerUp);
            button.UnregisterCallback<PointerCaptureOutEvent>(OnProbeActionCaptureOut);
            button.RegisterCallback<PointerDownEvent>(OnProbeActionPointerDown);
            button.RegisterCallback<PointerUpEvent>(OnProbeActionPointerUp);
            button.RegisterCallback<PointerCaptureOutEvent>(OnProbeActionCaptureOut);
        }

        private void OnProbeActionPointerDown(PointerDownEvent evt)
        {
            var target = (VisualElement)evt.currentTarget;
            target.CapturePointer(evt.pointerId);
            target.AddToClassList("probe-tool-button--held");
            if (target.userData is ProbeAction action)
                probe?.SetAction(action);
            evt.StopImmediatePropagation();
        }

        private void OnProbeActionPointerUp(PointerUpEvent evt)
        {
            var target = (VisualElement)evt.currentTarget;
            if (target.HasPointerCapture(evt.pointerId))
                target.ReleasePointer(evt.pointerId);
            target.RemoveFromClassList("probe-tool-button--held");
            probe?.SetAction(ProbeAction.None);
        }

        private void OnProbeActionCaptureOut(PointerCaptureOutEvent evt)
        {
            ((VisualElement)evt.currentTarget).RemoveFromClassList("probe-tool-button--held");
            probe?.SetAction(ProbeAction.None);
        }

        private void SetCameraViewMode(CameraViewMode mode)
        {
            display?.SetCameraViewMode(mode);
            RefreshCameraModeButtons();
        }

        private void RefreshCameraModeButtons()
        {
            CameraViewMode mode = display != null ? display.ViewMode : CameraViewMode.Globe;
            followCameraButton?.EnableInClassList("probe-camera-button--active", mode == CameraViewMode.ProbeFollow);
            globeCameraButton?.EnableInClassList("probe-camera-button--active", mode == CameraViewMode.Globe);
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
            HideSettingTooltip();
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
            HideSettingTooltip();
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
                { "ecology", root.Q<ScrollView>("settings-ecology") },
                { "combustion", root.Q<ScrollView>("settings-combustion") },
                { "storm", root.Q<ScrollView>("settings-storm") },
                { "probe", root.Q<ScrollView>("settings-probe") }
            };
            HideSettingTooltip();
            foreach (ScrollView container in containers.Values)
            {
                if (container == null) continue;
                container.Clear();
                container.verticalScroller.valueChanged -= OnSettingsScrolled;
                container.verticalScroller.valueChanged += OnSettingsScrolled;
            }

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
                    AddSettingControl(container, control, field.Name);
                }
                else if (field.Name == nameof(SimulationConfig.useOgWorldgen) && field.FieldType == typeof(bool))
                {
                    var control = new Toggle("Use OG Worldgen") { value = (bool)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    AddSettingControl(container, control, field.Name);
                }
                else if (ToggleSettingsFields.Contains(field.Name) && field.FieldType == typeof(int))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (int)field.GetValue(host.Config) != 0 };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue ? 1 : 0));
                    AddSettingControl(container, control, field.Name);
                }
                else if (field.FieldType == typeof(float))
                {
                    var control = new FloatField(Humanize(field.Name)) { value = (float)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    AddSettingControl(container, control, field.Name);
                }
                else if (field.FieldType == typeof(int))
                {
                    var control = new IntegerField(Humanize(field.Name)) { value = (int)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    AddSettingControl(container, control, field.Name);
                }
                else if (field.FieldType == typeof(bool))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (bool)field.GetValue(host.Config) };
                    control.RegisterValueChangedCallback(evt => field.SetValue(host.Config, evt.newValue));
                    AddSettingControl(container, control, field.Name);
                }
            }
        }

        private void BuildMaterialSettings(ScrollView container, GeneSys.Materials.MaterialDefinition definition)
        {
            if (container == null || definition == null) return;
            HideSettingTooltip();
            container.Clear();
            container.verticalScroller.valueChanged -= OnSettingsScrolled;
            container.verticalScroller.valueChanged += OnSettingsScrolled;
            foreach (FieldInfo field in typeof(GeneSys.Materials.MaterialDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name == nameof(GeneSys.Materials.MaterialDefinition.stableId) || field.Name == nameof(GeneSys.Materials.MaterialDefinition.displayColor))
                    continue;
                if (field.FieldType == typeof(float))
                {
                    var control = new FloatField(Humanize(field.Name)) { value = (float)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    AttachMaterialTooltip(control, field);
                    container.Add(control);
                }
                else if (field.FieldType == typeof(int))
                {
                    var control = new IntegerField(Humanize(field.Name)) { value = (int)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    AttachMaterialTooltip(control, field);
                    container.Add(control);
                }
                else if (field.FieldType == typeof(bool))
                {
                    var control = new Toggle(Humanize(field.Name)) { value = (bool)field.GetValue(definition) };
                    control.RegisterValueChangedCallback(evt => { field.SetValue(definition, evt.newValue); host.RefreshMaterialDefinitions(); });
                    AttachMaterialTooltip(control, field);
                    container.Add(control);
                }
            }
        }

        private void SetupSettingTooltip(VisualElement root)
        {
            settingTooltip = root.Q("setting-tooltip");
            settingTooltipTitle = root.Q<Label>("setting-tooltip-title");
            settingTooltipLabel = root.Q<Label>("setting-tooltip-label");
            if (settingTooltip != null)
                settingTooltip.pickingMode = PickingMode.Ignore;
            tooltipLaidOut = _ => PositionSettingTooltip();
            settingsBody?.RegisterCallback<WheelEvent>(_ => HideSettingTooltip());
            toolsBody?.RegisterCallback<WheelEvent>(_ => HideSettingTooltip());
        }

        private void AttachStaticSettingTooltips(VisualElement root)
        {
            AttachNamedSettingTooltip(root, "speed", nameof(SimulationConfig.simulationSpeed));
            AttachNamedSettingTooltip(root, "overlay",
                "Overlay",
                "Chooses which world field the planetoid display color-codes. Material is the default view; Temperature, Pressure, Wind, Vapor, Groundwater, Mycology, Fire, Oxygen, Storm Charge, Flora, Light, Genome, and the others reveal the systems those settings drive.");
            AttachNamedSettingTooltip(root, "brush-mode",
                "Brush Mode",
                "Selects what left-drag paints: material, heat, water, pressure, vapor, ignition, or life spores. Right-click still inspects the cell under the cursor.");
            AttachNamedSettingTooltip(root, "material",
                "Material",
                "Material the brush paints, and whose properties appear below. Changing density, conductivity, or absorbency immediately affects gravity, weather, hydrology, and phase changes for that pixel type.");
            AttachNamedSettingTooltip(root, "brush-radius", nameof(SimulationConfig.brushRadius));
            AttachNamedSettingTooltip(root, "brush-strength", nameof(SimulationConfig.brushStrength));
            AttachNamedSettingTooltip(root, "probe-action-vapor",
                "Seed Vapor",
                "Hold to inject humidity at the outer atmosphere ring, a couple of degrees ahead of the clockwise probe so vapor trails into its path.");
            AttachNamedSettingTooltip(root, "probe-action-water",
                "Add Water",
                "Hold to add surface water at the outer atmosphere ring ahead of the probe. Moisture can rain out and run off along the heading.");
            AttachNamedSettingTooltip(root, "probe-action-soil",
                "Drop Soil",
                "Hold to paint soil at the outer ring ahead of the probe. Soil is density-displaceable, so dumped grains can settle toward the surface.");
            AttachNamedSettingTooltip(root, "probe-action-cool",
                "Cool",
                "Hold to lower temperatures at the outer ring ahead of the probe. Cooling the column encourages condensation along the orbit.");
            AttachNamedSettingTooltip(root, "probe-action-heat",
                "Heat",
                "Hold to raise temperatures at the outer ring ahead of the probe. Heating the column warms atmosphere and surface along the heading.");
            AttachNamedSettingTooltip(root, "probe-camera-follow",
                "Probe Camera",
                "Centers the view on the probe and rotates the planetoid with its orbit so the orbiter stays pinned while the surface scrolls underneath.");
            AttachNamedSettingTooltip(root, "probe-camera-globe",
                "Globe Camera",
                "Returns to the default camera: middle-drag pans, mouse wheel zooms, and Q/E rotates the planetoid independently of the probe.");
        }

        private void AttachNamedSettingTooltip(VisualElement root, string elementName, string fieldName)
        {
            VisualElement element = root.Q(elementName);
            if (element == null || !SimulationSettingTooltips.TryGet(fieldName, out string tooltip)) return;
            BindSettingTooltip(element, Humanize(fieldName), tooltip);
        }

        private void AttachNamedSettingTooltip(VisualElement root, string elementName, string title, string tooltip)
        {
            VisualElement element = root.Q(elementName);
            if (element == null) return;
            BindSettingTooltip(element, title, tooltip);
        }

        private void AddSettingControl(VisualElement container, VisualElement control, string fieldName)
        {
            if (SimulationSettingTooltips.TryGet(fieldName, out string tooltip))
            {
                string title = fieldName == nameof(SimulationConfig.useOgWorldgen) ? "Use OG Worldgen" : Humanize(fieldName);
                BindSettingTooltip(control, title, tooltip);
            }
            container.Add(control);
        }

        private void AttachMaterialTooltip(VisualElement control, FieldInfo field)
        {
            if (SimulationSettingTooltips.TryGetMaterial(field.Name, out string tooltip))
                BindSettingTooltip(control, Humanize(field.Name), tooltip);
        }

        private void BindSettingTooltip(VisualElement target, string title, string body)
        {
            if (target == null || string.IsNullOrEmpty(body)) return;
            target.RegisterCallback<PointerEnterEvent>(_ => ScheduleSettingTooltip(target, title, body));
            target.RegisterCallback<PointerLeaveEvent>(_ => HideSettingTooltip(target));
        }

        private void ScheduleSettingTooltip(VisualElement target, string title, string body)
        {
            HideSettingTooltip();
            tooltipAnchor = target;
            tooltipShow = target.schedule.Execute(() =>
            {
                if (tooltipAnchor == target)
                    ShowSettingTooltip(target, title, body);
            }).StartingIn(350);
        }

        private void ShowSettingTooltip(VisualElement target, string title, string body)
        {
            if (settingTooltip == null || settingTooltipLabel == null) return;
            if (settingTooltipTitle != null) settingTooltipTitle.text = title ?? string.Empty;
            settingTooltipLabel.text = body;
            settingTooltip.style.visibility = Visibility.Hidden;
            settingTooltip.RemoveFromClassList("hidden");
            settingTooltip.BringToFront();
            settingTooltip.RegisterCallback(tooltipLaidOut);
            settingTooltip.schedule.Execute(PositionSettingTooltip);
        }

        private void OnSettingsScrolled(float _)
        {
            HideSettingTooltip();
        }

        private void PositionSettingTooltip()
        {
            if (settingTooltip == null || tooltipAnchor == null || document == null) return;
            settingTooltip.UnregisterCallback(tooltipLaidOut);

            VisualElement root = document.rootVisualElement;
            if (root == null) return;

            Rect targetBounds = tooltipAnchor.worldBound;
            Rect rootBounds = root.worldBound;
            float width = settingTooltip.resolvedStyle.width;
            float height = settingTooltip.resolvedStyle.height;
            if (float.IsNaN(width) || width < 8f) width = 280f;
            if (float.IsNaN(height) || height < 8f) height = 80f;

            float x = targetBounds.xMin - width - 12f;
            if (x < rootBounds.xMin + 8f)
                x = Mathf.Min(targetBounds.xMax + 12f, rootBounds.xMax - width - 8f);
            float y = Mathf.Clamp(targetBounds.yMin, rootBounds.yMin + 8f, rootBounds.yMax - height - 8f);

            Vector2 local = root.WorldToLocal(new Vector2(x, y));
            settingTooltip.style.left = local.x;
            settingTooltip.style.top = local.y;
            settingTooltip.style.visibility = Visibility.Visible;
        }

        private void HideSettingTooltip(VisualElement target)
        {
            if (tooltipAnchor != null && tooltipAnchor != target) return;
            HideSettingTooltip();
        }

        private void HideSettingTooltip()
        {
            tooltipShow?.Pause();
            tooltipShow = null;
            tooltipAnchor = null;
            if (settingTooltip == null) return;
            if (tooltipLaidOut != null)
                settingTooltip.UnregisterCallback(tooltipLaidOut);
            settingTooltip.AddToClassList("hidden");
            settingTooltip.style.visibility = StyleKeyword.Null;
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

        public static Vector2 ToUiToolkitScreenPosition(Vector2 screenPosition, float screenHeight) =>
            new(screenPosition.x, screenHeight - screenPosition.y);

        public static bool ShouldBlockWorldBrush(bool pointerOverInteractiveUi, ProbeAction probeAction) =>
            pointerOverInteractiveUi || probeAction != ProbeAction.None;

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            bool overInteractive = false;
            if (document != null && document.rootVisualElement != null && document.rootVisualElement.panel != null)
            {
                IPanel panel = document.rootVisualElement.panel;
                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, ToUiToolkitScreenPosition(screenPosition, Screen.height));
                VisualElement picked = panel.Pick(panelPoint);
                overInteractive = picked != null && picked != document.rootVisualElement;
            }
            ProbeAction action = probe != null ? probe.ActiveAction : ProbeAction.None;
            return ShouldBlockWorldBrush(overInteractive, action);
        }

        private void SetInspection(CellInspection inspection)
        {
            if (inspectLabel == null) return;
            GeneSys.Materials.MaterialDefinition definition = host.MaterialRegistry.Get((int)inspection.materialId);
            inspectLabel.text = $"Cell θ:{inspection.cell.x} r:{inspection.cell.y}\nMaterial: {(definition != null ? definition.displayName : inspection.materialId.ToString())}\nT {inspection.state.x:F2}  P {inspection.state.y:F3}\nWater {inspection.state.z:F3}  Charge {inspection.state.w:F3}\nVapor {inspection.aux.x:F3}  Ground {inspection.aux.y:F3}\nNutrient {inspection.aux.z:F3}  Stress {inspection.aux.w:F3}\nSpores {inspection.ecology.x:F3}  Myco {inspection.ecology.y:F3}\nStrain {MycologyTraits.Describe(MycologyTraits.FromFloat(inspection.ecology.z))}\nO2 {inspection.combustion.x:F3}  Flame {inspection.combustion.y:F3}\nSoot {inspection.combustion.z:F3}  Ignite {inspection.combustion.w:F3}\nStorm Q {inspection.storm.x:F3}  Bolt {inspection.storm.y:F3}\nFlash {inspection.storm.z:F3}  Break {inspection.storm.w:F3}\nFlora spores {inspection.life.x:F3}  biomass {inspection.life.y:F3}\nEnergy {inspection.life.z:F3}  exudate {inspection.life.w:F3}\nStage {FloraGenome.DescribeStage(FloraGenome.Stage(inspection.genome))}  gen {FloraGenome.Generation(inspection.genome)}  toxin {FloraGenome.ToxinDose(inspection.genome)}\nLight {inspection.light:F3}\n{FloraGenome.DescribeGenes(inspection.genome, host.Config != null ? host.Config.floraGeneExpressionRange : 0.45f)}\nWind θ {inspection.flow.x:F3}  r {inspection.flow.y:F3}";
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
                        $"Fire cells {metrics.BurningCellCount}  intensity {metrics.TotalFireIntensity:F2}  O2 {metrics.MeanOxygen:F2}  soot {metrics.SootMass:F2}\n" +
                        $"Organisms {metrics.OrganismCount}";
                }
            });
        }

        private string SnapshotPath => System.IO.Path.Combine(Application.persistentDataPath, "genesys-phase1.snapshot");
        private void SaveSnapshot() => snapshots.Save(host, SnapshotPath, ok => Debug.Log(ok ? $"Saved {SnapshotPath}" : "Snapshot save failed."));
        private void LoadSnapshot() { if (!snapshots.Load(host, SnapshotPath)) Debug.LogWarning("Snapshot load failed or grid preset differs."); }
    }
}
