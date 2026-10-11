using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using GlowUp.Data;
using UnityEngine;
#if GLOWUP_FACEBOOK
using Facebook.Unity;
#endif

namespace GlowUp.Auth
{
    /// <summary>
    /// Signing in with a Google or a Facebook account (the two round buttons on the login page).
    ///
    /// GOOGLE needs nothing extra in the project: Firebase opens Google's own sign-in page in the phone's
    /// browser and returns to the app. In the Firebase console the Google provider must be switched on and
    /// the app's SHA-1 fingerprint added (Project settings > Your apps), then the new google-services.json
    /// put in Assets. It only works on a phone, not in the Editor.
    ///
    /// FACEBOOK needs Facebook's own SDK (their rules do not allow the browser way): import the "Facebook
    /// SDK for Unity" package, type the App ID under Facebook > Edit Settings, and switch the Facebook
    /// provider on in the Firebase console with the same App ID and App secret. The project then switches
    /// this code on by itself (symbol GLOWUP_FACEBOOK, see Editor/FacebookSdkDefine.cs).
    /// </summary>
    public static class SocialSignIn
    {
        public const string Google = "google.com", Facebook = "facebook.com", Password = "password";

        /// <summary>How a sign-in ended. Exactly one of User / Cancelled / Problem says what happened.</summary>
        public class Outcome
        {
            public FirebaseUser User;
            public bool Cancelled;
            public string Problem;
            public bool Ok => User != null;
        }

        /// <summary>True when the Facebook SDK is in the project (otherwise the Facebook button explains what is missing).</summary>
        public static bool FacebookInstalled
        {
            get
            {
#if GLOWUP_FACEBOOK
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Which way this account signs in: "password", "google.com" or "facebook.com".</summary>
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

#if GLOWUP_FACEBOOK
        public static async Task<Outcome> WithFacebook()
        {
            try
            {
                string token = await FacebookToken();
                if (token == null) return new Outcome { Cancelled = true };
                FirebaseUser user = await FirebaseAuth.DefaultInstance.SignInWithCredentialAsync(FacebookAuthProvider.GetCredential(token));
                return new Outcome { User = user };
            }
            catch (Exception e) { return Failed(e, "Facebook"); }
        }

        /// <summary>Opens Facebook's sign-in. Returns the access token, or null when the user closed it.</summary>
        static async Task<string> FacebookToken()
        {
            if (!FB.IsInitialized)
            {
                var ready = new TaskCompletionSource<bool>();
                FB.Init(() => ready.TrySetResult(true));
                await ready.Task;
            }
            if (!FB.IsInitialized) throw new InvalidOperationException("Facebook could not start. Check the App ID under Facebook > Edit Settings.");
            FB.ActivateApp();

            var answered = new TaskCompletionSource<ILoginResult>();
            FB.LogInWithReadPermissions(new List<string> { "public_profile", "email" }, result => answered.TrySetResult(result));
            ILoginResult login = await answered.Task;
            if (login == null || login.Cancelled) return null;
            if (!string.IsNullOrEmpty(login.Error)) throw new InvalidOperationException(login.Error);
            string token = AccessToken.CurrentAccessToken != null ? AccessToken.CurrentAccessToken.TokenString : null;
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Facebook did not return a sign-in token.");
            return token;
        }
#else
        public static Task<Outcome> WithFacebook()
        {
            return Task.FromResult(new Outcome { Problem = "Facebook sign-in needs the Facebook SDK for Unity in the project (see SocialSignIn.cs)." });
        }
#endif

        /// <summary>
        /// Proves again that it is really this person (Firebase asks for it before an account is deleted).
        /// For accounts without a password: Google's or Facebook's sign-in opens once more.
        /// </summary>
        public static async Task Reauthenticate(FirebaseUser user)
        {
            string provider = ProviderOf(user);
            if (provider == Google) { await user.ReauthenticateWithProviderAsync(GoogleProvider()); return; }
#if GLOWUP_FACEBOOK
            if (provider == Facebook)
            {
                string token = await FacebookToken();
                if (token == null) throw new OperationCanceledException("Sign-in was closed.");
                await user.ReauthenticateAsync(FacebookAuthProvider.GetCredential(token));
                return;
            }
#endif
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
