using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VirtualCafe.Inventory;
using VirtualCafe.Player;

namespace VirtualCafe.Recommendation
{
    // Camera-facing interaction panel, displayed only after approaching the counter.
    public sealed class CafeFrontDeskUI : MonoBehaviour
    {
        public CafeReceptionController reception;
        public CafeRecommendationController recommendation;
        public Camera customerCamera;
        [Range(20, 200)] public float chatScrollSpeed = 75;
        public Vector3 receptionCenter = new Vector3(.7f, 0, 0);
        public Vector3 receptionHalfExtents = new Vector3(1.4f, 2, 1);
        private CafeDesktopPlayer player;
        private bool greeted;
        public bool IsInterfaceVisible => root && root.gameObject.activeSelf;
        public string InteractionState => mode;
        private RectTransform root;
        private Font font;
        private InputField input;
        private Text dialogue, status, selectionText;
        private Button send, another, confirm;
        private RectTransform options;
        private string mode = "welcome", draft = "我想喝一杯燕麦奶拿铁，预算25元。";
        private bool purchased, dirty;
        private bool purchasing, scrollToLatest;
        private ScrollRect chatScroll;
        private CafeOrderReceipt currentReceipt;
        public CafeOrderReceipt CurrentReceipt => currentReceipt;
        private readonly List<string> messages = new List<string>();
        private readonly List<Button> configurationButtons = new List<Button>();
        public Canvas TerminalCanvas { get; private set; }
        public void SetPointerInteraction(bool enabled)
        {
            if (!root) return;
            var group = root.GetComponent<CanvasGroup>();
            if (!group) group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = enabled;
            group.blocksRaycasts = enabled;
        }
        public void EndPlayerInteraction()
        {
            if (!root || mode == "closed") return;
            recommendation.CancelSession();
            reception.EndReception();
            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
            SetPointerInteraction(false);
            if (player) player.SetUiMode(false);
        }

        private void Start()
        {
            if (!reception) reception = GetComponent<CafeReceptionController>();
            if (!recommendation) recommendation = GetComponent<CafeRecommendationController>();
            if (!customerCamera) customerCamera = Camera.main;
            var customer = GameObject.Find("CafeCustomer");
            if (customer)
                foreach (var renderer in customer.GetComponentsInChildren<Renderer>())
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            recommendation.menuAdapter = reception.menuAdapter;
            recommendation.highlighter = reception.highlighter;
            var old = reception.welcomePanel.GetComponentInParent<Canvas>();
            if (old) old.enabled = false;
            if (!FindObjectOfType<EventSystem>()) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 24);
            var go = new GameObject("FrontDeskTerminal", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(customerCamera.transform, false);
            TerminalCanvas = go.GetComponent<Canvas>();
            TerminalCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            TerminalCanvas.worldCamera = customerCamera;
            TerminalCanvas.planeDistance = Mathf.Max(customerCamera.nearClipPlane + .1f, .6f);
            TerminalCanvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 1080);
            scaler.matchWidthOrHeight = .5f;
            root = new GameObject("InteractionPanel", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(go.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(800, 800);
            root.gameObject.AddComponent<Image>().color = new Color(.99f, .95f, .87f);
            recommendation.ReplyChanged.AddListener(OnReply);
            recommendation.ErrorChanged.AddListener(OnError);
            recommendation.RecommendationApplied.AddListener(OnSelection);
            CafeRuntimeServices.Menu.InventoryChanged += InventoryChanged;
            reception.EndReception();
            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
        }
        private void OnDestroy()
        {
            if (recommendation)
            {
                recommendation.ReplyChanged.RemoveListener(OnReply);
                recommendation.ErrorChanged.RemoveListener(OnError);
                recommendation.RecommendationApplied.RemoveListener(OnSelection);
                recommendation.CancelSession();
            }
            if (font) Destroy(font);
            CafeRuntimeServices.Menu.InventoryChanged -= InventoryChanged;
        }
        private void InventoryChanged() { dirty = true; }
        private void OnReply(string value) { messages.Add("小暖：" + value); if (messages.Count > 12) messages.RemoveAt(0); RefreshDialogue(); }
        private void OnError(string value) { if (status) status.text = value; }
        private void OnSelection(MenuSelection value) { purchased = false; currentReceipt = null; dirty = true; }
        private void Clear()
        {
            if (input) draft = input.text;
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            configurationButtons.Clear();
        }
        public void ShowWelcome()
        {
            if (!root) return;
            root.gameObject.SetActive(true);
            TerminalCanvas.enabled = true;
            root.sizeDelta = new Vector2(800, 800);
            mode = "welcome"; recommendation.CancelSession(); reception.BeginReception(); Clear();
            Label(root, "Virtual Cafe", 40, 30, 720, 70, 42);
            Label(root, "欢迎光临！今天想喝点什么？", 40, 135, 720, 100, 32);
            MakeButton(root, "帮我选一杯", 60, 290, 680, 84, OpenRecommendation);
            MakeButton(root, "我想直接点单", 60, 405, 680, 84, OpenOrder);
            MakeButton(root, "稍后再来", 60, 520, 680, 84, EndPlayerInteraction);
            Label(root, "选择操作 · Esc 返回自由行走", 60, 690, 680, 60, 22);
        }
        public void EvaluateReception(Vector3 position, bool seated)
        {
            if (!root) return;
            Vector3 delta = position - receptionCenter;
            bool inside = Mathf.Abs(delta.x) <= receptionHalfExtents.x && Mathf.Abs(delta.z) <= receptionHalfExtents.z;
            bool outside = Mathf.Abs(delta.x) > receptionHalfExtents.x + .35f || Mathf.Abs(delta.z) > receptionHalfExtents.z + .35f;
            if (outside)
            {
                if (greeted) EndPlayerInteraction();
                greeted = false;
                return;
            }
            if (!inside || seated || greeted) return;
            greeted = true;
            mode = "prompt";
            root.gameObject.SetActive(true);
            TerminalCanvas.enabled = true;
            root.sizeDelta = new Vector2(640, 300);
            Clear();
            Label(root, "欢迎光临，想点一杯吗？", 25, 25, 590, 85, 32);
            MakeButton(root, "是，我想点单", 25, 150, 285, 85, AcceptReception);
            MakeButton(root, "否，先逛逛", 330, 150, 285, 85, EndPlayerInteraction);
            if (player) player.SetUiMode(true);
            SetPointerInteraction(true);
        }
        public void AcceptReception()
        {
            if (mode != "prompt") return;
            ShowWelcome();
            if (player) player.SetUiMode(true);
            SetPointerInteraction(true);
        }
        public void OpenRecommendation()
        {
            mode = "recommendation"; messages.Clear(); purchased = false; currentReceipt = null;
            reception.OpenRecommendation(); Build(); recommendation.BeginSession();
        }
        public void OpenOrder()
        {
            mode = "order"; recommendation.CancelSession(); reception.OpenOrder(); purchased = false; currentReceipt = null; Build();
        }
        private void Build()
        {
            Clear();
            Label(root, mode == "order" ? "今天的菜单" : "和小暖选一杯", 25, 16, 560, 55, 34);
            MakeButton(root, "返回", 650, 20, 125, 48, ShowWelcome);
            if (mode == "recommendation")
            {
                // Scrollable conversation viewport, kept inside the physical screen.
                var viewport = Box(root, "Conversation", 25, 82, 750, 215);
                viewport.gameObject.AddComponent<Image>().color = new Color(1, .99f, .96f);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Box(viewport, "Content", 0, 0, 718, 215);
                dialogue = Label(content, "", 12, 0, 716, 215, 24);
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = chatScrollSpeed;
                scroll.decelerationRate = .08f;
                chatScroll = scroll;
                var barRoot = Box(viewport, "ChatScrollbar", 730, 4, 16, 207);
                barRoot.gameObject.AddComponent<Image>().color = new Color(.91f, .86f, .78f);
                var bar = barRoot.gameObject.AddComponent<Scrollbar>();
                var handle = Box(barRoot, "Handle", 0, 0, 16, 40);
                handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
                handle.offsetMin = handle.offsetMax = Vector2.zero;
                var handleImage = handle.gameObject.AddComponent<Image>();
                handleImage.color = new Color(.57f, .4f, .25f);
                bar.handleRect = handle; bar.targetGraphic = handleImage;
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
                dialogue.gameObject.AddComponent<LayoutElement>().minHeight = 215;
                var field = Box(root, "PreferenceInput", 25, 308, 530, 78);
                field.gameObject.AddComponent<Image>().color = Color.white;
                input = field.gameObject.AddComponent<InputField>();
                input.textComponent = Label(field, "", 10, 6, 510, 66, 24);
                input.lineType = InputField.LineType.MultiLineNewline; input.characterLimit = 2000; input.text = draft;
                send = MakeButton(root, "发送偏好", 570, 308, 205, 78, () => Submit(false));
                another = MakeButton(root, "换一杯", 570, 397, 205, 48, () => Submit(true));
                MakeButton(root, "重试", 25, 397, 150, 48, () => recommendation.Retry());
                MakeButton(root, "直接点单", 190, 397, 180, 48, OpenOrder);
                RefreshDialogue();
            }
            else
            {
                int index = 0;
                foreach (var item in CafeRuntimeServices.Menu.GetMenu().items)
                {
                    string id = item.id;
                    MakeButton(root, item.name, 25 + index % 2 * 380, 90 + index / 2 * 66, 365, 56,
                        () => { reception.menuAdapter.SelectDrink(id); reception.menuAdapter.SetQuantity(1); purchased = false; currentReceipt = null; if (reception.highlighter) reception.highlighter.HighlightProduct(id); dirty = true; });
                    index++;
                }
            }
            status = Label(root, "", 25, mode == "order" ? 270 : 450, 750, 60, 22);
            selectionText = Label(root, "", 25, 520, 750, 75, 24);
            options = Box(root, "Configuration", 25, 600, 750, 110);
            confirm = MakeButton(root, "确认购买（模拟）", 25, 725, 750, 52, Purchase);
            dirty = true;
        }
        private void RefreshDialogue()
        {
            if (!dialogue) return;
            dialogue.text = string.Join("\n\n", messages);
            scrollToLatest = true;
        }
        private void Submit(bool alternate)
        {
            if (recommendation.IsBusy || (!alternate && string.IsNullOrWhiteSpace(input.text))) return;
            messages.Add("你：" + (alternate ? "保留我的偏好，换一杯吧。" : input.text));
            purchased = false; currentReceipt = null; RefreshDialogue();
            if (alternate) recommendation.RecommendAnother(); else recommendation.Submit(input.text);
            dirty = true;
        }
        private void Purchase()
        {
            if (purchased || purchasing || recommendation.IsBusy) return;
            if (mode == "recommendation" && recommendation.CurrentRecommendation == null) return;
            var adapter = reception.menuAdapter;
            var selection = adapter.CurrentSelection;
            if (!CafeRuntimeServices.Menu.TryGetPrice(selection, out var quote, out string reason)) { status.text = reason; return; }
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            var receipt = new CafeOrderReceipt { selection = selection, drinkName = item.name,
                beanName = Name(string.IsNullOrEmpty(selection.BeanId) ? item.beanIds[0] : selection.BeanId),
                milkName = string.IsNullOrEmpty(selection.MilkId) ? "" : Name(selection.MilkId), totalPriceCents = quote.totalPriceCents };
            purchasing = true;
            if (confirm) confirm.interactable = false;
            try
            {
                adapter.ConfirmCurrentSelection();
                purchased = adapter.LastConfirmationSucceeded;
                if (!purchased) { status.text = adapter.LastResult; return; }
                currentReceipt = receipt;
                receipt.IssuePickupNumber();
                recommendation.NotifyConfirmedOrder(receipt);
                status.text = mode == "order" ? receipt.ClerkMessage : "点单完成 · 取餐号 " + receipt.pickupNumber;
                if (mode == "order") status.rectTransform.sizeDelta = new Vector2(750, 230);
                dirty = true;
            }
            finally { purchasing = false; }
        }
        private void Update()
        {
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) EvaluateReception(player.transform.position, player.IsSeated);
            if (mode != "order" && mode != "recommendation") return;
            bool busy = recommendation.IsBusy;
            bool selected = mode == "order" || recommendation.CurrentRecommendation != null;
            if (send) send.interactable = !busy;
            if (another) another.interactable = !busy;
            if (input) input.interactable = !busy;
            if (confirm) confirm.interactable = !busy && selected && !purchased && !purchasing && reception.menuAdapter.CanConfirm;
            if (busy && status) status.text = "正在看看最新菜单，稍等一下……";
            else if (status && status.text == "正在看看最新菜单，稍等一下……") status.text = recommendation.ErrorText;
            foreach (var b in configurationButtons) if (b) b.interactable = !busy && !purchased;
            if (dirty) { dirty = false; DrawSelection(selected); }
        }
        private void LateUpdate()
        {
            if (!root || !root.gameObject.activeSelf) return;
            var canvasRect = TerminalCanvas.GetComponent<RectTransform>();
            Vector2 available = canvasRect.rect.size * .9f;
            if (available.x <= 0 || available.y <= 0) return;
            float fit = Mathf.Min(1, available.x / root.sizeDelta.x, available.y / root.sizeDelta.y);
            root.localScale = Vector3.one * fit;
            root.anchoredPosition = Vector2.zero;
            root.localRotation = Quaternion.identity;
            if (scrollToLatest && chatScroll && chatScroll.gameObject.activeInHierarchy)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(chatScroll.content);
                Canvas.ForceUpdateCanvases();
                chatScroll.StopMovement();
                chatScroll.verticalNormalizedPosition = 0;
                scrollToLatest = false;
            }
        }
        private void DrawSelection(bool selected)
        {
            foreach (Transform child in options) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            configurationButtons.Clear();
            if (purchased && currentReceipt != null)
            {
                selectionText.text = currentReceipt.drinkName + " · " + currentReceipt.selection.CupSizeMl + "ml · 已确认";
                Label(options, "你的取餐号：" + currentReceipt.pickupNumber, 0, 5, 750, 65, 38);
                Label(options, "请保留号码，可以先到座位休息。", 0, 70, 750, 35, 22);
                confirm.GetComponentInChildren<Text>().text = "已完成点单";
                return;
            }
            confirm.GetComponentInChildren<Text>().text = "确认购买（模拟）";
            if (!selected) { selectionText.text = "告诉我你的口味，推荐会显示在这里。"; return; }
            var adapter = reception.menuAdapter; var s = adapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == s.MenuItemId);
            if (item == null) return;
            selectionText.text = item.name + " · " + s.CupSizeMl + "ml · " + adapter.PriceText + "\n" +
                Name(string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId) +
                (string.IsNullOrEmpty(s.MilkId) ? " · 不加奶" : " · " + Name(s.MilkId)) + " · " + adapter.StatusText;
            int x = 0;
            foreach (int size in item.cupSizesMl)
            {
                int v = size; configurationButtons.Add(MakeButton(options, size + "ml", x, 0, 120, 45, () => { adapter.SelectCupSize(v); dirty = true; })); x += 125;
            }
            foreach (string bean in item.beanIds)
            {
                string id = bean; configurationButtons.Add(MakeButton(options, Name(id), x, 0, 150, 45, () => { adapter.SelectBean(id); dirty = true; })); x += 155;
            }
            x = 0;
            foreach (string milk in item.milkIds)
            {
                string id = milk; configurationButtons.Add(MakeButton(options, Name(id), x, 55, 235, 45, () => { adapter.SelectMilk(id); dirty = true; })); x += 245;
            }
        }
        private string Name(string id) => CafeRuntimeServices.Inventory.GetItemDefinition(id).name;
        private RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        private Text Label(Transform parent, string value, float x, float y, float w, float h, int size)
        {
            var text = Box(parent, "Text", x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.text = value; text.color = new Color(.23f, .16f, .11f);
            text.supportRichText = false; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        private Button MakeButton(Transform parent, string value, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
        {
            var rt = Box(parent, value, x, y, w, h); rt.gameObject.AddComponent<Image>().color = new Color(.85f, .73f, .56f);
            var b = rt.gameObject.AddComponent<Button>(); b.onClick.AddListener(action);
            var text = Label(rt, value, 3, 0, w - 6, h, 24); text.alignment = TextAnchor.MiddleCenter; return b;
        }
    }
}
