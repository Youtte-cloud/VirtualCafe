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
    [Header("Animation")]
    public float brewingDuration = 3f;
    public float transferDuration = 1.2f;
    public float liquidRiseHeight = .08f;
    public float transferArcHeight = .2f;
    [Header("Cup size")]
    public float largeCupScale = 1.08f;
    [Header("Optional sound")]
    public AudioSource audioSource;
    public AudioClip brewingSound;
    public AudioClip readySound;
    public bool IsBusy { get; private set; }

    private Vector3 cupStartPosition, surfaceStartPosition;
    private Quaternion cupStartRotation, surfaceStartRotation;
    private bool initialized;
    private Coroutine routine;
    private readonly List<GameObject> finishedCups = new List<GameObject>();
    private string message = "", pickupNumber = "";
    private int totalCups;
    private float noticeStartedAt;
    private Font font;
    private GUIStyle titleStyle, numberStyle;
    private sealed class CupSpec
    {
        public int size;
        public string label;
    }
    private static string L(string en, string zh, string ja, params object[] args)
        => CafeUiLocalization.Format(en, zh, ja, args);

    public int OrderCapacity => GetSlots().Count;
    private List<Transform> GetSlots()
    {
        var result = new List<Transform>();
        if (pickupSlots != null)
            foreach (var slot in pickupSlots)
                if (slot && !result.Contains(slot)) result.Add(slot);
        if (result.Count == 0 && (pickupSlots == null || pickupSlots.Length == 0) && pickupAnchor)
            result.Add(pickupAnchor);
        return result;
    }
    private void Start() { Initialize(); }
    private void Initialize()
    {
        if (initialized || !productionCup || !coffeeSurface) return;
        cupStartPosition = productionCup.position;
        cupStartRotation = productionCup.rotation;
        surfaceStartPosition = coffeeSurface.position;
        surfaceStartRotation = coffeeSurface.rotation;
        initialized = true;
        HideParts();
    }
    public bool IsConfigured() => productionCup && coffeeStream && coffeeSurface &&
        pickupAnchor && player && OrderCapacity > 0;
    public bool CanAcceptOrder(int servings, out string reason)
    {
        if (!isActiveAndEnabled || !IsConfigured())
        { reason = L("Production setup is incomplete.", "制作组件绑定不完整。", "製作設定が不完全です。"); return false; }
        if (IsBusy || routine != null)
        { reason = L("Collect your previous order first.", "请先领取上一笔订单。", "前の注文を先に受け取ってください。"); return false; }
        if (servings < 1 || servings > OrderCapacity)
        { reason = L("Choose 1 to {0} cups in total.", "整单请选择 1 至 {0} 杯。", "注文は合計 1 ～ {0} 杯です。", OrderCapacity); return false; }
        reason = "Success"; return true;
    }
    public bool BeginProduction(string number) => BeginProduction(number, 1);
    public bool BeginProduction(string number, int servings)
    {
        if (!CanAcceptOrder(servings, out _)) return false;
        var specs = new List<CupSpec>();
        for (int i = 0; i < servings; i++) specs.Add(new CupSpec { size = 400, label = "" });
        return Begin(number, specs);
    }
    public bool BeginProduction(CafeOrderReceipt receipt)
    {
        if (receipt == null) return false;
        var specs = new List<CupSpec>();
        int count;
        try { count = receipt.TotalServings; }
        catch (OverflowException) { return false; }
        if (!CanAcceptOrder(count, out _)) return false;
        foreach (var line in receipt.GetLines())
        {
            if (line == null || line.selection == null || line.selection.Servings <= 0) return false;
            var s = line.selection;
            var item = Array.Find(CafeRuntimeServices.Menu.GetMenu().items, x => x.id == s.MenuItemId);
            if (item == null) return false;
            string label = item.recipeId == "latte" ? CafeUiLocalization.Text("Latte") : CafeUiLocalization.Text("Black coffee");
            string bean = s.BeanId;
            if (string.IsNullOrEmpty(bean) && item.beanIds.Length == 1) bean = item.beanIds[0];
            label += " · " + IngredientName(bean);
            if (!string.IsNullOrEmpty(s.MilkId)) label += " · " + IngredientName(s.MilkId);
            label += " · " + s.CupSizeMl + " ml";
            for (int i = 0; i < s.Servings; i++) specs.Add(new CupSpec { size = s.CupSizeMl, label = label });
        }
        return Begin(receipt.pickupNumber, specs);
    }
    private string IngredientName(string id)
    {
        switch (id)
        {
            case "bean-blue-mountain": return CafeUiLocalization.Text("Blue Mountain");
            case "bean-geisha": return CafeUiLocalization.Text("Geisha");
            case "bean-kona": return CafeUiLocalization.Text("Kona");
            case "milk-whole": return CafeUiLocalization.Text("Whole milk");
            case "milk-skim": return CafeUiLocalization.Text("Skim milk");
            case "milk-oat": return CafeUiLocalization.Text("Oat milk");
            default:
                return string.IsNullOrEmpty(id) ? "" : CafeUiLocalization.Text(CafeRuntimeServices.Inventory.GetItemDefinition(id).name);
        }
    }
    private bool Begin(string number, List<CupSpec> specs)
    {
        Initialize();
        if (!initialized || !CanAcceptOrder(specs.Count, out _)) return false;
        pickupNumber = number; totalCups = specs.Count; IsBusy = true;
        SetMessage(L("Preparing your order...", "正在准备订单…", "注文を準備しています…"));
        routine = StartCoroutine(Produce(specs, GetSlots()));
        return true;
    }
    private IEnumerator Produce(List<CupSpec> specs, List<Transform> slots)
    {
        try
        {
            for (int i = 0; i < specs.Count; i++)
            {
                RestoreParts();
                productionCup.gameObject.SetActive(true);
                coffeeSurface.gameObject.SetActive(true);
                coffeeStream.SetActive(false);
                SetMessage(L("Preparing {0} / {1}\n{2}", "正在制作第 {0} / {1} 杯\n{2}",
                    "{0} / {1} 杯目を製作中\n{2}", i + 1, specs.Count, specs[i].label));
                var empty = surfaceStartPosition - Vector3.up * Mathf.Max(0, liquidRiseHeight);
                coffeeSurface.position = empty;
                yield return new WaitForSecondsRealtime(.3f);
                coffeeStream.SetActive(true);
                if (audioSource && brewingSound)
                { audioSource.clip = brewingSound; audioSource.loop = true; audioSource.Play(); }
                float elapsed = 0, seconds = Mathf.Max(.1f, brewingDuration);
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    coffeeSurface.position = Vector3.Lerp(empty, surfaceStartPosition, Mathf.Clamp01(elapsed / seconds));
                    yield return null;
                }
                coffeeSurface.position = surfaceStartPosition;
                coffeeStream.SetActive(false); StopSound();
                var cup = CreateCup(specs[i].size);
                finishedCups.Add(cup); HideParts();
                if (!slots[i]) throw new InvalidOperationException("Pickup position removed during production.");
                yield return MoveCup(cup.transform, slots[i].position);
            }
            SetMessage(L("Your entire order is ready. Please collect it at the pickup counter.",
                "整单已制作完成，请到取餐区领取。", "すべてできました。受取カウンターへお越しください。"));
            if (audioSource && readySound) audioSource.PlayOneShot(readySound);
            yield return null;
            bool? previousNearby = null;
            while (true)
            {
                if (!player || !pickupAnchor) yield break;
                var offset = player.position - pickupAnchor.position; offset.y = 0;
                float radius = Mathf.Max(0, pickupDistance);
                bool nearby = offset.sqrMagnitude <= radius * radius;
                if (previousNearby != nearby)
                {
                    SetMessage(nearby
                        ? L("Press E to collect your entire order", "按 E 领取整单饮品", "Eキーで注文をすべて受け取る")
                        : L("Your entire order is ready. Please collect it at the pickup counter.", "整单已制作完成，请到取餐区领取。", "すべてできました。受取カウンターへお越しください。"));
                    previousNearby = nearby;
                }
                if (nearby && Input.GetKeyDown(KeyCode.E)) break;
                yield return null;
            }
            ClearCups();
            SetMessage(L("Collected {0} cups. Enjoy!", "已领取 {0} 杯，祝你享用愉快！", "{0} 杯受け取りました。お楽しみください！", specs.Count));
            yield return new WaitForSecondsRealtime(1.5f);
        }
        finally
        {
            StopSound(); HideParts(); RestoreParts(); ClearCups();
            IsBusy = false; routine = null; message = "";
        }
    }
    private GameObject CreateCup(int size)
    {
        var root = new GameObject("Pickup_" + pickupNumber + "_" + (finishedCups.Count + 1));
        CopyPart(productionCup, root.transform);
        if (coffeeSurface != productionCup && !coffeeSurface.IsChildOf(productionCup)) CopyPart(coffeeSurface, root.transform);
        // Move the pivot to the cup bottom before size scaling, preserving visual position.
        Vector3 bottom = FindBottom(root.transform);
        foreach (Transform child in root.transform) child.position -= bottom;
        root.transform.position = bottom;
        root.transform.localScale = Vector3.one * (size == 500 ? Mathf.Max(.1f, largeCupScale) : 1f);
        return root;
    }
    private static void CopyPart(Transform source, Transform parent)
    {
        var copy = Instantiate(source.gameObject);
        copy.transform.SetParent(parent, false);
        copy.transform.SetPositionAndRotation(source.position, source.rotation);
        copy.transform.localScale = source.lossyScale;
        copy.SetActive(true);
    }
    private IEnumerator MoveCup(Transform cup, Vector3 destination)
    {
        var start = cup.position;
        var movement = destination - FindBottom(cup);
        float elapsed = 0, seconds = Mathf.Max(.1f, transferDuration);
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            cup.position = start + movement * Mathf.SmoothStep(0, 1, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * transferArcHeight;
            yield return null;
        }
        cup.position = start + movement;
    }
    private static Vector3 FindBottom(Transform target)
    {
        bool found = false; Bounds b = new Bounds();
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r.GetComponent<TextMesh>()) continue;
            if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
        }
        return found ? new Vector3(b.center.x, b.min.y, b.center.z) : target.position;
    }
    private void RestoreParts()
    {
        if (!initialized) return;
        if (productionCup) productionCup.SetPositionAndRotation(cupStartPosition, cupStartRotation);
        if (coffeeSurface) coffeeSurface.SetPositionAndRotation(surfaceStartPosition, surfaceStartRotation);
    }
    private void HideParts()
    {
        if (coffeeStream) coffeeStream.SetActive(false);
        if (coffeeSurface) coffeeSurface.gameObject.SetActive(false);
        if (productionCup) productionCup.gameObject.SetActive(false);
    }
    private void ClearCups()
    {
        foreach (var cup in finishedCups) if (cup) { cup.SetActive(false); Destroy(cup); }
        finishedCups.Clear();
    }
    private void StopSound() { if (audioSource) { audioSource.Stop(); audioSource.loop = false; } }
    private void SetMessage(string value) { if (message != value) { message = value; noticeStartedAt = Time.unscaledTime; } }
    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message)) return;
        if (titleStyle == null)
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Yu Gothic", "Noto Sans CJK JP", "Arial" }, 24);
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            numberStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            if (font) { titleStyle.font = font; numberStyle.font = font; }
            titleStyle.normal.textColor = numberStyle.normal.textColor = new Color(.25f, .21f, .17f);
        }
        float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.5f);
        float t = Mathf.Clamp01((Time.unscaledTime - noticeStartedAt) / .35f);
        float pop = Mathf.Lerp(.94f, 1, Mathf.SmoothStep(0, 1, t));
        float width = 660 * scale * pop, height = 132 * scale * pop;
        var panel = new Rect((Screen.width - width) * .5f, Mathf.Lerp(10, 30, t) * scale, width, height);
        var old = GUI.color;
        GUI.color = new Color(.97f, .94f, .86f, t * .97f); GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, t);
        titleStyle.fontSize = Mathf.RoundToInt(20 * scale); numberStyle.fontSize = Mathf.RoundToInt(17 * scale);
        GUI.Label(new Rect(panel.x + 12, panel.y + 8, panel.width - 24, panel.height * .65f), message, titleStyle);
        GUI.Label(new Rect(panel.x + 12, panel.y + panel.height * .70f, panel.width - 24, panel.height * .25f),
            L("Pickup: {0} | {1} cups", "取餐号：{0}　共 {1} 杯", "受取番号：{0}　合計 {1} 杯", pickupNumber, totalCups), numberStyle);
        GUI.color = old;
    }
    private void OnDisable()
    {
        StopAllCoroutines(); StopSound(); HideParts(); ClearCups(); RestoreParts();
        routine = null; IsBusy = false; message = "";
    }
    private void OnDestroy() { if (font) Destroy(font); }
}
