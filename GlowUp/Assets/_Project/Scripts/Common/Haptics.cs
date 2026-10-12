using System;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// A short buzz of the phone, as a sign that something was done (a part of the scan ring became full, the scan is
    /// finished). It follows the "Vibration" switch that other screens may offer through <see cref="Enabled"/>, does
    /// nothing in the Editor, and never throws: a phone without a vibration motor simply stays quiet.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Off = the phone never buzzes.</summary>
        public static bool Enabled = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static bool looked;

        static AndroidJavaObject Vibrator()
        {
            if (looked) return vibrator;
            looked = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator != null && !vibrator.Call<bool>("hasVibrator")) vibrator = null;
            }
            catch (Exception e) { vibrator = null; Debug.LogWarning("[Haptics] " + e.Message); }
            return vibrator;
        }
#endif

        /// <summary>One short buzz of about `milliseconds` (25 = a light tick).</summary>
        public static void Tick(int milliseconds = 25)
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AndroidJavaObject v = Vibrator();
                if (v == null) return;
                long length = Math.Max(5, Math.Min(400, milliseconds));
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 26)
                    {
                        using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (AndroidJavaObject effect = effects.CallStatic<AndroidJavaObject>("createOneShot", length, -1))     // -1 = the phone's usual strength
                            v.Call("vibrate", effect);
                        return;
                    }
                }
                v.Call("vibrate", length);
            }
            catch (Exception e) { Debug.LogWarning("[Haptics] " + e.Message); }
#endif
        }
    }
}
