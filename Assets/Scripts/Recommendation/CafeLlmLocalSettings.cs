using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VirtualCafe.Recommendation
{
    [Serializable]
    public sealed class CafeLlmLocalSettings
    {
        public string endpoint = "https://api.deepseek.com/chat/completions";
        public string model = "deepseek-flash";
        public string apiKey = "";

        // Outside Assets: never imported as an asset or included in a build.
        public static string FilePath => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/CafeLLM.local.json"))
            : Path.Combine(Application.persistentDataPath, "CafeLLM.local.json");

        public static CafeLlmLocalSettings Load()
        {
            if (!File.Exists(FilePath)) return new CafeLlmLocalSettings();
            return JsonUtility.FromJson<CafeLlmLocalSettings>(File.ReadAllText(FilePath, Encoding.UTF8))
                ?? throw new InvalidDataException("Invalid local LLM settings.");
        }

        public bool Validate(out string error)
        {
            error = "";
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                error = "Enter a full HTTPS Chat Completions URL (HTTP is allowed for localhost).";
            else if (string.IsNullOrWhiteSpace(model)) error = "Enter a model ID.";
            else if (string.IsNullOrWhiteSpace(apiKey)) error = "Enter your API key.";
            else if (apiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0) error = "The API key must be a single line.";
            return error.Length == 0;
        }

        public void Save()
        {
            endpoint = (endpoint ?? "").Trim();
            model = (model ?? "").Trim();
            apiKey = (apiKey ?? "").Trim();
            if (!Validate(out string error)) throw new ArgumentException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));
        }
    }
}
