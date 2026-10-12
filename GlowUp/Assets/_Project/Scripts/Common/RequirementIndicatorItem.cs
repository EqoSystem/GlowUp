using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// One row of a requirement checklist ("Uppercase"). Call SetMet(true/false).
    /// The WORDS stay readable the whole time (the user must be able to read what is asked while typing); it is the
    /// CHECK MARK that shows the state: faint while the condition is not met, bright green with a small pop when it is.
    /// Text, colours and timing are Inspector fields.
    /// </summary>
    public class RequirementIndicatorItem : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private Image checkIcon;
        [SerializeField] private TextMeshProUGUI label;

        [Header("Content")]
        [SerializeField] private string requirementText = "Minimum of 8 characters";

        [Header("The check mark (filled in from the theme while Use Theme Colors is on)")]
        [Tooltip("The check mark when the condition is met")]
        [SerializeField] private Color metColor = new Color32(24, 169, 87, 255);
        [Tooltip("The check mark while the condition is not met yet (faint)")]
        [SerializeField] private Color unmetColor = new Color32(190, 180, 176, 255);
        [Tooltip("On = a met condition gets the BRIGHT green below. Off = the theme's calmer Success colour.")]
        [SerializeField] private bool brightCheckWhenMet = true;
        [Tooltip("The bright green on the light theme (dark enough to be seen on the pale card)")]
        [SerializeField] private Color brightGreenOnLight = new Color32(24, 169, 87, 255);
        [Tooltip("The bright green on the dark theme")]
        [SerializeField] private Color brightGreenOnDark = new Color32(61, 220, 132, 255);

        [Header("The words (filled in from the theme while Use Theme Colors is on)")]
        [Tooltip("On = the words keep a readable colour whether the condition is met or not. Off = they fade with the check mark, as before.")]
        [SerializeField] private bool wordsAlwaysReadable = true;
        [Tooltip("The words while the condition is not met yet")]
        [SerializeField] private Color labelColor = new Color32(108, 96, 98, 255);
        [Tooltip("The words when the condition is met")]
        [SerializeField] private Color labelMetColor = new Color32(58, 47, 48, 255);

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
            bool dark = p.background.grayscale < 0.5f;
            metColor = brightCheckWhenMet ? (dark ? brightGreenOnDark : brightGreenOnLight) : p.success;
            unmetColor = Color.Lerp(p.secondaryText, p.background, 0.55f); unmetColor.a = 1f;       // the check that is not earned yet: faint
            if (wordsAlwaysReadable) { labelColor = p.secondaryText; labelMetColor = p.mainText; }
            else { labelColor = Color.Lerp(p.secondaryText, p.background, 0.30f); labelColor.a = 1f; labelMetColor = metColor; }
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
            if (checkIcon != null) checkIcon.color = Color.Lerp(unmetColor, metColor, k);
            if (label != null) label.color = Color.Lerp(labelColor, labelMetColor, k);
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

        /// <summary>
        /// True when the text is written like an email address. Whether its mail domain may sign up (a well-known provider,
        /// or one that was checked online) is decided by EmailDomains and the EmailFieldVerifier on the field.
        /// </summary>
        public static bool IsValidEmail(string email) => EmailDomains.WellFormed(email);

        public static int CalculateAge(System.DateTime dob, System.DateTime? today = null)
        {
            var now = today ?? System.DateTime.Today;
            int age = now.Year - dob.Year;
            if (now < dob.AddYears(age)) age--;
            return Mathf.Max(0, age);
        }
    }
}
