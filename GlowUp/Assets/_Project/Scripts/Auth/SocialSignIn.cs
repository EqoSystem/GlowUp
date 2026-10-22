using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// Signing in with a Google account (the round button on the login page). Facebook sign-in was removed on request.
    ///
    /// GOOGLE needs nothing extra in the project: Firebase opens Google's own sign-in page in the phone's
    /// browser and returns to the app. In the Firebase console the Google provider must be switched on and
    /// the app's SHA-1 fingerprint added (Project settings > Your apps), then the new google-services.json
    /// put in Assets. It only works on a phone, not in the Editor.
    /// </summary>
    public static class SocialSignIn
    {
        public const string Google = "google.com", Password = "password";
        const string Facebook = "facebook.com";          // only so that an account made that way in the past is still recognised

        /// <summary>How a sign-in ended. Exactly one of User / Cancelled / Problem says what happened.</summary>
        public class Outcome
        {
            public FirebaseUser User;
            public bool Cancelled;
            public string Problem;
            public bool Ok => User != null;
        }

        /// <summary>Which way this account signs in: "password", "google.com" or (an old account) "facebook.com".</summary>
        public static string ProviderOf(FirebaseUser user)
        {
            if (user == null) return Password;
            string found = null;
            foreach (IUserInfo info in user.ProviderData)
            {
                if (info == null) continue;
                if (info.ProviderId == Password) return Password;
                if (info.ProviderId == Google || info.ProviderId == Facebook) found = info.ProviderId;
            }
            return found ?? Password;
        }

        // ---------------------------------------------------------------------------------------
        public static async Task<Outcome> WithGoogle()
        {
            try
            {
                AuthResult result = await FirebaseAuth.DefaultInstance.SignInWithProviderAsync(GoogleProvider());
                return new Outcome { User = result != null ? result.User : FirebaseAuth.DefaultInstance.CurrentUser };
            }
            catch (Exception e) { return Failed(e, "Google"); }
        }

        static FederatedOAuthProvider GoogleProvider()
        {
            var data = new FederatedOAuthProviderData
            {
                ProviderId = Google,
                Scopes = new List<string> { "email", "profile" },
                CustomParameters = new Dictionary<string, string> { { "prompt", "select_account" } }     // always let the user pick the account
            };
            var provider = new FederatedOAuthProvider();
            provider.SetProviderData(data);
            return provider;
        }

        /// <summary>
        /// Proves again that it is really this person (Firebase asks for it before an account is deleted).
        /// For accounts without a password: Google's sign-in opens once more.
        /// </summary>
        public static async Task Reauthenticate(FirebaseUser user)
        {
            string provider = ProviderOf(user);
            if (provider == Google) { await user.ReauthenticateWithProviderAsync(GoogleProvider()); return; }
            throw new InvalidOperationException("This account cannot be checked again on this phone.");
        }

        // ---------------------------------------------------------------------------------------
        static Outcome Failed(Exception e, string name)
        {
            FirebaseException firebase = TaskLimits.FirebaseError(e);
            AuthError code = firebase != null ? (AuthError)firebase.ErrorCode : AuthError.None;
            Debug.LogWarning("[GlowUp] " + name + " sign-in: " + (firebase != null ? code + " " + firebase.Message : e.ToString()));
            switch (code)
            {
                case AuthError.WebContextCancelled:
                    return new Outcome { Cancelled = true };
                case AuthError.NetworkRequestFailed:
                    return new Outcome { Problem = "No internet connection. Connect and try again." };
                case AuthError.AccountExistsWithDifferentCredentials:
                case AuthError.EmailAlreadyInUse:
                    return new Outcome { Problem = "This email already has a Glow Up account. Sign in the way you did the first time." };
                case AuthError.OperationNotAllowed:
                    return new Outcome { Problem = name + " sign-in is not switched on for this app yet (Firebase console > Authentication > Sign-in method)." };
                case AuthError.UserDisabled:
                    return new Outcome { Problem = "This account has been disabled." };
            }
            if (e is OperationCanceledException) return new Outcome { Cancelled = true };
            string detail = firebase != null ? firebase.Message : e.InnerException != null ? e.InnerException.Message : e.Message;
            return new Outcome { Problem = name + " sign-in did not work: " + detail };
        }
    }
}
