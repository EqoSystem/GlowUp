using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// The safe / use-with-care / harmful status indicator of a product: a coloured pill with a short label.
    /// The three wordings and colours are Inspector fields (colours follow the theme by default).
    /// </summary>
    public class StatusPillView : ThemeAwareBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image icon;

        [Header("Wording")]
        [SerializeField] private string safeText = "Nothing flagged";
        [SerializeField] private string cautionText = "Use with care";
        [SerializeField] private string avoidText = "Avoid";

        [Header("Icons (optional)")]
        [SerializeField] private Sprite safeIcon;
        [SerializeField] private Sprite cautionIcon;
        [SerializeField] private Sprite harmfulIcon;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color safeColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color harmfulColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color darkText = new Color32(47, 38, 37, 255);

        [Tooltip("Which status is shown in the Editor and before any product is loaded: safe, caution or harmful")]
        [SerializeField] private string status = "safe";

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            safeColor = p.success; cautionColor = p.caution; harmfulColor = p.warning;
            Paint();
        }

        public void Bind(string newStatus)
        {
            status = string.IsNullOrEmpty(newStatus) ? "caution" : newStatus.ToLowerInvariant();
            Paint();
        }

        void Paint()
        {
            Color c = status == "safe" ? safeColor : status == "harmful" ? harmfulColor : cautionColor;
            // white text on darker pills, dark text on lighter ones: whichever has the higher WCAG contrast (New21)
            Color text = ContrastInk.On(c, darkText);
            if (background != null) background.color = c;
            if (label != null)
            {
                label.text = status == "safe" ? safeText : status == "harmful" ? avoidText : cautionText;
                label.color = text;
            }
            if (icon != null)
            {
                Sprite s = status == "safe" ? safeIcon : status == "harmful" ? harmfulIcon : cautionIcon;
                if (s != null) icon.sprite = s;
                icon.color = text;
            }
        }
    }
}
