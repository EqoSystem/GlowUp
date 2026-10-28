using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// The colour on the TRACKED face (live face tracking). The phone's face tracking gives a mesh that
    /// follows the user's face; this script draws thin layers on that mesh: one per part of the face
    /// (whole face, cheeks, eyelids, lips, brows) and one more for the highlight of the finish. Each layer is a
    /// material whose picture (a "mask") says where on the face it shows; this script only sets each
    /// layer's colour and strength.
    ///
    /// Since 12 October 2026 the colour layers TINT the skin (shader GlowUp/FaceMakeupBlend): the skin's own texture,
    /// light and shadow stay visible under the colour, so it looks worn and not like a flat coloured shape.
    /// Since 26 October 2026: a BROW layer (its mask is made from the tracked face by ArBrowMask), and in the full pipeline a
    /// GLOSS highlight that is worked out from the face mesh's surface (shader GlowUp/FaceMakeupGloss), so a glossy lip's
    /// highlight slides over the lip as the head turns. The light pipeline keeps the fixed highlight picture.
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
        [Tooltip("The brow layer. Its mask is made from the tracked face (ArBrowMask). Empty = no brow colour.")]
        [SerializeField] private Material browsMaterial;

        [Header("Finish: the highlight layer (drawn on top)")]
        [Tooltip("The fixed highlight of the light pipeline. Empty = finishes only change the colour, without a highlight.")]
        [SerializeField] private Material shineMaterial;
        [Tooltip("Where the fixed highlight sits for each part, in this order: face, cheeks, eyes, lips")]
        [SerializeField] private Texture2D[] shineMasks = new Texture2D[0];
        [Tooltip("The full pipeline's highlight that follows the face's surface (shader GlowUp/FaceMakeupGloss). Empty = the fixed highlight is used everywhere.")]
        [SerializeField] private Material glossMaterial;
        [Tooltip("How strong the highlight is on the tracked face (1 = as the finish says)")]
        [Range(0f, 2f)] [SerializeField] private float shineStrength = 1f;
        [Tooltip("How sharp the moving highlight is for each finish, in this order: matte, satin, glossy, shimmer, metallic (high = a small, sharp spot)")]
        [SerializeField] private float[] glossSharpness = { 8f, 14f, 90f, 24f, 36f };
        [Tooltip("How much a metallic finish's highlight takes the shade's colour (0 = white)")]
        [Range(0f, 1f)] [SerializeField] private float metallicTint = 0.6f;

        [Header("Look")]
        [Tooltip("How quickly a new shade or strength blends in")]
        [SerializeField] private float blendSpeed = 10f;
        [Tooltip("Live tracking sits exactly on the face, so it can be a little stronger or weaker than the guided preview. 1 = the same.")]
        [Range(0.3f, 1.5f)] [SerializeField] private float strengthScale = 1f;
        [Tooltip("The colour bare LIPS are assumed to have. Lip colour is worked out against it: lips of this colour become exactly the shade, and the lines and light of the real lips stay visible.")]
        [SerializeField] private Color lipsUnderneath = new Color(0.72f, 0.45f, 0.45f, 1f);
        [Tooltip("The colour bare SKIN is assumed to have (cheeks, eyelids, whole face). Set it nearer to the user's skin if a shade looks too dark or too light.")]
        [SerializeField] private Color skinUnderneath = new Color(0.78f, 0.60f, 0.52f, 1f);
        [Tooltip("The colour bare BROWS are assumed to have (hair over skin). Brow colour is worked out against it.")]
        [SerializeField] private Color browsUnderneath = new Color(0.42f, 0.32f, 0.27f, 1f);
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

        // this scene's own copies. The colour layers in the order face, cheeks, eyes, lips, brows (the order of Areas)
        const int Brows = 4;
        Material[] colour;
        Material shine, gloss;
        Material[] all;
        bool made;
        readonly Color[] shown = new Color[5];
        int shineOn = -1;
        Texture glossMaskOn;
        int shownLayer = -1;                // the colour layer that is drawn now (0 face, 1 cheeks, 2 eyes, 3 lips, 4 brows), -1 = none
        bool browMaskReady;
        float sharpnessNow = 14f;

        // ---- set by the other AR scripts (all of them optional) ----
        /// <summary>How present the make-up is: 1 = fully, 0 = gone. ArFaceHold lowers it while a lost face fades out.</summary>
        public float Presence { get; set; } = 1f;
        /// <summary>The highlight's strength follows the room's brightness (ArLightAdapter): 1 = as the finish says.</summary>
        public float ShineGain { get; set; } = 1f;
        /// <summary>The skin colour measured in the picture, at the brightness the layers assume (ArLightAdapter).</summary>
        public Color SkinSample { get; set; } = Color.white;
        /// <summary>How far the layers' assumed skin tone moves towards SkinSample (0 to 1).</summary>
        public float SkinAdapt { get; set; }
        /// <summary>True = the full pipeline's moving highlight (gloss) is drawn instead of the fixed one (set by ArFaceTrackingController).</summary>
        public bool UseGloss { get; set; }
        /// <summary>The brightness (0 to 1) of the skin tone the layers assume.</summary>
        public float AssumedSkinBrightness => 0.299f * skinUnderneath.r + 0.587f * skinUnderneath.g + 0.114f * skinUnderneath.b;
        /// <summary>Changes whenever another part of the face is shown (ArFaceTrackingController redraws the face's materials then).</summary>
        public int ShownLayer => shownLayer;
        /// <summary>The user's choices (read by the debug overlay, which can also set a test shade).</summary>
        public FallbackMakeupOverlay Choices => choices;
        /// <summary>True when the moving highlight can be drawn (its material is set).</summary>
        public bool HasGloss => glossMaterial != null;
        static readonly string[] Areas = { ShadeAdvisor.Face, ShadeAdvisor.Cheeks, ShadeAdvisor.Eyes, ShadeAdvisor.Lips, ShadeAdvisor.Brows };
        static readonly string[] FinishOrder = { "matte", "satin", "glossy", "shimmer", "metallic" };

        void MakeLayers()
        {
            if (made) return;
            made = true;
            Material[] originals = { faceMaterial, cheeksMaterial, eyesMaterial, lipsMaterial, browsMaterial };
            colour = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;
                Material m = new Material(originals[i]) { name = originals[i].name + " (this scene)" };
                shown[i] = new Color(1f, 1f, 1f, 0f);
                m.SetColor(colourProperty, new Color(1f, 1f, 1f, 0f));          // nothing shows until a shade is chosen
                if (m.HasProperty("_SkinBase")) m.SetColor("_SkinBase", i == 3 ? lipsUnderneath : i == Brows ? browsUnderneath : skinUnderneath);
                if (i == Brows && m.HasProperty(maskProperty)) m.SetTexture(maskProperty, Texture2D.blackTexture);   // no brows until ArBrowMask made the mask
                Flip(m, maskProperty);
                colour[i] = m;
            }
            if (shineMaterial != null)
            {
                shine = new Material(shineMaterial) { name = shineMaterial.name + " (this scene)" };
                shine.SetColor(colourProperty, new Color(1f, 1f, 1f, 0f));
                Flip(shine, maskProperty);
            }
            if (glossMaterial != null)
            {
                gloss = new Material(glossMaterial) { name = glossMaterial.name + " (this scene)" };
                gloss.SetColor(colourProperty, new Color(1f, 1f, 1f, 0f));
                Flip(gloss, maskProperty);
            }
            var list = new System.Collections.Generic.List<Material>();
            foreach (Material m in colour) if (m != null) list.Add(m);
            if (shine != null) list.Add(shine);
            if (gloss != null) list.Add(gloss);
            all = list.ToArray();
        }

        void Flip(Material m, string property)
        {
            if (!flipMasksUpsideDown || m == null || !m.HasProperty(property)) return;
            m.SetTextureScale(property, new Vector2(1f, -1f));
            m.SetTextureOffset(property, new Vector2(0f, 1f));
        }

        /// <summary>Every material of this scene (created on first use).</summary>
        public Material[] Layers { get { MakeLayers(); return all; } }

        /// <summary>
        /// The materials to put on the tracked face's renderer. With <paramref name="onlyShown"/> (the light pipeline) only the colour
        /// layer of the part being tried and the highlight: fewer passes for the phone's graphics chip. The highlight is the moving
        /// gloss when <see cref="UseGloss"/> is on and its material exists, otherwise the fixed one.
        /// </summary>
        public Material[] LayersFor(bool onlyShown)
        {
            MakeLayers();
            var some = new System.Collections.Generic.List<Material>(7);
            if (onlyShown && shownLayer >= 0 && shownLayer < colour.Length) { if (colour[shownLayer] != null) some.Add(colour[shownLayer]); }
            else foreach (Material m in colour) if (m != null) some.Add(m);
            Material highlight = UseGloss && gloss != null ? gloss : shine;
            if (highlight != null) some.Add(highlight);
            return some.ToArray();
        }

        /// <summary>The inside of an open mouth is cut out of the colour layers and the gloss (ArMouthMask). The same flip as the masks is used.</summary>
        public void SetMouth(Texture mask, float open)
        {
            if (!made) return;
            for (int i = 0; i < colour.Length; i++) Mouth(colour[i], mask, open);
            Mouth(gloss, mask, open);
        }

        void Mouth(Material m, Texture mask, float open)
        {
            if (m == null) return;
            if (m.HasProperty(mouthMaskProperty))
            {
                if (m.GetTexture(mouthMaskProperty) != mask) m.SetTexture(mouthMaskProperty, mask);
                if (flipMasksUpsideDown) { m.SetTextureScale(mouthMaskProperty, new Vector2(1f, -1f)); m.SetTextureOffset(mouthMaskProperty, new Vector2(0f, 1f)); }
            }
            if (m.HasProperty(mouthOpenProperty)) m.SetFloat(mouthOpenProperty, mask != null ? Mathf.Clamp01(open) : 0f);
        }

        /// <summary>The brow mask made from the tracked face (ArBrowMask); null = no brow colour.</summary>
        public void SetBrowMask(Texture mask)
        {
            MakeLayers();
            Material m = colour[Brows];
            browMaskReady = mask != null;
            if (m != null && m.HasProperty(maskProperty)) m.SetTexture(maskProperty, mask != null ? mask : Texture2D.blackTexture);
            if (glossMaskOn != null && shownLayer == Brows) glossMaskOn = null;      // the gloss takes the new mask on the next frame
        }

        /// <summary>The colour layer `i` shows now (0 face, 1 cheeks, 2 eyes, 3 lips, 4 brows; alpha = strength). The guided try-on that follows the face copies it.</summary>
        public Color LayerColour(int i) => made && i >= 0 && i < shown.Length ? shown[i] : new Color(1f, 1f, 1f, 0f);

        /// <summary>The skin colour layer `i` assumes under it: the lips and brows have their own, the others follow the measured skin.</summary>
        public Color SkinUnderFor(int i) => i == 3 ? lipsUnderneath : i == Brows ? browsUnderneath
            : SkinAdapt > 0.001f ? Color.Lerp(skinUnderneath, SkinSample, Mathf.Clamp01(SkinAdapt)) : skinUnderneath;

        /// <summary>True when every colour layer has its material (the brow layer is optional).</summary>
        public bool Ready => faceMaterial != null && cheeksMaterial != null && eyesMaterial != null && lipsMaterial != null;

        void Update()
        {
            if (!made || choices == null) return;
            float t = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            Color shade = Color.Lerp(choices.Shade, Color.white, choices.FinishBrighten);
            bool has = choices.Shade.a > 0.01f;
            int active = -1; float activeShare = 0f;
            for (int i = 0; i < Areas.Length; i++)
            {
                Material layer = colour[i];
                if (layer == null) continue;
                bool on = has && choices.Area == Areas[i] && (i != Brows || browMaskReady);
                float full = Mathf.Clamp01(choices.Intensity * choices.MaxOpacityFor(Areas[i]) * strengthScale);
                float alpha = on ? Mathf.Clamp01(full * choices.FinishOpacity) * Mathf.Clamp01(Presence) : 0f;
                // a layer that is fading out keeps its own colour while it fades
                var target = on ? new Color(shade.r, shade.g, shade.b, alpha) : new Color(shown[i].r, shown[i].g, shown[i].b, 0f);
                shown[i] = Color.Lerp(shown[i], target, t);
                layer.SetColor(colourProperty, shown[i]);
                // the skin tone moves towards the measured one (not for the lips and brows: they have their own)
                if (i < 3 && layer.HasProperty("_SkinBase")) layer.SetColor("_SkinBase", SkinAdapt > 0.001f ? Color.Lerp(skinUnderneath, SkinSample, Mathf.Clamp01(SkinAdapt)) : skinUnderneath);
                if (on) { active = i; activeShare = full > 0.001f ? Mathf.Clamp01(shown[i].a / full) : 0f; }
            }

            shownLayer = has ? active : -1;
            float twinkle = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * choices.TwinkleSpeed);
            float presence = Mathf.Clamp01(Presence);

            // the fixed highlight (light pipeline): on the part that is shown, as strong as the finish says; a sparkling finish makes it twinkle
            if (shine != null)
            {
                bool fixedOn = !(UseGloss && gloss != null);
                if (active >= 0 && active < 4 && active != shineOn && shineMasks != null && active < shineMasks.Length && shineMasks[active] != null)
                {
                    shine.SetTexture(maskProperty, shineMasks[active]);
                    shineOn = active;
                }
                float strength = fixedOn && active >= 0 && shineOn == active
                    ? activeShare * choices.Intensity * (choices.FinishShine + choices.FinishGlitter * 0.6f * twinkle) * shineStrength * ShineGain * presence : 0f;
                shine.SetColor(colourProperty, new Color(1f, 1f, 1f, Mathf.Clamp01(strength)));
            }

            // the moving highlight (full pipeline): on the WHOLE part being tried (its own mask), shaped by the face's surface
            if (gloss != null)
            {
                bool glossOn = UseGloss && active >= 0 && active != Brows;
                if (glossOn && colour[active] != null)
                {
                    Texture m = colour[active].GetTexture(maskProperty);
                    if (m != glossMaskOn) { gloss.SetTexture(maskProperty, m); glossMaskOn = m; }
                }
                sharpnessNow = Mathf.Lerp(sharpnessNow, SharpnessFor(choices.FinishName), t);
                if (gloss.HasProperty("_Sharpness")) gloss.SetFloat("_Sharpness", sharpnessNow);
                if (gloss.HasProperty("_Sparkle")) gloss.SetFloat("_Sparkle", choices.FinishGlitter);
                float strength = glossOn ? activeShare * choices.Intensity * choices.FinishShine * shineStrength * ShineGain * presence : 0f;
                Color tint = Color.Lerp(Color.white, choices.Shade, choices.FinishName == "metallic" ? metallicTint : 0.1f);
                // a sparkling finish without much shine still needs strength to show its sparkles
                strength = Mathf.Max(strength, glossOn ? activeShare * choices.Intensity * choices.FinishGlitter * 0.8f * ShineGain * presence : 0f);
                gloss.SetColor(colourProperty, new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(strength)));
            }
        }

        float SharpnessFor(string finish)
        {
            int at = System.Array.IndexOf(FinishOrder, finish ?? "");
            if (glossSharpness == null || glossSharpness.Length == 0) return 24f;
            return Mathf.Max(1f, glossSharpness[Mathf.Clamp(at < 0 ? 1 : at, 0, glossSharpness.Length - 1)]);
        }

        void OnDestroy()
        {
            if (!made) return;
            foreach (Material m in all) if (m != null) Destroy(m);
            all = null; colour = null; shine = gloss = null; made = false;
        }
    }
}
