using System;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The "Are you sure?" pop-up (the Settings page has one; the sign-up pages use it for "Leave sign-up?"). Other scripts call Ask(...) with their own
    /// wording; the pop-up shows it and runs their action only when the user confirms.
    /// A dangerous action (log out, delete) paints the confirm button in the warning colour.
    /// </summary>
    public class ConfirmDialog : ThemeAwareBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TextMeshProUGUI confirmLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TextMeshProUGUI cancelLabel;
        [SerializeField] private string defaultCancelText = "Cancel";

        [Header("Confirm button colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color normalFill = new Color32(216, 108, 138, 255);
        [SerializeField] private Color normalText = new Color32(36, 26, 28, 255);
        [SerializeField] private Color dangerFill = new Color32(181, 71, 47, 255);
        [SerializeField] private Color dangerText = Color.white;

        Action onConfirm, onCancel;
        bool danger;

        /// <summary>True while the question is on the screen.</summary>
        public bool IsOpen => panel != null && panel.IsOpen;

        /// <summary>Puts the question away without doing anything (the same as its Cancel button).</summary>
        public void Close() { if (panel != null) panel.Hide(); }

        void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        /// <summary>The Cancel button: the question goes away; a caller that asked for it (`cancelled`) is told.</summary>
        void Cancel()
        {
            Action action = onCancel;
            onCancel = null; onConfirm = null;
            if (panel != null) panel.Hide();
            action?.Invoke();
        }

        /// <summary>Like Ask, and `cancelled` runs when the user presses Cancel (New17: the retake's "Save edits" / "Cancel").</summary>
        public void Ask(string title, string message, string confirmText, Action confirmed, Action cancelled, bool isDangerous = false, string cancelText = null)
        {
            Ask(title, message, confirmText, confirmed, isDangerous, cancelText);
            onCancel = cancelled;
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            normalFill = p.buttonFill; normalText = p.buttonText;
            dangerFill = p.warning;
            dangerText = p.warning.grayscale > 0.45f ? p.background : Color.white;      // dark text on the lighter dark-mode red
            Paint();
        }

        /// <summary>Shows the question. `confirmed` runs when the user presses the confirm button.</summary>
        public void Ask(string title, string message, string confirmText, Action confirmed, bool isDangerous = false, string cancelText = null)
        {
            onConfirm = confirmed; onCancel = null; danger = isDangerous;
            if (titleText != null) titleText.text = title;
            if (messageText != null) messageText.text = message;
            if (confirmLabel != null) confirmLabel.text = confirmText;
            if (cancelLabel != null) cancelLabel.text = string.IsNullOrEmpty(cancelText) ? defaultCancelText : cancelText;
            if (panel != null) panel.Show();
            Relayout();
            Paint();            // after Show(): the button's own theme colour is applied when it becomes active
        }

        // ---- the readable layout (24 Nov 2026): every question pop-up of the app (the allergen notice, Leave sign-up?, log out, delete, ...) gets
        //      the same direct layout, whatever its scene built: a bold title, text at 38 (it was 31), the box as tall as its text needs, any extra
        //      button as a full-width row, and the two answers side by side at 44 dp height, the main one pink. ----
        const float SideMargin = 56f, TitleBand = 190f, AnswerHeight = 132f, ExtraHeight = 124f, RowGap = 22f, BottomMargin = 48f;

        void Relayout()
        {
            RectTransform box = messageText != null ? messageText.transform.parent as RectTransform : null;
            if (box == null || confirmButton == null || cancelButton == null) return;
            if (confirmButton.transform.parent != box || cancelButton.transform.parent != box || box.GetComponent<LayoutGroup>() != null) return;       // a pop-up laid out some other way keeps its own layout
            float width = box.rect.width > 100f ? box.rect.width : 900f;

            if (titleText != null) { titleText.fontStyle = FontStyles.Bold; }
            messageText.enableAutoSizing = false; messageText.fontSize = 38f; messageText.lineSpacing = 12f; messageText.paragraphSpacing = 16f;
            messageText.alignment = TextAlignmentOptions.TopLeft; messageText.enableWordWrapping = true; messageText.richText = true;

            // buttons that are neither answer nor the X: stacked above the answers
            var extras = new System.Collections.Generic.List<RectTransform>();
            foreach (Button b in box.GetComponentsInChildren<Button>(true))
            {
                if (b == confirmButton || b == cancelButton || b.name == "CloseButton" || !b.gameObject.activeSelf) continue;
                extras.Add((RectTransform)b.transform);
            }

            float answersTop = BottomMargin + AnswerHeight;
            float extrasTop = answersTop + (extras.Count > 0 ? RowGap : 0f) + extras.Count * ExtraHeight + Mathf.Max(0, extras.Count - 1) * 16f;
            SetRow((RectTransform)cancelButton.transform, 0f, 0.5f, SideMargin, RowGap * 0.5f, BottomMargin, AnswerHeight);
            SetRow((RectTransform)confirmButton.transform, 0.5f, 1f, RowGap * 0.5f, SideMargin, BottomMargin, AnswerHeight);
            for (int i = 0; i < extras.Count; i++)
                SetRow(extras[i], 0f, 1f, SideMargin, SideMargin, answersTop + RowGap + i * (ExtraHeight + 16f), ExtraHeight);
            foreach (TextMeshProUGUI label in new[] { confirmLabel, cancelLabel })
                if (label != null) { label.enableAutoSizing = false; label.fontSize = 36f; label.enableWordWrapping = true; label.fontStyle = label == confirmLabel ? FontStyles.Bold : FontStyles.Normal; }
            foreach (RectTransform extra in extras)
            {
                TextMeshProUGUI label = extra.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) { label.enableAutoSizing = false; label.fontSize = 34f; }
            }

            float textHeight = messageText.GetPreferredValues(messageText.text, width - 2f * SideMargin, 0f).y;
            float below = extrasTop + 36f;
            RectTransform text = messageText.rectTransform;
            text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(SideMargin, below); text.offsetMax = new Vector2(-SideMargin, -TitleBand);
            box.sizeDelta = new Vector2(box.sizeDelta.x, Mathf.Clamp(TitleBand + textHeight + below + 12f, 560f, 1600f));
        }

        static void SetRow(RectTransform rt, float from, float to, float left, float right, float bottom, float height)
        {
            rt.anchorMin = new Vector2(from, 0f); rt.anchorMax = new Vector2(to, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, bottom + height);
        }

        void Confirm()
        {
            Action action = onConfirm;
            onConfirm = null; onCancel = null;
            if (panel != null) panel.Hide();
            action?.Invoke();
        }

        void Paint()
        {
            if (confirmButton != null && confirmButton.targetGraphic != null) confirmButton.targetGraphic.color = danger ? dangerFill : normalFill;
            if (confirmLabel != null) confirmLabel.color = danger ? dangerText : normalText;
        }
    }
}
