using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlowUp.AR
{
    /// <summary>
    /// AR LIGHT ADAPTER: makes the virtual make-up follow the room.
    ///
    /// The colour layers already MULTIPLY the camera picture (shader GlowUp/FaceMakeupBlend), so a lipstick gets darker in a dim
    /// room and warmer under a lamp by itself. What this script adds, from a tiny copy of the camera picture a few times a second:
    ///  - the SKIN COLOUR: the layers assume a skin tone ("Skin Under It"); the middle of the picture (where the face is) is
    ///    measured and the assumed tone moves towards it (its hue, not its brightness), so a shade looks right on dark and on pale skin;
    ///  - the HIGHLIGHT: the shine of a glossy or shimmer finish is weaker in a dim room and stronger in bright sun;
    ///  - LOW LIGHT: when the picture stays dark, a short kind hint is offered (the face tracker struggles in the dark).
    /// It costs one small copy and one tiny read-back (32 x 32 pixels) every few tenths of a second.
    /// </summary>
    public class ArLightAdapter : MonoBehaviour
    {
        [Header("What is measured")]
        [Tooltip("How often the picture is looked at (seconds). Larger = lighter for the phone.")]
        [Min(0.1f)] [SerializeField] private float sampleSeconds = 0.4f;
        [Tooltip("The tiny copy of the picture is this many pixels on each side")]
        [Range(8, 64)] [SerializeField] private int sampleSize = 32;
        [Tooltip("The skin colour is measured in the middle of the picture: this share of the width and height (0.3 = the middle 30 percent)")]
        [Range(0.1f, 0.8f)] [SerializeField] private float centerShare = 0.3f;

        [Header("Skin colour")]
        [SerializeField] private bool adaptSkinColour = true;
        [Tooltip("How far the assumed skin tone moves towards the measured one (0 = not at all, 1 = fully)")]
        [Range(0f, 1f)] [SerializeField] private float skinStrength = 0.6f;
        [Tooltip("How quickly the assumed tone follows (bigger = quicker)")]
        [Min(0.1f)] [SerializeField] private float followSpeed = 3f;

        [Header("Highlight (the shine of a finish)")]
        [SerializeField] private bool adaptHighlight = true;
        [Tooltip("The picture's brightness (0 to 1) at which the highlight is as the finish says")]
        [Range(0.1f, 0.9f)] [SerializeField] private float referenceBrightness = 0.45f;
        [Tooltip("The weakest the highlight gets in a dark room (1 = as the finish says)")]
        [Range(0f, 1f)] [SerializeField] private float darkestHighlight = 0.35f;
        [Tooltip("The strongest the highlight gets in bright light")]
        [Range(1f, 2f)] [SerializeField] private float brightestHighlight = 1.15f;

        [Header("Low light")]
        [Tooltip("The picture counts as dark below this brightness (0 to 1)")]
        [Range(0.02f, 0.5f)] [SerializeField] private float darkBelow = 0.2f;
        [Tooltip("The hint comes after the picture has stayed dark this many seconds")]
        [Min(0f)] [SerializeField] private float hintAfterSeconds = 2.5f;
        [SerializeField] private string msgLowLight = "It is quite dark here. Face a window or a lamp for a steadier try-on.";

        /// <summary>The picture's brightness, 0 (black) to 1 (white).</summary>
        public float Brightness { get; private set; } = 0.5f;
        /// <summary>True when the picture has stayed dark long enough for the hint.</summary>
        public bool LowLight { get; private set; }
        /// <summary>The words of the low-light hint.</summary>
        public string LowLightMessage => msgLowLight;
        /// <summary>Set by the tracker: skin is only measured while a face is in view.</summary>
        public bool FaceVisible { get; set; }

        ARCameraBackground background;
        ArFaceMakeup makeup;
        RenderTexture tiny;
        Texture2D readBack;
        float nextSample, darkFor;
        float skinAmount;
        Color skinNow = Color.white, skinTarget = Color.white;
        float gainNow = 1f, gainTarget = 1f;
        bool running;

        /// <summary>Live tracking started: the camera's background is what is measured.</summary>
        public void BeginSession(Camera arCamera, ArFaceMakeup targetMakeup)
        {
            makeup = targetMakeup;
            background = arCamera != null ? arCamera.GetComponent<ARCameraBackground>() : null;
            running = background != null && makeup != null;
            LowLight = false; darkFor = 0f; nextSample = 0f; skinAmount = 0f; gainNow = gainTarget = 1f;
            if (makeup != null) { makeup.ShineGain = 1f; makeup.SkinAdapt = 0f; }
        }

        /// <summary>Live tracking stopped: everything is put back and the tiny picture is released.</summary>
        public void EndSession()
        {
            running = false;
            LowLight = false;
            if (makeup != null) { makeup.ShineGain = 1f; makeup.SkinAdapt = 0f; }
            if (tiny != null) { tiny.Release(); Destroy(tiny); tiny = null; }
            if (readBack != null) { Destroy(readBack); readBack = null; }
        }

        void Update()
        {
            if (!running || makeup == null) return;
            if (Time.unscaledTime >= nextSample) { nextSample = Time.unscaledTime + sampleSeconds; Sample(); }

            float t = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            // skin colour: only while a face is in view
            float wanted = adaptSkinColour && FaceVisible ? skinStrength : 0f;
            skinAmount = Mathf.Lerp(skinAmount, wanted, t);
            skinNow = Color.Lerp(skinNow, skinTarget, t);
            makeup.SkinSample = skinNow;
            makeup.SkinAdapt = skinAmount;
            // highlight
            gainNow = Mathf.Lerp(gainNow, adaptHighlight ? gainTarget : 1f, t);
            makeup.ShineGain = gainNow;
        }

        void Sample()
        {
            if (background == null || background.material == null) return;
            try
            {
                if (tiny == null)
                {
                    tiny = new RenderTexture(sampleSize, sampleSize, 0, RenderTextureFormat.ARGB32) { name = "AR light sample", filterMode = FilterMode.Bilinear };
                    tiny.Create();
                    readBack = new Texture2D(sampleSize, sampleSize, TextureFormat.RGBA32, false);
                }
                Graphics.Blit(null, tiny, background.material);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tiny;
                readBack.ReadPixels(new Rect(0, 0, sampleSize, sampleSize), 0, 0, false);
                readBack.Apply(false);
                RenderTexture.active = previous;
            }
            catch (System.Exception e) { Debug.LogWarning("AR Try-On: the light could not be measured, the colours stay as they are: " + e.Message); running = false; return; }

            Color32[] pixels = readBack.GetPixels32();
            double all = 0; int count = 0;
            double r = 0, g = 0, b = 0; int middle = 0;
            int from = Mathf.RoundToInt(sampleSize * (0.5f - centerShare * 0.5f)), to = Mathf.RoundToInt(sampleSize * (0.5f + centerShare * 0.5f));
            for (int y = 0; y < sampleSize; y++)
                for (int x = 0; x < sampleSize; x++)
                {
                    Color32 p = pixels[y * sampleSize + x];
                    all += Luma(p.r, p.g, p.b); count++;
                    if (x >= from && x < to && y >= from && y < to) { r += p.r; g += p.g; b += p.b; middle++; }
                }
            Brightness = count > 0 ? (float)(all / count) / 255f : 0.5f;
            gainTarget = Mathf.Clamp(Brightness / Mathf.Max(0.05f, referenceBrightness), darkestHighlight, brightestHighlight);

            if (middle > 0)
            {
                // the hue of the skin, kept at the brightness the layers assume (the brightness itself must keep following the room)
                var sample = new Color((float)(r / middle) / 255f, (float)(g / middle) / 255f, (float)(b / middle) / 255f, 1f);
                float assumed = makeup != null ? makeup.AssumedSkinBrightness : 0.6f;
                float have = Mathf.Max(0.02f, Luma(sample.r, sample.g, sample.b));
                float k = assumed / have;
                skinTarget = new Color(Mathf.Clamp01(sample.r * k), Mathf.Clamp01(sample.g * k), Mathf.Clamp01(sample.b * k), 1f);
            }

            if (Brightness < darkBelow) darkFor += sampleSeconds; else if (Brightness > darkBelow + 0.05f) darkFor = 0f;
            LowLight = darkFor >= hintAfterSeconds;
        }

        static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;
    }
}
