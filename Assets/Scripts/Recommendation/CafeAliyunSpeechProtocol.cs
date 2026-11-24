using System;
using System.Text;
using UnityEngine;

namespace VirtualCafe.Recommendation
{
    // DashScope synchronous multimodal API; audio stays in memory as a WAV data URI.
    public static class CafeAliyunSpeechProtocol
    {
        [Serializable] private sealed class AudioContent { public string audio; }
        [Serializable] private sealed class RequestMessage { public string role = "user"; public AudioContent[] content; }
        [Serializable] private sealed class RequestInput { public RequestMessage[] messages; }
        [Serializable] private sealed class AsrOptions { public string language; public bool enable_itn = false; }
        [Serializable] private sealed class Parameters { public AsrOptions asr_options; }
        [Serializable] private sealed class Request
        {
            public string model = CafeSpeechSettings.AliyunModel;
            public RequestInput input;
            public Parameters parameters;
        }
        [Serializable] private sealed class TextContent { public string text; }
        [Serializable] private sealed class ResponseMessage { public TextContent[] content; }
        [Serializable] private sealed class Choice { public ResponseMessage message; }
        [Serializable] private sealed class Output { public Choice[] choices; }
        [Serializable] private sealed class Response { public Output output; public string code; }

        public static string BuildRequest(byte[] wav, string language)
        {
            if (wav == null || wav.Length == 0) throw new ArgumentException("Audio is empty.", nameof(wav));
            string asrLanguage;
            switch (language)
            {
                case "zh-CN": asrLanguage = "zh"; break;
                case "en-US": asrLanguage = "en"; break;
                case "ja-JP": asrLanguage = "ja"; break;
                default: throw new ArgumentException("Unsupported speech language.", nameof(language));
            }
            return JsonUtility.ToJson(new Request {
                input = new RequestInput { messages = new[] {
                    new RequestMessage { content = new[] {
                        new AudioContent { audio = "data:audio/wav;base64," + Convert.ToBase64String(wav) }
                    } }
                } },
                parameters = new Parameters { asr_options = new AsrOptions { language = asrLanguage } }
            });
        }

        public static string ReadTranscript(string json)
        {
            var result = Parse(json);
            if (!string.IsNullOrEmpty(result?.code)) return "";
            var choices = result?.output?.choices;
            if (choices == null || choices.Length == 0 || choices[0]?.message?.content == null) return "";
            var text = new StringBuilder();
            foreach (var part in choices[0].message.content)
                if (!string.IsNullOrEmpty(part?.text)) text.Append(part.text);
            return text.ToString().Trim();
        }

        public static string ReadErrorCode(string json) => Parse(json)?.code ?? "";
        private static Response Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<Response>(json); }
            catch (ArgumentException) { return null; }
        }
    }
}
