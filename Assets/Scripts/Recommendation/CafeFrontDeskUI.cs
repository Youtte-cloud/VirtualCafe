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
        [Header("Voice conversation")]
        [Tooltip("Optional: drag the robot head here. Otherwise a barista model is detected at runtime.")]
        public Transform baristaSpeechAnchor;
        public Transform customerSpeechAnchor;
        private CafeSpeechConversation speech;
        private bool pendingMachineProduction;
        private CafeBaristaBubble speechBubble;
        private CafeBaristaBubble customerBubble;
        private Button microphoneButton;
        private Text speechStatus;
        [Header("Order animation")]
        public CafeOrderAnimation orderAnimation;
        [Header("Coffee machine production")]
        public CafeMachineProduction machineProduction;
        public float completionDisplaySeconds = 3f;

        private Coroutine orderCompletionRoutine;
        [Range(20, 200)] public float chatScrollSpeed = 75;
        public Vector3 receptionCenter = new Vector3(.7f, 0, 0);
        public Vector3 receptionHalfExtents = new Vector3(1.4f, 2, 1);
        [Tooltip("Horizontal interaction radius around the barista. Leave this radius plus 0.5 m to re-arm the greeting.")]
        [Range(1f, 3f)] public float baristaApproachRadius = 2f;
        private CafeDesktopPlayer player;
        private bool greeted;
        private bool autoGreetingDismissed;
        private int worldClickBlockedUntilFrame;
        public bool IsInterfaceVisible => root && (root.gameObject.activeSelf || dialogueActive || waitingForReply);
        public string InteractionState => mode;
        public bool IsPreparingOrder => purchasing;
        private bool browsingMenu, hasChosenDrink;
        public bool ShouldShowDrinkPreview => IsInterfaceVisible && !browsingMenu &&
            ((mode == "order" && hasChosenDrink) ||
             (mode == "recommendation" && recommendation && recommendation.CurrentRecommendation != null));
        // 提供给原料模型展示读取。
        public bool IsBeanChosen => beanChosen;
        public bool IsMilkChosen => milkChosen;
        private RectTransform root;
        private Font font;
        private Sprite roundedCard;
        private bool dialogueActive, waitingForReply;
        private string lastSubmittedPreference;
        private InputField input;
        private Text status, selectionText;
        private Button send, another, confirm;
        private RectTransform options;
        private string mode = "welcome", draft = "";
        private bool purchased, dirty;
        private bool beanChosen = true, cupChosen = true, milkChosen = true;
        private GameObject validationPopup;
        private string beanChoiceProduct;
        private bool purchasing, scrollToLatest, wasBusy, hadRecommendation;
        private CafeMenuService sharedMenu;
        private ScrollRect chatScroll;
        private CafeOrderReceipt currentReceipt;
        private CafeOrderReceipt recentReceipt;
        private Action menuReturn;
        private bool languageChosen;
        private CafeCupInteraction cupInteraction;
        private CafeBeanInspection beanInspection;
        private Transform inspectedCupAnchor;
        private Transform autoSpeechAnchor;
        public bool CanInspectCups => root && isActiveAndEnabled && !IsInterfaceVisible && !purchasing &&
            Time.frameCount > worldClickBlockedUntilFrame && player && !player.IsSeated;
        public string HoveredCupName => beanInspection && !string.IsNullOrEmpty(beanInspection.HoverName)
            ? beanInspection.HoverName : cupInteraction ? cupInteraction.HoverName : "";
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
            autoGreetingDismissed = true;
            worldClickBlockedUntilFrame = Time.frameCount + 1;
            greeted = true;
            dialogueActive = waitingForReply = false;
            inspectedCupAnchor = null;

            if (beanInspection)
                beanInspection.ClearSelection();

            StopVoice();

            if (input)
                draft = input.text;

            recommendation.CancelSession();
            reception.EndReception();

            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
            SetPointerInteraction(false);

            if (player)
                player.SetUiMode(false);

            // 点单界面关闭后，由场景物体独立播放制作动画。
            if (pendingMachineProduction)
            {
                if (machineProduction &&
                    machineProduction.isActiveAndEnabled &&
                    machineProduction.BeginProduction(
                        currentReceipt.pickupNumber,
                        currentReceipt.selection.Servings))
                {
                    pendingMachineProduction = false;
                }
                else
                {
                    Debug.LogError(
                        "无法启动制作：请检查制作组件的绑定，" +
                        "以及上一杯是否已经取走。",
                        this
                    );
                }
            }
        }

        // UI-only reopen entry. Player/interaction owner can bind F to this method.
        // Does not change movement, reception detection, or the current auto-greeting rule.
        public bool CanOpenInteraction
        {
            get
            {
                if (!root || !isActiveAndEnabled || purchasing || (machineProduction && machineProduction.IsBusy) || IsInterfaceVisible) return false;
                if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
                if (!player || player.IsSeated) return false;
                return IsInReceptionRange;
            }
        }
        public bool IsInReceptionRange
        {
            get
            {
                if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
                return player && !player.IsSeated && InsideReception(player.transform.position, 0);
            }
        }
        private bool InsideReception(Vector3 position, float margin)
        {
            Transform anchor = baristaSpeechAnchor ? baristaSpeechAnchor : speechBubble ? speechBubble.baristaAnchor : null;
            Vector3 center = anchor ? anchor.position : receptionCenter;
            Vector3 offset = position - center;
            offset.y = 0;
            float radius = Mathf.Max(1, baristaApproachRadius) + margin;
            return offset.sqrMagnitude <= radius * radius;
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
            speech = GetComponent<CafeSpeechConversation>();
            if (!speech) speech = gameObject.AddComponent<CafeSpeechConversation>();
            speech.Recognized.AddListener(OnSpeechRecognized);
            speechBubble = gameObject.AddComponent<CafeBaristaBubble>();
            roundedCard = CreateRoundedCard();
            speechBubble.Initialize(customerCamera, font, false, roundedCard);
            customerBubble = gameObject.AddComponent<CafeBaristaBubble>();
            customerBubble.Initialize(customerCamera, font, true, roundedCard);
            customerBubble.baristaAnchor = customerSpeechAnchor;
            speechBubble.baristaAnchor = baristaSpeechAnchor;
            speechBubble.fallbackPosition = receptionCenter + new Vector3(0, 1.8f, 1.3f);
            if (!speechBubble.baristaAnchor)
                foreach (var renderer in FindObjectsOfType<Renderer>())
                {
                    Transform robot = null;
                    for (Transform t = renderer.transform; t; t = t.parent)
                    {
                        string name = t.name.ToLowerInvariant();
                        if (name.Contains("mugi") || name.Contains("barista") || name.Contains("robot")) { robot = t; break; }
                    }
                    if (!robot) continue;
                    // Imported robot models may use MeshRenderer rather than SkinnedMeshRenderer.
                    Bounds body = renderer.bounds;
                    foreach (var part in robot.GetComponentsInChildren<Renderer>())
                        if (!(part is ParticleSystemRenderer) && !part.GetComponent<TextMesh>()) body.Encapsulate(part.bounds);
                    autoSpeechAnchor = new GameObject("BaristaSpeechAnchor").transform;
                    autoSpeechAnchor.SetParent(robot, true);
                    autoSpeechAnchor.position = new Vector3(body.center.x, body.max.y - body.size.y * .18f, body.center.z);
                    speechBubble.baristaAnchor = autoSpeechAnchor;
                    break;
                }
            var go = new GameObject("FrontDeskTerminal", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            TerminalCanvas = go.GetComponent<Canvas>();
            // Interaction windows must stay readable even when the player is very close to the counter.
            TerminalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            TerminalCanvas.worldCamera = null;
            TerminalCanvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 1080);
            scaler.matchWidthOrHeight = .5f;
            root = new GameObject("InteractionPanel", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(go.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(800, 800);
            var background = root.gameObject.AddComponent<Image>();
            background.sprite = roundedCard; background.type = Image.Type.Sliced;
            background.color = new Color(.99f, .97f, .92f);
            root.gameObject.AddComponent<Shadow>().effectColor = new Color(.15f, .09f, .04f, .18f);
            recommendation.ReplyChanged.AddListener(OnReply);
            recommendation.ErrorChanged.AddListener(OnError);
            recommendation.RecommendationApplied.AddListener(OnSelection);
            CafeOrderReceipt.TryLoadLatest(out recentReceipt);
            sharedMenu = CafeRuntimeServices.Menu;
            sharedMenu.InventoryChanged += InventoryChanged;
            reception.EndReception();
            mode = "closed";
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = false;
            beanInspection = gameObject.AddComponent<CafeBeanInspection>();
            beanInspection.Initialize(this);
            cupInteraction = gameObject.AddComponent<CafeCupInteraction>();
            cupInteraction.Initialize(this, reception.highlighter, customerCamera, beanInspection);
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
            if (speech) { speech.Recognized.RemoveListener(OnSpeechRecognized); speech.Cancel(); }
            if (autoSpeechAnchor) Destroy(autoSpeechAnchor.gameObject);
            if (beanInspection) Destroy(beanInspection);
            if (cupInteraction) Destroy(cupInteraction);
            if (speechBubble) Destroy(speechBubble);
            if (customerBubble) Destroy(customerBubble);
            if (recommendation)
            {
                recommendation.ReplyChanged.RemoveListener(OnReply);
                recommendation.ErrorChanged.RemoveListener(OnError);
                recommendation.RecommendationApplied.RemoveListener(OnSelection);
                recommendation.CancelSession();
            }
            if (font) Destroy(font);
            if (roundedCard) { Destroy(roundedCard.texture); Destroy(roundedCard); }
            if (sharedMenu != null) sharedMenu.InventoryChanged -= InventoryChanged;
            if (TerminalCanvas) Destroy(TerminalCanvas.gameObject);
        }
        private void InventoryChanged() { dirty = true; RefreshMenuOffers(); }
        private void OnReply(string value)
        {
            if (mode != "recommendation" || purchasing) return;
            waitingForReply = false;
            if (CafeUiLocalization.LanguageIndex == 1 && value == "你好呀！今天想喝点什么？口味、奶种或预算都可以告诉我。")
                value = "Hello! Tell me your taste, milk preference or budget.";
            string priceSummary = null;
            if (recommendation.CurrentRecommendation != null)
            {
                var selection = reception.menuAdapter.CurrentSelection;
                var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
                if (item != null && CafeRuntimeServices.Menu.TryGetPrice(selection, out var quote, out _))
                    priceSummary = CafeUiLocalization.Format(
                        "{0}, {1} ml. Current price: CNY {2}.",
                        "{0}，{1} 毫升，当前价格 ¥{2}。",
                        "{0}、{1} ml。現在の価格は CNY {2} です。",
                        DisplayEnglish(item.name), selection.CupSizeMl, (quote.totalPriceCents / 100m).ToString("0.00"));
            }
            value = CafeReplyPriceFormatter.Compose(value, priceSummary);
            messages.Add("Assistant: " + value);
            if (messages.Count > 12) messages.RemoveAt(0);
            Speak(value, ShowRecommendationChoices, value.Length <= (CafeUiLocalization.LanguageIndex == 1 ? 95 : 50));
        }
        private void Speak(string text, Action next, bool automaticallyContinue = true)
        {
            if (!root || mode == "closed") return;
            dialogueActive = true; waitingForReply = false;
            root.gameObject.SetActive(false);
            TerminalCanvas.enabled = true;
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) player.SetUiMode(true);
            speechBubble.ShowDialogue(CafeUiLocalization.Text(text), () => {
                if (mode == "closed") return;
                dialogueActive = false;
                root.gameObject.SetActive(true);
                SetPointerInteraction(true);
                next?.Invoke();
            }, automaticallyContinue);
        }
        private void ActionWindow(string title, float height, Action back = null)
        {
            dialogueActive = waitingForReply = false;
            speechBubble.Hide(); customerBubble.Hide();
            root.gameObject.SetActive(true); TerminalCanvas.enabled = true;
            Clear(); root.sizeDelta = new Vector2(800, height);
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) player.SetUiMode(true);
            SetPointerInteraction(true);
            if (back != null) MakeButton(root, "Back", 28, 20, 100, 44, () => { if (!purchasing) back(); });
            Label(root, title, back == null ? 28 : 148, 20, back == null ? 600 : 492, 44, 29).fontStyle = FontStyle.Bold;
            MakeButton(root, "Close", 660, 20, 112, 44, EndPlayerInteraction);
        }
        private void StopVoice()
        {
            if (speech) speech.Cancel();
            if (speechBubble) speechBubble.Hide();
            if (customerBubble) customerBubble.Hide();
            if (recommendation) recommendation.SetConversationLanguage(CafeUiLocalization.LanguageCode);
        }
        private void OnSpeechRecognized(string text)
        {
            if (mode != "recommendation" || !speech.Active || recommendation.IsBusy || purchasing || dialogueActive || waitingForReply || !input) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            input.text = text.Trim();
            draft = input.text;
            Submit(false);
        }
        public void StartVoiceConversation(int languageIndex)
        {
            if (purchasing || !root) return;
            browsingMenu = false;
            languageChosen = true;
            CafeUiLocalization.LanguageIndex = Mathf.Clamp(languageIndex, 0, 2);
            draft = "";
            recommendation.CancelSession(); speech.Activate(CafeUiLocalization.LanguageIndex);
            recommendation.SetConversationLanguage(speech.Language);
            lastSubmittedPreference = null;
            mode = "recommendation"; messages.Clear(); purchased = false; currentReceipt = null;
            reception.OpenRecommendation(); recommendation.BeginSession();
        }
        private void ShowVoiceLanguages()
        {
            bool resume = mode == "recommendation";
            ShowLanguagePicker(index => {
                if (!resume) { StartVoiceConversation(index); return; }
                speech.Activate(index);
                recommendation.SetConversationLanguage(CafeUiLocalization.LanguageCode);
                mode = "recommendation";
                ShowPreferenceInput();
            }, resume ? (Action)ShowPreferenceInput : ShowWelcomeChoices);
        }
        private void ShowLanguagePicker(Action<int> selected, Action back)
        {
            // Keep the recommendation session and selected recipe intact while changing the display language.
            if (mode == "closed" || mode == "bean-info") mode = "languages";
            speechBubble.Hide(); customerBubble.Hide();
            if (speech) speech.Cancel();
            dialogueActive = waitingForReply = false;
            root.gameObject.SetActive(true); TerminalCanvas.enabled = true;
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (player) player.SetUiMode(true);
            SetPointerInteraction(true); Clear(); root.sizeDelta = new Vector2(800, 380);
            Label(root, "Choose a language", 40, 32, 720, 42, 30).fontStyle = FontStyle.Bold;
            Label(root, "选择语言  /  言語を選択", 40, 88, 720, 36, 24);
            for (int i = 0; i < CafeSpeechConversation.Languages.Length; i++)
            {
                int index = i;
                MakeButton(root, CafeSpeechConversation.LanguageNames[i], 40 + i * 245, 160, 230, 80, () => {
                    languageChosen = true;
                    CafeUiLocalization.LanguageIndex = index;
                    recommendation.SetConversationLanguage(CafeUiLocalization.LanguageCode);
                    selected(index);
                });
            }
            MakeButton(root, "Back", 40, 290, 200, 55, () => back());
        }
        private void OnError(string value)
        {
            if (mode != "recommendation" || string.IsNullOrEmpty(value)) return;
            waitingForReply = false;
            Speak(DisplayEnglish(value), () => {
                ShowPreferenceInput();
                if (status) status.text = DisplayEnglish(value);
            });
        }
        private void OnSelection(MenuSelection value) { if (mode != "recommendation" || value == null) return; beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = value.MenuItemId; purchased = false; currentReceipt = null; dirty = true; }
        private void Clear()
        {
            microphoneButton = null; speechStatus = null;
            if (input) draft = input.text;
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            configurationButtons.Clear(); menuButtons.Clear(); optionAvailability.Clear();
            validationPopup = null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            input = null; status = null; selectionText = null;
            send = null; another = null; confirm = null; chatScroll = null; options = null;
        }
        public void ShowWelcome()
        {
            if (!root || purchasing) return;
            if (!languageChosen) { ShowLanguagePicker(_ => ShowWelcome(), EndPlayerInteraction); return; }
            StopVoice(); recommendation.CancelSession(); reception.BeginReception();
            mode = "welcome";
            Speak("Would you like to choose a coffee yourself, or shall I help you find one?", ShowWelcomeChoices);
        }
        private void ShowWelcomeChoices()
        {
            if (purchasing) return;
            StopVoice(); recommendation.CancelSession(); reception.BeginReception(); mode = "welcome";
            ActionWindow("How would you like to order?", recentReceipt == null ? 300 : 356);
            root.Find("Text").GetComponent<RectTransform>().sizeDelta = new Vector2(490, 44);
            string languageName = CafeSpeechConversation.LanguageNames[CafeUiLocalization.LanguageIndex];
            var languageButton = MakeButton(root, languageName + " ▾", 542, 24, 100, 36,
                () => ShowLanguagePicker(_ => ShowWelcomeChoices(), ShowWelcomeChoices));
            languageButton.GetComponent<Image>().color = new Color(.95f, .91f, .84f);
            var languageText = languageButton.GetComponentInChildren<Text>();
            languageText.resizeTextMaxSize = 18; languageText.fontSize = 18;
            MakeButton(root, "Show me the menu", 28, 100, 360, 65, OpenOrder);
            MakeButton(root, "Help me choose", 412, 100, 360, 65, OpenRecommendation);
            MakeButton(root, "Order by voice", 28, 195, 360, 60, ShowVoiceLanguages);
            MakeButton(root, "Just browsing", 412, 195, 360, 60, EndPlayerInteraction);
            if (recentReceipt != null)
            {
                var recent = MakeButton(root, "View recent order", 28, 284, 270, 38, () => {
                    mode = "receipt"; RenderReceipt(recentReceipt, ShowWelcomeChoices, true);
                });
                recent.GetComponent<Image>().color = new Color(.95f, .91f, .84f);
                recent.GetComponentInChildren<Text>().resizeTextMaxSize = 19;
            }
        }
        public void EvaluateReception(Vector3 position, bool seated)
        {
            // 已经打开的对话保持原来的行为。
            if (!root || IsInterfaceVisible)
                return;

            // 取餐范围内不自动弹出点单。
            // 与制作脚本使用同一个取餐定位点和距离。
            if (machineProduction && machineProduction.pickupAnchor)
            {
                Vector3 difference =
                    position - machineProduction.pickupAnchor.position;

                difference.y = 0f;

                float pickupRadius = Mathf.Max(
                    0f,
                    machineProduction.pickupDistance
                );

                if (difference.sqrMagnitude <=
                    pickupRadius * pickupRadius)
                {
                    greeted = true;
                    autoGreetingDismissed = true;
                    return;
                }
            }

            bool inside = InsideReception(position, 0);
            bool outside = !InsideReception(position, .5f);

            // 真正离开接待区域后，允许下次进入时重新招呼。
            if (outside)
            {
                greeted = false;
                autoGreetingDismissed = false;
                return;
            }

            // 制作或等待取餐期间，不重新启动自动接待。
            // 同时记录已处理，避免制作结束后在原地弹窗。
            if (machineProduction && machineProduction.IsBusy)
            {
                greeted = true;
                autoGreetingDismissed = true;
                return;
            }

            if (!inside ||
                seated ||
                greeted ||
                autoGreetingDismissed ||
                purchasing)
            {
                return;
            }

            greeted = true;

            if (!languageChosen)
            {
                ShowLanguagePicker(
                    _ => BeginGreeting(),
                    EndPlayerInteraction
                );
                return;
            }

            BeginGreeting();
        }
        private void BeginGreeting()
        {
            mode = "prompt";
            Speak("Hello! Welcome to our cafe. Would you like a coffee?", () => {
                ActionWindow("Would you like to order?", 230);
                MakeButton(root, "Yes, please", 28, 110, 360, 65, AcceptReception);
                MakeButton(root, "Just browsing", 412, 110, 360, 65, EndPlayerInteraction);
            });
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
            browsingMenu = false;
            StopVoice();
            lastSubmittedPreference = null;
            mode = "recommendation"; messages.Clear(); purchased = false; currentReceipt = null;
            reception.OpenRecommendation(); recommendation.BeginSession();
        }
        public void OpenOrder()
        {
            if (purchasing) return;
            hasChosenDrink = false; browsingMenu = true;
            StopVoice();
            beanChosen = cupChosen = milkChosen = true;
            if (speechBubble) speechBubble.Hide();
            menuReturn = ShowWelcomeChoices;
            mode = "order"; recommendation.CancelSession(); reception.OpenOrder(); purchased = false; currentReceipt = null;
            Speak("Take your time. Which coffee would you like?", ShowDrinkMenu);
        }
        private void ShowDrinkMenu()
        {
            browsingMenu = true;
            var items = CafeRuntimeServices.Menu.GetMenu().items;
            ActionWindow("Choose your coffee", 145 + ((items.Length + 1) / 2) * 100, () => { (menuReturn ?? ShowWelcomeChoices)(); });
            int index = 0;
            foreach (var item in items)
            {
                string id = item.id;
                var button = MakeButton(root, DisplayEnglish(item.name), 28 + index % 2 * 384, 95 + index / 2 * 100, 360, 86, () => {
                    SelectCupForOrder(id);
                });
                var title = button.GetComponentInChildren<Text>();
                title.rectTransform.anchoredPosition = new Vector2(14, -8);
                title.rectTransform.sizeDelta = new Vector2(332, 38);
                title.alignment = TextAnchor.MiddleLeft;
                title.resizeTextForBestFit = true; title.resizeTextMinSize = 14; title.resizeTextMaxSize = 22;
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                var priceLabel = Label(button.transform, "", 14, 50, 332, 28, 20);
                priceLabel.gameObject.name = "MenuPrice";
                priceLabel.alignment = TextAnchor.MiddleLeft;
                priceLabel.resizeTextForBestFit = true; priceLabel.resizeTextMinSize = 14; priceLabel.resizeTextMaxSize = 20;
                menuButtons[id] = button; index++;
            }
            RefreshMenuOffers();
        }
        public void InspectCup(string productId)
        {
            if (!CanInspectCups) return;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == productId);
            if (item == null) return;
            if (reception.highlighter && reception.highlighter.products != null)
                foreach (var display in reception.highlighter.products)
                    if (display != null && display.productId == productId && display.cupRenderer)
                        inspectedCupAnchor = display.cupRenderer.transform;
            greeted = true; mode = "inspect";
            if (!languageChosen)
            {
                ShowLanguagePicker(_ => IntroduceCup(productId), EndPlayerInteraction);
                return;
            }
            IntroduceCup(productId);
        }
        public bool InspectBean(string beanId, Transform target)
        {
            if (!CanInspectCups) return false;
            var definition = CafeRuntimeServices.Inventory.GetItemDefinition(beanId);
            if (definition.category != "bean") return false;
            inspectedCupAnchor = target; greeted = true; mode = "bean-info";
            if (!languageChosen)
                ShowLanguagePicker(_ => ShowBeanInfo(beanId), EndPlayerInteraction);
            else ShowBeanInfo(beanId);
            return true;
        }
        private void ShowBeanInfo(string beanId)
        {
            mode = "bean-info";
            ActionWindow("", 300);
            // This is a small off-centre commentary card, not the normal ordering window.
            Clear();
            var definition = CafeRuntimeServices.Inventory.GetItemDefinition(beanId);
            const float width = 460, padding = 20;
            Label(root, "YOUR BARISTA", padding, 16, width - 40, 26, 17).fontStyle = FontStyle.Bold;
            Label(root, DisplayEnglish(definition.name), padding, 58, width - 40, 42, 27).fontStyle = FontStyle.Bold;
            var explanation = Label(root, CafeUiLocalization.Text(definition.flavorTendency), padding, 118, width - 40, 170, 22);
            float bodyHeight = Mathf.Max(70, Mathf.Ceil(explanation.preferredHeight) + 12);
            explanation.rectTransform.sizeDelta = new Vector2(width - 40, bodyHeight);
            float height = 118 + bodyHeight + 82;
            root.sizeDelta = new Vector2(width, height);
            MakeButton(root, "Keep exploring", padding, height - 64, width - 40, 44, EndPlayerInteraction);
        }
        private void IntroduceCup(string productId)
        {
            mode = "inspect";
            var menu = CafeRuntimeServices.Menu;
            var item = Array.Find(menu.GetMenu().items, x => x.id == productId);
            if (item == null) { ShowWelcomeChoices(); return; }
            int minimum = int.MaxValue; bool available = false;
            foreach (var offer in menu.GetMenuOffers())
            {
                if (offer.selection.MenuItemId != productId || !offer.available) continue;
                minimum = Math.Min(minimum, offer.price.unitPriceCents); available = true;
            }
            string flavor = item.beanIds.Length > 0
                ? CafeUiLocalization.Text(CafeRuntimeServices.Inventory.GetItemDefinition(item.beanIds[0]).flavorTendency) : "";
            string intro = CafeUiLocalization.Format("This is our {0}. {1}", "这是我们的{0}。{1}", "こちらは{0}です。{1}", DisplayEnglish(item.name), flavor);
            if (item.milkIds.Length > 0) intro += " " + CafeUiLocalization.Text("Made with milk; you can choose your bean and milk.");
            intro += " " + (available ? CafeUiLocalization.Format("Prices start at CNY {0}. Would you like a cup?", "当前 ¥{0} 起，要来一杯吗？", "現在 CNY {0} からです。一杯いかがですか？", (minimum / 100m).ToString("0.00"))
                : CafeUiLocalization.Text("This drink is sold out. Shall I help you choose another?"));
            if (reception.highlighter) reception.highlighter.HighlightProduct(productId);
            Speak(intro, () => {
                ActionWindow("Interested in this coffee?", 330, ShowWelcomeChoices);
                var order = MakeButton(root, "Order this cup", 28, 100, 360, 60, () => SelectCupForOrder(productId));
                order.interactable = available;
                if (!available) order.GetComponentInChildren<Text>().text = CafeUiLocalization.Text("Sold out");
                MakeButton(root, "Help me choose", 412, 100, 360, 60, OpenRecommendation);
                MakeButton(root, "Just browsing", 28, 195, 744, 60, EndPlayerInteraction);
            }, false);
        }
        private void SelectCupForOrder(string productId)
        {
            hasChosenDrink = true; browsingMenu = false;
            mode = "order"; recommendation.CancelSession(); StopVoice(); reception.OpenOrder();
            beanChosen = cupChosen = milkChosen = true; beanChoiceProduct = productId;
            reception.menuAdapter.SelectDrink(productId); reception.menuAdapter.SetQuantity(1);
            purchased = false; currentReceipt = null;
            menuReturn = ShowWelcomeChoices;
            if (reception.highlighter) reception.highlighter.HighlightProduct(productId);
            Speak("Good choice. Let's choose your cup size and ingredients.", ShowConfiguration);
        }
        private void ShowPreferenceInput()
        {
            browsingMenu = false;
            bool voiceMode = speech && speech.Active;
            ActionWindow("Tell your barista", voiceMode ? 480 : 360, ShowWelcomeChoices);
            var field = ChatSurface(root, "Preference input", 28, 95, 590, 100, true);
            input = field.gameObject.AddComponent<InputField>();
            input.textComponent = Label(field, "", 14, 10, 562, 80, 24);
            input.lineType = InputField.LineType.MultiLineNewline; input.characterLimit = 2000;
            input.text = draft;
            var placeholder = Label(field, "Taste, milk preference, budget...", 14, 10, 562, 80, 23);
            placeholder.color = new Color(.5f, .46f, .4f); input.placeholder = placeholder;
            send = MakeButton(root, "Send", 636, 95, 136, 100, () => Submit(false));
            MakeButton(root, "View menu", 28, 225, 220, 55, () => { menuReturn = ShowPreferenceInput; Speak("Of course. Here is our menu.", ShowDrinkMenu); });
            MakeButton(root, "Retry", 268, 225, 220, 55, () => SubmitRequest(true, true));
            status = Label(root, "", 28, 294, 744, 55, 19);
            if (voiceMode)
            {
                microphoneButton = MakeButton(root, "Speak", 28, 360, 360, 52, () => { if (!recommendation.IsBusy) speech.ToggleRecording(); });
                MakeButton(root, "Language", 412, 360, 360, 52, ShowVoiceLanguages);
                speechStatus = Label(root, "", 28, 420, 744, 48, 18);
            }
        }
        private void ShowRecommendationChoices()
        {
            if (recommendation.CurrentRecommendation == null) { ShowPreferenceInput(); return; }
            ActionWindow("What would you like to do?", 350, ShowPreferenceInput);
            var adapter = reception.menuAdapter;
            Label(root, DisplayEnglish(adapter.PriceText) + " / " + DisplayEnglish(adapter.StatusText), 28, 78, 744, 45, 24);
            MakeButton(root, "Choose this drink", 28, 140, 360, 60, ReviewOrder);
            MakeButton(root, "Adjust size / ingredients", 412, 140, 360, 60, ShowConfiguration);
            MakeButton(root, "Another recommendation", 28, 225, 360, 60, () => SubmitRequest(true, false));
            MakeButton(root, "Tell you more", 412, 225, 360, 60, ShowPreferenceInput);
        }
        private void ShowConfiguration()
        {
            browsingMenu = false;
            ActionWindow("Make it your own", 790, () => {
                if (mode == "recommendation" && recommendation.CurrentRecommendation != null) ShowRecommendationChoices();
                else { menuReturn = ShowWelcomeChoices; ShowDrinkMenu(); }
            });
            selectionText = Label(root, "", 28, 85, 744, 80, 24);
            options = Box(root, "Configuration", 28, 185, 740, 430);
            status = Label(root, "", 28, 620, 744, 42, 20);
            confirm = MakeButton(root, "Review order", 28, 685, 480, 60, ReviewOrder);
            MakeButton(root, "Change drink", 528, 685, 244, 60, () => { menuReturn = mode == "recommendation" ? (Action)ShowPreferenceInput : ShowWelcomeChoices; Speak("Let's choose another coffee.", ShowDrinkMenu); });
            dirty = true;
        }
        private void ReviewOrder()
        {
            var selection = reception.menuAdapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == selection.MenuItemId);
            var missing = CafeUiSelectionRules.MissingOptions(selection, item, cupChosen, beanChosen, milkChosen);
            if (missing.Length > 0) { ShowValidationPopup("Please select: " + string.Join(", ", missing)); return; }
            if (!CafeRuntimeServices.Menu.TryGetPrice(selection, out var quote, out var reason))
            { Speak(DisplayEnglish(reason), ShowConfiguration); return; }
            string summary = CafeUiLocalization.Format(
                "{0}, {1} ml. {2}, {3}. Your total is CNY {4}. Shall I place your order?",
                "{0}，{1} 毫升，{2}，{3}。合计 ¥{4}，现在下单吗？",
                "{0}、{1} ml、{2}、{3}。合計 CNY {4} です。注文を確定しますか？",
                DisplayEnglish(item.name), selection.CupSizeMl,
                Name(string.IsNullOrEmpty(selection.BeanId) ? item.beanIds[0] : selection.BeanId),
                string.IsNullOrEmpty(selection.MilkId) ? CafeUiLocalization.Text("No milk") : Name(selection.MilkId),
                (quote.totalPriceCents / 100m).ToString("0.00"));
            Speak(summary, () => {
                ActionWindow("Confirm your order", 320, ShowConfiguration);
                Label(root, summary, 28, 84, 744, 100, 24);
                status = Label(root, "", 28, 185, 744, 42, 19);
                confirm = MakeButton(root, "Confirm purchase", 28, 240, 360, 55, Purchase);
                MakeButton(root, "Go back and adjust", 412, 240, 360, 55, ShowConfiguration);
            });
        }
        private RectTransform ChatSurface(Transform parent, string name, float x, float y, float w, float h, bool customer)
        {
            var rect = Box(parent, name, x, y, w, h);
            var surface = rect.gameObject.AddComponent<Image>();
            surface.sprite = roundedCard; surface.type = Image.Type.Sliced;
            surface.color = customer ? new Color(.87f, .92f, .85f) : new Color(1, .99f, .96f);
            return rect;
        }
        private void Submit(bool alternate) { SubmitRequest(alternate, false); }
        private void SubmitRequest(bool alternate, bool retry)
        {
            if (mode != "recommendation" || purchasing || recommendation.IsBusy || (speech && speech.IsBusy)) return;
            string text = input ? input.text.Trim() : "";
            if (!alternate && text.Length == 0) return;
            if (retry && string.IsNullOrEmpty(lastSubmittedPreference))
            { if (status) status.text = CafeUiLocalization.Text("Send your preference before retrying."); return; }
            if (!alternate) lastSubmittedPreference = text;
            if (input) { input.text = ""; draft = ""; }
            messages.Add("You: " + (alternate ? "Another recommendation, please." : text));
            waitingForReply = true; dialogueActive = false;
            root.gameObject.SetActive(false);
            speechBubble.Show(CafeUiLocalization.Text("Let me check our menu and the latest stock for you..."));
            if (retry) recommendation.Retry();
            else if (alternate) recommendation.RecommendAnother();
            else recommendation.Submit(text);
        }
        private void Purchase()
        {
            if (purchased || purchasing || validationPopup ||
                recommendation.IsBusy || (speech && speech.Active && speech.IsBusy) ||
                (mode != "order" && mode != "recommendation"))
                return;

            if (mode == "recommendation" &&
                recommendation.CurrentRecommendation == null)
                return;

            var adapter = reception.menuAdapter;
            var source = adapter.CurrentSelection;

            var selection = new MenuSelection
            {
                MenuItemId = source.MenuItemId,
                CupSizeMl = source.CupSizeMl,
                BeanId = source.BeanId,
                MilkId = source.MilkId,
                Servings = source.Servings
            };
            // 必须在确认库存之前检查，不能付款后才发现无法制作。
            if (pendingMachineProduction)
            {
                ShowValidationPopup(
                    "Please close your confirmed receipt to start production."
                );
                return;
            }

            if (!machineProduction)
            {
                ShowValidationPopup(
                    "Production setup is incomplete."
                );
                return;
            }

            if (!machineProduction.CanAcceptOrder(
                    selection.Servings,
                    out string productionReason))
            {
                ShowValidationPopup(productionReason);
                return;
            }

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
                recentReceipt = receipt;

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
                status.text =
                    CafeUiLocalization.Text("Preparing your drink...");

            try
            {
                yield return null;

                // 保留原来的原料融合动画。
                if (orderAnimation && orderAnimation.isActiveAndEnabled)
                    yield return orderAnimation.Play();

                // 这里只记录待制作，不等待取餐。
                pendingMachineProduction = true;

                // 保留原来的机器人回复、凭据和取餐号。
                ShowCompletion();
            }
            finally
            {
                purchasing = false;
                orderCompletionRoutine = null;

                if (root && root.gameObject.activeSelf)
                    SetPointerInteraction(true);
            }
        }
        private void ShowCompletion()
        {
            mode = "completed";
            Speak(CafeUiLocalization.Format("Thank you! Your pickup number is {0}. Enjoy your coffee!", "谢谢！你的取餐号是 {0}，祝你享用愉快！", "ありがとうございます！受取番号は {0} です。ごゆっくりどうぞ！", currentReceipt.pickupNumber), ShowReceipt);
        }
        private void ShowReceipt()
        {
            mode = "completed";
            recommendation.CancelSession(); StopVoice();
            if (reception.highlighter) reception.highlighter.ClearHighlight();
            RenderReceipt(currentReceipt, ShowWelcomeChoices, false);
        }
        private void RenderReceipt(CafeOrderReceipt receipt, Action back, bool historical)
        {
            if (receipt == null) { back(); return; }
            ActionWindow(historical ? "Recent order" : "Order confirmed", 700, back);
            var card = ChatSurface(root, "Saved receipt", 24, 100, 752, 450, false);
            Label(card, historical ? "Your saved order. No new purchase has been made." : "Your drink is confirmed. Thank you! Keep this pickup number.", 24, 28, 704, 80, 27);
            Label(card, receipt.pickupNumber, 24, 130, 704, 90, 58).fontStyle = FontStyle.Bold;
            string name = receipt.drinkName;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == receipt.selection.MenuItemId);
            if (item != null) name = DisplayEnglish(item.name);
            Label(
                card,
                name + " / " + receipt.selection.CupSizeMl +
                " ml × " + receipt.selection.Servings + " cups",
                24, 240, 704, 45, 26
            );
            Label(card, "Total: CNY " + (receipt.totalPriceCents / 100m).ToString("0.00"), 24, 295, 704, 42, 26);
            if (DateTimeOffset.TryParse(receipt.confirmedAt, out var date))
                Label(card, date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 24, 365, 704, 32, 20);
            MakeButton(root, "Thanks, keep exploring", 24, 595, 752, 64, EndPlayerInteraction);
        }

        private void Update()
        {
            if (!player) player = FindObjectOfType<CafeDesktopPlayer>();
            if (customerBubble && player && !customerSpeechAnchor)
                customerBubble.fallbackPosition = player.transform.position + Vector3.up * 1.95f;
            if (player) EvaluateReception(player.transform.position, player.IsSeated);
            if (purchasing || dialogueActive || waitingForReply || !root.gameObject.activeSelf) return;
            if (mode != "order" && mode != "recommendation") return;
            bool voiceBusy = speech && speech.Active && speech.IsBusy;
            bool busy = recommendation.IsBusy || voiceBusy;
            if (microphoneButton)
            {
                microphoneButton.interactable = !recommendation.IsBusy && (!speech.IsBusy || speech.IsRecording);
                microphoneButton.GetComponentInChildren<Text>().text = CafeUiLocalization.Text(speech.IsRecording ? "Send / 结束并发送" : "Speak / 说话");
            }
            if (speechStatus)
            {
                string voiceMessage = speech.Status;
                if (speech.NeedsConfiguration) voiceMessage = "Voice input is unavailable. You can still type.";
                else if (speech.IsRecording) voiceMessage = "Recording... Tap again to send.";
                else if (speech.IsBusy) voiceMessage = "Recognizing your speech...";
                else if (voiceMessage.Contains("Qwen-ASR") || voiceMessage.StartsWith("Windows 语音输入") || voiceMessage.StartsWith("Ready /")) voiceMessage = "Tap Speak to start recording.";
                else if (voiceMessage.StartsWith("You: ")) voiceMessage = "";
                else if (voiceMessage.Contains("没有识别到") || voiceMessage.Contains("未识别到")) voiceMessage = "No speech recognized. Please try again or type.";
                else if (voiceMessage.Contains("失败") || voiceMessage.Contains("不可用") || voiceMessage.Contains("无法启动")) voiceMessage = "Voice input is unavailable. You can still type.";
                speechStatus.text = CafeUiLocalization.Text(voiceMessage);
                speechStatus.color = speech.NeedsConfiguration ? new Color(.75f, .18f, .1f) : new Color(.23f, .16f, .11f);
            }
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
            if (busy && status) status.text = CafeUiLocalization.Text("Checking the latest menu. Please wait...");
            else if (status && status.text == CafeUiLocalization.Text("Checking the latest menu. Please wait...")) status.text = mode == "recommendation" ? DisplayEnglish(recommendation.ErrorText) : "";
            foreach (var b in configurationButtons) if (b)
                b.interactable = !busy && !purchased && (!optionAvailability.TryGetValue(b, out bool allowed) || allowed);
            if (dirty && options) { dirty = false; DrawSelection(selected); }
        }
        private void LateUpdate()
        {
            if (!root || !root.gameObject.activeSelf) return;
            var canvasRect = TerminalCanvas.GetComponent<RectTransform>();
            Camera canvasCamera = TerminalCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : TerminalCanvas.worldCamera;
            Vector2 available = canvasRect.rect.size * .9f;
            if (available.x <= 0 || available.y <= 0) return;
            float fit = Mathf.Min(1, available.x / root.sizeDelta.x, available.y / root.sizeDelta.y);
            root.localScale = Vector3.one * fit;
            root.anchoredPosition = Vector2.zero;
            if (mode == "bean-info" && beanInspection && beanInspection.GetSelectedBounds(out var targetBounds))
            {
                float halfW = canvasRect.rect.width * .5f, halfH = canvasRect.rect.height * .5f;
                float cardHalfW = root.sizeDelta.x * fit * .5f, cardHalfH = root.sizeDelta.y * fit * .5f;
                Vector3 screen = customerCamera.WorldToScreenPoint(targetBounds.center);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, canvasCamera, out var point);
                float x = point.x >= 0 ? -halfW + cardHalfW + 28 : halfW - cardHalfW - 28;
                float y = -halfH + cardHalfH + 90;
                var projected = new Rect(point.x, point.y, 0, 0);
                Vector3[] corners = {
                    new Vector3(targetBounds.min.x,targetBounds.min.y,targetBounds.min.z), new Vector3(targetBounds.max.x,targetBounds.min.y,targetBounds.min.z),
                    new Vector3(targetBounds.min.x,targetBounds.max.y,targetBounds.min.z), new Vector3(targetBounds.max.x,targetBounds.max.y,targetBounds.min.z),
                    new Vector3(targetBounds.min.x,targetBounds.min.y,targetBounds.max.z), new Vector3(targetBounds.max.x,targetBounds.min.y,targetBounds.max.z),
                    new Vector3(targetBounds.min.x,targetBounds.max.y,targetBounds.max.z), new Vector3(targetBounds.max.x,targetBounds.max.y,targetBounds.max.z) };
                float minX=point.x, maxX=point.x, minY=point.y, maxY=point.y;
                foreach (var corner in corners)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, customerCamera.WorldToScreenPoint(corner), canvasCamera, out var p);
                    minX=Mathf.Min(minX,p.x); maxX=Mathf.Max(maxX,p.x); minY=Mathf.Min(minY,p.y); maxY=Mathf.Max(maxY,p.y);
                }
                projected = Rect.MinMaxRect(minX - 18, minY - 18, maxX + 18, maxY + 18);
                if (projected.Overlaps(new Rect(x-cardHalfW,y-cardHalfH,cardHalfW*2,cardHalfH*2)))
                    y = halfH - cardHalfH - 28;
                root.anchoredPosition = new Vector2(x, Mathf.Clamp(y, -Mathf.Max(0,halfH-cardHalfH-20), Mathf.Max(0,halfH-cardHalfH-20)));
            }
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
                selectionText.text = currentReceipt.drinkName + " · " + currentReceipt.selection.CupSizeMl + "ml / " + CafeUiLocalization.Text("Confirmed");
                Label(options, "Your pickup number: " + currentReceipt.pickupNumber, 0, 5, 750, 65, 38);
                Label(options, "Keep this number. You may take a seat.", 0, 70, 750, 35, 22);
                confirm.GetComponentInChildren<Text>().text = CafeUiLocalization.Text("Order complete");
                return;
            }
            confirm.GetComponentInChildren<Text>().text = CafeUiLocalization.Text("Review order");
            if (!selected) { selectionText.text = CafeUiLocalization.Text("Talk to the barista to complete your choice. Confirm your drink only when you are ready."); return; }
            var adapter = reception.menuAdapter; var s = adapter.CurrentSelection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == s.MenuItemId);
            if (item == null) return;
            if (beanChoiceProduct != s.MenuItemId) { beanChoiceProduct = s.MenuItemId; beanChosen = cupChosen = milkChosen = true; }
            selectionText.text = DisplayEnglish(item.name) + " × " + s.Servings + " · " + (cupChosen ? s.CupSizeMl + "ml" : "Select a cup size") + " · " + adapter.PriceText + "\n" +
                (beanChosen ? Name(string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId) : "Select a coffee bean") +
                (item.milkIds.Length == 0 ? " / No milk" : !milkChosen ? " / Select milk" : " · " + Name(s.MilkId)) + " · " + (cupChosen && beanChosen && (item.milkIds.Length == 0 || milkChosen) ? DisplayEnglish(adapter.StatusText) : "Selection incomplete");
            selectionText.text = CafeUiLocalization.Text(selectionText.text);
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
            // 数量：放在杯型按钮右侧。
            int maxServings = machineProduction
                ? machineProduction.OrderCapacity
                : 1;

            Label(options, "Qty", 432, 0, 60, 48, 20)
                .alignment = TextAnchor.MiddleLeft;

            var minusButton = MakeButton(
                options, "-", 496, 0, 52, 48,
                () =>
                {
                    int quantity = adapter.CurrentSelection.Servings;

                    if (quantity > 1)
                    {
                        adapter.SetQuantity(quantity - 1);
                        if (status) status.text = "";
                        dirty = true;
                    }
                }
            );

            bool canDecrease = s.Servings > 1;
            minusButton.interactable = canDecrease;
            configurationButtons.Add(minusButton);
            optionAvailability[minusButton] = canDecrease;

            Label(
                options,
                s.Servings.ToString(),
                552, 0, 64, 48, 24
            ).alignment = TextAnchor.MiddleCenter;

            var plusButton = MakeButton(
                options, "+", 620, 0, 52, 48,
                () =>
                {
                    int quantity = adapter.CurrentSelection.Servings;

                    if (quantity < maxServings)
                    {
                        adapter.SetQuantity(quantity + 1);
                        if (status) status.text = "";
                        dirty = true;
                    }
                }
            );

            configurationButtons.Add(plusButton);

            if (s.Servings >= maxServings)
            {
                plusButton.interactable = false;
                optionAvailability[plusButton] = false;
            }
            else
            {
                var quantityCandidate = CopySelection(s);
                quantityCandidate.Servings = s.Servings + 1;
                bool available = CafeRuntimeServices.Menu.IsAvailable(
                    quantityCandidate,
                    out string quantityReason
                );

                Debug.Log(
                    $"[Quantity] 杯数={quantityCandidate.Servings}, " +
                    $"可制作={available}, 原因={quantityReason}"
                );

                // 数量按钮直接检查整单库存，
                // 不走杯型、豆、奶的通用选项判断。
                bool canAddCup = CafeRuntimeServices.Menu.IsAvailable(
                    quantityCandidate,
                    out string addCupReason
                );

                plusButton.interactable = canAddCup;
                optionAvailability[plusButton] = canAddCup;

                // 保持按钮显示“＋”，不追加“售罄”文字。
                plusButton.GetComponentInChildren<Text>().text = "+";

                if (!canAddCup)
                {
                    Debug.Log(
                        "[Quantity] 无法增加一杯：" + addCupReason
                    );
                }
            }
            Label(options, "Beans", 0, 68, 90, 48, 22).alignment = TextAnchor.MiddleLeft;
            x = 100;
            string activeBean = string.IsNullOrEmpty(s.BeanId) ? item.beanIds[0] : s.BeanId;
            foreach (string bean in item.beanIds)
            {
                string id = bean;
                var button = MakeButton(options, BeanOptionName(id), x, 68, 200, 48, () =>
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
                var beanLabel = button.GetComponentInChildren<Text>();
                beanLabel.rectTransform.anchoredPosition = new Vector2(10, -4);
                beanLabel.rectTransform.sizeDelta = new Vector2(180, 40);
                beanLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                beanLabel.resizeTextMinSize = 16; beanLabel.resizeTextMaxSize = 22;
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
            label.text += "\n" + CafeUiLocalization.Text("Sold out"); label.fontSize = 17; label.resizeTextMaxSize = 17; label.resizeTextMinSize = 14;
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
                string priceText = price == int.MaxValue ? CafeUiLocalization.Text("Price unavailable") : "¥" + (price / 100m).ToString("0.00") + "+";
                button.GetComponentInChildren<Text>().text = DisplayEnglish(item.name);
                var priceLabel = button.transform.Find("MenuPrice");
                if (priceLabel) priceLabel.GetComponent<Text>().text = anyAvailable
                    ? priceText : CafeUiLocalization.Text("Sold out");
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
                if (text.Contains("LLM Settings")) return CafeUiLocalization.Text("Check your connection in Virtual Cafe > LLM Settings.");
                return CafeUiLocalization.Text("Unable to complete this action. Check your options, stock or connection and retry.");
            }
            return CafeUiLocalization.Text(text);
        }
        private string BeanOptionName(string id)
        {
            switch (id)
            {
                case "bean-blue-mountain": return CafeUiLocalization.Text("Blue Mountain");
                case "bean-geisha": return CafeUiLocalization.Text("Geisha");
                case "bean-kona": return CafeUiLocalization.Text("Kona");
                default: return Name(id);
            }
        }
        private string Name(string id) => DisplayEnglish(CafeRuntimeServices.Inventory.GetItemDefinition(id).name);
        private static Sprite CreateRoundedCard()
        {
            const int size = 64, radius = 14;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - .5f, x + .5f - (size - radius));
                    float dy = Mathf.Max(radius - y - .5f, y + .5f - (size - radius));
                    float distance = new Vector2(Mathf.Max(0, dx), Mathf.Max(0, dy)).magnitude;
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - distance + .5f)));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
        private RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        private Text Label(Transform parent, string value, float x, float y, float w, float h, int size)
        {
            var text = Box(parent, "Text", x, y, w, h).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.text = CafeUiLocalization.Text(value); text.color = new Color(.23f, .16f, .11f);
            text.supportRichText = false; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        private Button MakeButton(Transform parent, string value, float x, float y, float w, float h, UnityEngine.Events.UnityAction action)
        {
            var rt = Box(parent, value, x, y, w, h); var image = rt.gameObject.AddComponent<Image>(); image.sprite = roundedCard; image.type = Image.Type.Sliced; image.color = new Color(.88f, .78f, .63f);
            var b = rt.gameObject.AddComponent<Button>(); b.onClick.AddListener(action);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = Label(rt, value, 3, 0, w - 6, h, 24); text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 24; return b;
        }
    }
}
