using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// TWO BIG, PLAIN NOTICES MADE IN CODE (12 November 2026), on a layer of their own above the screen (sort order 6500):
    ///   Centre : a large card in the MIDDLE of the screen with a big message and, optionally, one button. It cannot be closed during its first
    ///            1.5 seconds (a tap or the button does nothing yet), so it is always read; after that a tap beside it, the close X or its button
    ///            closes it, and it closes by itself after 6 seconds. Used for "Scan a product first" (AR Try-On needs a product).
    ///   Bottom : a green toast low on the screen (above the safe area), shown 2.5 seconds with a short fade. Used for "Report sent. Thank you!".
    /// Both are built once per canvas and reused. Nothing here reads or logs anything the user typed.
    /// </summary>
    public class ScreenNotice : MonoBehaviour
    {
        public const float MinimumSeconds = 1.5f;     // the centre notice cannot be closed before this
        public const float CentreSeconds = 6f;        // and closes by itself after this
        public const float BottomSeconds = 2.5f;

        RectTransform centre, bottom;
        CanvasGroup centreGroup, bottomGroup;
        TextMeshProUGUI centreWords, bottomWords, buttonWords;
        Button button;
        Action onButton;
        float centreShownAt = -100f, bottomShownAt = -100f;
        bool centreOpen, bottomOpen;

        /// <summary>The notice layer of the screen `anywhere` is on (made once, then reused).</summary>
        public static ScreenNotice On(Transform anywhere, TMP_Text fontFrom)
        {
            Canvas canvas = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            Transform existing = top.Find("NoticeLayer");
            if (existing != null) { var found = existing.GetComponent<ScreenNotice>(); if (found != null) return found; Destroy(existing.gameObject); }

            RectTransform layer = RuntimeUiKit.Rect("NoticeLayer", top);
            RuntimeUiKit.Stretch(layer);
            var sort = layer.gameObject.AddComponent<Canvas>(); sort.overrideSorting = true; sort.sortingOrder = 6500;
            layer.gameObject.AddComponent<GraphicRaycaster>();
            var notice = layer.gameObject.AddComponent<ScreenNotice>();
            notice.Build(layer, fontFrom);
            return notice;
        }

        void Build(RectTransform layer, TMP_Text fontFrom)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            TMP_FontAsset heading = RuntimeTheme.Font(FontRole.Heading, fontFrom), body = RuntimeTheme.Font(FontRole.Body, fontFrom), buttonFont = RuntimeTheme.Font(FontRole.Button, fontFrom);

            // ---- the centre notice: a dim layer (a tap on it closes the notice once 1.5 s have passed) and the card in the middle ----
            centre = RuntimeUiKit.Rect("CentreNotice", layer);
            RuntimeUiKit.Stretch(centre);
            centreGroup = centre.gameObject.AddComponent<CanvasGroup>();
            Image dim = centre.gameObject.AddComponent<Image>(); dim.color = new Color32(43, 27, 33, 150);
            var dimButton = centre.gameObject.AddComponent<Button>(); dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(CloseCentreIfAllowed);

            Image card = RuntimeUiKit.Box(centre, "Card", p.surface);
            RectTransform cardRt = card.rectTransform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(Mathf.Min(900f, layer.rect.width > 0 ? layer.rect.width - 100f : 900f), 0f);
            var outline = card.gameObject.AddComponent<Outline>(); outline.effectColor = p.accent; outline.effectDistance = new Vector2(3f, -3f);
            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(56, 56, 56, 56); column.spacing = 36; column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = column.childControlHeight = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.raycastTarget = true;                                                 // a tap on the card itself does not reach the dim layer

            Image icon = RuntimeUiKit.Icon(cardRt, "Icon", RuntimeIcons.Info, p.accentText, 120);
            var iconElement = icon.gameObject.AddComponent<LayoutElement>(); iconElement.preferredHeight = 120; iconElement.preferredWidth = 120;
            centreWords = RuntimeUiKit.Label(cardRt, "Words", "", 56, p.mainText, heading, TextAlignmentOptions.Center);
            centreWords.enableWordWrapping = true; centreWords.fontStyle = FontStyles.Bold;
            button = RuntimeUiKit.Pill(cardRt, "ActionButton", "", p.buttonFill, p.buttonText, buttonFont);
            buttonWords = button.GetComponentInChildren<TextMeshProUGUI>();
            // the button answers at once: only a tap on the dim area waits for the 1.5 seconds (19 Nov 2026: "Scan now" needed several taps)
            button.onClick.AddListener(() => { if (!centreOpen) return; var run = onButton; CloseCentre(); run?.Invoke(); });
            centre.gameObject.SetActive(false);

            // ---- the bottom toast ----
            bottom = RuntimeUiKit.Rect("BottomToast", layer);
            bottomGroup = bottom.gameObject.AddComponent<CanvasGroup>(); bottomGroup.blocksRaycasts = false; bottomGroup.interactable = false;
            Image back = bottom.gameObject.AddComponent<Image>();
            back.sprite = RuntimeIcons.RoundedBox; back.type = Image.Type.Sliced; back.raycastTarget = false;
            back.color = RuntimeTheme.Mix(p.surface, p.success, RuntimeTheme.IsDark ? 0.30f : 0.16f);
            var line = bottom.gameObject.AddComponent<Outline>(); line.effectColor = p.success; line.effectDistance = new Vector2(2f, -2f);
            bottom.anchorMin = new Vector2(0.5f, 0f); bottom.anchorMax = new Vector2(0.5f, 0f); bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(Mathf.Min(900f, layer.rect.width > 0 ? layer.rect.width - 80f : 900f), 120f);
            Image tick = RuntimeUiKit.Icon(bottom, "Tick", RuntimeIcons.Check, p.success, 56);
            tick.rectTransform.anchorMin = tick.rectTransform.anchorMax = new Vector2(0f, 0.5f); tick.rectTransform.pivot = new Vector2(0f, 0.5f);
            tick.rectTransform.anchoredPosition = new Vector2(36f, 0f);
            bottomWords = RuntimeUiKit.Label(bottom, "Words", "", 40, p.mainText, body, TextAlignmentOptions.MidlineLeft);
            bottomWords.enableWordWrapping = true; bottomWords.fontStyle = FontStyles.Bold;
            RuntimeUiKit.Stretch(bottomWords.rectTransform, 112, 28, 8, 8);
            bottom.gameObject.SetActive(false);
        }

        /// <summary>The big notice in the middle of the screen. `buttonWords` + `action` add one button (it answers at once; only a tap beside the card waits for the 1.5 seconds).</summary>
        public void ShowCentre(string message, string buttonLabel = null, Action action = null)
        {
            if (centre == null) return;
            centreWords.text = message ?? "";
            onButton = action;
            bool hasButton = !string.IsNullOrEmpty(buttonLabel) && action != null;
            button.gameObject.SetActive(hasButton);
            if (hasButton) buttonWords.text = buttonLabel;
            transform.SetAsLastSibling();
            centre.gameObject.SetActive(true);
            centreGroup.alpha = 0f;
            centreShownAt = Time.unscaledTime; centreOpen = true;
        }

        /// <summary>The green toast low on the screen (2.5 seconds).</summary>
        public void ShowBottom(string message)
        {
            if (bottom == null) return;
            bottomWords.text = message ?? "";
            Rect safe = Screen.safeArea;
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.rootCanvas.scaleFactor > 0f ? canvas.rootCanvas.scaleFactor : 1f;
            bottom.anchoredPosition = new Vector2(0f, safe.yMin / scale + 120f);       // above the gesture bar, clear of the form's Send button area
            transform.SetAsLastSibling();
            bottom.gameObject.SetActive(true);
            bottomGroup.alpha = 0f;
            bottomShownAt = Time.unscaledTime; bottomOpen = true;
        }

        public bool CentreOpen => centreOpen;

        bool CentreAllowsClosing() => centreOpen && Time.unscaledTime - centreShownAt >= MinimumSeconds;
        void CloseCentreIfAllowed() { if (CentreAllowsClosing()) CloseCentre(); }
        void CloseCentre() { centreOpen = false; onButton = null; if (centre != null) centre.gameObject.SetActive(false); }

        void Update()
        {
            if (centreOpen)
            {
                float t = Time.unscaledTime - centreShownAt;
                centreGroup.alpha = Mathf.Clamp01(t / 0.2f);
                if (t > CentreSeconds) CloseCentre();
            }
            if (bottomOpen)
            {
                float t = Time.unscaledTime - bottomShownAt;
                bottomGroup.alpha = Mathf.Min(Mathf.Clamp01(t / 0.2f), Mathf.Clamp01((BottomSeconds - t) / 0.3f));
                if (t > BottomSeconds) { bottomOpen = false; bottom.gameObject.SetActive(false); }
            }
        }
    }
}
