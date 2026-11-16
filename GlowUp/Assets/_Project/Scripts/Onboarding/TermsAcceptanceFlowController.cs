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
    /// Continue then saves the profile (Firestore + offline cache) to the account that Step 1 made when the
    /// email was confirmed (or to the Google / Facebook account), stores an Argon2id verifier for offline sign-in,
    /// queues any custom conditions/allergens for review, and opens the dashboard. Only a scene flow built before
    /// 12 October 2026 (no confirmation on Step 1) still creates the account here.
    /// </summary>
    public class TermsAcceptanceFlowController : MonoBehaviour
    {
        static bool openReadOnlyFromLogin;

        [SerializeField] private ScrollRect policyScroll;
        [SerializeField] private Toggle readToggle;
        [SerializeField] private TextMeshProUGUI toggleLabel;
        [Tooltip("The second box: consent to keep the skin and allergy details (health information). Both boxes must be ticked. A scene built before it has none.")]
        [SerializeField] private Toggle healthToggle;
        [SerializeField] private TextMeshProUGUI healthLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private ContinueButtonGlowAnimator continueAnimator;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Range(0f, 0.2f)] [SerializeField] private float bottomThreshold = 0.03f;
        [SerializeField] private string healthText = "I agree that GlowUp keeps my skin type, skin tone, skin conditions and allergies, to personalise my results (sensitive personal information)";
        [SerializeField] private string readLabel = "I have read and agree to the Privacy Policy and the Terms and Conditions";
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
        [SerializeField] private string msgNoBirthday = "Your birthday is missing. Go back to the summary and fill in your name and birthday.";
        [SerializeField] private string msgConfirmFirst = "Your email is not confirmed yet. Go back to Step 1 and tap Continue there.";

        bool reachedBottom, busy, readOnly;
        bool watching;          // false until the document has been laid out, so nothing unlocks by accident while the scene opens

        static string openAtPhrase;

        /// <summary>
        /// Opens this page for READING only (from the log-in page): no progress bar, no status line, no boxes, no Continue. `section` =
        /// a heading to start at ("Privacy Policy" or "Terms and Conditions"); null = the top.
        /// </summary>
        public static void OpenReadOnlyFromLogin(string section = null) { openReadOnlyFromLogin = true; openAtPhrase = section; }

        void Awake()
        {
            readOnly = openReadOnlyFromLogin;
            openReadOnlyFromLogin = false;
            if (readLabel != null) readLabel = readLabel.Replace("Terms & Conditions", UiWording.TermsAndConditions);   // a scene saved earlier keeps the old words
            UiWording.Retitle("Privacy Policy & Terms", UiWording.LegalTitle);           // one name everywhere (10 November 2026)
            if (readOnly)
            {
                previousScene = "LogInScene";
                if (readToggle != null) readToggle.gameObject.SetActive(false);
                if (toggleLabel != null) toggleLabel.gameObject.SetActive(false);
                if (healthToggle != null) healthToggle.gameObject.SetActive(false);
                if (continueButton != null) continueButton.gameObject.SetActive(false);
                // reading is not a sign-up step: no "Step 5 of 5" bar, and the X simply goes back (no "Leave sign-up?" question)
                foreach (OnboardingProgressBar bar in FindObjectsOfType<OnboardingProgressBar>(true)) bar.HideForReading();
                OnboardingLeaveGuard.PlainExitScene = previousScene;
                if (backButton != null) backButton.gameObject.SetActive(true);            // the arrow (and the phone's back key) return to the log-in page
            }
            if (policyScroll != null) policyScroll.onValueChanged.AddListener(_ => CheckBottom());
            if (readToggle != null) { readToggle.isOn = false; readToggle.interactable = false; readToggle.onValueChanged.AddListener(_ => OnToggled()); }
            if (toggleLabel != null) toggleLabel.text = readLabel;
            if (healthToggle != null) { healthToggle.isOn = false; healthToggle.interactable = false; healthToggle.onValueChanged.AddListener(_ => OnToggled()); }
            if (healthLabel != null) healthLabel.text = healthText;
            if (continueButton != null) continueButton.onClick.AddListener(() => _ = Create());
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(previousScene));
        }

        IEnumerator Start()
        {
            // New17: the page is read top to bottom without a progress bar, from the log-in page AND after Generate Profile on Step 5
            // (the user reads, agrees and continues: it is not shown as one more step)
            showSteps = false;
            openedFromSignUp = false;
            LayHeader();
            if (!readOnly) OnboardingSession.LoadDraft();
            // reading only (from Settings or the log-in page): no status line at all (the old "Viewing only..." line read like a log, 7 November 2026)
            Say(readOnly ? "" : msgScrollFirst);
            Refresh();

            // 1. let the document take its full height, 2. start at the very top, 3. only then watch for the bottom
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (policyScroll != null) policyScroll.verticalNormalizedPosition = 1f;
            // opened from one of the two links on the log-in page: start at that part of the document
            string phrase = readOnly ? openAtPhrase : null;
            openAtPhrase = null;
            if (!string.IsNullOrEmpty(phrase) && policyScroll != null && policyScroll.content != null)
            {
                TMP_Text doc = policyScroll.content.GetComponentInChildren<TMP_Text>(true);
                yield return null;                                                  // the document's own text is set on its first frame
                Canvas.ForceUpdateCanvases();
                UiWording.ScrollToPhrase(policyScroll, doc, phrase);
            }
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
                if (healthToggle != null) healthToggle.interactable = true;
                Say(msgTickToContinue);
            }
        }

        void OnToggled()
        {
            // the box cannot be ticked before the end has been reached (it is locked), but make sure of it
            if (!reachedBottom && ((readToggle != null && readToggle.isOn) || (healthToggle != null && healthToggle.isOn)))
            {
                if (readToggle != null) readToggle.SetIsOnWithoutNotify(false);
                if (healthToggle != null) healthToggle.SetIsOnWithoutNotify(false);
                Say(msgScrollFirst);
            }
            else if (reachedBottom && !busy) Say(AgreedToAll() ? "" : msgTickToContinue);
            Refresh();
        }

        void Refresh()
        {
            bool ready = reachedBottom && AgreedToAll() && !busy;
            if (continueAnimator != null) continueAnimator.SetReady(ready);
            else if (continueButton != null) continueButton.interactable = ready;
            // while the account is being created nobody may walk away from this page: going back or leaving in the middle
            // would empty the sign-up details that are about to be saved as the profile
            if (backButton != null) backButton.interactable = !busy;
            OnboardingLeaveGuard.Locked = busy;
        }

        /// <summary>The policy box is ticked and, when the scene has it, so is the health-information box.</summary>
        bool AgreedToAll() => readToggle != null && readToggle.isOn && (healthToggle == null || healthToggle.isOn);

        void OnDestroy() { OnboardingLeaveGuard.Locked = false; OnboardingLeaveGuard.PlainExitScene = null; }

        async Task Create()
        {
            if (busy) return;
            if (!reachedBottom) { Say(msgScrollFirst); return; }
            // a profile is never made without the names and the birthday (a Google account does not bring them)
            if (string.IsNullOrWhiteSpace(OnboardingSession.BirthdayIso) || OnboardingSession.Age <= 0
                || string.IsNullOrWhiteSpace(OnboardingSession.FirstName) || string.IsNullOrWhiteSpace(OnboardingSession.LastName)) { Say(msgNoBirthday); return; }
            bool social = OnboardingSession.Social;        // signed in with Google / Facebook: the account exists, only the profile is saved
            bool hasAccount = OnboardingSession.HasAccount; // Step 1 made the account when the email was confirmed: the same
            if (!social && !hasAccount && (string.IsNullOrEmpty(OnboardingSession.Email) || string.IsNullOrEmpty(OnboardingSession.Password))) { Say(msgSessionLost); return; }
            if (!ConnectivityMonitor.Online) { Say(msgNeedInternet); return; }

            busy = true; Refresh(); Say(msgCreating);
            string uid;
            try
            {
                await FirebaseBootstrap.Ready;
                if (social || hasAccount)
                {
                    FirebaseUser signedIn = FirebaseAuth.DefaultInstance.CurrentUser;
                    if (signedIn == null || (hasAccount && signedIn.UserId != OnboardingSession.AccountUid)) { Say(msgSessionLost); busy = false; Refresh(); return; }
                    if (hasAccount && !signedIn.IsEmailVerified)
                    {
                        // the phone's copy of the account can be older than the confirmation: ask once before saying no
                        try { await signedIn.ReloadAsync().OrTimeout(createWaitSeconds, "Checking the confirmation"); }
                        catch (Exception e) { { } }
                        signedIn = FirebaseAuth.DefaultInstance.CurrentUser ?? signedIn;
                        if (!signedIn.IsEmailVerified) { Say(msgConfirmFirst); busy = false; Refresh(); return; }
                    }
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
            OnboardingSession.HealthConsent = healthToggle != null && healthToggle.isOn;
            try { await UserProfileRepository.Save(OnboardingSession.ToProfile(uid)); }
            catch (Exception e) { { } }
            try
            {
                // Argon2id verifier for offline sign-in (runs in the background). The password is only in memory: after the app was
                // closed in the middle of a sign-up it is gone, and the verifier is made at the next log-in instead.
                if (!social && !string.IsNullOrEmpty(OnboardingSession.Password)) OfflineCredentialStore.Remember(OnboardingSession.Email, OnboardingSession.Password);
                if (!social) SavedAccounts.Remember(OnboardingSession.Email);       // only the email, for the list on the log-in page
                QueueSuggestions(uid);
                PlayerPrefs.SetInt("IsOnboarded", 1);
                ContextualHintController.MarkNewUser(true);
            }
            catch (Exception e) { { } }
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

        /// <summary>
        /// The two hints about the boxes ("Scroll to the bottom...", "Tick the box...") stay in the one-line hint above Continue; everything
        /// else (creating the profile, a slow connection, a lost session) is a toast at the top (New13 item 5, 10 November 2026).
        /// </summary>
        void Say(string m)
        {
            if (feedbackText == null) return;
            if (readOnly) m = "";                        // nothing to tick or continue while only reading
            bool hint = string.IsNullOrEmpty(m) || m == msgScrollFirst || m == msgTickToContinue;
            if (!hint)
            {
                OnboardingNotices.Toast(feedbackText, m == msgCreating ? FormalToast.Kind.Info : FormalToast.Kind.Error, m);
                m = "";
            }
            OnboardingNotices.OneLine(feedbackText);
            feedbackText.text = m;
            feedbackText.gameObject.SetActive(!string.IsNullOrEmpty(m));
        }

        // ---------------------------------------------------------------------------------------
        //  The header (New13 item 3, 10 November 2026): the page name wrapped onto the progress bar. Now it flows: the name (at most two
        //  lines, centred, clear of the Back arrow and the X), then the bar 12 units under it, then "Step 5 of 5", then the document.
        //  The bar and its line are hidden in every case since New17 (OpenFromSignUp is kept for the callers; showSteps stays false).
        // ---------------------------------------------------------------------------------------
        static bool openedFromSignUp;
        /// <summary>Step 5's Generate Profile opens this page as the last step of the sign-up (the progress bar shows).</summary>
        public static void OpenFromSignUp() { openedFromSignUp = true; }
        bool showSteps;

        void LayHeader()
        {
            RectTransform card = feedbackText != null ? feedbackText.transform.parent as RectTransform : null;
            if (card == null) return;
            OnboardingProgressBar bar = FindObjectOfType<OnboardingProgressBar>(true);
            TMP_Text subtitle = null;
            foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(true))
                if (t.transform.parent == card && (t.name == "PrivacyPolicySubtitleText" || t.name == "SubtitleText" || t.name == "SectionHeaderText")) { subtitle = t; break; }
            if (subtitle == null) return;
            TMP_Text stepLine = null;
            foreach (TMP_Text t in card.GetComponentsInChildren<TMP_Text>(true)) if (t.name == "StepText") { stepLine = t; break; }

            const float nameTop = 176f, sideRoom = 140f, gap = 12f, barHeight = 16f, stepHeight = 46f;
            var srt = subtitle.rectTransform;
            srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f);
            subtitle.enableWordWrapping = true; subtitle.overflowMode = TextOverflowModes.Ellipsis;
            subtitle.margin = Vector4.zero;                         // an older scene kept a 49-unit top margin (one line only)
            subtitle.alignment = TextAlignmentOptions.Top;
            float width = card.rect.width - 2f * sideRoom;
            float size = subtitle.fontSize, lineHeight = 0f, height = 0f;
            for (; size >= 34f; size -= 2f)
            {
                subtitle.fontSize = size;
                height = subtitle.GetPreferredValues(subtitle.text, width, 0f).y;
                lineHeight = subtitle.GetPreferredValues("Ag", width, 0f).y;
                if (height <= lineHeight * 2.05f) break;
            }
            subtitle.enableAutoSizing = false;
            height = Mathf.Min(height, lineHeight * 2.05f);
            srt.sizeDelta = new Vector2(-2f * sideRoom, height);
            srt.anchoredPosition = new Vector2(0f, -nameTop);
            float y = nameTop + height + gap;

            if (bar != null)
            {
                if (showSteps)
                {
                    var brt = (RectTransform)bar.transform;
                    brt.anchoredPosition = new Vector2(brt.anchoredPosition.x, -y); y += barHeight + 8f;
                    if (stepLine != null) { var lrt = stepLine.rectTransform; lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x, -y); y += stepHeight; }
                }
                else bar.HideForReading();
            }
            y += 14f;
            if (policyScroll != null)
            {
                var prt = (RectTransform)policyScroll.transform;
                if (prt.anchorMin.y < prt.anchorMax.y) prt.offsetMax = new Vector2(prt.offsetMax.x, -Mathf.Max(y, 300f));
            }
        }
    }

    [Serializable]
    public class SuggestionRecord { public string text, uid, status; public long createdAtUnix; }
}
