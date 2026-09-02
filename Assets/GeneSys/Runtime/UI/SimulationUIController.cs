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
            { "Ecology - Mycology", "ecology-mycology" },
            { "Ecology - Algae", "ecology-algae" },
            { "Ecology - Cricket", "ecology-cricket" },
            { "Ecology - Wasp", "ecology-wasp" },
            { "Ecology - Grass", "ecology-grass" },
            { "Ecology - Tree", "ecology-tree" },
            { "Detritus", "ecology-detritus" },
            { "Combustion", "combustion" },
            { "Storm and lightning", "storm" },
            { "Graphics", "performance" },
            { "Tools and validation", "performance" },
            { "Probe", "probe" }
        };

        private static readonly string[] EcologySubTabs =
        {
            "mycology", "algae", "cricket", "wasp", "grass", "tree", "detritus"
        };

        private static readonly HashSet<string> ToggleSettingsFields = new()
        {
            nameof(SimulationConfig.enableStarfield),
            nameof(SimulationConfig.enableNebula),
            nameof(SimulationConfig.enableAtmosphereGlow),
            nameof(SimulationConfig.enableSolarBody),
            nameof(SimulationConfig.enableCoreVisual)
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
        private VisualElement worldOverlay;
        private VisualElement worldSaveGroup;
        private VisualElement worldLoadGroup;
        private Label worldDialogTitle;
        private Label worldError;
        private Label worldEmpty;
        private TextField worldFilename;
        private ListView worldList;
        private Button worldConfirm;
        private Button worldSaveButton;
        private Button worldSaveAsButton;
        private bool worldDialogIsSave;
        private bool worldSaveInProgress;
        private string currentWorldFileName;
        private List<string> worldNames = new();
        private string worldStatusMessage;
        private float worldStatusUntil;
        private Label statusLabel;
        private Label envInspectLabel;
        private Label lifeInspectLabel;
        private Label envInspectTitle;
        private Label lifeInspectTitle;
        private VisualElement envInspectPanel;
        private VisualElement lifeInspectPanel;
        private CellInspection lastInspection;
        private bool hasInspection;
        private bool envInspectExpanded;
        private bool lifeInspectExpanded;
        private Label worldMetricsLabel;
        private Label simulationStatusLabel;
        private Button playButton;
        private Button toolsHeader;
        private Button statusHeader;
        private Button historyHeader;
        private VisualElement inspectBar;
        private VisualElement toolsDrawer;
        private VisualElement statusDrawer;
        private VisualElement historyDrawer;
        private VisualElement toolsBody;
        private VisualElement settingsBody;
        private VisualElement statusBody;
        private VisualElement historyBody;
        private ScrollView historyLog;
        private VisualElement settingsOverlay;
        private Button followCameraButton;
        private Button globeCameraButton;
        private Button lifeSeedButton;
        private Button steerLeftButton;
        private Button steerStopButton;
        private Button steerRightButton;
        private VisualElement probeEnergyFill;
        private VisualElement probeSteerCard;
        private VisualElement probeToolsCard;
        private VisualElement probeCameraCard;
        private int probeHoverCount;
        private float inspectActivityTime;
        private float probeActivityTime;
        private const float HudFadeDuration = 1f;
        private const float ProbeHudIdleAlpha = 0.1f;
        private bool initialized;
        private bool metricsReadbackPending;
        private float metricsRefreshTimer;
        private int historyRenderedVersion = -1;
        private VisualElement settingTooltip;
        private Label settingTooltipTitle;
        private Label settingTooltipLabel;
        private VisualElement tooltipAnchor;
        private IVisualElementScheduledItem tooltipShow;
        private EventCallback<GeometryChangedEvent> tooltipLaidOut;
        private DropdownField materialDropdown;
        private readonly List<BrushSelection> materialChoices = new();
        private BrushMode materialCatalogMode = BrushMode.Material;
        private int lastMaterialBrushIndex = -1;
        private int lastLifeBrushIndex = -1;
        private bool suppressingMaterialCallback;

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
            statusDrawer = root.Q("status-drawer");
            historyDrawer = root.Q("history-drawer");
            toolsHeader = root.Q<Button>("tools-header");
            statusHeader = root.Q<Button>("status-header");
            historyHeader = root.Q<Button>("history-header");
            toolsBody = root.Q("tools-body");
            settingsBody = root.Q("settings-body");
            statusBody = root.Q("status-body");
            historyBody = root.Q("history-body");
            historyLog = root.Q<ScrollView>("history-log");
            settingsOverlay = root.Q("settings-overlay");
            inspectBar = root.Q("inspect-bar");
            statusLabel = root.Q<Label>("status");
            envInspectPanel = root.Q("env-inspect-panel");
            lifeInspectPanel = root.Q("life-inspect-panel");
            envInspectTitle = root.Q<Label>("env-inspect-title");
            lifeInspectTitle = root.Q<Label>("life-inspect-title");
            envInspectLabel = root.Q<Label>("env-inspection");
            lifeInspectLabel = root.Q<Label>("life-inspection");
            worldMetricsLabel = root.Q<Label>("world-metrics");
            simulationStatusLabel = root.Q<Label>("simulation-status");
            playButton = root.Q<Button>("play");

            root.Q<Button>("play")?.RegisterCallback<ClickEvent>(_ => { host.Clock.Toggle(); RefreshPlayLabel(); });
            root.Q<Button>("step")?.RegisterCallback<ClickEvent>(_ => host.Clock.RequestStep());
            root.Q<Button>("regenerate")?.RegisterCallback<ClickEvent>(_ =>
            {
                host.Regenerate();
                RefreshWorldMetrics(force: true);
                RefreshHistory(force: true);
            });
            root.Q<Button>("save")?.RegisterCallback<ClickEvent>(_ => SaveSnapshot());
            root.Q<Button>("load")?.RegisterCallback<ClickEvent>(_ => LoadSnapshot());
            root.Q<Button>("validate")?.RegisterCallback<ClickEvent>(_ => validator?.ValidateNow());
            root.Q<Button>("restore-defaults")?.RegisterCallback<ClickEvent>(_ => RestoreDefaultSettings(root));
            root.Q<Button>("save-preset")?.RegisterCallback<ClickEvent>(_ => ShowSavePresetDialog());
            root.Q<Button>("load-preset")?.RegisterCallback<ClickEvent>(_ => ShowLoadPresetDialog());
            worldSaveButton = root.Q<Button>("world-save");
            worldSaveAsButton = root.Q<Button>("world-save-as");
            worldSaveButton?.RegisterCallback<ClickEvent>(_ => SaveCurrentWorld());
            worldSaveAsButton?.RegisterCallback<ClickEvent>(_ =>
            {
                if (!worldSaveInProgress) ShowWorldSaveAsDialog();
            });
            root.Q<Button>("world-load")?.RegisterCallback<ClickEvent>(_ => ShowWorldLoadDialog());
            SetupPresetDialog(root);
            SetupWorldDialog(root);
            RefreshWorldSaveButton();

            var speed = root.Q<Slider>("speed");
            if (speed != null) { speed.value = host.Clock.Speed; speed.RegisterValueChangedCallback(evt => host.Clock.SetSpeed(evt.newValue)); }
            var radius = root.Q<SliderInt>("brush-radius");
            if (radius != null) { radius.value = tools.Radius; radius.RegisterValueChangedCallback(evt => tools.Radius = evt.newValue); }
            var strength = root.Q<Slider>("brush-strength");
            if (strength != null) { strength.value = tools.Strength; strength.RegisterValueChangedCallback(evt => tools.Strength = evt.newValue); }

            SetupSettingTooltip(root);
            SetupDrawers();
            SetupSettingsModal(root);
            SetupDropdowns(root);
            SetupTabs(root);
            SetupEcologySubTabs(root);
            BuildSettings(root);
            SetupProbeHud(root);
            SetupInspectPanels();
            AttachStaticSettingTooltips(root);
            tools.Inspected -= SetInspection;
            tools.Inspected += SetInspection;
            initialized = true;
            metricsRefreshTimer = 0f;
            historyRenderedVersion = -1;
            RefreshPlayLabel();
            RefreshWorldMetrics(force: true);
            RefreshHistory(force: true);
        }

        private void SetupDrawers()
        {
            SetDrawerCollapsed(toolsHeader, toolsBody, toolsDrawer, "Tools", true);
            SetDrawerCollapsed(statusHeader, statusBody, statusDrawer, "Simulation Status", true);
            SetDrawerCollapsed(historyHeader, historyBody, historyDrawer, "History", true);
            toolsHeader?.RegisterCallback<ClickEvent>(_ => ToggleDrawer(toolsHeader, toolsBody, toolsDrawer, "Tools"));
            statusHeader?.RegisterCallback<ClickEvent>(_ =>
            {
                ToggleDrawer(statusHeader, statusBody, statusDrawer, "Simulation Status");
                if (statusBody != null && !statusBody.ClassListContains("collapsed"))
                    RefreshWorldMetrics(force: true);
            });
            historyHeader?.RegisterCallback<ClickEvent>(_ =>
            {
                ToggleDrawer(historyHeader, historyBody, historyDrawer, "History");
                if (historyBody != null && !historyBody.ClassListContains("collapsed"))
                    RefreshHistory(force: true);
            });
        }

        private void SetupSettingsModal(VisualElement root)
        {
            if (settingsOverlay != null)
                settingsOverlay.focusable = true;
            root.Q<Button>("settings-open")?.RegisterCallback<ClickEvent>(_ => ShowSettingsModal());
            root.Q<Button>("settings-close")?.RegisterCallback<ClickEvent>(_ => HideSettingsModal());
            settingsOverlay?.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == settingsOverlay)
                    HideSettingsModal();
            });
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                if (worldOverlay != null && !worldOverlay.ClassListContains("hidden"))
                {
                    HideWorldDialog();
                    evt.StopPropagation();
                    return;
                }
                if (presetOverlay != null && !presetOverlay.ClassListContains("hidden"))
                {
                    HidePresetDialog();
                    evt.StopPropagation();
                    return;
                }
                if (settingsOverlay != null && !settingsOverlay.ClassListContains("hidden"))
                {
                    HideSettingsModal();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        private void ShowSettingsModal()
        {
            HideSettingTooltip();
            settingsOverlay?.RemoveFromClassList("hidden");
            settingsOverlay?.Focus();
        }

        private void HideSettingsModal()
        {
            HideSettingTooltip();
            settingsOverlay?.AddToClassList("hidden");
        }

        private void ToggleDrawer(Button header, VisualElement body, VisualElement drawer, string title)
        {
            HideSettingTooltip();
            if (header == null || body == null) return;
            SetDrawerCollapsed(header, body, drawer, title, !body.ClassListContains("collapsed"));
        }

        private static void SetDrawerCollapsed(Button header, VisualElement body, VisualElement drawer, string title, bool collapsed)
        {
            if (header == null || body == null) return;
            body.EnableInClassList("collapsed", collapsed);
            drawer?.EnableInClassList("collapsed", collapsed);
            header.text = title + (collapsed ? " ▸" : " ▾");
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
                    "Fire", "Oxygen", "Storm Charge", "Flora", "Light", "Genome", "Fauna", "Acoustic", "Grass", "Tree"
                };
                overlay.index = 0;
                overlay.RegisterValueChangedCallback(_ => display.SetOverlay(overlay.index));
            }
            var mode = root.Q<DropdownField>("brush-mode");
            if (mode != null)
            {
                mode.choices = new List<string>(Enum.GetNames(typeof(BrushMode)));
                mode.index = (int)BrushMode.Off;
                tools.Mode = BrushMode.Off;
                mode.RegisterValueChangedCallback(_ =>
                {
                    tools.Mode = (BrushMode)mode.index;
                    if (tools.Mode == BrushMode.Material || tools.Mode == BrushMode.Life)
                        RefreshMaterialDropdown(root, tools.Mode);
                });
            }
            materialDropdown = root.Q<DropdownField>("material");
            if (materialDropdown != null)
            {
                materialDropdown.RegisterValueChangedCallback(_ =>
                {
                    if (suppressingMaterialCallback) return;
                    ApplyMaterialChoice(root, materialDropdown.index);
                    RememberBrushMaterialIndex(materialCatalogMode, materialDropdown.index);
                });
                RefreshMaterialDropdown(root, BrushMode.Material);
            }
        }

        private void RefreshMaterialDropdown(VisualElement root, BrushMode catalogMode)
        {
            if (materialDropdown == null || host?.MaterialRegistry == null) return;
            materialCatalogMode = catalogMode;
            materialChoices.Clear();
            materialChoices.AddRange(BrushSelectionCatalog.BuildChoices(catalogMode, host.MaterialRegistry));
            var names = new List<string>(materialChoices.Count);
            foreach (BrushSelection choice in materialChoices) names.Add(choice.Label);

            int remembered = catalogMode == BrushMode.Life ? lastLifeBrushIndex : lastMaterialBrushIndex;
            if (remembered < 0 || remembered >= names.Count)
            {
                uint fallback = catalogMode == BrushMode.Life ? MaterialIds.Algae : MaterialIds.Soil;
                remembered = BrushSelectionCatalog.IndexOf(materialChoices, fallback);
                if (remembered < 0) remembered = 0;
            }

            suppressingMaterialCallback = true;
            materialDropdown.choices = names;
            if (names.Count > 0)
                materialDropdown.SetValueWithoutNotify(names[remembered]);
            suppressingMaterialCallback = false;
            ApplyMaterialChoice(root, remembered);
            RememberBrushMaterialIndex(catalogMode, remembered);
        }

        private void ApplyMaterialChoice(VisualElement root, int index)
        {
            if (index < 0 || index >= materialChoices.Count || tools == null) return;
            BrushSelection choice = materialChoices[index];
            tools.SelectedMaterialId = choice.Id;
            var settings = root.Q<ScrollView>("material-settings");
            if (BrushSelectionCatalog.IsRegistryMaterial(choice.Id))
                BuildMaterialSettings(settings, host.MaterialRegistry.Get((int)choice.Id));
            else
                BuildVirtualBrushSettings(settings, choice);
        }

        private void RememberBrushMaterialIndex(BrushMode catalogMode, int index)
        {
            if (catalogMode == BrushMode.Life) lastLifeBrushIndex = index;
            else if (catalogMode == BrushMode.Material) lastMaterialBrushIndex = index;
        }

        private void BuildVirtualBrushSettings(ScrollView container, BrushSelection choice)
        {
            if (container == null) return;
            HideSettingTooltip();
            container.Clear();
            string hint = choice.Id == BrushSelectionIds.MycoSpores
                ? "Paints mycology spore load. Rare strains roll per cell at the configured worldgen rate. Radius and Strength still apply."
                : "Plants adult grass in free slots on Soil. Radius still applies.";
            container.Add(new Label(hint));
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

        private void SetupEcologySubTabs(VisualElement root)
        {
            void Show(string name)
            {
                HideSettingTooltip();
                foreach (string pageName in EcologySubTabs)
                {
                    VisualElement page = root.Q($"settings-ecology-{pageName}");
                    if (page != null) page.style.display = pageName == name ? DisplayStyle.Flex : DisplayStyle.None;
                    Button tab = root.Q<Button>($"ecology-tab-{pageName}");
                    tab?.EnableInClassList("ecology-subtab--active", pageName == name);
                }
            }

            foreach (string name in EcologySubTabs)
            {
                Button button = root.Q<Button>($"ecology-tab-{name}");
                string captured = name;
                button?.RegisterCallback<ClickEvent>(_ => Show(captured));
            }

            Show("mycology");
        }

        private void SetupProbeHud(VisualElement root)
        {
            if (probe == null && host != null)
                probe = host.GetComponent<ProbeController>();
            if (display != null) display.FollowProbe = probe;

            probeToolsCard = root.Q("probe-tools-card");
            probeSteerCard = root.Q("probe-steer-card");
            probeCameraCard = root.Q("probe-camera-card");
            if (probeToolsCard != null) probeToolsCard.pickingMode = PickingMode.Position;
            if (probeSteerCard != null) probeSteerCard.pickingMode = PickingMode.Position;
            if (probeCameraCard != null) probeCameraCard.pickingMode = PickingMode.Position;
            BindProbeFadeHover(probeToolsCard);
            BindProbeFadeHover(probeSteerCard);
            BindProbeFadeHover(probeCameraCard);
            probeHoverCount = 0;
            probeActivityTime = Time.unscaledTime;

            probeEnergyFill = root.Q("probe-energy-fill");
            BindProbeActionButton(root.Q<Button>("probe-action-vapor"), ProbeAction.Vapor);
            BindProbeActionButton(root.Q<Button>("probe-action-water"), ProbeAction.Water);
            BindProbeActionButton(root.Q<Button>("probe-action-soil"), ProbeAction.Soil);
            BindProbeActionButton(root.Q<Button>("probe-action-cool"), ProbeAction.Cool);
            BindProbeActionButton(root.Q<Button>("probe-action-heat"), ProbeAction.Heat);

            lifeSeedButton = root.Q<Button>("probe-action-life");
            lifeSeedButton?.UnregisterCallback<ClickEvent>(OnLifeSeedClicked);
            lifeSeedButton?.RegisterCallback<ClickEvent>(OnLifeSeedClicked);

            steerLeftButton = root.Q<Button>("probe-steer-left");
            steerStopButton = root.Q<Button>("probe-steer-stop");
            steerRightButton = root.Q<Button>("probe-steer-right");
            steerLeftButton?.UnregisterCallback<ClickEvent>(OnSteerLeftClicked);
            steerStopButton?.UnregisterCallback<ClickEvent>(OnSteerStopClicked);
            steerRightButton?.UnregisterCallback<ClickEvent>(OnSteerRightClicked);
            steerLeftButton?.RegisterCallback<ClickEvent>(OnSteerLeftClicked);
            steerStopButton?.RegisterCallback<ClickEvent>(OnSteerStopClicked);
            steerRightButton?.RegisterCallback<ClickEvent>(OnSteerRightClicked);

            followCameraButton = root.Q<Button>("probe-camera-follow");
            globeCameraButton = root.Q<Button>("probe-camera-globe");
            followCameraButton?.UnregisterCallback<ClickEvent>(OnFollowCameraClicked);
            globeCameraButton?.UnregisterCallback<ClickEvent>(OnGlobeCameraClicked);
            followCameraButton?.RegisterCallback<ClickEvent>(OnFollowCameraClicked);
            globeCameraButton?.RegisterCallback<ClickEvent>(OnGlobeCameraClicked);
            RefreshLifeSeedButton();
            RefreshSteerButtons();
            RefreshCameraModeButtons();
            RefreshProbeEnergy();
            ApplyProbeFade(1f);
        }

        private void BindProbeFadeHover(VisualElement card)
        {
            if (card == null) return;
            card.UnregisterCallback<PointerEnterEvent>(OnProbeFadePointerEnter);
            card.UnregisterCallback<PointerLeaveEvent>(OnProbeFadePointerLeave);
            card.RegisterCallback<PointerEnterEvent>(OnProbeFadePointerEnter);
            card.RegisterCallback<PointerLeaveEvent>(OnProbeFadePointerLeave);
        }

        private void OnProbeFadePointerEnter(PointerEnterEvent _)
        {
            probeHoverCount++;
            probeActivityTime = Time.unscaledTime;
        }

        private void OnProbeFadePointerLeave(PointerLeaveEvent _)
        {
            probeHoverCount = Mathf.Max(0, probeHoverCount - 1);
            if (probeHoverCount == 0)
                probeActivityTime = Time.unscaledTime;
        }

        private void OnFollowCameraClicked(ClickEvent _) => SetCameraViewMode(CameraViewMode.ProbeFollow);

        private void OnGlobeCameraClicked(ClickEvent _) => SetCameraViewMode(CameraViewMode.Globe);

        private void OnLifeSeedClicked(ClickEvent evt)
        {
            probe?.SetLifeSeedActive(probe == null || !probe.LifeSeedActive);
            RefreshLifeSeedButton();
            evt.StopImmediatePropagation();
        }

        private void OnSteerLeftClicked(ClickEvent _) => SetProbeFlightMode(ProbeFlightMode.Counterclockwise);

        private void OnSteerStopClicked(ClickEvent _) => SetProbeFlightMode(ProbeFlightMode.Stopped);

        private void OnSteerRightClicked(ClickEvent _) => SetProbeFlightMode(ProbeFlightMode.Clockwise);

        private void SetProbeFlightMode(ProbeFlightMode mode)
        {
            probe?.SetFlightMode(mode);
            RefreshSteerButtons();
        }

        private void RefreshLifeSeedButton()
        {
            lifeSeedButton?.EnableInClassList("probe-tool-life--on", probe != null && probe.LifeSeedActive);
        }

        private void RefreshSteerButtons()
        {
            ProbeFlightMode mode = probe != null ? probe.FlightMode : ProbeFlightMode.Clockwise;
            steerLeftButton?.EnableInClassList("probe-camera-button--active", mode == ProbeFlightMode.Counterclockwise);
            steerStopButton?.EnableInClassList("probe-camera-button--active", mode == ProbeFlightMode.Stopped);
            steerRightButton?.EnableInClassList("probe-camera-button--active", mode == ProbeFlightMode.Clockwise);
        }

        private void RefreshProbeEnergy()
        {
            if (probeEnergyFill == null) return;
            float t = probe != null ? probe.EnergyNormalized : 1f;
            probeEnergyFill.style.width = Length.Percent(t * 100f);
            probeEnergyFill.style.backgroundColor = Color.Lerp(
                new Color(0.78f, 0.2f, 0.16f, 1f),
                new Color(0.28f, 0.78f, 0.35f, 1f),
                t);
        }

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
            RefreshHistory(force: true);
            RefreshHistory(force: true);
        }

        private void SetupWorldDialog(VisualElement root)
        {
            worldOverlay = root.Q("world-overlay");
            worldSaveGroup = root.Q("world-save-group");
            worldLoadGroup = root.Q("world-load-group");
            worldDialogTitle = root.Q<Label>("world-dialog-title");
            worldError = root.Q<Label>("world-error");
            worldEmpty = root.Q<Label>("world-empty");
            worldFilename = root.Q<TextField>("world-filename");
            worldList = root.Q<ListView>("world-list");
            worldConfirm = root.Q<Button>("world-confirm");
            root.Q<Button>("world-cancel")?.RegisterCallback<ClickEvent>(_ => HideWorldDialog());
            worldConfirm?.RegisterCallback<ClickEvent>(_ => ConfirmWorldDialog());
            if (worldList != null)
            {
                worldList.selectionType = SelectionType.Single;
                worldList.fixedItemHeight = 22;
                worldList.makeItem = () => new Label();
                worldList.bindItem = (element, index) =>
                {
                    if (element is Label label && index >= 0 && index < worldNames.Count)
                        label.text = worldNames[index];
                };
            }
        }

        private void ShowWorldSaveAsDialog()
        {
            worldDialogIsSave = true;
            if (worldDialogTitle != null) worldDialogTitle.text = "Save World";
            if (worldConfirm != null)
            {
                worldConfirm.text = "Save";
                worldConfirm.SetEnabled(true);
            }
            worldSaveGroup?.RemoveFromClassList("hidden");
            worldLoadGroup?.AddToClassList("hidden");
            if (worldFilename != null) worldFilename.value = currentWorldFileName ?? string.Empty;
            SetWorldError(null);
            ShowWorldOverlay();
        }

        private void ShowWorldLoadDialog()
        {
            worldDialogIsSave = false;
            if (worldDialogTitle != null) worldDialogTitle.text = "Load World";
            if (worldConfirm != null) worldConfirm.text = "Load";
            worldSaveGroup?.AddToClassList("hidden");
            worldLoadGroup?.RemoveFromClassList("hidden");
            SetWorldError(null);
            worldNames = snapshots.ListWorlds();
            bool empty = worldNames.Count == 0;
            worldEmpty?.EnableInClassList("hidden", !empty);
            if (worldList != null)
            {
                worldList.itemsSource = worldNames;
                worldList.Rebuild();
                worldList.selectedIndex = empty ? -1 : 0;
            }
            if (worldConfirm != null) worldConfirm.SetEnabled(!empty);
            ShowWorldOverlay();
        }

        private void ShowWorldOverlay()
        {
            HideSettingTooltip();
            worldOverlay?.RemoveFromClassList("hidden");
        }

        private void HideWorldDialog()
        {
            worldOverlay?.AddToClassList("hidden");
            if (worldConfirm != null) worldConfirm.SetEnabled(true);
        }

        private void SetWorldError(string message)
        {
            if (worldError == null) return;
            bool hasError = !string.IsNullOrEmpty(message);
            worldError.text = message ?? string.Empty;
            worldError.EnableInClassList("hidden", !hasError);
        }

        private void ConfirmWorldDialog()
        {
            if (worldDialogIsSave)
            {
                if (!WorldSnapshotService.TryNormalizeFileName(worldFilename != null ? worldFilename.value : string.Empty, out string fileName, out string error))
                {
                    SetWorldError(error);
                    return;
                }

                worldConfirm?.SetEnabled(false);
                BeginWorldSave(fileName, fromDialog: true);
                return;
            }

            int index = worldList != null ? worldList.selectedIndex : -1;
            if (index < 0 || index >= worldNames.Count)
            {
                SetWorldError("Select a world to load.");
                return;
            }

            string name = worldNames[index];
            if (!snapshots.Load(host, snapshots.GetPath(name)))
            {
                SetWorldError("File not found, invalid snapshot, or grid size does not match current world");
                return;
            }

            currentWorldFileName = name;
            RefreshWorldSaveButton();
            RefreshHistory(force: true);
            RefreshWorldMetrics(force: true);
            HideWorldDialog();
            SetWorldStatus($"Loaded world: {name}");
        }

        private void SaveCurrentWorld()
        {
            if (string.IsNullOrEmpty(currentWorldFileName) || worldSaveInProgress) return;
            BeginWorldSave(currentWorldFileName, fromDialog: false);
        }

        private void BeginWorldSave(string name, bool fromDialog)
        {
            worldSaveInProgress = true;
            RefreshWorldSaveButton();
            worldSaveAsButton?.SetEnabled(false);
            snapshots.Save(host, snapshots.GetPath(name), ok => OnWorldSaveComplete(name, ok, fromDialog));
        }

        private void OnWorldSaveComplete(string name, bool ok, bool fromDialog)
        {
            worldSaveInProgress = false;
            worldSaveAsButton?.SetEnabled(true);
            if (ok)
            {
                currentWorldFileName = name;
                if (fromDialog) HideWorldDialog();
                SetWorldStatus($"Saved world: {name}");
            }
            else if (fromDialog)
            {
                SetWorldError("Snapshot save failed.");
                if (worldConfirm != null) worldConfirm.SetEnabled(true);
            }
            else
            {
                SetWorldStatus("World save failed.");
            }
            RefreshWorldSaveButton();
        }

        private void RefreshWorldSaveButton()
        {
            worldSaveButton?.SetEnabled(!string.IsNullOrEmpty(currentWorldFileName) && !worldSaveInProgress);
        }

        private void SetWorldStatus(string message)
        {
            worldStatusMessage = message;
            worldStatusUntil = Time.unscaledTime + 3f;
            if (statusLabel != null) statusLabel.text = message;
        }

        private void RestoreDefaultSettings(VisualElement root)
        {
            host.RestoreDefaultSettings();
            RefreshBoundControls(root);
            BuildSettings(root);
            RefreshPlayLabel();
            RefreshWorldMetrics(force: true);
            RefreshHistory(force: true);
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
                { "ecology-mycology", root.Q<ScrollView>("settings-ecology-mycology") },
                { "ecology-algae", root.Q<ScrollView>("settings-ecology-algae") },
                { "ecology-cricket", root.Q<ScrollView>("settings-ecology-cricket") },
                { "ecology-wasp", root.Q<ScrollView>("settings-ecology-wasp") },
                { "ecology-grass", root.Q<ScrollView>("settings-ecology-grass") },
                { "ecology-tree", root.Q<ScrollView>("settings-ecology-tree") },
                { "ecology-detritus", root.Q<ScrollView>("settings-ecology-detritus") },
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
                    if (!currentTab.StartsWith("ecology-", StringComparison.Ordinal)
                        && containers.TryGetValue(currentTab, out ScrollView sectionContainer) && sectionContainer != null)
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
                    var control = new Toggle(SettingLabel(field.Name)) { value = (bool)field.GetValue(host.Config) };
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
                    var control = new FloatField(SettingLabel(field.Name)) { value = (float)field.GetValue(host.Config) };
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
                "Selects what left-drag paints. Off does nothing; Material paints geology and Detritus; Life uses the Material field as a type picker for organisms and seeds; otherwise heat, water, pressure, vapor, or ignition. Hold right-click to inspect the cell under the cursor.");
            AttachNamedSettingTooltip(root, "material",
                "Material",
                "Material the brush paints in Material mode (0–13 plus Detritus). In Life mode this field picks the organism or seed type: Algae/Moss spores, Cricket, Cricket Egg, Myco Spores, Grass Seeds, or Tree Sprout. Registry materials still expose editable properties below.");
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
            AttachNamedSettingTooltip(root, "probe-action-life",
                "Life Seed",
                "Toggle to drop mixed dormant organisms from the probe aim cell: algae spores and cricket eggs, one per tick in small bursts about every three seconds.");
            AttachNamedSettingTooltip(root, "probe-steer-left",
                "Reverse Probe",
                "Flies the probe counterclockwise. Deposits stay ahead of the new heading, and the sprite faces along reverse travel.");
            AttachNamedSettingTooltip(root, "probe-steer-stop",
                "Hold Station",
                "Hovers the probe in place. Energy does not regenerate while stopped; hold tools and life seed still drain and still fire.");
            AttachNamedSettingTooltip(root, "probe-steer-right",
                "Clockwise Flight",
                "Resumes the default clockwise orbit along the atmosphere glow, opposite the sun.");
            AttachNamedSettingTooltip(root, "probe-energy-bar",
                "Probe Energy",
                "Starts at 100. Active tools and life seed drain 3 per sim tick; otherwise it regenerates 10 per sim second. Stopped flight pauses regen. Actions still fire at 0.");
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
                BindSettingTooltip(control, SettingLabel(fieldName), tooltip);
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

        private static string SettingLabel(string fieldName)
        {
            if (fieldName == nameof(SimulationConfig.useOgWorldgen)) return "Use OG Worldgen";
            if (fieldName == nameof(SimulationConfig.uiFadeDelay)) return "UI Fade Delay";
            return Humanize(fieldName);
        }

        private static string Humanize(string value)
        {
            if (value.StartsWith("fauna", StringComparison.Ordinal))
                value = "cricket" + value.Substring(5);
            else if (value.StartsWith("flora", StringComparison.Ordinal))
                value = "algae" + value.Substring(5);

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
            if (!string.IsNullOrEmpty(worldStatusMessage) && Time.unscaledTime < worldStatusUntil)
                statusLabel.text = worldStatusMessage;
            else
            {
                worldStatusMessage = null;
                statusLabel.text = $"Tick {host.Clock.TickCount:N0} | {(host.Clock.IsRunning ? "Running" : "Paused")} | {host.LastTickMilliseconds:F2} ms CPU dispatch";
            }

            if (statusBody != null && !statusBody.ClassListContains("collapsed"))
            {
                metricsRefreshTimer -= Time.unscaledDeltaTime;
                if (metricsRefreshTimer <= 0f)
                    RefreshWorldMetrics(force: false);
            }

            if (historyBody != null && !historyBody.ClassListContains("collapsed"))
                RefreshHistory(force: false);

            RefreshLifeSeedButton();
            RefreshSteerButtons();
            RefreshProbeEnergy();
            RefreshHudFades();
        }

        private void RefreshHudFades()
        {
            float delay = host.Config != null ? host.Config.uiFadeDelay : 8f;
            if (ProbeHudIsActive())
                probeActivityTime = Time.unscaledTime;
            ApplyProbeFade(HudFadeAlpha(Time.unscaledTime - probeActivityTime, delay, HudFadeDuration, ProbeHudIdleAlpha));

            if (!hasInspection) return;
            ApplyInspectFade(HudFadeAlpha(Time.unscaledTime - inspectActivityTime, delay, HudFadeDuration));
        }

        private bool ProbeHudIsActive() =>
            probeHoverCount > 0 || (probe != null && probe.ActiveAction != ProbeAction.None);

        private void ApplyProbeFade(float alpha)
        {
            SetElementOpacity(probeSteerCard, alpha);
            SetElementOpacity(probeToolsCard, alpha);
            SetElementOpacity(probeCameraCard, alpha);
        }

        private void ApplyInspectFade(float alpha)
        {
            if (inspectBar == null) return;
            SetElementOpacity(inspectBar, alpha);
            if (alpha <= 0f)
            {
                inspectBar.AddToClassList("hidden");
                SetInspectPicking(false);
            }
            else
            {
                inspectBar.RemoveFromClassList("hidden");
                SetInspectPicking(true);
            }
        }

        private void SetInspectPicking(bool enabled)
        {
            PickingMode mode = enabled ? PickingMode.Position : PickingMode.Ignore;
            if (envInspectPanel != null) envInspectPanel.pickingMode = mode;
            if (lifeInspectPanel != null) lifeInspectPanel.pickingMode = mode;
        }

        private static void SetElementOpacity(VisualElement element, float alpha)
        {
            if (element != null)
                element.style.opacity = alpha;
        }

        public static float HudFadeAlpha(float secondsSinceActivity, float delay, float duration, float idleAlpha = 0f)
        {
            idleAlpha = Mathf.Clamp01(idleAlpha);
            if (secondsSinceActivity <= delay) return 1f;
            if (duration <= 0f) return idleAlpha;
            float t = Mathf.Clamp01((secondsSinceActivity - delay) / duration);
            return Mathf.Lerp(1f, idleAlpha, t);
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

        private void SetupInspectPanels()
        {
            envInspectPanel?.RegisterCallback<ClickEvent>(evt =>
            {
                envInspectExpanded = !envInspectExpanded;
                RefreshInspectionDisplay();
                evt.StopPropagation();
            });
            lifeInspectPanel?.RegisterCallback<ClickEvent>(evt =>
            {
                lifeInspectExpanded = !lifeInspectExpanded;
                RefreshInspectionDisplay();
                evt.StopPropagation();
            });
        }

        private void SetInspection(CellInspection inspection)
        {
            lastInspection = inspection;
            hasInspection = true;
            inspectActivityTime = Time.unscaledTime;
            inspectBar?.RemoveFromClassList("hidden");
            SetElementOpacity(inspectBar, 1f);
            SetInspectPicking(true);
            RefreshInspectionDisplay();
        }

        private void RefreshInspectionDisplay()
        {
            if (!hasInspection) return;
            envInspectPanel?.EnableInClassList("inspect-panel--expanded", envInspectExpanded);
            lifeInspectPanel?.EnableInClassList("inspect-panel--expanded", lifeInspectExpanded);
            if (envInspectTitle != null)
                envInspectTitle.text = envInspectExpanded ? "Environment ▾" : "Environment ▸";
            if (lifeInspectTitle != null)
                lifeInspectTitle.text = lifeInspectExpanded ? "Life ▾" : "Life ▸";
            if (envInspectLabel != null)
                envInspectLabel.text = FormatEnvironmentInspection(lastInspection, envInspectExpanded);
            if (lifeInspectLabel != null)
                lifeInspectLabel.text = FormatLifeInspection(lastInspection, lifeInspectExpanded);
        }

        private string FormatEnvironmentInspection(CellInspection inspection, bool expanded)
        {
            GeneSys.Materials.MaterialDefinition definition = host.MaterialRegistry.Get((int)inspection.materialId);
            string materialName = definition != null ? definition.displayName : inspection.materialId.ToString();
            string header =
                $"Cell θ:{inspection.cell.x} r:{inspection.cell.y}\n" +
                $"Material: {materialName}\n" +
                $"T {inspection.state.x:F2}  P {inspection.state.y:F3}";
            if (!expanded) return header;
            return header +
                   $"\nWater {inspection.state.z:F3}  Charge {inspection.state.w:F3}\n" +
                   $"Vapor {inspection.aux.x:F3}  Ground {inspection.aux.y:F3}\n" +
                   $"Nutrient {inspection.aux.z:F3}  Stress {inspection.aux.w:F3}\n" +
                   $"Wind θ {inspection.flow.x:F3}  r {inspection.flow.y:F3}\n" +
                   $"Light {inspection.light:F3}";
        }

        private string FormatLifeInspection(CellInspection inspection, bool expanded)
        {
            float floraGeneRange = host.Config != null ? host.Config.floraGeneExpressionRange : 0.45f;
            float faunaGeneRange = host.Config != null ? host.Config.faunaGeneExpressionRange : 0.45f;
            string header =
                $"Spores {inspection.ecology.x:F3}  Myco {inspection.ecology.y:F3}\n" +
                $"Strain {MycologyTraits.Describe(MycologyTraits.FromFloat(inspection.ecology.z))}\n" +
                $"Stage {FloraGenome.DescribeStage(FloraGenome.Stage(inspection.genome))}  gen {FloraGenome.Generation(inspection.genome)}  toxin {FloraGenome.ToxinDose(inspection.genome)}";
            if (!expanded) return header;
            return header +
                   $"\nO2 {inspection.combustion.x:F3}  Flame {inspection.combustion.y:F3}\n" +
                   $"Soot {inspection.combustion.z:F3}  Ignite {inspection.combustion.w:F3}\n" +
                   $"Storm Q {inspection.storm.x:F3}  Bolt {inspection.storm.y:F3}\n" +
                   $"Flash {inspection.storm.z:F3}  Break {inspection.storm.w:F3}\n" +
                   $"Flora spores {inspection.life.x:F3}  biomass {inspection.life.y:F3}\n" +
                   $"Energy {inspection.life.z:F3}  exudate {inspection.life.w:F3}\n" +
                   FloraGenome.DescribeGenes(inspection.genome, floraGeneRange) + "\n" +
                   $"Fauna cal {inspection.faunaVitals.x:F3}  hyd {inspection.faunaVitals.y:F3}  age {inspection.faunaVitals.z:F0}  cd {inspection.faunaVitals.w:F0}\n" +
                   $"Fauna {FaunaGenome.DescribeStage(FaunaGenome.Stage(inspection.faunaGenome))} / {FaunaGenome.DescribeBehavior(FaunaGenome.Behavior(inspection.faunaGenome))}  gen {FaunaGenome.Generation(inspection.faunaGenome)}\n" +
                   $"Call feed {inspection.acoustic.x:F3}  mate {inspection.acoustic.y:F3}\n" +
                   FaunaGenome.DescribeGenes(inspection.faunaGenome, faunaGeneRange) +
                   FormatGrassInspection(inspection, host.Config != null ? host.Config.grassGeneExpressionRange : 0.45f) +
                   FormatWaspInspection(inspection, host.Config != null ? host.Config.waspGeneExpressionRange : 0.45f) +
                   FormatTreeInspection(inspection, host.Config != null ? host.Config.treeGeneExpressionRange : 0.45f);
        }

        private static string FormatWaspInspection(CellInspection inspection, float expressionRange)
        {
            uint stage = WaspGenome.Stage(inspection.waspGenome);
            if (!WaspGenome.IsLivingStage(stage)) return "";
            var text = new System.Text.StringBuilder();
            text.Append($"\nWasp {WaspGenome.DescribeStage(stage)} / {WaspGenome.DescribeBehavior(WaspGenome.Behavior(inspection.waspGenome))}");
            text.Append($" gen {WaspGenome.Generation(inspection.waspGenome)} lin {WaspGenome.Lineage(inspection.waspGenome)}");
            text.Append($"\n  cal {inspection.waspVitals.x:F3}  hyd {inspection.waspVitals.y:F3}  age {inspection.waspVitals.z:F0}  cd {inspection.waspVitals.w:F0}");
            text.Append($"\n  vel θ {inspection.waspMotion.x:F3}  r {inspection.waspMotion.y:F3}");
            if (inspection.waspCargo != null)
            {
                int carried = 0;
                var lineages = new System.Text.StringBuilder();
                for (int slot = 0; slot < inspection.waspCargo.Length; slot++)
                {
                    if (!WaspGenome.CargoValid(inspection.waspCargo[slot])) continue;
                    if (carried > 0) lineages.Append(", ");
                    lineages.Append(WaspGenome.CargoLineage(inspection.waspCargo[slot]));
                    carried++;
                }
                text.Append($"\n  pollen {carried}/{inspection.waspCargo.Length}");
                if (carried > 0) text.Append($"  donors {lineages}");
            }
            text.Append($"\n  {WaspGenome.DescribeGenes(inspection.waspGenome, expressionRange)}");
            return text.ToString();
        }

        private static string FormatTreeInspection(CellInspection inspection, float expressionRange)
        {
            uint stage = TreeGenome.Stage(inspection.treeGenome);
            if (stage == TreeGenome.StageEmpty && inspection.treeTopology.X == 0) return "";
            uint role = TreeGenome.Role(inspection.treeTopology.Z);
            var text = new System.Text.StringBuilder();
            text.Append($"\nTree {TreeGenome.DescribeStage(stage)} / {TreeGenome.DescribeRole(role)}");
            text.Append($" gen {TreeGenome.Generation(inspection.treeGenome)} lin {TreeGenome.Lineage(inspection.treeGenome)}");
            text.Append($"\n  energy {inspection.treePhysiology.x:F2}  hyd {inspection.treePhysiology.y:F2}  nut {inspection.treePhysiology.z:F2}  hp {inspection.treePhysiology.w:F2}");
            text.Append($"\n  owner {inspection.treeTopology.X}  parent {inspection.treeTopology.Y}  flags {TreeGenome.Flags(inspection.treeTopology.Z)}");
            float timer = System.BitConverter.Int32BitsToSingle(unchecked((int)inspection.treeTopology.W));
            if (role == TreeGenome.RoleLeaf || stage == TreeGenome.StageDead)
                text.Append($"  timer {timer:F0}");
            text.Append($"\n  {TreeGenome.DescribeGenes(inspection.treeGenome, expressionRange)}");
            return text.ToString();
        }

        private void RefreshPlayLabel()
        {
            if (playButton != null) playButton.text = host.Clock.IsRunning ? "Pause" : "Play";
        }

        private static string FormatGrassInspection(CellInspection inspection, float expressionRange)
        {
            if (inspection.grassGenomes == null || inspection.grassLife == null) return "";
            var text = new System.Text.StringBuilder();
            for (int slot = 0; slot < inspection.grassGenomes.Length; slot++)
            {
                GrassGenome.Packed genome = inspection.grassGenomes[slot];
                uint stage = GrassGenome.Stage(genome);
                if (!GrassGenome.IsLivingStage(stage)) continue;
                Vector4 life = inspection.grassLife[slot];
                Vector4 timing = inspection.grassTiming != null && slot < inspection.grassTiming.Length
                    ? inspection.grassTiming[slot] : Vector4.zero;
                uint flags = GrassGenome.TimingFlags(timing.w);
                text.Append($"\nGrass {slot} {GrassGenome.DescribeStage(stage)} gen {GrassGenome.Generation(genome)} lin {GrassGenome.Lineage(genome)}");
                text.Append($"\n  bio {life.x:F2}  energy {life.y:F2}  hyd {life.z:F2}  nectar {life.w:F2}");
                text.Append($"\n  roots {GrassGenome.RootMask(flags)}  flower {(GrassGenome.IsFlowering(flags) ? "open" : "idle")}  pollen {(GrassGenome.IsPollinated(flags) ? "yes" : "no")}");
                if (inspection.grassDonors != null && slot < inspection.grassDonors.Length && GrassGenome.HasDonor(inspection.grassDonors[slot]))
                    text.Append($"\n  donor lin {GrassGenome.Lineage(inspection.grassDonors[slot])}");
                text.Append($"\n  {GrassGenome.DescribeGenes(genome, expressionRange)}");
            }
            return text.ToString();
        }

        private float MetricsRefreshIntervalSeconds()
        {
            if (host == null || !host.IsReady) return 1.5f;
            int cells = host.Grid.CellCount;
            float interval = 1.5f;
            if (cells >= 1_500_000) interval = 4f;
            else if (cells >= 400_000) interval = 2.5f;
            if (host.Clock.Speed > SimulationClock.FastForwardSpeed)
                interval *= host.Clock.Speed;
            return interval;
        }

        private void RefreshHistory(bool force)
        {
            if (historyLog == null || host == null) return;
            if (!force && (historyBody == null || historyBody.ClassListContains("collapsed"))) return;
            OrganismHistoryLog log = host.OrganismHistory;
            if (!force && historyRenderedVersion == log.Version) return;
            historyRenderedVersion = log.Version;
            historyLog.Clear();
            IReadOnlyList<OrganismHistoryLog.Entry> entries = log.Entries;
            if (entries.Count == 0)
            {
                var empty = new Label("No organism events yet.");
                empty.AddToClassList("history-empty");
                historyLog.Add(empty);
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var line = new Label(entries[i].Format());
                line.AddToClassList("history-entry");
                historyLog.Add(line);
            }

            historyLog.schedule.Execute(() =>
            {
                historyLog.scrollOffset = new Vector2(0f, historyLog.contentContainer.layout.height);
            });
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
                SimulationMetrics.MeasureFaunaAsync(host, fauna =>
                {
                    SimulationMetrics.MeasureWaspAsync(host, wasp =>
                    {
                        SimulationMetrics.MeasureTreeAsync(host, tree =>
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
                                $"Organisms {metrics.OrganismCount}  cricket {fauna.AdultCount}  nymph {fauna.JuvenileCount}  eggs {fauna.EggCount}\n" +
                                $"Fauna cal {fauna.TotalCalories:F2}  hyd {fauna.TotalHydration:F2}\n" +
                                $"Wasps {wasp.AdultCount}  larvae {wasp.JuvenileCount}  eggs {wasp.EggCount}  carrying {wasp.PollenCarrierCount} ({wasp.PollenSampleCount} pollen)\n" +
                                $"Wasp cal {wasp.TotalCalories:F2}  hyd {wasp.TotalHydration:F2}  gen {wasp.MeanGeneration:F1}\n" +
                                $"Trees {tree.AnchorCount}  pixels {tree.PixelCount}  sprout {tree.SproutCount}  sapling {tree.SaplingCount}  mature {tree.TreeCount}  dead {tree.DeadCount}";
                        }
                        });
                    });
                });
            });
        }

        private string SnapshotPath => System.IO.Path.Combine(Application.persistentDataPath, "genesys-phase1.snapshot");
        private void SaveSnapshot() => snapshots.Save(host, SnapshotPath, ok => Debug.Log(ok ? $"Saved {SnapshotPath}" : "Snapshot save failed."));
        private void LoadSnapshot()
        {
            if (!snapshots.Load(host, SnapshotPath))
            {
                Debug.LogWarning("Snapshot load failed or grid preset differs.");
                return;
            }
            RefreshHistory(force: true);
        }
    }
}
