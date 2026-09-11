using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GeneSys.AI
{
    public sealed class AiActionLog
    {
        public const string FileName = "actions.jsonl";
        private readonly string directoryOverride;
        private string resolvedDirectory;
        private readonly List<string> entries = new();

        public string DirectoryPath => resolvedDirectory ??= string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "ai-logs")
            : directoryOverride;

        public string FilePath => Path.Combine(DirectoryPath, FileName);
        public IReadOnlyList<string> Entries => entries;

        public AiActionLog(string directoryPath = null)
        {
            directoryOverride = directoryPath;
        }

        public string Record(long tick, ActStep step, string toolName, string argumentsJson, string result)
        {
            string line = BuildLine(DateTime.UtcNow, tick, step, toolName, argumentsJson, result);
            entries.Add(line);
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(FilePath, line + "\n");
            return line;
        }

        public static string BuildLine(DateTime utc, long tick, ActStep step, string toolName, string argumentsJson, string result)
        {
            return $"{{\"utc\":\"{utc:O}\",\"tick\":{tick},\"step\":\"{step}\",\"tool\":{Quote(toolName)},\"arguments\":{Quote(argumentsJson)},\"result\":{Quote(result)}}}";
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }
    }

    public sealed class AiScratchpad
    {
        public const string FileName = "scratchpad.txt";
        private readonly string directoryOverride;
        private string resolvedDirectory;
        private string content = string.Empty;

        public string DirectoryPath => resolvedDirectory ??= string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "ai-logs")
            : directoryOverride;

        public string FilePath => Path.Combine(DirectoryPath, FileName);
        public string Content => content ?? string.Empty;

        public AiScratchpad(string directoryPath = null)
        {
            directoryOverride = directoryPath;
        }

        public string Read()
        {
            if (!string.IsNullOrEmpty(content)) return content;
            if (File.Exists(FilePath))
                content = File.ReadAllText(FilePath) ?? string.Empty;
            return content;
        }

        public void Write(string text)
        {
            content = text ?? string.Empty;
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, content, Encoding.UTF8);
        }
    }

    public sealed class AiHistoryLog
    {
        public const int Capacity = 256;
        public const int SummaryMaxLength = 280;
        public const int VerboseMaxLength = 800;
        private readonly List<AiHistoryEntry> entries = new(Capacity);

        public int Version { get; private set; }
        public IReadOnlyList<AiHistoryEntry> Entries => entries;

        public void Add(long tick, string summary) => Add(tick, summary, SummaryMaxLength);

        public void Add(long tick, string summary, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(summary)) return;
            entries.Add(new AiHistoryEntry(tick, Sanitize(summary, maxLength), DateTime.UtcNow));
            int overflow = entries.Count - Capacity;
            if (overflow > 0) entries.RemoveRange(0, overflow);
            Version++;
        }

        public static string Sanitize(string text) => Sanitize(text, SummaryMaxLength);

        public static string Sanitize(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            int limit = Mathf.Max(4, maxLength);
            string trimmed = text.Trim();
            if (trimmed.Length > limit) trimmed = trimmed.Substring(0, limit - 3) + "...";
            return trimmed.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
