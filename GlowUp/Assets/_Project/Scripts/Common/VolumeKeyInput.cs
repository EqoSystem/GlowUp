using System;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// The phone's volume buttons as an app button (phase 2A, 11 November 2026). Android normally keeps these keys for the volume and
    /// Unity never sees them, so a small Android part (Assets/Plugins/Android/GlowUpVolumeKeys.java) takes them while a screen asks:
    /// SetActive(true) on entering, SetActive(false) on leaving, TakePresses() once per frame.
    /// In the Editor the keyboard's Page Up / Page Down stand in for the volume keys. On other platforms nothing happens.
    /// </summary>
    public static class VolumeKeyInput
    {
        static bool active;

        public static void SetActive(bool on)
        {
            if (active == on) return;
            active = on;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var keys = new AndroidJavaClass("com.glowup.input.GlowUpVolumeKeys"))
                    keys.CallStatic("setActive", activity, on);
            }
            catch (Exception e) { Debug.LogWarning("Volume buttons could not be " + (on ? "taken" : "given back") + ": " + e.Message); }
#endif
        }

        /// <summary>Presses since the last call (0 while not active).</summary>
        public static int TakePresses()
        {
            if (!active) return 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var keys = new AndroidJavaClass("com.glowup.input.GlowUpVolumeKeys"))
                    return keys.CallStatic<int>("takePresses");
            }
            catch (Exception) { return 0; }
#elif UNITY_EDITOR
            return Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown) ? 1 : 0;
#else
            return 0;
#endif
        }
    }
}
