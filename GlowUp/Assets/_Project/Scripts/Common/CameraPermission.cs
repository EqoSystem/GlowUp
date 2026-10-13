using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.Common
{
    /// <summary>
    /// What the app needs to know about the phone's camera permission, in one place: is it given, may the app ask for
    /// it again, and how to open the phone's own settings page of this app when it may not.
    /// (Asking itself is done where the camera is started: CameraFeedController and ArFaceTrackingController.)
    /// </summary>
    public static class CameraPermission
    {
        /// <summary>True when the user has allowed the camera. Always true in the Editor and on a computer.</summary>
        public static bool Granted
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// True when asking again shows the phone's question again. After "Don't allow" was chosen twice (or "Don't ask
        /// again"), Android answers "no" by itself without showing anything: then only the phone's settings can allow it.
        /// Only meaningful AFTER the user has said no once (before the first question Android says false as well).
        /// </summary>
        public static bool CanAskAgain
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // asked of Android itself (this Unity version has no call of its own for it)
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        return activity.Call<bool>("shouldShowRequestPermissionRationale", Permission.Camera);
                }
                catch (System.Exception) { return true; }
#else
                return true;
#endif
            }
        }

        /// <summary>Opens this app's page in the phone's settings, where the camera can be allowed by hand.</summary>
        public static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var address = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", address))
                {
                    activity.Call("startActivity", intent);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("The phone's settings could not be opened: " + e.Message); }
#else
            Debug.Log("[GlowUp] On a phone this opens the app's page in the phone's settings.");
#endif
        }
    }
}
