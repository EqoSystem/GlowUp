using System;
using System.Collections.Generic;
using System.Linq;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Step 5 - summary of everything entered so far, shown as plain "Label ... Value" lines.
    /// The input fields only appear once the user taps the Edit button above "Generate Profile"
    /// (the button then reads Done). "Generate Profile" saves any edits and opens the Privacy
    /// Policy / Terms scene.
    /// </summary>
    public class OnboardingStep5FlowController : MonoBehaviour
    {
        [Header("Summary lines (each one has its own SummaryRow settings)")]
        [SerializeField] private SummaryRow[] rows;
        [SerializeField] private SummaryRow ageRow;

        [Header("Editors that live inside the lines (inactive until Edit)")]
        [SerializeField] private TMP_InputField firstName;
        [SerializeField] private TMP_InputField middleName;
        [SerializeField] private TMP_InputField lastName;
        [SerializeField] private DatePickerField birthday;
        [SerializeField] private TMP_InputField email;
        [Tooltip("Decides whether an edited email may be used (the same rule as on Step 1). Its settings and messages are on the email editor.")]
        [SerializeField] private EmailFieldVerifier emailCheck;
        [SerializeField] private TMP_Dropdown skinType;
        [SerializeField] private TMP_Dropdown skinTone;
        [Tooltip("Comma-separated, e.g. Acne, Eczema")]
        [SerializeField] private TMP_InputField conditions;
        [Tooltip("Comma-separated, e.g. Fragrance, Parabens")]
        [SerializeField] private TMP_InputField allergens;

        [Header("Dropdown options (editable)")]
        [SerializeField] private string[] skinTypeOptions = { "Oily", "Normal", "Dry", "Mix" };
        [SerializeField] private string[] skinToneOptions = { "Fair", "Light", "Medium", "Tan", "Deep", "Dark" };

        [Header("Edit mode")]
        [SerializeField] private Button editButton;
        [SerializeField] private TextMeshProUGUI editButtonLabel;
        [SerializeField] private string editLabel = "Edit";
        [SerializeField] private string doneLabel = "Done";
        [Tooltip("The summary scroll view (jumps back to the top when the mode changes)")]
        [SerializeField] private ScrollRect scroll;

        [Header("Buttons")]
        [SerializeField] private Button generateButton;
        [SerializeField] private ContinueButtonGlowAnimator generateAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Rules")]
        [SerializeField] private int minimumAge = 13;
        [SerializeField] private int emailMax = 100;
        [SerializeField] private string msgNoEmail = "Enter your email address.";

        [Header("Scenes")]
        [SerializeField] private string termsScene = SceneNames.Terms;
        [SerializeField] private string previousScene = SceneNames.Onboarding4;

        bool editing;

        void Awake()
        {
            Fill(skinType, skinTypeOptions); Fill(skinTone, skinToneOptions);
            foreach (var f in new[] { firstName, middleName, lastName, email, conditions, allergens })
                if (f != null) f.onValueChanged.AddListener(_ => Refresh());
            // the username was removed from sign-up: a scene built before that still has its summary line, which is switched off
            // here (GlowUp > Build Scene > 6 Onboarding Step 5 takes it out for good)
            if (rows != null) foreach (var r in rows) if (r != null && r.name == "SummaryRow_Username") r.gameObject.SetActive(false);
            if (skinType != null) skinType.onValueChanged.AddListener(_ => Refresh());
            if (skinTone != null) skinTone.onValueChanged.AddListener(_ => Refresh());
            if (birthday != null) birthday.DateChanged += d => { SetAge(d); Refresh(); };
            if (generateButton != null) generateButton.onClick.AddListener(OnGenerate);
            if (editButton != null) editButton.onClick.AddListener(() => SetEditing(!editing));
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(previousScene));
            emailCheck = EmailFieldVerifier.On(gameObject, emailCheck);
            if (emailCheck != null) emailCheck.Changed += Refresh;
        }

        static void Fill(TMP_Dropdown d, string[] options)
        {
            if (d == null) return;
            d.ClearOptions(); d.AddOptions(options.ToList());
        }

        void Start()
        {
            OnboardingSession.LoadDraft();
            Set(firstName, OnboardingSession.FirstName); Set(middleName, OnboardingSession.MiddleName); Set(lastName, OnboardingSession.LastName);
            Set(email, OnboardingSession.Email);
            if (birthday != null && DateTime.TryParse(OnboardingSession.BirthdayIso, out var dob)) { birthday.SetValue(dob); SetAge(dob); }
            else if (ageRow != null) ageRow.SetValue("");
            Pick(skinType, skinTypeOptions, OnboardingSession.SkinType); Pick(skinTone, skinToneOptions, OnboardingSession.SkinTone);
            Set(conditions, string.Join(", ", OnboardingSession.SkinConditions)); Set(allergens, string.Join(", ", OnboardingSession.Allergens));
            if (feedbackText != null) feedbackText.text = "";
            if (email != null) email.characterLimit = emailMax;
            SetEditing(false);
            Refresh();
        }

        /// <summary>Plain text lines (false) or input fields (true).</summary>
        public void SetEditing(bool value)
        {
            editing = value;
            if (rows != null) foreach (var r in rows) if (r != null) r.SetEditing(value);
            if (birthday != null) birthday.SetPickerEnabled(value);
            if (editButtonLabel != null) editButtonLabel.text = value ? doneLabel : editLabel;
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            Refresh();
        }

        void SetAge(DateTime dob) { if (ageRow != null) ageRow.SetValue(InputRules.CalculateAge(dob).ToString()); }

        static void Set(TMP_InputField f, string v) { if (f != null) f.SetTextWithoutNotify(v ?? ""); }
        static string T(TMP_InputField f) => f != null ? f.text.Trim() : "";
        static void Pick(TMP_Dropdown d, string[] opts, string v)
        {
            if (d == null) return;
            int i = Array.FindIndex(opts, o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase));
            d.SetValueWithoutNotify(Mathf.Max(0, i));
        }

        static List<string> ParseList(string s) =>
            s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        bool Valid(out string reason)
        {
            reason = "";
            // Signed in with Google / Facebook: Step 1 was skipped, so a birthday is welcome here but not needed, and the
            // email is the account's own. What IS filled in still has to follow the rules.
            bool social = OnboardingSession.Social;
            // the email: a well-known provider, or a domain that was checked online (an answer Step 1 already has is used again)
            if (!social && (emailCheck == null || !emailCheck.Look(T(email))))
            {
                reason = emailCheck != null && emailCheck.Message.Length > 0 ? emailCheck.Message : msgNoEmail;
                return false;
            }
            bool hasBirthday = birthday != null && birthday.Value.HasValue;
            if (hasBirthday ? InputRules.CalculateAge(birthday.Value.Value) < minimumAge : !social) { reason = $"You must be at least {minimumAge} years old."; return false; }
            return true;
        }

        void Refresh()
        {
            bool ok = Valid(out string reason);
            if (feedbackText != null) feedbackText.text = ok ? "" : reason;
            if (generateAnimator != null) generateAnimator.SetReady(ok);
            else if (generateButton != null) generateButton.interactable = ok;
        }

        void OnGenerate()
        {
            if (!Valid(out string reason)) { if (feedbackText != null) feedbackText.text = reason; return; }
            OnboardingSession.FirstName = T(firstName); OnboardingSession.MiddleName = T(middleName); OnboardingSession.LastName = T(lastName);
            OnboardingSession.Email = T(email).ToLowerInvariant();
            if (birthday != null && birthday.Value.HasValue)
            {
                var dob = birthday.Value.Value;
                OnboardingSession.BirthdayIso = dob.ToString("yyyy-MM-dd"); OnboardingSession.Age = InputRules.CalculateAge(dob);
            }
            else { OnboardingSession.BirthdayIso = ""; OnboardingSession.Age = 0; }      // no birthday given (Google / Facebook sign-up): the age stays "not set"
            if (skinType != null && skinTypeOptions.Length > 0) OnboardingSession.SkinType = skinTypeOptions[Mathf.Clamp(skinType.value, 0, skinTypeOptions.Length - 1)];
            if (skinTone != null && skinToneOptions.Length > 0) OnboardingSession.SkinTone = skinToneOptions[Mathf.Clamp(skinTone.value, 0, skinToneOptions.Length - 1)];
            OnboardingSession.SkinConditions = ParseList(T(conditions));
            OnboardingSession.Allergens = ParseList(T(allergens));
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(termsScene);
        }
    }
}
