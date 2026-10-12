using System;
using Firebase.Auth;
using GlowUp.Data;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Account Info on the Settings page: the name, the email and the account ID of whoever is signed
    /// in (from Firebase Auth), and whether they signed in online or offline.
    /// </summary>
    public class AccountInfoPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI emailText;
        [SerializeField] private TextMeshProUGUI userIdText;
        [SerializeField] private TextMeshProUGUI signInStateText;

        [Header("Wording")]
        [SerializeField] private string nameWhenUnknown = "Your account";
        [SerializeField] private string emailWhenUnknown = "No email on this account";
        [Tooltip("{0} = the account ID")]
        [SerializeField] private string userIdFormat = "Account ID: {0}";
        [SerializeField] private string userIdWhenUnknown = "Account ID: not available";
        [SerializeField] private string signedInOnline = "Signed in. Your scans and profile are saved to your account.";
        [SerializeField] private string signedInOffline = "Signed in without internet. Changes are sent to your account after you sign in online.";
        [SerializeField] private string notSignedIn = "Not signed in.";

        async void Start()
        {
            Show(null);
            UserProfileData profile = null;
            GlowUp.Common.TextSkeleton.Waiting(this);       // the profile is being fetched (bars meanwhile only if TextSkeleton is switched on)
            try { profile = await UserProfileRepository.Load(); }
            catch (Exception e) { Debug.LogWarning("Settings: profile not loaded: " + e.Message); }
            GlowUp.Common.TextSkeleton.Ready(this);
            if (this == null) return;
            Show(profile);
        }

        void Show(UserProfileData profile)
        {
            FirebaseUser user = null;
            try { user = FirebaseAuth.DefaultInstance.CurrentUser; } catch (Exception) { /* Firebase is not available on this device */ }

            string fullName = profile == null ? "" : string.Join(" ", new[] { profile.firstName, profile.lastName }).Trim();
            if (string.IsNullOrWhiteSpace(fullName) && profile != null) fullName = profile.username;
            string email = user != null && !string.IsNullOrEmpty(user.Email) ? user.Email : profile != null ? profile.email : UserProfileRepository.OfflineEmail;
            string uid = UserProfileRepository.ActiveUid;

            if (nameText != null) nameText.text = string.IsNullOrWhiteSpace(fullName) ? nameWhenUnknown : fullName;
            if (emailText != null) emailText.text = string.IsNullOrWhiteSpace(email) ? emailWhenUnknown : email;
            if (userIdText != null) userIdText.text = string.IsNullOrEmpty(uid) ? userIdWhenUnknown : string.Format(userIdFormat, uid);
            if (signInStateText != null)
                signInStateText.text = user != null ? signedInOnline : !string.IsNullOrEmpty(uid) || !string.IsNullOrEmpty(UserProfileRepository.OfflineEmail) ? signedInOffline : notSignedIn;
        }
    }
}
