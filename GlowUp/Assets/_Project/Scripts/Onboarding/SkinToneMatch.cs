using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Picks the skin tone tile that is nearest to a colour sampled from a selfie (panel revision, 22 Nov 2026). Distance is measured in
    /// CIE Lab (how different two colours LOOK), not in RGB. The result is a suggestion: lighting changes the colour a camera sees,
    /// so the user always keeps the last word. Pure maths, tested in Editor/Tests/SkinToneSelfieTests.cs.
    /// </summary>
    public static class SkinToneMatch
    {
        /// <summary>The tile colours of Step 3, in the order Fair, Light, Medium, Tan, Deep, Dark (the same hex values the scene's tiles use).</summary>
        public static readonly string[] Names = { "Fair", "Light", "Medium", "Tan", "Deep", "Dark" };
        public static readonly Color32[] Palette =
        {
            new Color32(0xF7, 0xF4, 0xE2, 255), new Color32(0xF2, 0xDE, 0xC9, 255), new Color32(0xD4, 0xA8, 0x6F, 255),
            new Color32(0xB6, 0x97, 0x72, 255), new Color32(0x9D, 0x85, 0x5D, 255), new Color32(0x5F, 0x45, 0x3B, 255)
        };

        /// <summary>The index of the nearest palette colour (-1 when the palette is empty).</summary>
        public static int Nearest(Color32 sample, Color32[] palette = null)
        {
            palette = palette ?? Palette;
            int best = -1; float bestDistance = float.MaxValue;
            Vector3 s = Lab(sample);
            for (int i = 0; i < palette.Length; i++)
            {
                float d = (Lab(palette[i]) - s).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        public static string NameOf(Color32 sample) { int i = Nearest(sample); return i < 0 ? "" : Names[i]; }

        /// <summary>The middle value of each channel over the given pixels (a median ignores a bright glare or a dark shadow better than an average).</summary>
        public static Color32 Median(System.Collections.Generic.IList<Color32> pixels)
        {
            if (pixels == null || pixels.Count == 0) return new Color32(0, 0, 0, 255);
            var r = new byte[pixels.Count]; var g = new byte[pixels.Count]; var b = new byte[pixels.Count];
            for (int i = 0; i < pixels.Count; i++) { r[i] = pixels[i].r; g[i] = pixels[i].g; b[i] = pixels[i].b; }
            System.Array.Sort(r); System.Array.Sort(g); System.Array.Sort(b);
            int m = pixels.Count / 2;
            return new Color32(r[m], g[m], b[m], 255);
        }

        /// <summary>sRGB to CIE Lab (D65).</summary>
        public static Vector3 Lab(Color32 c)
        {
            float Lin(byte v) { float x = v / 255f; return x <= 0.04045f ? x / 12.92f : Mathf.Pow((x + 0.055f) / 1.055f, 2.4f); }
            float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            float x2 = (0.4124564f * r + 0.3575761f * g + 0.1804375f * b) / 0.95047f;
            float y2 = 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
            float z2 = (0.0193339f * r + 0.1191920f * g + 0.9503041f * b) / 1.08883f;
            float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
            float fx = F(x2), fy = F(y2), fz = F(z2);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }
    }

    /// <summary>The selfie kept for the skin tone step: one small picture in the app's own folder on this phone (never sent anywhere), deleted on request.</summary>
    public static class SelfieStore
    {
        public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, "skin_tone_selfie.jpg");
        public static bool Exists { get { try { return System.IO.File.Exists(FilePath); } catch (System.Exception) { return false; } } }
        public static bool Save(byte[] jpeg) { try { System.IO.File.WriteAllBytes(FilePath, jpeg); return true; } catch (System.Exception) { return false; } }
        public static void Delete() { try { if (System.IO.File.Exists(FilePath)) System.IO.File.Delete(FilePath); } catch (System.Exception) { } }
    }
}
