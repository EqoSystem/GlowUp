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

        /// <summary>The volume-up and volume-down presses since the last call (both 0 while not active).</summary>
        public static void Take(out int up, out int down)
        {
            up = down = 0;
            if (!active) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var keys = new AndroidJavaClass("com.glowup.input.GlowUpVolumeKeys"))
                {
                    up = keys.CallStatic<int>("takeUpPresses");
                    down = keys.CallStatic<int>("takeDownPresses");
                }
            }
            catch (Exception) { up = down = 0; }
#elif UNITY_EDITOR
            up = Input.GetKeyDown(KeyCode.PageUp) ? 1 : 0;
            down = Input.GetKeyDown(KeyCode.PageDown) ? 1 : 0;
#endif
        }
    }
}
