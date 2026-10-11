using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// Remembers what a product's PACKAGING looks like, on this phone, so the scanner knows it again at once
    /// the next time it is held up to the camera (also offline, and before any text has been read).
    ///
    /// When a scan has named a product, the look of the picture is kept as a small "signature":
    ///   - its colours (how much of the picture each of 64 colours takes), and
    ///   - its pattern of light and dark (a 64-point fingerprint of the picture's layout).
    /// A new picture is the same packaging when both are close to a remembered one. To be safe, a
    /// remembered product is only suggested when the text on the pack does not speak against it: either
    /// little text was read, or a word of the product's or the brand's name is among it.
    ///
    /// Nothing is sent anywhere: the signatures are a few numbers kept in the app's own storage
    /// (the newest 200). Clearing the app's saved data forgets them.
    /// </summary>
    public static class PackagingMemory
    {
        public static bool Enabled = true;
        /// <summary>How alike the colours must be (0 to 1; higher = stricter).</summary>
        public static float ColourMatch = 0.88f;
        /// <summary>How many of the 64 light/dark points may differ (lower = stricter).</summary>
        public static int PatternDifference = 12;
        const string CacheKey = "packaging_memory";
        const int Limit = 200;

        [Serializable]
        public class Look
        {
            public string productName = "", brandName = "", cosmeticType = "", shadeHex = "", shadeName = "", finish = "";
            public float[] colours = new float[64];
            public long pattern;
            public long seenAtUnix;
        }
        [Serializable] class Holder { public List<Look> items = new List<Look>(); }

        static Holder holder;
        static Holder H() { if (holder == null && !OfflineCache.TryLoad(CacheKey, out holder)) holder = new Holder(); return holder ?? (holder = new Holder()); }

        public static int Count => H().items.Count;

        /// <summary>The look of a picture: its colours and its light/dark pattern. Null when the picture cannot be read.</summary>
        public static Look SignatureOf(Texture2D picture)
        {
            if (picture == null || picture.width < 16 || picture.height < 16) return null;
            Color32[] pixels;
            try { pixels = picture.GetPixels32(); } catch (Exception) { return null; }
            int w = picture.width, h = picture.height;
            var look = new Look();

            // colours: the middle of the picture counts (the edges are mostly the hand and the background)
            int x0 = w / 6, x1 = w - w / 6, y0 = h / 6, y1 = h - h / 6, step = Mathf.Max(1, Mathf.Min(w, h) / 96), counted = 0;
            for (int y = y0; y < y1; y += step)
                for (int x = x0; x < x1; x += step)
                {
                    Color32 p = pixels[y * w + x];
                    look.colours[(p.r >> 6) * 16 + (p.g >> 6) * 4 + (p.b >> 6)] += 1f;
                    counted++;
                }
            if (counted == 0) return null;
            for (int i = 0; i < 64; i++) look.colours[i] /= counted;

            // pattern: 9 x 8 points of brightness; each point is compared with its right-hand neighbour
            var grey = new float[9 * 8];
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 9; c++)
                {
                    int cx = x0 + (x1 - x0) * (2 * c + 1) / 18, cy = y0 + (y1 - y0) * (2 * r + 1) / 16;
                    float sum = 0f; int n = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int px = Mathf.Clamp(cx + dx * step, 0, w - 1), py = Mathf.Clamp(cy + dy * step, 0, h - 1);
                            Color32 p = pixels[py * w + px];
                            sum += p.r * 0.3f + p.g * 0.59f + p.b * 0.11f; n++;
                        }
                    grey[r * 9 + c] = sum / n;
                }
            long bits = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (grey[r * 9 + c] > grey[r * 9 + c + 1]) bits |= 1L << (r * 8 + c);
            look.pattern = bits;
            return look;
        }

        /// <summary>Keeps the look of a product that has just been named.</summary>
        public static void Remember(Look look, string productName, string brandName, string cosmeticType, string shadeHex, string shadeName, string finish)
        {
            if (!Enabled || look == null || (string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(brandName))) return;
            look.productName = (productName ?? "").Trim(); look.brandName = (brandName ?? "").Trim(); look.cosmeticType = (cosmeticType ?? "").Trim();
            look.shadeHex = shadeHex ?? ""; look.shadeName = shadeName ?? ""; look.finish = finish ?? "";
            look.seenAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            List<Look> items = H().items;
            // the same product seen again from nearly the same side replaces its older look
            items.RemoveAll(old => string.Equals(old.productName, look.productName, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(old.brandName, look.brandName, StringComparison.OrdinalIgnoreCase) && Alike(old, look));
            items.Add(look);
            while (items.Count > Limit) items.RemoveAt(0);
            OfflineCache.Save(CacheKey, H());
        }

        /// <summary>The remembered product this picture shows, or null. `textOnPack` is what the text reader found (may be empty).</summary>
        public static Look Recall(Look seen, string textOnPack)
        {
            if (!Enabled || seen == null) return null;
            Look best = null; float bestScore = 0f;
            foreach (Look known in H().items)
            {
                float colours = ColourOverlap(known, seen);
                int differ = Differences(known.pattern, seen.pattern);
                if (colours < ColourMatch || differ > PatternDifference) continue;
                float score = colours - differ * 0.004f;
                if (score > bestScore) { bestScore = score; best = known; }
            }
            if (best == null) return null;
            // the text on the pack must not speak against it
            string text = " " + Plain(textOnPack) + " ";
            if (text.Trim().Length < 12) return best;
            foreach (string word in (Plain(best.productName) + " " + Plain(best.brandName)).Split(' '))
                if (word.Length >= 4 && text.Contains(" " + word + " ")) return best;
            return null;
        }

        static bool Alike(Look a, Look b) => ColourOverlap(a, b) >= ColourMatch && Differences(a.pattern, b.pattern) <= PatternDifference;

        static float ColourOverlap(Look a, Look b)
        {
            if (a.colours == null || b.colours == null || a.colours.Length != 64 || b.colours.Length != 64) return 0f;
            float shared = 0f;
            for (int i = 0; i < 64; i++) shared += Mathf.Min(a.colours[i], b.colours[i]);
            return shared;
        }

        static int Differences(long a, long b)
        {
            ulong x = (ulong)(a ^ b); int n = 0;
            while (x != 0) { x &= x - 1; n++; }
            return n;
        }

        static string Plain(string text)
        {
            var sb = new System.Text.StringBuilder();
            bool space = true;
            foreach (char c in text ?? "")
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }
    }
}
