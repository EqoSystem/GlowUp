using System;
using UnityEngine;

namespace GlowUp.Performance
{
    /// <summary>What the user picked for AR Try-On on the Settings page.</summary>
    public enum ArQualityChoice { Automatic = 0, High = 1, Light = 2 }

    /// <summary>The two AR Try-On pipelines.</summary>
    public enum ArTier { Full = 0, Light = 1 }

    /// <summary>
    /// AR Try-On quality on the Settings page, and which pipeline this phone gets.
    ///
    ///  - Full:  dense face mesh, blend shaders with soft edges, light adaptation, mouth mask, hold on a lost face.
    ///  - Light: small overlay picture, one colour layer at a time, no light adaptation, a frame-rate cap while AR Try-On is open.
    ///
    /// Automatic decides from the phone's memory (limits in the PerformanceProfile asset) and from a short frame-rate check
    /// while AR Try-On runs (ArAdaptiveQuality): a phone that cannot keep up is moved to Light and stays there. The user can
    /// choose High or Light on the Settings page at any time; a choice made there is never changed automatically.
    /// Like PerformanceSettings, it needs no object in the Hierarchy.
    /// </summary>
    public static class ArQualitySettings
    {
        const string ChoiceKey = "GlowUp.Ar.Quality";
        const string SlowKey = "GlowUp.Ar.FoundSlow";

        /// <summary>Raised after the choice changed (the Settings page repaints itself).</summary>
        public static event Action Changed;

        public static ArQualityChoice Choice { get; private set; }
        /// <summary>True when a frame-rate check on this phone found AR Try-On too slow in the full pipeline.</summary>
        public static bool FoundSlow { get; private set; }

        static bool initialised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { initialised = false; Initialise(); }

        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;
            Choice = (ArQualityChoice)Mathf.Clamp(PlayerPrefs.GetInt(ChoiceKey, 0), 0, 2);
            FoundSlow = PlayerPrefs.GetInt(SlowKey, 0) == 1;
        }

        /// <summary>The pipeline AR Try-On uses now.</summary>
        public static ArTier Tier
        {
            get
            {
                Initialise();
                if (Choice == ArQualityChoice.High) return ArTier.Full;
                if (Choice == ArQualityChoice.Light) return ArTier.Light;
                return AutomaticTier();
            }
        }

        /// <summary>What Automatic gives this phone (without the user's choice).</summary>
        public static ArTier AutomaticTier()
        {
            Initialise();
            if (FoundSlow) return ArTier.Light;
            PerformanceSettings.Initialise();
            PerformanceProfile profile = PerformanceSettings.Profile;
            int memory = SystemInfo.systemMemorySize;
            if (profile == null || memory <= 0) return ArTier.Full;                       // the phone does not say: the full pipeline, the frame-rate check will catch a slow one
            if (profile.arWeakGraphicsMemoryMb > 0 && SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize <= profile.arWeakGraphicsMemoryMb) return ArTier.Light;
            if (memory >= profile.arHighMinMemoryMb) return ArTier.Full;
            if (memory < profile.arLowBelowMemoryMb) return ArTier.Light;
            return profile.arMidRangeGetsFullPipeline ? ArTier.Full : ArTier.Light;
        }

        /// <summary>The user picked a choice on the Settings page.</summary>
        public static void SetChoice(ArQualityChoice choice)
        {
            Initialise();
            Choice = choice;
            PlayerPrefs.SetInt(ChoiceKey, (int)choice);
            if (choice == ArQualityChoice.Automatic) { FoundSlow = false; PlayerPrefs.DeleteKey(SlowKey); }     // "Automatic" starts the frame-rate check afresh
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>The frame-rate check found the full pipeline too slow. Only changes what Automatic does.</summary>
        public static void RememberSlow()
        {
            Initialise();
            if (Choice != ArQualityChoice.Automatic || FoundSlow) return;
            FoundSlow = true;
            PlayerPrefs.SetInt(SlowKey, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>A trial of the full pipeline kept up: the mark of an earlier slow check is removed (Automatic only).</summary>
        public static void ForgetSlow()
        {
            Initialise();
            if (Choice != ArQualityChoice.Automatic || !FoundSlow) return;
            FoundSlow = false;
            PlayerPrefs.DeleteKey(SlowKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>For the Settings page: what this phone is and what it gets.</summary>
        public static string DescribeDevice()
        {
            Initialise();
            int memory = SystemInfo.systemMemorySize;
            string ram = memory <= 0 ? "memory not reported" : memory >= 1024 ? (memory / 1024f).ToString("0.#") + " GB memory" : memory + " MB memory";
            return ram + ", " + SystemInfo.processorCount + "-core processor";
        }
    }
}
