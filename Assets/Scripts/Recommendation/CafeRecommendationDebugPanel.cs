using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualCafe.Inventory;

namespace VirtualCafe.Recommendation
{
    [RequireComponent(typeof(CafeRecommendationController))]
    public sealed class CafeRecommendationDebugPanel : MonoBehaviour
    {
        public CafeReceptionController reception;
        private CafeRecommendationController controller;
        private string input = "我想喝一杯燕麦奶拿铁，预算25元。";
        private readonly List<string> conversation = new List<string>();
        private Vector2 scroll;
        private Font font;
        private GUIStyle body, title, button, field, muted;
        private CanvasGroup placeholder;
        private float oldAlpha;
        private bool oldInteraction, oldRaycasts, purchased, showTrace;
        private string purchaseResult = "";

        private void OnEnable()
        {
            controller = GetComponent<CafeRecommendationController>();
            if (!reception) reception = FindObjectOfType<CafeReceptionController>();
            if (reception)
            {
                if (!controller.menuAdapter) controller.menuAdapter = reception.menuAdapter;
                if (!controller.highlighter) controller.highlighter = reception.highlighter;
            }
            placeholder = GetComponent<CanvasGroup>();
            if (!placeholder) placeholder = gameObject.AddComponent<CanvasGroup>();
            oldAlpha = placeholder.alpha;
            oldInteraction = placeholder.interactable;
            oldRaycasts = placeholder.blocksRaycasts;
            placeholder.alpha = 0;
            placeholder.interactable = false;
            placeholder.blocksRaycasts = false;
            conversation.Clear();
            purchased = false;
            purchaseResult = "";
            controller.ReplyChanged.AddListener(OnReply);
            controller.BeginSession();
        }
        private void OnDisable()
        {
            if (controller) { controller.ReplyChanged.RemoveListener(OnReply); controller.CancelSession(); }
            if (placeholder)
            {
                placeholder.alpha = oldAlpha;
                placeholder.interactable = oldInteraction;
                placeholder.blocksRaycasts = oldRaycasts;
            }
        }
        private void OnDestroy() { if (font) Destroy(font); }
        private void OnReply(string text)
        {
            conversation.Add("小暖\n" + text);
            if (conversation.Count > 24) conversation.RemoveRange(0, conversation.Count - 24);
            scroll.y = float.MaxValue;
        }
        private void Send(string message, bool another = false)
        {
            if (string.IsNullOrWhiteSpace(message) || controller.IsBusy) return;
            conversation.Add("你\n" + message);
            scroll.y = float.MaxValue;
            purchased = false;
            purchaseResult = "";
            if (another) controller.RecommendAnother(); else controller.Submit(message);
        }
        private void EnsureStyles()
        {
            if (body != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 18);
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true, richText = false };
            body.normal.textColor = new Color(.24f, .17f, .12f);
            body.padding = new RectOffset(4, 4, 6, 6);
            title = new GUIStyle(body) { fontSize = 25, fontStyle = FontStyle.Bold };
            muted = new GUIStyle(body) { fontSize = 14 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, fixedHeight = 36, richText = false };
            field = new GUIStyle(GUI.skin.textArea) { font = font, fontSize = 18, wordWrap = true, richText = false };
        }
        private void OnGUI()
        {
            if (!controller) return;
            EnsureStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            float scale = Mathf.Clamp(Screen.height / 900f, .65f, 1.3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Mathf.Min(480, Screen.width / scale - 24);
            Rect area = new Rect(Screen.width / scale - width - 12, 12, width, Screen.height / scale - 24);
            GUI.color = new Color(1f, .96f, .89f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 18, area.y + 16, area.width - 36, area.height - 32));
            GUILayout.Label("和小暖选一杯", title);
            GUILayout.Label("聊聊口味，找一杯适合今天的咖啡", muted);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            foreach (string line in conversation) GUILayout.Label(line, body);
            if (controller.IsBusy) GUILayout.Label("正在看看今天的菜单，稍等一下……", body);
            if (!string.IsNullOrEmpty(controller.ErrorText))
            {
                GUILayout.Label(controller.ErrorText, body);
                if (GUILayout.Button("重试上次请求", button)) controller.Retry();
#if UNITY_EDITOR
                if (GUILayout.Button("打开 LLM Settings", button)) UnityEditor.EditorApplication.ExecuteMenuItem("Virtual Cafe/LLM Settings");
#endif
            }
            DrawSelection();
            if (!string.IsNullOrEmpty(purchaseResult)) GUILayout.Label(purchaseResult, body);
            if (showTrace && !string.IsNullOrEmpty(controller.LastTracePath)) GUILayout.Label("调用记录：" + controller.LastTracePath, muted);
            GUILayout.EndScrollView();
            GUI.enabled = !controller.IsBusy;
            input = GUILayout.TextArea(input, 2000, field, GUILayout.Height(68));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("发送偏好", button)) Send(input);
            if (GUILayout.Button("换一杯", button)) Send("保留我的偏好，帮我换一杯吧。", true);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.BeginHorizontal();
            if (reception && GUILayout.Button("返回欢迎页", button)) reception.BeginReception();
            if (reception && GUILayout.Button("直接点单", button)) reception.OpenOrder();
            GUILayout.EndHorizontal();
            showTrace = GUILayout.Toggle(showTrace, "显示调用记录位置", muted);
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.enabled = oldEnabled;
        }
        private void DrawSelection()
        {
            if (controller.CurrentRecommendation == null || !controller.menuAdapter) return;
            var adapter = controller.menuAdapter;
            var selection = adapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            if (item == null) return;
            GUILayout.Label("为你选的 · " + item.name, title);
            GUILayout.Label(selection.CupSizeMl + "ml · " + IngredientName(selection.BeanId) +
                (string.IsNullOrEmpty(selection.MilkId) ? " · 不加奶" : " · " + IngredientName(selection.MilkId)), body);
            GUI.enabled = !controller.IsBusy && !purchased;
            GUILayout.BeginHorizontal();
            foreach (int size in item.cupSizesMl)
                if (GUILayout.Button(size + "ml" + (selection.CupSizeMl == size ? " ✓" : ""), button)) adapter.SelectCupSize(size);
            GUILayout.EndHorizontal();
            DrawOptions(item.beanIds, selection.BeanId, adapter.SelectBean);
            DrawOptions(item.milkIds, selection.MilkId, adapter.SelectMilk);
            GUILayout.Label("当前报价 " + adapter.PriceText + " · " + adapter.StatusText, body);
            GUI.enabled = !controller.IsBusy && !purchased && adapter.CanConfirm;
            if (GUILayout.Button(purchased ? "这杯已经购买" : "确认购买（模拟）", button))
            {
                adapter.ConfirmCurrentSelection();
                purchased = adapter.LastConfirmationSucceeded;
                purchaseResult = purchased ? "购买完成，谢谢惠顾！还想选一杯的话，继续告诉我就好。" : adapter.LastResult;
            }
            GUI.enabled = true;
            GUILayout.Label("价格以当前配置为准，确认时再次检查库存。", muted);
        }
        private void DrawOptions(string[] ids, string selected, Action<string> choose)
        {
            if (ids.Length <= 1) return;
            GUILayout.BeginHorizontal();
            foreach (string id in ids)
                if (GUILayout.Button(IngredientName(id) + (id == selected ? " ✓" : ""), button)) choose(id);
            GUILayout.EndHorizontal();
        }
        private static string IngredientName(string id)
        {
            return string.IsNullOrEmpty(id) ? "默认豆种" : CafeRuntimeServices.Inventory.GetItemDefinition(id).name;
        }
    }
}
