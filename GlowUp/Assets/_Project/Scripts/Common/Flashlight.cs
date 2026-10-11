using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Switches the phone's flashlight (the light next to the back camera) on and off.
    ///
    /// Good to know: many Android phones refuse to switch the flashlight on while an app is showing
    /// the same camera's picture. TrySet then returns false with the reason, and the caller tells
    /// the user (the scanner makes the screen brighter instead).
    /// </summary>
    public static class Flashlight
    {
        public static bool IsOn { get; private set; }

        /// <summary>Returns true when the phone accepted the change. `problem` says why not otherwise.</summary>
        public static bool TrySet(bool on, out string problem)
        {
            problem = null;
            if (!on && !IsOn) return true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject manager = activity.Call<AndroidJavaObject>("getSystemService", "camera"))
                using (var keys = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics"))
                using (AndroidJavaObject hasFlashKey = keys.GetStatic<AndroidJavaObject>("FLASH_INFO_AVAILABLE"))
                {
                    string[] ids = manager.Call<string[]>("getCameraIdList");
                    foreach (string id in ids)
                    {
                        using (AndroidJavaObject about = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id))
                        using (AndroidJavaObject hasFlash = about.Call<AndroidJavaObject>("get", hasFlashKey))
                        {
                            if (hasFlash == null || !hasFlash.Call<bool>("booleanValue")) continue;
                            manager.Call("setTorchMode", id, on);
                            IsOn = on;
                            return true;
                        }
                    }
                }
                problem = "This phone has no flashlight.";
                return false;
            }
            catch (System.Exception e)
            {
                IsOn = false;
                problem = e.Message;
                Debug.LogWarning("Flashlight: " + e.Message);
                return false;
            }
#else
            IsOn = on;              // in the Editor there is no flashlight: pretend, so the button can be tried
            return true;
#endif
        }
    }
}
