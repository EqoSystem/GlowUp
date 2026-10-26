using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// Paints soft-edged masks in the face mesh's picture coordinates (UVs): fills polygons, grows them by a few pixels and
    /// softens the edge. Used for masks that are made from the tracked face itself at run time (ArBrowMask). The result is a
    /// grey value per pixel, 0 to 1, row by row from the bottom (the order of Texture2D.SetPixels32).
    /// </summary>
    public static class FaceMaskPainter
    {
        /// <summary>Fills each polygon (points in pixels) into `target` (n x n), keeping the largest value.</summary>
        public static void FillPolygon(float[] target, int n, Vector2[] polygon, List<float> crossings)
        {
            if (target == null || polygon == null || polygon.Length < 3) return;
            for (int y = 0; y < n; y++)
            {
                crossings.Clear();
                float sy = y + 0.5f;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) crossings.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                crossings.Sort();
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    int from = Mathf.Max(0, Mathf.CeilToInt(crossings[c] - 0.5f)), to = Mathf.Min(n - 1, Mathf.FloorToInt(crossings[c + 1] - 0.5f));
                    for (int x = from; x <= to; x++) target[y * n + x] = 1f;
                }
            }
        }

        /// <summary>Grows the filled area by `radius` pixels.</summary>
        public static float[] Dilate(float[] source, int n, int radius)
        {
            if (radius <= 0) return source;
            var result = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float best = 0f;
                    for (int dy = -radius; dy <= radius && best < 1f; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= n) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= n) continue;
                            if (source[yy * n + xx] > best) best = source[yy * n + xx];
                        }
                    }
                    result[y * n + x] = best;
                }
            return result;
        }

        /// <summary>A box blur of `radius` pixels (horizontal, then vertical).</summary>
        public static float[] BoxBlur(float[] source, int n, int radius)
        {
            if (radius <= 0) return source;
            var horizontal = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int dx = -radius; dx <= radius; dx++) { int xx = x + dx; if (xx < 0 || xx >= n) continue; sum += source[y * n + xx]; count++; }
                    horizontal[y * n + x] = sum / count;
                }
            var result = new float[source.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f; int count = 0;
                    for (int dy = -radius; dy <= radius; dy++) { int yy = y + dy; if (yy < 0 || yy >= n) continue; sum += horizontal[yy * n + x]; count++; }
                    result[y * n + x] = sum / count;
                }
            return result;
        }

        /// <summary>A texture whose ALPHA is the mask (white colour), as the make-up layer shaders read it.</summary>
        public static Texture2D ToAlphaTexture(float[] shape, int n, string name)
        {
            var pixels = new Color32[n * n];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(shape[i]) * 255f));
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
