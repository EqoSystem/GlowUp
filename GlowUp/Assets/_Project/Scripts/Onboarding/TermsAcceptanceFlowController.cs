using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
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
    /// Privacy Policy / Terms scene. The document is a scroll view: the checkbox stays locked until the
    /// user has scrolled from the top to the very bottom, and Continue stays locked until the box is ticked.
    /// Continue then creates the Firebase account (real password over TLS), saves the profile
    /// (Firestore + offline cache), stores an Argon2id verifier for offline sign-in, queues any
    /// custom conditions/allergens for review, and opens the dashboard.
    /// </summary>
    public class TermsAcceptanceFlowController : MonoBehaviour
    {
        [SerializeField] private ScrollRect policyScroll;
        [SerializeField] private Toggle readToggle;
        [SerializeField] private TextMeshProUGUI toggleLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Range(0f, 0.2f)] [SerializeField] private float bottomThreshold = 0.03f;
        [SerializeField] private string readLabel = "I have read and agree to the Privacy Policy and Terms & Conditions";
        [SerializeField] private string dashboardScene = SceneNames.Dashboard;
        [SerializeField] private string previousScene = SceneNames.Onboarding5;
        [SerializeField] private string msgScrollFirst = "Scroll to the bottom to read everything first.";
        [SerializeField] private string msgTickToContinue = "Tick the box to agree, then tap Continue.";
        [SerializeField] private string msgNeedInternet = "Connect to the internet once to create your account.";
        [SerializeField] private string msgCreating = "Creating your profile...";
        [SerializeField] private string msgSlowConnection = "The internet connection is too slow or not working. Check it and tap Continue again.";
        [SerializeField] private string msgEmailTaken = "This email already has an account. Go back to sign in, or use another email.";
        [Tooltip("How long to wait for the account to be created before asking the user to try again (seconds)")]
        [SerializeField] private float createWaitSeconds = 30f;
        [SerializeField] private string msgSessionLost = "Your sign-up details were lost. Please start again.";

        bool reachedBottom, busy;
        bool watching;          // false until the document has been laid out, so nothing unlocks by accident while the scene opens

        void Awake()
        {
            if (policyScroll != null) policyScroll.onValueChanged.AddListener(_ => CheckBottom());
            if (readToggle != null) { readToggle.isOn = false; readToggle.interactable = false; readToggle.onValueChanged.AddListener(_ => OnToggled()); }
            if (toggleLabel != null) toggleLabel.text = readLabel;
            if (continueButton != null) continueButton.onClick.AddListener(() => _ = Create());
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(previousScene));
        }

        IEnumerator Start()
        {
            OnboardingSession.LoadDraft();
            Say(msgScrollFirst);
            Refresh();

            // 1. let the document take its full height, 2. start at the very top, 3. only then watch for the bottom
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (policyScroll != null) policyScroll.verticalNormalizedPosition = 1f;
            yield return null;
            watching = true;
            CheckBottom();      // a text short enough to fit on the screen has nothing to scroll: it counts as read
            Refresh();
        }

        /// <summary>The box unlocks the first time the user reaches the end of the document, and stays unlocked.</summary>
        void CheckBottom()
        {
            if (!watching || reachedBottom || policyScroll == null) return;
            bool fits = policyScroll.content != null && policyScroll.viewport != null && policyScroll.content.rect.height <= policyScroll.viewport.rect.height + 1f;
            if (fits || policyScroll.verticalNormalizedPosition <= bottomThreshold)
            {
                reachedBottom = true;
                if (readToggle != null) readToggle.interactable = true;
                Say(msgTickToContinue);
            }
        }

        void OnToggled()
        {
            // the box cannot be ticked before the end has been reached (it is locked), but make sure of it
            if (!reachedBottom && readToggle != null && readToggle.isOn) { readToggle.SetIsOnWithoutNotify(false); Say(msgScrollFirst); }
            else if (reachedBottom && !busy) Say(readToggle != null && readToggle.isOn ? "" : msgTickToContinue);
            Refresh();
        }

        void Refresh()
        {
            bool ready = reachedBottom && readToggle != null && readToggle.isOn && !busy;
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;
        }

        async Task Create()
        {
            if (busy) return;
            if (!reachedBottom) { Say(msgScrollFirst); return; }
            bool social = OnboardingSession.Social;        // signed in with Google / Facebook: the account exists, only the profile is saved
            if (!social && (string.IsNullOrEmpty(OnboardingSession.Email) || string.IsNullOrEmpty(OnboardingSession.Password))) { Say(msgSessionLost); return; }
            if (!ConnectivityMonitor.Online) { Say(msgNeedInternet); return; }

            busy = true; Refresh(); Say(msgCreating);
            string uid;
            try
            {
                await FirebaseBootstrap.Ready;
                if (social)
                {
                    FirebaseUser signedIn = FirebaseAuth.DefaultInstance.CurrentUser;
                    if (signedIn == null) { Say(msgSessionLost); busy = false; Refresh(); return; }
                    uid = signedIn.UserId;
                }
                else
                try
                {
                    var result = await FirebaseAuth.DefaultInstance.CreateUserWithEmailAndPasswordAsync(OnboardingSession.Email, OnboardingSession.Password)
                        .OrTimeout(createWaitSeconds, "Creating the account");
                    uid = result.User.UserId;
                }
                catch (Exception first)
                {
                    // "This email already has an account" right after a slow first try usually means that first try
                    // did go through. The same email and password prove it is the same person, so sign-up carries on.
                    FirebaseException firebase = TaskLimits.FirebaseError(first);
                    if (firebase == null || (AuthError)firebase.ErrorCode != AuthError.EmailAlreadyInUse) throw;
                    var again = await FirebaseAuth.DefaultInstance.SignInWithEmailAndPasswordAsync(OnboardingSession.Email, OnboardingSession.Password)
                        .OrTimeout(createWaitSeconds, "Signing in");
                    uid = again.User.UserId;
                }
            }
            catch (Exception e)
            {
                FirebaseException firebase = TaskLimits.FirebaseError(e);
                AuthError code = firebase != null ? (AuthError)firebase.ErrorCode : AuthError.None;
                Say(TaskLimits.IsNetworkProblem(e) ? msgSlowConnection
                    : code == AuthError.EmailAlreadyInUse || code == AuthError.WrongPassword || code == AuthError.InvalidCredential ? msgEmailTaken
                    : "Could not create your account: " + (firebase != null ? firebase.Message : e.InnerException != null ? e.InnerException.Message : e.Message));
                busy = false; Refresh();
                return;
            }

            // The account exists from here on. Nothing below may leave the user stuck on this page: each step
            // that fails is only noted, and the profile is always kept on the phone and sent again later.
            try { await UserProfileRepository.Save(OnboardingSession.ToProfile(uid)); }
            catch (Exception e) { Debug.LogWarning("Sign-up: the profile will be sent later: " + e.Message); }
            try
            {
                if (!social) OfflineCredentialStore.Remember(OnboardingSession.Email, OnboardingSession.Password);   // Argon2id verifier for offline sign-in (runs in the background)
                QueueSuggestions(uid);
                PlayerPrefs.SetInt("IsOnboarded", 1);
                ContextualHintController.MarkNewUser(true);
            }
            catch (Exception e) { Debug.LogWarning("Sign-up: a follow-up step was skipped: " + e.Message); }
            OnboardingSession.Clear();
            if (this == null) return;
            SceneNavigator.Go(dashboardScene);
        }

        static void QueueSuggestions(string uid)
        {
            foreach (var c in OnboardingSession.CustomConditions) Suggest("conditionSuggestions", c, uid);
            foreach (var a in OnboardingSession.CustomAllergens) Suggest("allergenSuggestions", a, uid);
        }

        static void Suggest(string collection, string text, string uid)
        {
            var rec = new SuggestionRecord { text = text, uid = uid, status = "pending", createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            SyncQueue.EnqueueSet($"{collection}/{Guid.NewGuid():N}", rec);
        }

        void Say(string m) { if (feedbackText != null) feedbackText.text = m; }
    }

    [Serializable]
    public class SuggestionRecord { public string text, uid, status; public long createdAtUnix; }
}
