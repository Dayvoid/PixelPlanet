using System;
using System.Collections.Generic;
using System.IO;
using GeneSys.AI;
using GeneSys.Configuration;
using GeneSys.Simulation;
using GeneSys.UI;
using NUnit.Framework;
using UnityEngine;

namespace GeneSys.Tests
{
    public sealed class AiCrewSettingsTests
    {
        private string directory;
        private AiCrewSettingsService service;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "genesys-ai-settings", Guid.NewGuid().ToString("N"));
            service = new AiCrewSettingsService(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Test]
        public void SaveAndLoadRoundTripsCrewSettings()
        {
            var source = new AiCrewSettings
            {
                host = "10.0.0.4",
                port = 1234,
                model = "gemma-4",
                agentLoopDelaySeconds = 20f,
                maxToolCallsPerStep = 8
            };
            source.Mode = GameMode.AiSandbox;
            source.visionCapable = true;
            source.verboseCrewLogs = true;
            Assert.That(service.Save(source, out string saveError), Is.True, saveError);

            AiCrewSettings loaded = service.LoadOrDefault();
            Assert.That(loaded.host, Is.EqualTo("10.0.0.4"));
            Assert.That(loaded.port, Is.EqualTo(1234));
            Assert.That(loaded.model, Is.EqualTo("gemma-4"));
            Assert.That(loaded.agentLoopDelaySeconds, Is.EqualTo(20f).Within(0.001f));
            Assert.That(loaded.maxToolCallsPerStep, Is.EqualTo(8));
            Assert.That(loaded.Mode, Is.EqualTo(GameMode.AiSandbox));
            Assert.That(loaded.DeityToolsAllowed, Is.True);
            Assert.That(loaded.AgentLoopAllowed, Is.True);
            Assert.That(loaded.visionCapable, Is.True);
            Assert.That(loaded.verboseCrewLogs, Is.True);
        }

        [Test]
        public void SandboxDisablesAiSystems()
        {
            var settings = new AiCrewSettings();
            settings.Mode = GameMode.Sandbox;
            settings.Sanitize();
            Assert.That(settings.AiSystemsEnabled, Is.False);
            Assert.That(settings.AgentLoopAllowed, Is.False);
            Assert.That(settings.DeityToolsAllowed, Is.False);
        }

        [Test]
        public void StoryIsStubWithoutDeityTools()
        {
            var settings = new AiCrewSettings();
            settings.Mode = GameMode.Story;
            settings.Sanitize();
            Assert.That(settings.AiSystemsEnabled, Is.True);
            Assert.That(settings.AgentLoopAllowed, Is.False);
            Assert.That(settings.DeityToolsAllowed, Is.False);
        }

        [Test]
        public void CrewmateKeepsAgentLoopWithoutDeityTools()
        {
            var source = new AiCrewSettings();
            source.Mode = GameMode.AiCrewmate;
            source.Sanitize();
            Assert.That(source.Mode, Is.EqualTo(GameMode.AiCrewmate));
            Assert.That(source.AiSystemsEnabled, Is.True);
            Assert.That(source.AgentLoopAllowed, Is.True);
            Assert.That(source.DeityToolsAllowed, Is.False);
            Assert.That(service.Save(source, out string saveError), Is.True, saveError);

            AiCrewSettings loaded = service.LoadOrDefault();
            Assert.That(loaded.Mode, Is.EqualTo(GameMode.AiCrewmate));
            Assert.That(loaded.AgentLoopAllowed, Is.True);
            Assert.That(loaded.DeityToolsAllowed, Is.False);
        }
    }

    public sealed class AiToolRegistryTests
    {
        [Test]
        public void BuildOpenAiToolsEmitsAvailableNames()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var tools = registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiSandbox);
            var names = new List<string>();
            foreach (var token in tools)
                names.Add(token["function"]?["name"]?.ToString());
            Assert.That(names, Does.Contain("get_planet_summary"));
            Assert.That(names, Does.Contain("next_step"));
            Assert.That(names, Does.Not.Contain("probe_steer"));
            Assert.That(names, Does.Not.Contain("planet_adjust_field"));
        }

        [Test]
        public void ConvertStepIncludesDeityToolsOnlyInAiSandbox()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var sandbox = registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiSandbox);
            var story = registry.BuildOpenAiTools(ActStep.Convert, GameMode.Story);
            var sandboxNames = new List<string>();
            var storyNames = new List<string>();
            foreach (var token in sandbox)
                sandboxNames.Add(token["function"]?["name"]?.ToString());
            foreach (var token in story)
                storyNames.Add(token["function"]?["name"]?.ToString());
            Assert.That(sandboxNames, Does.Contain("planet_adjust_field"));
            Assert.That(sandboxNames, Does.Contain("probe_steer"));
            Assert.That(sandboxNames, Does.Not.Contain("send_chat"));
            Assert.That(storyNames, Does.Contain("probe_steer"));
            Assert.That(storyNames, Does.Not.Contain("planet_adjust_field"));
            Assert.That(storyNames, Does.Not.Contain("send_chat"));
        }

        [Test]
        public void UserConvertExposesOnlySendChatAndNextStep()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var names = ToolNames(registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiSandbox, false, PromptKind.User));
            Assert.That(names, Is.EquivalentTo(new[] { "next_step", "send_chat" }));
        }

        [Test]
        public void UserAssessAndThinkKeepSensorsWithoutSendChat()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var assess = ToolNames(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiSandbox, false, PromptKind.User));
            var think = ToolNames(registry.BuildOpenAiTools(ActStep.Think, GameMode.AiSandbox, false, PromptKind.User));
            Assert.That(assess, Does.Contain("get_planet_summary"));
            Assert.That(assess, Does.Contain("next_step"));
            Assert.That(assess, Does.Not.Contain("send_chat"));
            Assert.That(assess, Does.Not.Contain("probe_steer"));
            Assert.That(think, Does.Contain("get_planet_summary"));
            Assert.That(think, Does.Contain("next_step"));
            Assert.That(think, Does.Not.Contain("send_chat"));
            Assert.That(think, Does.Not.Contain("probe_steer"));
        }

        [Test]
        public void SandboxModeExposesNoTools()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var tools = registry.BuildOpenAiTools(ActStep.Assess, GameMode.Sandbox);
            Assert.That(tools.Count, Is.EqualTo(0));
        }

        [Test]
        public void VisionToolIsGatedBySettingAndStep()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var assessOff = ToolNames(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiSandbox));
            var assessOn = ToolNames(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiSandbox, true));
            var thinkOn = ToolNames(registry.BuildOpenAiTools(ActStep.Think, GameMode.AiSandbox, true));
            var convertOn = ToolNames(registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiSandbox, true));
            Assert.That(assessOff, Does.Not.Contain("capture_probe_view"));
            Assert.That(assessOn, Does.Contain("capture_probe_view"));
            Assert.That(thinkOn, Does.Contain("capture_probe_view"));
            Assert.That(convertOn, Does.Not.Contain("capture_probe_view"));
            Assert.That(assessOn, Does.Not.Contain("poll_sensor_arrays"));
        }

        [Test]
        public void CrewmateExposesProbeAndPlacedSensorsWithoutOmniscience()
        {
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            var assess = ToolNames(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiCrewmate, true));
            var convert = ToolNames(registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiCrewmate, true));
            var think = ToolNames(registry.BuildOpenAiTools(ActStep.Think, GameMode.AiCrewmate, true));
            var userConvert = ToolNames(registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiCrewmate, true, PromptKind.User));

            Assert.That(assess, Does.Contain("poll_sensor_arrays"));
            Assert.That(assess, Does.Contain("probe_status"));
            Assert.That(assess, Does.Contain("next_step"));
            Assert.That(assess, Does.Not.Contain("probe_steer"));
            Assert.That(assess, Does.Not.Contain("get_planet_summary"));
            Assert.That(assess, Does.Not.Contain("get_species_metrics"));
            Assert.That(assess, Does.Not.Contain("inspect_cell"));
            Assert.That(assess, Does.Not.Contain("get_recent_organism_events"));
            Assert.That(assess, Does.Not.Contain("capture_probe_view"));
            Assert.That(assess, Does.Not.Contain("planet_adjust_field"));

            Assert.That(convert, Does.Contain("probe_steer"));
            Assert.That(convert, Does.Contain("probe_use_tool"));
            Assert.That(convert, Does.Contain("probe_toggle_life_seed"));
            Assert.That(convert, Does.Contain("probe_status"));
            Assert.That(convert, Does.Not.Contain("poll_sensor_arrays"));
            Assert.That(convert, Does.Not.Contain("planet_adjust_field"));
            Assert.That(convert, Does.Not.Contain("set_world_parameter"));
            Assert.That(convert, Does.Not.Contain("terraform"));
            Assert.That(convert, Does.Not.Contain("seed_life"));
            Assert.That(convert, Does.Not.Contain("capture_probe_view"));
            Assert.That(convert, Does.Not.Contain("get_planet_summary"));

            Assert.That(think, Does.Contain("poll_sensor_arrays"));
            Assert.That(think, Does.Not.Contain("capture_probe_view"));
            Assert.That(think, Does.Not.Contain("probe_steer"));
            Assert.That(userConvert, Is.EquivalentTo(new[] { "next_step", "send_chat" }));

            string status = Description(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiCrewmate), "probe_status");
            Assert.That(status, Does.Not.Contain("energy").IgnoreCase);
            string sandboxStatus = Description(registry.BuildOpenAiTools(ActStep.Assess, GameMode.AiSandbox), "probe_status");
            Assert.That(sandboxStatus, Does.Contain("energy").IgnoreCase);
        }

        [Test]
        public void ProbeStatusOmitsEnergyForCrewmate()
        {
            string crew = AiBuiltinTools.FormatProbeStatus(
                0.5f, ProbeFlightMode.Stopped, ProbeAction.Heat, false, new Vector2Int(1, 2), false, 0.25f);
            string sandbox = AiBuiltinTools.FormatProbeStatus(
                0.5f, ProbeFlightMode.Stopped, ProbeAction.Heat, true, new Vector2Int(1, 2), true, 0.25f);
            Assert.That(crew, Does.Not.Contain("energy").IgnoreCase);
            Assert.That(crew, Does.Contain("flight=Stopped"));
            Assert.That(crew, Does.Contain("action=Heat"));
            Assert.That(crew, Does.Contain("aim=(1,2)"));
            Assert.That(sandbox, Does.Contain("energy="));
            Assert.That(sandbox, Does.Contain("lifeSeed=True"));
        }

        [Test]
        public void FormatSensorArrayReadoutsUsesPlacedSlotText()
        {
            var slot = new SensorSlotGpu
            {
                Alive = 1,
                Serial = 7,
                AnchorAx = 3,
                AnchorAy = 10,
                AnchorBx = 4,
                AnchorBy = 10,
                SurfaceMaterial = 7,
                AtmTemp = 21.5f,
                AtmVapor = 0.01f,
                AtmWater = 0.2f,
                SurfaceTemp = 18.25f,
                SurfaceFilm = 0.4f,
                SurfaceGround = 0.1f
            };
            string text = AiBuiltinTools.FormatSensorArrayReadouts(new[] { slot }, _ => "Soil", 0.01f);
            Assert.That(text, Does.Contain("slot 0 serial=7 anchors=(3,10)-(4,10)"));
            Assert.That(text, Does.Contain(SimulationUIController.FormatSensorReadout(slot, "Soil", 0.01f)));

            var empty = new SensorSlotGpu[SensorArrayLogic.MaxSensors];
            Assert.That(AiBuiltinTools.FormatSensorArrayReadouts(empty, _ => "Soil", 0.01f), Is.EqualTo("No sensor arrays are placed."));
            Assert.That(AiBuiltinTools.FormatSensorArrayReadouts(null, _ => "Soil", 0.01f), Is.EqualTo("No sensor arrays are placed."));
        }

        private static List<string> ToolNames(Newtonsoft.Json.Linq.JArray tools)
        {
            var names = new List<string>();
            foreach (var token in tools)
                names.Add(token["function"]?["name"]?.ToString());
            return names;
        }

        private static string Description(Newtonsoft.Json.Linq.JArray tools, string name)
        {
            foreach (var token in tools)
            {
                if (token["function"]?["name"]?.ToString() == name)
                    return token["function"]?["description"]?.ToString();
            }

            return string.Empty;
        }
    }

    public sealed class AiPromptQueueTests
    {
        [Test]
        public void UserPromptsAreDequeuedBeforeAgentPrompts()
        {
            var queue = new AiPromptQueue();
            queue.EnqueueAgent("agent-1");
            queue.EnqueueAgent("agent-2");
            queue.EnqueueUser("player");
            Assert.That(queue.TryDequeue(out QueuedPrompt first), Is.True);
            Assert.That(first.Kind, Is.EqualTo(PromptKind.User));
            Assert.That(first.Text, Is.EqualTo("player"));
            Assert.That(queue.TryDequeue(out QueuedPrompt second), Is.True);
            Assert.That(second.Kind, Is.EqualTo(PromptKind.Agent));
            Assert.That(second.Text, Is.EqualTo("agent-1"));
        }

        [Test]
        public void ActLoopWalksAssessConvertThinkThenCompletes()
        {
            var loop = new ActLoopMachine();
            loop.Begin();
            Assert.That(loop.Step, Is.EqualTo(ActStep.Assess));
            Assert.That(loop.IsComplete, Is.False);
            loop.NextStep();
            Assert.That(loop.Step, Is.EqualTo(ActStep.Convert));
            loop.NextStep();
            Assert.That(loop.Step, Is.EqualTo(ActStep.Think));
            loop.NextStep();
            Assert.That(loop.IsComplete, Is.True);
            Assert.That(loop.Step, Is.EqualTo(ActStep.Think));
        }
    }

    public sealed class OccupiedNoticeTests
    {
        [Test]
        public void OccupiedNoticeShowsOnlyWhenBusyAndVerboseOff()
        {
            Assert.That(AiAgentOrchestrator.ShouldShowOccupiedNotice(true, false), Is.True);
            Assert.That(AiAgentOrchestrator.ShouldShowOccupiedNotice(true, true), Is.False);
            Assert.That(AiAgentOrchestrator.ShouldShowOccupiedNotice(false, false), Is.False);
            Assert.That(AiAgentOrchestrator.ShouldShowOccupiedNotice(false, true), Is.False);
        }

        [Test]
        public void CrewmateNoticeNamesAliceAndSkipsLiveSummary()
        {
            Assert.That(AiAgentOrchestrator.OccupiedNoticeFor(GameMode.AiCrewmate), Does.Contain("Alice"));
            Assert.That(AiAgentOrchestrator.OccupiedNoticeFor(GameMode.AiSandbox), Does.Contain("Deity"));
            Assert.That(AiAgentOrchestrator.IncludesLivePlanetSummary(GameMode.AiCrewmate), Is.False);
            Assert.That(AiAgentOrchestrator.IncludesLivePlanetSummary(GameMode.AiSandbox), Is.True);
            Assert.That(AiAgentOrchestrator.AttachesVisionToEachPrompt(GameMode.AiCrewmate, true), Is.True);
            Assert.That(AiAgentOrchestrator.AttachesVisionToEachPrompt(GameMode.AiCrewmate, false), Is.False);
            Assert.That(AiAgentOrchestrator.AttachesVisionToEachPrompt(GameMode.AiSandbox, true), Is.False);
            Assert.That(AiAgentOrchestrator.AgentTurnPrompt(GameMode.AiCrewmate), Is.EqualTo(AiAgentOrchestrator.CrewmateAgentTurn));
            Assert.That(AiAgentOrchestrator.CrewmateVisionLine, Does.Contain("attached to each prompt"));
        }
    }

    public sealed class LlmClientParseTests
    {
        [Test]
        public void ParseChatResponseReadsNativeToolCalls()
        {
            const string json = "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"next_step\",\"arguments\":\"{}\"}}]}}]}";
            LlmChatResult result = LlmClient.ParseChatResponse(json);
            Assert.That(result.Ok, Is.True);
            Assert.That(result.HasToolCalls, Is.True);
            Assert.That(result.ToolCalls[0].Function.Name, Is.EqualTo("next_step"));
        }

        [Test]
        public void ParseChatResponseFallsBackToEmbeddedJson()
        {
            const string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"I will inspect first {\\\"name\\\":\\\"get_planet_summary\\\",\\\"arguments\\\":{}} then act.\"}}]}";
            LlmChatResult result = LlmClient.ParseChatResponse(json);
            Assert.That(result.Ok, Is.True);
            Assert.That(result.HasToolCalls, Is.True);
            Assert.That(result.ToolCalls[0].Function.Name, Is.EqualTo("get_planet_summary"));
        }

        [Test]
        public void ParseModelsReadsDataIds()
        {
            const string json = "{\"data\":[{\"id\":\"google/gemma-4\",\"owned_by\":\"lmstudio\"},{\"id\":\"local-model\"}]}";
            Assert.That(LlmClient.TryParseModels(json, out List<LlmModelInfo> models, out string error), Is.True, error);
            Assert.That(models.Count, Is.EqualTo(2));
            Assert.That(models[0].Id, Is.EqualTo("google/gemma-4"));
        }

        [Test]
        public void BuildChatRequestIncludesTools()
        {
            var messages = new List<LlmMessage> { new() { Role = "user", Content = "hi" } };
            var registry = new AiToolRegistry();
            AiBuiltinTools.RegisterAll(registry);
            string body = LlmClient.BuildChatRequest("gemma-4", messages, registry.BuildOpenAiTools(ActStep.Convert, GameMode.AiSandbox));
            Assert.That(body, Does.Contain("\"model\":\"gemma-4\""));
            Assert.That(body, Does.Contain("probe_steer"));
            Assert.That(body, Does.Contain("tool_choice"));
        }

        [Test]
        public void BuildChatRequestEmbedsJpegAsImageUrl()
        {
            var messages = new List<LlmMessage>
            {
                new()
                {
                    Role = "user",
                    Content = "look",
                    ImageJpegBase64 = "QQ=="
                }
            };
            string body = LlmClient.BuildChatRequest("gemma-4", messages, null);
            Assert.That(body, Does.Contain("\"type\":\"image_url\""));
            Assert.That(body, Does.Contain("data:image/jpeg;base64,QQ=="));
            Assert.That(body, Does.Contain("\"type\":\"text\""));
        }
    }

    public sealed class AiActionLogTests
    {
        [Test]
        public void BuildLineIsDeterministicForSameInputs()
        {
            var utc = new DateTime(2026, 9, 10, 7, 0, 0, DateTimeKind.Utc);
            string a = AiActionLog.BuildLine(utc, 42, ActStep.Convert, "probe_steer", "{\"direction\":\"clockwise\"}", "ok");
            string b = AiActionLog.BuildLine(utc, 42, ActStep.Convert, "probe_steer", "{\"direction\":\"clockwise\"}", "ok");
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Does.Contain("\"tick\":42"));
            Assert.That(a, Does.Contain("probe_steer"));
        }

        [Test]
        public void HistorySanitizeCollapsesNewlinesAndTruncates()
        {
            string summary = AiHistoryLog.Sanitize("line one\nline two " + new string('x', 300));
            Assert.That(summary, Does.Not.Contain("\n"));
            Assert.That(summary.Length, Is.LessThanOrEqualTo(AiHistoryLog.SummaryMaxLength));
        }

        [Test]
        public void HistorySanitizeVerboseAllowsLongerOneLine()
        {
            string verbose = AiHistoryLog.Sanitize("line one\nline two " + new string('x', 900), AiHistoryLog.VerboseMaxLength);
            Assert.That(verbose, Does.Not.Contain("\n"));
            Assert.That(verbose.Length, Is.GreaterThan(AiHistoryLog.SummaryMaxLength));
            Assert.That(verbose.Length, Is.LessThanOrEqualTo(AiHistoryLog.VerboseMaxLength));
        }

        [Test]
        public void FormatChatterUsesStepAndContent()
        {
            Assert.That(
                AiHistoryLog.FormatChatter(ActStep.Assess, "I can't help but just chat sometimes"),
                Is.EqualTo("[Assess] (Chatter): I can't help but just chat sometimes"));
        }
    }

    public sealed class AiPrimerTests
    {
        [Test]
        public void CrewmatePromptIntroducesAliceWithoutDeityOrEnergy()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.floraGrowthTempMin = 17.375f;
            string prompt = AiPrimerLibrary.BuildSystemPrompt(config, GameMode.AiCrewmate);
            Assert.That(prompt, Does.Contain("Alice"));
            Assert.That(prompt, Does.Contain("Monitor conditions and attempt to stabilize planetary conditions to establish a thriving ecosystem."));
            Assert.That(prompt, Does.Contain("17.375"));
            Assert.That(prompt, Does.Contain("poll_sensor_arrays"));
            Assert.That(prompt, Does.Not.Contain("Current planetary"));
            Assert.That(prompt, Does.Not.Contain("planet_adjust_field"));
            Assert.That(prompt, Does.Not.Contain("set_world_parameter"));
            Assert.That(prompt, Does.Not.Contain("terraform"));
            Assert.That(prompt, Does.Not.Contain("seed_life"));
            Assert.That(prompt, Does.Not.Contain("capture_probe_view"));
            Assert.That(prompt, Does.Not.Contain("energy").IgnoreCase);
            Assert.That(prompt, Does.Not.Contain("Deity"));
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void InterpolateInsertsLiveSurvivalValues()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            config.floraSurvivalTempMin = -3f;
            config.floraSurvivalTempMax = 77f;
            string text = AiPrimerLibrary.Interpolate("flora {floraSurvivalTempMin}..{floraSurvivalTempMax}", config);
            Assert.That(text, Is.EqualTo("flora -3..77"));
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void WorldParameterCatalogRejectsUnknownFields()
        {
            SimulationConfig config = ScriptableObject.CreateInstance<SimulationConfig>();
            Assert.That(WorldParameterCatalog.TrySet(config, "notAField", 1f, out string message), Is.False);
            Assert.That(message, Does.Contain("whitelist"));
            Assert.That(WorldParameterCatalog.TrySet(config, "solarIntensity", 1.5f, out string ok), Is.True, ok);
            Assert.That(config.solarIntensity, Is.EqualTo(1.5f).Within(0.0001f));
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void PolarCellMappingWrapsTheta()
        {
            var grid = GeneSys.Simulation.Topology.PolarGridDefinition.Validation;
            Vector2Int cell = PolarCellMapping.FromNormalized(grid, 1.25f, 1f);
            Assert.That(cell.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(cell.x, Is.LessThan(grid.angularResolution));
            Assert.That(cell.y, Is.EqualTo(grid.radialResolution - 1));
        }
    }
}
