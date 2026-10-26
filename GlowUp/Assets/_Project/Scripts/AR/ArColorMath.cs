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
        /// <summary>
        /// What one camera pixel becomes under a tint layer: skin of exactly the assumed colour becomes exactly the shade,
        /// lighter or darker skin a lighter or darker version of it (its own light and texture stay). The blend can at most
        /// double a pixel's brightness, the limit of "Blend DstColor SrcColor".
        /// </summary>
        public static Color Tint(Color pixel, Color shade, Color skin, float amount)
        {
            float a = Mathf.Clamp01(amount);
            return new Color(Channel(pixel.r, shade.r, skin.r, a), Channel(pixel.g, shade.g, skin.g, a), Channel(pixel.b, shade.b, skin.b, a), pixel.a);
        }

        static float Channel(float pixel, float shade, float skin, float amount)
        {
            float tint = Mathf.Clamp01(0.5f * shade / Mathf.Max(skin, 0.08f));
            float output = Mathf.Lerp(0.5f, tint, amount);
            return Mathf.Clamp01(pixel * 2f * output);
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
