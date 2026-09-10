using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GeneSys.AI
{
    public enum GameMode
    {
        Story = 0,
        Sandbox = 1,
        AiSandbox = 2
    }

    public enum ActStep
    {
        Assess = 0,
        Convert = 1,
        Think = 2
    }

    [Flags]
    public enum ActStepMask
    {
        None = 0,
        Assess = 1 << ActStep.Assess,
        Convert = 1 << ActStep.Convert,
        Think = 1 << ActStep.Think,
        All = Assess | Convert | Think
    }

    public enum PromptKind
    {
        Agent = 0,
        User = 1
    }

    public readonly struct QueuedPrompt
    {
        public readonly PromptKind Kind;
        public readonly string Text;

        public QueuedPrompt(PromptKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class LlmMessage
    {
        [JsonProperty("role")] public string Role;
        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)] public string Content;
        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)] public string ToolCallId;
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name;
        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)] public List<LlmToolCall> ToolCalls;
    }

    [Serializable]
    public sealed class LlmToolCall
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type = "function";
        [JsonProperty("function")] public LlmToolFunction Function;
    }

    [Serializable]
    public sealed class LlmToolFunction
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("arguments")] public string Arguments;
    }

    public sealed class LlmChatResult
    {
        public bool Ok;
        public string Error;
        public string Content;
        public string FinishReason;
        public List<LlmToolCall> ToolCalls = new();

        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;

        public static LlmChatResult Failed(string error) => new()
        {
            Ok = false,
            Error = error ?? "Unknown LLM error."
        };
    }

    public sealed class LlmModelInfo
    {
        public string Id;
        public string OwnedBy;
    }

    public sealed class AiChatMessage
    {
        public string Role;
        public string Text;
        public bool IsError;
        public DateTime UtcTime;
    }

    public readonly struct AiHistoryEntry
    {
        public readonly long Tick;
        public readonly string Summary;
        public readonly DateTime UtcTime;

        public AiHistoryEntry(long tick, string summary, DateTime utcTime)
        {
            Tick = tick;
            Summary = summary ?? string.Empty;
            UtcTime = utcTime;
        }

        public string Format() => $"Tick {Tick}  {Summary}";
    }
}
