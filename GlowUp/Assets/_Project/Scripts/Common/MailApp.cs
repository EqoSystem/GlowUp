using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Opens the Gmail app when the user taps "Open Gmail" (see Assets/Plugins/Android/GlowUpMail.java for the order it tries:
    /// Gmail, then its Play Store page, then the phone's own email app). It never reads mail and never opens anything by itself.
    /// </summary>
    public static class MailApp
    {
        public enum Result { Gmail, Store, MailApp, None, NotOnThisPlatform }

        public static Result OpenGmail()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var mail = new AndroidJavaClass("com.glowup.mail.GlowUpMail"))
                {
                    switch (mail.CallStatic<string>("open", activity))
                    {
                        case "gmail": return Result.Gmail;
                        case "store": return Result.Store;
                        case "mail": return Result.MailApp;
                        default: return Result.None;
                    }
                }
            }
            catch (System.Exception e) { { } return Result.None; }
#else
            return Result.NotOnThisPlatform;
#endif
        }
    }
}
