using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Generates a seamless, subtle "old paper" grain (soft blotches, fine grain, a few fibres and
    /// specks) used as a tiled overlay on the background of every scene. It only darkens the paper
    /// slightly, so the background colour itself still comes from the ThemeProfile.
    /// Replace Assets/_Project/Sprites/Textures/paper_grain.png with a scanned paper texture
    /// (same file name) if you prefer a real one.
    /// </summary>
    public static class PaperTextureFactory
    {
        const string Folder = "Assets/_Project/Sprites/Textures/";

        public static Sprite Paper
        {
            get
            {
                string path = Folder + "paper_grain.png";
                var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (existing != null) return existing;

                Directory.CreateDirectory(Folder);
                const int N = 512;
                var alpha = new float[N * N];
                var rnd = new System.Random(1907);

                // soft blotches + medium mottling + fine grain (all seamless)
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float blot = Tileable(x, y, N, 5f);
                        float mid = Tileable(x, y, N, 24f);
                        float grain = (float)rnd.NextDouble();
                        alpha[y * N + x] = 0.22f * Mathf.Max(0f, blot - 0.45f) + 0.14f * Mathf.Max(0f, mid - 0.5f) + 0.035f * grain * grain;
                    }

                // paper fibres: short, faint strokes in random directions (wrapping at the edges)
                for (int i = 0; i < 340; i++)
                {
                    float fx = (float)rnd.NextDouble() * N, fy = (float)rnd.NextDouble() * N;
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    int len = 8 + rnd.Next(34);
                    float strength = 0.04f + (float)rnd.NextDouble() * 0.06f;
                    float bend = ((float)rnd.NextDouble() - 0.5f) * 0.06f;
                    for (int s = 0; s < len; s++)
                    {
                        int px = ((int)fx % N + N) % N, py = ((int)fy % N + N) % N;
                        alpha[py * N + px] += strength;
                        fx += Mathf.Cos(ang); fy += Mathf.Sin(ang); ang += bend;
                    }
                }

                // specks
                for (int i = 0; i < 700; i++)
                {
                    int px = rnd.Next(N), py = rnd.Next(N);
                    alpha[py * N + px] += 0.10f + (float)rnd.NextDouble() * 0.14f;
                }

                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var pixels = new Color[N * N];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.36f, 0.29f, 0.22f, Mathf.Clamp(alpha[i], 0f, 0.45f));
                tex.SetPixels(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;      // needed so the Image can tile it in one quad
                settings.wrapMode = TextureWrapMode.Repeat;
                imp.SetTextureSettings(settings);
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }

        /// <summary>Perlin noise that repeats seamlessly every n pixels.</summary>
        static float Tileable(int x, int y, int n, float freq)
        {
            float u = x / (float)n, v = y / (float)n;
            float a = Mathf.PerlinNoise(100f + u * freq, 100f + v * freq);
            float b = Mathf.PerlinNoise(100f + (u - 1f) * freq, 100f + v * freq);
            float c = Mathf.PerlinNoise(100f + (u - 1f) * freq, 100f + (v - 1f) * freq);
            float d = Mathf.PerlinNoise(100f + u * freq, 100f + (v - 1f) * freq);
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * u * v + d * (1 - u) * v;
        }
    }
}
