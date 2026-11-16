using System;
using UnityEngine;

namespace GlowUp.Performance
{
    /// <summary>The two graphics choices on the Settings page.</summary>
    public enum GraphicsMode { Performance = 0, HighQuality = 1 }

    /// <summary>
    /// Everything about "optimisation for low-end phones" in one asset:
    /// Assets/_Project/Resources/PerformanceProfile.asset (select it in the Project window to edit).
    ///
    ///  - How a phone is judged the first time the app opens (memory, processor, graphics memory).
    ///  - What Performance Mode and High Quality each switch on.
    ///  - The two frame-rate choices.
    ///
    /// The app reads this asset when it starts. If the asset is missing, the values written below are used.
    /// </summary>
    [CreateAssetMenu(menuName = "GlowUp/Performance Profile", fileName = "PerformanceProfile")]
    public class PerformanceProfile : ScriptableObject
    {
        public const string ResourceName = "PerformanceProfile";

        [Serializable]
        public class ModeSettings
        {
            [Tooltip("The Quality level this mode switches to. Must be a name from Edit > Project Settings > Quality (this project has Performant, Balanced, High Fidelity).")]
            public string qualityLevel = "Balanced";
            [Tooltip("Frames per second used with this mode until the user picks a frame rate themselves")]
            public int defaultFrameRate = 60;
            [Tooltip("Share of the phone's own screen resolution the app draws at. Lower = less work for the graphics chip, slightly softer picture. Phones only.")]
            [Range(0.5f, 1f)] public float resolutionScale = 1f;
            [Tooltip("On: decorative glow and sweep animations are skipped (the buttons still react to a tap)")]
            public bool reduceEffects = false;
        }

        [Header("Automatic choice (the first time the app opens)")]
        [Tooltip("On: the phone is checked once and the mode is chosen for the user. Off: every phone starts in the mode below.")]
        public bool detectOnFirstLaunch = true;
        [Tooltip("The mode used when detection is off, or when the phone does not tell us its specifications")]
        public GraphicsMode modeWhenNotDetected = GraphicsMode.HighQuality;

        [Header("A phone counts as low-end when enough of these checks are true")]
        [Tooltip("Memory (RAM) at or below this many megabytes. 3072 = 3 GB.")]
        public int lowMemoryMb = 3072;
        [Tooltip("Processor cores at or below this number")]
        public int lowProcessorCores = 4;
        [Tooltip("Processor speed at or below this many MHz (ignored when the phone does not report it)")]
        public int lowProcessorMhz = 1900;
        [Tooltip("Graphics memory at or below this many megabytes (ignored when the phone does not report it)")]
        public int lowGraphicsMemoryMb = 1024;
        [Tooltip("How many of the checks above must be true before the phone is put in Performance Mode")]
        [Range(1, 4)] public int checksNeededForLowEnd = 2;

        [Header("Performance Mode (low-end phones)")]
        public ModeSettings performance = new ModeSettings { qualityLevel = "Performant", defaultFrameRate = 30, resolutionScale = 0.8f, reduceEffects = true };

        [Header("High Quality")]
        public ModeSettings highQuality = new ModeSettings { qualityLevel = "High Fidelity", defaultFrameRate = 60, resolutionScale = 1f, reduceEffects = false };

        [Header("AR Try-On: which pipeline a phone gets (Settings > AR Try-On quality > Automatic)")]
        [Tooltip("Memory (RAM) at or above this many megabytes: the full pipeline (dense face mesh, blend shaders, light adaptation, mouth mask). A phone sold as 6 GB reports about 5500, so 5500 is the line for \"more than 6 GB\" phones.")]
        public int arHighMinMemoryMb = 5500;
        [Tooltip("Memory below this many megabytes: the lighter pipeline (small overlay picture, one colour layer at a time, no light adaptation, frame-rate cap). A phone sold as 4 GB reports 3500 to 3900, so 4500 puts every 4 GB phone on the lighter pipeline (phase 3B, 11 November 2026; was 3800).")]
        public int arLowBelowMemoryMb = 4500;
        [Tooltip("A processor with this many cores or fewer counts as low-end: the lighter pipeline whatever the memory (0 = not used)")]
        public int arLowEndMaxCores = 4;
        [Tooltip("A processor whose top speed is below this many MHz counts as low-end: the lighter pipeline (0 = not used; a phone that does not report its speed is not judged by it)")]
        public int arLowEndBelowMhz = 1800;
        [Tooltip("A phone between the two limits gets: on = the full pipeline, off = the lighter one")]
        public bool arMidRangeGetsFullPipeline = true;
        [Tooltip("Graphics memory at or below this many megabytes counts as a weak graphics chip (0 = this check is not used). A weak chip gets the lighter pipeline whatever its RAM.")]
        public int arWeakGraphicsMemoryMb = 0;

        [Header("Frame rate choices on the Settings page")]
        [Tooltip("Saves battery and keeps budget phones cooler")]
        public int batterySaverFrameRate = 30;
        [Tooltip("Smoother animations")]
        public int smoothFrameRate = 60;

        public ModeSettings Get(GraphicsMode mode) => mode == GraphicsMode.Performance ? performance : highQuality;
    }
}
