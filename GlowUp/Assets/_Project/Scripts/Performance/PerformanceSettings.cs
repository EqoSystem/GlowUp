using System;
using UnityEngine;

namespace GlowUp.Performance
{
    /// <summary>
    /// The app's graphics mode and frame-rate cap. It applies itself before the first scene opens,
    /// in every scene, without any object in the Hierarchy:
    ///
    ///  - First launch: the phone is checked (DeviceTierDetector) and Performance Mode or High Quality
    ///    is chosen automatically.
    ///  - Later launches: the saved choice is used. A choice the user made on the Settings page is
    ///    never changed automatically again.
    ///
    /// What each mode does is set in the PerformanceProfile asset (Assets/_Project/Resources).
    /// The Settings page calls SetMode / SetFrameRate / DetectAgain.
    /// </summary>
    public static class PerformanceSettings
    {
        const string ModeKey = "GlowUp.Graphics.Mode";
        const string ModeChosenKey = "GlowUp.Graphics.ModeChosenByUser";
        const string FrameRateKey = "GlowUp.Graphics.FrameRate";

        /// <summary>Raised after the mode or the frame rate changed (the Settings page repaints itself).</summary>
        public static event Action Changed;

        public static PerformanceProfile Profile { get; private set; }
        public static GraphicsMode Mode { get; private set; } = GraphicsMode.HighQuality;
        /// <summary>True when the user picked the mode on the Settings page; false when the app chose it.</summary>
        public static bool ModeChosenByUser { get; private set; }
        public static int FrameRate { get; private set; } = 60;
        /// <summary>True when the user picked the frame rate; false while it follows the mode.</summary>
        public static bool FrameRateChosenByUser => PlayerPrefs.HasKey(FrameRateKey);
        /// <summary>What was read from this phone.</summary>
        public static DeviceTierDetector.Report Device { get; private set; }
        /// <summary>Decorative animations ask this before they play.</summary>
        public static bool ReduceEffects => initialised && Profile != null && Profile.Get(Mode).reduceEffects;

        static bool initialised;
        static int nativeWidth, nativeHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { initialised = false; Initialise(); }

        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;

            Profile = Resources.Load<PerformanceProfile>(PerformanceProfile.ResourceName);
            if (Profile == null) Profile = ScriptableObject.CreateInstance<PerformanceProfile>();      // the values written in the script
            Device = DeviceTierDetector.Inspect(Profile);

            ModeChosenByUser = PlayerPrefs.GetInt(ModeChosenKey, 0) == 1;
            if (PlayerPrefs.HasKey(ModeKey))
            {
                Mode = (GraphicsMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey), 0, 1);
                // a mode the APP chose (not the user) follows the current rule: a phone judged by an older rule is judged again
                if (!ModeChosenByUser && Profile.detectOnFirstLaunch && Mode != Device.suggested)
                {
                    Mode = Device.suggested;
                    PlayerPrefs.SetInt(ModeKey, (int)Mode);
                    PlayerPrefs.Save();
                }
            }
            else
            {
                // first launch on this phone: choose for the user
                Mode = Profile.detectOnFirstLaunch ? Device.suggested : Profile.modeWhenNotDetected;
                PlayerPrefs.SetInt(ModeKey, (int)Mode);
                PlayerPrefs.SetInt(ModeChosenKey, 0);
                PlayerPrefs.Save();
                { }
            }
            FrameRate = PlayerPrefs.HasKey(FrameRateKey) ? PlayerPrefs.GetInt(FrameRateKey) : Profile.Get(Mode).defaultFrameRate;
            Apply();
        }

        /// <summary>The user picked a graphics mode on the Settings page.</summary>
        public static void SetMode(GraphicsMode mode)
        {
            Initialise();
            Mode = mode; ModeChosenByUser = true;
            PlayerPrefs.SetInt(ModeKey, (int)mode);
            PlayerPrefs.SetInt(ModeChosenKey, 1);
            if (!FrameRateChosenByUser) FrameRate = Profile.Get(mode).defaultFrameRate;     // the frame rate follows the mode until the user picks one
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>The user picked a frame-rate cap on the Settings page.</summary>
        public static void SetFrameRate(int framesPerSecond)
        {
            Initialise();
            FrameRate = Mathf.Clamp(framesPerSecond, 15, 120);
            PlayerPrefs.SetInt(FrameRateKey, FrameRate);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Forget the user's own choices and let the app decide again from the phone's specifications.</summary>
        public static void DetectAgain()
        {
            Initialise();
            Device = DeviceTierDetector.Inspect(Profile);
            Mode = Profile.detectOnFirstLaunch ? Device.suggested : Profile.modeWhenNotDetected;
            ModeChosenByUser = false;
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.SetInt(ModeChosenKey, 0);
            PlayerPrefs.DeleteKey(FrameRateKey);
            PlayerPrefs.Save();
            FrameRate = Profile.Get(Mode).defaultFrameRate;
            Apply();
            Changed?.Invoke();
        }

        static void Apply()
        {
            PerformanceProfile.ModeSettings settings = Profile.Get(Mode);

            // 1. the quality level (in this project each level uses its own render settings: shadows, anti-aliasing, render scale)
            string[] levels = QualitySettings.names;
            int level = Array.FindIndex(levels, n => string.Equals(n, settings.qualityLevel, StringComparison.OrdinalIgnoreCase));
            if (level < 0) { }

            // In the Editor the project's own Quality settings are left alone: a change made while playing there
            // would be written into the project. On a phone the level and vSync are switched for real.
            if (!Application.isEditor)
            {
                if (level >= 0 && QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
                QualitySettings.vSyncCount = 0;         // vSync would otherwise override the frame-rate cap
            }

            // 2. the frame-rate cap
            Application.targetFrameRate = FrameRate;

            // 3. draw at a share of the screen's own resolution (phones only; the Editor and desktop windows are left alone)
            if (Application.isMobilePlatform)
            {
                if (nativeWidth == 0) { nativeWidth = Screen.width; nativeHeight = Screen.height; }     // read once, before anything is scaled
                float scale = Mathf.Clamp(settings.resolutionScale, 0.5f, 1f);
                int width = Mathf.RoundToInt(nativeWidth * scale), height = Mathf.RoundToInt(nativeHeight * scale);
                if (Screen.width != width || Screen.height != height) Screen.SetResolution(width, height, true);
            }
        }
    }
}
