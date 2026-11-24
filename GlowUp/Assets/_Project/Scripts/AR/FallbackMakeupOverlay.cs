using System;
using System.Collections.Generic;
using GlowUp.Scan;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// The guided try-on preview: colour shapes for the lips, cheeks, eyelids and whole face that sit
    /// on the face guide. The user lines their face up with the guide and the chosen shade is laid
    /// over the matching part at the chosen strength.
    /// Each shape is an ordinary Image: move or resize it in the Scene view to fine-tune where the
    /// colour lands. The strongest allowed opacity of each part is an Inspector field.
    ///
    /// The colour also has a FINISH (matte, satin, glossy, shimmer, metallic). A finish changes how
    /// solid the colour is, how much it is lightened, how strong the highlight on it is ("Shine", a
    /// child of each shape) and how much it sparkles ("Glitter", a child of each shape). The numbers
    /// of each finish are in the list "Finishes" below.
    ///
    /// This script also keeps the user's choices (part of the face, shade, strength, finish) for the
    /// live face tracking, so both previews always show the same thing.
    /// </summary>
    public class FallbackMakeupOverlay : MonoBehaviour
    {
        [Serializable]
        public class FinishLook
        {
            public string name = "matte";
            [Tooltip("How solid the colour is (1 = as set by the strength slider)")]
            [Range(0.3f, 1.2f)] public float opacity = 1f;
            [Tooltip("How strong the highlight is (0 = none)")]
            [Range(0f, 1f)] public float shine = 0f;
            [Tooltip("How much it sparkles (0 = not at all)")]
            [Range(0f, 1f)] public float glitter = 0f;
            [Tooltip("How much the colour is lightened (0 = not at all)")]
            [Range(0f, 0.6f)] public float brighten = 0f;
        }

        [Header("Shapes")]
        [SerializeField] private Image lips;
        [SerializeField] private Image cheekLeft;
        [SerializeField] private Image cheekRight;
        [SerializeField] private Image eyeLeft;
        [SerializeField] private Image eyeRight;
        [SerializeField] private Image faceTint;

        [Header("Opacity at full strength (0 = invisible, 1 = solid)")]
        [Range(0f, 1f)] [SerializeField] private float lipsMax = 0.78f;
        [Range(0f, 1f)] [SerializeField] private float cheeksMax = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float eyesMax = 0.62f;
        [Range(0f, 1f)] [SerializeField] private float faceMax = 0.34f;
        [Tooltip("Brows: live face tracking only (the guided preview has no brow shapes)")]
        [Range(0f, 1f)] [SerializeField] private float browsMax = 0.7f;

        [Header("Finish (the texture of the colour)")]
        [SerializeField] private FinishLook[] finishes =
        {
            new FinishLook { name = "matte", opacity = 1f, shine = 0f, glitter = 0f, brighten = 0f },
            new FinishLook { name = "satin", opacity = 0.94f, shine = 0.22f, glitter = 0f, brighten = 0.04f },
            new FinishLook { name = "glossy", opacity = 0.86f, shine = 0.6f, glitter = 0f, brighten = 0.08f },
            new FinishLook { name = "shimmer", opacity = 0.9f, shine = 0.3f, glitter = 0.7f, brighten = 0.1f },
            new FinishLook { name = "metallic", opacity = 1f, shine = 0.5f, glitter = 0.3f, brighten = 0.18f }
        };
        [Tooltip("The highlight on each shape, in this order: lips, left cheek, right cheek, left eye, right eye, face")]
        [SerializeField] private Image[] shines = new Image[0];
        [Tooltip("The sparkle on each shape, in the same order")]
        [SerializeField] private Image[] glitters = new Image[0];
        [Tooltip("How fast the sparkle twinkles")]
        [SerializeField] private float twinkleSpeed = 7f;

        [Header("Animation")]
        [Tooltip("How quickly a new shade, strength or finish blends in")]
        [SerializeField] private float blendSpeed = 10f;

        string area = ShadeAdvisor.Lips;
        Color shade = Color.clear;
        float intensity = 0.6f;
        Color shownLips, shownCheeks, shownEyes, shownFace;
        FinishLook look;
        float opacityNow = 1f, shineNow, glitterNow, brightenNow;

        void Awake() { if (look == null) look = FindFinish("matte"); Paint(true); }

        /// <summary>Which part of the face gets the colour: Lips, Cheeks, Eyes or Face.</summary>
        public void SetArea(string newArea) { area = string.IsNullOrEmpty(newArea) ? ShadeAdvisor.Lips : newArea; }
        public void SetShade(Color colour) { shade = colour; }
        /// <summary>0 = none, 1 = full strength.</summary>
        public void SetIntensity(float value) { intensity = Mathf.Clamp01(value); }
        public void Clear() { shade = Color.clear; }

        // ---- a LOOK of several products (24 Nov 2026, owner): the other products of the look, one per part of the face ----
        // The product being edited (area, shade, finish above) is the main one; every other product of the look is shown on ITS part
        // of the face with its own shade and the solidity of its own finish. A part the main product uses is never taken by an extra.
        readonly Dictionary<string, KeyValuePair<Color, float>> extras = new Dictionary<string, KeyValuePair<Color, float>>();
        public void ClearExtras() { extras.Clear(); }
        public void SetExtra(string part, Color colour, string finishName)
        {
            if (string.IsNullOrEmpty(part) || colour.a <= 0.01f) return;
            extras[part] = new KeyValuePair<Color, float>(colour, FindFinish(finishName).opacity);
        }
        public bool TryExtra(string part, out Color colour, out float opacity)
        {
            if (part != null && extras.TryGetValue(part, out KeyValuePair<Color, float> e)) { colour = e.Key; opacity = e.Value; return true; }
            colour = Color.clear; opacity = 0f; return false;
        }
        /// <summary>matte, satin, glossy, shimmer or metallic (a name from the list "Finishes").</summary>
        public void SetFinish(string finishName) { look = FindFinish(finishName); }

        FinishLook FindFinish(string finishName)
        {
            if (finishes != null)
                foreach (FinishLook f in finishes)
                    if (f != null && string.Equals(f.name, finishName, StringComparison.OrdinalIgnoreCase)) return f;
            return finishes != null && finishes.Length > 0 && finishes[0] != null ? finishes[0] : new FinishLook();
        }

        // ---- the current choices, read by the live face tracking (ArFaceMakeup) so both previews show the same thing ----
        public string Area => area;
        public Color Shade => shade;
        public float Intensity => intensity;
        /// <summary>The finish as it is shown right now (the numbers glide from one finish to the next).</summary>
        public float FinishOpacity => opacityNow;
        public float FinishShine => shineNow;
        public float FinishGlitter => glitterNow;
        public float FinishBrighten => brightenNow;
        public float TwinkleSpeed => twinkleSpeed;
        /// <summary>The material of the shapes (shader GlowUp/UiMakeupBlend): the guided try-on that follows the face draws with the same blend.</summary>
        public Material ShapeMaterial => lips != null ? lips.material : null;
        /// <summary>The name of the finish chosen now (matte, satin, glossy, shimmer, metallic).</summary>
        public string FinishName => look != null ? look.name : "matte";
        /// <summary>The strongest opacity allowed for a part of the face (the Inspector values above).</summary>
        public float MaxOpacityFor(string part)
        {
            float saved = part == ShadeAdvisor.Cheeks ? cheeksMax : part == ShadeAdvisor.Eyes ? eyesMax : part == ShadeAdvisor.Face ? faceMax
                        : part == ShadeAdvisor.Brows ? browsMax : lipsMax;
            // 18 Nov 2026: every cosmetic type at least as solid as MakeupQuality says, whatever the scene has saved
            int layer = MakeupQuality.LayerOf(part);
            return Mathf.Max(saved, MakeupQuality.SolidOpacity(layer < 0 ? MakeupQuality.Lips : layer));
        }

        void Update() { Paint(false); }

        void Paint(bool instant)
        {
            float t = instant ? 1f : 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            if (look != null)
            {
                opacityNow = Mathf.Lerp(opacityNow, look.opacity, t); shineNow = Mathf.Lerp(shineNow, look.shine, t);
                glitterNow = Mathf.Lerp(glitterNow, look.glitter, t); brightenNow = Mathf.Lerp(brightenNow, look.brighten, t);
            }
            bool has = shade.a > 0.01f;
            float lipsTop = MaxOpacityFor(ShadeAdvisor.Lips), cheeksTop = MaxOpacityFor(ShadeAdvisor.Cheeks), eyesTop = MaxOpacityFor(ShadeAdvisor.Eyes), faceTop = MaxOpacityFor(ShadeAdvisor.Face);
            shownLips = Color.Lerp(shownLips, Target(has && area == ShadeAdvisor.Lips, lipsTop), t);
            shownCheeks = Color.Lerp(shownCheeks, Target(has && area == ShadeAdvisor.Cheeks, cheeksTop), t);
            shownEyes = Color.Lerp(shownEyes, Target(has && area == ShadeAdvisor.Eyes, eyesTop), t);
            shownFace = Color.Lerp(shownFace, Target(has && area == ShadeAdvisor.Face, faceTop), t);
            if (lips != null) lips.color = shownLips;
            if (cheekLeft != null) cheekLeft.color = shownCheeks;
            if (cheekRight != null) cheekRight.color = shownCheeks;
            if (eyeLeft != null) eyeLeft.color = shownEyes;
            if (eyeRight != null) eyeRight.color = shownEyes;
            if (faceTint != null) faceTint.color = shownFace;

            // the highlight and the sparkle follow the shape they sit on
            Extra(0, shownLips.a, lipsTop); Extra(1, shownCheeks.a, cheeksTop); Extra(2, shownCheeks.a, cheeksTop);
            Extra(3, shownEyes.a, eyesTop); Extra(4, shownEyes.a, eyesTop); Extra(5, shownFace.a, faceTop);
        }

        Color Target(bool active, float max)
        {
            Color c = Color.Lerp(shade, Color.white, brightenNow);
            return new Color(c.r, c.g, c.b, active ? Mathf.Clamp01(intensity * max * opacityNow) : 0f);
        }

        void Extra(int index, float shapeAlpha, float max)
        {
            float present = max > 0.001f ? Mathf.Clamp01(shapeAlpha / max) : 0f;       // 0 = this part is not shown, 1 = shown at full strength
            if (shines != null && index < shines.Length && shines[index] != null)
                shines[index].color = new Color(1f, 1f, 1f, present * shineNow);
            if (glitters != null && index < glitters.Length && glitters[index] != null)
            {
                float twinkle = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * twinkleSpeed + index * 1.7f);
                glitters[index].color = new Color(1f, 1f, 1f, present * glitterNow * twinkle);
            }
        }
    }
}
