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

        [Header("Scenes")]
        [SerializeField] private string loginScene = SceneNames.Login;

        bool busy;

        void Awake()
        {
            if (logOutButton != null) logOutButton.onClick.AddListener(AskLogOut);
            if (deleteAccountButton != null) deleteAccountButton.onClick.AddListener(OpenDelete);
            if (confirmDeleteButton != null) confirmDeleteButton.onClick.AddListener(() => { _ = DeleteAccount(); });
            if (cancelDeleteButton != null) cancelDeleteButton.onClick.AddListener(() => { if (!busy && deletePanel != null) deletePanel.Hide(); });
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
            if (passwordInput != null) passwordInput.text = "";
            Say("");
            SetBusy(false);
            if (deletePanel != null) deletePanel.Show();
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

            // a Google / Facebook account has no password here: it is confirmed by signing in with Google / Facebook once more
            bool usesPassword = GlowUp.Auth.SocialSignIn.ProviderOf(user) == GlowUp.Auth.SocialSignIn.Password;
            string password = passwordInput != null ? passwordInput.text : "";
            if (usesPassword && string.IsNullOrEmpty(password)) { Say(msgTypePassword); return; }

            SetBusy(true); Say("");
            string uid = user.UserId, email = user.Email ?? "";
            bool dataRemoved = false;
            try
            {
                // 1. Firebase deletes an account only right after a fresh sign-in
                if (usesPassword) await user.ReauthenticateAsync(EmailAuthProvider.GetCredential(email, password)).OrTimeout(StepWaitSeconds, "Checking your password");
                else await GlowUp.Auth.SocialSignIn.Reauthenticate(user);       // no time limit: the user is choosing their account

                // 2. the user's own data in the database (the rules let each user delete only their own)
                FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
                DocumentReference profile = db.Collection("users").Document(uid);
                QuerySnapshot scans = await profile.Collection("scans").GetSnapshotAsync(Source.Server).OrTimeout(StepWaitSeconds, "Finding your scans");
                var pending = new List<DocumentReference>();
                foreach (DocumentSnapshot scan in scans.Documents) pending.Add(scan.Reference);
                ReadCounter.AddQuery("your scans (account deletion)", pending.Count);
                for (int i = 0; i < pending.Count; i += 400)
                {
                    WriteBatch batch = db.StartBatch();
                    for (int j = i; j < Mathf.Min(i + 400, pending.Count); j++) batch.Delete(pending[j]);
                    await batch.CommitAsync().OrTimeout(StepWaitSeconds, "Deleting your scans");
                }
                await profile.DeleteAsync().OrTimeout(StepWaitSeconds, "Deleting your profile");
                dataRemoved = true;

                // 3. the sign-in itself
                await user.DeleteAsync().OrTimeout(StepWaitSeconds, "Deleting your account");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Delete account: " + e.Message);
                if (dataRemoved) ForgetOnThisPhone(uid, email);     // the database copy is gone, so the copy on this phone must not bring it back
                if (this == null) return;
                SetBusy(false);
                Say(dataRemoved ? msgPartly : Explain(e));
                return;
            }

            // 4. everything about this account on this phone
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

        string Explain(Exception e)
        {
            FirebaseException firebase = null;
            for (Exception x = e; x != null && firebase == null; x = x.InnerException) firebase = x as FirebaseException;
            if (e is AggregateException aggregate)
                foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                    for (Exception x = inner; x != null && firebase == null; x = x.InnerException) firebase = x as FirebaseException;

            if (firebase != null)
                switch ((AuthError)firebase.ErrorCode)
                {
                    case AuthError.WrongPassword:
                    case AuthError.InvalidCredential:
                        return msgWrongPassword;
                    case AuthError.TooManyRequests:
                        return msgTooManyTries;
                    case AuthError.NetworkRequestFailed:
                        return msgNoConnection;
                }
            if (!ConnectivityMonitor.Online || TaskLimits.IsNetworkProblem(e)) return msgNoConnection;
            return string.Format(msgFailed, firebase != null ? firebase.Message : e.InnerException != null ? e.InnerException.Message : e.Message);
        }

        void SetBusy(bool value)
        {
            busy = value;
            if (confirmDeleteButton != null) confirmDeleteButton.interactable = !value;
            if (cancelDeleteButton != null) cancelDeleteButton.interactable = !value;
            if (passwordInput != null) passwordInput.interactable = !value;
            if (confirmDeleteLabel != null) confirmDeleteLabel.text = value ? deletingLabel : deleteLabel;
        }

        void Say(string message) { if (deleteMessageText != null) deleteMessageText.text = message; }
    }
}
