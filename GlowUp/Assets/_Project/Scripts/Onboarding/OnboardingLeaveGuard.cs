using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Data;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// ASKS BEFORE THE SIGN-UP IS LEFT (the user's rule of 12 October 2026): "You are about to leave the sign-up. What
    /// you typed and selected will be discarded." Only "Leave" leaves; "Stay" (or a tap beside the box) keeps everything.
    ///
    /// One of these is on every sign-up page (Steps 1 to 5 and the Terms page). It is asked by
    ///   - the X at the top right of the card (Leave Button), on every page;
    ///   - the page's own Back arrow where going back would leave the sign-up (Step 1; Step 2 for a Google / Facebook
    ///     account or while the skin quiz is retaken): those pages call RequestLeave();
    ///   - the phone's back key, through the Back arrow: it does what the arrow does, and closes this question when it is open.
    ///
    /// Leaving discards the sign-up draft (also the copy that survives a restart), signs a Google / Facebook account
    /// out again, and opens the log-in page. While a signed-in user retakes the skin quiz from Settings, leaving only
    /// drops the new answers and returns to Settings: the saved skin profile is not touched.
    /// </summary>
    public class OnboardingLeaveGuard : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The pop-up that asks (LeaveSignUpPanel)")]
        [SerializeField] private ConfirmDialog confirm;
        [Tooltip("The X at the top right of the card")]
        [SerializeField] private Button leaveButton;
        [Tooltip("The page's own Back arrow: the phone's back key presses it")]
        [SerializeField] private Button backButton;
        [Tooltip("The calendar of this page, if it has one: the phone's back key closes it first")]
        [SerializeField] private DatePickerPopup calendar;

        [Header("Behaviour")]
        [Tooltip("The phone's back key (Android) does the same as the Back arrow")]
        [SerializeField] private bool phoneBackKeyGoesBack = true;
        [SerializeField] private string loginScene = SceneNames.Login;

        [Header("Wording: leaving the sign-up")]
        [SerializeField] private string title = "Leave sign-up?";
        [TextArea(2, 4)] [SerializeField] private string message = "You are about to leave the sign-up. What you typed and selected will be discarded, and you go back to the log-in page.";
        [TextArea(2, 4)] [SerializeField] private string socialMessage = "You are about to leave the sign-up. What you selected will be discarded, and you are signed out of this app again.";
        [SerializeField] private string leaveLabel = "Leave";
        [SerializeField] private string stayLabel = "Stay";

        [Header("Wording: leaving the skin quiz (retaken from Settings)")]
        [SerializeField] private string retakeTitle = "Leave the skin quiz?";
        [TextArea(2, 4)] [SerializeField] private string retakeMessage = "The answers you chose just now will be discarded. Your saved skin profile stays as it is.";

        /// <summary>
        /// True while a page is in the middle of something that must not be cut off (the Terms page creating the account):
        /// the question is not shown and the back key does nothing until it is over.
        /// </summary>
        public static bool Locked;

        static OnboardingLeaveGuard current;
        float keyboardSeenAt = -10f;

        void Awake()
        {
            if (leaveButton != null) leaveButton.onClick.AddListener(Ask);
        }

        void OnEnable() { current = this; }
        void OnDisable() { if (current == this) current = null; }

        /// <summary>
        /// For a page whose Back arrow would leave the sign-up: asks first. A scene that was built before this rule has
        /// no question to show; there the page is left as before, and what was typed is discarded all the same.
        /// </summary>
        public static void RequestLeave()
        {
            if (Locked) return;
            if (current != null && current.confirm != null) current.Ask();
            else LeaveNow(current != null ? current.loginScene : SceneNames.Login);
        }

        /// <summary>Shows the question. Nothing changes unless the user presses Leave.</summary>
        public void Ask()
        {
            if (Locked) return;
            if (confirm == null) { LeaveNow(loginScene); return; }
            if (OnboardingSession.Retake) confirm.Ask(retakeTitle, retakeMessage, leaveLabel, () => LeaveNow(loginScene), true, stayLabel);
            else confirm.Ask(title, OnboardingSession.Social ? socialMessage : message, leaveLabel, () => LeaveNow(loginScene), true, stayLabel);
        }

        /// <summary>Leaves without asking: the draft is discarded and the page before the sign-up opens.</summary>
        public static void LeaveNow(string loginScene)
        {
            if (OnboardingSession.Retake)
            {
                string back = string.IsNullOrEmpty(OnboardingSession.RetakeReturnScene) ? SceneNames.Dashboard : OnboardingSession.RetakeReturnScene;
                OnboardingSession.EndRetake();
                SceneNavigator.Go(back);
                return;
            }
            // signed in with Google / Facebook: sign out, or the log-in page would send the user straight back into the sign-up
            if (OnboardingSession.Social) UserProfileRepository.SignOut();
            OnboardingSession.Clear();
            SceneNavigator.Go(string.IsNullOrEmpty(loginScene) ? SceneNames.Login : loginScene);
        }

        void Update()
        {
            // the back key that only puts the keyboard away must not also turn the page
            if (TouchScreenKeyboard.visible) keyboardSeenAt = Time.unscaledTime;
            if (!phoneBackKeyGoesBack || !Input.GetKeyDown(KeyCode.Escape) || Time.unscaledTime - keyboardSeenAt < 0.4f || Locked) return;

            if (confirm != null && confirm.IsOpen) { confirm.Close(); return; }                                   // the same as Stay
            if (calendar != null && calendar.IsOpen) { calendar.Close(); return; }
            if (backButton != null && backButton.isActiveAndEnabled && backButton.interactable) backButton.onClick.Invoke();
            else Ask();
        }
    }
}
