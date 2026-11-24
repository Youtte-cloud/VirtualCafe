using UnityEngine;
using UnityEngine.UI;

namespace VirtualCafe.Recommendation
{
    // Separate canvas: the bubble follows the barista, rather than the terminal panel.
    public sealed class CafeBaristaBubble : MonoBehaviour
    {
        public Transform baristaAnchor;
        public Vector3 fallbackPosition;
        private Canvas canvas;
        private RectTransform panel;
        private Text reply;
        private Font font;
        private Camera view;
        private ScrollRect scroll;
        private bool customer;
        public void Initialize(Camera camera, Font uiFont, bool isCustomer = false)
        {
            view = camera; font = uiFont; customer = isCustomer;
            var go = new GameObject(customer ? "CustomerSpeechBubble" : "BaristaSpeechBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 110;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 1080); scaler.matchWidthOrHeight = .5f;
            panel = Rect(go.transform, "Reply", new Vector2(430, 260));
            Color bubbleColor = customer ? new Color(.87f, .95f, 1, .97f) : new Color(1, .97f, .88f, .97f);
            panel.gameObject.AddComponent<Image>().color = bubbleColor;
            var title = Rect(panel, "Title", new Vector2(390, 30)); title.anchoredPosition = new Vector2(0, 105);
            var label = title.gameObject.AddComponent<Text>(); label.font = font; label.fontSize = 22;
            label.text = customer ? "You / 顾客" : "Barista / 店员"; label.color = new Color(.3f, .2f, .12f);
            var viewport = Rect(panel, "Viewport", new Vector2(390, 190)); viewport.anchoredPosition = new Vector2(0, -16);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var content = Rect(viewport, "Text", new Vector2(390, 190));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, 190);
            reply = content.gameObject.AddComponent<Text>(); reply.font = font; reply.fontSize = 23;
            reply.color = new Color(.2f, .15f, .1f); reply.supportRichText = false;
            reply.horizontalOverflow = HorizontalWrapMode.Wrap; reply.verticalOverflow = VerticalWrapMode.Overflow;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var tail = Rect(panel, "Tail", new Vector2(25, 25)); tail.anchoredPosition = new Vector2(-180, -128);
            tail.localRotation = Quaternion.Euler(0, 0, 45); tail.gameObject.AddComponent<Image>().color = bubbleColor;
            canvas.gameObject.SetActive(false);
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
        }
        public void Show(string text)
        {
            if (!canvas) return;
            reply.text = text; canvas.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1;
        }
        public void Hide() { if (canvas) canvas.gameObject.SetActive(false); }
        private void LateUpdate()
        {
            if (!canvas || !canvas.gameObject.activeSelf || !view) return;
            Vector3 head = baristaAnchor ? baristaAnchor.position : fallbackPosition;
            Vector3 screen = view.WorldToScreenPoint(head);
            var bounds = canvas.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, screen, null, out var point);
            point += new Vector2(240, 150);
            // Keep the bubble readable beside the voice panel, even if the character is near a screen edge.
            float halfW = bounds.rect.width * .5f, halfH = bounds.rect.height * .5f;
            if (customer)
            {
                // First-person camera sits inside the customer's head; show its bubble in the lower right.
                // With another camera, project it above the actual customer head.
                bool firstPerson = Vector3.Distance(view.transform.position, head) < 1 || screen.z <= .05f;
                point = firstPerson ? new Vector2(halfW - 235, -halfH + 165) : point - new Vector2(240, 0);
                point.x = Mathf.Clamp(point.x, -Mathf.Max(0, halfW - 220), Mathf.Max(0, halfW - 220));
                point.y = Mathf.Clamp(point.y, -Mathf.Max(0, halfH - 145), Mathf.Max(0, halfH - 145));
                panel.anchoredPosition = point;
                return;
            }
            point.x = Mathf.Clamp(screen.z > 0 ? point.x : halfW * .5f, Mathf.Min(220, halfW - 220), Mathf.Max(220, halfW - 220));
            point.y = Mathf.Clamp(point.y, -Mathf.Max(0, halfH - 145), Mathf.Max(0, halfH - 145));
            panel.anchoredPosition = point;
        }
        private void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }
    }
}
