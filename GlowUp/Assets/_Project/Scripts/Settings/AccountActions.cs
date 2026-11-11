using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Log Out and Delete Account on the Settings page (Firebase Auth).
    ///
    /// Log Out: asks first, signs out and opens the login screen. The profile copy stays on the phone
    /// so the same person can sign back in without internet.
    ///
    /// Delete Account: needs internet and the password again (Firebase only deletes an account right
    /// after a fresh sign-in). In this order it deletes the user's scan history in the database, their
    /// profile, the account itself, and then everything about them on this phone. It cannot be undone.
    /// </summary>
    public class AccountActions : MonoBehaviour
    {
        [Header("Log out")]
        [SerializeField] private Button logOutButton;
        [SerializeField] private ConfirmDialog confirm;
        [SerializeField] private string logOutTitle = "Log out?";
        [SerializeField] private string logOutMessage = "You can sign back in at any time with your email and password.";
        [SerializeField] private string logOutMessageWithPending = "{0} change(s) have not been sent to your account yet. They stay on this phone and are sent the next time you sign in online.";
        [SerializeField] private string logOutConfirm = "Log Out";

        [Header("Delete account")]
        [SerializeField] private Button deleteAccountButton;
        [SerializeField] private OverlayPanel deletePanel;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button confirmDeleteButton;
        [SerializeField] private TextMeshProUGUI confirmDeleteLabel;
        [SerializeField] private Button cancelDeleteButton;
        [SerializeField] private TextMeshProUGUI deleteMessageText;
        [Tooltip("The line above the box (it names what to type)")]
        [SerializeField] private TextMeshProUGUI passwordLabel;
        [Tooltip("The warning at the top of the pop-up")]
        [SerializeField] private TextMeshProUGUI warningText;
        [Tooltip("Accounts with BOTH a password and Google: switches between the two ways to confirm")]
        [SerializeField] private Button switchMethodButton;
        [SerializeField] private string deleteLabel = "Delete My Account";
        [SerializeField] private string deletingLabel = "Deleting...";
        [SerializeField] private string msgOffline = "You need internet to delete your account.";
        [SerializeField] private string msgOfflineSignIn = "You signed in without internet. Log out, sign in again with internet, then delete your account.";
        [SerializeField] private string msgTypePassword = "Type your password to confirm.";
        [SerializeField] private string msgWrongPassword = "That password is not correct.";
        [SerializeField] private string msgTooManyTries = "Too many tries. Wait a few minutes and try again.";
        [SerializeField] private string msgNoConnection = "The connection was lost. Nothing was deleted. Try again.";
        [Tooltip("{0} = the reason")]
        [SerializeField] private string msgFailed = "Your account was not deleted: {0}";
        [Tooltip("Shown when the profile data was removed but Firebase refused to delete the sign-in itself")]
        [SerializeField] private string msgPartly = "Your data was removed, but the sign-in could not be deleted. Log out, sign in again and repeat this step.";

        [Header("Google accounts (12 November 2026)")]
        [Tooltip("What a Google account types: only a guard against an accidental tap, never proof of who it is (Google's sign-in is)")]
        [SerializeField] private string confirmPhrase = "Confirm Delete";
        [SerializeField] private string googleWarning = "Confirm with your Google account to delete your GlowUp account. This cannot be undone.";
        [SerializeField] private string passwordWarning = "This permanently deletes your GlowUp account, your skin profile and your scan history. It cannot be undone.";
        [SerializeField] private string phraseLabel = "Type Confirm Delete to continue";
        [SerializeField] private string passwordLabelWords = "Type your password to confirm";
        [SerializeField] private string useGoogleWords = "Confirm with Google instead";
        [SerializeField] private string usePasswordWords = "Confirm with my password instead";
        [SerializeField] private string msgGoogleCancelled = "Google sign-in was cancelled. Nothing was deleted.";
        [SerializeField] private string msgWrongGoogle = "That Google account is not the one signed in to GlowUp. Nothing was deleted.";
        [SerializeField] private string msgSignInAgain = "For your safety, confirm again: tap Delete My Account once more.";
        [SerializeField] private string msgRetry = "Not everything was deleted yet (the connection was lost). Tap Delete My Account again to finish; it is safe to repeat.";

        [Header("Scenes")]
        [SerializeField] private string loginScene = SceneNames.Login;

        bool busy;
        bool hasPassword, hasGoogle, googleMode;     // which ways this account can confirm, and which one the pop-up shows

        void Awake()
        {
            if (logOutButton != null) logOutButton.onClick.AddListener(AskLogOut);
            if (deleteAccountButton != null) deleteAccountButton.onClick.AddListener(OpenDelete);
            if (confirmDeleteButton != null) confirmDeleteButton.onClick.AddListener(() => { _ = DeleteAccount(); });
            if (cancelDeleteButton != null) cancelDeleteButton.onClick.AddListener(() => { if (!busy && deletePanel != null) deletePanel.Hide(); });
            if (passwordInput != null) passwordInput.onValueChanged.AddListener(_ => RefreshConfirm());
            if (switchMethodButton != null) switchMethodButton.onClick.AddListener(() => { if (!busy) ShowMode(!googleMode); });
        }

        // ---------------------------------------------------------------------------------------
        //  Log out
        // ---------------------------------------------------------------------------------------
        void AskLogOut()
        {
            int waiting = SyncQueue.PendingCount;
            string message = waiting > 0 ? string.Format(logOutMessageWithPending, waiting) + "\n\n" + logOutMessage : logOutMessage;
            if (confirm != null) confirm.Ask(logOutTitle, message, logOutConfirm, LogOut, true);
            else LogOut();
        }

        void LogOut()
        {
            UserProfileRepository.SignOut();
            PlayerPrefs.DeleteKey("UserEmail");
            SceneNavigator.Go(loginScene);
        }

        // ---------------------------------------------------------------------------------------
        //  Delete account
        // ---------------------------------------------------------------------------------------
        void OpenDelete()
        {
            FirebaseUser user = null;
            try { user = FirebaseAuth.DefaultInstance.CurrentUser; } catch (Exception) { }
            Providers(user, out hasPassword, out hasGoogle);
            Say("");
            SetBusy(false);
            ShowMode(!hasPassword && hasGoogle);            // a password account confirms with its password; a Google-only account with Google
            if (deletePanel != null) deletePanel.Show();
        }

        /// <summary>Which sign-in methods the account has (ProviderData): email/password, Google, or both.</summary>
        public static void Providers(FirebaseUser user, out bool password, out bool google)
        {
            password = google = false;
            if (user == null) return;
            foreach (IUserInfo info in user.ProviderData)
            {
                if (info == null) continue;
                if (info.ProviderId == GlowUp.Auth.SocialSignIn.Password) password = true;
                if (info.ProviderId == GlowUp.Auth.SocialSignIn.Google) google = true;
            }
            if (!password && !google) password = true;      // nothing reported (offline copy): the password way, as before
        }

        /// <summary>Google mode: the box asks for the phrase (plain text). Password mode: the box is the password (hidden).</summary>
        void ShowMode(bool google)
        {
            googleMode = google;
            if (passwordInput != null)
            {
                passwordInput.text = "";
                passwordInput.contentType = google ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                passwordInput.inputType = google ? TMP_InputField.InputType.Standard : TMP_InputField.InputType.Password;
                if (passwordInput.placeholder is TMP_Text hint) hint.text = google ? confirmPhrase : "Your password";
                Transform eye = passwordInput.transform.Find("EyeButton");
                if (eye != null) eye.gameObject.SetActive(!google);
                passwordInput.ForceLabelUpdate();
            }
            if (passwordLabel != null) passwordLabel.text = google ? phraseLabel : passwordLabelWords;
            if (warningText != null) warningText.text = google ? googleWarning : passwordWarning;
            if (switchMethodButton != null)
            {
                switchMethodButton.gameObject.SetActive(hasPassword && hasGoogle);
                TMP_Text words = switchMethodButton.GetComponentInChildren<TMP_Text>(true);
                if (words != null) words.text = "<u>" + (google ? usePasswordWords : useGoogleWords) + "</u>";
            }
            Say("");
            RefreshConfirm();
        }

        /// <summary>The phrase typed right (trimmed, any capitals). Only an accident guard: never proof of identity.</summary>
        bool PhraseMatches() => passwordInput != null && string.Equals(passwordInput.text.Trim(), confirmPhrase, StringComparison.OrdinalIgnoreCase);

        void RefreshConfirm()
        {
            if (confirmDeleteButton == null || busy) return;
            confirmDeleteButton.interactable = !googleMode || PhraseMatches();       // Google mode: disabled until the phrase is right
        }

        const float StepWaitSeconds = 25f;      // each step of deleting gives up after this long without an answer, so the page never hangs

        async Task DeleteAccount()
        {
            if (busy) return;
            if (!ConnectivityMonitor.Online) { Say(msgOffline); return; }

            FirebaseUser user = null;
            try { if (await FirebaseBootstrap.Ready) user = FirebaseAuth.DefaultInstance.CurrentUser; }
            catch (Exception e) { Debug.LogWarning("Delete account: " + e.Message); }
            if (user == null) { Say(msgOfflineSignIn); return; }

            string password = passwordInput != null ? passwordInput.text : "";
            if (googleMode && !PhraseMatches()) { RefreshConfirm(); return; }                    // the button is disabled anyway
            if (!googleMode && string.IsNullOrEmpty(password)) { Say(msgTypePassword); return; }

            SetBusy(true); Say("");
            string uid = user.UserId, email = user.Email ?? "";
            bool dataRemoved = false;
            try
            {
                // 1. a FRESH proof of identity, every time (never a cached session): the password against Firebase, or a new Google sign-in
                if (!googleMode)
                    await user.ReauthenticateAsync(EmailAuthProvider.GetCredential(email, password)).OrTimeout(StepWaitSeconds, "Checking your password");
                else
                {
                    NativeGoogleSignIn.Answer google = await NativeGoogleSignIn.RequestToken();              // no time limit: the user is choosing
                    if (google.result == NativeGoogleSignIn.Result.Cancelled) { SetBusy(false); Say(msgGoogleCancelled); return; }
                    if (google.result != NativeGoogleSignIn.Result.Token)
                    { SetBusy(false); Say(google.result == NativeGoogleSignIn.Result.NoAccount ? GlowUp.Auth.SocialSignIn.NoGoogleAccount : GlowUp.Auth.SocialSignIn.GoogleUnavailable); return; }
                    AuthResult again = await user.ReauthenticateAndRetrieveDataAsync(GoogleAuthProvider.GetCredential(google.idToken, null))
                                                 .OrTimeout(StepWaitSeconds, "Checking your Google account");
                    FirebaseUser proved = again != null ? again.User : null;
                    // the SAME person: the same account id and the same e-mail as the one signed in (Firebase also refuses another account)
                    if (proved == null || proved.UserId != uid || !string.Equals(proved.Email ?? "", email, StringComparison.OrdinalIgnoreCase))
                    { SetBusy(false); Say(msgWrongGoogle); return; }
                }

                // 2. a new token, so the database rules see the new sign-in time (they allow deleting the profile only right after one)
                await user.TokenAsync(true).OrTimeout(StepWaitSeconds, "Refreshing your sign-in");

                // 3. the user's own data in the database, page by page (safe to repeat: what is gone stays gone)
                FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
                DocumentReference profile = db.Collection("users").Document(uid);
                int removedScans = 0;
                while (true)
                {
                    QuerySnapshot page = await profile.Collection("scans").Limit(300).GetSnapshotAsync(Source.Server).OrTimeout(StepWaitSeconds, "Finding your scans");
                    int count = 0;
                    WriteBatch batch = db.StartBatch();
                    foreach (DocumentSnapshot scan in page.Documents) { batch.Delete(scan.Reference); count++; }
                    ReadCounter.AddQuery("your scans (account deletion)", count);
                    if (count == 0) break;
                    await batch.CommitAsync().OrTimeout(StepWaitSeconds, "Deleting your scans");
                    removedScans += count;
                    if (count < 300) break;
                }
                await profile.DeleteAsync().OrTimeout(StepWaitSeconds, "Deleting your profile");
                dataRemoved = true;
                Debug.Log("[GlowUp] Delete account: " + removedScans + " scan(s) and the profile removed; deleting the sign-in.");
                // (no Firebase Storage is used by the app: there are no files to remove)

                // 4. only then the sign-in itself
                await user.DeleteAsync().OrTimeout(StepWaitSeconds, "Deleting your account");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Delete account: " + e.Message);
                if (dataRemoved) ForgetOnThisPhone(uid, email);     // the database copy is gone, so the copy on this phone must not bring it back
                if (this == null) return;
                SetBusy(false);
                Say(Explain(e, dataRemoved));
                return;
            }

            // 5. everything about this account on this phone
            ForgetOnThisPhone(uid, email);
            UserProfileRepository.SignOut();
            PlayerPrefs.DeleteKey("UserEmail");
            if (this == null) return;
            SceneNavigator.Go(loginScene);
        }

        static void ForgetOnThisPhone(string uid, string email)
        {
            SyncQueue.RemoveWhere("users/" + uid);              // writes that were still waiting for this account
            ScanHistoryRepository.DeleteLocal(uid);
            UserProfileRepository.Clear();
            if (!string.IsNullOrEmpty(email)) OfflineCredentialStore.Forget(email);
            if (!string.IsNullOrEmpty(email)) GlowUp.Auth.SavedAccounts.Forget(email);          // a deleted account is not offered on the log-in page any more
            OnboardingSession.Clear();
            PlayerPrefs.Save();
        }

        string Explain(Exception e, bool dataRemoved)
        {
            FirebaseException firebase = null;
            for (Exception x = e; x != null && firebase == null; x = x.InnerException) firebase = x as FirebaseException;
            if (e is AggregateException aggregate)
                foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                    for (Exception x = inner; x != null && firebase == null; x = x.InnerException) firebase = x as FirebaseException;

            if (firebase != null)
                switch ((AuthError)firebase.ErrorCode)
                {
                    case AuthError.UserMismatch:
                        return msgWrongGoogle;
                    case AuthError.RequiresRecentLogin:
                        return msgSignInAgain;                                  // the next tap asks for a fresh proof again
                    case AuthError.WrongPassword:
                    case AuthError.InvalidCredential:
                        return msgWrongPassword;
                    case AuthError.TooManyRequests:
                        return msgTooManyTries;
                    case AuthError.NetworkRequestFailed:
                        return dataRemoved ? msgRetry : msgNoConnection;
                }
            if (dataRemoved) return msgPartly;
            if (!ConnectivityMonitor.Online || TaskLimits.IsNetworkProblem(e)) return msgRetry;
            return string.Format(msgFailed, firebase != null ? firebase.Message : e.InnerException != null ? e.InnerException.Message : e.Message);
        }

        void SetBusy(bool value)
        {
            busy = value;
            if (confirmDeleteButton != null) confirmDeleteButton.interactable = !value && (!googleMode || PhraseMatches());
            if (switchMethodButton != null) switchMethodButton.interactable = !value;
            if (cancelDeleteButton != null) cancelDeleteButton.interactable = !value;
            if (passwordInput != null) passwordInput.interactable = !value;
            if (confirmDeleteLabel != null) confirmDeleteLabel.text = value ? deletingLabel : deleteLabel;
        }

        void Say(string message) { if (deleteMessageText != null) deleteMessageText.text = message; }
    }
}
