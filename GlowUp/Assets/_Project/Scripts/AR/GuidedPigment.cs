using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// How much of a shade ITSELF lies over the tint in the guided try-on (phase 3A, 11 November 2026). Pure arithmetic, tested in the Editor.
    ///
    /// The tint (shader GlowUp/UiMakeupBlend) multiplies each skin pixel by shade / skin, at most by 2: the skin's light and texture stay, which
    /// is what makes make-up look worn. But a shade more than about twice as light as the skin in any colour channel cannot be reached that way:
    /// a nude lipstick on deep lips, or a light foundation, stayed too dark. A thin see-through layer of the shade on top carries the rest.
    /// It is kept thin so the skin still shows through, and grows only for shades the tint cannot reach.
    /// </summary>
    public static class GuidedPigment
    {
        /// <summary>The highest factor the tint can reach (the shader's blend doubles at most).</summary>
        public const float TintReach = 2f;

        /// <summary>The share (0 to 1) of the layer's strength the pigment gets: `baseShare`, plus up to `extraForLighter` for a shade out of the tint's reach.</summary>
        public static float Share(Color shade, Color skin, float baseShare, float extraForLighter)
        {
            float need = Mathf.Clamp01((NeededFactor(shade, skin) - TintReach * 0.8f) / (TintReach * 0.7f));
            return Mathf.Clamp01(baseShare + extraForLighter * need);
        }

        /// <summary>The largest factor any colour channel of the skin must be multiplied by to become the shade (1 = no lightening needed).</summary>
        public static float NeededFactor(Color shade, Color skin)
        {
            float r = shade.r / Mathf.Max(skin.r, 0.04f), g = shade.g / Mathf.Max(skin.g, 0.04f), b = shade.b / Mathf.Max(skin.b, 0.04f);
            return Mathf.Max(1f, Mathf.Max(r, Mathf.Max(g, b)));
        }

        /// <summary>
        /// What the pixel becomes (for checks): the tint as the shader does it, then the pigment over it. `strength` = the layer's alpha.
        /// </summary>
        public static Color Result(Color skinPixel, Color shade, Color skinBase, float strength, float pigmentShare)
        {
            Color tinted = skinPixel;
            for (int i = 0; i < 3; i++)
            {
                float factor = Mathf.Clamp01(0.5f * shade[i] / Mathf.Max(skinBase[i], 0.08f)) * 2f;    // the shader: 2 x saturate(0.5 x shade / skin)
                tinted[i] = Mathf.Clamp01(skinPixel[i] * Mathf.Lerp(1f, factor, strength));
            }
            float cover = Mathf.Clamp01(strength * pigmentShare);
            return Color.Lerp(tinted, new Color(shade.r, shade.g, shade.b, 1f), cover);
        }
    }
}
