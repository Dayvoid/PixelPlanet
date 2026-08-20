using System;
using System.Collections.Generic;
using System.IO;
using GeneSys.Configuration;
using UnityEngine;

namespace GeneSys.Persistence
{
    public sealed class SettingsPresetService
    {
        public const string Extension = ".preset";
        public const string InvalidCharactersMessage = "Filename contains unacceptable characters.";

        private readonly string directoryOverride;
        private string resolvedDirectory;

        public string DirectoryPath => resolvedDirectory ??= string.IsNullOrWhiteSpace(directoryOverride)
            ? Path.Combine(Application.persistentDataPath, "presets")
            : directoryOverride;

        public SettingsPresetService(string directoryPath = null)
        {
            directoryOverride = directoryPath;
        }

        public static bool TryNormalizeFileName(string input, out string name, out string error)
        {
            name = null;
            error = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a filename.";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - Extension.Length).Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "." || trimmed == "..")
            {
                error = "Enter a valid filename.";
                return false;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = InvalidCharactersMessage;
                return false;
            }

            name = trimmed;
            return true;
        }

        public string GetPath(string name) => Path.Combine(DirectoryPath, name + Extension);

        public bool Save(SimulationConfig config, string rawName, out string error)
        {
            if (!TryNormalizeFileName(rawName, out string name, out error))
                return false;
            if (config == null)
            {
                error = "No configuration to save.";
                return false;
            }

            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(GetPath(name), JsonUtility.ToJson(config, true));
            return true;
        }

        public bool Load(SimulationConfig config, string rawName, out string error)
        {
            if (config == null)
            {
                error = "No configuration to load into.";
                return false;
            }
            if (!TryNormalizeFileName(rawName, out string name, out error))
                return false;

            string path = GetPath(name);
            if (!File.Exists(path))
            {
                error = "Preset file was not found.";
                return false;
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Preset file is empty.";
                return false;
            }

            JsonUtility.FromJsonOverwrite(json, config);
            config.grid.Validate();
            return true;
        }

        public List<string> ListPresets()
        {
            var names = new List<string>();
            if (!Directory.Exists(DirectoryPath))
                return names;

            foreach (string file in Directory.GetFiles(DirectoryPath, "*" + Extension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
