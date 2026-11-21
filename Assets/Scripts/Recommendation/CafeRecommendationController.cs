using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    public sealed class CafeRecommendationController : MonoBehaviour
    {
        [Serializable] public sealed class TextEvent : UnityEvent<string> { }
        [Serializable] public sealed class BoolEvent : UnityEvent<bool> { }
        [Serializable] public sealed class SelectionEvent : UnityEvent<MenuSelection> { }
        [Tooltip("本机配置优先；未保存配置时使用环境变量及此默认地址")]
        public string endpoint = "https://api.deepseek.com/chat/completions";
        public string model = "deepseek-flash";
        public string apiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
        [Range(5, 120)] public int timeoutSeconds = 45;
        public CafeMenuSceneAdapter menuAdapter;
        public CafeProductHighlighter highlighter;
        public TextEvent ReplyChanged = new TextEvent();
        public TextEvent ErrorChanged = new TextEvent();
        public BoolEvent BusyChanged = new BoolEvent();
        public SelectionEvent RecommendationApplied = new SelectionEvent();
        public bool IsBusy { get; private set; }
        public string ReplyText { get; private set; } = "你好呀！今天想喝点什么？口味、奶种或预算都可以告诉我。";
        public MenuSelection CurrentRecommendation { get; private set; }
        public string ErrorText { get; private set; } = "";
        public string LastTracePath { get; private set; }
        public CafeOrderReceipt LastConfirmedOrder { get; private set; }
        public void NotifyConfirmedOrder(CafeOrderReceipt receipt)
        {
            if (receipt == null || string.IsNullOrEmpty(receipt.pickupNumber)) return;
            LastConfirmedOrder = receipt;
            ReplyText = receipt.ClerkMessage;
            ReplyChanged.Invoke(ReplyText);
        }
        private readonly List<ChatMessage> history = new List<ChatMessage>();
        private UnityWebRequest activeRequest;
        private Coroutine routine;
        private bool sessionOpen;
        private int generation;
        private string lastInput;
        private string lastProduct;
        private string firstPreference;

        public void BeginSession()
        {
            CancelSession();
            sessionOpen = true;
            history.Clear();
            lastInput = null;
            lastProduct = null;
            firstPreference = null;
            CurrentRecommendation = null;
            ReplyText = "你好呀！今天想喝点什么？口味、奶种或预算都可以告诉我。";
            ReplyChanged.Invoke(ReplyText);
            SetError("");
        }
        public void CancelSession()
        {
            sessionOpen = false;
            CurrentRecommendation = null;
            generation++;
            if (activeRequest != null) { activeRequest.Abort(); activeRequest.Dispose(); activeRequest = null; }
            if (routine != null) { StopCoroutine(routine); routine = null; }
            SetBusy(false);
        }
        private void OnDisable() { CancelSession(); }
        public void Submit(string input)
        {
            if (!sessionOpen || !isActiveAndEnabled || IsBusy) return;
            if (string.IsNullOrWhiteSpace(input)) { SetError("请先输入偏好。"); return; }
            if (input.Length > 2000) { SetError("输入最多2000字。"); return; }
            lastInput = input.Trim();
            if (string.IsNullOrEmpty(firstPreference)) firstPreference = lastInput;
            CurrentRecommendation = null;
            if (highlighter) highlighter.ClearHighlight();
            SetError("");
            SetBusy(true);
            routine = StartCoroutine(Request(lastInput, generation));
        }
        public void Retry() { if (!string.IsNullOrEmpty(lastInput)) Submit(lastInput); }
        public void RecommendAnother()
        {
            Submit("保留此前偏好，换一个不同商品，上次商品ID：" + (lastProduct ?? "无"));
        }
        private IEnumerator Request(string input, int version)
        {
            string url = Environment.GetEnvironmentVariable("CAFE_LLM_ENDPOINT");
            if (string.IsNullOrWhiteSpace(url)) url = endpoint;
            string modelName = Environment.GetEnvironmentVariable("CAFE_LLM_MODEL");
            if (string.IsNullOrWhiteSpace(modelName)) modelName = model;
            string key = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            try
            {
                // A saved local file takes precedence over environment variables and scene defaults.
                if (File.Exists(CafeLlmLocalSettings.FilePath))
                {
                    var local = CafeLlmLocalSettings.Load();
                    url = local.endpoint;
                    modelName = local.model;
                    key = local.apiKey;
                }
            }
            catch (Exception)
            {
                SetError("本机配置读取失败，请打开 Virtual Cafe > LLM Settings 重新保存。");
                SetBusy(false);
                yield break;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                string.IsNullOrWhiteSpace(modelName) || string.IsNullOrWhiteSpace(key))
            {
                SetError("请打开 Virtual Cafe > LLM Settings 填写接口、模型及API Key并保存。");
                SetBusy(false);
                yield break;
            }
            string body;
            try
            {
                var messages = new List<ChatMessage> { new ChatMessage("system", CafeRecommendationProtocol.SystemPrompt) };
                messages.AddRange(history);
                messages.Add(new ChatMessage("user", JsonUtility.ToJson(new RequestContext {
                    snapshot = JsonUtility.FromJson<MenuRecommendationSnapshot>(CafeRuntimeServices.Menu.GetRecommendationSnapshotJson()),
                    firstPreference = firstPreference, currentSelection = menuAdapter ? menuAdapter.CurrentSelection : null,
                    previousProductId = lastProduct, userInput = input, lastConfirmedOrder = LastConfirmedOrder
                })));
                body = JsonUtility.ToJson(new ChatRequest { model = modelName, messages = messages.ToArray() });
                activeRequest = new UnityWebRequest(url, "POST");
                activeRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                activeRequest.downloadHandler = new DownloadHandlerBuffer();
                activeRequest.timeout = timeoutSeconds;
                activeRequest.SetRequestHeader("Content-Type", "application/json");
                activeRequest.SetRequestHeader("Authorization", "Bearer " + key);
            }
            catch (Exception e)
            {
                if (activeRequest != null) { activeRequest.Dispose(); activeRequest = null; }
                SetError("请求初始化失败：" + e.GetType().Name);
                SetBusy(false);
                yield break;
            }
            var request = activeRequest;
            float started = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            if (version != generation || !sessionOpen) yield break;
            string raw = request.downloadHandler.text;
            string content = "";
            string validation = "";
            RecommendationReply parsed = null;
            bool valid = false;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                    validation = RequestFailure(request.responseCode);
                else
                {
                    var response = JsonUtility.FromJson<ChatResponse>(raw);
                    if (response == null || response.choices == null || response.choices.Length == 0 || response.choices[0].message == null)
                        validation = "模型接口返回缺少回复内容。";
                    else if (response.choices[0].finish_reason == "length")
                        validation = "这次回复没有完整生成，麻烦再试一次吧。";
                    else
                    {
                        content = response.choices[0].message.content;
                        valid = CafeRecommendationProtocol.TryParse(content, CafeRuntimeServices.Menu, out parsed, out validation);
                        if (valid && parsed.status == "recommendation")
                        {
                            if (!menuAdapter) { valid = false; validation = "未绑定共用菜单适配器。"; }
                            else Apply(parsed.selection);
                        }
                    }
                }
            }
            catch (Exception e) { valid = false; validation = "回复处理失败：" + e.GetType().Name; }
            if (version != generation || !sessionOpen) yield break;
            WriteTrace(body, raw, content, valid, validation, parsed, Time.realtimeSinceStartup - started, key);
            request.Dispose();
            activeRequest = null;
            routine = null;
            SetBusy(false);
            if (!valid) { SetError(validation); yield break; }
            history.Add(new ChatMessage("user", input));
            history.Add(new ChatMessage("assistant", content));
            while (history.Count > 12) history.RemoveRange(0, 2);
            ReplyText = parsed.reply +
                (string.IsNullOrWhiteSpace(parsed.followUp) || parsed.reply.Contains(parsed.followUp) ? "" : "\n" + parsed.followUp);
            ReplyChanged.Invoke(ReplyText);
        }
        private void Apply(MenuSelection selection)
        {
            // SelectDrink resets latte options, so apply explicit options afterwards.
            menuAdapter.SelectDrink(selection.MenuItemId);
            menuAdapter.SelectBean(selection.BeanId);
            menuAdapter.SelectMilk(selection.MilkId);
            menuAdapter.SelectCupSize(selection.CupSizeMl);
            menuAdapter.SetQuantity(1);
            lastProduct = selection.MenuItemId;
            CurrentRecommendation = menuAdapter.CurrentSelection;
            if (highlighter) highlighter.HighlightProduct(selection.MenuItemId);
            RecommendationApplied.Invoke(menuAdapter.CurrentSelection);
        }
        [Serializable] private sealed class Trace
        {
            public string utc, request, rawResponse, modelOutput, validation, parsedResult;
            public bool valid;
            public float elapsedSeconds;
        }
        [Serializable] private sealed class RequestContext
        {
            public MenuRecommendationSnapshot snapshot;
            public string firstPreference, previousProductId, userInput;
            public MenuSelection currentSelection;
            public CafeOrderReceipt lastConfirmedOrder;
        }
        private static string RequestFailure(long code)
        {
            if (code == 401) return "暂时连不上店员服务，请在 LLM Settings 检查 API Key 是否有效（401）。";
            if (code == 402) return "模型账户余额不足，请检查 DeepSeek 账户后再试（402）。";
            if (code == 429) return "店员服务现在有点忙，稍等片刻再试试吧（429）。";
            if (code == 0) return "这次连接没有完成，可能是网络中断或等待超时。你可以重试，也可以直接点单。";
            return "店员服务暂时没有回复（HTTP " + code + "），可以稍后重试，或先直接点单。";
        }
        private void WriteTrace(string body, string raw, string content, bool valid, string validation, RecommendationReply parsed, float elapsed, string key)
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "CafeLLM");
                Directory.CreateDirectory(folder);
                LastTracePath = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                string json = JsonUtility.ToJson(new Trace { utc = DateTime.UtcNow.ToString("o"), request = body,
                    rawResponse = raw, modelOutput = content, valid = valid, validation = validation,
                    parsedResult = parsed == null ? null : JsonUtility.ToJson(parsed), elapsedSeconds = elapsed }, true);
                // Request headers are never logged. Also redact an accidental echoed credential.
                File.WriteAllText(LastTracePath, json.Replace(key, "[REDACTED]"), Encoding.UTF8);
            }
            catch (Exception e) { Debug.LogWarning("LLM记录保存失败：" + e.GetType().Name); }
        }
        private void SetError(string value) { ErrorText = value; ErrorChanged.Invoke(value); }
        private void SetBusy(bool value) { IsBusy = value; BusyChanged.Invoke(value); }
    }
}
