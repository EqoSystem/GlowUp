using System;
using System.Threading.Tasks;
using Firebase.Auth;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Onboarding;
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
        [SerializeField] private string msgSocialNotConfigured = "{0} sign-in isn't set up yet. Enable it in the Firebase console and add the provider plugin.";
        [SerializeField] private string msgSocialNeedsOnline = "Connect to the internet to sign in with {0}.";
        [SerializeField] private string msgSocialOpening = "Opening {0} sign-in...";

        [Header("Colours")]
        [SerializeField] private Color errorColor = new Color32(179, 64, 47, 255);
        [SerializeField] private Color infoColor = new Color32(62, 142, 90, 255);

        void Awake()
        {
            // both boxes start EMPTY (the grey hint shows until the first letter); nothing has to be deleted before typing
            ClearField(emailInput); ClearField(passwordInput);

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
                if (legalEntryText != null)
                {
                    legalEntryText.text = "Read the Privacy Policy and Terms before signing up";
                    legalEntryText.raycastTarget = true;
                    Button legalButton = legalEntryText.GetComponent<Button>();
                    if (legalButton == null) legalButton = legalEntryText.gameObject.AddComponent<Button>();
                    legalButton.targetGraphic = legalEntryText;
                    legalButton.transition = Selectable.Transition.None;
                    Hook(legalButton, OpenLegalDocuments);
                }
            }
        }

        static void Hook(Button b, Action a) { if (b != null) b.onClick.AddListener(() => a()); }

        void OpenLegalDocuments()
        {
            GlowUp.Onboarding.TermsAcceptanceFlowController.OpenReadOnlyFromLogin();
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
                if (!outcome.Ok) { Fail(outcome.Problem); return; }

                UserProfileRepository.OfflineEmail = null;
                UserProfileData profile = null;
                try { profile = await UserProfileRepository.Load(); }
                catch (Exception e) { Debug.LogWarning("Sign-in: the profile could not be loaded yet: " + e.Message); }
                if (this == null) return;
                if (HasSkinProfile(profile)) SceneNavigator.Go(dashboardScene);       // already has a skin profile: straight to the dashboard
                else StartSocialSignUp(outcome.User);                                  // no skin profile yet: the skin quiz (Onboarding Step 2)
            }
            catch (Exception e) { Debug.LogWarning(name + " sign-in failed: " + e); Fail(string.Format(msgSocialNotConfigured, name)); }
            finally { socialBusy = false; }
        }

        /// <summary>A skin profile record = a saved profile that has a skin type.</summary>
        static bool HasSkinProfile(UserProfileData profile) => profile != null && !string.IsNullOrWhiteSpace(profile.skinType);

        void StartSocialSignUp(FirebaseUser user)
        {
            OnboardingSession.BeginSocial(user.Email, user.DisplayName);
            ContextualHintController.MarkNewUser(true);
            SceneNavigator.Go(string.IsNullOrEmpty(socialOnboardingScene) ? onboardingScene : socialOnboardingScene);
        }

        void Fail(string m) { Show(m, errorColor); ContextualHintController.Instance?.ReportValidationError(); }
        void Show(string m, Color? c = null)
        {
            if (feedbackText == null) return;
            feedbackText.text = m;
            if (c.HasValue) feedbackText.color = c.Value;
        }
    }
}
