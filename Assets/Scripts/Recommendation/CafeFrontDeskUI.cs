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
        private bool beanChosen = true, cupChosen = true, milkChosen = true;
        private GameObject validationPopup;
        private string beanChoiceProduct;
        private bool purchasing, scrollToLatest;
        private ScrollRect chatScroll;
        private CafeOrderReceipt currentReceipt;
        public CafeOrderReceipt CurrentReceipt => currentReceipt;
        private readonly List<string> messages = new List<string>();
        private readonly List<Button> configurationButtons = new List<Button>();
        private readonly Dictionary<string, Button> menuButtons = new Dictionary<string, Button>();
        private readonly Dictionary<Button, bool> optionAvailability = new Dictionary<Button, bool>();
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
            if (!root || mode == "closed" || purchasing) return;
            if (input) draft = input.text;
            recommendation.CancelSession();
            reception.EndReception();
            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
            SetPointerInteraction(false);
            if (player) player.SetUiMode(false);
        }

        // UI-only reopen entry. Player/interaction owner can bind F to this method.
        // Does not change movement, reception detection, or the current auto-greeting rule.
        public bool CanOpenInteraction
        {
            get
            {
                if (!root || !isActiveAndEnabled || purchasing || IsInterfaceVisible) return false;
                if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
                if (!player || player.IsSeated) return false;
                Vector3 delta = player.transform.position - receptionCenter;
                return Mathf.Abs(delta.x) <= receptionHalfExtents.x &&
                    Mathf.Abs(delta.z) <= receptionHalfExtents.z;
            }
        }
        public bool TryOpenInteraction()
        {
            if (!CanOpenInteraction) return false;
            greeted = true;
            ShowWelcome();
            return true;
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
        private void InventoryChanged() { dirty = true; RefreshMenuOffers(); }
        private void OnReply(string value) { messages.Add("小暖：" + value); if (messages.Count > 12) messages.RemoveAt(0); RefreshDialogue(); }
        private void OnError(string value) { if (status) status.text = value; }
        private void OnSelection(MenuSelection value) { beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = value.MenuItemId; purchased = false; currentReceipt = null; dirty = true; }
        private void Clear()
        {
            if (input) draft = input.text;
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            configurationButtons.Clear(); menuButtons.Clear(); optionAvailability.Clear();
            input = null; dialogue = null; status = null; selectionText = null;
            send = null; another = null; confirm = null; chatScroll = null; options = null;
        }
        public void ShowWelcome()
        {
            if (!root || purchasing) return;
            root.gameObject.SetActive(true);
            TerminalCanvas.enabled = true;
            root.sizeDelta = new Vector2(800, 800);
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) player.SetUiMode(true);
            SetPointerInteraction(true);
            mode = "welcome"; recommendation.CancelSession(); reception.BeginReception(); Clear();
            Label(root, "Virtual Cafe", 40, 30, 560, 70, 42);
            MakeButton(root, "关闭", 650, 30, 110, 48, EndPlayerInteraction);
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
            root.sizeDelta = new Vector2(800, mode == "order" ? 830 : 1040);
            Clear();
            Label(root, mode == "order" ? "今天的菜单" : "和小暖选一杯", 25, 16, 515, 55, 34);
            MakeButton(root, "返回", 560, 20, 100, 48, ShowWelcome);
            MakeButton(root, "关闭", 675, 20, 100, 48, EndPlayerInteraction);
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
                    var menuButton = MakeButton(root, item.name, 25 + index % 2 * 380, 90 + index / 2 * 92, 365, 82,
                        () => { beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = id; reception.menuAdapter.SelectDrink(id); reception.menuAdapter.SetQuantity(1); purchased = false; currentReceipt = null; if (reception.highlighter) reception.highlighter.HighlightProduct(id); dirty = true; });
                    menuButton.GetComponentInChildren<Text>().fontSize = 20;
                    menuButtons[id] = menuButton;
                    index++;
                }
                RefreshMenuOffers();
            }
            status = Label(root, "", 25, mode == "order" ? 285 : 450, 750, 60, 22);
            float summaryY = mode == "order" ? 360 : 520;
            float configY = mode == "order" ? 450 : 610;
            selectionText = Label(root, "", 30, summaryY, 740, 75, 24);
            options = Box(root, "Configuration", 30, configY, 740, 270);
            confirm = MakeButton(root, "确认购买（模拟）", 30, configY + 290, 740, 56, Purchase);
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
            var definition = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            var missing = new List<string>();
            if (!cupChosen || definition == null || Array.IndexOf(definition.cupSizesMl, selection.CupSizeMl) < 0) missing.Add("杯型");
            string bean = definition != null && string.IsNullOrEmpty(selection.BeanId) && definition.beanIds.Length == 1 ? definition.beanIds[0] : selection.BeanId;
            if (!beanChosen || definition == null || Array.IndexOf(definition.beanIds, bean) < 0) missing.Add("咖啡豆");
            if (definition != null && definition.milkIds.Length > 0 &&
                (!milkChosen || Array.IndexOf(definition.milkIds, selection.MilkId) < 0)) missing.Add("奶种");
            if (missing.Count > 0)
            {
                ShowValidationPopup("请选择正确的" + string.Join("、", missing));
                return; // Never call confirmation or mutate stock for an incomplete UI selection.
            }
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
                ShowCompletion();
            }
            finally { purchasing = false; }
        }
        private void ShowCompletion()
        {
            // Presentation only. Stock was already confirmed exactly once by Purchase.
            mode = "completed";
            recommendation.CancelSession();
            if (reception.highlighter) reception.highlighter.ClearHighlight();
            Clear(); dirty = false;
            root.sizeDelta = new Vector2(800, 650);
            Label(root, "点单成功", 40, 35, 560, 60, 36);
            MakeButton(root, "关闭", 650, 35, 110, 48, EndPlayerInteraction);
            Label(root, "谢谢惠顾，请保留你的取餐号", 40, 115, 720, 50, 25);
            var receiptCard = Box(root, "Receipt", 40, 185, 720, 190);
            receiptCard.gameObject.AddComponent<Image>().color = new Color(.94f, .88f, .77f);
            Label(receiptCard, "取餐号", 28, 20, 660, 36, 23);
            Label(receiptCard, currentReceipt.pickupNumber, 28, 62, 660, 80, 48);
            Label(root, currentReceipt.drinkName + " · " + currentReceipt.selection.CupSizeMl + " ml", 40, 400, 720, 45, 26);
            Label(root, "可以先到座位休息，感谢你的耐心等待。", 40, 455, 720, 50, 23);
            MakeButton(root, "返回前台", 40, 555, 345, 56, ShowWelcome);
            MakeButton(root, "完成，继续逛逛", 405, 555, 355, 56, EndPlayerInteraction);
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
            foreach (var b in configurationButtons) if (b)
                b.interactable = !busy && !purchased && (!optionAvailability.TryGetValue(b, out bool allowed) || allowed);
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
            configurationButtons.Clear(); optionAvailability.Clear();
            var unavailableReasons = new List<string>();
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
            if (beanChoiceProduct != s.MenuItemId) { beanChoiceProduct = s.MenuItemId; beanChosen = cupChosen = milkChosen = true; }
            selectionText.text = item.name + " · " + (cupChosen ? s.CupSizeMl + "ml" : "未选择杯型") + " · " + adapter.PriceText + "\n" +
                (beanChosen ? Name(string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId) : "未选择咖啡豆") +
                (item.milkIds.Length == 0 ? " · 不加奶" : !milkChosen ? " · 未选择奶种" : " · " + Name(s.MilkId)) + " · " + (cupChosen && beanChosen && (item.milkIds.Length == 0 || milkChosen) ? adapter.StatusText : "待完善选项");
            Label(options, "杯型", 0, 0, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            int x = 100;
            foreach (int size in item.cupSizesMl)
            {
                int value = size;
                var button = MakeButton(options, size + " ml", x, 0, 150, 48,
                    () => {
                        if (cupChosen && adapter.CurrentSelection.CupSizeMl == value) cupChosen = false;
                        else { adapter.SelectCupSize(value); cupChosen = true; }
                        dirty = true;
                    });
                MarkSelected(button, cupChosen && s.CupSizeMl == size);
                var candidate = CopySelection(s); candidate.CupSizeMl = size;
                ConfigureAvailability(button, candidate, "杯型 " + size + "ml", "cup", unavailableReasons);
                configurationButtons.Add(button); x += 166;
            }
            Label(options, "咖啡豆", 0, 68, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            x = 100;
            string activeBean = string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId;
            foreach (string bean in item.beanIds)
            {
                string id = bean;
                var button = MakeButton(options, Name(id), x, 68, 200, 48, () =>
                {
                    var current = adapter.CurrentSelection;
                    string currentBean = string.IsNullOrEmpty(current.BeanId) ? item.beanIds[0] : current.BeanId;
                    if (beanChosen && currentBean == id)
                    {
                        beanChosen = false;
                        status.text = "";
                    }
                    else
                    {
                        adapter.SelectBean(id);
                        beanChosen = true;
                        status.text = "";
                    }
                    dirty = true;
                });
                MarkSelected(button, beanChosen && activeBean == id);
                var candidate = CopySelection(s); candidate.BeanId = id;
                ConfigureAvailability(button, candidate, Name(id), "bean", unavailableReasons);
                configurationButtons.Add(button); x += 215;
            }
            Label(options, "奶种", 0, 136, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            if (item.milkIds.Length == 0)
            {
                Label(options, "不加奶", 100, 136, 640, 48, 22).alignment = TextAnchor.MiddleLeft;
            }
            else
            {
                x = 100;
                foreach (string milk in item.milkIds)
                {
                    string id = milk;
                    var button = MakeButton(options, Name(id), x, 136, 200, 48,
                        () => {
                            if (milkChosen && adapter.CurrentSelection.MilkId == id) milkChosen = false;
                            else { adapter.SelectMilk(id); milkChosen = true; }
                            dirty = true;
                        });
                    MarkSelected(button, milkChosen && s.MilkId == id);
                    var candidate = CopySelection(s); candidate.MilkId = id;
                    ConfigureAvailability(button, candidate, Name(id), "milk", unavailableReasons);
                    configurationButtons.Add(button); x += 215;
                }
            }
            if (unavailableReasons.Count > 0)
            {
                var reasonText = Label(options, string.Join("；", unavailableReasons), 0, 192, 740, 85, 18);
                reasonText.color = new Color(.60f, .25f, .17f);
            }
        }
        private static MenuSelection CopySelection(MenuSelection s) => new MenuSelection {
            MenuItemId = s.MenuItemId, CupSizeMl = s.CupSizeMl,
            BeanId = s.BeanId, MilkId = s.MilkId, Servings = s.Servings
        };
        private void ConfigureAvailability(Button button, MenuSelection candidate, string name, string dimension, List<string> reasons)
        {
            // Test this option across legal combinations, not against an unrelated
            // exhausted ingredient in the current selection. Do not change selection.
            bool available = false;
            string reason = "没有可制作的组合";
            foreach (var offer in CafeRuntimeServices.Menu.GetMenuOffers())
            {
                var option = offer.selection;
                if (option.MenuItemId != candidate.MenuItemId) continue;
                if (dimension == "cup" && option.CupSizeMl != candidate.CupSizeMl) continue;
                if (dimension == "bean" && option.BeanId != candidate.BeanId) continue;
                if (dimension == "milk" && option.MilkId != candidate.MilkId) continue;
                var test = CopySelection(option); test.Servings = candidate.Servings;
                if (CafeRuntimeServices.Menu.IsAvailable(test, out string failure))
                { available = true; break; }
                reason = failure;
            }
            optionAvailability[button] = available;
            button.interactable = available && !recommendation.IsBusy && !purchased;
            if (available) return;
            MarkSelected(button, false);
            var label = button.GetComponentInChildren<Text>();
            label.text += "\n售罄"; label.fontSize = 18;
            string detail = name + "无可售组合";
            if (!reasons.Contains(detail)) reasons.Add(detail);
        }
        private void RefreshMenuOffers()
        {
            if (menuButtons.Count == 0) return;
            var menu = CafeRuntimeServices.Menu;
            var offers = menu.GetMenuOffers();
            foreach (var item in menu.GetMenu().items)
            {
                if (!menuButtons.TryGetValue(item.id, out var button) || !button) continue;
                int minimum = int.MaxValue; int availableMinimum = int.MaxValue;
                bool anyAvailable = false; string reason = "没有可售配置";
                foreach (var offer in offers)
                {
                    if (offer.selection.MenuItemId != item.id) continue;
                    minimum = Math.Min(minimum, offer.price.unitPriceCents);
                    if (offer.available) { anyAvailable = true; availableMinimum = Math.Min(availableMinimum, offer.price.unitPriceCents); }
                    else reason = offer.reason;
                }
                int price = anyAvailable ? availableMinimum : minimum;
                string priceText = price == int.MaxValue ? "暂无报价" : "¥" + (price / 100m).ToString("0.00") + " 起";
                button.GetComponentInChildren<Text>().text = item.name + "  ·  " + priceText +
                    (anyAvailable ? "\n选择杯型与原料" : "\nSold out / 售罄：" + reason);
                button.interactable = anyAvailable;
                button.GetComponent<Image>().color = anyAvailable ? new Color(.85f, .73f, .56f) : new Color(.78f, .77f, .74f);
            }
        }
        private void ShowValidationPopup(string message)
        {
            if (validationPopup) return;
            var overlay = Box(root, "Selection validation", 0, 0, root.sizeDelta.x, root.sizeDelta.y);
            overlay.gameObject.AddComponent<Image>().color = new Color(.10f, .07f, .04f, .55f);
            validationPopup = overlay.gameObject;
            overlay.SetAsLastSibling(); // The raycastable overlay blocks all controls behind the dialog.
            var card = Box(overlay, "Dialog", (root.sizeDelta.x - 600) / 2, (root.sizeDelta.y - 270) / 2, 600, 270);
            card.gameObject.AddComponent<Image>().color = new Color(.99f, .95f, .87f);
            Label(card, "请完善饮品选项", 30, 25, 540, 45, 30);
            Label(card, message, 30, 90, 540, 65, 25);
            MakeButton(card, "返回选择", 30, 190, 540, 52, () => {
                overlay.gameObject.SetActive(false);
                Destroy(overlay.gameObject);
                validationPopup = null;
            });
        }

        private static void MarkSelected(Button button, bool selected)
        {
            if (!selected)
            {
                button.GetComponent<Image>().color = new Color(.78f, .77f, .74f);
                button.GetComponentInChildren<Text>().color = new Color(.30f, .29f, .27f);
                return;
            }
            button.GetComponent<Image>().color = new Color(.43f, .29f, .18f);
            button.GetComponentInChildren<Text>().color = new Color(1, .98f, .93f);
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
