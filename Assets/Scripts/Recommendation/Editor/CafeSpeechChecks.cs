using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VirtualCafe.Recommendation.Editor
{
    public static class CafeSpeechChecks
    {
        [MenuItem("Virtual Cafe/Speech/Run Audio Checks")]
        public static void Run()
        {
            RunAudioChecks();
            Debug.Log("Speech checks passed: audio encoding, Aliyun request/response, language mapping and credential validation.");
        }
        public static void RunAudioChecks()
        {
            var stereo = new float[480 * 2];
            for (int i = 0; i < 480; i++) { stereo[i * 2] = 1; stereo[i * 2 + 1] = -1; }
            byte[] wav = CafeSpeechAudio.EncodeWav(stereo, 480, 2, 48000);
            using (var reader = new BinaryReader(new MemoryStream(wav)))
            {
                Check(new string(reader.ReadChars(4)) == "RIFF", "RIFF header");
                Check(reader.ReadInt32() == wav.Length - 8, "RIFF length");
                Check(new string(reader.ReadChars(8)) == "WAVEfmt ", "WAV format");
                Check(reader.ReadInt32() == 16 && reader.ReadInt16() == 1 && reader.ReadInt16() == 1, "Mono PCM format");
                Check(reader.ReadInt32() == 16000 && reader.ReadInt32() == 32000, "Sample and byte rate");
                Check(reader.ReadInt16() == 2 && reader.ReadInt16() == 16, "PCM16 alignment");
                Check(new string(reader.ReadChars(4)) == "data" && reader.ReadInt32() == 320, "48kHz to 16kHz resampling");
                while (reader.BaseStream.Position < reader.BaseStream.Length) Check(reader.ReadInt16() == 0, "Stereo downmix");
            }
            var clipped = CafeSpeechAudio.EncodeWav(new[] { 2f, -2f }, 2, 1, 16000);
            Check(BitConverter.ToInt16(clipped, 44) == 32767 && BitConverter.ToInt16(clipped, 46) == -32767, "Clipping");
            var decoded = CafeSpeechAudio.DecodePcm16(new byte[] { 0, 128, 0, 0, 255, 127 });
            Check(decoded[0] == -1 && decoded[1] == 0 && decoded[2] > .999f, "Little-endian signed PCM");
            bool rejected = false;
            try { CafeSpeechAudio.DecodePcm16(new byte[] { 1 }); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Truncated PCM rejected");
            rejected = false;
            try { CafeSpeechAudio.EncodeWav(new float[0], 0, 1, 16000); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Empty recording rejected");
            Check(!new CafeSpeechSettings { region = "bad/region", apiKey = "test" }.Validate(CafeSpeechConversation.RecognitionProvider.Azure, out _), "Invalid Azure region rejected");
            Check(!new CafeSpeechSettings().Validate(out _), "Missing Aliyun key rejected");
            Check(!new CafeSpeechSettings { aliyunApiKey = "test\nheader" }.Validate(out _), "Multiline key rejected");
            Check(new CafeSpeechSettings { aliyunApiKey = "test", region = "" }.Validate(out _), "Aliyun does not require Azure region");
            foreach (string language in CafeSpeechConversation.Languages)
            {
                string request = CafeAliyunSpeechProtocol.BuildRequest(wav, language);
                Check(request.Contains("\"model\":\"qwen3-asr-flash\""), "Qwen model selected");
                Check(request.Contains("data:audio/wav;base64," + Convert.ToBase64String(wav)), "WAV included as data URI");
                Check(request.Contains("\"language\":\"" + language.Substring(0, 2) + "\""), "ASR language mapping");
                Check(!request.Contains("apiKey"), "No credential in request body");
            }
            Check(CafeAliyunSpeechProtocol.ReadTranscript("{\"output\":{\"choices\":[{\"message\":{\"content\":[{\"text\":\"我要一杯\"},{\"text\":\"拿铁。\"}]}}]}}") == "我要一杯拿铁。", "DashScope transcript parts decoded");
            Check(CafeAliyunSpeechProtocol.ReadTranscript("{\"output\":{\"choices\":[]}}") == "", "Empty response handled");
            Check(CafeAliyunSpeechProtocol.ReadTranscript("not json") == "", "Malformed response handled");
            Check(CafeAliyunSpeechProtocol.ReadErrorCode("{\"code\":\"InvalidApiKey\"}") == "InvalidApiKey", "Service error code decoded");
        }
        private static void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException("Speech check failed: " + name);
        }
    }
}
