using System.Text;
using GlowUp.Theme;
using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Shows a long document (Privacy Policy, Terms and Conditions) from a plain .txt file, so the
    /// wording can be edited without touching code: Assets/_Project/Legal/PrivacyPolicyAndTerms.txt.
    /// In that file:   "# Title" = big coloured title,   "## Heading" = bold section heading,
    ///                 "- item"  = bullet point,         anything else = normal paragraph.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LegalTextSource : ThemeAwareBehaviour
    {
        [SerializeField] private TextAsset document;

        [Header("Formatting")]
        [Range(100, 200)] [SerializeField] private int titleSizePercent = 135;
        [Range(100, 160)] [SerializeField] private int headingSizePercent = 108;
        [Tooltip("Colour of the '# Title' lines (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color titleColor = new Color32(217, 142, 160, 255);

        void Awake() { Apply(); }

        // every Privacy Policy / Terms screen gets the round "Back to top" button on the scroll area this text sits in (7 November 2026)
        void Start() { BackToTopButton.Attach(GetComponentInParent<UnityEngine.UI.ScrollRect>()); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            titleColor = p.heading;
            Apply();
        }

        /// <summary>Formats the document into this object's text.</summary>
        public void Apply()
        {
            if (document == null) return;
            var text = GetComponent<TMP_Text>();
            if (text != null) text.text = Format(document.text);
        }

        string Format(string raw)
        {
            string hex = ColorUtility.ToHtmlStringRGB(titleColor);
            var sb = new StringBuilder(raw.Length + 512);
            foreach (string rawLine in raw.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                if (line.StartsWith("## ")) sb.Append("<size=").Append(headingSizePercent).Append("%><b>").Append(line.Substring(3)).Append("</b></size>");
                else if (line.StartsWith("# ")) sb.Append("<size=").Append(titleSizePercent).Append("%><color=#").Append(hex).Append("><b>").Append(line.Substring(2)).Append("</b></color></size>");
                else if (line.StartsWith("- ")) sb.Append("<indent=4%>- ").Append(line.Substring(2)).Append("</indent>");
                else sb.Append(line);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
