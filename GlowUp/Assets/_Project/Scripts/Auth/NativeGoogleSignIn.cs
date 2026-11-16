using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Auth
{
    /// <summary>
    /// Google's own account sheet on the phone (Android Credential Manager, Assets/Plugins/Android/GlowUpGoogleSignIn.java):
    /// gives a Google ID token without opening a browser (7 November 2026). SocialSignIn turns the token into a Firebase sign-in.
    /// Anywhere else (the Editor, an older build without the libraries) it answers Unavailable, and the browser way is used.
    /// </summary>
    public static class NativeGoogleSignIn
    {
        /// <summary>The OAuth web client id of the Firebase project (from google-services.json, client_type 3). Not a secret.</summary>
        public const string WebClientId = "996836291256-lvrlvtacr22aed795f4uh15ql8hu8jio.apps.googleusercontent.com";

        public enum Result { Token, Cancelled, NoAccount, Unavailable, Failed }

        public class Answer
        {
            public Result result = Result.Unavailable;
            public string idToken = "", email = "", displayName = "", detail = "";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.signin.GlowUpGoogleSignIn";

        public static Task<Answer> RequestToken()
        {
            var done = new TaskCompletionSource<Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                using (var plugin = new AndroidJavaClass(JavaClass))
                {
                    if (!plugin.CallStatic<bool>("isAvailable")) { done.TrySetResult(new Answer()); return done.Task; }
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                        plugin.CallStatic("signIn", activity, WebClientId, new Reply(done));
                    }
                }
            }
            catch (Exception e)
            {
                { }
                done.TrySetResult(new Answer());
            }
            return done.Task;
        }

        class Reply : AndroidJavaProxy
        {
            readonly TaskCompletionSource<Answer> done;
            public Reply(TaskCompletionSource<Answer> done) : base(JavaClass + "$Callback") { this.done = done; }
            [Preserve] public void onToken(string idToken, string email, string displayName) =>
                done.TrySetResult(new Answer { result = string.IsNullOrEmpty(idToken) ? Result.Failed : Result.Token, idToken = idToken ?? "", email = email ?? "", displayName = displayName ?? "" });
            [Preserve] public void onCancelled() => done.TrySetResult(new Answer { result = Result.Cancelled });
            [Preserve] public void onError(string code, string message) =>
                done.TrySetResult(new Answer { result = code == "no-account" ? Result.NoAccount : code == "unavailable" ? Result.Unavailable : Result.Failed, detail = (code ?? "") + " " + (message ?? "") });
        }
#else
        public static Task<Answer> RequestToken() => Task.FromResult(new Answer());
#endif
    }
}
