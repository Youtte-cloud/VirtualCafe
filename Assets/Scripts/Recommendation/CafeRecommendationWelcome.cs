using UnityEngine;

namespace VirtualCafe.Recommendation
{
    // Chinese welcome entry for the independent LLM scene, without changing the shared main scene.
    public sealed class CafeRecommendationWelcome : MonoBehaviour
    {
        public CafeReceptionController reception;
        private Font font;
        private CanvasGroup original;
        private float alpha;
        private bool interaction, raycasts;
        private void Start()
        {
            if (!reception) reception = GetComponent<CafeReceptionController>();
            if (!reception || !reception.welcomePanel) return;
            original = reception.welcomePanel.GetComponent<CanvasGroup>();
            if (!original) original = reception.welcomePanel.AddComponent<CanvasGroup>();
            alpha = original.alpha; interaction = original.interactable; raycasts = original.blocksRaycasts;
            original.alpha = 0; original.interactable = false; original.blocksRaycasts = false;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 20);
        }
        private void OnDestroy()
        {
            if (original) { original.alpha = alpha; original.interactable = interaction; original.blocksRaycasts = raycasts; }
            if (font) Destroy(font);
        }
        private void OnGUI()
        {
            if (!reception || !reception.welcomePanel || !reception.welcomePanel.activeInHierarchy) return;
            float width = Mathf.Min(380, Screen.width - 24);
            var area = new Rect((Screen.width - width) / 2, Mathf.Max(12, (Screen.height - 240) / 2), width, 240);
            Color old = GUI.color;
            GUI.color = new Color(1, .96f, .89f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = old;
            var label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            label.normal.textColor = new Color(.24f, .17f, .12f);
            var button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19, fixedHeight = 42 };
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, area.width - 40, area.height - 32));
            GUILayout.Label("欢迎光临！今天想喝点什么？", label, GUILayout.Height(58));
            if (GUILayout.Button("帮我选一杯", button)) reception.OpenRecommendation();
            if (GUILayout.Button("我想直接点单", button)) reception.OpenOrder();
            if (GUILayout.Button("稍后再来", button)) reception.EndReception();
            GUILayout.EndArea();
        }
    }
}
