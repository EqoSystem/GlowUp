using System;
using System.Threading.Tasks;
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
                        catch (Exception e) { Debug.LogWarning("Session restore: " + e.Message); }
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
            catch (Exception e) { Debug.LogWarning("Session restore skipped: " + e.Message); }
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
                        if (!TaskLimits.IsNetworkProblem(e)) { Fail(msgBadCredentials); return; }
                        Debug.LogWarning("Sign-in: no answer from the sign-in service, checking on this phone instead: " + e.Message);
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
                    catch (Exception e) { Debug.LogWarning("Sign-in: the profile could not be loaded yet: " + e.Message); }
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
            catch (Exception e) { Debug.LogWarning("Sign-in failed: " + e.Message); Fail(msgBadCredentials); }
            finally { if (this != null && signInButton != null) signInButton.interactable = true; }
        }

        async void ForgotPassword()
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
            OnboardingSession.Social = false;
            // "Sign Up" always starts a sign-up of its own: an account left in the draft by an earlier, abandoned one is not taken over
            // (nobody is signed in to it here; Step 1 would notice that too)
            if (OnboardingSession.HasAccount) OnboardingSession.ForgetAccount();
            ContextualHintController.MarkNewUser(true);
            SceneNavigator.Go(onboardingScene);
        }

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
                catch (Exception e) { Debug.LogWarning("Sign-in: the profile could not be loaded yet: " + e.Message); }
                if (this == null) return;
                if (HasSkinProfile(profile)) SceneNavigator.Go(dashboardScene);       // already has a skin profile: straight to the dashboard
                else if (FinishedSignUp(profile)) RetakeQuizOnly();                   // a finished account whose quiz was reset: the quiz only
                else StartSocialSignUp(outcome.User);                                  // no skin profile yet: the skin quiz (Onboarding Step 2)
            }
            catch (Exception e) { Debug.LogWarning(name + " sign-in failed: " + e); FailWithRetry(SocialSignIn.DidNotComplete, name); }
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
