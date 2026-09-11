using System;
using System.Collections.Generic;
using System.IO;
using GeneSys.AI;
using GeneSys.Configuration;
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
            Assert.That(storyNames, Does.Contain("probe_steer"));
            Assert.That(storyNames, Does.Not.Contain("planet_adjust_field"));
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
        }

        private static List<string> ToolNames(Newtonsoft.Json.Linq.JArray tools)
        {
            var names = new List<string>();
            foreach (var token in tools)
                names.Add(token["function"]?["name"]?.ToString());
            return names;
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
    }

    public sealed class AiPrimerTests
    {
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
