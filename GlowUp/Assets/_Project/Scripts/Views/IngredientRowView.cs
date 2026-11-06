using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// One ingredient in the ingredient list. Always visible: its name (with the common name in brackets), its classification, this
    /// user's status (a coloured level), what it does in a product and, when it is flagged, why it matters for this user. The rest
    /// is behind "More about this ingredient": only the fields that have something to say are shown.
    /// A scene built before this layout has no fold: its description and uses stay visible, as they were.
    /// </summary>
    public class IngredientRowView : ThemeAwareBehaviour
    {
        /// <summary>What the row shows beside the finding itself. Every field may be empty.</summary>
        public class Details
        {
            public string commonName, classification, purpose, aliases, chemical, allergen, caution, source, sourceUrl;
            public List<string> highlights = new List<string>();
        }

        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private GameObject classificationBadge;
        [SerializeField] private TextMeshProUGUI classificationText;
        [SerializeField] private Image levelDot;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI purposeText;
        [Tooltip("Why it matters for this user. The whole line is hidden when there is nothing to say.")]
        [SerializeField] private TextMeshProUGUI reasonText;

        [Header("Behind \"More about this ingredient\"")]
        [Tooltip("The toggle and what it opens. Hidden when there is nothing behind it.")]
        [SerializeField] private GameObject fold;
        [SerializeField] private TextMeshProUGUI descriptionText;
        [SerializeField] private TextMeshProUGUI aliasesText;
        [SerializeField] private TextMeshProUGUI chemicalText;
        [SerializeField] private TextMeshProUGUI usesText;
        [SerializeField] private TextMeshProUGUI highlightsText;
        [SerializeField] private TextMeshProUGUI allergenText;
        [SerializeField] private TextMeshProUGUI cautionText;
        [SerializeField] private TextMeshProUGUI sourceText;
        [SerializeField] private Button sourceLinkButton;

        [Header("Wording")]
        [SerializeField] private string safeLabel = "Not flagged";
        [SerializeField] private string cautionLabel = "Caution";
        [SerializeField] private string avoidLabel = "Avoid";
        [SerializeField] private string unknownLabel = "Not checked";
        [SerializeField] private string purposePrefix = "Purpose: ";
        [SerializeField] private string usesPrefix = "<b>Commonly used in:</b> ";
        [SerializeField] private string reasonPrefix = "For you: ";
        [SerializeField] private string descriptionPrefix = "<b>What it is:</b> ";
        [SerializeField] private string aliasesPrefix = "<b>Also known as:</b> ";
        [SerializeField] private string chemicalPrefix = "<b>What the chemical is:</b> ";
        [SerializeField] private string allergenPrefix = "<b>Allergen family:</b> ";
        [SerializeField] private string cautionPrefix = "<b>Caution:</b> ";
        [Tooltip("The small tint behind each highlight tag, as #RRGGBBAA. It is neutral on purpose: colours mean this user's status.")]
        [SerializeField] private string tagTint = "#8C7F7F33";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color safeColor = new Color32(94, 127, 75, 255);
        [SerializeField] private Color cautionColor = new Color32(169, 116, 28, 255);
        [SerializeField] private Color harmfulColor = new Color32(181, 71, 47, 255);
        [SerializeField] private Color unknownColor = new Color32(110, 98, 92, 255);

        string level = "safe";
        string linkAddress = "";
        bool linkHooked;

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            safeColor = p.success; cautionColor = p.caution; harmfulColor = p.warning; unknownColor = p.secondaryText;
            Paint();
        }

        /// <summary>
        /// Everything the database holds is plain text, never markup: the app's own AI look-up writes entries nobody has checked, and a
        /// "&lt;" in them must not become a tag (a size, a link, a colour). Every "&lt;" gets an invisible zero-width space behind it, so no
        /// tag can form however the text is built (a wrapper like noparse can be closed from inside the text; this cannot).
        /// </summary>
        public static string Plain(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("<", "<​");

        /// <summary>
        /// Takes out Markdown bold marks ("**In high amounts**") that AI-written entries carry: the card shows plain sentences, and a caution
        /// that matters belongs in the Caution field, not in bold in the middle of the definition.
        /// </summary>
        public static string NoMarkdown(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("**", "").Replace("__", "");

        static void Line(TextMeshProUGUI text, string prefix, string value)
        {
            if (text == null) return;
            value = NoMarkdown(value);
            bool has = !string.IsNullOrWhiteSpace(value);
            if (has) text.text = prefix + Plain(value);
            text.gameObject.SetActive(has);
        }

        public void Bind(IngredientFinding f, Details d = null)
        {
            d = d ?? new Details();
            level = string.IsNullOrEmpty(f.level) ? "unknown" : f.level;
            string common = !string.IsNullOrWhiteSpace(d.commonName) ? d.commonName : f.techName;
            if (nameText != null) nameText.text = string.IsNullOrWhiteSpace(common) || common == f.name ? Plain(f.name) : $"{Plain(f.name)}  <size=80%>({Plain(common)})</size>";
            if (classificationText != null) classificationText.text = Plain(d.classification);
            if (classificationBadge != null) classificationBadge.SetActive(!string.IsNullOrWhiteSpace(d.classification));
            // the line under the status is the short Purpose only. The long definition is ALWAYS behind "More about this ingredient" (closed by
            // default), even when there is no purpose: then the card shows name, badge and status, and nothing long. (Until 2026-11-06 the
            // definition took the purpose's place, so every entry without a purpose showed its whole definition open.)
            bool hasPurpose = !string.IsNullOrWhiteSpace(d.purpose);
            Line(reasonText, reasonPrefix, f.reason);

            Line(aliasesText, aliasesPrefix, d.aliases);
            Line(chemicalText, chemicalPrefix, d.chemical);
            Line(usesText, fold != null ? usesPrefix : "Used in: ", f.commonUses);
            Line(allergenText, allergenPrefix, d.allergen);
            Line(cautionText, cautionPrefix, d.caution);
            if (highlightsText != null)                                          // the tags carry their own (escaped) markup, so they do not go through Line
            {
                string tags = Tags(d.highlights);
                if (tags.Length > 0) highlightsText.text = tags;
                highlightsText.gameObject.SetActive(tags.Length > 0);
            }
            Line(sourceText, "", d.source);
            // a scene built before the fold has no line of its own for the source: it stays at the end of the description, as it was
            string description = sourceText != null || string.IsNullOrWhiteSpace(d.source) ? f.description : (f.description + " " + d.source).Trim();      // (escaped by Line)
            bool hasDefinition = !string.IsNullOrWhiteSpace(description);
            if (fold == null) Line(descriptionText, "", description);                    // a scene built before the fold shows it as it always did
            else
            {
                Line(purposeText, purposePrefix, d.purpose);                              // hidden when the entry has no purpose
                Line(descriptionText, descriptionPrefix, description);                    // in the fold, always
            }
            if (fold == null) Line(purposeText, purposePrefix, d.purpose);

            linkAddress = d.sourceUrl ?? "";
            if (sourceLinkButton != null)
            {
                sourceLinkButton.gameObject.SetActive(IsWebAddress(linkAddress));
                if (!linkHooked)
                {
                    linkHooked = true;
                    sourceLinkButton.onClick.AddListener(() => { if (IsWebAddress(linkAddress)) Application.OpenURL(linkAddress); });
                }
            }

            if (fold != null)
                fold.SetActive(hasDefinition ||!string.IsNullOrWhiteSpace(d.aliases) || !string.IsNullOrWhiteSpace(d.chemical)
                    || !string.IsNullOrWhiteSpace(f.commonUses) || !string.IsNullOrWhiteSpace(d.allergen) || !string.IsNullOrWhiteSpace(d.caution)
                    || d.highlights.Count > 0 || !string.IsNullOrWhiteSpace(d.source));
            Paint();
        }

        string Tags(List<string> names)
        {
            var parts = new List<string>();
            foreach (string name in names) if (!string.IsNullOrWhiteSpace(name)) parts.Add($"<mark={tagTint}> {Plain(name)} </mark>");
            return string.Join("  ", parts);
        }

        static bool IsWebAddress(string address) =>
            !string.IsNullOrEmpty(address) && address.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

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
