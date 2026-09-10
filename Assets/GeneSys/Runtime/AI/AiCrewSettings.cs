using System;
using System.IO;
using UnityEngine;

namespace GeneSys.AI
{
    [Serializable]
    public sealed class AiCrewSettings
    {
        public string host = "127.0.0.1";
        public int port = 1234;
        public string model = "";
        public float agentLoopDelaySeconds = 15f;
        public int gameMode = (int)GameMode.AiSandbox;
        public int maxToolCallsPerStep = 12;
        public bool visionCapable;

        public GameMode Mode
        {
            get => (GameMode)Mathf.Clamp(gameMode, 0, 2);
            set => gameMode = (int)value;
        }

        public bool AiSystemsEnabled => Mode != GameMode.Sandbox;
        public bool AgentLoopAllowed => Mode == GameMode.AiSandbox;
        public bool DeityToolsAllowed => Mode == GameMode.AiSandbox;

        public string BaseUrl
        {
            get
            {
                string trimmedHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim().TrimEnd('/');
                if (trimmedHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || trimmedHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return $"{trimmedHost}:{Mathf.Clamp(port, 1, 65535)}";
                return $"http://{trimmedHost}:{Mathf.Clamp(port, 1, 65535)}";
            }
        }

        public void Sanitize()
        {
            if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
            port = Mathf.Clamp(port, 1, 65535);
            agentLoopDelaySeconds = Mathf.Clamp(agentLoopDelaySeconds, 1f, 600f);
            maxToolCallsPerStep = Mathf.Clamp(maxToolCallsPerStep, 1, 64);
            gameMode = Mathf.Clamp(gameMode, 0, 2);
            model ??= string.Empty;
        }
    }

    public sealed class AiCrewSettingsService
    {
        public const string FileName = "ai-crew-settings.json";
        private readonly string directoryOverride;
        private string resolvedDirectory;

        public string DirectoryPath => resolvedDirectory ??= string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "ai-crew")
            : directoryOverride;

        public string FilePath => Path.Combine(DirectoryPath, FileName);

        public AiCrewSettingsService(string directoryPath = null)
        {
            directoryOverride = directoryPath;
        }

        public AiCrewSettings LoadOrDefault()
        {
            if (!File.Exists(FilePath))
            {
                var created = new AiCrewSettings();
                created.Sanitize();
                return created;
            }

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                var created = new AiCrewSettings();
                created.Sanitize();
                return created;
            }

            var settings = JsonUtility.FromJson<AiCrewSettings>(json) ?? new AiCrewSettings();
            settings.Sanitize();
            return settings;
        }

        public bool Save(AiCrewSettings settings, out string error)
        {
            error = null;
            if (settings == null)
            {
                error = "No AI crew settings to save.";
                return false;
            }

            settings.Sanitize();
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(settings, true));
            return true;
        }
    }
}
