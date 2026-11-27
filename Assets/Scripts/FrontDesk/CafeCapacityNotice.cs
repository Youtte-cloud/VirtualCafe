using UnityEngine;
using VirtualCafe.Recommendation;

[DisallowMultipleComponent]
public sealed class CafeCapacityNotice : MonoBehaviour
{
    private static CafeCapacityNotice instance;

    private CafeMachineProduction production;
    private CafeFrontDeskUI frontDesk;

    private string notice;
    private float noticeUntil;

    private Font noticeFont;
    private GUIStyle noticeStyle;

    private void Awake()
    {
        instance = this;

        production = FindObjectOfType<CafeMachineProduction>();
        frontDesk = FindObjectOfType<CafeFrontDeskUI>();

        noticeFont = Font.CreateDynamicFontFromOSFont(
            new[]
            {
                "Microsoft YaHei",
                "Yu Gothic",
                "Noto Sans CJK JP",
                "Arial"
            },
            20);
    }

    public static void Show(string en, string zh, string ja)
    {
        if (!instance)
            instance = FindObjectOfType<CafeCapacityNotice>();

        if (!instance)
        {
            var host = new GameObject("CafeCapacityNotice");
            instance = host.AddComponent<CafeCapacityNotice>();
        }

        instance.notice = CafeUiLocalization.Format(en, zh, ja);
        instance.noticeUntil = Time.unscaledTime + 6f;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(notice) ||
            Time.unscaledTime >= noticeUntil)
        {
            return;
        }

        string text = notice;

        if (noticeStyle == null)
        {
            noticeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(16, 16, 10, 10)
            };

            noticeStyle.normal.background = Texture2D.whiteTexture;
            noticeStyle.normal.textColor =
                new Color(0.25f, 0.21f, 0.17f);

            if (noticeFont)
                noticeStyle.font = noticeFont;
        }

        float scale = Mathf.Clamp(Screen.width / 1280f, 0.7f, 1.4f);
        float width = Mathf.Min(720f * scale, Screen.width - 24f);

        noticeStyle.fontSize = Mathf.RoundToInt(20f * scale);

        var rect = new Rect(
            (Screen.width - width) * 0.5f,
            20f * scale,
            width,
            80f * scale);

        Color previousColor = GUI.backgroundColor;

        GUI.backgroundColor =
            new Color(0.97f, 0.94f, 0.86f, 0.98f);

        GUI.Box(rect, text, noticeStyle);

        GUI.backgroundColor = previousColor;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (noticeFont)
            Destroy(noticeFont);
    }
}