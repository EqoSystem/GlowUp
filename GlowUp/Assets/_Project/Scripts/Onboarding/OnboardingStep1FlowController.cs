using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
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
    ///
    /// THE EMAIL IS CONFIRMED HERE (the user's decision of 12 October 2026). Once every field meets its conditions and the
    /// user continues, the account is made (that is what lets Firebase send its "confirm your email" message), the
    /// message is sent, and the pop-up "Confirm your email" waits until the link in it was tapped. The app looks by itself
    /// every few seconds and when the user comes back from the mail app, so nothing has to be typed. Only then does the
    /// sign-up go on to Step 2. From that moment the email and the password are fixed; "Use another email" drops the
    /// account and lets the user start that part again. A Google / Facebook account is confirmed by its provider.
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

        [Header("Confirm the email (empty in a scene built before 12 October 2026: then nothing is confirmed)")]
        [Tooltip("On = the email must be confirmed before the sign-up goes on (the account is made on this page). Off = as it was before: no confirmation, and the account is made on the Terms page at the end.")]
        [SerializeField] private bool confirmEmailOnSignUp = true;
        [Tooltip("The pop-up \"Confirm your email\"")]
        [SerializeField] private OverlayPanel confirmPanel;
        [SerializeField] private TextMeshProUGUI confirmMessageText;
        [Tooltip("The line in the pop-up that says what is going on (sent again, not confirmed yet ...)")]
        [SerializeField] private TextMeshProUGUI confirmStatusText;
        [SerializeField] private Button confirmedButton;
        [Tooltip("\"Open Gmail\": opens the Gmail app (or its Play Store page, or the phone's own email app). Only when tapped.")]
        [SerializeField] private Button openGmailButton;
        [SerializeField] private Button sendAgainButton;
        [SerializeField] private TextMeshProUGUI sendAgainLabel;
        [Tooltip("In the pop-up: \"Use another email\"")]
        [SerializeField] private Button changeEmailButton;
        [Tooltip("The small link under the email field that shows once the account exists: \"Use another email\"")]
        [SerializeField] private Button useAnotherEmailButton;
        [Tooltip("Asks before the sign-up for the old email is dropped")]
        [SerializeField] private GlowUp.AppSettings.ConfirmDialog questionDialog;
        [Tooltip("Seconds before the confirmation message may be sent again")]
        [SerializeField] private float sendAgainAfterSeconds = 60f;
        [Tooltip("While the pop-up is open the app looks every so many seconds whether the email was confirmed")]
        [SerializeField] private float lookEverySeconds = 4f;

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

        [Header("Messages")]
        [SerializeField] private string msgFillAll = "Please complete every required field.";
        [SerializeField] private string msgUnderAge = "You must be at least {0} years old to use Glow Up.";

        [Header("Messages: confirming the email ({0} = the email)")]
        [SerializeField] private string msgNeedInternet = "Connect to the internet to confirm your email.";
        [SerializeField] private string msgSending = "Sending the confirmation email...";
        [TextArea(2, 4)] [SerializeField] private string msgConfirmBody = "We sent a message to {0}. Open it and tap the link in it, then come back here. This page goes on by itself once the email is confirmed.";
        [SerializeField] private string msgNotYet = "Not confirmed yet. Open the message and tap its link (look in the spam folder too).";
        [SerializeField] private string msgSentAgain = "Sent again. It can take a minute to arrive.";
        [SerializeField] private string msgEmailInUse = "This email already has an account. Log in on the log-in page, or use \"Forgot Password?\" there.";
        [SerializeField] private string msgTooManyTries = "Too many tries. Wait a few minutes, then try again.";
        [SerializeField] private string msgCouldNot = "That did not work. Check the internet connection and try again.";
        [SerializeField] private string msgConfirmed = "Email confirmed.";
        [SerializeField] private string msgGmailFromStore = "Gmail is not on this phone: its Play Store page opened so you can install it. You can also check your email another way.";
        [SerializeField] private string msgOwnMailApp = "Gmail is not on this phone, so your email app opened.";
        [SerializeField] private string msgNoMailApp = "No email app was found on this phone. Please check your email manually.";
        [SerializeField] private string msgWaiting = "Waiting for you to confirm this email.";
        [SerializeField] private string sendAgainText = "Send again";
        [Tooltip("{0} = seconds left")]
        [SerializeField] private string sendAgainWaitText = "Send again in {0} s";
        [SerializeField] private string changeTitle = "Use another email?";
        [TextArea(2, 4)] [SerializeField] private string changeMessage = "The sign-up for {0} is dropped and you can type another email and password. Everything else you filled in stays.";
        [SerializeField] private string changeConfirm = "Use another";

        void Awake()
        {
            OnboardingSession.LoadDraft();
            Limit(firstName, nameMaxLength); Limit(middleName, nameMaxLength); Limit(lastName, nameMaxLength);
            Limit(email, emailMax); Limit(password, passwordMax); Limit(retypePassword, passwordMax);

            foreach (var f in new[] { firstName, middleName, lastName, email, password, retypePassword })
                if (f != null)
                {
                    f.onValueChanged.AddListener(_ => { flowMessage = ""; Validate(); });
                    TMP_InputField field = f;
                    f.onEndEdit.AddListener(_ => { if (T(field).Length > 0) left.Add(field); Validate(); });        // a field's error shows once the user leaves it
                }
            if (password != null) password.onSelect.AddListener(_ => RevealPasswordRequirements());
            if (birthday != null) birthday.DateChanged += OnBirthday;
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            DisabledTapRelay.On(continueButton, OnContinue);         // a tap on the greyed Continue names the field that needs correcting
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (age != null) { age.readOnly = true; age.interactable = false; }
            // any real mail domain may sign up now (12 October 2026); the verifier says when a checked domain has its answer
            emailCheck = EmailFieldVerifier.On(gameObject, emailCheck);
            if (emailCheck != null) emailCheck.Changed += Validate;
            if (confirmedButton != null) confirmedButton.onClick.AddListener(() => { _ = LookWhetherConfirmed(true); });
            if (openGmailButton != null) openGmailButton.onClick.AddListener(OpenGmail);
            if (sendAgainButton != null) sendAgainButton.onClick.AddListener(() => { _ = SendAgain(); });
            if (changeEmailButton != null) changeEmailButton.onClick.AddListener(AskUseAnotherEmail);
            if (useAnotherEmailButton != null) { useAnotherEmailButton.onClick.AddListener(AskUseAnotherEmail); useAnotherEmailButton.gameObject.SetActive(false); }
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
            if (OnboardingSession.Retake) LockForRetake();
            else if (OnboardingSession.Social || OnboardingSession.HasAccount) UseAccountEmail();
            Limit(email, emailMax);          // once more after every script on the field has started: theirs is the older, shorter limit in a scene that was not rebuilt
            Show("");
            Validate();
            if (OnboardingSession.HasAccount) _ = CheckAccountIsStillThere();
            StartCoroutine(FocusFirstEmpty());
        }

        /// <summary>New17: the first empty box gets the focus when the page opens (the keyboard opens with it), once the layout has settled.</summary>
        IEnumerator FocusFirstEmpty()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.35f);
            foreach (TMP_InputField f in new[] { firstName, lastName, email, password, retypePassword })
            {
                if (f == null || !f.isActiveAndEnabled || !f.interactable || f.readOnly || T(f).Length > 0) continue;
                f.Select(); f.ActivateInputField();
                yield break;
            }
        }

        // New17: errors are shown when Continue is tapped or a field is left, never while the user is typing
        readonly HashSet<TMP_InputField> left = new HashSet<TMP_InputField>();
        bool submitted;
        [SerializeField] private string msgNoMatch = "The two passwords are not the same.";

        bool Judged(TMP_InputField f) => submitted || (f != null && left.Contains(f));

        /// <summary>After a tap on Continue that cannot go on: the first box that needs correcting gets the focus (what was typed stays).</summary>
        void FocusFirstWrong()
        {
            string pw = password != null ? password.text : "", re = retypePassword != null ? retypePassword.text : "";
            bool askingPassword = !(OnboardingSession.Social || OnboardingSession.HasAccount);
            TMP_InputField wrong =
                requireFirstAndLastName && T(firstName).Length == 0 ? firstName :
                requireFirstAndLastName && T(lastName).Length == 0 ? lastName :
                !emailFromAccount && (emailCheck == null || !emailCheck.Look(T(email))) ? email :
                askingPassword && pw.Length < passwordMin ? password :
                askingPassword && re != pw ? retypePassword : null;
            if (wrong != null && wrong.isActiveAndEnabled && wrong.interactable) { wrong.Select(); wrong.ActivateInputField(); }
        }

        bool emailFromAccount, busy;
        string flowMessage = "";            // what the confirming last said in the page's message line (cleared when the user types)
        float sentAt = -1f, nextLook;

        /// <summary>
        /// The sign-up has its account already (Google / Facebook, or the one this page made to confirm the email):
        /// everything about the password is hidden and the email is the account's own (it stays editable only for a
        /// Google / Facebook account that has none).
        /// </summary>
        void UseAccountEmail()
        {
            ShowPasswordRows(false);
            emailFromAccount = email != null && T(email).Length > 0;
            if (emailFromAccount) { email.readOnly = true; email.interactable = false; }
            if (OnboardingSession.HasAccount)
            {
                if (useAnotherEmailButton != null) useAnotherEmailButton.gameObject.SetActive(true);
                if (emailCheck != null) emailCheck.SetNote(SignUpAccount.ConfirmedSoFar ? msgConfirmed : msgWaiting, SignUpAccount.ConfirmedSoFar);
            }
        }

        /// <summary>
        /// New17, the retake from Settings: the names and birthday can be corrected, the email and the password are LOCKED (shown, not
        /// editable). The password row shows dots with "not changed here"; the retype row and the requirement ticks are hidden.
        /// </summary>
        void LockForRetake()
        {
            emailFromAccount = true;
            if (email != null) { email.readOnly = true; email.interactable = false; }
            ShowPasswordRows(false);
            if (password != null)
            {
                password.gameObject.SetActive(true);
                foreach (Transform r in password.transform.parent)
                    if (r == password.transform || r.name.StartsWith("PasswordText", StringComparison.Ordinal)) r.gameObject.SetActive(true);
                password.SetTextWithoutNotify("");
                password.readOnly = true; password.interactable = false;
                if (password.placeholder is TMP_Text hint) hint.text = msgPasswordLocked;
                Transform eye = password.transform.Find("EyeButton");
                if (eye != null) eye.gameObject.SetActive(false);
            }
            if (useAnotherEmailButton != null) useAnotherEmailButton.gameObject.SetActive(false);
            if (emailCheck != null) emailCheck.SetNote(msgEmailLocked, true);
        }

        [SerializeField] private string msgPasswordLocked = "•••••••• (not changed here)";
        [SerializeField] private string msgEmailLocked = "Your account's email. It is not changed here.";

        void ShowPasswordRows(bool show)
        {
            if (password == null || password.transform.parent == null) return;
            foreach (Transform row in password.transform.parent)
                if (row.name.StartsWith("Password", StringComparison.Ordinal) || row.name.StartsWith("Retype", StringComparison.Ordinal))
                    row.gameObject.SetActive(show);
        }

        /// <summary>The draft says this sign-up has an account, but nobody is signed in to it any more (signed out, removed): the sign-up starts that part again.</summary>
        async Task CheckAccountIsStillThere()
        {
            bool there = await FirebaseBootstrap.Ready && SignUpAccount.SignedInAs(OnboardingSession.AccountUid);
            if (this == null || there || !OnboardingSession.HasAccount) return;
            DropAccountFromThePage();
        }

        /// <summary>The page as it is without an account: the email can be typed, the password is asked for.</summary>
        void DropAccountFromThePage()
        {
            OnboardingSession.ForgetAccount();
            emailFromAccount = false; sentAt = -1f;
            if (email != null) { email.readOnly = false; email.interactable = true; }
            if (password != null) password.SetTextWithoutNotify("");
            if (retypePassword != null) retypePassword.SetTextWithoutNotify("");
            ShowPasswordRows(true);
            if (useAnotherEmailButton != null) useAnotherEmailButton.gameObject.SetActive(false);
            if (emailCheck != null) emailCheck.SetNote("", false);
            if (confirmPanel != null && confirmPanel.IsOpen) confirmPanel.Hide();
            Validate();
        }

        /// <summary>
        /// Back on the first page leaves the sign-up, so the user is asked first ("Leave sign-up?"). Leaving discards
        /// what was typed and signs a Google / Facebook account out again: see OnboardingLeaveGuard.
        /// </summary>
        void OnBack()
        {
            OnboardingLeaveGuard.RequestLeave();
        }

        static void Limit(TMP_InputField f, int n) { if (f != null) f.characterLimit = n; }
        static void Set(TMP_InputField f, string v) { if (f != null && !string.IsNullOrEmpty(v)) f.SetTextWithoutNotify(v); }
        static string T(TMP_InputField f) => f != null ? f.text.Trim() : "";

        void OnBirthday(DateTime d) { SetAge(d); Validate(); }

        void SetAge(DateTime d)
        {
            if (age != null) age.SetTextWithoutNotify(InputRules.CalculateAge(d).ToString());
        }

        void RevealPasswordRequirements()
        {
            if (password == null || !isActiveAndEnabled) return;
            ScrollRect scroll = password.GetComponentInParent<ScrollRect>();
            if (scroll != null) StartCoroutine(ScrollRequirementsIntoView(scroll));
        }

        IEnumerator ScrollRequirementsIntoView(ScrollRect scroll)
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
            Canvas.ForceUpdateCanvases();
            if (scroll == null || scroll.viewport == null || scroll.content == null || pwUpper == null || pwLength == null) yield break;

            Bounds requirements = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, (RectTransform)pwUpper.transform);
            requirements.Encapsulate(RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, (RectTransform)pwLength.transform));
            Bounds viewport = new Bounds(scroll.viewport.rect.center, scroll.viewport.rect.size);
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow <= 0f) yield break;

            float position = scroll.verticalNormalizedPosition;
            if (requirements.min.y < viewport.min.y)
                position -= (viewport.min.y - requirements.min.y) / overflow;
            else if (requirements.max.y > viewport.max.y)
                position += (requirements.max.y - viewport.max.y) / overflow;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        void Validate()
        {
            string pw = password != null ? password.text : "", re = retypePassword != null ? retypePassword.text : "";

            pwUpper?.SetMet(InputRules.HasUpper(pw)); pwLower?.SetMet(InputRules.HasLower(pw));
            pwNumber?.SetMet(InputRules.HasDigit(pw)); pwSpecial?.SetMet(InputRules.HasSpecial(pw));
            pwLength?.SetMet(pw.Length >= passwordMin && pw.Length <= passwordMax);
            bool pwOk = pw.Length >= passwordMin && pw.Length <= passwordMax && InputRules.HasUpper(pw) && InputRules.HasLower(pw) && InputRules.HasDigit(pw) && InputRules.HasSpecial(pw);
            bool match = pwOk && re.Length > 0 && re == pw;
            pwMatch?.SetMet(match);
            if (OnboardingSession.Social || OnboardingSession.HasAccount || OnboardingSession.Retake) { pwOk = true; match = true; }     // the account exists: no password is asked for here

            bool emailOk = emailFromAccount || (emailCheck != null && emailCheck.Look(T(email)));
            bool namesOk = !requireFirstAndLastName || (T(firstName).Length > 0 && T(lastName).Length > 0);
            bool dobOk = birthday != null && birthday.Value.HasValue && InputRules.CalculateAge(birthday.Value.Value) >= minimumAge;

            bool ready = pwOk && match && emailOk && namesOk && dobOk && !busy;
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;

            // a field's reason stands UNDER that field (New13 item 5A); passing messages are toasts (Say); the old free line stays empty
            FieldHint dobHint = birthday != null ? FieldHint.After(birthday.transform, feedbackText) : null;
            if (dobHint != null) { if (birthday.Value.HasValue && !dobOk) dobHint.Show(FieldHint.Kind.Error, string.Format(msgUnderAge, minimumAge)); else dobHint.Hide(); }
            FieldHint mailHint = email != null && emailCheck != null && !emailCheck.ShowsOwnMessages ? FieldHint.After(email.transform, feedbackText) : null;
            if (mailHint != null) { if (!emailOk && T(email).Length > 0 && Judged(email)) mailHint.Show(FieldHint.Kind.Error, emailCheck.Message); else mailHint.Hide(); }
            // the retyped password: named under its box once judged (the tick "Passwords match" shows the live state)
            bool askingPassword = !(OnboardingSession.Social || OnboardingSession.HasAccount);
            FieldHint retypeHint = askingPassword && retypePassword != null && retypePassword.isActiveAndEnabled ? FieldHint.After(retypePassword.transform, feedbackText) : null;
            if (retypeHint != null) { if (Judged(retypePassword) && re.Length > 0 && re != pw) retypeHint.Show(FieldHint.Kind.Error, msgNoMatch); else retypeHint.Hide(); }
            Show("");
        }


        void OnContinue()
        {
            if (busy) return;
            submitted = true;
            Validate();
            if (continueButton != null && !continueButton.interactable) { OnboardingNotices.Error(feedbackText, msgFillAll); FocusFirstWrong(); return; }
            OnboardingSession.FirstName = T(firstName); OnboardingSession.MiddleName = T(middleName); OnboardingSession.LastName = T(lastName);
            OnboardingSession.Username = "";                                                                      // no longer asked for
            if (OnboardingSession.Retake)
            {
                // New17: the retake keeps the account's email and password; nothing is confirmed or made here
                var retakeDob = birthday.Value.Value;
                OnboardingSession.BirthdayIso = retakeDob.ToString("yyyy-MM-dd");
                OnboardingSession.Age = InputRules.CalculateAge(retakeDob);
                OnboardingSession.SaveDraft();
                SceneNavigator.Go(nextScene);
                return;
            }
            OnboardingSession.Email = T(email).ToLowerInvariant();
            // memory only. With an account the password field is hidden: what was typed when the account was made is kept as it is.
            if (OnboardingSession.Social) OnboardingSession.Password = "";
            else if (!OnboardingSession.HasAccount && password != null) OnboardingSession.Password = password.text;
            var dob = birthday.Value.Value;
            OnboardingSession.BirthdayIso = dob.ToString("yyyy-MM-dd");
            OnboardingSession.Age = InputRules.CalculateAge(dob);
            OnboardingSession.SaveDraft();

            // a Google / Facebook email is confirmed by its provider; with the confirmation switched off (or in a scene built before
            // it existed) the sign-up goes on as before and the account is made on the Terms page. An account that exists already
            // (a sign-up that was begun with the confirmation on) still has to be confirmed.
            bool confirm = confirmPanel != null && (confirmEmailOnSignUp || OnboardingSession.HasAccount);
            if (OnboardingSession.Social || !confirm) { SceneNavigator.Go(nextScene); return; }
            _ = ConfirmThenGoOn();
        }

        // =====================================================================================
        //  Confirming the email
        // =====================================================================================
        /// <summary>
        /// Everything on the page is right: the account is made (once), the confirmation message is sent (once per visit),
        /// and the pop-up waits for the user to tap the link. An email that is confirmed already goes straight on.
        /// </summary>
        async Task ConfirmThenGoOn()
        {
            SetBusy(true);
            try
            {
                if (!ConnectivityMonitor.Online) { Say(msgNeedInternet); return; }
                Say(msgSending);

                if (!OnboardingSession.HasAccount)
                {
                    SignUpAccount.Result made = await SignUpAccount.CreateOrSignIn(OnboardingSession.Email, OnboardingSession.Password);
                    if (this == null) return;
                    if (!made.Ok) { Say(WordsFor(made)); return; }
                    if (!made.madeNew)
                    {
                        // this email and password had an account already. It may be a sign-up that was not finished (no profile:
                        // it carries on), but an account WITH a profile is somebody's finished account and is never signed up again.
                        UserProfileData existing = null;
                        try { existing = await UserProfileRepository.Load(); }
                        catch (Exception e) { { } }
                        if (this == null) return;
                        if (existing != null || !UserProfileRepository.NoProfileInCloud)
                        {
                            UserProfileRepository.SignOut();
                            Say(existing != null ? msgEmailInUse : msgCouldNot);
                            return;
                        }
                    }
                    OnboardingSession.AccountUid = made.uid; OnboardingSession.AccountMadeHere = made.madeNew;
                    OnboardingSession.SaveDraft();
                    UseAccountEmail();
                }

                SignUpAccount.Result check = await SignUpAccount.CheckConfirmed();
                if (this == null) return;
                if (check.outcome == SignUpAccount.Outcome.NotSignedIn) { DropAccountFromThePage(); Say(msgCouldNot); return; }
                if (check.Ok && check.confirmed) { GoOnConfirmed(); return; }
                if (!check.Ok) { Say(WordsFor(check)); return; }

                string status = "";
                if (sentAt < 0f)
                {
                    SignUpAccount.Result sent = await SignUpAccount.SendConfirmation();
                    if (this == null) return;
                    if (sent.Ok) sentAt = Time.unscaledTime;
                    else status = WordsFor(sent);               // the pop-up still opens: an earlier message may be in the inbox already
                }
                Say("");
                OpenConfirmPanel(status);
            }
            finally { if (this != null) SetBusy(false); }
        }

        void OpenConfirmPanel(string status)
        {
            if (confirmMessageText != null) confirmMessageText.text = string.Format(msgConfirmBody, OnboardingSession.Email);
            if (confirmStatusText != null) confirmStatusText.text = status ?? "";
            if (emailCheck != null) emailCheck.SetNote(msgWaiting, false);
            nextLook = Time.unscaledTime + Mathf.Max(1f, lookEverySeconds);
            ShowSendAgain();
            if (confirmPanel != null) confirmPanel.Show();
        }

        /// <summary>Asks whether the email was confirmed. `said` = the user tapped "I have confirmed" (then "not yet" is said too).</summary>
        async Task LookWhetherConfirmed(bool said)
        {
            if (busy || !OnboardingSession.HasAccount) return;
            busy = true;                        // the page's Continue stays as it is: only a second look is held back
            try
            {
                SignUpAccount.Result check = await SignUpAccount.CheckConfirmed();
                if (this == null) return;
                if (check.Ok && check.confirmed) { busy = false; GoOnConfirmed(); return; }
                if (said && confirmStatusText != null) confirmStatusText.text = check.Ok ? msgNotYet : WordsFor(check);
            }
            finally { if (this != null) { busy = false; nextLook = Time.unscaledTime + Mathf.Max(1f, lookEverySeconds); } }
        }

        async Task SendAgain()
        {
            if (busy || SignUpAccount.SecondsUntilSendAgain(sentAt, Time.unscaledTime, sendAgainAfterSeconds) > 0) return;
            busy = true;
            try
            {
                if (confirmStatusText != null) confirmStatusText.text = msgSending;
                SignUpAccount.Result sent = await SignUpAccount.SendConfirmation();
                if (this == null) return;
                if (sent.Ok) sentAt = Time.unscaledTime;
                if (confirmStatusText != null) confirmStatusText.text = sent.Ok ? msgSentAgain : WordsFor(sent);
            }
            finally { if (this != null) { busy = false; ShowSendAgain(); } }
        }

        void GoOnConfirmed()
        {
            if (emailCheck != null) emailCheck.SetNote(msgConfirmed, true);
            if (confirmPanel != null && confirmPanel.IsOpen) confirmPanel.Hide();
            FormalToast.ShowAfterNextScene(FormalToast.Kind.Success, msgEmailConfirmedToast);       // New17: said on Step 2, where the user lands
            SceneNavigator.Go(nextScene);
        }

        [SerializeField] private string msgEmailConfirmedToast = "Email confirmed. Let's set up your skin profile.";

        void ShowSendAgain()
        {
            int left = SignUpAccount.SecondsUntilSendAgain(sentAt, Time.unscaledTime, sendAgainAfterSeconds);
            if (sendAgainButton != null) sendAgainButton.interactable = left == 0;
            if (sendAgainLabel != null) sendAgainLabel.text = left == 0 ? sendAgainText : string.Format(sendAgainWaitText, left);
        }

        void Update()
        {
            if (confirmPanel == null || !confirmPanel.IsOpen) return;
            ShowSendAgain();
            // the app looks by itself, so the user only has to tap the link in the message
            if (!busy && Time.unscaledTime >= nextLook) _ = LookWhetherConfirmed(false);
        }

        // back from the mail app: look at once
        /// <summary>The "Open Gmail" button. Says what happened when it was not Gmail itself that opened.</summary>
        void OpenGmail()
        {
            MailApp.Result result = MailApp.OpenGmail();
            if (confirmStatusText == null) return;
            if (result == MailApp.Result.Store) confirmStatusText.text = msgGmailFromStore;
            else if (result == MailApp.Result.MailApp) confirmStatusText.text = msgOwnMailApp;
            else if (result == MailApp.Result.None) confirmStatusText.text = msgNoMailApp;
        }

        void OnApplicationFocus(bool focused) { if (focused && confirmPanel != null && confirmPanel.IsOpen && !busy) _ = LookWhetherConfirmed(false); }

        // New17, layer 2: the confirmation page on glowupapp.xyz opens the app again (glowup://verified): look at once
        void OnEnable() { Application.deepLinkActivated += OnDeepLink; }
        void OnDisable() { Application.deepLinkActivated -= OnDeepLink; }
        void OnDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url) || url.IndexOf("verified", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (OnboardingSession.HasAccount && !busy) _ = LookWhetherConfirmed(false);
        }
        void OnApplicationPause(bool paused) { if (!paused && confirmPanel != null && confirmPanel.IsOpen && !busy) _ = LookWhetherConfirmed(false); }

        /// <summary>"Use another email": asked first, because the sign-up for the old email is dropped.</summary>
        void AskUseAnotherEmail()
        {
            if (busy || !OnboardingSession.HasAccount) return;
            string question = string.Format(changeMessage, OnboardingSession.Email);
            if (questionDialog != null) questionDialog.Ask(changeTitle, question, changeConfirm, () => { _ = UseAnotherEmail(); }, true);
            else _ = UseAnotherEmail();
        }

        async Task UseAnotherEmail()
        {
            if (busy) return;
            SetBusy(true);
            try
            {
                // the account this sign-up made has no profile yet: removed, so that email stays free. When it cannot be removed
                // now (no internet), it is left signed out: the same email and password carry on with it another time.
                if (OnboardingSession.AccountMadeHere) await SignUpAccount.Delete(OnboardingSession.AccountUid, 10f);
                if (this == null) return;
                UserProfileRepository.SignOut();
                DropAccountFromThePage();
                Say("");
            }
            finally { if (this != null) SetBusy(false); }
        }

        string WordsFor(SignUpAccount.Result result)
        {
            switch (result.outcome)
            {
                case SignUpAccount.Outcome.Offline: return msgNeedInternet;
                case SignUpAccount.Outcome.EmailInUse: return msgEmailInUse;
                case SignUpAccount.Outcome.TooManyTries: return msgTooManyTries;
                case SignUpAccount.Outcome.WeakPassword:
                case SignUpAccount.Outcome.BadEmail: return string.IsNullOrEmpty(result.detail) ? msgCouldNot : result.detail;
                default: return msgCouldNot;
            }
        }

        void SetBusy(bool on) { busy = on; Validate(); }
        /// <summary>A line of the confirming in the page's message line; it stays until the user types again.</summary>
        /// <summary>A passing message of the sign-up (sending the e-mail, an error after Continue): the one toast at the top (New13 item 5B).</summary>
        void Say(string m)
        {
            flowMessage = m ?? "";
            if (flowMessage.Length > 0) OnboardingNotices.Toast(feedbackText, flowMessage == msgSending ? FormalToast.Kind.Info : FormalToast.Kind.Error, flowMessage);
            Show("");
        }

        void Show(string m) { if (feedbackText != null) feedbackText.text = m; }
    }
}
