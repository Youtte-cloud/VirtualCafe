using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    [Serializable] public sealed class RecommendationReply
    {
        public string status;
        public string reply;
        public string reason;
        public string followUp;
        public MenuSelection selection;
    }
    [Serializable] public sealed class ChatMessage
    {
        public string role;
        public string content;
        public ChatMessage(string role, string content) { this.role = role; this.content = content; }
    }
    [Serializable] public sealed class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature = 0.3f;
        public int max_tokens = 1800;
        public JsonResponseFormat response_format = new JsonResponseFormat();
        public ThinkingMode thinking = new ThinkingMode();
    }
    [Serializable] public sealed class JsonResponseFormat { public string type = "json_object"; }
    [Serializable] public sealed class ThinkingMode { public string type = "disabled"; }
    [Serializable] public sealed class ChatChoice { public ChatMessage message; public string finish_reason; }
    [Serializable] public sealed class ChatResponse { public ChatChoice[] choices; }

    public static class CafeRecommendationProtocol
    {
        public static string SystemPrompt
        {
            get
            {
                var asset = Resources.Load<TextAsset>("Cafe/recommendation-system-prompt");
                if (asset == null || string.IsNullOrWhiteSpace(asset.text))
                    throw new InvalidOperationException("Missing Cafe/recommendation-system-prompt.txt");
                return asset.text;
            }
        }

        public static bool TryParse(string raw, CafeMenuService menu, out RecommendationReply reply, out string error)
        {
            reply = null;
            error = "模型返回格式无效，请重试或直接点单。";
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 32000) return false;
            string json = raw.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int start = json.IndexOf('\n');
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (start < 0 || end <= start) return false;
                json = json.Substring(start + 1, end - start - 1).Trim();
            }
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal)) return false;
            try { reply = JsonUtility.FromJson<RecommendationReply>(json); }
            catch (ArgumentException) { return false; }
            if (reply == null || string.IsNullOrWhiteSpace(reply.reply)) return false;
            if (reply.status == "follow_up" || reply.status == "no_match")
            {
                if (!HasNullSelection(json)) return false;
                reply.selection = null; // JsonUtility may instantiate nested objects even for JSON null.
                error = "";
                return true;
            }
            if (reply.status != "recommendation" || reply.selection == null || reply.selection.Servings != 1) return false;
            if (!menu.TryGetPrice(reply.selection, out var quote, out error)) return false;
            if (!menu.IsAvailable(reply.selection, out error)) return false;
            error = "";
            return true;
        }

        private static bool HasNullSelection(string json)
        {
            int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{' || json[i] == '[') depth++;
                else if (json[i] == '}' || json[i] == ']') depth--;
                else if (json[i] == '"')
                {
                    int start = ++i;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') i++;
                        i++;
                    }
                    if (depth != 1 || json.Substring(start, i - start) != "selection") continue;
                    int pos = i + 1;
                    while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
                    if (pos >= json.Length || json[pos++] != ':') continue;
                    while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
                    return pos + 4 <= json.Length && json.Substring(pos, 4) == "null";
                }
            }
            return false;
        }
    }
}
