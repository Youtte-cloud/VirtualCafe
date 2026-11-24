using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine.Windows.Speech;
#endif

namespace VirtualCafe.Recommendation
{
    public sealed class CafeSpeechConversation : MonoBehaviour
    {
        public enum RecognitionProvider { Windows, Azure, Aliyun }
        public RecognitionProvider provider = RecognitionProvider.Aliyun;
        [Serializable] public sealed class TextEvent : UnityEvent<string> { }
        public TextEvent Recognized = new TextEvent();
        public TextEvent PreviewChanged = new TextEvent();
        public static readonly string[] Languages = { "zh-CN", "en-US", "ja-JP" };
        public static readonly string[] LanguageNames = { "中文", "English", "日本語" };
        public int LanguageIndex { get; private set; }
        public string Language => Languages[LanguageIndex];
        public bool Active { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsBusy { get; private set; }
        public string Status { get; private set; } = "";
        public bool NeedsConfiguration { get; private set; }
        private AudioClip microphoneClip;
        private string device;
        private bool stopRecording;
        private UnityWebRequest activeRequest;
        private CafeSpeechSettings settings;
        private readonly StringBuilder windowsText = new StringBuilder();
        private bool windowsFinished;
        private float windowsStarted, finishDeadline;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private DictationRecognizer dictation;
#endif

        public void Activate(int language)
        {
            Cancel(); Active = true; LanguageIndex = Mathf.Clamp(language, 0, Languages.Length - 1);
            NeedsConfiguration = false;
            Status = provider == RecognitionProvider.Windows
                ? "Windows 语音输入，无需 Key。识别语言跟随 Windows 语音设置；所选语言用于店员回复。"
                : provider == RecognitionProvider.Aliyun ? "百炼 Qwen-ASR：点击麦克风录音，再次点击发送。" : "Ready / 点击麦克风开始说话";
        }
        public void Cancel()
        {
            Active = false;
            ReleaseWindows(); windowsText.Clear(); windowsFinished = false;
            if (activeRequest != null) { activeRequest.Abort(); activeRequest.Dispose(); activeRequest = null; }
            StopAllCoroutines();
            if (microphoneClip) { Microphone.End(device); Destroy(microphoneClip); microphoneClip = null; }
            IsRecording = IsBusy = stopRecording = false;
            Status = "";
        }
        private void OnDisable() { Cancel(); }
        private bool LoadSettings()
        {
            try
            {
                settings = CafeSpeechSettings.Load();
                NeedsConfiguration = !settings.Validate(provider, out _);
                if (NeedsConfiguration)
                {
                    Status = provider == RecognitionProvider.Aliyun ? "语音服务未配置：请点击“语音设置”填写北京地域的百炼 API Key。仍可输入文字。"
                        : LanguageIndex == 0 ? "语音服务未配置：请点击“语音设置”填写 Region 和 Key。仍可输入文字。"
                        : LanguageIndex == 2 ? "音声サービス未設定です。Speech Settings で Region と Key を入力してください。文字入力は使えます。"
                        : "Speech is not configured. Open Speech Settings and enter Region and Key. You can still type.";
                    return false;
                }
                return true;
            }
            catch (Exception) { NeedsConfiguration = true; Status = "Cannot load speech settings. Open Speech Settings. You can still type."; return false; }
        }
        public void ToggleRecording()
        {
            if (!Active) return;
            if (provider == RecognitionProvider.Windows)
            {
                if (IsRecording) StopWindows();
                else if (!IsBusy) StartWindows();
                return;
            }
            if (IsRecording) { stopRecording = true; return; }
            if (IsBusy || !LoadSettings()) return;
            StartCoroutine(Record());
        }
        private void StartWindows()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            ReleaseWindows(); windowsText.Clear(); windowsFinished = false; finishDeadline = 0;
            NeedsConfiguration = false;
            try
            {
                dictation = new DictationRecognizer();
                dictation.DictationHypothesis += OnHypothesis;
                dictation.DictationResult += OnWindowsResult;
                dictation.DictationComplete += OnWindowsComplete;
                dictation.DictationError += OnWindowsError;
                dictation.InitialSilenceTimeoutSeconds = 10;
                dictation.AutoSilenceTimeoutSeconds = 5;
                IsRecording = IsBusy = true; windowsStarted = Time.realtimeSinceStartup;
                Status = "正在听，请说话；再次点击结束并发送。";
                PreviewChanged.Invoke("正在说话…");
                dictation.Start();
            }
            catch (Exception exception)
            {
                ReleaseWindows(); IsRecording = IsBusy = false; NeedsConfiguration = true;
                Debug.LogException(exception, this);
                Status = "无法启动 Windows 语音 (0x" + exception.HResult.ToString("X8")
                    + ")：" + exception.Message + "。详细原因见 Console；仍可输入文字。";
                PreviewChanged.Invoke(Status);
            }
#else
            NeedsConfiguration = true;
            Status = "Windows 语音仅支持 Windows。可改用 Azure 识别或输入文字。";
            PreviewChanged.Invoke(Status);
#endif
        }
        private void StopWindows()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IsRecording = false; finishDeadline = Time.realtimeSinceStartup + 3;
            Status = "正在完成识别…";
            try { if (dictation != null && dictation.Status == SpeechSystemStatus.Running) dictation.Stop(); else windowsFinished = true; }
            catch (Exception) { windowsFinished = true; }
#endif
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private void OnHypothesis(string text) { if (Active) PreviewChanged.Invoke(windowsText + text); }
        private void OnWindowsResult(string text, ConfidenceLevel confidence)
        {
            if (!Active) return;
            windowsText.Append(text).Append(" ");
            PreviewChanged.Invoke(windowsText.ToString().Trim());
        }
        private void OnWindowsComplete(DictationCompletionCause cause) { windowsFinished = true; }
        private void OnWindowsError(string error, int code)
        {
            NeedsConfiguration = true;
            Debug.LogWarning("Windows dictation failed (0x" + code.ToString("X8") + "): " + error, this);
            Status = "Windows 语音不可用 (0x" + code.ToString("X8") + ")：" + error + "。仍可输入文字。";
            PreviewChanged.Invoke(Status); windowsText.Clear(); windowsFinished = true;
        }
#endif
        private void ReleaseWindows()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (dictation == null) return;
            dictation.DictationHypothesis -= OnHypothesis;
            dictation.DictationResult -= OnWindowsResult;
            dictation.DictationComplete -= OnWindowsComplete;
            dictation.DictationError -= OnWindowsError;
            try { if (dictation.Status == SpeechSystemStatus.Running) dictation.Stop(); }
            catch (Exception) { }
            finally { dictation.Dispose(); dictation = null; }
#endif
        }
        private void Update()
        {
            if (!Active || provider != RecognitionProvider.Windows || !IsBusy) return;
            if (IsRecording && Time.realtimeSinceStartup - windowsStarted >= 30) StopWindows();
            if (!windowsFinished && !(finishDeadline > 0 && Time.realtimeSinceStartup >= finishDeadline)) return;
            ReleaseWindows(); IsRecording = IsBusy = false; windowsFinished = false; finishDeadline = 0;
            string text = windowsText.ToString().Trim(); windowsText.Clear();
            if (text.Length > 0) { Status = "You: " + text; Recognized.Invoke(text); }
            else if (!NeedsConfiguration)
            {
                Status = "没有识别到语音，请重试或输入文字。"; PreviewChanged.Invoke(Status);
            }
        }
        private IEnumerator Record()
        {
            IsBusy = true;
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            { Status = "Microphone permission denied. You can still type."; IsBusy = false; yield break; }
            if (Microphone.devices.Length == 0)
            { Status = "No microphone found. You can still type."; IsBusy = false; yield break; }
            device = Microphone.devices[0];
            try { microphoneClip = Microphone.Start(device, false, 31, 16000); }
            catch (Exception) { Status = "Cannot start microphone. You can still type."; }
            if (!microphoneClip) { IsBusy = false; yield break; }
            IsRecording = true; stopRecording = false;
            Status = "Listening / 录音中 — click again to send (max 30s)";
            PreviewChanged.Invoke("正在说话…");
            float started = Time.realtimeSinceStartup;
            while (!stopRecording && Time.realtimeSinceStartup - started < 30) yield return null;
            int frames = Microphone.GetPosition(device);
            Microphone.End(device); IsRecording = false;
            byte[] wav = null;
            try
            {
                if (frames < microphoneClip.frequency / 4) throw new InvalidOperationException();
                var samples = new float[frames * microphoneClip.channels];
                if (!microphoneClip.GetData(samples, 0)) throw new InvalidOperationException();
                wav = CafeSpeechAudio.EncodeWav(samples, frames, microphoneClip.channels, microphoneClip.frequency);
            }
            catch (Exception) { Status = "Recording was empty or too short. Please try again."; }
            Destroy(microphoneClip); microphoneClip = null;
            if (wav != null) yield return Transcribe(wav);
            IsBusy = false;
        }
        [Serializable] private sealed class RecognitionResult { public string RecognitionStatus, DisplayText; }
        private IEnumerator Transcribe(byte[] wav)
        {
            if (provider == RecognitionProvider.Aliyun)
            {
                yield return TranscribeAliyun(wav);
                yield break;
            }
            Status = "Recognizing speech / 正在识别…";
            PreviewChanged.Invoke("正在识别刚才的话…");
            using (var request = new UnityWebRequest("https://" + settings.region +
                ".stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language=" + Language, "POST"))
            {
                activeRequest = request; request.timeout = 45;
                request.uploadHandler = new UploadHandlerRaw(wav); request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Ocp-Apim-Subscription-Key", settings.apiKey);
                request.SetRequestHeader("Content-Type", "audio/wav; codecs=audio/pcm; samplerate=16000");
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success) { Status = "Speech recognition failed (HTTP " + request.responseCode + "). Try again or type."; yield break; }
                RecognitionResult result = null;
                try { result = JsonUtility.FromJson<RecognitionResult>(request.downloadHandler.text); }
                catch (Exception) { }
                if (result == null || result.RecognitionStatus != "Success" || string.IsNullOrWhiteSpace(result.DisplayText))
                { Status = "No speech recognized. Please try again or type."; yield break; }
                Status = "You: " + result.DisplayText;
                // Release the input lock before handing the transcript to the shared text submission path.
                IsBusy = false;
                Recognized.Invoke(result.DisplayText);
            }
        }

        private IEnumerator TranscribeAliyun(byte[] wav)
        {
            Status = "识别中";
            PreviewChanged.Invoke(Status);
            using (var request = new UnityWebRequest(CafeSpeechSettings.AliyunEndpoint, "POST"))
            {
                activeRequest = request;
                request.timeout = 60;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(CafeAliyunSpeechProtocol.BuildRequest(wav, Language)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + settings.aliyunApiKey.Trim());
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string reason = request.responseCode == 401 ? "API Key 无效或地域不匹配，请使用北京地域密钥。"
                        : request.responseCode == 403 ? "没有模型调用权限，请检查百炼业务空间和模型权限。"
                        : request.responseCode == 429 ? "请求受限或额度不足，请检查控制台额度并稍后重试。"
                        : request.responseCode == 0 ? "网络连接失败或超时，请检查网络后重试。"
                        : "请检查百炼服务状态和账户额度后重试。";
                    Status = "百炼语音识别失败 (HTTP " + request.responseCode + ")：" + reason;
                    string code = CafeAliyunSpeechProtocol.ReadErrorCode(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(code)) Status += " 服务错误码：" + code;
                    PreviewChanged.Invoke(Status);
                    yield break;
                }
                string text = CafeAliyunSpeechProtocol.ReadTranscript(request.downloadHandler.text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Status = "未识别到文字或服务返回无效，请重试或输入文字。";
                    PreviewChanged.Invoke(Status);
                    yield break;
                }
                Status = "You: " + text;
                IsBusy = false;
                Recognized.Invoke(text);
            }
        }
    }
}
