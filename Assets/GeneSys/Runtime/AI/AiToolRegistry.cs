using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GeneSys.AI
{
    public delegate void AiToolHandler(string argumentsJson, Action<string> completed);

    public sealed class AiTool
    {
        public string Name;
        public string Description;
        public JObject Parameters;
        public ActStepMask AllowedSteps;
        public bool RequiresDeity;
        public bool RequiresVision;
        public AiToolHandler Handler;
    }

    public sealed class AiToolContext
    {
        public Simulation.SimulationHost Host;
        public Simulation.ProbeController Probe;
        public Tools.SimulationTools Tools;
        public AiScratchpad Scratchpad;
        public AiActionLog ActionLog;
        public Rendering.PlanetoidDisplayRenderer Display;
        public Action<byte[], int, int> OnVisionFrame;
        public Func<System.Collections.IEnumerator, UnityEngine.Coroutine> StartRoutine;
        public Action RequestNextStep;
        public GameMode Mode;
        public ActStep Step;
    }

    public sealed class AiToolRegistry
    {
        private readonly List<AiTool> tools = new();

        public AiToolContext Context { get; set; }

        public IReadOnlyList<AiTool> Tools => tools;

        public void Add(AiTool tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name)) return;
            tools.Add(tool);
        }

        public void Clear() => tools.Clear();

        public AiTool Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            for (int i = 0; i < tools.Count; i++)
            {
                if (string.Equals(tools[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return tools[i];
            }

            return null;
        }

        public JArray BuildOpenAiTools(ActStep step, GameMode mode, bool visionCapable = false)
        {
            var array = new JArray();
            for (int i = 0; i < tools.Count; i++)
            {
                AiTool tool = tools[i];
                if (!IsAvailable(tool, step, mode, visionCapable)) continue;
                array.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description ?? string.Empty,
                        ["parameters"] = tool.Parameters ?? EmptyObjectSchema()
                    }
                });
            }

            return array;
        }

        public static bool IsAvailable(AiTool tool, ActStep step, GameMode mode, bool visionCapable = false)
        {
            if (tool == null) return false;
            if (mode == GameMode.Sandbox) return false;
            if (tool.RequiresDeity && mode != GameMode.AiSandbox) return false;
            if (tool.RequiresVision && !visionCapable) return false;
            return (tool.AllowedSteps & Mask(step)) != 0;
        }

        public static ActStepMask Mask(ActStep step) => (ActStepMask)(1 << (int)step);

        public static JObject ObjectSchema(params (string name, JObject schema, bool required)[] properties)
        {
            var props = new JObject();
            var required = new JArray();
            if (properties != null)
            {
                foreach ((string name, JObject schema, bool isRequired) in properties)
                {
                    props[name] = schema;
                    if (isRequired) required.Add(name);
                }
            }

            var schemaObject = new JObject
            {
                ["type"] = "object",
                ["properties"] = props
            };
            if (required.Count > 0) schemaObject["required"] = required;
            return schemaObject;
        }

        public static JObject StringProp(string description, params string[] enums)
        {
            var schema = new JObject
            {
                ["type"] = "string",
                ["description"] = description ?? string.Empty
            };
            if (enums is { Length: > 0 })
                schema["enum"] = new JArray(enums);
            return schema;
        }

        public static JObject NumberProp(string description) => new()
        {
            ["type"] = "number",
            ["description"] = description ?? string.Empty
        };

        public static JObject IntegerProp(string description) => new()
        {
            ["type"] = "integer",
            ["description"] = description ?? string.Empty
        };

        public static JObject BoolProp(string description) => new()
        {
            ["type"] = "boolean",
            ["description"] = description ?? string.Empty
        };

        public static JObject EmptyObjectSchema() => new()
        {
            ["type"] = "object",
            ["properties"] = new JObject()
        };

        public static JObject ParseArgs(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)) return new JObject();
            try
            {
                JToken token = JToken.Parse(argumentsJson);
                return token as JObject ?? new JObject();
            }
            catch (Exception)
            {
                return new JObject();
            }
        }

        public static string ArgString(JObject args, string key, string fallback = "")
        {
            JToken token = args?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        public static float ArgFloat(JObject args, string key, float fallback = 0f)
        {
            JToken token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return token.Type == JTokenType.Float || token.Type == JTokenType.Integer
                ? token.Value<float>()
                : float.TryParse(token.ToString(), out float parsed) ? parsed : fallback;
        }

        public static int ArgInt(JObject args, string key, int fallback = 0) =>
            (int)Math.Round(ArgFloat(args, key, fallback));

        public static bool ArgBool(JObject args, string key, bool fallback = false)
        {
            JToken token = args?[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            string text = token.ToString();
            if (bool.TryParse(text, out bool parsed)) return parsed;
            if (text == "1" || text.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (text == "0" || text.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }
}
