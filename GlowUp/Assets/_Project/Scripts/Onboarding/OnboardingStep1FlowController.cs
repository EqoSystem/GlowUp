using System;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Step 1 - account details. Validates every field live, lights up the requirement
    /// checklists, calculates age from the birthday, and only enables (and animates) Continue
    /// when everything is valid. All limits and messages are Inspector fields.
    /// There is no username any more (removed on 11 October 2026): the dashboard greets the user by first name.
    /// </summary>
    public class OnboardingStep1FlowController : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] private TMP_InputField firstName;
        [SerializeField] private TMP_InputField middleName;
        [SerializeField] private TMP_InputField lastName;
        [SerializeField] private DatePickerField birthday;
        [SerializeField] private TMP_InputField age;
        [SerializeField] private TMP_InputField email;
        [Tooltip("Decides whether the typed email may be used (well-known provider, or a domain that was checked online). Its settings and messages are on the email field.")]
        [SerializeField] private EmailFieldVerifier emailCheck;
        [SerializeField] private TMP_InputField password;
        [SerializeField] private TMP_InputField retypePassword;

        [Header("Password checklist")]
        [SerializeField] private RequirementIndicatorItem pwUpper;
        [SerializeField] private RequirementIndicatorItem pwLower;
        [SerializeField] private RequirementIndicatorItem pwNumber;
        [SerializeField] private RequirementIndicatorItem pwSpecial;
        [SerializeField] private RequirementIndicatorItem pwLength;
        [SerializeField] private RequirementIndicatorItem pwMatch;

        [Header("Buttons / Feedback")]
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Limits (industry standard defaults)")]
        [SerializeField] private int nameMaxLength = 50;
        [Tooltip("Long enough for a school or work address")]
        [SerializeField] private int emailMax = 100;
        [SerializeField] private int passwordMin = 8;
        [SerializeField] private int passwordMax = 64;
        [SerializeField] private int minimumAge = 13;
        [Tooltip("First and last name are needed: together with the middle name they identify the user's record in the database")]
        [SerializeField] private bool requireFirstAndLastName = true;

        [Header("Scenes")]
        [SerializeField] private string nextScene = SceneNames.Onboarding2;
        [SerializeField] private string previousScene = SceneNames.Login;

        [Header("Messages")]
        [SerializeField] private string msgFillAll = "Please complete every required field.";
        [SerializeField] private string msgUnderAge = "You must be at least {0} years old to use Glow Up.";

        void Awake()
        {
            OnboardingSession.LoadDraft();
            Limit(firstName, nameMaxLength); Limit(middleName, nameMaxLength); Limit(lastName, nameMaxLength);
            Limit(email, emailMax); Limit(password, passwordMax); Limit(retypePassword, passwordMax);

            foreach (var f in new[] { firstName, middleName, lastName, email, password, retypePassword })
                if (f != null) f.onValueChanged.AddListener(_ => Validate());
            if (birthday != null) birthday.DateChanged += OnBirthday;
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (age != null) { age.readOnly = true; age.interactable = false; }
            // any real mail domain may sign up now (12 October 2026); the verifier says when a checked domain has its answer
            emailCheck = EmailFieldVerifier.On(gameObject, emailCheck);
            if (emailCheck != null) emailCheck.Changed += Validate;
            HideRemovedUsernameRows();
        }

        /// <summary>
        /// The username was removed from sign-up. A scene that was built before that still holds the username's label,
        /// field and checklist rows: they are switched off here, and the form closes the gap by itself. Building the scene
        /// again (GlowUp > Build Scene > 2 Onboarding Step 1) takes them out for good.
        /// </summary>
        void HideRemovedUsernameRows()
        {
            Transform form = firstName != null ? firstName.transform.parent : null;
            if (form == null) return;
            foreach (Transform row in form)
                if (row.name.Trim().StartsWith("Username", StringComparison.Ordinal)) row.gameObject.SetActive(false);
        }

        void Start()
        {
            // restore a draft if the user came back from a later step
            Set(firstName, OnboardingSession.FirstName); Set(middleName, OnboardingSession.MiddleName);
            Set(lastName, OnboardingSession.LastName); Set(email, OnboardingSession.Email);
            if (DateTime.TryParse(OnboardingSession.BirthdayIso, out var dob)) { birthday.SetValue(dob); SetAge(dob); }
            if (OnboardingSession.Social) UseSocialAccount();
            Limit(email, emailMax);          // once more after every script on the field has started: theirs is the older, shorter limit in a scene that was not rebuilt
            Show("");
            Validate();
        }

        bool emailFromAccount;

        /// <summary>
        /// Signed in with Google or Facebook: the account exists already, so everything about the password is
        /// hidden and the email comes from that account (it stays editable only when the account has none).
        /// </summary>
        void UseSocialAccount()
        {
            if (password != null && password.transform.parent != null)
                foreach (Transform row in password.transform.parent)
                    if (row.name.StartsWith("Password", StringComparison.Ordinal) || row.name.StartsWith("Retype", StringComparison.Ordinal))
                        row.gameObject.SetActive(false);
            emailFromAccount = email != null && T(email).Length > 0;
            if (emailFromAccount) { email.readOnly = true; email.interactable = false; }
        }

        void OnBack()
        {
            // leaving sign-up after a Google / Facebook sign-in also signs out, or the login page would send the user straight back here
            if (OnboardingSession.Social) { GlowUp.Data.UserProfileRepository.SignOut(); OnboardingSession.Clear(); }
            SceneNavigator.Go(previousScene);
        }

        static void Limit(TMP_InputField f, int n) { if (f != null) f.characterLimit = n; }
        static void Set(TMP_InputField f, string v) { if (f != null && !string.IsNullOrEmpty(v)) f.SetTextWithoutNotify(v); }
        static string T(TMP_InputField f) => f != null ? f.text.Trim() : "";

        void OnBirthday(DateTime d) { SetAge(d); Validate(); }

        void SetAge(DateTime d)
        {
            if (age != null) age.SetTextWithoutNotify(InputRules.CalculateAge(d).ToString());
        }

        void Validate()
        {
            string pw = password != null ? password.text : "", re = retypePassword != null ? retypePassword.text : "";

            pwUpper?.SetMet(InputRules.HasUpper(pw)); pwLower?.SetMet(InputRules.HasLower(pw));
            pwNumber?.SetMet(InputRules.HasDigit(pw)); pwSpecial?.SetMet(InputRules.HasSpecial(pw)); pwLength?.SetMet(pw.Length >= passwordMin);
            bool pwOk = pw.Length >= passwordMin && pw.Length <= passwordMax && InputRules.HasUpper(pw) && InputRules.HasLower(pw) && InputRules.HasDigit(pw) && InputRules.HasSpecial(pw);
            bool match = pwOk && re.Length > 0 && re == pw;
            pwMatch?.SetMet(match);
            if (OnboardingSession.Social) { pwOk = true; match = true; }        // a Google / Facebook account has no password here

            bool emailOk = emailFromAccount || (emailCheck != null && emailCheck.Look(T(email)));
            bool namesOk = !requireFirstAndLastName || (T(firstName).Length > 0 && T(lastName).Length > 0);
            bool dobOk = birthday != null && birthday.Value.HasValue && InputRules.CalculateAge(birthday.Value.Value) >= minimumAge;

            bool ready = pwOk && match && emailOk && namesOk && dobOk;
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;

            if (birthday != null && birthday.Value.HasValue && !dobOk) Show(string.Format(msgUnderAge, minimumAge));
            else if (!emailOk && emailCheck != null && !emailCheck.ShowsOwnMessages) Show(emailCheck.Message);      // a rebuilt scene says it right under the field
            else Show("");
        }


        void OnContinue()
        {
            if (continueButton != null && !continueButton.interactable) { Show(msgFillAll); return; }
            OnboardingSession.FirstName = T(firstName); OnboardingSession.MiddleName = T(middleName); OnboardingSession.LastName = T(lastName);
            OnboardingSession.Username = "";                                                                      // no longer asked for
            OnboardingSession.Email = T(email).ToLowerInvariant();
            OnboardingSession.Password = OnboardingSession.Social || password == null ? "" : password.text;      // memory only
            var dob = birthday.Value.Value;
            OnboardingSession.BirthdayIso = dob.ToString("yyyy-MM-dd");
            OnboardingSession.Age = InputRules.CalculateAge(dob);
            OnboardingSession.SaveDraft();
            SceneNavigator.Go(nextScene);
        }

        void Show(string m) { if (feedbackText != null) feedbackText.text = m; }
    }
}
