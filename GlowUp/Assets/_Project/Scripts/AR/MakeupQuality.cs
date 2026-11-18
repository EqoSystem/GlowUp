using GlowUp.Performance;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// ONE PLACE for how solid each kind of make-up looks in AR Try-On (18 November 2026, the owner's target: retail try-on apps,
    /// solid colour with clean edges, for every cosmetic type). Code only: a scene may hold older, weaker Inspector values, and the
    /// stronger of the two is used.
    ///
    /// Layers in the order of ArFaceMakeup: 0 face (foundation, concealer, powder), 1 cheeks (blush, bronzer, contour, highlighter),
    /// 2 eyes (eyeshadow, eyeliner), 3 lips, 4 brows.
    ///
    /// The colour itself is the SAME on both pipelines of Settings > AR Try-On quality, so a budget phone still shows a real
    /// make-up look. What differs is the cost:
    ///  - High (full):  the thin layer of the shade itself (pigment) on every part that is shown, moving highlights, the rest of the
    ///                  full pipeline (dense tracking, light adaptation, mouth mask).
    ///  - Lighter:      the pigment only on the part being tried on (one extra draw), fixed highlights, smaller picture, 30 fps.
    /// </summary>
    public static class MakeupQuality
    {
        public const int Face = 0, Cheeks = 1, Eyes = 2, Lips = 3, Brows = 4;

        // opacity at full strength: face and cheeks blend, eyes / lips / brows read as solid product
        static readonly float[] Solid = { 0.40f, 0.62f, 0.75f, 0.90f, 0.80f };
        // how much of the shade itself lies over the tint (the tint can only double the skin's colour)
        static readonly float[] PigmentShare = { 0.06f, 0.28f, 0.36f, 0.42f, 0.38f };

        /// <summary>The least opacity layer `layer` reaches at full strength.</summary>
        public static float SolidOpacity(int layer) => layer >= 0 && layer < Solid.Length ? Solid[layer] : 0f;

        /// <summary>Foundation, concealer and powder keep the skin's own light and texture: a pale shade must never be painted over darker skin as a flat
        /// cream (a chalky mask, seen on the owner's phone 18 Nov 2026), so the face gets no extra pigment for a lighter shade.</summary>
        public static float ExtraForLighter(int layer, float configured) => layer == Face ? 0f : configured;

        /// <summary>The least pigment share of layer `layer`.</summary>
        public static float Pigment(int layer) => layer >= 0 && layer < PigmentShare.Length ? PigmentShare[layer] : 0f;

        /// <summary>The layer index of a face part name (ShadeAdvisor.Face, Cheeks, Eyes, Lips, Brows); -1 = unknown.</summary>
        public static int LayerOf(string part)
        {
            if (part == GlowUp.Scan.ShadeAdvisor.Face) return Face;
            if (part == GlowUp.Scan.ShadeAdvisor.Cheeks) return Cheeks;
            if (part == GlowUp.Scan.ShadeAdvisor.Eyes) return Eyes;
            if (part == GlowUp.Scan.ShadeAdvisor.Lips) return Lips;
            if (part == GlowUp.Scan.ShadeAdvisor.Brows) return Brows;
            return -1;
        }

        /// <summary>True when layer `layer` gets its pigment on this pipeline (`shownLayer` = the part being tried on).</summary>
        public static bool PigmentOn(int layer, int shownLayer, ArTier tier) => tier == ArTier.Full || layer == shownLayer;

        /// <summary>One line for the Settings page: what the make-up looks like on this pipeline.</summary>
        public static string Describe(ArTier tier) => tier == ArTier.Full
            ? "Make-up: solid colour with clean edges on lips, eyes and brows, soft blending on face and cheeks, moving shine."
            : "Make-up: the same solid colour and clean edges, with a fixed shine to save power.";
    }
}
