using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VirtualCafe.Inventory;
using VirtualCafe.Recommendation;

public sealed class CafeMachineProduction : MonoBehaviour
{
    [Header("Machine parts")]
    public Transform productionCup;
    public GameObject coffeeStream;
    public Transform coffeeSurface;

    [Header("Pickup")]
    public Transform pickupAnchor;
    public Transform player;
    public float pickupDistance = 1.8f;
    public Transform[] pickupSlots = new Transform[0];

    [Header("Carried order")]
    public CafeCarriedDrinks carriedDrinks;

    [Header("Animation")]
    public float brewingDuration = 3f;
    public float transferDuration = 1.2f;
    public float liquidRiseHeight = 0.08f;
    public float transferArcHeight = 0.2f;

    [Header("Finished cup models")]
    public Transform blueMountainCup;
    public Transform geishaCup;
    public Transform konaCup;
    public Transform latteCup;

    [Header("Cup size")]
    [Tooltip("400 ml 成品杯的整体高度，包含模型自带的碟子。")]
    [Min(0.01f)]
    public float smallCupHeight = 0.12f;

    [Tooltip("500 ml 相对于 400 ml 的外观缩放倍率。")]
    [Min(1f)]
    public float largeCupScale = 1.15f;

    [Header("Optional sound")]
    public AudioSource audioSource;
    public AudioClip brewingSound;
    public AudioClip readySound;

    [Header("Grinder animation")]
    public CafeGrinderAnimation grinderAnimation;

    [Header("Grounds transfer animation")]
    public CafeGroundsTransferAnimation groundsTransferAnimation;

    [Header("Grounds pouring animation")]
    public CafeGroundsPourAnimation groundsPourAnimation;

    [Header("Bean pouring animation")]
    public CafeBeanPourAnimation beanPourAnimation;

    [Header("Milk pouring animation")]
    public CafeMilkPourAnimation milkPourAnimation;

    public bool IsBusy { get; private set; }

    private Vector3 cupStartPosition;
    private Vector3 surfaceStartPosition;

    private Quaternion cupStartRotation;
    private Quaternion surfaceStartRotation;

    private bool initialized;
    private Coroutine routine;

    private readonly List<GameObject> finishedCups =
        new List<GameObject>();

    private string message = "";
    private string pickupNumber = "";

    private int totalCups;
    private float noticeStartedAt;

    private Font font;
    private GUIStyle titleStyle;
    private GUIStyle numberStyle;

    private sealed class CupSpec
    {
        public int size;
        public string label;
        public string beanId;
        public bool isLatte;
    }

    private static string L(
        string en,
        string zh,
        string ja,
        params object[] args)
    {
        return CafeUiLocalization.Format(en, zh, ja, args);
    }

    public int OrderCapacity => GetSlots().Count;

    private List<Transform> GetSlots()
    {
        var result = new List<Transform>();

        if (pickupSlots != null)
        {
            foreach (var slot in pickupSlots)
            {
                if (slot && !result.Contains(slot))
                    result.Add(slot);
            }
        }

        if (result.Count == 0 &&
            (pickupSlots == null || pickupSlots.Length == 0) &&
            pickupAnchor)
        {
            result.Add(pickupAnchor);
        }

        return result;
    }

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized || !productionCup || !coffeeSurface)
            return;

        cupStartPosition = productionCup.position;
        cupStartRotation = productionCup.rotation;

        surfaceStartPosition = coffeeSurface.position;
        surfaceStartRotation = coffeeSurface.rotation;

        initialized = true;

        HideParts();
    }

    public bool IsConfigured()
    {
        return productionCup &&
               coffeeStream &&
               coffeeSurface &&
               pickupAnchor &&
               player &&
               OrderCapacity > 0;
    }

    public bool CanAcceptOrder(int servings, out string reason)
    {
        if (!carriedDrinks)
        {
            reason = L(
                "Pickup storage is not configured.",
                "尚未绑定随身订单组件。",
                "受取用の設定がありません。");

            return false;
        }

        int remaining = carriedDrinks.RemainingCapacity;

        if (servings > remaining)
        {
            reason = L(
                "You are carrying {0} cups. You can order up to {1} more. Place drinks on a table to free up space.",
                "你已随身携带 {0} 杯，最多还能点 {1} 杯。可先将饮品放到桌上腾出容量。",
                "現在 {0} 杯持っています。追加注文は {1} 杯までです。テーブルに置くと空きができます。",
                carriedDrinks.CarriedCupCount,
                remaining);

            return false;
        }

        if (!isActiveAndEnabled || !IsConfigured())
        {
            reason = L(
                "Production setup is incomplete.",
                "制作组件绑定不完整。",
                "製作設定が不完全です。");

            return false;
        }

        if (IsBusy || routine != null)
        {
            reason = L(
                "Collect your previous order first.",
                "请先领取上一笔订单。",
                "前の注文を先に受け取ってください。");

            return false;
        }

        if (servings < 1 || servings > OrderCapacity)
        {
            reason = L(
                "Choose 1 to {0} cups in total.",
                "整单请选择 1 至 {0} 杯。",
                "注文は合計 1 ～ {0} 杯です。",
                OrderCapacity);

            return false;
        }

        reason = "Success";
        return true;
    }

    public bool BeginProduction(string number)
    {
        return BeginProduction(number, 1);
    }

    public bool BeginProduction(string number, int servings)
    {
        if (!CanAcceptOrder(servings, out _))
            return false;

        var specs = new List<CupSpec>();

        for (int i = 0; i < servings; i++)
        {
            specs.Add(new CupSpec
            {
                size = 400,
                label = "",
                beanId = "",
                isLatte = false
            });
        }

        return Begin(number, specs);
    }

    public bool BeginProduction(CafeOrderReceipt receipt)
    {
        if (receipt == null)
            return false;

        int count;

        try
        {
            count = receipt.TotalServings;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (!CanAcceptOrder(count, out _))
            return false;

        var specs = new List<CupSpec>();

        foreach (var line in receipt.GetLines())
        {
            if (line == null ||
                line.selection == null ||
                line.selection.Servings <= 0)
            {
                return false;
            }

            var selection = line.selection;

            var item = Array.Find(
                CafeRuntimeServices.Menu.GetMenu().items,
                x => x.id == selection.MenuItemId);

            if (item == null)
                return false;

            bool isLatte = item.recipeId == "latte";

            string label = isLatte
                ? CafeUiLocalization.Text("Latte")
                : CafeUiLocalization.Text("Black coffee");

            string bean = selection.BeanId;

            if (string.IsNullOrEmpty(bean) &&
                item.beanIds != null &&
                item.beanIds.Length == 1)
            {
                bean = item.beanIds[0];
            }

            label += " · " + IngredientName(bean);

            if (!string.IsNullOrEmpty(selection.MilkId))
            {
                label += " · " + IngredientName(selection.MilkId);
            }

            label += " · " + selection.CupSizeMl + " ml";

            for (int i = 0; i < selection.Servings; i++)
            {
                specs.Add(new CupSpec
                {
                    size = selection.CupSizeMl,
                    label = label,
                    beanId = bean,
                    isLatte = isLatte
                });
            }
        }

        return Begin(receipt.pickupNumber, specs);
    }

    private string IngredientName(string id)
    {
        switch (id)
        {
            case "bean-blue-mountain":
                return CafeUiLocalization.Text("Blue Mountain");

            case "bean-geisha":
                return CafeUiLocalization.Text("Geisha");

            case "bean-kona":
                return CafeUiLocalization.Text("Kona");

            case "milk-whole":
                return CafeUiLocalization.Text("Whole milk");

            case "milk-skim":
                return CafeUiLocalization.Text("Skim milk");

            case "milk-oat":
                return CafeUiLocalization.Text("Oat milk");

            default:
                return string.IsNullOrEmpty(id)
                    ? ""
                    : CafeUiLocalization.Text(
                        CafeRuntimeServices.Inventory
                            .GetItemDefinition(id).name);
        }
    }

    private bool Begin(string number, List<CupSpec> specs)
    {
        Initialize();

        if (!initialized || !CanAcceptOrder(specs.Count, out _))
            return false;

        pickupNumber = number;
        totalCups = specs.Count;
        IsBusy = true;

        SetMessage(L(
            "Preparing your order...",
            "正在准备订单…",
            "注文を準備しています…"));

        routine = StartCoroutine(Produce(specs, GetSlots()));

        return true;
    }

    private IEnumerator Produce(
        List<CupSpec> specs,
        List<Transform> slots)
    {
        try
        {
            for (int i = 0; i < specs.Count; i++)
            {
                SetMessage(L(
                    "Adding coffee beans {0} / {1}",
                    "正在加入咖啡豆，第 {0} / {1} 杯",
                    "コーヒー豆を入れています {0} / {1}",
                    i + 1,
                    specs.Count));

                if (beanPourAnimation)
                    yield return beanPourAnimation.PlayPour(specs[i].beanId);

                SetMessage(L(
                    "Grinding beans {0} / {1}",
                    "正在磨豆，第 {0} / {1} 杯",
                    "豆を挽いています {0} / {1}",
                    i + 1,
                    specs.Count));

                if (grinderAnimation)
                    yield return grinderAnimation.Grind();

                SetMessage(L(
                    "Transferring coffee grounds...",
                    "正在收集咖啡粉…",
                    "コーヒー粉を移しています…"));

                if (groundsTransferAnimation)
                    yield return groundsTransferAnimation.PlayTransfer();

                SetMessage(L(
                    "Loading coffee grounds...",
                    "正在向滤篮装入咖啡粉…",
                    "フィルターにコーヒー粉を入れています…"));

                if (groundsPourAnimation)
                    yield return groundsPourAnimation.PlayPour();

                RestoreParts();

                productionCup.gameObject.SetActive(true);
                coffeeSurface.gameObject.SetActive(true);
                coffeeStream.SetActive(false);

                SetMessage(L(
                    "Preparing {0} / {1}\n{2}",
                    "正在制作第 {0} / {1} 杯\n{2}",
                    "{0} / {1} 杯目を製作中\n{2}",
                    i + 1,
                    specs.Count,
                    specs[i].label));

                Vector3 empty =
                    surfaceStartPosition -
                    Vector3.up * Mathf.Max(0f, liquidRiseHeight);

                coffeeSurface.position = empty;

                yield return new WaitForSecondsRealtime(0.3f);

                coffeeStream.SetActive(true);

                if (audioSource && brewingSound)
                {
                    audioSource.clip = brewingSound;
                    audioSource.loop = true;
                    audioSource.Play();
                }

                float elapsed = 0f;
                float seconds = Mathf.Max(0.1f, brewingDuration);

                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;

                    coffeeSurface.position = Vector3.Lerp(
                        empty,
                        surfaceStartPosition,
                        Mathf.Clamp01(elapsed / seconds));

                    yield return null;
                }

                coffeeSurface.position = surfaceStartPosition;

                coffeeStream.SetActive(false);
                StopSound();

                // 只有拿铁播放倒奶动画。
                if (specs[i].isLatte && milkPourAnimation)
                {
                    SetMessage(L(
                        "Adding milk...",
                        "正在加入牛奶…",
                        "ミルクを注いでいます…"));

                    yield return milkPourAnimation.Pour();
                }

                GameObject cup = CreateCup(specs[i]);
                finishedCups.Add(cup);

                HideParts();

                if (!slots[i])
                {
                    throw new InvalidOperationException(
                        "Pickup position removed during production.");
                }

                yield return MoveCup(
                    cup.transform,
                    slots[i].position);
            }

            SetMessage(L(
                "Your entire order is ready. Please collect it at the pickup counter.",
                "整单已制作完成，请到取餐区领取。",
                "すべてできました。受取カウンターへお越しください。"));

            if (audioSource && readySound)
                audioSource.PlayOneShot(readySound);

            yield return null;

            bool? previousNearby = null;

            while (true)
            {
                if (!player || !pickupAnchor)
                    yield break;

                Vector3 offset =
                    player.position - pickupAnchor.position;

                offset.y = 0f;

                float radius = Mathf.Max(0f, pickupDistance);
                bool nearby =
                    offset.sqrMagnitude <= radius * radius;

                if (previousNearby != nearby)
                {
                    SetMessage(nearby
                        ? L(
                            "Press E to collect your entire order",
                            "按 E 领取整单饮品",
                            "Eキーで注文をすべて受け取る")
                        : L(
                            "Your entire order is ready. Please collect it at the pickup counter.",
                            "整单已制作完成，请到取餐区领取。",
                            "すべてできました。受取カウンターへお越しください。"));

                    previousNearby = nearby;
                }

                if (nearby && Input.GetKeyDown(KeyCode.E))
                {
                    if (!carriedDrinks)
                    {
                        SetMessage(L(
                            "Pickup storage is not configured.",
                            "尚未绑定随身订单组件。",
                            "受取用の設定がありません。"));
                    }
                    else
                    {
                        bool received = carriedDrinks.TryReceive(
                            pickupNumber,
                            finishedCups.ToArray());

                        if (received)
                        {
                            // 管理权已交给随身订单组件。
                            finishedCups.Clear();
                            break;
                        }

                        SetMessage(L(
                            "Unable to collect this order. Please try again.",
                            "暂时无法领取这笔订单，请重试。",
                            "注文を受け取れませんでした。もう一度お試しください。"));
                    }
                }

                // 领取失败也等待下一帧，避免循环卡住。
                yield return null;
            }

            ClearCups();

            SetMessage(L(
                "Collected {0} cups. Enjoy!",
                "已领取 {0} 杯，祝你享用愉快！",
                "{0} 杯受け取りました。お楽しみください！",
                specs.Count));

            yield return new WaitForSecondsRealtime(1.5f);
        }
        finally
        {
            if (milkPourAnimation)
                milkPourAnimation.ResetAnimation();

            if (beanPourAnimation)
                beanPourAnimation.ResetAnimation();

            if (groundsPourAnimation)
                groundsPourAnimation.ResetAnimation();

            if (groundsTransferAnimation)
                groundsTransferAnimation.ResetAnimation();
            StopSound();
            HideParts();
            RestoreParts();
            ClearCups();

            IsBusy = false;
            routine = null;
            message = "";
        }
    }

    private Transform GetCupTemplate(CupSpec spec)
    {
        // 拿铁使用带盖纸杯。
        if (spec.isLatte)
            return latteCup ? latteCup : productionCup;

        // 黑咖啡按咖啡豆选择杯子。
        switch (spec.beanId)
        {
            case "bean-blue-mountain":
                return blueMountainCup
                    ? blueMountainCup
                    : productionCup;

            case "bean-geisha":
                return geishaCup
                    ? geishaCup
                    : productionCup;

            case "bean-kona":
                return konaCup
                    ? konaCup
                    : productionCup;

            default:
                return productionCup;
        }
    }

    private GameObject CreateCup(CupSpec spec)
    {
        var root = new GameObject(
            "Pickup_" +
            pickupNumber +
            "_" +
            (finishedCups.Count + 1));

        Transform template = GetCupTemplate(spec);
        Transform visual = CopyPart(template, root.transform);

        if (template != productionCup)
        {
            // 使用预览模型自身的朝向，
            // 避免继承预览父节点朝向后产生倾斜。
            visual.rotation = template.localRotation;
        }

        // 使用原制作杯时，同时复制独立液面。
        if (template == productionCup &&
            coffeeSurface != productionCup &&
            !coffeeSurface.IsChildOf(productionCup))
        {
            CopyPart(coffeeSurface, root.transform);
        }

        // 将根节点对齐到杯子或碟子的底部中心。
        Vector3 bottom = FindBottom(root.transform);

        foreach (Transform child in root.transform)
            child.position -= bottom;

        // 统一基础高度，消除预览父节点缩放的影响。
        float scale = 1f;

        if (TryGetVisibleBounds(root.transform, out Bounds bounds) &&
            bounds.size.y > 0.0001f)
        {
            scale =
                Mathf.Max(0.01f, smallCupHeight) /
                bounds.size.y;
        }

        // 500 ml 在基础尺寸上整体放大。
        if (spec.size == 500)
            scale *= Mathf.Max(1f, largeCupScale);

        root.transform.localScale = Vector3.one * scale;

        // 从机器制作位置移动到取餐区。
        root.transform.position = FindBottom(productionCup);

        return root;
    }

    private static Transform CopyPart(
        Transform source,
        Transform parent)
    {
        GameObject copy = Instantiate(source.gameObject);

        copy.transform.SetParent(parent, false);

        copy.transform.SetPositionAndRotation(
            source.position,
            source.rotation);

        copy.transform.localScale = source.lossyScale;
        copy.SetActive(true);

        return copy.transform;
    }

    private IEnumerator MoveCup(
        Transform cup,
        Vector3 destination)
    {
        Vector3 start = cup.position;
        Vector3 movement = destination - FindBottom(cup);

        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, transferDuration);

        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);

            cup.position =
                start +
                movement * Mathf.SmoothStep(0f, 1f, t) +
                Vector3.up *
                Mathf.Sin(t * Mathf.PI) *
                transferArcHeight;

            yield return null;
        }

        cup.position = start + movement;
    }

    private static bool TryGetVisibleBounds(
        Transform target,
        out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;

        foreach (var renderer in
                 target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer.GetComponent<TextMesh>())
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static Vector3 FindBottom(Transform target)
    {
        if (TryGetVisibleBounds(target, out Bounds bounds))
        {
            return new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);
        }

        return target.position;
    }

    private void RestoreParts()
    {
        if (!initialized)
            return;

        if (productionCup)
        {
            productionCup.SetPositionAndRotation(
                cupStartPosition,
                cupStartRotation);
        }

        if (coffeeSurface)
        {
            coffeeSurface.SetPositionAndRotation(
                surfaceStartPosition,
                surfaceStartRotation);
        }
    }

    private void HideParts()
    {
        if (coffeeStream)
            coffeeStream.SetActive(false);

        if (coffeeSurface)
            coffeeSurface.gameObject.SetActive(false);

        if (productionCup)
            productionCup.gameObject.SetActive(false);
    }

    private void ClearCups()
    {
        foreach (var cup in finishedCups)
        {
            if (!cup)
                continue;

            cup.SetActive(false);
            Destroy(cup);
        }

        finishedCups.Clear();
    }

    private void StopSound()
    {
        if (!audioSource)
            return;

        audioSource.Stop();
        audioSource.loop = false;
    }

    private void SetMessage(string value)
    {
        if (message == value)
            return;

        message = value;
        noticeStartedAt = Time.unscaledTime;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (titleStyle == null)
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[]
                {
                    "Microsoft YaHei",
                    "Yu Gothic",
                    "Noto Sans CJK JP",
                    "Arial"
                },
                24);

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            numberStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            if (font)
            {
                titleStyle.font = font;
                numberStyle.font = font;
            }

            Color textColor = new Color(0.25f, 0.21f, 0.17f);

            titleStyle.normal.textColor = textColor;
            numberStyle.normal.textColor = textColor;
        }

        float scale = Mathf.Clamp(
            Screen.width / 1280f,
            0.65f,
            1.5f);

        float t = Mathf.Clamp01(
            (Time.unscaledTime - noticeStartedAt) / 0.35f);

        float pop = Mathf.Lerp(
            0.94f,
            1f,
            Mathf.SmoothStep(0f, 1f, t));

        float width = 660f * scale * pop;
        float height = 132f * scale * pop;

        var panel = new Rect(
            (Screen.width - width) * 0.5f,
            Mathf.Lerp(10f, 30f, t) * scale,
            width,
            height);

        Color oldColor = GUI.color;

        GUI.color = new Color(
            0.97f,
            0.94f,
            0.86f,
            t * 0.97f);

        GUI.DrawTexture(panel, Texture2D.whiteTexture);

        GUI.color = new Color(1f, 1f, 1f, t);

        titleStyle.fontSize = Mathf.RoundToInt(20f * scale);
        numberStyle.fontSize = Mathf.RoundToInt(17f * scale);

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + 8f,
                panel.width - 24f,
                panel.height * 0.65f),
            message,
            titleStyle);

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + panel.height * 0.70f,
                panel.width - 24f,
                panel.height * 0.25f),
            L(
                "Pickup: {0} | {1} cups",
                "取餐号：{0}　共 {1} 杯",
                "受取番号：{0}　合計 {1} 杯",
                pickupNumber,
                totalCups),
            numberStyle);

        GUI.color = oldColor;
    }

    private void OnDisable()
    {
        if (milkPourAnimation)
            milkPourAnimation.ResetAnimation();

        if (beanPourAnimation)
            beanPourAnimation.ResetAnimation();

        if (groundsPourAnimation)
            groundsPourAnimation.ResetAnimation();

        if (groundsTransferAnimation)
            groundsTransferAnimation.ResetAnimation();
        StopAllCoroutines();
        StopSound();
        HideParts();
        ClearCups();
        RestoreParts();

        routine = null;
        IsBusy = false;
        message = "";
    }

    private void OnDestroy()
    {
        if (font)
            Destroy(font);
    }
}