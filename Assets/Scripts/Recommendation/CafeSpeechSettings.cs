using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VirtualCafe.Recommendation
{
    [Serializable]
    public sealed class CafeSpeechSettings
    {
        public string region = "eastasia";
        public string apiKey = "";
        public string aliyunApiKey = "";
        public const string AliyunModel = "qwen3-asr-flash";
        public const string AliyunEndpoint = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        public static string FilePath => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/CafeSpeech.local.json"))
            : Path.Combine(Application.persistentDataPath, "CafeSpeech.local.json");

        public static CafeSpeechSettings Load()
        {
            var settings = File.Exists(FilePath)
                ? JsonUtility.FromJson<CafeSpeechSettings>(File.ReadAllText(FilePath))
                    ?? throw new InvalidDataException("Invalid speech settings.")
                : new CafeSpeechSettings {
                    region = Environment.GetEnvironmentVariable("CAFE_SPEECH_REGION") ?? "eastasia",
                    apiKey = Environment.GetEnvironmentVariable("CAFE_SPEECH_KEY") ?? ""
                };
            if (string.IsNullOrWhiteSpace(settings.aliyunApiKey))
                settings.aliyunApiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
            return settings;
        }
        public bool Validate(out string error)
            => Validate(CafeSpeechConversation.RecognitionProvider.Aliyun, out error);

        public bool Validate(CafeSpeechConversation.RecognitionProvider provider, out string error)
        {
            error = "";
            if (provider == CafeSpeechConversation.RecognitionProvider.Windows) return true;
            if (provider == CafeSpeechConversation.RecognitionProvider.Aliyun)
            {
                if (string.IsNullOrWhiteSpace(aliyunApiKey) || aliyunApiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    error = "请填写华北2（北京）地域的百炼 API Key（单行）。";
                return error.Length == 0;
            }
            if (!Regex.IsMatch(region ?? "", "^[a-z][a-z0-9-]{1,49}$")) error = "Enter the Azure Speech resource region (for example eastasia).";
            else if (string.IsNullOrWhiteSpace(apiKey) || apiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0) error = "Enter a single-line Azure Speech key.";
            return error.Length == 0;
        }
        public void Save()
        {
            region = (region ?? "").Trim(); apiKey = (apiKey ?? "").Trim();
            aliyunApiKey = (aliyunApiKey ?? "").Trim();
            if (!Validate(out string error)) throw new ArgumentException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true), new UTF8Encoding(false));
        }
    }
}
