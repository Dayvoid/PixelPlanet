using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GeneSys.Configuration;
using GeneSys.Rendering;
using GeneSys.Simulation;
using GeneSys.Tools;
using GeneSys.Validation;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GeneSys.AI
{
    public sealed class AiAgentOrchestrator : MonoBehaviour
    {
        public const float FullyZoomedEpsilon = 0.02f;
        public const float MinOrthographicSize = 0.75f;

        [SerializeField] private SimulationHost host;
        [SerializeField] private ProbeController probe;
        [SerializeField] private SimulationTools tools;
        [SerializeField] private PlanetoidDisplayRenderer display;

        private readonly LlmClient client = new();
        private readonly AiToolRegistry registry = new();
        private readonly AiPromptQueue queue = new();
        private readonly ActLoopMachine loop = new();
        private readonly List<LlmMessage> conversation = new();
        private readonly List<AiChatMessage> chat = new();
        private AiCrewSettingsService settingsService;
        private AiCrewSettings settings;
        private AiActionLog actionLog;
        private AiScratchpad scratchpad;
        private string cachedPlanetSummary = "Planetary summary not yet measured.";
        private bool planetSummaryPending;
        private bool busy;
        private bool agentLoopEnabled;
        private bool advanceRequested;
        private bool initialized;
        private float loopDelayRemaining;
        private Coroutine running;
        private byte[] pendingVisionJpeg;

        public AiCrewSettings Settings => settings;
        public AiToolRegistry Tools => registry;
        public AiPromptQueue Queue => queue;
        public ActLoopMachine Loop => loop;
        public AiHistoryLog History { get; } = new();
        public IReadOnlyList<AiChatMessage> ChatMessages => chat;
        public bool IsBusy => busy;
        public bool AgentLoopEnabled => agentLoopEnabled;
        public bool AiSystemsEnabled => settings != null && settings.AiSystemsEnabled;
        public ActStep CurrentStep => loop.Step;
        public string ConnectionStatus { get; private set; } = "Not connected.";
        public IReadOnlyList<LlmModelInfo> AvailableModels { get; private set; } = Array.Empty<LlmModelInfo>();

        public event Action ChatChanged;
        public event Action HistoryChanged;
        public event Action LoopStateChanged;
        public event Action SettingsChanged;

        public void Initialize(SimulationHost simulationHost, ProbeController probeController,
            SimulationTools simulationTools, PlanetoidDisplayRenderer renderer,
            AiCrewSettingsService service = null)
        {
            host = simulationHost;
            probe = probeController;
            tools = simulationTools;
            display = renderer;
            settingsService = service ?? new AiCrewSettingsService();
            settings = settingsService.LoadOrDefault();
            actionLog = new AiActionLog();
            scratchpad = new AiScratchpad();
            scratchpad.Read();
            registry.Clear();
            AiBuiltinTools.RegisterAll(registry);
            BindToolContext();
            initialized = true;
            LoopStateChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        public void ApplySettings(AiCrewSettings next)
        {
            settings = next ?? new AiCrewSettings();
            settings.Sanitize();
            settingsService ??= new AiCrewSettingsService();
            settingsService.Save(settings, out _);
            if (!settings.AgentLoopAllowed && agentLoopEnabled)
                SetAgentLoopEnabled(false);
            BindToolContext();
            SettingsChanged?.Invoke();
        }

        public void SetAgentLoopEnabled(bool enabled)
        {
            agentLoopEnabled = enabled && settings != null && settings.AgentLoopAllowed;
            if (agentLoopEnabled)
            {
                loopDelayRemaining = 0f;
                if (!busy) EnqueueAgentTurn();
            }

            LoopStateChanged?.Invoke();
        }

        public void EnqueueUserPrompt(string text)
        {
            if (!AiSystemsEnabled || string.IsNullOrWhiteSpace(text)) return;
            string trimmed = text.Trim();
            AppendChat("user", trimmed, false);
            queue.EnqueueUser(trimmed);
            if (!busy) StartNext();
        }

        public IEnumerator FetchModels()
        {
            if (settings == null)
            {
                ConnectionStatus = "Settings missing.";
                SettingsChanged?.Invoke();
                yield break;
            }

            ConnectionStatus = "Fetching models...";
            SettingsChanged?.Invoke();
            bool ok = false;
            string error = null;
            List<LlmModelInfo> models = null;
            yield return client.ListModels(settings.BaseUrl, (success, fail, list) =>
            {
                ok = success;
                error = fail;
                models = list;
            });
            if (!ok)
            {
                ConnectionStatus = error ?? "Fetch failed.";
                SettingsChanged?.Invoke();
                yield break;
            }

            AvailableModels = models ?? new List<LlmModelInfo>();
            if (AvailableModels.Count == 0)
                ConnectionStatus = "No models returned.";
            else
            {
                ConnectionStatus = $"Connected. {AvailableModels.Count} model(s).";
                if (string.IsNullOrWhiteSpace(settings.model))
                    settings.model = AvailableModels[0].Id;
            }

            SettingsChanged?.Invoke();
        }

        public bool IsChatVisible()
        {
            if (!AiSystemsEnabled || display == null || display.TargetCamera == null) return false;
            if (display.ViewMode != CameraViewMode.ProbeFollow) return false;
            return display.TargetCamera.orthographicSize <= MinOrthographicSize + FullyZoomedEpsilon;
        }

        private void Update()
        {
            if (!initialized || settings == null || !settings.AiSystemsEnabled) return;
            BindToolContext();
            if (!agentLoopEnabled || busy || !settings.AgentLoopAllowed) return;
            loopDelayRemaining -= Time.unscaledDeltaTime;
            if (loopDelayRemaining <= 0f)
                EnqueueAgentTurn();
        }

        private void EnqueueAgentTurn()
        {
            queue.EnqueueAgent("Continue the ACT loop toward stable organism populations.");
            loopDelayRemaining = Mathf.Max(1f, settings.agentLoopDelaySeconds);
            if (!busy) StartNext();
        }

        private void StartNext()
        {
            if (busy || running != null) return;
            if (!queue.TryDequeue(out QueuedPrompt prompt)) return;
            running = StartCoroutine(RunLoop(prompt));
        }

        private IEnumerator RunLoop(QueuedPrompt prompt)
        {
            busy = true;
            LoopStateChanged?.Invoke();
            yield return RefreshPlanetSummary();
            loop.Begin();
            BindToolContext();
            conversation.Add(new LlmMessage { Role = "user", Content = BuildTurnContent(prompt) });
            TrimConversation();

            while (!loop.IsComplete)
            {
                int toolCalls = 0;
                advanceRequested = false;
                bool stepDone = false;
                while (!stepDone && toolCalls < Mathf.Max(1, settings.maxToolCallsPerStep))
                {
                    LlmChatResult result = null;
                    yield return client.Chat(settings.BaseUrl, settings.model, BuildMessages(),
                        registry.BuildOpenAiTools(loop.Step, settings.Mode, settings.visionCapable), chatResult => result = chatResult);
                    if (result == null || !result.Ok)
                    {
                        string error = result?.Error ?? "LLM request failed.";
                        AppendChat("assistant", error, true);
                        loop.Complete();
                        stepDone = true;
                        break;
                    }

                    if (result.HasToolCalls)
                    {
                        conversation.Add(new LlmMessage
                        {
                            Role = "assistant",
                            Content = string.IsNullOrWhiteSpace(result.Content) ? null : result.Content,
                            ToolCalls = result.ToolCalls
                        });
                        foreach (LlmToolCall call in result.ToolCalls)
                        {
                            toolCalls++;
                            string toolResult = null;
                            yield return ExecuteTool(call, value => toolResult = value);
                            conversation.Add(new LlmMessage
                            {
                                Role = "tool",
                                ToolCallId = call.Id,
                                Name = call.Function?.Name,
                                Content = toolResult ?? string.Empty
                            });
                            AttachPendingVisionFrame();
                            if (advanceRequested)
                            {
                                stepDone = true;
                                loop.NextStep();
                                break;
                            }
                        }
                    }
                    else
                    {
                        string text = string.IsNullOrWhiteSpace(result.Content)
                            ? "(no content)"
                            : result.Content.Trim();
                        conversation.Add(new LlmMessage { Role = "assistant", Content = text });
                        if (loop.Step == ActStep.Think)
                            AppendChat("assistant", text, false);
                        else
                            AppendChat("assistant", $"[{loop.Step}] {text}", false);
                        History.Add(host != null ? host.Clock.TickCount : 0L, AiHistoryLog.FormatChatter(loop.Step, text));
                        HistoryChanged?.Invoke();

                        stepDone = true;
                        loop.NextStep();
                    }
                }

                if (!stepDone)
                    loop.NextStep();
            }

            loopDelayRemaining = Mathf.Max(1f, settings.agentLoopDelaySeconds);
            busy = false;
            running = null;
            LoopStateChanged?.Invoke();
            StartNext();
        }

        private IEnumerator ExecuteTool(LlmToolCall call, Action<string> completed)
        {
            string name = call?.Function?.Name;
            string args = call?.Function?.Arguments ?? "{}";
            AiTool tool = registry.Find(name);
            if (tool == null)
            {
                completed?.Invoke($"Unknown tool '{name}'.");
                yield break;
            }

            if (!AiToolRegistry.IsAvailable(tool, loop.Step, settings.Mode, settings.visionCapable))
            {
                completed?.Invoke($"Tool '{name}' is not available during {loop.Step} in {settings.Mode}.");
                yield break;
            }

            string result = null;
            bool finished = false;
            try
            {
                tool.Handler?.Invoke(args, value =>
                {
                    result = value;
                    finished = true;
                });
            }
            catch (Exception exception)
            {
                result = exception.Message;
                finished = true;
            }

            if (tool.Handler == null) finished = true;
            float timeout = 35f;
            while (!finished && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!finished) result = "Tool timed out.";
            long tick = host != null ? host.Clock.TickCount : 0L;
            if (!string.Equals(name, "log_note", StringComparison.OrdinalIgnoreCase))
                actionLog?.Record(tick, loop.Step, name, args, result);
            if (settings != null && settings.verboseCrewLogs)
            {
                History.Add(tick, $"[{loop.Step}] {name} {args} -> {result}", AiHistoryLog.VerboseMaxLength);
                HistoryChanged?.Invoke();
            }
            completed?.Invoke(result ?? string.Empty);
        }

        private IEnumerator RefreshPlanetSummary()
        {
            if (host == null || !host.IsReady || planetSummaryPending) yield break;
            planetSummaryPending = true;
            bool done = false;
            SimulationMetrics.MeasureAsync(host, metrics =>
            {
                cachedPlanetSummary = $"meanT={metrics.MeanTemperature:0.##} RH={metrics.MeanRelativeHumidity:0.###} " +
                                      $"moisture={metrics.MeanMoisture:0.###} organisms={metrics.OrganismCount} " +
                                      $"waterS={metrics.SurfaceWaterMass:0.##} waterG={metrics.GroundwaterMass:0.##} vapor={metrics.VaporMass:0.##}";
                done = true;
                planetSummaryPending = false;
            });
            float timeout = 8f;
            while (!done && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            planetSummaryPending = false;
        }

        private List<LlmMessage> BuildMessages()
        {
            var messages = new List<LlmMessage>
            {
                new()
                {
                    Role = "system",
                    Content = BuildSystemContent()
                }
            };
            messages.AddRange(conversation);
            return messages;
        }

        private string BuildSystemContent()
        {
            SimulationConfig config = host != null ? host.Config : null;
            var builder = new StringBuilder();
            builder.AppendLine(AiPrimerLibrary.BuildSystemPrompt(config, settings.Mode));
            builder.AppendLine();
            builder.AppendLine("Current planetary summary:");
            builder.AppendLine(cachedPlanetSummary);
            builder.AppendLine();
            builder.AppendLine("Scratchpad:");
            builder.AppendLine(string.IsNullOrWhiteSpace(scratchpad?.Read()) ? "(empty)" : scratchpad.Read());
            builder.AppendLine();
            builder.Append("You are in ACT step ").Append(loop.Step)
                .Append(". Use only tools listed for this step. Call next_step when ready to proceed.");
            if (settings != null && settings.visionCapable)
            {
                builder.AppendLine();
                builder.Append("Vision is enabled. During Assess and Think you may call capture_probe_view to attach a probe-follow screenshot at 50% zoom.");
            }
            return builder.ToString();
        }

        private string BuildTurnContent(QueuedPrompt prompt)
        {
            if (prompt.Kind == PromptKind.User)
                return "Player message (handle after finishing any in-flight reasoning, then continue the ACT loop):\n" + prompt.Text;
            return prompt.Text;
        }

        private void TrimConversation()
        {
            const int max = 40;
            int overflow = conversation.Count - max;
            if (overflow > 0) conversation.RemoveRange(0, overflow);
        }

        private void AttachPendingVisionFrame()
        {
            if (pendingVisionJpeg == null || pendingVisionJpeg.Length == 0) return;
            for (int i = 0; i < conversation.Count; i++)
                conversation[i].ImageJpegBase64 = null;
            conversation.Add(new LlmMessage
            {
                Role = "user",
                Content = "Vision frame from capture_probe_view (probe-follow, 50% zoom). Use this image with the preceding tool result.",
                ImageJpegBase64 = Convert.ToBase64String(pendingVisionJpeg)
            });
            pendingVisionJpeg = null;
        }

        private void BindToolContext()
        {
            registry.Context = new AiToolContext
            {
                Host = host,
                Probe = probe,
                Tools = tools,
                Scratchpad = scratchpad,
                ActionLog = actionLog,
                Display = display,
                OnVisionFrame = (jpeg, _, _) => pendingVisionJpeg = jpeg,
                StartRoutine = StartCoroutine,
                RequestNextStep = () => advanceRequested = true,
                Mode = settings != null ? settings.Mode : GameMode.Sandbox,
                Step = loop.Step
            };
        }

        private void AppendChat(string role, string text, bool error)
        {
            chat.Add(new AiChatMessage
            {
                Role = role,
                Text = text,
                IsError = error,
                UtcTime = DateTime.UtcNow
            });
            ChatChanged?.Invoke();
        }
    }
}
