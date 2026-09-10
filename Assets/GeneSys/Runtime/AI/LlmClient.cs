using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GeneSys.AI
{
    public sealed class LlmClient
    {
        public const int DefaultTimeoutSeconds = 120;

        public IEnumerator ListModels(string baseUrl, Action<bool, string, List<LlmModelInfo>> completed)
        {
            string url = Combine(baseUrl, "/v1/models");
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = DefaultTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                completed?.Invoke(false, request.error ?? "Model list request failed.", null);
                yield break;
            }

            if (!TryParseModels(request.downloadHandler?.text, out List<LlmModelInfo> models, out string error))
            {
                completed?.Invoke(false, error, null);
                yield break;
            }

            completed?.Invoke(true, null, models);
        }

        public IEnumerator Chat(string baseUrl, string model, IReadOnlyList<LlmMessage> messages,
            JArray tools, Action<LlmChatResult> completed)
        {
            string url = Combine(baseUrl, "/v1/chat/completions");
            string body = BuildChatRequest(model, messages, tools);
            byte[] payload = Encoding.UTF8.GetBytes(body);
            using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = DefaultTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                completed?.Invoke(LlmChatResult.Failed(request.error ?? "Chat request failed."));
                yield break;
            }

            completed?.Invoke(ParseChatResponse(request.downloadHandler?.text));
        }

        public static string BuildChatRequest(string model, IReadOnlyList<LlmMessage> messages, JArray tools)
        {
            var body = new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(model) ? "local" : model,
                ["messages"] = messages == null
                    ? new JArray()
                    : JArray.FromObject(messages, JsonSerializer.Create(SerializerSettings())),
                ["temperature"] = 0.4
            };
            if (tools != null && tools.Count > 0)
            {
                body["tools"] = tools;
                body["tool_choice"] = "auto";
            }

            return body.ToString(Formatting.None);
        }

        public static bool TryParseModels(string json, out List<LlmModelInfo> models, out string error)
        {
            models = new List<LlmModelInfo>();
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty model list response.";
                return false;
            }

            try
            {
                JObject root = JObject.Parse(json);
                JToken data = root["data"];
                if (data is JArray array)
                {
                    foreach (JToken item in array)
                    {
                        string id = item?["id"]?.ToString();
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        models.Add(new LlmModelInfo
                        {
                            Id = id,
                            OwnedBy = item["owned_by"]?.ToString()
                        });
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static LlmChatResult ParseChatResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return LlmChatResult.Failed("Empty chat response.");

            try
            {
                JObject root = JObject.Parse(json);
                JToken errorToken = root["error"];
                if (errorToken != null && errorToken.Type != JTokenType.Null)
                {
                    string errorMessage = errorToken["message"]?.ToString() ?? errorToken.ToString();
                    return LlmChatResult.Failed(errorMessage);
                }

                JToken choice = root["choices"] is JArray choices && choices.Count > 0 ? choices[0] : null;
                JToken message = choice?["message"];
                var result = new LlmChatResult
                {
                    Ok = true,
                    Content = message?["content"]?.ToString() ?? string.Empty,
                    FinishReason = choice?["finish_reason"]?.ToString()
                };

                if (message?["tool_calls"] is JArray toolCalls)
                {
                    foreach (JToken call in toolCalls)
                    {
                        var parsed = call.ToObject<LlmToolCall>();
                        if (parsed == null || parsed.Function == null || string.IsNullOrWhiteSpace(parsed.Function.Name))
                            continue;
                        parsed.Type = string.IsNullOrWhiteSpace(parsed.Type) ? "function" : parsed.Type;
                        parsed.Function.Arguments ??= "{}";
                        result.ToolCalls.Add(parsed);
                    }
                }

                if (!result.HasToolCalls)
                    TryParseFallbackToolCalls(result.Content, result.ToolCalls);

                return result;
            }
            catch (Exception exception)
            {
                return LlmChatResult.Failed(exception.Message);
            }
        }

        public static void TryParseFallbackToolCalls(string content, List<LlmToolCall> destination)
        {
            if (string.IsNullOrWhiteSpace(content) || destination == null) return;

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] != '{') continue;
                int end = FindMatchingBrace(content, i);
                if (end < 0) continue;
                TryAddFallbackObject(content.Substring(i, end - i + 1), destination);
                i = end;
            }

            foreach (Match match in Regex.Matches(content,
                         "<tool_call>\\s*([\\s\\S]*?)\\s*</tool_call>", RegexOptions.IgnoreCase))
                TryAddFallbackObject(match.Groups[1].Value.Trim(), destination);
        }

        private static int FindMatchingBrace(string text, int start)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                    inString = !inString;
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static void TryAddFallbackObject(string json, List<LlmToolCall> destination)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                string name = obj["name"]?.ToString() ?? obj["tool"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) return;
                JToken args = obj["arguments"] ?? obj["parameters"] ?? obj["args"];
                string arguments = args == null
                    ? "{}"
                    : args.Type == JTokenType.String ? args.ToString() : args.ToString(Formatting.None);
                destination.Add(new LlmToolCall
                {
                    Id = $"fallback_{destination.Count + 1}",
                    Type = "function",
                    Function = new LlmToolFunction { Name = name, Arguments = arguments }
                });
            }
            catch (JsonException)
            {
            }
        }

        private static JsonSerializerSettings SerializerSettings() => new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private static string Combine(string baseUrl, string path)
        {
            string trimmed = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:1234" : baseUrl.Trim().TrimEnd('/');
            return trimmed + path;
        }
    }
}
