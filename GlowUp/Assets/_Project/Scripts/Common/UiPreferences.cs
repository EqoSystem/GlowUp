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

        /// <summary>
        /// The "wave to change screen", "shake to change screen" and finger-sign features are gone.
        /// What an earlier version of the app saved for them is removed once; nothing else of the user's settings is touched.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ForgetRemovedFeatures()
        {
            foreach (string key in new[] { "GlowUp.Ui.AirGestures", "GlowUp.Ui.AirSensitivity", "GlowUp.Ui.ShakeToNavigate", "GlowUp.Ui.FingerSigns", "GlowUp.Ui.FingerSignHold", "GlowUp.Ui.FingerSign.1", "GlowUp.Ui.FingerSign.2", "GlowUp.Ui.FingerSign.3", "GlowUp.Ui.FingerSign.4", "GlowUp.Ui.FingerSign.5" })
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

        /// <summary>
        /// New20 (owner, 17 November 2026): which key chooses which mode on AI Smart Scan. True (the default) = volume + is photo capture and
        /// volume − is live scan; false = the other way round. Pressing the key of the mode already on is the trigger (take the photo /
        /// start or pause the scan); both keys together open "Type it in", and both again close it.
        /// </summary>
        public static bool VolumeUpTakesPhoto { get => Get("GlowUp.Camera.VolumeUpTakesPhoto", true); set => Set("GlowUp.Camera.VolumeUpTakesPhoto", value); }

        /// <summary>What one key does, as the settings line and the scan screen say it.</summary>
        public static string VolumeKeysLine(bool upTakesPhoto) =>
            (upTakesPhoto ? "Volume + = photo capture, volume − = live scan." : "Volume + = live scan, volume − = photo capture.") +
            " Press the same key again to take the photo or start / pause the scan. Both keys together open or close Type it in.";

        static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
        static void Set(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); Save(); }
        static void Save() { PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
