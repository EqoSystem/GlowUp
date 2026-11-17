using GlowUp.Data;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// One quick-glance line of the compatibility matrix (skin type, allergens, skin concerns, age):
    /// an icon that is a tick / warning / cross, a title and, where there is room, a one-line reason.
    /// </summary>
    public class CompatibilityIndicator : ThemeAwareBehaviour
    {
        [SerializeField] private Image icon;
        [Tooltip("Optional round background behind the icon")]
        [SerializeField] private Image iconBackground;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [Tooltip("Optional: the one-line reason")]
        [SerializeField] private TextMeshProUGUI detailLabel;

        [Header("Content")]
        [SerializeField] private string title = "Skin type";

        [Header("Icons")]
        [SerializeField] private Sprite goodIcon;
        [SerializeField] private Sprite cautionIcon;
        [SerializeField] private Sprite badIcon;
        [SerializeField] private Sprite unknownIcon;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color goodColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color badColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color unknownColor = new Color32(110, 98, 92, 255);

        [Tooltip("Which state is shown in the Editor and before any product is loaded: good, caution, bad or unknown")]
        [SerializeField] private string level = "good";

        void Awake() { if (titleLabel != null) titleLabel.text = title; Paint(); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            goodColor = p.success; cautionColor = p.caution; badColor = p.warning; unknownColor = p.secondaryText;
            Paint();
        }

        public void Bind(CompatibilityItem item)
        {
            level = item != null && !string.IsNullOrEmpty(item.level) ? item.level.ToLowerInvariant() : "unknown";
            if (detailLabel != null) detailLabel.text = item != null ? item.summary : "";
            Paint();
        }

        void Paint()
        {
            Color c = level == "good" ? goodColor : level == "caution" ? cautionColor : level == "bad" ? badColor : unknownColor;
            Sprite s = level == "good" ? goodIcon : level == "caution" ? cautionIcon : level == "bad" ? badIcon : unknownIcon;
            if (iconBackground != null) { iconBackground.color = c; if (icon != null) icon.color = ContrastInk.On(c); }   // New21: readable on light fills too
            else if (icon != null) icon.color = c;
            if (icon != null && s != null) icon.sprite = s;
        }
    }
}
