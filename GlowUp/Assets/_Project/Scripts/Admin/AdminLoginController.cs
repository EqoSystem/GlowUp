using System;
using System.Threading.Tasks;
using Firebase.Auth;
using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// Staff Access sign-in (AdminLoginScene). The same Firebase accounts as the website; no second account system and no
    /// sign-up here (staff access is requested and approved on the website). After the password is accepted, the
    /// admins/{uid} record is read from the SERVER; only an active admin reaches the capture dashboard, anyone else is
    /// signed out again with the reason. The same 3-tries / 5-minute lock as the customer login (LoginLockout).
    /// </summary>
    public class AdminLoginController : MonoBehaviour
    {
        [Header("Scene objects")]
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button signInButton;
        [SerializeField] private TextMeshProUGUI signInLabel;
        [SerializeField] private Button forgotButton;
        [SerializeField] private Button customerLoginButton;

        [Header("Scenes")]
        [SerializeField] private string customerLoginScene = "LogInScene";
        [SerializeField] private string dashboardScene = "AdminDashboardScene";

        [Header("Wording")]
        [SerializeField] private string signInText = "Sign In";
        [SerializeField] private string signingInText = "Signing in...";
        [SerializeField] private string badCredentials = "Email or password is incorrect.";
        [SerializeField] private string resetSent = "If this email has a GlowUp account, a link to set a new password is on its way.";
        [SerializeField] private float signInWaitSeconds = 20f;

        bool busy;

        async void Start()
        {
            if (backButton != null) backButton.onClick.AddListener(Back);
            if (customerLoginButton != null) customerLoginButton.onClick.AddListener(Back);
            if (signInButton != null) signInButton.onClick.AddListener(() => SignIn());
            if (forgotButton != null) forgotButton.onClick.AddListener(() => Forgot());
            Say("", false);

            // a staff session that is still signed in (app reopened): checked again on the server, never trusted from the phone
            if (await FirebaseBootstrap.Ready && StaffAccess.User != null)
            {
                SetBusy(true);
                StaffAccess.Result r = await StaffAccess.Verify();
                SetBusy(false);
                if (r == StaffAccess.Result.Granted) { SceneNavigator.Go(dashboardScene); return; }
                if (emailInput != null && string.IsNullOrEmpty(emailInput.text)) emailInput.text = StaffAccess.Email;
                StaffAccess.SignOut();
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !busy) Back();          // Android Back: to the customer login, never blocked
        }

        void Back() { SceneNavigator.Go(customerLoginScene); }

        async void SignIn()
        {
            if (busy) return;
            string email = (emailInput != null ? emailInput.text : "").Trim();
            string pw = passwordInput != null ? passwordInput.text : "";
            if (email.Length == 0 || !email.Contains("@")) { Say("Type the email of your staff account.", true); emailInput?.ActivateInputField(); return; }
            if (pw.Length == 0) { Say("Type your password.", true); passwordInput?.ActivateInputField(); return; }
            TimeSpan wait = LoginLockout.Remaining(email);
            if (wait > TimeSpan.Zero) { Say($"Too many wrong tries. Try again in {LoginLockout.MinSec(wait)}.", true); return; }
            if (!ConnectivityMonitor.Online) { Say(StaffAccess.Message(StaffAccess.Result.Offline), true); return; }

            SetBusy(true);
            Say("", false);
            try
            {
                if (!await FirebaseBootstrap.Ready) { Say(StaffAccess.Message(StaffAccess.Result.Offline), true); return; }
                try { await FirebaseAuth.DefaultInstance.SignInWithEmailAndPasswordAsync(email, pw).OrTimeout(signInWaitSeconds, "Signing in"); }
                catch (Exception e)
                {
                    if (TaskLimits.IsNetworkProblem(e)) { Say(StaffAccess.Message(StaffAccess.Result.Offline), true); return; }
                    LoginLockout.Result tries = LoginLockout.Fail(email);
                    Say(tries.locked > TimeSpan.Zero ? $"Too many wrong tries. Try again in {LoginLockout.MinSec(tries.locked)}."
                        : badCredentials + (tries.left > 0 ? $" {tries.left} {(tries.left == 1 ? "try" : "tries")} left." : ""), true);
                    return;
                }
                LoginLockout.Clear(email);

                StaffAccess.Result r = await StaffAccess.Verify();
                if (r == StaffAccess.Result.Granted)
                {
                    if (passwordInput != null) passwordInput.text = "";
                    SceneNavigator.Go(dashboardScene);
                    return;
                }
                string request = r == StaffAccess.Result.NotStaff ? await StaffAccess.RequestStatus() : "";
                StaffAccess.SignOut();
                Say(request == "pending" ? "Your staff access request is waiting for an admin's approval on the website."
                    : request == "rejected" ? "Your staff access request was not approved." : StaffAccess.Message(r), true);
            }
            finally { SetBusy(false); }
        }

        async void Forgot()
        {
            if (busy) return;
            string email = (emailInput != null ? emailInput.text : "").Trim();
            if (email.Length == 0 || !email.Contains("@")) { Say("Type your email first, then tap Forgot Password.", true); emailInput?.ActivateInputField(); return; }
            if (!ConnectivityMonitor.Online) { Say(StaffAccess.Message(StaffAccess.Result.Offline), true); return; }
            SetBusy(true);
            try
            {
                await FirebaseBootstrap.Ready;
                try { await FirebaseAuth.DefaultInstance.SendPasswordResetEmailAsync(email).OrTimeout(signInWaitSeconds, "Sending the reset email"); }
                catch (Exception e) { if (TaskLimits.IsNetworkProblem(e)) { Say(StaffAccess.Message(StaffAccess.Result.Offline), true); return; } }
                Say(resetSent, false);                  // the same words whether or not the email has an account
            }
            finally { SetBusy(false); }
        }

        void SetBusy(bool on)
        {
            busy = on;
            if (signInButton != null) signInButton.interactable = !on;
            if (forgotButton != null) forgotButton.interactable = !on;
            if (signInLabel != null) signInLabel.text = on ? signingInText : signInText;
        }

        /// <summary>Messages are toasts only (aligned and kept clear of the fields by the toast's own placement); no text box on the card.</summary>
        void Say(string text, bool problem)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            StaffToast.Show(this, problem ? FormalToast.Kind.Warning : FormalToast.Kind.Info, text);
        }
    }
}
