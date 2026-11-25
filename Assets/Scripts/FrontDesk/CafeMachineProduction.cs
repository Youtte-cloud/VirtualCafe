using System;
using System.Collections;
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
    private string productionMessage = "";
    private string productionPickupNumber = "";

    private bool readyNotice;
    private float noticeStartedAt;
    private float productionStartedAt;

    private Font noticeFont;
    private GUIStyle titleStyle;
    private GUIStyle numberStyle;

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized || !productionCup || !coffeeSurface)
            return;

        // 记录场景中杯子和满杯液面的原始位置。
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
               player;
    }

    // 由前台关闭点单界面后调用。
    // 动画运行在本组件上，不阻塞订单页面。
    public bool BeginProduction(string pickupNumber)
    {
        Initialize();

        if (!isActiveAndEnabled ||
            !initialized ||
            !IsConfigured() ||
            IsBusy ||
            productionRoutine != null)
        {
            return false;
        }

        productionPickupNumber = pickupNumber;
        productionMessage = "Preparing your drink... Please wait.";
        productionStartedAt = Time.unscaledTime;
        readyNotice = false;

        productionRoutine =
            StartCoroutine(RunProductionIndependently());

        return true;
    }

    private IEnumerator RunProductionIndependently()
    {
        try
        {
            yield return Play(UpdateProductionMessage);
        }
        finally
        {
            productionRoutine = null;
            productionMessage = "";
            readyNotice = false;
        }
    }

    public IEnumerator Play(Action<string> setStatus)
    {
        Initialize();

        if (!initialized || !IsConfigured() || IsBusy)
            yield break;

        IsBusy = true;

        try
        {
            RestorePositions();

            productionCup.gameObject.SetActive(true);
            coffeeSurface.gameObject.SetActive(true);
            coffeeStream.SetActive(false);

            setStatus?.Invoke("Preparing your drink...");

            // 沿世界竖直方向降低液面，
            // 不依赖导入模型的本地轴向。
            Vector3 emptySurface =
                surfaceStartPosition -
                Vector3.up * Mathf.Max(0f, liquidRiseHeight);

            coffeeSurface.position = emptySurface;

            yield return new WaitForSecondsRealtime(0.3f);

            coffeeStream.SetActive(true);

            if (audioSource && brewingSound)
            {
                audioSource.clip = brewingSound;
                audioSource.loop = true;
                audioSource.Play();
            }

            float seconds = Mathf.Max(0.1f, brewingDuration);
            float elapsed = 0f;

            // 制作中：液面逐渐升高。
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);

                coffeeSurface.position = Vector3.Lerp(
                    emptySurface,
                    surfaceStartPosition,
                    t
                );

                yield return null;
            }

            coffeeSurface.position = surfaceStartPosition;
            coffeeStream.SetActive(false);
            StopSound();

            setStatus?.Invoke("Your drink is ready!");

            if (audioSource && readySound)
                audioSource.PlayOneShot(readySound);

            yield return new WaitForSecondsRealtime(0.5f);

            // 按杯底对齐取餐定位点。
            Vector3 bottom = FindCupBottom();
            Vector3 movement = pickupAnchor.position - bottom;

            seconds = Mathf.Max(0.1f, transferDuration);
            elapsed = 0f;

            // 杯身和液面一起移动到取餐区。
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / seconds);
                float smooth = Mathf.SmoothStep(0f, 1f, t);

                Vector3 offset = movement * smooth;
                offset += Vector3.up *
                    Mathf.Sin(t * Mathf.PI) *
                    transferArcHeight;

                productionCup.position =
                    cupStartPosition + offset;

                coffeeSurface.position =
                    surfaceStartPosition + offset;

                yield return null;
            }

            productionCup.position =
                cupStartPosition + movement;

            coffeeSurface.position =
                surfaceStartPosition + movement;

            setStatus?.Invoke(
                "Your drink is at the pickup counter."
            );

            yield return null;

            // 独立等待玩家取餐。
            while (true)
            {
                if (!player || !pickupAnchor)
                    yield break;

                Vector3 difference =
                    player.position - pickupAnchor.position;

                difference.y = 0f;

                float distance = Mathf.Max(0f, pickupDistance);
                bool nearby =
                    difference.sqrMagnitude <= distance * distance;

                setStatus?.Invoke(nearby
                    ? "Press E to pick up your drink."
                    : "Your drink is at the pickup counter.");

                if (nearby && Input.GetKeyDown(KeyCode.E))
                    break;

                yield return null;
            }

            HideParts();
            setStatus?.Invoke("Enjoy your coffee!");

            yield return new WaitForSecondsRealtime(1.5f);
        }
        finally
        {
            StopSound();
            HideParts();
            RestorePositions();
            IsBusy = false;
        }
    }

    private Vector3 FindCupBottom()
    {
        bool found = false;
        Bounds bounds = new Bounds();

        foreach (Renderer item in
                 productionCup.GetComponentsInChildren<Renderer>())
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
                bounds.center.z
            )
            : productionCup.position;
    }

    private void RestorePositions()
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

    private void HideParts()
    {
        if (coffeeStream)
            coffeeStream.SetActive(false);

        if (coffeeSurface)
            coffeeSurface.gameObject.SetActive(false);

        if (productionCup)
            productionCup.gameObject.SetActive(false);
    }

    private void StopSound()
    {
        if (!audioSource)
            return;

        audioSource.Stop();
        audioSource.loop = false;
    }

    private void UpdateProductionMessage(string message)
    {
        // Keep language-neutral message keys so a language change also updates an active notice.
        switch (message)
        {
            case "Preparing your drink...":
                productionMessage = "Preparing your drink... Please wait.";
                break;
            case "Your drink is ready!":
                productionMessage = "Your drink is ready! Moving it to the pickup counter...";
                readyNotice = true;
                noticeStartedAt = Time.unscaledTime;
                break;
            case "Your drink is at the pickup counter.":
            case "Press E to pick up your drink.":
            case "Enjoy your coffee!":
                productionMessage = message;
                break;
        }
    }

    private void PrepareNoticeStyles()
    {
        if (titleStyle != null && numberStyle != null)
            return;

        noticeFont = Font.CreateDynamicFontFromOSFont(
            new[]
            {
                "Microsoft YaHei",
                "SimHei",
                "Arial"
            },
            24
        );

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

        if (noticeFont)
        {
            titleStyle.font = noticeFont;
            numberStyle.font = noticeFont;
        }

        Color textColor = new Color(0.25f, 0.21f, 0.17f);
        titleStyle.normal.textColor = textColor;
        numberStyle.normal.textColor = textColor;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(productionMessage))
            return;

        PrepareNoticeStyles();

        float uiScale = Mathf.Clamp(
            Screen.width / 1280f,
            0.65f,
            1.5f
        );

        float animationStart = readyNotice
            ? noticeStartedAt
            : productionStartedAt;

        float progress = Mathf.Clamp01(
            (Time.unscaledTime - animationStart) / 0.35f
        );

        float eased =
            Mathf.SmoothStep(0f, 1f, progress);

        float pop = Mathf.Lerp(0.94f, 1f, eased);
        float width = 560f * uiScale * pop;
        float height = 110f * uiScale * pop;

        float top =
            Mathf.Lerp(10f, 30f, eased) * uiScale;

        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            top,
            width,
            height
        );

        Color previousColor = GUI.color;

        // 奶油色提示卡背景。
        GUI.color = new Color(
            0.97f,
            0.94f,
            0.86f,
            progress * 0.97f
        );

        GUI.DrawTexture(
            panel,
            Texture2D.whiteTexture
        );

        GUI.color = new Color(1f, 1f, 1f, progress);

        titleStyle.fontSize =
            Mathf.RoundToInt(22f * uiScale);

        numberStyle.fontSize =
            Mathf.RoundToInt(17f * uiScale);

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + 10f,
                panel.width - 24f,
                panel.height * 0.55f
            ),
            CafeUiLocalization.Text(productionMessage),
            titleStyle
        );

        GUI.Label(
            new Rect(
                panel.x + 12f,
                panel.y + panel.height * 0.62f,
                panel.width - 24f,
                panel.height * 0.3f
            ),
            CafeUiLocalization.Text("Your pickup number: ") + productionPickupNumber,
            numberStyle
        );

        GUI.color = previousColor;
    }
}