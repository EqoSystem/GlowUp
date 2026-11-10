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
        [SerializeField] private string msgNoFirstName = "Enter your first name.";
        [SerializeField] private string msgNoLastName = "Enter your last name.";
        [SerializeField] private string msgNoBirthday = "Enter your birthday: tap Edit and pick it. Your age is worked out from it.";
        [Tooltip("The same reason while the lines are open (Edit was tapped): the user only has to tap the box")]
        [SerializeField] private string msgPickBirthday = "Tap the box to pick your birthday. Your age is worked out from it.";

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
            OnboardingNotices.OneLine(feedbackText);
            if (email != null) email.characterLimit = emailMax;
            // something required is empty (a Google account has no birthday, and often only one name): the fields are open at once
            SetEditing(MissingRequired());
            Refresh();
        }

        /// <summary>True when the first name, the last name or the birthday is empty. They are required for every sign-up, also after a Google sign-in.</summary>
        bool MissingRequired() => T(firstName).Length == 0 || T(lastName).Length == 0 || birthday == null || !birthday.Value.HasValue;

        /// <summary>Plain text lines (false) or input fields (true).</summary>
        public void SetEditing(bool value)
        {
            editing = value;
            if (rows != null) foreach (var r in rows) if (r != null) r.SetEditing(value);
            // the email belongs to the account (confirmed on Step 1, or Google / Facebook's own): it is not changed here
            if (value && (OnboardingSession.Social || OnboardingSession.HasAccount) && email != null)
            {
                SummaryRow emailRow = email.GetComponentInParent<SummaryRow>(true);
                if (emailRow != null) emailRow.SetEditing(false);
            }
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

        bool Valid(out string reason) => Valid(out reason, out _);

        /// <summary>`field` = the box the reason belongs to (its summary line shows the reason under it), null = none.</summary>
        bool Valid(out string reason, out Component field)
        {
            reason = ""; field = null;
            // Signed in with Google: Step 1 was skipped and Google gives no birthday, so the names and the birthday are asked here and
            // are required, like on Step 1. The email is the account's own.
            bool social = OnboardingSession.Social;
            bool hasAccount = OnboardingSession.HasAccount;        // its email was confirmed on Step 1 and is fixed
            // the email: a well-known provider, or a domain that was checked online (an answer Step 1 already has is used again)
            if (!social && !hasAccount && (emailCheck == null || !emailCheck.Look(T(email))))
            {
                reason = emailCheck != null && emailCheck.Message.Length > 0 ? emailCheck.Message : msgNoEmail;
                field = email;
                return false;
            }
            if (T(firstName).Length == 0) { reason = msgNoFirstName; field = firstName; return false; }
            if (T(lastName).Length == 0) { reason = msgNoLastName; field = lastName; return false; }
            bool hasBirthday = birthday != null && birthday.Value.HasValue;
            if (!hasBirthday) { reason = editing ? msgPickBirthday : msgNoBirthday; field = birthday; return false; }
            if (InputRules.CalculateAge(birthday.Value.Value) < minimumAge) { reason = $"You must be at least {minimumAge} years old."; field = birthday; return false; }
            return true;
        }

        /// <summary>
        /// The reason Generate Profile is locked, UNDER the summary line it belongs to (New13 items 5A and 6, 10 November 2026): it used
        /// to be a free line between Done and Generate Profile that ran over the Done button. The old line stays empty.
        /// </summary>
        void ShowReason(string reason, Component field)
        {
            if (feedbackText != null) { feedbackText.text = ""; OnboardingNotices.OneLine(feedbackText); }
            SummaryRow target = field != null ? field.GetComponentInParent<SummaryRow>(true) : null;
            if (rows != null)
                foreach (SummaryRow r in rows)
                {
                    if (r == null) continue;
                    FieldHint hint = FieldHint.In(r.transform, feedbackText, 28f);
                    if (r == target && reason.Length > 0) hint.Show(FieldHint.Kind.Error, reason); else hint.Hide();
                }
            if (target == null && reason.Length > 0) OnboardingNotices.Error(feedbackText, reason);
        }

        void Refresh()
        {
            bool ok = Valid(out string reason, out Component field);
            ShowReason(ok ? "" : reason, field);
            if (generateAnimator != null) generateAnimator.SetReady(ok);
            else if (generateButton != null) generateButton.interactable = ok;
        }

        void OnGenerate()
        {
            if (!Valid(out string reason, out Component field))
            {
                ShowReason(reason, field);
                if (!editing && field != null) SetEditing(true);            // the line to fix opens at once
                return;
            }
            OnboardingSession.FirstName = T(firstName); OnboardingSession.MiddleName = T(middleName); OnboardingSession.LastName = T(lastName);
            if (!OnboardingSession.Social && !OnboardingSession.HasAccount) OnboardingSession.Email = T(email).ToLowerInvariant();     // an account's email stays the account's
            if (birthday != null && birthday.Value.HasValue)
            {
                var dob = birthday.Value.Value;
                OnboardingSession.BirthdayIso = dob.ToString("yyyy-MM-dd"); OnboardingSession.Age = InputRules.CalculateAge(dob);
            }
            if (skinType != null && skinTypeOptions.Length > 0) OnboardingSession.SkinType = skinTypeOptions[Mathf.Clamp(skinType.value, 0, skinTypeOptions.Length - 1)];
            if (skinTone != null && skinToneOptions.Length > 0) OnboardingSession.SkinTone = skinToneOptions[Mathf.Clamp(skinTone.value, 0, skinToneOptions.Length - 1)];
            OnboardingSession.SkinConditions = ParseList(T(conditions));
            OnboardingSession.Allergens = ParseList(T(allergens));
            OnboardingSession.SaveDraft();
            TermsAcceptanceFlowController.OpenFromSignUp();           // the progress bar shows on the next page only on this way in
            SceneNavigator.Go(termsScene);
        }
    }
}
