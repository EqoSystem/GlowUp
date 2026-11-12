using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A CENTERED POPUP for the support feedback form (and reusable elsewhere).
    /// Dimmed backdrop, centered card with title + message + OK button. Blocks taps behind it.
    /// Styled to match the existing ConfirmDialog (same corner radius, same font sizes, same padding).
    /// Built once per canvas at run time — no scene rebuild needed.
    ///
    /// Usage:
    ///   var popup = CenteredFeedbackPopup.Get(anyTransformOnCanvas);
    ///   popup.Show("Title", "Message", "OK", optionalAutoCloseSeconds, optionalCallback);
    ///   popup.Hide();
    /// </summary>
    public class CenteredFeedbackPopup : MonoBehaviour
    {
        RectTransform root;
        CanvasGroup rootGroup;
        TextMeshProUGUI titleLabel, messageLabel;
        Button okButton, backdropButton;
        float shownAt = -100f, autoCloseAt = float.MaxValue;
        bool open;
        Action onClosed;

        /// <summary>True while the popup is visible.</summary>
        public bool IsOpen => open;

        // ------------------------------------------------------------------
        //  Factory: one per canvas, reused
        // ------------------------------------------------------------------

        /// <summary>Gets or creates the centered popup on the root canvas that `anywhere` belongs to.</summary>
        public static CenteredFeedbackPopup Get(Transform anywhere)
        {
            Canvas canvas = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            Transform existing = top.Find("FeedbackPopupLayer");
            if (existing != null)
            {
                var found = existing.GetComponent<CenteredFeedbackPopup>();
                if (found != null) return found;
                Destroy(existing.gameObject);
            }
            // build the whole thing in code so no scene YAML change is needed
            return Build(top);
        }

        static CenteredFeedbackPopup Build(Transform canvasRoot)
        {
            // layer: full-screen, high sort order, blocks everything behind it
            GameObject layerGo = new GameObject("FeedbackPopupLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
            layerGo.transform.SetParent(canvasRoot, false);
            RectTransform layerRt = layerGo.GetComponent<RectTransform>();
            Stretch(layerRt);
            Canvas sort = layerGo.GetComponent<Canvas>();
            sort.overrideSorting = true; sort.sortingOrder = 7000;
            CanvasGroup layerGroup = layerGo.GetComponent<CanvasGroup>();

            var popup = layerGo.AddComponent<CenteredFeedbackPopup>();
            popup.root = layerRt;
            popup.rootGroup = layerGroup;

            // dimmed backdrop (tapping it does nothing while the popup shows; it only blocks taps behind)
            Image dim = layerGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;
            popup.backdropButton = layerGo.AddComponent<Button>();
            popup.backdropButton.transition = Selectable.Transition.None;
            popup.backdropButton.onClick.AddListener(() => popup.TryClose());

            // centered card
            GameObject cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            cardGo.transform.SetParent(layerRt, false);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            float canvasW = layerRt.rect.width > 0 ? layerRt.rect.width : 1080f;
            cardRt.sizeDelta = new Vector2(Mathf.Min(860f, canvasW - 120f), 0f);
            Image cardBg = cardGo.GetComponent<Image>();
            // match ConfirmDialog card: white / off-white with rounded corners
            cardBg.color = new Color32(255, 248, 245, 255);
            cardBg.raycastTarget = true;
            VerticalLayoutGroup col = cardGo.GetComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(56, 56, 52, 52);
            col.spacing = 28;
            col.childAlignment = TextAnchor.UpperCenter;
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            cardGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // optional: soft shadow
            Shadow shadow = cardGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
            shadow.effectDistance = new Vector2(0f, -6f);

            // borrow font from any existing TMP text in canvasRoot so typography matches the app
            TMP_FontAsset font = canvasRoot.GetComponentInChildren<TextMeshProUGUI>(true)?.font;

            // title
            popup.titleLabel = MakeLabel(cardRt, "Title", "", 48, FontStyles.Bold, font);
            popup.titleLabel.alignment = TextAlignmentOptions.Center;

            // message
            popup.messageLabel = MakeLabel(cardRt, "Message", "", 36, FontStyles.Normal, font);
            popup.messageLabel.alignment = TextAlignmentOptions.Center;
            popup.messageLabel.enableWordWrapping = true;

            // OK button (pill shape, accent colour — matches ConfirmDialog's confirm button style)
            GameObject btnGo = new GameObject("OkButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(cardRt, false);
            Image btnBg = btnGo.GetComponent<Image>();
            btnBg.color = new Color32(216, 108, 138, 255);   // same as ConfirmDialog normalFill
            btnBg.raycastTarget = true;
            btnGo.GetComponent<LayoutElement>().preferredHeight = 100f;
            popup.okButton = btnGo.GetComponent<Button>();
            popup.okButton.targetGraphic = btnBg;
            popup.okButton.onClick.AddListener(() => popup.TryClose());

            TextMeshProUGUI btnLabel = MakeLabel(btnGo.GetComponent<RectTransform>(), "Label", "OK", 36, FontStyles.Bold, font);
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.color = new Color32(36, 26, 28, 255);   // same as ConfirmDialog normalText
            Stretch(btnLabel.rectTransform);

            layerGo.SetActive(false);
            return popup;
        }

        static TextMeshProUGUI MakeLabel(RectTransform parent, string name, string text, float size, FontStyles style, TMP_FontAsset font = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = new Color32(36, 26, 28, 255);
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------
        //  Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Shows the centered popup. `autoCloseSeconds` <= 0 means no auto-close.
        /// `closed` is called when the popup hides (by OK, backdrop tap, or auto-close).
        /// </summary>
        public void Show(string title, string message, string okText = "OK", float autoCloseSeconds = 0f, Action closed = null)
        {
            if (titleLabel != null) titleLabel.text = title ?? "";
            if (messageLabel != null) messageLabel.text = message ?? "";
            if (okButton != null)
            {
                var label = okButton.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = string.IsNullOrEmpty(okText) ? "OK" : okText;
            }
            onClosed = closed;
            shownAt = Time.unscaledTime;
            autoCloseAt = autoCloseSeconds > 0f ? Time.unscaledTime + autoCloseSeconds : float.MaxValue;
            open = true;
            gameObject.SetActive(true);
            root.SetAsLastSibling();
            if (rootGroup != null) { rootGroup.alpha = 0f; rootGroup.blocksRaycasts = true; rootGroup.interactable = true; }
        }

        public void Hide()
        {
            if (!open) return;
            open = false;
            if (rootGroup != null) rootGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
            var cb = onClosed;
            onClosed = null;
            cb?.Invoke();
        }

        void TryClose()
        {
            // block closing for the first 0.6 s so the user reads it
            if (Time.unscaledTime - shownAt < 0.6f) return;
            Hide();
        }

        void Update()
        {
            if (!open) return;
            // fade in
            float t = Time.unscaledTime - shownAt;
            if (rootGroup != null) rootGroup.alpha = Mathf.Clamp01(t / 0.2f);
            // auto-close
            if (Time.unscaledTime >= autoCloseAt) Hide();
        }
    }
}
