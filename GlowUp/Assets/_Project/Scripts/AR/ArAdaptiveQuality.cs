using System;
using GlowUp.Performance;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// AR QUALITY CONTROLLER: decides which of the two AR Try-On pipelines runs, and holds every number of both.
    ///
    ///   Full  (strong phones): dense face mesh, blend shaders, light adaptation, mouth mask, the face is held when it is lost,
    ///                          the face mesh is smoothed, and the highlight of a finish follows the face's surface.
    ///   Light (weak phones):   small overlay picture, only the colour layer that is shown is drawn, no light adaptation,
    ///                          a frame-rate cap while AR Try-On is open, and (optionally) the guided preview instead of live tracking.
    ///
    /// Which one a phone gets is the user's choice on the Settings page (AR Try-On quality). On "Automatic" it comes from the phone's
    /// memory (limits in the PerformanceProfile asset) and from the frame times measured while AR Try-On runs (ArTierPolicy):
    /// a phone that cannot keep up is moved to the light pipeline; a phone that was moved down and then runs the light pipeline
    /// steadily gets ONE trial of the full pipeline per visit (kept when it keeps up, undone at once when it does not).
    /// ArFaceTrackingController asks this object for every number it needs; nothing else needs to be changed in code.
    /// </summary>
    public class ArAdaptiveQuality : MonoBehaviour
    {
        [Header("The frame-time check (only when the Settings page says Automatic)")]
        [Tooltip("On = while AR Try-On runs, frame times are measured and the pipeline follows them")]
        [SerializeField] private bool checkFrameRate = true;
        [Tooltip("Seconds to wait after live tracking started before measuring (the camera and the face mesh are starting up)")]
        [Min(0f)] [SerializeField] private float warmUpSeconds = 2f;
        [Tooltip("Seconds in one measuring window")]
        [Min(0.5f)] [SerializeField] private float measureSeconds = 1.5f;
        [Tooltip("Below this many frames per second (average of a window) the phone is moved to the light pipeline")]
        [Min(5f)] [SerializeField] private float slowBelowFps = 20f;
        [Tooltip("Above this 95th-percentile frame time (ms) in a window the phone is moved to the light pipeline (stutter, even when the average is fine)")]
        [Min(20f)] [SerializeField] private float slowP95Ms = 80f;
        [Tooltip("On = a phone that was moved down automatically may try the full pipeline again (once per visit) when the light one runs steadily")]
        [SerializeField] private bool tryFullAgain = true;
        [Tooltip("Seconds the light pipeline must run steadily (at its cap, 95th percentile below the next number) before the trial")]
        [Min(5f)] [SerializeField] private float steadySecondsBeforeTrial = 20f;
        [Tooltip("95th-percentile frame time (ms) below which the light pipeline counts as steady")]
        [Min(20f)] [SerializeField] private float steadyP95Ms = 45f;

        [Header("Full pipeline")]
        [Tooltip("The live picture is drawn at most this many pixels high")]
        [SerializeField] private int fullPictureHeight = 1280;
        [Tooltip("The light follows the room (brightness, warmth, skin colour)")]
        [SerializeField] private bool fullLightAdaptation = true;
        [Tooltip("The make-up is kept off the teeth and the tongue when the mouth opens")]
        [SerializeField] private bool fullMouthMask = true;
        [Tooltip("A face that is lost for a moment (a fast turn of the head) stays on screen and fades, instead of vanishing")]
        [SerializeField] private bool fullHoldLostFace = true;
        [Tooltip("The trembling of the face mesh is smoothed (AR Mesh Smoother)")]
        [SerializeField] private bool fullMeshSmoothing = true;
        [Tooltip("The highlight of glossy, satin, shimmer and metallic finishes follows the face's surface (shader GlowUp/FaceMakeupGloss)")]
        [SerializeField] private bool fullMovingHighlight = true;

        [Header("Light pipeline")]
        [Tooltip("The live picture is drawn at most this many pixels high (half the height is a quarter of the work)")]
        [SerializeField] private int lightPictureHeight = 540;
        [Tooltip("Frame-rate cap while AR Try-On is open (0 = leave the app's own cap). Face tracking itself arrives at about 30 a second on a selfie camera.")]
        [SerializeField] private int lightFrameRate = 30;
        [Tooltip("On = only the colour layer of the part being tried is drawn (and the highlight), not all of them")]
        [SerializeField] private bool lightDrawsOnlyShownLayer = true;
        [SerializeField] private bool lightLightAdaptation = false;
        [SerializeField] private bool lightMouthMask = false;
        [SerializeField] private bool lightHoldLostFace = true;
        [SerializeField] private bool lightMeshSmoothing = false;
        [SerializeField] private bool lightMovingHighlight = false;
        [Tooltip("On = a phone in the light pipeline uses the guided preview (the face outline and a flat 2D overlay) and no live tracking at all")]
        [SerializeField] private bool lightUsesGuidedPreview = false;

        /// <summary>Raised when the frame-time check moved the phone to another pipeline while AR Try-On was open.</summary>
        public event Action TierChanged;

        public ArTier Tier => policy.Tier;
        public bool IsLight => Tier == ArTier.Light;
        public int PictureHeight => IsLight ? lightPictureHeight : fullPictureHeight;
        public bool LightAdaptation => IsLight ? lightLightAdaptation : fullLightAdaptation;
        public bool MouthMask => IsLight ? lightMouthMask : fullMouthMask;
        public bool HoldLostFace => IsLight ? lightHoldLostFace : fullHoldLostFace;
        public bool MeshSmoothing => IsLight ? lightMeshSmoothing : fullMeshSmoothing;
        public bool MovingHighlight => IsLight ? lightMovingHighlight : fullMovingHighlight;
        public bool DrawOnlyShownLayer => IsLight && lightDrawsOnlyShownLayer;
        public bool AdaptiveEnabled => checkFrameRate || debugAdaptiveEnabled;
        /// <summary>True when this phone should not use live tracking at all.</summary>
        public bool PreferGuidedPreview => IsLight && lightUsesGuidedPreview;
        /// <summary>The numbers of the last finished window (for the debug overlay).</summary>
        public ArTierPolicy.Window LastWindow { get; private set; }
        /// <summary>True while the full pipeline is being tried again after a move down.</summary>
        public bool InTrial => policy.InTrial;

        readonly ArTierPolicy policy = new ArTierPolicy();
        const int MaxFrames = 512;
        readonly float[] frameTimes = new float[MaxFrames];
        readonly float[] sorted = new float[MaxFrames];
        bool running;
        bool debugAdaptiveEnabled;
        float sinceStart, windowTime; int frames;
        int earlierFrameRate = -2;

        public void SetDebugAdaptiveEnabled(bool value) { checkFrameRate = value; debugAdaptiveEnabled = value; }

        /// <summary>Live tracking is about to start: the pipeline is chosen and the frame-rate cap is set.</summary>
        public void BeginSession()
        {
            ArQualitySettings.Initialise();
            policy.SlowBelowFps = slowBelowFps; policy.SlowP95Ms = slowP95Ms;
            policy.SteadyP95Ms = steadyP95Ms; policy.SteadySecondsBeforeTrial = steadySecondsBeforeTrial;
            policy.UpgradeTrialsPerVisit = tryFullAgain ? 1 : 0;
            policy.Begin(ArQualitySettings.Tier, ArQualitySettings.FoundSlow);
            running = true; sinceStart = 0f; windowTime = 0f; frames = 0;
            ApplyFrameCap();
            Debug.Log("[GlowUp] AR Try-On pipeline: " + Tier + " (choice " + ArQualitySettings.Choice + ", " + ArQualitySettings.DescribeDevice() + ").");
        }

        /// <summary>Live tracking stopped: the app's own frame-rate cap comes back.</summary>
        public void EndSession()
        {
            running = false;
            if (earlierFrameRate != -2) { Application.targetFrameRate = earlierFrameRate; earlierFrameRate = -2; }
        }

        void ApplyFrameCap()
        {
            if (IsLight && lightFrameRate > 0)
            {
                if (earlierFrameRate == -2) earlierFrameRate = Application.targetFrameRate;
                Application.targetFrameRate = lightFrameRate;
            }
            else if (earlierFrameRate != -2) { Application.targetFrameRate = earlierFrameRate; earlierFrameRate = -2; }
        }

        void Update()
        {
            if (!running) return;
            float dt = Time.unscaledDeltaTime;
            sinceStart += dt;
            if (sinceStart < warmUpSeconds) return;
            if (frames < MaxFrames) frameTimes[frames] = dt;
            frames++; windowTime += dt;
            if (windowTime < measureSeconds) return;

            var window = new ArTierPolicy.Window
            {
                averageFps = frames / Mathf.Max(0.01f, windowTime),
                p95Ms = ArTierPolicy.P95Ms(frameTimes, Mathf.Min(frames, MaxFrames), sorted),
                seconds = windowTime
            };
            LastWindow = window;
            windowTime = 0f; frames = 0;
            if (!checkFrameRate && !debugAdaptiveEnabled) return;

            bool automatic = debugAdaptiveEnabled || ArQualitySettings.Choice == ArQualityChoice.Automatic;
            ArTierPolicy.Verdict verdict = policy.Observe(window, automatic, lightFrameRate);
            switch (verdict)
            {
                case ArTierPolicy.Verdict.MoveToLight:
                    Debug.Log("[GlowUp] AR Try-On frame-time check: " + window.averageFps.ToString("0") + " fps, 95% of frames within " + window.p95Ms.ToString("0") + " ms. Moving this phone to the light pipeline.");
                    ArQualitySettings.RememberSlow();
                    ApplyFrameCap();
                    sinceStart = 0f;                     // the new pipeline gets its own warm-up
                    TierChanged?.Invoke();
                    break;
                case ArTierPolicy.Verdict.TryFull:
                    Debug.Log("[GlowUp] AR Try-On runs steadily in the light pipeline (" + window.p95Ms.ToString("0") + " ms). Trying the full pipeline again.");
                    ApplyFrameCap();
                    sinceStart = 0f;
                    TierChanged?.Invoke();
                    break;
                case ArTierPolicy.Verdict.TrialPassed:
                    Debug.Log("[GlowUp] AR Try-On: the full pipeline keeps up now; it stays.");
                    ArQualitySettings.ForgetSlow();
                    break;
            }
        }

        void OnDisable() { EndSession(); }
    }
}
