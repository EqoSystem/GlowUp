using System;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// The user's choices for how the app feels (Settings page), saved on the phone: tap sounds, the air
    /// wave that changes screen, the light that runs round the screen edge, and the camera helpers (bright
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

        /// <summary>Old: shake the phone to change screen. The air wave below replaced it; kept so a scene that still has the old row keeps working.</summary>
        public static bool ShakeToNavigate { get => Get("GlowUp.Ui.ShakeToNavigate", false); set => Set("GlowUp.Ui.ShakeToNavigate", value); }
        /// <summary>Wave a hand past the selfie camera: to the right for the next screen, to the left to go back. Off until the user switches it on.</summary>
        public static bool AirGestures { get => Get("GlowUp.Ui.AirGestures", false); set => Set("GlowUp.Ui.AirGestures", value); }
        /// <summary>How easily the air wave reacts: 0 = only a big, clear wave, 1 = a small wave is enough. 0.5 is the middle.</summary>
        public static float AirSensitivity
        {
            get => PlayerPrefs.GetFloat("GlowUp.Ui.AirSensitivity", 0.5f);
            set { PlayerPrefs.SetFloat("GlowUp.Ui.AirSensitivity", Mathf.Clamp01(value)); Save(); }
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

        static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
        static void Set(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); Save(); }
        static void Save() { PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
