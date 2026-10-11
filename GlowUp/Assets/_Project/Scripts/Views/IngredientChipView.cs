using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A tappable ingredient chip (the highlighted key ingredients of a result): a coloured dot for
    /// its level and its name. Tapping it opens that ingredient's details.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class IngredientChipView : ThemeAwareBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image dot;

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
            if (dot != null) dot.color = level == "safe" ? safeColor : level == "caution" ? cautionColor : level == "harmful" ? harmfulColor : unknownColor;
        }
    }
}
