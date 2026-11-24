using System.Collections;
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
        [Header("Order animation")]
        public CafeOrderAnimation orderAnimation;
        public float completionDisplaySeconds = 3f;

        private Coroutine orderCompletionRoutine;
        [Range(20, 200)] public float chatScrollSpeed = 75;
        public Vector3 receptionCenter = new Vector3(.7f, 0, 0);
        public Vector3 receptionHalfExtents = new Vector3(1.4f, 2, 1);
        private CafeDesktopPlayer player;
        private bool greeted;
        public bool IsInterfaceVisible => root && root.gameObject.activeSelf;
        public string InteractionState => mode;
        // 提供给原料模型展示读取。
        public bool IsBeanChosen => beanChosen;
        public bool IsMilkChosen => milkChosen;
        private RectTransform root;
        private Font font;
        private InputField input;
        private Text dialogue, status, selectionText;
        private Button send, another, confirm;
        private RectTransform options;
        private string mode = "welcome", draft = "I would like an oat milk latte, within CNY 25.";
        private bool purchased, dirty;
        private bool beanChosen = true, cupChosen = true, milkChosen = true;
        private GameObject validationPopup;
        private string beanChoiceProduct;
        private bool purchasing, scrollToLatest, wasBusy, hadRecommendation;
        private CafeMenuService sharedMenu;
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
            sharedMenu = CafeRuntimeServices.Menu;
            sharedMenu.InventoryChanged += InventoryChanged;
            reception.EndReception();
            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
        }
        private void OnDisable()
        {
            if (orderCompletionRoutine != null)
            {
                StopCoroutine(orderCompletionRoutine);
                orderCompletionRoutine = null;
            }

            purchasing = false;

            if (root)
                EndPlayerInteraction();
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
            if (sharedMenu != null) sharedMenu.InventoryChanged -= InventoryChanged;
            if (TerminalCanvas) Destroy(TerminalCanvas.gameObject);
        }
        private void InventoryChanged() { dirty = true; RefreshMenuOffers(); }
        private void OnReply(string value) { if (mode != "recommendation") return; if (value == "你好呀！今天想喝点什么？口味、奶种或预算都可以告诉我。") value = "Hello! Tell me your taste, milk preference or budget."; messages.Add("Assistant: " + value); if (messages.Count > 12) messages.RemoveAt(0); RefreshDialogue(); }
        private void OnError(string value) { if (mode == "recommendation" && status) status.text = DisplayEnglish(value); }
        private void OnSelection(MenuSelection value) { if (mode != "recommendation" || value == null) return; beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = value.MenuItemId; purchased = false; currentReceipt = null; dirty = true; }
        private void Clear()
        {
            if (input) draft = input.text;
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            configurationButtons.Clear(); menuButtons.Clear(); optionAvailability.Clear();
            validationPopup = null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
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
            MakeButton(root, "Close", 650, 30, 110, 48, EndPlayerInteraction);
            Label(root, "Welcome! What would you like today?", 40, 135, 720, 100, 32);
            MakeButton(root, "Help me choose", 60, 290, 680, 84, OpenRecommendation);
            MakeButton(root, "Order from the menu", 60, 405, 680, 84, OpenOrder);
            MakeButton(root, "Maybe later", 60, 520, 680, 84, EndPlayerInteraction);
            Label(root, "Choose an option / Esc to close and walk", 60, 690, 680, 60, 22);
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
            Label(root, "Welcome! Would you like a coffee?", 25, 25, 590, 85, 32);
            MakeButton(root, "Yes, please", 25, 150, 285, 85, AcceptReception);
            MakeButton(root, "No, just browsing", 330, 150, 285, 85, EndPlayerInteraction);
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
            if (purchasing) return;
            mode = "recommendation"; messages.Clear(); purchased = false; currentReceipt = null;
            reception.OpenRecommendation(); Build(); recommendation.BeginSession();
        }
        public void OpenOrder()
        {
            if (purchasing) return;
            beanChosen = cupChosen = milkChosen = true;
            mode = "order"; recommendation.CancelSession(); reception.OpenOrder(); purchased = false; currentReceipt = null; Build();
        }
        private void Build()
        {
            root.sizeDelta = new Vector2(800, mode == "order" ? 930 : 1140);
            Clear();
            Label(root, mode == "order" ? "Our menu" : "Find your favourite", 25, 16, 515, 55, 34);
            MakeButton(root, "Back", 560, 20, 100, 48, ShowWelcome);
            MakeButton(root, "Close", 675, 20, 100, 48, EndPlayerInteraction);
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
                send = MakeButton(root, "Send", 570, 308, 205, 78, () => Submit(false));
                another = MakeButton(root, "Another option", 570, 397, 205, 48, () => Submit(true));
                MakeButton(root, "Retry", 25, 397, 150, 48, () => recommendation.Retry());
                MakeButton(root, "View menu", 190, 397, 180, 48, OpenOrder);
                RefreshDialogue();
            }
            else
            {
                int index = 0;
                foreach (var item in CafeRuntimeServices.Menu.GetMenu().items)
                {
                    string id = item.id;
                    var menuButton = MakeButton(root, DisplayEnglish(item.name), 25 + index % 2 * 380, 90 + index / 2 * 92, 365, 82,
                        () => { beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = id; if (status) status.text = ""; reception.menuAdapter.SelectDrink(id); reception.menuAdapter.SetQuantity(1); purchased = false; currentReceipt = null; if (reception.highlighter) reception.highlighter.HighlightProduct(id); dirty = true; });
                    menuButton.GetComponentInChildren<Text>().fontSize = 20;
                    menuButtons[id] = menuButton;
                    index++;
                }
                RefreshMenuOffers();
            }
            status = Label(root, "", 25, mode == "order" ? 278 : 450, 750, mode == "order" ? 38 : 60, 22);
            float summaryY = mode == "order" ? 318 : 520;
            float configY = mode == "order" ? 400 : 610;
            selectionText = Label(root, "", 30, summaryY, 740, 75, 24);
            options = Box(root, "Configuration", 30, configY, 740, 430);
            confirm = MakeButton(root, "Confirm order (demo)", 30, configY + 440, 740, 56, Purchase);
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
            messages.Add("You: " + (alternate ? "Keep my preferences and suggest a different drink." : input.text));
            purchased = false; currentReceipt = null; RefreshDialogue();
            if (alternate) recommendation.RecommendAnother(); else recommendation.Submit(input.text);
            dirty = true;
        }
        private void Purchase()
        {
            if (purchased || purchasing || validationPopup ||
                recommendation.IsBusy ||
                (mode != "order" && mode != "recommendation"))
                return;

            if (mode == "recommendation" &&
                recommendation.CurrentRecommendation == null)
                return;

            var adapter = reception.menuAdapter;
            var selection = adapter.CurrentSelection;

            var item = Array.Find(
                CafeRuntimeServices.Menu.GetMenu().items,
                x => x.id == selection.MenuItemId
            );

            var missing = CafeUiSelectionRules.MissingOptions(
                selection, item, cupChosen, beanChosen, milkChosen
            );

            if (missing.Length > 0)
            {
                ShowValidationPopup(
                    "Please select: " + string.Join(", ", missing)
                );
                return;
            }

            if (!CafeRuntimeServices.Menu.TryGetPrice(
                selection, out var quote, out string reason))
            {
                status.text = DisplayEnglish(reason);
                return;
            }

            var receipt = new CafeOrderReceipt
            {
                selection = selection,
                drinkName = DisplayEnglish(item.name),
                beanName = Name(
                    string.IsNullOrEmpty(selection.BeanId)
                        ? item.beanIds[0]
                        : selection.BeanId
                ),
                milkName = string.IsNullOrEmpty(selection.MilkId)
                    ? ""
                    : Name(selection.MilkId),
                totalPriceCents = quote.totalPriceCents
            };

            purchasing = true;

            if (confirm)
                confirm.interactable = false;

            bool confirmed = false;

            try
            {
                // Confirm stock exactly once before playing the animation.
                adapter.ConfirmCurrentSelection();
                purchased = adapter.LastConfirmationSucceeded;

                if (!purchased)
                {
                    status.text = DisplayEnglish(adapter.LastResult);
                    return;
                }

                currentReceipt = receipt;
                receipt.IssuePickupNumber();

                try
                {
                    recommendation.NotifyConfirmedOrder(receipt);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                confirmed = true;
            }
            finally
            {
                if (!confirmed)
                    purchasing = false;
            }

            orderCompletionRoutine =
                StartCoroutine(CompleteOrderWithAnimation());
        }

        private IEnumerator CompleteOrderWithAnimation()
        {
            SetPointerInteraction(false);

            if (status)
                status.text = "Preparing your drink...";

            try
            {
                yield return null;

                if (orderAnimation && orderAnimation.isActiveAndEnabled)
                    yield return orderAnimation.Play();

                ShowCompletion();
            }
            finally
            {
                purchasing = false;
                orderCompletionRoutine = null;

                // 动画结束后恢复按钮点击。
                if (root && root.gameObject.activeSelf)
                    SetPointerInteraction(true);
            }
        }
        private void ShowCompletion()
        {
            // Presentation only. Stock was already confirmed exactly once by Purchase.
            mode = "completed";
            recommendation.CancelSession();
            if (reception.highlighter) reception.highlighter.ClearHighlight();
            Clear(); dirty = false;
            root.sizeDelta = new Vector2(800, 650);
            Label(root, "Order confirmed", 40, 35, 560, 60, 36);
            MakeButton(root, "Close", 650, 35, 110, 48, EndPlayerInteraction);
            Label(root, "Purchase complete. Thank you for visiting!", 40, 115, 720, 50, 25);
            var receiptCard = Box(root, "Receipt", 40, 185, 720, 190);
            receiptCard.gameObject.AddComponent<Image>().color = new Color(.94f, .88f, .77f);
            Label(receiptCard, "Pickup number", 28, 20, 660, 36, 23);
            Label(receiptCard, currentReceipt.pickupNumber, 28, 62, 660, 80, 48);
            Label(root, DisplayEnglish(currentReceipt.drinkName) + " · " + currentReceipt.selection.CupSizeMl + " ml", 40, 400, 720, 45, 26);
            Label(root, "Take a seat and relax. Thank you for your patience.", 40, 455, 720, 50, 23);
            MakeButton(root, "Back to welcome", 40, 555, 345, 56, ShowWelcome);
            MakeButton(root, "Done, keep exploring", 405, 555, 355, 56, EndPlayerInteraction);
        }

        private void Update()
        {
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) EvaluateReception(player.transform.position, player.IsSeated);
            if (purchasing) return;
            if (mode != "order" && mode != "recommendation") return;
            bool busy = recommendation.IsBusy;
            bool selected = mode == "order" || recommendation.CurrentRecommendation != null;
            if (busy != wasBusy || selected != hadRecommendation) dirty = true;
            wasBusy = busy; hadRecommendation = selected;
            if (send) send.interactable = !busy;
            if (another) another.interactable = !busy;
            if (input) input.interactable = !busy;
            var current = reception.menuAdapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == current.MenuItemId);
            bool incomplete = CafeUiSelectionRules.MissingOptions(current, item, cupChosen, beanChosen, milkChosen).Length > 0;
            if (confirm) confirm.interactable = !validationPopup && !busy && selected && !purchased && !purchasing &&
                (incomplete || reception.menuAdapter.CanConfirm);
            if (busy && status) status.text = "Checking the latest menu. Please wait...";
            else if (status && status.text == "Checking the latest menu. Please wait...") status.text = mode == "recommendation" ? DisplayEnglish(recommendation.ErrorText) : "";
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
                selectionText.text = DisplayEnglish(currentReceipt.drinkName) + " · " + currentReceipt.selection.CupSizeMl + "ml / Confirmed";
                Label(options, "Your pickup number: " + currentReceipt.pickupNumber, 0, 5, 750, 65, 38);
                Label(options, "Keep this number. You may take a seat.", 0, 70, 750, 35, 22);
                confirm.GetComponentInChildren<Text>().text = "Order complete";
                return;
            }
            confirm.GetComponentInChildren<Text>().text = "Confirm order (demo)";
            if (!selected) { selectionText.text = "Tell us what you like. Your recommendation will appear here."; return; }
            var adapter = reception.menuAdapter; var s = adapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == s.MenuItemId);
            if (item == null) return;
            if (beanChoiceProduct != s.MenuItemId) { beanChoiceProduct = s.MenuItemId; beanChosen = cupChosen = milkChosen = true; }
            selectionText.text = DisplayEnglish(item.name) + " · " + (cupChosen ? s.CupSizeMl + "ml" : "Select a cup size") + " · " + adapter.PriceText + "\n" +
                (beanChosen ? Name(string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId) : "Select a coffee bean") +
                (item.milkIds.Length == 0 ? " / No milk" : !milkChosen ? " / Select milk" : " · " + Name(s.MilkId)) + " · " + (cupChosen && beanChosen && (item.milkIds.Length == 0 || milkChosen) ? DisplayEnglish(adapter.StatusText) : "Selection incomplete");
            Label(options, "Cup size", 0, 0, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            int x = 100;
            foreach (int size in item.cupSizesMl)
            {
                int value = size;
                var button = MakeButton(options, size + " ml", x, 0, 150, 48,
                    () => {
                        if (status) status.text = "";
                        if (cupChosen && adapter.CurrentSelection.CupSizeMl == value) cupChosen = false;
                        else { adapter.SelectCupSize(value); cupChosen = true; }
                        dirty = true;
                    });
                MarkSelected(button, cupChosen && s.CupSizeMl == size);
                var candidate = CopySelection(s); candidate.CupSizeMl = size;
                ConfigureAvailability(button, candidate, "Cup size " + size + "ml", "cup", unavailableReasons);
                configurationButtons.Add(button); x += 166;
            }
            Label(options, "Beans", 0, 68, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
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
            Label(options, "Milk", 0, 136, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            if (item.milkIds.Length == 0)
            {
                Label(options, "No milk", 100, 136, 640, 48, 22).alignment = TextAnchor.MiddleLeft;
            }
            else
            {
                x = 100;
                foreach (string milk in item.milkIds)
                {
                    string id = milk;
                    var button = MakeButton(options, Name(id), x, 136, 200, 48,
                        () => {
                            if (status) status.text = "";
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
            DrawRecipe(s, item);
            if (unavailableReasons.Count > 0)
            {
                var reasonText = Label(options, "Sold out: " + string.Join(", ", unavailableReasons) + ". Other available options can still be ordered.", 0, 374, 740, 52, 18);
                reasonText.color = new Color(.47f, .38f, .28f);
            }
        }
        private void DrawRecipe(MenuSelection selection, CafeMenuItem item)
        {
            var card = Box(options, "Recipe amounts", 0, 206, 740, 156);
            card.gameObject.AddComponent<Image>().color = new Color(.95f, .90f, .81f);
            var heading = Label(card, "RECIPE / PER CUP", 20, 14, 700, 28, 18);
            heading.fontStyle = FontStyle.Bold;
            if (CafeUiSelectionRules.MissingOptions(selection, item, cupChosen, beanChosen, milkChosen).Length > 0)
            {
                Label(card, "Complete your size, bean and milk choices to see the recipe.", 20, 58, 700, 70, 22);
                return;
            }
            if (!CafeRuntimeServices.Menu.TryGetRecipe(selection, out var recipe, out _))
            {
                Label(card, "Recipe details are unavailable for this selection.", 20, 58, 700, 70, 22);
                return;
            }
            // Definitions and quantities come from the same read-only service as pricing.
            var ingredients = new List<string>();
            foreach (var requirement in recipe.requirements)
            {
                string id = requirement.source == "bean"
                    ? (string.IsNullOrEmpty(selection.BeanId) ? item.beanIds[0] : selection.BeanId)
                    : requirement.source == "milk" ? selection.MilkId : requirement.itemId;
                var definition = CafeRuntimeServices.Inventory.GetItemDefinition(id);
                ingredients.Add(DisplayEnglish(definition.name) + "  " + requirement.quantity + " " + definition.unit);
            }
            // Two columns keep bean and milk amounts readable without a dense sentence.
            Label(card, ingredients.Count > 0 ? ingredients[0] : "", 20, 52, 340, 48, 23);
            Label(card, ingredients.Count > 1 ? string.Join(" / ", ingredients.GetRange(1, ingredients.Count - 1)) : "No milk", 380, 52, 340, 48, 23);
            var preparation = new List<string>();
            if (recipe.extractionTargetG > 0) preparation.Add("Espresso yield " + recipe.extractionTargetG + " g");
            if (recipe.waterMl > 0) preparation.Add("Water " + recipe.waterMl + " ml");
            if (recipe.iceG > 0) preparation.Add("Ice " + recipe.iceG + " g");
            var targets = Label(card, string.Join("  /  ", preparation), 20, 110, 700, 32, 18);
            targets.color = new Color(.47f, .38f, .28f);
        }
        private static MenuSelection CopySelection(MenuSelection s) => new MenuSelection {
            MenuItemId = s.MenuItemId, CupSizeMl = s.CupSizeMl,
            BeanId = s.BeanId, MilkId = s.MilkId, Servings = s.Servings
        };
        private void ConfigureAvailability(Button button, MenuSelection candidate, string name, string dimension, List<string> reasons)
        {
            // Test this option across legal combinations, not against an unrelated
            // exhausted ingredient in the current selection. Do not change selection.
            bool available = CafeUiSelectionRules.HasAvailableOption(CafeRuntimeServices.Menu.GetMenuOffers(),
                candidate, dimension, test => CafeRuntimeServices.Menu.IsAvailable(test, out _));
            optionAvailability[button] = available;
            button.interactable = available && !recommendation.IsBusy && !purchased;
            if (available) return;
            MarkSelected(button, false);
            var label = button.GetComponentInChildren<Text>();
            label.text += "\nSold out"; label.fontSize = 17;
            string detail = name;
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
                bool anyAvailable = false; string reason = "No available configuration";
                foreach (var offer in offers)
                {
                    if (offer.selection.MenuItemId != item.id) continue;
                    minimum = Math.Min(minimum, offer.price.unitPriceCents);
                    if (offer.available) { anyAvailable = true; availableMinimum = Math.Min(availableMinimum, offer.price.unitPriceCents); }
                    else reason = offer.reason;
                }
                int price = anyAvailable ? availableMinimum : minimum;
                string priceText = price == int.MaxValue ? "Price unavailable" : "¥" + (price / 100m).ToString("0.00") + "+";
                button.GetComponentInChildren<Text>().text = DisplayEnglish(item.name) + "  ·  " + priceText +
                    (anyAvailable ? "\nChoose size and ingredients" : "\nSold out: " + DisplayEnglish(reason));
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
            Label(card, "Complete your selection", 30, 25, 540, 45, 30);
            Label(card, message, 30, 90, 540, 65, 25);
            MakeButton(card, "Back to options", 30, 190, 540, 52, () => {
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

        private static string DisplayEnglish(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string[,] words = {
                {"蓝山黑咖啡", "Blue Mountain coffee"}, {"瑰夏黑咖啡", "Geisha coffee"}, {"科纳黑咖啡", "Kona coffee"},
                {"牙买加蓝山", "Blue Mountain"}, {"巴拿马瑰夏", "Geisha"}, {"夏威夷科纳", "Kona"}, {"拿铁", "Latte"},
                {"全脂奶", "Whole milk"}, {"脱脂奶", "Skim milk"}, {"燕麦奶", "Oat milk"},
                {"可制作", "Available"}, {"库存不足", "Insufficient stock"}, {"需要", "Required"}, {"现有", "In stock"},
                {"整笔未扣减。", "No stock was deducted."}, {"库存保存失败，未扣减：", "Stock could not be saved; nothing deducted: "}, {"菜单饮品、杯型、豆奶选项或数量无效。", "Invalid drink, size, ingredient or quantity."}, {"确认失败：", "Confirmation failed: "}, {"库存初始化或查询失败：", "Inventory is unavailable: "}, {"成功", "Success"},
                {"请先输入偏好。", "Please enter your preferences."},
                {"请打开 Virtual Cafe > LLM Settings 填写接口、模型及API Key并保存。", "Open Virtual Cafe > LLM Settings, enter endpoint, model and API key, then save."}
            };
            for (int i=0;i<words.GetLength(0);i++) text=text.Replace(words[i,0],words[i,1]);
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            {
                if (text.Contains("LLM Settings")) return "Check your connection in Virtual Cafe > LLM Settings.";
                return "Unable to complete this action. Check your options, stock or connection and retry.";
            }
            return text;
        }
        private string Name(string id) => DisplayEnglish(CafeRuntimeServices.Inventory.GetItemDefinition(id).name);
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
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = Label(rt, value, 3, 0, w - 6, h, 24); text.alignment = TextAnchor.MiddleCenter; return b;
        }
    }
}
