using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

    [Tooltip("按顺序绑定托盘上的放杯位置。")]
    public Transform[] pickupSlots = new Transform[0];

    [Header("Animation")]
    public float brewingDuration = 3f;
    public float transferDuration = 1.2f;
    public float liquidRiseHeight = 0.08f;
    public float transferArcHeight = 0.2f;

    [Header("Optional sound")]
    public AudioSource audioSource;
    public AudioClip brewingSound;
    public AudioClip readySound;

    public bool IsBusy { get; private set; }

    private Vector3 cupStartPosition;
    private Vector3 surfaceStartPosition;
    private Quaternion cupStartRotation;
    private Quaternion surfaceStartRotation;
    private bool initialized;

    private Coroutine productionRoutine;
    private readonly List<GameObject> finishedCups =
        new List<GameObject>();

    private string pickupNumber = "";
    private string message = "";
    private int totalCups;
    private float noticeStartedAt;

    private Font noticeFont;
    private GUIStyle messageStyle;
    private GUIStyle numberStyle;

    public int OrderCapacity
    {
        get
        {
            // 没有配置多杯位置时，保留原来的单杯模式。
            if (pickupSlots == null || pickupSlots.Length == 0)
                return pickupAnchor ? 1 : 0;

            var unique = new HashSet<Transform>();

            foreach (var slot in pickupSlots)
            {
                if (slot)
                    unique.Add(slot);
            }

            return unique.Count;
        }
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
        HideMachineParts();
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

    // 在扣库存之前，由 Purchase 调用。
    public bool CanAcceptOrder(int servings, out string reason)
    {
        reason = "";

        if (!isActiveAndEnabled || !IsConfigured())
        {
            reason = "Production setup is incomplete.";
            return false;
        }

        if (IsBusy || productionRoutine != null)
        {
            reason = "Please collect your previous order first.";
            return false;
        }

        if (servings < 1 || servings > OrderCapacity)
        {
            reason =
                "Please choose between 1 and " +
                OrderCapacity + " cups.";
            return false;
        }

        return true;
    }

    // 兼容原来的单参数调用。
    public bool BeginProduction(string number)
    {
        return BeginProduction(number, 1);
    }

    public bool BeginProduction(string number, int servings)
    {
        Initialize();

        if (!initialized ||
            !CanAcceptOrder(servings, out string reason))
        {
            return false;
        }

        pickupNumber = number;
        totalCups = servings;

        // 在启动协程前锁定，避免重复启动。
        IsBusy = true;
        SetMessage(CafeUiLocalization.Format(
            "Preparing your coffee...",
            "正在准备制作咖啡…",
            "コーヒーを準備しています…"
        ));

        productionRoutine =
            StartCoroutine(ProduceOrder(servings));

        return true;
    }

    private List<Transform> GetPickupPositions()
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

        if (result.Count == 0 && pickupAnchor)
            result.Add(pickupAnchor);

        return result;
    }

    private IEnumerator ProduceOrder(int servings)
    {
        // 记录本单使用的位置，制作途中不切换数组。
        List<Transform> slots = GetPickupPositions();

        try
        {
            for (int i = 0; i < servings; i++)
            {
                RestoreMachineParts();

                productionCup.gameObject.SetActive(true);
                coffeeSurface.gameObject.SetActive(true);
                coffeeStream.SetActive(false);

                SetMessage(CafeUiLocalization.Format(
                    "Preparing cup {0} / {1}",
                    "正在制作第 {0} / {1} 杯",
                    "{0} / {1} 杯目を作っています",
                    i + 1,
                    servings
                ));

                Vector3 emptySurface =
                    surfaceStartPosition -
                    Vector3.up * Mathf.Max(0f, liquidRiseHeight);

                coffeeSurface.position = emptySurface;

                yield return new WaitForSecondsRealtime(0.3f);

                coffeeStream.SetActive(true);
                StartBrewingSound();

                float seconds = Mathf.Max(0.1f, brewingDuration);
                float elapsed = 0f;

                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;

                    coffeeSurface.position = Vector3.Lerp(
                        emptySurface,
                        surfaceStartPosition,
                        Mathf.Clamp01(elapsed / seconds)
                    );

                    yield return null;
                }

                coffeeSurface.position = surfaceStartPosition;
                coffeeStream.SetActive(false);
                StopSound();

                // 复制完整成品，原来的制作杯留在机器中复用。
                GameObject completedCup = CreateFinishedCup();
                finishedCups.Add(completedCup);

                HideMachineParts();

                if (!slots[i])
                    throw new InvalidOperationException(
                        "Pickup slot was removed during production."
                    );

                SetMessage(CafeUiLocalization.Format(
                    "Cup {0} / {1} is ready",
                    "第 {0} / {1} 杯已完成",
                    "{0} / {1} 杯目ができました",
                    i + 1,
                    servings
                ));

                yield return MoveToSlot(
                    completedCup.transform,
                    slots[i].position
                );
            }

            // 所有杯子送达之后才通知整单完成。
            SetMessage(CafeUiLocalization.Format(
                "Your order is ready. Please collect it at the pickup counter.",
                "咖啡已全部制作完成，请到取餐区领取",
                "ご注文の商品ができました。受取カウンターへお越しください。"
            ));

            if (audioSource && readySound)
                audioSource.PlayOneShot(readySound);

            // 避免上一阶段按键影响取餐。
            yield return null;

            bool? wasNearby = null;

            while (true)
            {
                if (!player || !pickupAnchor)
                    yield break;

                Vector3 difference =
                    player.position - pickupAnchor.position;
                difference.y = 0f;

                float radius = Mathf.Max(0f, pickupDistance);
                bool nearby =
                    difference.sqrMagnitude <= radius * radius;

                // 只在靠近状态改变时更新，避免提示动画每帧重播。
                if (wasNearby != nearby)
                {
                    SetMessage(nearby
                        ? CafeUiLocalization.Format(
                            "Press E to collect your entire order",
                            "按 E 领取整单咖啡",
                            "Eキーで注文の商品をすべて受け取る"
                        )
                        : CafeUiLocalization.Format(
                            "Your order is ready. Please collect it at the pickup counter.",
                            "咖啡已全部制作完成，请到取餐区领取",
                            "ご注文の商品ができました。受取カウンターへお越しください。"
                        ));

                    wasNearby = nearby;
                }

                if (nearby && Input.GetKeyDown(KeyCode.E))
                    break;

                yield return null;
            }

            ClearFinishedCups();

            SetMessage(CafeUiLocalization.Format(
                "Collected {0} cup(s). Enjoy your coffee!",
                "已领取 {0} 杯，祝你享用愉快！",
                "{0} 杯受け取りました。お楽しみください！",
                servings
            ));

            // 此时仍保持 IsBusy，避免立即重新接待。
            yield return new WaitForSecondsRealtime(1.5f);
        }
        finally
        {
            StopSound();
            HideMachineParts();
            RestoreMachineParts();
            ClearFinishedCups();

            message = "";
            IsBusy = false;
            productionRoutine = null;
        }
    }

    private GameObject CreateFinishedCup()
    {
        var container = new GameObject(
            "Pickup_" + pickupNumber + "_" +
            (finishedCups.Count + 1)
        );

        // 成品根节点保持单位缩放。
        container.transform.position = Vector3.zero;
        container.transform.rotation = Quaternion.identity;
        container.transform.localScale = Vector3.one;

        CopyPart(productionCup, container.transform);

        // 液面若已经属于杯子子层级，不再复制第二份。
        if (coffeeSurface != productionCup &&
            !coffeeSurface.IsChildOf(productionCup))
        {
            CopyPart(coffeeSurface, container.transform);
        }

        return container;
    }

    private static void CopyPart(
        Transform source,
        Transform parent)
    {
        GameObject copy = Instantiate(source.gameObject);

        copy.transform.SetParent(parent, false);
        copy.transform.SetPositionAndRotation(
            source.position,
            source.rotation
        );

        // 原部件继承了咖啡机的缩放，
        // 独立复制后要保留其世界尺寸。
        copy.transform.localScale = source.lossyScale;
        copy.SetActive(true);
    }

    private IEnumerator MoveToSlot(
        Transform cup,
        Vector3 destination)
    {
        Vector3 start = cup.position;
        Vector3 bottom = FindBottom(cup);
        Vector3 movement = destination - bottom;

        float elapsed = 0f;
        float seconds = Mathf.Max(0.1f, transferDuration);

        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            cup.position =
                start +
                movement * smooth +
                Vector3.up *
                Mathf.Sin(t * Mathf.PI) *
                transferArcHeight;

            yield return null;
        }

        cup.position = start + movement;
    }

    private static Vector3 FindBottom(Transform target)
    {
        bool found = false;
        Bounds bounds = new Bounds();

        foreach (Renderer item in
                 target.GetComponentsInChildren<Renderer>())
        {
            if (!item.enabled ||
                !item.gameObject.activeInHierarchy ||
                item.GetComponent<TextMesh>())
            {
                continue;
            }

            if (!found)
            {
                bounds = item.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(item.bounds);
            }
        }

        return found
            ? new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z)
            : target.position;
    }

    private void RestoreMachineParts()
    {
        if (!initialized)
            return;

        if (productionCup)
        {
            productionCup.SetPositionAndRotation(
                cupStartPosition,
                cupStartRotation
            );
        }

        if (coffeeSurface)
        {
            coffeeSurface.SetPositionAndRotation(
                surfaceStartPosition,
                surfaceStartRotation
            );
        }
    }

    private void HideMachineParts()
    {
        if (coffeeStream)
            coffeeStream.SetActive(false);

        if (coffeeSurface)
            coffeeSurface.gameObject.SetActive(false);

        if (productionCup)
            productionCup.gameObject.SetActive(false);
    }

    private void ClearFinishedCups()
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

    private void StartBrewingSound()
    {
        if (!audioSource || !brewingSound)
            return;

        audioSource.clip = brewingSound;
        audioSource.loop = true;
        audioSource.Play();
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

    private void PrepareNoticeStyles()
    {
        if (messageStyle != null)
            return;

        noticeFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Arial" },
            24
        );

        messageStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };

        numberStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter
        };

        if (noticeFont)
        {
            messageStyle.font = noticeFont;
            numberStyle.font = noticeFont;
        }

        Color textColor = new Color(.25f, .21f, .17f);
        messageStyle.normal.textColor = textColor;
        numberStyle.normal.textColor = textColor;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message))
            return;

        PrepareNoticeStyles();

        float scale = Mathf.Clamp(
            Screen.width / 1280f, .65f, 1.5f
        );

        float t = Mathf.Clamp01(
            (Time.unscaledTime - noticeStartedAt) / .35f
        );

        float eased = Mathf.SmoothStep(0f, 1f, t);
        float pop = Mathf.Lerp(.94f, 1f, eased);

        float width = 560f * scale * pop;
        float height = 110f * scale * pop;

        Rect panel = new Rect(
            (Screen.width - width) * .5f,
            Mathf.Lerp(10f, 30f, eased) * scale,
            width,
            height
        );

        Color previousColor = GUI.color;

        GUI.color = new Color(.97f, .94f, .86f, t * .97f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);

        GUI.color = new Color(1f, 1f, 1f, t);

        messageStyle.fontSize = Mathf.RoundToInt(22f * scale);
        numberStyle.fontSize = Mathf.RoundToInt(17f * scale);

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + 10f,
                panel.width - 24f,
                panel.height * .55f),
            message,
            messageStyle
        );

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + panel.height * .62f,
                panel.width - 24f,
                panel.height * .3f),
            CafeUiLocalization.Format(
                "Pickup number: {0} | {1} cup(s)",
                "取餐号：{0}　共 {1} 杯",
                "受取番号：{0}　合計 {1} 杯",
                pickupNumber,
                totalCups
            ),
            numberStyle
        );

        GUI.color = previousColor;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        StopSound();
        HideMachineParts();
        ClearFinishedCups();
        RestoreMachineParts();

        productionRoutine = null;
        IsBusy = false;
        message = "";
    }

    private void OnDestroy()
    {
        if (noticeFont)
            Destroy(noticeFont);
    }
}