using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Onboarding;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Auth
{
    /// <summary>
    /// Login / Sign-up screen logic: real Firebase email+password sign-in (online), Argon2id
    /// offline check (no internet), forgot-password email, sign-up routing, social buttons and
    /// the Privacy / Terms links. Auto-skips to the dashboard for an already signed-in user.
    /// All texts, scene names and links are Inspector fields.
    /// </summary>
    public class LoginFlowController : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TextMeshProUGUI feedbackText;
        PasswordLengthMask passwordMask;          // added at run time: when the password is sent, the asterisks become a long row (nobody can count them)

        [Header("Buttons")]
        [SerializeField] private Button signInButton;
        [SerializeField] private Button forgotPasswordButton;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button googleButton;
        [SerializeField] private Button privacyPolicyButton;
        [SerializeField] private Button termsButton;
        [SerializeField] private TextMeshProUGUI legalEntryText;

        [Header("Forgot password (New17)")]
        [Tooltip("The \"Reset your password\" pop-up. Empty in an older scene: the Email field is used directly, as before.")]
        [SerializeField] private OverlayPanel resetPanel;
        [SerializeField] private TMP_InputField resetEmailInput;
        [SerializeField] private TextMeshProUGUI resetMessageText;
        [SerializeField] private Button resetSendButton;
        [SerializeField] private Button resetCancelButton;
        [SerializeField] private string msgResetSending = "Sending reset link...";
        [Tooltip("The same words whether or not an account uses that email, so nobody can find out which emails have an account")]
        [SerializeField] private string msgResetGeneric = "If an account uses that email, a reset link is on its way. Check your inbox and spam folder.";
        [SerializeField] private string msgResetBadEmail = "Check the email address: it should look like name@example.com.";
        [SerializeField] private string msgResetFailed = "The reset link could not be sent. Try again in a moment.";

        [Header("Scenes")]
        [SerializeField] private string dashboardScene = SceneNames.Dashboard;
        [SerializeField] private string onboardingScene = SceneNames.Onboarding1;
        [Tooltip("Where someone who signed in with Google goes when they have no skin profile yet (the skin quiz; Step 1 is skipped because the account already exists)")]
        [SerializeField] private string socialOnboardingScene = SceneNames.Onboarding2;
        [SerializeField] private bool autoSkipIfSignedIn = true;

        [Header("Legal documents")]
        [SerializeField] private string legalDocumentsScene = "TermsAndConditionScene";

        [Header("Messages")]
        [SerializeField] private string msgEnterEmail = "Please enter your email address.";
        [SerializeField] private string msgEnterPassword = "Please enter your password.";
        [SerializeField] private string msgBadCredentials = "Email or password is incorrect.";
        [SerializeField] private string msgNotRegistered = "This email is not registered. Check it, or tap Sign Up.";
        [SerializeField] private string msgWrongPassword = "Password is incorrect.";
        [SerializeField] private string msgProfileNotLoaded = "You are signed in, but your profile could not be loaded. Check the internet connection and tap Log-In again.";
        [Tooltip("How long to wait for the sign-in service before checking the email and password on this phone instead (seconds)")]
        [SerializeField] private float signInWaitSeconds = 20f;
        [SerializeField] private string msgOfflineNoRecord = "You are offline and this account hasn't been used on this device yet.";
        [SerializeField] private string msgResetSent = "Verification email sent. Check your inbox to change your password.";
        [SerializeField] private string msgResetNeedsOnline = "Connect to the internet to reset your password.";
        [SerializeField] private string msgSocialNotConfigured = "{0} sign-in isn't available right now. Try again later, or sign in with your email.";
        [SerializeField] private string msgSocialNeedsOnline = "Connect to the internet to sign in with {0}.";
        [SerializeField] private string msgSocialOpening = "Opening {0} sign-in...";

        [Header("Colours")]
        [SerializeField] private Color errorColor = new Color32(179, 64, 47, 255);
        [SerializeField] private Color infoColor = new Color32(62, 142, 90, 255);

        void Awake()
        {
            // a scene saved earlier keeps the old wording, which named the Firebase console: plain words for users (7 November 2026)
            if (string.IsNullOrWhiteSpace(msgSocialNotConfigured) || msgSocialNotConfigured.IndexOf("Firebase", StringComparison.OrdinalIgnoreCase) >= 0)
                msgSocialNotConfigured = "{0} sign-in isn't available right now. Try again later, or sign in with your email.";
            // both boxes start EMPTY (the grey hint shows until the first letter); nothing has to be deleted before typing
            ClearField(emailInput); ClearField(passwordInput);
            if (passwordInput != null && !passwordInput.TryGetComponent(out passwordMask)) passwordMask = passwordInput.gameObject.AddComponent<PasswordLengthMask>();

            // clearing the e-mail also clears the password
            if (emailInput != null) emailInput.onValueChanged.AddListener(value => { if (string.IsNullOrEmpty(value) && passwordInput != null && !string.IsNullOrEmpty(passwordInput.text)) passwordInput.text = ""; });

            Hook(signInButton, SignIn); Hook(forgotPasswordButton, ForgotPassword); Hook(signUpButton, SignUp);
            // 17 Nov 2026: the first tap after typing closes the keyboard and the card slides back under the finger, so the click was lost
            // (the tap sound played, nothing happened, the second tap worked). These buttons also take a tap the page moved under.
            TapThroughShift.On(signInButton); TapThroughShift.On(forgotPasswordButton); TapThroughShift.On(signUpButton);
            // the two text links are about 23 dp high: their tappable area is made taller than their words (to about 45 dp)
            foreach (Button link in new[] { forgotPasswordButton, signUpButton })
                if (link != null && link.targetGraphic != null) link.targetGraphic.raycastPadding = new Vector4(0f, -30f, 0f, -30f);
            Hook(resetSendButton, SendReset);
            Hook(resetCancelButton, () => { if (resetPanel != null) resetPanel.Hide(); });
            if (resetEmailInput != null) resetEmailInput.onValueChanged.AddListener(_ => SayReset(""));        // a message goes once the user types again
            Hook(googleButton, () => Social("Google"));
            if (privacyPolicyButton != null) Hook(privacyPolicyButton, OpenLegalDocuments);
            if (termsButton != null && termsButton != privacyPolicyButton) Hook(termsButton, OpenLegalDocuments);
            if (privacyPolicyButton == null && termsButton == null)
            {
                if (legalEntryText == null)
                {
                    GameObject legalObject = GameObject.Find("TermsText");
                    if (legalObject != null) legalEntryText = legalObject.GetComponent<TextMeshProUGUI>();
                }
                if (legalEntryText != null) FooterLinks(legalEntryText);
            }
        }

        /// <summary>
        /// The footer line: "Read the Privacy Policy and Terms and Conditions before signing up." The two names are separate links in
        /// gold, underlined, each opening the document at its own part. The line is at least 132 units high (44 dp) so a finger hits it.
        /// </summary>
        void FooterLinks(TextMeshProUGUI line)
        {
            Color gold = LinkGold();
            string hex = ColorUtility.ToHtmlStringRGB(gold);
            line.richText = true;
            line.text = "Read the <link=\"privacy\"><color=#" + hex + "><u>" + UiWording.PrivacyPolicy + "</u></color></link> and <link=\"terms\"><color=#" + hex + "><u>"
                + UiWording.TermsAndConditions + "</u></color></link> before signing up.";
            line.enableWordWrapping = true;
            line.raycastTarget = true;
            RectTransform rt = line.rectTransform;
            if (rt.rect.height < 132f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + (132f - rt.rect.height));     // grows equally up and down
            Button old = line.GetComponent<Button>();
            if (old != null) old.onClick.RemoveAllListeners();
            TextLinkClicks clicks = line.GetComponent<TextLinkClicks>() ?? line.gameObject.AddComponent<TextLinkClicks>();
            clicks.Clicked = id => OpenLegalDocuments(id == "terms" ? UiWording.TermsAndConditions : UiWording.PrivacyPolicy);
        }

        /// <summary>The logo's gold, darkened on a light page until small text on it reads at 4.5 : 1 (WCAG AA).</summary>
        static Color LinkGold()
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Color gold = p.gold;
            if (RuntimeTheme.IsDark) return gold;
            for (int i = 0; i < 20 && Contrast(gold, p.surface) < 4.6f; i++) gold = Color.Lerp(gold, Color.black, 0.08f);
            return gold;
        }

        static float Contrast(Color a, Color b)
        {
            float L(Color c) { float f(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); return 0.2126f * f(c.r) + 0.7152f * f(c.g) + 0.0722f * f(c.b); }
            float x = L(a), y = L(b);
            return (Mathf.Max(x, y) + 0.05f) / (Mathf.Min(x, y) + 0.05f);
        }

        static void Hook(Button b, Action a) { if (b != null) b.onClick.AddListener(() => a()); }

        void OpenLegalDocuments() => OpenLegalDocuments(null);

        void OpenLegalDocuments(string section)
        {
            GlowUp.Onboarding.TermsAcceptanceFlowController.OpenReadOnlyFromLogin(section);
            SceneNavigator.Go(legalDocumentsScene);
        }

        static void ClearField(TMP_InputField field)
        {
            if (field == null) return;
            field.SetTextWithoutNotify("");
            if (field.textComponent != null) field.textComponent.text = "";
            if (field.placeholder != null) { field.placeholder.gameObject.SetActive(true); field.placeholder.enabled = true; }
        }

        async void Start()
        {
            Show("");
            if (!autoSkipIfSignedIn) return;
            // Whoever was signed in stays signed in: after closing the app, after the phone restarts, and after a crash.
            try
            {
                FirebaseUser user = await FirebaseBootstrap.Ready ? FirebaseAuth.DefaultInstance.CurrentUser : null;
                if (this == null) return;
                if (user != null)
                {
                    UserProfileData profile = UserProfileRepository.Cached();          // the usual case: on the phone already, no waiting
                    if (profile == null)
                    {
                        try { profile = await UserProfileRepository.Load(); }
                        catch (Exception e) { { } }
                        if (this == null) return;
                    }
                    // a quiz retake that was going on when the app closed: back into the quiz only (Steps 2 to 4)
                    OnboardingSession.LoadDraft();
                    if (OnboardingSession.Retake && profile != null) { SceneNavigator.Go(quizScene); return; }
                    // a Google account whose sign-up is finished but whose quiz was reset: the quiz only, never the account steps
                    if (!HasSkinProfile(profile) && FinishedSignUp(profile)) { RetakeQuizOnly(); return; }
                    // a Google account that never finished the skin quiz carries on with it
                    if (!HasSkinProfile(profile) && SocialSignIn.ProviderOf(user) != SocialSignIn.Password) { StartSocialSignUp(user); return; }
                    // a sign-up that was begun on this phone and not finished (Step 1 made its account to confirm the email) carries on there
                    OnboardingSession.LoadDraft();
                    if (profile == null && OnboardingSession.HasAccount && OnboardingSession.AccountUid == user.UserId)
                    {
                        ContextualHintController.MarkNewUser(true);
                        SceneNavigator.Go(onboardingScene);
                        return;
                    }
                    SceneNavigator.Go(dashboardScene);
                    return;
                }
                // signed in without internet last time: that session is kept too
                if (!string.IsNullOrEmpty(UserProfileRepository.OfflineEmail) && UserProfileRepository.Cached() != null)
                    SceneNavigator.Go(dashboardScene);
            }
            catch (Exception e) { { } }
        }

        async void SignIn()
        {
            string email = emailInput != null ? emailInput.text.Trim() : "";
            string pw = passwordInput != null ? passwordInput.text : "";
            if (email.Length == 0) { Fail(msgEnterEmail); return; }
            if (pw.Length == 0) { Fail(msgEnterPassword); return; }
            if (passwordMask != null) passwordMask.Mask();
            if (signInButton != null) signInButton.interactable = false;

            try
            {
                // Connected does not always mean reachable (Wi-Fi with no internet, a very weak signal). When the
                // sign-in service gives no answer, the email and password are checked against this phone instead.
                bool reached = false;
                if (ConnectivityMonitor.Online && await FirebaseBootstrap.Ready)
                {
                    // only this step decides whether the email and password are right
                    try
                    {
                        await FirebaseAuth.DefaultInstance.SignInWithEmailAndPasswordAsync(email, pw).OrTimeout(signInWaitSeconds, "Signing in");
                        reached = true;
                    }
                    catch (Exception e)
                    {
                        if (!TaskLimits.IsNetworkProblem(e))
                        {
                            // 17 Nov 2026 (owner): an email with no account says so, instead of "Email or password is incorrect".
                            // Firebase tells the two apart (UserNotFound / WrongPassword) only while its "email enumeration protection"
                            // is off; with it on, both come back as InvalidCredential and the general message stays.
                            AuthError why = AuthErrorOf(e);
                            Fail(why == AuthError.UserNotFound ? msgNotRegistered : why == AuthError.WrongPassword ? msgWrongPassword : msgBadCredentials);
                            return;
                        }
                        { }
                    }
                }

                if (reached)
                {
                    // signed in: nothing after this point may send the user back with "wrong password"
                    UserProfileRepository.OfflineEmail = null;
                    OfflineCredentialStore.Remember(email, pw);        // Argon2id verifier for later offline use (runs in the background)
                    SavedAccounts.Remember(email);                     // only the email: it is offered under the email field next time
                    UserProfileData profile = null;
                    try { profile = await UserProfileRepository.Load(); }
                    catch (Exception e) { { } }
                    if (this == null) return;
                    if (profile == null && !UserProfileRepository.NoProfileInCloud)
                    {
                        // nobody knows whether this account has a profile (the cloud did not answer): it is NOT sent through
                        // the sign-up, which would write a new profile over the real one
                        Fail(msgProfileNotLoaded);
                        return;
                    }
                    if (profile == null)
                    {
                        // an account without a profile (a sign-up that was not finished): the sign-up continues WITH this account,
                        // so Step 1 asks for no second password and makes no second account
                        FirebaseUser signedIn = FirebaseAuth.DefaultInstance.CurrentUser;
                        if (signedIn != null) OnboardingSession.BeginExisting(signedIn.UserId, signedIn.Email);
                        ContextualHintController.MarkNewUser(true);
                    }
                    SceneNavigator.Go(profile != null ? dashboardScene : onboardingScene);
                }
                else if (OfflineCredentialStore.HasCredential(email) && await OfflineCredentialStore.CheckAsync(email, pw))
                {
                    if (this == null) return;
                    UserProfileRepository.OfflineEmail = email;         // the copy of this person's profile on the device is used
                    SavedAccounts.Remember(email);
                    SceneNavigator.Go(dashboardScene);
                }
                else Fail(OfflineCredentialStore.HasCredential(email) ? msgBadCredentials : msgOfflineNoRecord);
            }
            catch (Exception e) { { } Fail(msgBadCredentials); }
            finally { if (this != null && signInButton != null) signInButton.interactable = true; }
        }

        /// <summary>
        /// Forgot Password? opens "Reset your password": the email typed on the login page is already in its box; with none typed the
        /// box is empty and has the focus. The pop-up sends Firebase's own password-reset email.
        /// </summary>
        void ForgotPassword()
        {
            if (resetPanel == null || resetEmailInput == null) { ForgotPasswordDirect(); return; }
            string typed = emailInput != null ? emailInput.text.Trim() : "";
            resetEmailInput.text = typed;
            SayReset("");
            if (resetSendButton != null) resetSendButton.interactable = true;
            resetPanel.Show();
            if (typed.Length == 0) { resetEmailInput.Select(); resetEmailInput.ActivateInputField(); }
        }

        void SayReset(string message) { if (resetMessageText != null) resetMessageText.text = message; }

        static bool LooksLikeEmail(string s)
        {
            int at = s.IndexOf('@');
            return at > 0 && at == s.LastIndexOf('@') && s.IndexOf('.', at) > at + 1 && !s.EndsWith(".") && s.IndexOf(' ') < 0;
        }

        async void SendReset()
        {
            string email = resetEmailInput != null ? resetEmailInput.text.Trim() : "";
            // checked when Send is tapped, never while typing; the typed text stays in the box
            if (email.Length == 0) { SayReset(msgEnterEmail); return; }
            if (!LooksLikeEmail(email)) { SayReset(msgResetBadEmail); return; }
            if (!ConnectivityMonitor.Online) { Toast()?.Show(FormalToast.Kind.Error, msgResetNeedsOnline); return; }
            if (resetSendButton != null) resetSendButton.interactable = false;          // no double sending
            Toast()?.Show(FormalToast.Kind.Info, msgResetSending);
            try
            {
                await FirebaseBootstrap.Ready;
                await FirebaseAuth.DefaultInstance.SendPasswordResetEmailAsync(email).OrTimeout(signInWaitSeconds, "Sending the reset email");
                ResetDone();
            }
            catch (Exception e)
            {
                if (this == null) return;
                if (resetSendButton != null) resetSendButton.interactable = true;
                AuthError code = AuthErrorOf(e);
                if (code == AuthError.UserNotFound) { ResetDone(); return; }            // the same answer as a sent link: nothing reveals the account
                if (code == AuthError.InvalidEmail) { SayReset(msgResetBadEmail); return; }
                Toast()?.Show(FormalToast.Kind.Error, TaskLimits.IsNetworkProblem(e) ? msgResetNeedsOnline : msgResetFailed);
            }
        }

        void ResetDone()
        {
            if (this == null) return;
            if (resetPanel != null) resetPanel.Hide();
            Toast()?.Show(FormalToast.Kind.Success, msgResetGeneric);
        }

        static AuthError AuthErrorOf(Exception e)
        {
            for (Exception x = e; x != null; x = x.InnerException)
                if (x is FirebaseException f) return (AuthError)f.ErrorCode;
            if (e is AggregateException a)
                foreach (Exception inner in a.Flatten().InnerExceptions)
                    for (Exception x = inner; x != null; x = x.InnerException)
                        if (x is FirebaseException f) return (AuthError)f.ErrorCode;
            return AuthError.None;
        }

        async void ForgotPasswordDirect()
        {
            string email = emailInput != null ? emailInput.text.Trim() : "";
            if (email.Length == 0) { Fail(msgEnterEmail); return; }
            if (!ConnectivityMonitor.Online) { Fail(msgResetNeedsOnline); return; }
            try
            {
                await FirebaseBootstrap.Ready;
                await FirebaseAuth.DefaultInstance.SendPasswordResetEmailAsync(email).OrTimeout(signInWaitSeconds, "Sending the reset email");
                Show(msgResetSent, infoColor);
            }
            catch (Exception e) { Fail(TaskLimits.IsNetworkProblem(e) ? msgResetNeedsOnline : e.InnerException != null ? e.InnerException.Message : e.Message); }
        }

        void SignUp()
        {
            if (ScreenTransition.IsBusy || signUpOpening) return;         // one tap is enough: a second tap used to queue another load
            OnboardingSession.Social = false;
            // "Sign Up" always starts a sign-up of its own: an account left in the draft by an earlier, abandoned one is not taken over
            // (nobody is signed in to it here; Step 1 would notice that too)
            if (OnboardingSession.HasAccount) OnboardingSession.ForgetAccount();
            ContextualHintController.MarkNewUser(true);
            // The sign-up screen is the biggest one and nothing covers the change, so the log-in page used to stand still for a moment
            // after the tap and looked frozen (17 Nov 2026). The button answers at once: a spinner and "Opening...".
            signUpOpening = true;
            if (signUpButton != null) ButtonBusy.Set(signUpButton, signUpButton.GetComponentInChildren<TMP_Text>(true), true, msgOpeningSignUp);
            SceneNavigator.Go(onboardingScene);
        }

        bool signUpOpening;
        [SerializeField] private string msgOpeningSignUp = "Opening...";

        bool socialBusy;

        /// <summary>The Google button. A first-time user goes on to the sign-up steps (without the password part).</summary>
        async void Social(string name)
        {
            if (socialBusy) return;
            if (!ConnectivityMonitor.Online) { Fail(string.Format(msgSocialNeedsOnline, name)); return; }

            socialBusy = true;
            Show(string.Format(msgSocialOpening, name), infoColor);
            try
            {
                if (!await FirebaseBootstrap.Ready) { Fail(string.Format(msgSocialNotConfigured, name)); return; }
                SocialSignIn.Outcome outcome = await SocialSignIn.WithGoogle();
                if (this == null) return;
                if (outcome.Cancelled) { Show(""); return; }
                if (!outcome.Ok) { FailWithRetry(outcome.Problem, name); return; }

                UserProfileRepository.OfflineEmail = null;
                UserProfileData profile = null;
                try { profile = await UserProfileRepository.Load(); }
                catch (Exception e) { { } }
                if (this == null) return;
                if (HasSkinProfile(profile)) SceneNavigator.Go(dashboardScene);       // already has a skin profile: straight to the dashboard
                else if (FinishedSignUp(profile)) RetakeQuizOnly();                   // a finished account whose quiz was reset: the quiz only
                else StartSocialSignUp(outcome.User);                                  // no skin profile yet: the skin quiz (Onboarding Step 2)
            }
            catch (Exception e) { { } FailWithRetry(SocialSignIn.DidNotComplete, name); }
            finally { socialBusy = false; }
        }

        /// <summary>A skin profile record = a saved profile that has a skin type.</summary>
        static bool HasSkinProfile(UserProfileData profile) => profile != null && !string.IsNullOrWhiteSpace(profile.skinType);

        /// <summary>The account went through the whole sign-up once (the Terms were accepted): its account steps never run again.</summary>
        static bool FinishedSignUp(UserProfileData profile) => profile != null && profile.termsAccepted;

        [Tooltip("The first page of the skin quiz (Onboarding Step 2): a finished account whose quiz was reset goes here, never to Step 5")]
        [SerializeField] private string quizScene = SceneNames.Onboarding2;

        /// <summary>The skin quiz (Steps 2 to 4) as a retake: it saves the four skin fields only and returns to the dashboard.</summary>
        void RetakeQuizOnly()
        {
            OnboardingSession.BeginRetake(dashboardScene);
            SceneNavigator.Go(quizScene);
        }

        void StartSocialSignUp(FirebaseUser user)
        {
            OnboardingSession.BeginSocial(user.Email, user.DisplayName);
            ContextualHintController.MarkNewUser(true);
            SceneNavigator.Go(string.IsNullOrEmpty(socialOnboardingScene) ? onboardingScene : socialOnboardingScene);
        }

        void Fail(string m) { Show(m, errorColor); ContextualHintController.Instance?.ReportValidationError(); }

        /// <summary>A sign-in with Google that did not finish: a friendly message with a "Try again" link (never a raw error page).</summary>
        void FailWithRetry(string m, string name)
        {
            FormalToast box = Toast();
            if (box == null) { Fail(m); return; }
            box.Show(FormalToast.Kind.Error, StatusMessage.Plain(m), "Try again", () => Social(name));
            ContextualHintController.Instance?.ReportValidationError();
        }

        // ---------------------------------------------------------------------------------------
        //  Messages: ONE formal toast inside the card, directly above the footer line (10 November 2026). The old message line
        //  under the Log-In button is never shown any more: wrapped in a status box it collapsed to one letter per line.
        //  Other parts of the page (the space guard of the boxes, the saved e-mails) still write into that line: what they
        //  write is moved into the toast (Update).
        // ---------------------------------------------------------------------------------------
        FormalToast toast;
        string mirrored = "";

        FormalToast Toast()
        {
            if (toast != null) return toast;
            RectTransform anchor = legalEntryText != null ? legalEntryText.rectTransform : null;
            RectTransform card = anchor != null ? anchor.parent as RectTransform : feedbackText != null ? feedbackText.rectTransform.parent as RectTransform : null;
            RectTransform widthFrom = emailInput != null ? (RectTransform)emailInput.transform : null;
            if (card == null) return null;
            Canvas.ForceUpdateCanvases();
            toast = FormalToast.Above(card, anchor, widthFrom, feedbackText != null ? feedbackText : legalEntryText);
            return toast;
        }

        void Show(string m, Color? c = null)
        {
            if (feedbackText != null) { feedbackText.text = ""; feedbackText.gameObject.SetActive(false); }
            mirrored = "";
            FormalToast box = Toast();
            if (box == null) return;
            if (string.IsNullOrEmpty(m)) { box.Hide(); return; }
            bool error = c.HasValue && c.Value == errorColor;
            box.Show(error ? FormalToast.Kind.Error : FormalToast.Kind.Info, StatusMessage.Plain(m));
        }

        void Update()
        {
            if (feedbackText == null) return;
            string written = feedbackText.text ?? "";
            if (written.Length == 0 || written == mirrored) { if (feedbackText.gameObject.activeSelf) feedbackText.gameObject.SetActive(false); return; }
            mirrored = written;
            FormalToast box = Toast();
            bool info = feedbackText.color == infoColor;
            feedbackText.gameObject.SetActive(false);
            if (box != null) box.Show(info ? FormalToast.Kind.Info : FormalToast.Kind.Error, StatusMessage.Plain(written));
        }
    }
}
