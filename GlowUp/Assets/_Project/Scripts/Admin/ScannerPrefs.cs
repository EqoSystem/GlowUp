using System;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// The staff scanner's own settings (Scanner Preferences screen). Kept on this phone only; they change how the camera screens
    /// behave and never what is sent to the website.
    /// </summary>
    public static class ScannerPrefs
    {
        public static event Action Changed;

        public static bool Flash { get => Get("flash", false); set => Set("flash", value); }
        public static bool AutoFocus { get => Get("autofocus", true); set => Set("autofocus", value); }
        public static bool ScanLine { get => Get("scanline", true); set => Set("scanline", value); }
        public static bool GuideFrame { get => Get("guideframe", true); set => Set("guideframe", value); }
        /// <summary>Batch Mode: scanning goes on after each barcode (each product joins the batch), and the gallery takes several photos at once.</summary>
        public static bool BatchMode { get => Get("batch", false); set => Set("batch", value); }
        public static bool HighQuality { get => Get("highquality", true); set => Set("highquality", value); }

        /// <summary>The longest side and JPEG quality of the small copies that go to the reviewer.</summary>
        public static int EvidenceSide => HighQuality ? 800 : 560;
        public static int EvidenceQuality => HighQuality ? 65 : 50;

        static bool Get(string key, bool fallback)
        {
            try { return PlayerPrefs.GetInt("staff_pref_" + key, fallback ? 1 : 0) == 1; } catch (Exception) { return fallback; }
        }

        static void Set(string key, bool value)
        {
            try { PlayerPrefs.SetInt("staff_pref_" + key, value ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { }
            Changed?.Invoke();
        }
    }
}
