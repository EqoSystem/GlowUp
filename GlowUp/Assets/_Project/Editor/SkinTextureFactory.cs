using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Generates PLACEHOLDER close-up skin textures (pores, tone variation, shine/flakes) for the
    /// four skin-type tiles. They are not photographs: drop real face-skin photos into
    /// Assets/_Project/Sprites/SkinTextures (same file names) or assign your own sprite on the tile's
    /// FaceTexture Image in the Inspector.
    /// </summary>
    public static class SkinTextureFactory
    {
        const string Folder = "Assets/_Project/Sprites/SkinTextures/";

        public static Sprite Oily => Make("skin_oily", new Color32(214, 160, 128, 255), shine: 0.55f, flakes: 0f, redness: 0.04f, seed: 11);
        public static Sprite Normal => Make("skin_normal", new Color32(224, 176, 146, 255), 0.08f, 0f, 0.02f, 23);
        public static Sprite Dry => Make("skin_dry", new Color32(212, 168, 144, 255), 0f, 0.5f, 0.05f, 37);
        public static Sprite Mix => Make("skin_mix", new Color32(220, 168, 136, 255), 0.30f, 0.2f, 0.03f, 51);

        static Sprite Make(string name, Color baseColor, float shine, float flakes, float redness, int seed)
        {
            string path = Folder + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(Folder);
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var rnd = new System.Random(seed);
            float ox = seed * 3.1f, oy = seed * 1.7f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float tone = Mathf.PerlinNoise(ox + u * 3f, oy + v * 3f) - 0.5f;                  // soft blotches
                    float fine = Mathf.PerlinNoise(ox + u * 60f, oy + v * 60f) - 0.5f;                // skin grain
                    float pore = Mathf.PerlinNoise(ox + u * 140f, oy + v * 140f);
                    Color c = baseColor * (1f + tone * 0.22f + fine * 0.10f);
                    if (pore > 0.80f) c *= 0.88f;                                                       // pores
                    c.r += redness * Mathf.Max(0, tone) * 2f;
                    // shine: bright oily patches, strongest across the upper-middle (T-zone)
                    float tz = Mathf.Exp(-Mathf.Pow((u - 0.5f) * 3.2f, 2)) * Mathf.Exp(-Mathf.Pow((v - 0.65f) * 2.2f, 2));
                    float sh = shine * tz * Mathf.Clamp01(Mathf.PerlinNoise(ox + u * 8f, oy + v * 8f) * 1.6f - 0.2f);
                    c = Color.Lerp(c, new Color(1f, 0.93f, 0.85f), sh);
                    // dry flakes: light, rough patches
                    float fl = Mathf.PerlinNoise(ox + u * 30f, oy + v * 30f);
                    if (flakes > 0f && fl > 0.66f) c = Color.Lerp(c, new Color(0.96f, 0.88f, 0.82f), flakes * (fl - 0.66f) * 3f);
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
