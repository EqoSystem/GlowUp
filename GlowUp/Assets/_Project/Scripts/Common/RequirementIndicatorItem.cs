using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// One row of a requirement checklist ("1 uppercase letter"). Call SetMet(true/false);
    /// the check icon lights up with a small pop. Text, colours and timing are Inspector fields.
    /// </summary>
    public class RequirementIndicatorItem : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private Image checkIcon;
        [SerializeField] private TextMeshProUGUI label;

        [Header("Content")]
        [SerializeField] private string requirementText = "At least 8 characters";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color metColor = new Color32(111, 143, 91, 255);
        [SerializeField] private Color unmetColor = new Color32(150, 140, 132, 255);

        [Header("Animation")]
        [SerializeField] private float fadeSeconds = 0.2f;
        [Tooltip("The check icon briefly grows to this scale when the requirement becomes met")]
        [SerializeField] private float popScale = 1.3f;
        [SerializeField] private float popSeconds = 0.25f;

        public bool IsMet { get; private set; }
        float k, pop;

        void Awake() { if (label != null) label.text = requirementText; Paint(); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            metColor = p.success;
            unmetColor = Color.Lerp(p.secondaryText, p.background, 0.30f); unmetColor.a = 1f;
            Paint();
        }

        public void SetMet(bool met)
        {
            if (met && !IsMet) pop = 1f;
            IsMet = met;
        }

        void Update()
        {
            float target = IsMet ? 1f : 0f;
            if (!Mathf.Approximately(k, target))
            {
                k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                Paint();
            }
            if (pop > 0f && checkIcon != null)
            {
                pop = Mathf.Max(0f, pop - Time.unscaledDeltaTime / Mathf.Max(0.01f, popSeconds));
                checkIcon.rectTransform.localScale = Vector3.one * (1f + (popScale - 1f) * Mathf.Sin(pop * Mathf.PI));
            }
        }

        void Paint()
        {
            var c = Color.Lerp(unmetColor, metColor, k);
            if (checkIcon != null) checkIcon.color = c;
            if (label != null) label.color = c;
        }
    }

    /// <summary>Common validation rules shared by the onboarding controllers.</summary>
    public static class InputRules
    {
        /// <summary>The special characters a username may contain (the default of the Inspector fields on Step 1 and Step 5).</summary>
        public const string UsernameSpecials = "._-@!#$%&*+?";

        /// <summary>True when the text has only English letters, digits and the given special characters (so no spaces).</summary>
        public static bool OnlyLettersDigitsAnd(string s, string specials)
        {
            foreach (char c in s)
                if (!((c < 128 && char.IsLetterOrDigit(c)) || (!string.IsNullOrEmpty(specials) && specials.IndexOf(c) >= 0))) return false;
            return true;
        }

        public static bool HasUpper(string s) { foreach (char c in s) if (c >= 'A' && c <= 'Z') return true; return false; }
        public static bool HasLower(string s) { foreach (char c in s) if (c >= 'a' && c <= 'z') return true; return false; }
        public static bool HasDigit(string s) { foreach (char c in s) if (c >= '0' && c <= '9') return true; return false; }
        public static bool HasSpecial(string s) { foreach (char c in s) if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)) return true; return false; }

        static readonly string[] AllowedDomains = { "gmail.com", "hotmail.com", "yahoo.com" };

        /// <summary>Must contain exactly one @ and a Gmail/Hotmail/Yahoo domain (pass your own list to change it).</summary>
        public static bool IsValidEmail(string email, string[] allowedDomains = null)
        {
            if (string.IsNullOrEmpty(email) || email.Length > 254) return false;
            int at = email.IndexOf('@');
            if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1) return false;
            string domain = email.Substring(at + 1).ToLowerInvariant();
            foreach (var d in allowedDomains ?? AllowedDomains) if (domain == d) return true;
            return false;
        }

        public static int CalculateAge(System.DateTime dob, System.DateTime? today = null)
        {
            var now = today ?? System.DateTime.Today;
            int age = now.Year - dob.Year;
            if (now < dob.AddYears(age)) age--;
            return Mathf.Max(0, age);
        }
    }
}
