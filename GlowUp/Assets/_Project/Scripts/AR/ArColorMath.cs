using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// The colour math of the AR make-up layers, written once in C# so it can be tested. KEEP IT EQUAL to the shaders:
    ///   Tint  = Shaders/FaceMakeupBlend.shader  (picture x 2 x lerp(0.5, clamp(0.5 x shade / skin), amount))
    ///   Gloss = Shaders/FaceMakeupGloss.shader  (Blinn-Phong highlight on the mesh normal)
    /// The project draws in Linear colour space, so these numbers are linear-light values.
    /// </summary>
    public static class ArColorMath
    {
        /// <summary>The most a tint may lighten the skin; keep equal to TintFor in the two blend shaders.</summary>
        public const float MaxGain = 1.3f;

        /// <summary>
        /// What one camera pixel becomes under a tint layer: skin of exactly the assumed colour becomes exactly the shade,
        /// lighter or darker skin a lighter or darker version of it (its own light and texture stay). The change of light is
        /// capped (MaxGain) so a light shade cannot wash the face out; the blend itself can at most double a pixel.
        /// </summary>
        public static Color Tint(Color pixel, Color shade, Color skin, float amount)
        {
            float a = Mathf.Clamp01(amount);
            float rr = shade.r / Mathf.Max(skin.r, 0.08f), rg = shade.g / Mathf.Max(skin.g, 0.08f), rb = shade.b / Mathf.Max(skin.b, 0.08f);
            float lum = Mathf.Max(0.2126f * rr + 0.7152f * rg + 0.0722f * rb, 0.0001f);
            float k = Mathf.Min(lum, MaxGain) / lum;
            return new Color(Out(pixel.r, rr * k, a), Out(pixel.g, rg * k, a), Out(pixel.b, rb * k, a), pixel.a);
        }

        static float Out(float pixel, float ratio, float amount)
        {
            float tint = Mathf.Clamp01(0.5f * ratio);
            return Mathf.Clamp01(pixel * 2f * Mathf.Lerp(0.5f, tint, amount));
        }

        /// <summary>Relative luminance (linear Rec. 709 weights).</summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The highlight of the gloss shader: (N.H)^sharpness with a light just above the camera (all vectors normalised).</summary>
        public static float Gloss(Vector3 normal, Vector3 toCamera, Vector3 lightOffset, float sharpness)
        {
            Vector3 n = normal.sqrMagnitude > 0.01f ? normal.normalized : toCamera.normalized;
            Vector3 v = toCamera.normalized;
            if (Vector3.Dot(n, v) < 0f) n = -n;
            Vector3 l = (v + lightOffset).normalized;
            Vector3 h = (l + v).normalized;
            return Mathf.Pow(Mathf.Clamp01(Vector3.Dot(n, h)), Mathf.Max(1f, sharpness));
        }
    }
}
