using System;
using GlowUp.Performance;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// AR QUALITY CONTROLLER: decides which of the two AR Try-On pipelines runs, and holds every number of both.
    ///
    ///   Full  (strong phones): dense face mesh, blend shaders, light adaptation, mouth mask, the face is held when it is lost.
    ///   Light (weak phones):   small overlay picture, only the colour layer that is shown is drawn, no light adaptation,
    ///                          a frame-rate cap while AR Try-On is open, and (optionally) the guided preview instead of live tracking.
    ///
    /// Which one a phone gets is the user's choice on the Settings page (AR Try-On quality). On "Automatic" it comes from the phone's
    /// memory (limits in the PerformanceProfile asset) and from a short frame-rate check while AR Try-On runs: when the full pipeline
    /// cannot keep up, the phone is moved to the light one at once and stays there.
    /// ArFaceTrackingController asks this object for every number it needs; nothing else needs to be changed in code.
    /// </summary>
    public class ArAdaptiveQuality : MonoBehaviour
    {
        [Header("The frame-rate check (only when the Settings page says Automatic)")]
        [Tooltip("On = while AR Try-On runs in the full pipeline, the frame rate is measured; a phone that is too slow is moved to the light pipeline")]
        [SerializeField] private bool checkFrameRate = true;
        [Tooltip("Seconds to wait after live tracking started before measuring (the camera and the face mesh are starting up)")]
        [Min(0f)] [SerializeField] private float warmUpSeconds = 2f;
        [Tooltip("Seconds the frame rate is measured for")]
        [Min(0.5f)] [SerializeField] private float measureSeconds = 1.5f;
        [Tooltip("Below this many frames per second the phone is moved to the light pipeline")]
        [Min(5f)] [SerializeField] private float slowBelowFps = 20f;

        [Header("Full pipeline")]
        [Tooltip("The live picture is drawn at most this many pixels high")]
        [SerializeField] private int fullPictureHeight = 1280;
        [Tooltip("The light follows the room (brightness, warmth, skin colour)")]
        [SerializeField] private bool fullLightAdaptation = true;
        [Tooltip("The make-up is kept off the teeth and the tongue when the mouth opens")]
        [SerializeField] private bool fullMouthMask = true;
        [Tooltip("A face that is lost for a moment (a fast turn of the head) stays on screen and fades, instead of vanishing")]
        [SerializeField] private bool fullHoldLostFace = true;

        [Header("Light pipeline")]
        [Tooltip("The live picture is drawn at most this many pixels high (half the height is a quarter of the work)")]
        [SerializeField] private int lightPictureHeight = 540;
        [Tooltip("Frame-rate cap while AR Try-On is open (0 = leave the app's own cap). Face tracking itself arrives at about 30 a second on a selfie camera.")]
        [SerializeField] private int lightFrameRate = 30;
        [Tooltip("On = only the colour layer of the part being tried is drawn (and the highlight), not all five")]
        [SerializeField] private bool lightDrawsOnlyShownLayer = true;
        [SerializeField] private bool lightLightAdaptation = false;
        [SerializeField] private bool lightMouthMask = false;
        [SerializeField] private bool lightHoldLostFace = true;
        [Tooltip("On = a phone in the light pipeline uses the guided preview (the face outline and a flat 2D overlay) and no live tracking at all")]
        [SerializeField] private bool lightUsesGuidedPreview = false;

        /// <summary>Raised when the frame-rate check moved the phone to the light pipeline while AR Try-On was open.</summary>
        public event Action TierChanged;

        public ArTier Tier { get; private set; } = ArTier.Full;
        public bool IsLight => Tier == ArTier.Light;
        public int PictureHeight => IsLight ? lightPictureHeight : fullPictureHeight;
        public bool LightAdaptation => IsLight ? lightLightAdaptation : fullLightAdaptation;
        public bool MouthMask => IsLight ? lightMouthMask : fullMouthMask;
        public bool HoldLostFace => IsLight ? lightHoldLostFace : fullHoldLostFace;
        public bool DrawOnlyShownLayer => IsLight && lightDrawsOnlyShownLayer;
        /// <summary>True when this phone should not use live tracking at all.</summary>
        public bool PreferGuidedPreview => IsLight && lightUsesGuidedPreview;

        bool running, measured;
        float sinceStart, sumTime; int frames;
        int earlierFrameRate = -2;

        /// <summary>Live tracking is about to start: the pipeline is chosen and the frame-rate cap is set.</summary>
        public void BeginSession()
        {
            ArQualitySettings.Initialise();
            Tier = ArQualitySettings.Tier;
            running = true; measured = false; sinceStart = 0f; sumTime = 0f; frames = 0;
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
            if (!running || measured || !checkFrameRate || IsLight || ArQualitySettings.Choice != ArQualityChoice.Automatic) return;
            sinceStart += Time.unscaledDeltaTime;
            if (sinceStart < warmUpSeconds) return;
            sumTime += Time.unscaledDeltaTime; frames++;
            if (sumTime < measureSeconds) return;
            measured = true;
            float fps = frames / Mathf.Max(0.01f, sumTime);
            if (fps >= slowBelowFps) { Debug.Log("[GlowUp] AR Try-On frame-rate check: " + fps.ToString("0") + " fps, the full pipeline stays."); return; }
            Debug.Log("[GlowUp] AR Try-On frame-rate check: only " + fps.ToString("0") + " fps. Moving this phone to the light pipeline.");
            ArQualitySettings.RememberSlow();
            Tier = ArTier.Light;
            ApplyFrameCap();
            TierChanged?.Invoke();
        }

        void OnDisable() { EndSession(); }
    }
}
