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
    /// GOOGLE, since 7 November 2026: the phone's own Google account sheet (Android Credential Manager, NativeGoogleSignIn) gives an
    /// ID token and Firebase signs in with it. No browser is opened, so browsers that block or split storage (Brave, strict privacy
    /// modes) can no longer break it with "missing initial state" on firebaseapp.com. Since 10 November 2026 the browser is NEVER used: when the
    /// phone has no Google account or cannot run its own sign-in, a clear message says so (NoGoogleAccount / GoogleUnavailable).
    /// In the Firebase console the Google provider must be switched on and the app's SHA-1 fingerprint added (Project settings >
    /// Your apps), then the new google-services.json put in Assets. It only works on a phone, not in the Editor.
    /// </summary>
    public static class SocialSignIn
    {
        public const string Google = "google.com", Password = "password";
        /// <summary>What the user reads when Google sign-in did not finish. Never Firebase's own text.</summary>
        public const string DidNotComplete = "Google sign-in didn't complete. Please try again.";
        /// <summary>The same, when the browser's own privacy settings are what stopped it (Firebase: "missing initial state" / sessionStorage / storage-partitioned).</summary>
        public const string BrowserBlocking = "Your browser may be blocking sign-in. Try disabling shields/privacy blocking for this site, or use a different browser.";
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
            // 1. inside the app (no browser)
            Task<NativeGoogleSignIn.Answer> asking = NativeGoogleSignIn.RequestToken();
            // the phone's sheet gets 3 minutes; an answer that never comes is reported (step 1) instead of leaving the page silent
            if (await Task.WhenAny(asking, Task.Delay(180000)) != asking) return new Outcome { Problem = DidNotComplete + " (step 1: no answer from the phone)" };
            NativeGoogleSignIn.Answer native = asking.Result;
            if (native.result == NativeGoogleSignIn.Result.Cancelled) return new Outcome { Cancelled = true };
            if (native.result == NativeGoogleSignIn.Result.Token)
            {
                try
                {
                    Credential credential = GoogleAuthProvider.GetCredential(native.idToken, null);
                    AuthResult signedIn = await FirebaseAuth.DefaultInstance.SignInAndRetrieveDataWithCredentialAsync(credential).OrTimeout(40f, "Signing in with Google");
                    return new Outcome { User = signedIn != null ? signedIn.User : FirebaseAuth.DefaultInstance.CurrentUser };
                }
                catch (Exception e) { return Failed(e, "Google"); }
            }
            if (native.result == NativeGoogleSignIn.Result.Failed)
            {
                return new Outcome { Problem = DidNotComplete + " (step 1: " + native.detail.Trim() + ")" };
            }
            // 2. NO BROWSER any more (10 November 2026). The old fallback opened Google's page in the phone's browser and came back through
            //    glowup-admin-portal.firebaseapp.com/__/auth/handler. Browsers that partition or clear storage (Brave with Shields, private
            //    tabs, some in-app browsers) lose the sign-in state there and show Firebase's black "missing initial state" page, which the
            //    app cannot style or recover from. A clear message is shown instead.
            if (native.result == NativeGoogleSignIn.Result.NoAccount) return new Outcome { Problem = NoGoogleAccount };
            { }
            return new Outcome { Problem = GoogleUnavailable + " (step 1: " + native.result + " " + native.detail.Trim() + ")" };
        }

        /// <summary>No Google account on the phone (or none was picked).</summary>
        public const string NoGoogleAccount = "No Google account was found on this phone. Add one in your phone's Settings > Accounts, then try again, or sign in with your email.";
        /// <summary>The phone's Google sign-in cannot run (no Google Play services, or this build is missing its setup).</summary>
        public const string GoogleUnavailable = "Google sign-in isn't available on this phone right now. Please sign in with your email.";

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
            if (provider == Google)
            {
                NativeGoogleSignIn.Answer native = await NativeGoogleSignIn.RequestToken();
                if (native.result == NativeGoogleSignIn.Result.Cancelled) throw new OperationCanceledException();
                if (native.result == NativeGoogleSignIn.Result.Token) { await user.ReauthenticateAsync(GoogleAuthProvider.GetCredential(native.idToken, null)); return; }
                throw new InvalidOperationException(native.result == NativeGoogleSignIn.Result.NoAccount ? NoGoogleAccount : GoogleUnavailable);   // never the browser (see WithGoogle)
            }
            throw new InvalidOperationException("This account cannot be checked again on this phone.");
        }

        // ---------------------------------------------------------------------------------------
        static bool BlockedByBrowser(string text) =>
            text.IndexOf("missing initial state", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("sessionStorage", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("storage-partitioned", StringComparison.OrdinalIgnoreCase) >= 0;

        static Outcome Failed(Exception e, string name)
        {
            FirebaseException firebase = TaskLimits.FirebaseError(e);
            AuthError code = firebase != null ? (AuthError)firebase.ErrorCode : AuthError.None;
            { }
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
                    return new Outcome { Problem = name + " sign-in isn't available right now. Try again later, or sign in with your email." };   // (the Google provider is switched off in the project settings)
                case AuthError.UserDisabled:
                    return new Outcome { Problem = "This account has been disabled." };
            }
            if (e is OperationCanceledException) return new Outcome { Cancelled = true };
            // The raw text is only logged (above), never shown. The sign-in page opens in the phone's browser; a browser that blocks or splits
            // its storage (Brave with Shields, strict privacy modes, some in-app browsers) makes Firebase answer "missing initial state".
            string detail = (firebase != null ? firebase.Message : "") + " " + e.Message + " " + (e.InnerException != null ? e.InnerException.Message : "");
            string shown = (firebase != null ? "firebase " + code + ": " : e.GetType().Name + ": ") + (firebase != null ? firebase.Message : e.Message);
            return new Outcome { Problem = BlockedByBrowser(detail) ? BrowserBlocking : DidNotComplete + " (step 2: " + (shown.Length > 120 ? shown.Substring(0, 120) : shown) + ")" };
        }
    }
}
