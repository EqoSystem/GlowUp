using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using GlowUp.Data;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// THE ACCOUNT OF A SIGN-UP THAT IS STILL GOING ON, and the "confirm your email" message.
    ///
    /// Since 12 October 2026 the email is confirmed on sign-up Step 1 (the user's decision). Firebase can only send its
    /// confirmation message to an account that exists, so the account is made on Step 1, as soon as everything there is
    /// filled in correctly, instead of on the Terms page at the end. Until the Terms page is passed it is an account
    /// without a profile: leaving the sign-up removes it again, so the email stays free.
    ///
    /// Every call here has a time limit and never throws: it comes back with what happened, and the page says so.
    /// The password is handed to Firebase Authentication only; it is not kept or logged here.
    /// </summary>
    public static class SignUpAccount
    {
        public enum Outcome { Ok, Offline, EmailInUse, WeakPassword, BadEmail, TooManyTries, NotSignedIn, Failed }

        public class Result
        {
            public Outcome outcome = Outcome.Failed;
            /// <summary>The account's id (after CreateOrSignIn).</summary>
            public string uid = "";
            /// <summary>True when CreateOrSignIn made a NEW account (false: this email and password had one already).</summary>
            public bool madeNew;
            /// <summary>True when the email of the signed-in account has been confirmed.</summary>
            public bool confirmed;
            /// <summary>What the service said, for a failure the page has no wording for.</summary>
            public string detail = "";
            public bool Ok => outcome == Outcome.Ok;
        }

        /// <summary>The longest one step may take (seconds).</summary>
        public static float WaitSeconds = 25f;

        static FirebaseUser User => FirebaseAuth.DefaultInstance.CurrentUser;

        /// <summary>True when somebody is signed in and it is this account.</summary>
        public static bool SignedInAs(string uid)
        {
            try { return !string.IsNullOrEmpty(uid) && User != null && User.UserId == uid; }
            catch (Exception) { return false; }
        }

        /// <summary>What the phone knows now (without asking the service): is the signed-in account's email confirmed?</summary>
        public static bool ConfirmedSoFar
        {
            get { try { return User != null && User.IsEmailVerified; } catch (Exception) { return false; } }
        }

        /// <summary>
        /// Makes the account. When this email has one already, the same email and password prove it is the same person
        /// (a sign-up that was begun before): they are signed in to it instead. Another password: EmailInUse.
        /// </summary>
        public static async Task<Result> CreateOrSignIn(string email, string password)
        {
            var result = new Result();
            if (!ConnectivityMonitor.Online || !await FirebaseBootstrap.Ready) { result.outcome = Outcome.Offline; return result; }
            try
            {
                AuthResult made = await FirebaseAuth.DefaultInstance.CreateUserWithEmailAndPasswordAsync(email, password).OrTimeout(WaitSeconds, "Creating the account");
                result.outcome = Outcome.Ok; result.madeNew = true; result.uid = made.User.UserId; result.confirmed = made.User.IsEmailVerified;
                return result;
            }
            catch (Exception first)
            {
                if (CodeOf(first) != AuthError.EmailAlreadyInUse) return Failed(result, first);
            }
            try
            {
                AuthResult again = await FirebaseAuth.DefaultInstance.SignInWithEmailAndPasswordAsync(email, password).OrTimeout(WaitSeconds, "Signing in");
                result.outcome = Outcome.Ok; result.madeNew = false; result.uid = again.User.UserId; result.confirmed = again.User.IsEmailVerified;
                return result;
            }
            catch (Exception second)
            {
                if (TaskLimits.IsNetworkProblem(second)) return Failed(result, second);
                result.outcome = Outcome.EmailInUse;            // the email has an account with another password: it is somebody's already
                return result;
            }
        }

        /// <summary>Sends Firebase's "confirm your email" message to the signed-in account's address.</summary>
        public static async Task<Result> SendConfirmation()
        {
            var result = new Result();
            if (!ConnectivityMonitor.Online || !await FirebaseBootstrap.Ready) { result.outcome = Outcome.Offline; return result; }
            FirebaseUser user = User;
            if (user == null) { result.outcome = Outcome.NotSignedIn; return result; }
            try
            {
                await user.SendEmailVerificationAsync().OrTimeout(WaitSeconds, "Sending the confirmation email");
                result.outcome = Outcome.Ok; result.uid = user.UserId;
                return result;
            }
            catch (Exception e) { return Failed(result, e); }
        }

        /// <summary>Asks the service whether the email has been confirmed by now (the user tapped the link in the message).</summary>
        public static async Task<Result> CheckConfirmed()
        {
            var result = new Result();
            if (!ConnectivityMonitor.Online || !await FirebaseBootstrap.Ready) { result.outcome = Outcome.Offline; return result; }
            FirebaseUser user = User;
            if (user == null) { result.outcome = Outcome.NotSignedIn; return result; }
            try
            {
                await user.ReloadAsync().OrTimeout(WaitSeconds, "Checking the confirmation");
                user = User ?? user;
                result.outcome = Outcome.Ok; result.uid = user.UserId; result.confirmed = user.IsEmailVerified;
                return result;
            }
            catch (Exception e) { return Failed(result, e); }
        }

        /// <summary>
        /// Removes the signed-in account. ONLY for an account that was made during this sign-up and has no profile yet
        /// (the user leaves the sign-up, or wants another email). The caller makes sure of that.
        /// </summary>
        public static async Task<Result> Delete(string uid, float waitSeconds = -1f)
        {
            var result = new Result();
            if (!SignedInAs(uid)) { result.outcome = Outcome.NotSignedIn; return result; }
            if (!ConnectivityMonitor.Online || !await FirebaseBootstrap.Ready) { result.outcome = Outcome.Offline; return result; }
            try
            {
                await User.DeleteAsync().OrTimeout(waitSeconds > 0f ? waitSeconds : WaitSeconds, "Removing the unfinished account");
                result.outcome = Outcome.Ok;
                return result;
            }
            catch (Exception e) { return Failed(result, e); }
        }

        // ---------------------------------------------------------------------------------------
        static AuthError CodeOf(Exception e)
        {
            FirebaseException firebase = TaskLimits.FirebaseError(e);
            return firebase != null ? (AuthError)firebase.ErrorCode : AuthError.None;
        }

        static Result Failed(Result result, Exception e)
        {
            AuthError code = CodeOf(e);
            result.outcome = TaskLimits.IsNetworkProblem(e) || code == AuthError.NetworkRequestFailed ? Outcome.Offline
                : code == AuthError.EmailAlreadyInUse ? Outcome.EmailInUse
                : code == AuthError.WeakPassword ? Outcome.WeakPassword
                : code == AuthError.InvalidEmail ? Outcome.BadEmail
                : code == AuthError.TooManyRequests ? Outcome.TooManyTries
                : Outcome.Failed;
            FirebaseException firebase = TaskLimits.FirebaseError(e);
            result.detail = firebase != null ? firebase.Message : e.InnerException != null ? e.InnerException.Message : e.Message;
            Debug.LogWarning("Sign-up account: " + result.outcome + " (" + result.detail + ")");
            return result;
        }

        // ---------------------------------------------------------------------------------------
        //  Plain arithmetic for the "Send again" button (covered by SignUpAccountTests)
        // ---------------------------------------------------------------------------------------
        /// <summary>Whole seconds until the message may be sent again (0 = now). `sentAt` and `now` are on the same clock.</summary>
        public static int SecondsUntilSendAgain(float sentAt, float now, float waitSeconds)
        {
            if (sentAt < 0f) return 0;                              // nothing was sent yet
            float left = sentAt + Mathf.Max(0f, waitSeconds) - now;
            return left <= 0f ? 0 : Mathf.CeilToInt(left);
        }
    }
}
