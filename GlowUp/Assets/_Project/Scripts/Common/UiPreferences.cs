using System;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// The user's choices for how the app feels (Settings page), saved on the phone: tap sounds, the finger
    /// signs that change screen, the light that runs round the screen edge, and the camera helpers (bright
    /// screen, flashlight, screen light). Every script that offers one of these reads it here, so a switch on the
    /// Settings page and a button inside a screen always agree.
    /// </summary>
    public static class UiPreferences
    {
        /// <summary>Raised whenever one of the choices changes.</summary>
        public static event Action Changed;

        public static bool TapSounds { get => Get("GlowUp.Ui.TapSounds", true); set => Set("GlowUp.Ui.TapSounds", value); }
        /// <summary>Which of the tap sounds is used (0 = the first one in UiSoundPlayer's list).</summary>
        public static int TapSoundIndex
        {
            get => PlayerPrefs.GetInt("GlowUp.Ui.TapSoundIndex", 0);
            set { PlayerPrefs.SetInt("GlowUp.Ui.TapSoundIndex", Mathf.Max(0, value)); Save(); }
        }
        public static float TapSoundVolume
        {
            get => PlayerPrefs.GetFloat("GlowUp.Ui.TapSoundVolume", 0.7f);
            set { PlayerPrefs.SetFloat("GlowUp.Ui.TapSoundVolume", Mathf.Clamp01(value)); Save(); }
        }

        /// <summary>The freezing sound when a scan is removed in Recent Scans (on until the user switches it off). See DeleteSoundPlayer.</summary>
        public static bool DeleteSound { get => Get("GlowUp.Ui.DeleteSound", true); set => Set("GlowUp.Ui.DeleteSound", value); }

        /// <summary>The old "shake to change screen" is gone. Always off; kept only so the old ShakeNavigator script still compiles.</summary>
        public static bool ShakeToNavigate => false;

        // ---- finger signs: hold 1 to 5 fingers up to the selfie camera to open a screen ----
        /// <summary>REMOVED on 6 November 2026 (the Settings rows and the navigator are no longer built). Always off, so the selfie camera is never
        /// started for it; the property stays only so the old scripts still compile. Whatever a phone saved earlier is ignored.</summary>
        public static bool FingerSigns { get => false; set { } }

        /// <summary>How long a sign must be held before the screen changes, in seconds.</summary>
        public static float FingerSignHoldSeconds
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat("GlowUp.Ui.FingerSignHold", 0.9f), ShortestFingerHold, LongestFingerHold);
            set { PlayerPrefs.SetFloat("GlowUp.Ui.FingerSignHold", Mathf.Clamp(value, ShortestFingerHold, LongestFingerHold)); Save(); }
        }
        public const float ShortestFingerHold = 0.4f, LongestFingerHold = 2.5f;

        /// <summary>The screens a finger sign can open, in the order the Settings page offers them. "" = this number of fingers does nothing.</summary>
        public static readonly string[] FingerSignScreens = { SceneNames.Dashboard, SceneNames.AiSmartAnalysis, SceneNames.RecentScans, SceneNames.ArTryOn, SceneNames.Settings, "" };
        /// <summary>What the user reads for each of those screens.</summary>
        public static readonly string[] FingerSignScreenNames = { "Home", "AI Smart Scan", "Recent Scans", "AR Try-On", "Settings", "Nothing" };

        /// <summary>The screen that `fingers` raised fingers (1 to 5) open. As delivered: 1 Home, 2 AI Smart Scan, 3 Recent Scans, 4 AR Try-On, 5 Settings.</summary>
        public static string FingerSignScreen(int fingers)
        {
            if (fingers < 1 || fingers > 5) return "";
            string saved = PlayerPrefs.GetString("GlowUp.Ui.FingerSign." + fingers, FingerSignScreens[fingers - 1]);
            return System.Array.IndexOf(FingerSignScreens, saved) >= 0 ? saved : FingerSignScreens[fingers - 1];
        }

        public static void SetFingerSignScreen(int fingers, string scene)
        {
            if (fingers < 1 || fingers > 5 || System.Array.IndexOf(FingerSignScreens, scene ?? "") < 0) return;
            PlayerPrefs.SetString("GlowUp.Ui.FingerSign." + fingers, scene ?? "");
            Save();
        }

        /// <summary>The name the user reads for a screen ("Home" for the dashboard).</summary>
        public static string FingerSignScreenName(string scene)
        {
            int at = System.Array.IndexOf(FingerSignScreens, scene ?? "");
            return at >= 0 ? FingerSignScreenNames[at] : "Nothing";
        }

        /// <summary>Puts the five finger signs back to how the app was delivered.</summary>
        public static void ResetFingerSigns()
        {
            for (int fingers = 1; fingers <= 5; fingers++) PlayerPrefs.DeleteKey("GlowUp.Ui.FingerSign." + fingers);
            PlayerPrefs.DeleteKey("GlowUp.Ui.FingerSignHold");
            Save();
        }

        /// <summary>
        /// The "wave to change screen" and "shake to change screen" features are gone (finger signs took their place).
        /// What an earlier version of the app saved for them is removed once; nothing else of the user's settings is touched.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ForgetRemovedFeatures()
        {
            foreach (string key in new[] { "GlowUp.Ui.AirGestures", "GlowUp.Ui.AirSensitivity", "GlowUp.Ui.ShakeToNavigate" })
                if (PlayerPrefs.HasKey(key)) PlayerPrefs.DeleteKey(key);
        }
        /// <summary>The light that travels round the edge of every screen.</summary>
        public static bool BorderLight { get => Get("GlowUp.Ui.BorderLight", true); set => Set("GlowUp.Ui.BorderLight", value); }

        /// <summary>The screen goes to full brightness while scanning (easier to see the product outdoors).</summary>
        public static bool ScanBrightScreen { get => Get("GlowUp.Camera.ScanBrightScreen", true); set => Set("GlowUp.Camera.ScanBrightScreen", value); }
        /// <summary>The screen goes to full brightness in AR Try-On (it also lights the face a little).</summary>
        public static bool TryOnBrightScreen { get => Get("GlowUp.Camera.TryOnBrightScreen", true); set => Set("GlowUp.Camera.TryOnBrightScreen", value); }
        /// <summary>The flashlight is offered by itself when the scanner finds the place too dark.</summary>
        public static bool AutoFlashlight { get => Get("GlowUp.Camera.AutoFlashlight", true); set => Set("GlowUp.Camera.AutoFlashlight", value); }
        /// <summary>AR Try-On lights the face with a bright white frame on the screen when it is dark (the "front flash").</summary>
        public static bool TryOnScreenLight { get => Get("GlowUp.Camera.TryOnScreenLight", true); set => Set("GlowUp.Camera.TryOnScreenLight", value); }

        /// <summary>
        /// The phone's volume keys on the AI Smart Scan screen (Settings > Camera Helpers; owner's rule of 12 November 2026):
        ///   volume UP switches between live scan and photo capture; volume DOWN is the trigger (starts / pauses the live scan, takes the
        ///   photo in photo capture). Both = both keys work, Up / Down = only that key, Off = the keys only change the volume.
        /// </summary>
        public enum VolumeKeys { Both = 0, Up = 1, Down = 2, Off = 3 }

        public static VolumeKeys VolumeKey
        {
            get => (VolumeKeys)Mathf.Clamp(PlayerPrefs.GetInt("GlowUp.Camera.VolumeKeys", (int)VolumeKeys.Both), 0, 3);
            set { PlayerPrefs.SetInt("GlowUp.Camera.VolumeKeys", (int)value); Save(); }
        }

        static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
        static void Set(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); Save(); }
        static void Save() { PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
