using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// The colour on the TRACKED face (live face tracking). The phone's face tracking gives a mesh that
    /// follows the user's face; this script draws thin layers on that mesh: one per part of the face
    /// (whole face, cheeks, eyelids, lips) and one more for the highlight of the finish. Each layer is a
    /// material whose picture (a "mask") says where on the face it shows; this script only sets each
    /// layer's colour and strength.
    ///
    /// Since 12 October 2026 the colour layers TINT the skin (shader GlowUp/FaceMakeupBlend): the skin's own texture,
    /// light and shadow stay visible under the colour, so it looks worn and not like a flat coloured shape.
    ///
    /// What to show (which part, which shade, how strong, which finish) is read from the
    /// FallbackMakeupOverlay, so the tabs, swatches and strength slider work the same in the guided
    /// preview and in live tracking. The numbers of each finish (matte, satin, glossy, shimmer,
    /// metallic) are set there too.
    /// </summary>
    public class ArFaceMakeup : MonoBehaviour
    {
        [Tooltip("The user's choices (part of the face, shade, strength, finish) are read from here")]
        [SerializeField] private FallbackMakeupOverlay choices;

        [Header("Layers (drawn in this order, the lips last)")]
        [SerializeField] private Material faceMaterial;
        [SerializeField] private Material cheeksMaterial;
        [SerializeField] private Material eyesMaterial;
        [SerializeField] private Material lipsMaterial;

        [Header("Finish: the highlight layer (drawn on top)")]
        [Tooltip("The material of the highlight. Empty = finishes only change the colour, without a highlight.")]
        [SerializeField] private Material shineMaterial;
        [Tooltip("Where the highlight sits for each part, in this order: face, cheeks, eyes, lips")]
        [SerializeField] private Texture2D[] shineMasks = new Texture2D[0];
        [Tooltip("How strong the highlight is on the tracked face (1 = as the finish says)")]
        [Range(0f, 2f)] [SerializeField] private float shineStrength = 1f;

        [Header("Look")]
        [Tooltip("How quickly a new shade or strength blends in")]
        [SerializeField] private float blendSpeed = 10f;
        [Tooltip("Live tracking sits exactly on the face, so it can be a little stronger or weaker than the guided preview. 1 = the same.")]
        [Range(0.3f, 1.5f)] [SerializeField] private float strengthScale = 1f;
        [Tooltip("The colour bare LIPS are assumed to have. Lip colour is worked out against it: lips of this colour become exactly the shade, and the lines and light of the real lips stay visible.")]
        [SerializeField] private Color lipsUnderneath = new Color(0.72f, 0.45f, 0.45f, 1f);
        [Tooltip("The colour bare SKIN is assumed to have (cheeks, eyelids, whole face). Set it nearer to the user's skin if a shade looks too dark or too light.")]
        [SerializeField] private Color skinUnderneath = new Color(0.78f, 0.60f, 0.52f, 1f);
        [Tooltip("Tick this only if the colour shows upside down on the face (for example lip colour on the forehead)")]
        [SerializeField] private bool flipMasksUpsideDown = false;
        [Tooltip("The colour setting of the layer materials (do not change unless the materials use another shader)")]
        [SerializeField] private string colourProperty = "_BaseColor";
        [Tooltip("The mask picture setting of the layer materials (do not change unless the materials use another shader)")]
        [SerializeField] private string maskProperty = "_BaseMap";

        [Header("Mouth mask (set by ArMouthMask)")]
        [Tooltip("The mask picture setting of the layer materials that hides the colour inside an open mouth")]
        [SerializeField] private string mouthMaskProperty = "_MouthMask";
        [Tooltip("How open the mouth is (0 to 1) on the layer materials")]
        [SerializeField] private string mouthOpenProperty = "_MouthOpen";

        Material[] layers;                  // this scene's own copies, in the order face, cheeks, eyes, lips, highlight
        readonly Color[] shown = new Color[4];
        int shineOn = -1;
        int shownLayer = -1;                // the layer that is drawn now (0 face, 1 cheeks, 2 eyes, 3 lips), -1 = none

        // ---- set by the other AR scripts (all of them optional) ----
        /// <summary>How present the make-up is: 1 = fully, 0 = gone. ArFaceHold lowers it while a lost face fades out.</summary>
        public float Presence { get; set; } = 1f;
        /// <summary>The highlight's strength follows the room's brightness (ArLightAdapter): 1 = as the finish says.</summary>
        public float ShineGain { get; set; } = 1f;
        /// <summary>The skin colour measured in the picture, at the brightness the layers assume (ArLightAdapter).</summary>
        public Color SkinSample { get; set; } = Color.white;
        /// <summary>How far the layers' assumed skin tone moves towards SkinSample (0 to 1).</summary>
        public float SkinAdapt { get; set; }
        /// <summary>The brightness (0 to 1) of the skin tone the layers assume.</summary>
        public float AssumedSkinBrightness => 0.299f * skinUnderneath.r + 0.587f * skinUnderneath.g + 0.114f * skinUnderneath.b;
        /// <summary>Changes whenever another part of the face is shown (ArFaceTrackingController redraws the face's materials then).</summary>
        public int ShownLayer => shownLayer;
        static readonly string[] Areas = { ShadeAdvisor.Face, ShadeAdvisor.Cheeks, ShadeAdvisor.Eyes, ShadeAdvisor.Lips };

        /// <summary>The materials to put on the tracked face's renderer (created on first use).</summary>
        public Material[] Layers
        {
            get
            {
                if (layers != null) return layers;
                Material[] originals = { faceMaterial, cheeksMaterial, eyesMaterial, lipsMaterial, shineMaterial };
                layers = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    if (originals[i] == null) continue;
                    layers[i] = new Material(originals[i]) { name = originals[i].name + " (this scene)" };
                    if (i < shown.Length) shown[i] = new Color(1f, 1f, 1f, 0f);
                    layers[i].SetColor(colourProperty, new Color(1f, 1f, 1f, 0f));          // nothing shows until a shade is chosen
                    if (layers[i].HasProperty("_SkinBase")) layers[i].SetColor("_SkinBase", i == 3 ? lipsUnderneath : skinUnderneath);
                    if (flipMasksUpsideDown && layers[i].HasProperty(maskProperty))
                    {
                        layers[i].SetTextureScale(maskProperty, new Vector2(1f, -1f));
                        layers[i].SetTextureOffset(maskProperty, new Vector2(0f, 1f));
                    }
                }
                return layers;
            }
        }

        /// <summary>
        /// The materials to draw. With <paramref name="onlyShown"/> (the light pipeline) only the colour layer of the part being
        /// tried and the highlight: three passes fewer for the phone's graphics chip.
        /// </summary>
        public Material[] LayersFor(bool onlyShown)
        {
            Material[] all = Layers;
            if (!onlyShown || shownLayer < 0 || shownLayer >= 4) return all;
            var some = new System.Collections.Generic.List<Material>();
            if (all[shownLayer] != null) some.Add(all[shownLayer]);
            if (all.Length > 4 && all[4] != null) some.Add(all[4]);
            return some.ToArray();
        }

        /// <summary>The inside of an open mouth is cut out of the colour layers (ArMouthMask). The same flip as the masks is used.</summary>
        public void SetMouth(Texture mask, float open)
        {
            if (layers == null) return;
            for (int i = 0; i < 4; i++)
            {
                Material m = layers[i];
                if (m == null) continue;
                if (m.HasProperty(mouthMaskProperty))
                {
                    if (m.GetTexture(mouthMaskProperty) != mask) m.SetTexture(mouthMaskProperty, mask);
                    if (flipMasksUpsideDown) { m.SetTextureScale(mouthMaskProperty, new Vector2(1f, -1f)); m.SetTextureOffset(mouthMaskProperty, new Vector2(0f, 1f)); }
                }
                if (m.HasProperty(mouthOpenProperty)) m.SetFloat(mouthOpenProperty, mask != null ? Mathf.Clamp01(open) : 0f);
            }
        }

        /// <summary>True when every colour layer has its material.</summary>
        public bool Ready => faceMaterial != null && cheeksMaterial != null && eyesMaterial != null && lipsMaterial != null;

        void Update()
        {
            if (layers == null || choices == null) return;
            float t = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            Color shade = Color.Lerp(choices.Shade, Color.white, choices.FinishBrighten);
            bool has = choices.Shade.a > 0.01f;
            int active = -1; float activeShare = 0f;
            for (int i = 0; i < Areas.Length; i++)
            {
                if (layers[i] == null) continue;
                bool on = has && choices.Area == Areas[i];
                float full = Mathf.Clamp01(choices.Intensity * choices.MaxOpacityFor(Areas[i]) * strengthScale);
                float alpha = on ? Mathf.Clamp01(full * choices.FinishOpacity) * Mathf.Clamp01(Presence) : 0f;
                // a layer that is fading out keeps its own colour while it fades
                var target = on ? new Color(shade.r, shade.g, shade.b, alpha) : new Color(shown[i].r, shown[i].g, shown[i].b, 0f);
                shown[i] = Color.Lerp(shown[i], target, t);
                layers[i].SetColor(colourProperty, shown[i]);
                // the skin tone moves towards the measured one (not for the lips: they have their own)
                if (i < 3 && SkinAdapt > 0.001f && layers[i].HasProperty("_SkinBase")) layers[i].SetColor("_SkinBase", Color.Lerp(skinUnderneath, SkinSample, Mathf.Clamp01(SkinAdapt)));
                else if (i < 3 && layers[i].HasProperty("_SkinBase")) layers[i].SetColor("_SkinBase", skinUnderneath);
                if (on) { active = i; activeShare = full > 0.001f ? Mathf.Clamp01(shown[i].a / full) : 0f; }
            }

            shownLayer = has ? active : -1;

            // the highlight: on the part that is shown, as strong as the finish says; a sparkling finish makes it twinkle
            Material shine = layers.Length > 4 ? layers[4] : null;
            if (shine == null) return;
            if (active >= 0 && active != shineOn && shineMasks != null && active < shineMasks.Length && shineMasks[active] != null)
            {
                shine.SetTexture(maskProperty, shineMasks[active]);
                shineOn = active;
            }
            float twinkle = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * choices.TwinkleSpeed);
            float strength = active >= 0 && shineOn == active
                ? activeShare * choices.Intensity * (choices.FinishShine + choices.FinishGlitter * 0.6f * twinkle) * shineStrength * ShineGain * Mathf.Clamp01(Presence) : 0f;
            shine.SetColor(colourProperty, new Color(1f, 1f, 1f, Mathf.Clamp01(strength)));
        }

        void OnDestroy()
        {
            if (layers == null) return;
            foreach (Material m in layers) if (m != null) Destroy(m);
            layers = null;
        }
    }
}
