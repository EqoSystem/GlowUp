using GlowUp.Data;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// One ingredient in the ingredient list: its name, a coloured level (safe / caution / harmful /
    /// not checked), a quick description, what products it is used in and, when it is flagged, why it
    /// matters for this user's skin type, conditions or allergens.
    /// </summary>
    public class IngredientRowView : ThemeAwareBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private Image levelDot;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI usesText;
        [Tooltip("Why it matters for this user. The whole line is hidden when there is nothing to say.")]
        [SerializeField] private TextMeshProUGUI reasonText;

        [Header("Wording")]
        [SerializeField] private string safeLabel = "Not flagged";
        [SerializeField] private string cautionLabel = "Caution";
        [SerializeField] private string avoidLabel = "Avoid";
        [SerializeField] private string unknownLabel = "Not checked";
        [SerializeField] private string usesPrefix = "Used in: ";
        [SerializeField] private string reasonPrefix = "For you: ";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color safeColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color harmfulColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color unknownColor = new Color32(110, 98, 92, 255);

        string level = "safe";

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            safeColor = p.success; cautionColor = p.caution; harmfulColor = p.warning; unknownColor = p.secondaryText;
            Paint();
        }

        public void Bind(IngredientFinding f)
        {
            level = string.IsNullOrEmpty(f.level) ? "unknown" : f.level;
            if (nameText != null) nameText.text = string.IsNullOrWhiteSpace(f.techName) || f.techName == f.name ? f.name : $"{f.name}  <size=80%>({f.techName})</size>";
            if (descriptionText != null) { descriptionText.text = f.description ?? ""; descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(f.description)); }
            if (usesText != null) { usesText.text = usesPrefix + f.commonUses; usesText.gameObject.SetActive(!string.IsNullOrWhiteSpace(f.commonUses)); }
            if (reasonText != null) { reasonText.text = reasonPrefix + f.reason; reasonText.gameObject.SetActive(!string.IsNullOrWhiteSpace(f.reason)); }
            Paint();
        }

        void Paint()
        {
            Color c = level == "safe" ? safeColor : level == "caution" ? cautionColor : level == "harmful" ? harmfulColor : unknownColor;
            if (levelDot != null) levelDot.color = c;
            if (levelText != null)
            {
                levelText.text = level == "safe" ? safeLabel : level == "caution" ? cautionLabel : level == "harmful" ? avoidLabel : unknownLabel;
                levelText.color = c;
            }
            if (reasonText != null) reasonText.color = level == "harmful" ? harmfulColor : cautionColor;
        }
    }
}
