using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A tappable ingredient chip (the highlighted key ingredients of a result): a coloured disc with an icon for its
    /// level (check = not flagged, warning sign = use with care, cross = flagged as harmful, i = not known), and its name.
    /// The level is never told by colour alone. Tapping the chip opens that ingredient's details.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class IngredientChipView : ThemeAwareBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("The coloured disc behind the icon")]
        [SerializeField] private Image dot;
        [Tooltip("The white icon inside the disc (optional: a chip of an older scene has only the disc)")]
        [SerializeField] private Image icon;

        [Header("Icons, one per level")]
        [SerializeField] private Sprite safeIcon;
        [SerializeField] private Sprite cautionIcon;
        [SerializeField] private Sprite harmfulIcon;
        [SerializeField] private Sprite unknownIcon;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color safeColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color harmfulColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color unknownColor = new Color32(110, 98, 92, 255);

        public string IngredientName { get; private set; }
        public event Action<string> Clicked;

        string level = "safe";
        bool hooked;

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            safeColor = p.success; cautionColor = p.caution; harmfulColor = p.warning; unknownColor = p.secondaryText;
            Paint();
        }

        public void Bind(string ingredientName, string ingredientLevel)
        {
            if (!hooked) { hooked = true; GetComponent<Button>().onClick.AddListener(() => Clicked?.Invoke(IngredientName)); }
            IngredientName = ingredientName;
            level = string.IsNullOrEmpty(ingredientLevel) ? "safe" : ingredientLevel;
            if (label != null) label.text = ingredientName;
            Paint();
        }

        void Paint()
        {
            Color fill = level == "safe" ? safeColor : level == "caution" ? cautionColor : level == "harmful" ? harmfulColor : unknownColor;
            if (dot != null) dot.color = fill;
            if (icon == null) return;
            Sprite s = level == "safe" ? safeIcon : level == "caution" ? cautionIcon : level == "harmful" ? harmfulIcon : unknownIcon;
            if (s != null) icon.sprite = s;
            icon.color = ContrastInk.On(fill);   // New21: white on dark fills, dark ink on the light dark-mode fills
        }
    }
}
