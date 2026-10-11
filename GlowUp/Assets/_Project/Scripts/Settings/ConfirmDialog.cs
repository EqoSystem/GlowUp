using System;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The one "Are you sure?" pop-up of the Settings page. Other scripts call Ask(...) with their own
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

        Action onConfirm;
        bool danger;

        void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(() => { if (panel != null) panel.Hide(); });
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
            onConfirm = confirmed; danger = isDangerous;
            if (titleText != null) titleText.text = title;
            if (messageText != null) messageText.text = message;
            if (confirmLabel != null) confirmLabel.text = confirmText;
            if (cancelLabel != null) cancelLabel.text = string.IsNullOrEmpty(cancelText) ? defaultCancelText : cancelText;
            if (panel != null) panel.Show();
            Paint();            // after Show(): the button's own theme colour is applied when it becomes active
        }

        void Confirm()
        {
            Action action = onConfirm;
            onConfirm = null;
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
