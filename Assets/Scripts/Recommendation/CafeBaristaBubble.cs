using System;
using System.Collections;
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
        private RectTransform panel, tail, titleRect;
        private bool contentOverflows;
        private Text reply, speakerLabel;
        private Font font;
        private Camera view;
        private ScrollRect scroll;
        private bool customer;
        private Button continueButton;
        private Text continueLabel;
        private Action continuation;
        private Coroutine typing;
        private string fullText;
        private bool autoAdvance;
        public void Initialize(Camera camera, Font uiFont, bool isCustomer = false, Sprite rounded = null)
        {
            view = camera; font = uiFont; customer = isCustomer;
            var go = new GameObject(customer ? "CustomerSpeechBubble" : "BaristaSpeechBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 110;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 1080); scaler.matchWidthOrHeight = .5f;
            panel = Rect(go.transform, "Reply", new Vector2(380, 150));
            Color bubbleColor = customer ? new Color(.87f, .95f, 1, .91f) : new Color(1, .97f, .88f, .91f);
            var surface = panel.gameObject.AddComponent<Image>(); surface.sprite = rounded; surface.type = Image.Type.Sliced; surface.color = bubbleColor;
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.15f, .09f, .04f, .10f); shadow.effectDistance = new Vector2(1, -2);
            var title = Rect(panel, "Title", new Vector2(340, 24)); title.anchoredPosition = new Vector2(0, 90);
            var label = title.gameObject.AddComponent<Text>(); label.font = font; label.fontSize = 16; label.fontStyle = FontStyle.Bold;
            titleRect = title; speakerLabel = label; label.text = CafeUiLocalization.Text(customer ? "YOU" : "YOUR BARISTA"); label.color = new Color(.3f, .2f, .12f);
            var viewport = Rect(panel, "Viewport", new Vector2(340, 120)); viewport.anchoredPosition = new Vector2(0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var content = Rect(viewport, "Content", new Vector2(340, 120));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, 120);
            var textRect = Rect(content, "Text", new Vector2(332, 120));
            textRect.anchorMin = new Vector2(0, 1); textRect.anchorMax = new Vector2(1, 1); textRect.pivot = new Vector2(.5f, 1);
            textRect.sizeDelta = new Vector2(-8, 120); textRect.anchoredPosition = new Vector2(0, -6);
            reply = textRect.gameObject.AddComponent<Text>(); reply.font = font; reply.fontSize = 22; reply.lineSpacing = 1.12f;
            reply.color = new Color(.2f, .15f, .1f); reply.supportRichText = false;
            reply.horizontalOverflow = HorizontalWrapMode.Wrap; reply.verticalOverflow = VerticalWrapMode.Overflow;
            scroll.viewport = viewport; scroll.content = content;
            tail = Rect(panel, "Tail", new Vector2(25, 25)); tail.anchoredPosition = new Vector2(-165, -116);
            tail.localRotation = Quaternion.Euler(0, 0, 45); tail.gameObject.AddComponent<Image>().color = bubbleColor;
            var buttonRect = Rect(panel, "Continue", new Vector2(340, 32));
            buttonRect.anchoredPosition = new Vector2(0, -91);
            buttonRect.gameObject.AddComponent<Image>().color = new Color(.87f, .77f, .61f);
            continueButton = buttonRect.gameObject.AddComponent<Button>();
            continueButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var caption = Rect(buttonRect, "Label", new Vector2(332, 30));
            continueLabel = caption.gameObject.AddComponent<Text>();
            continueLabel.font = font; continueLabel.fontSize = 18;
            continueLabel.color = new Color(.23f, .16f, .11f); continueLabel.alignment = TextAnchor.MiddleCenter;
            continueLabel.raycastTarget = false;
            continueButton.onClick.AddListener(ContinueDialogue);
            continueButton.gameObject.SetActive(false);
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
            Hide();
            if (speakerLabel) speakerLabel.text = CafeUiLocalization.Text(customer ? "YOU" : "YOUR BARISTA");
            reply.text = text; canvas.gameObject.SetActive(true);
            LayoutBubble(text, false);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1;
        }
        public void ShowDialogue(string text, Action next, bool automaticallyContinue = false)
        {
            Show(text); fullText = text ?? ""; continuation = next;
            // Never auto-dismiss text that overflows the visible speech viewport.
            autoAdvance = automaticallyContinue && !contentOverflows;
            LayoutBubble(fullText, !autoAdvance);
            reply.text = "";
            continueButton.gameObject.SetActive(!autoAdvance);
            typing = StartCoroutine(TypeDialogue());
        }
        private void LayoutBubble(string text, bool needsContinue)
        {
            // Measure the complete message once, so the panel does not resize while typing.
            const float width = 380, padding = 20, titleHeight = 24, gap = 10;
            float innerWidth = width - padding * 2;
            reply.rectTransform.sizeDelta = new Vector2(-8, 120);
            scroll.viewport.sizeDelta = new Vector2(innerWidth, 120);
            reply.text = text ?? "";
            Canvas.ForceUpdateCanvases();
            // Font metrics can underestimate the visible bottoms of CJK glyphs, especially after canvas scaling.
            // Keep the text inset inside a separate, padded scroll content instead of masking at its last baseline.
            float preferred = Mathf.Ceil(reply.preferredHeight) + 4;
            float paddedHeight = preferred + 16; // 6 px above, 10 px below the text rectangle.
            float bodyHeight = Mathf.Clamp(paddedHeight, 44, 184);
            contentOverflows = paddedHeight > bodyHeight + .5f;
            reply.rectTransform.sizeDelta = new Vector2(-8, preferred);
            reply.rectTransform.anchoredPosition = new Vector2(0, -6);
            scroll.content.sizeDelta = new Vector2(0, Mathf.Max(paddedHeight, bodyHeight));
            float height = padding + titleHeight + gap + bodyHeight + padding + (needsContinue ? 42 : 0);
            panel.sizeDelta = new Vector2(width, height);
            titleRect.sizeDelta = new Vector2(innerWidth, titleHeight);
            titleRect.anchoredPosition = new Vector2(0, height * .5f - padding - titleHeight * .5f);
            scroll.viewport.sizeDelta = new Vector2(innerWidth, bodyHeight);
            scroll.viewport.anchoredPosition = new Vector2(0, height * .5f - padding - titleHeight - gap - bodyHeight * .5f);
            var buttonRect = continueButton.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(innerWidth, 32);
            buttonRect.anchoredPosition = new Vector2(0, -height * .5f + padding + 16);
            scroll.content.anchoredPosition = Vector2.zero;
        }
        private IEnumerator TypeDialogue()
        {
            yield return null;
            continueLabel.text = CafeUiLocalization.Text("Click to reveal");
            float shown = 0;
            while (shown < fullText.Length)
            {
                shown += Time.unscaledDeltaTime * 55;
                reply.text = fullText.Substring(0, Mathf.Min(fullText.Length, (int)shown));
                yield return null;
            }
            reply.text = fullText; continueLabel.text = CafeUiLocalization.Text("Continue");
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1;
            if (autoAdvance)
            {
                yield return new WaitForSecondsRealtime(1.25f);
                typing = null;
                var next = continuation; Hide(); next?.Invoke();
            }
            else typing = null;
        }
        private void ContinueDialogue()
        {
            if (typing != null)
            {
                StopCoroutine(typing); typing = null; reply.text = fullText;
                continueLabel.text = CafeUiLocalization.Text("Continue"); return;
            }
            var next = continuation; Hide(); next?.Invoke();
        }
        public void Hide()
        {
            if (typing != null) { StopCoroutine(typing); typing = null; }
            continuation = null;
            if (continueButton) continueButton.gameObject.SetActive(false);
            if (canvas) canvas.gameObject.SetActive(false);
        }
        private void LateUpdate()
        {
            if (!canvas || !canvas.gameObject.activeSelf || !view) return;
            Vector3 head = baristaAnchor ? baristaAnchor.position : fallbackPosition;
            Vector3 screen = view.WorldToScreenPoint(head);
            var bounds = canvas.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, screen, null, out var point);
            Vector2 facePoint = point;
            // Keep the bubble readable beside the voice panel, even if the character is near a screen edge.
            float halfW = bounds.rect.width * .5f, halfH = bounds.rect.height * .5f;
            panel.localScale = Vector3.one * Mathf.Min(1, bounds.rect.width / (panel.rect.width + 40), bounds.rect.height / (panel.rect.height + 40));
            if (customer)
            {
                // First-person camera sits inside the customer's head; show its bubble in the lower right.
                // With another camera, project it above the actual customer head.
                bool firstPerson = Vector3.Distance(view.transform.position, head) < 1 || screen.z <= .05f;
                point = firstPerson ? new Vector2(halfW - 255, -halfH + 175) : point - new Vector2(240, 0);
                point.x = Mathf.Clamp(point.x, -Mathf.Max(0, halfW - 245), Mathf.Max(0, halfW - 245));
                point.y = Mathf.Clamp(point.y, -Mathf.Max(0, halfH - 140), Mathf.Max(0, halfH - 140));
                panel.anchoredPosition = point;
                return;
            }
            float scale = panel.localScale.x;
            float bubbleHalfWidth = panel.rect.width * scale * .5f;
            float bubbleHalfHeight = panel.rect.height * scale * .5f;
            float gap = 80 * scale;
            float rightRoom = halfW - facePoint.x;
            float leftRoom = facePoint.x + halfW;
            bool right = rightRoom >= bubbleHalfWidth * 2 + gap + 20 || rightRoom >= leftRoom;
            if (screen.z <= 0) facePoint = Vector2.zero;
            point = facePoint + new Vector2((right ? 1 : -1) * (bubbleHalfWidth + gap), 42 * scale);
            point.x = Mathf.Clamp(point.x, -Mathf.Max(0, halfW - bubbleHalfWidth - 20), Mathf.Max(0, halfW - bubbleHalfWidth - 20));
            point.y = Mathf.Clamp(point.y, -Mathf.Max(0, halfH - bubbleHalfHeight - 20), Mathf.Max(0, halfH - bubbleHalfHeight - 20));
            panel.anchoredPosition = point;
            if (tail)
            {
                bool headOnLeft = facePoint.x < point.x;
                tail.anchoredPosition = new Vector2(headOnLeft ? -panel.rect.width * .5f + 3 : panel.rect.width * .5f - 3,
                    Mathf.Clamp((facePoint.y - point.y) / Mathf.Max(.01f, scale), -panel.rect.height * .5f + 20, panel.rect.height * .5f - 20));
            }
        }
        private void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }
    }
}
