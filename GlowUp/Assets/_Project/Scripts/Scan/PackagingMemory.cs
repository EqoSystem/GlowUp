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
    /// Several sides of one product are kept (its front, another side), each as its own signature.
    ///
    /// A new picture is compared with all of them. A STRONG match is worth asking the user about at once ("Is this
    /// ...?"); a MEDIUM match only when a word of the product's or the brand's name was read on the pack as well;
    /// anything weaker is no match. The app never takes a look for the product without asking: two products of one
    /// line can look the same.
    ///
    /// Nothing is sent anywhere: the signatures are a few numbers, not pictures, kept in the app's own storage
    /// (the newest 400). Clearing the app's saved data forgets them.
    /// </summary>
    public static class PackagingMemory
    {
        public static bool Enabled = true;
        /// <summary>MEDIUM match: how alike the colours must be at least (0 to 1; higher = stricter).</summary>
        public static float ColourMatch = 0.88f;
        /// <summary>MEDIUM match: how many of the 64 light/dark points may differ at most (lower = stricter).</summary>
        public static int PatternDifference = 12;
        /// <summary>STRONG match: the colours are at least this alike ...</summary>
        public static float StrongColourMatch = 0.93f;
        /// <summary>... and at most this many of the 64 light/dark points differ.</summary>
        public static int StrongPatternDifference = 7;
        const string CacheKey = "packaging_memory";
        const int Limit = 400;

        [Serializable]
        public class Look
        {
            public string productName = "", brandName = "", cosmeticType = "", shadeHex = "", shadeName = "", finish = "";
            /// <summary>The inventory's id of the product ("" = a product only this phone knows).</summary>
            public string productId = "";
            /// <summary>Which side of the pack this is: "front" or "side" ("" = kept before sides were told apart).</summary>
            public string side = "";
            public float[] colours = new float[64];
            public long pattern;
            public long seenAtUnix;
        }

        public enum Strength { None, Medium, Strong }

        /// <summary>A remembered product a picture may show.</summary>
        public class Match
        {
            public Look look;
            /// <summary>0 to 1: how alike the picture and the remembered look are.</summary>
            public float score;
            public Strength strength;
            /// <summary>A word of the product's or the brand's name was read on the pack.</summary>
            public bool wordSeen;
            /// <summary>Strong, or medium with a word of the name on the pack: worth asking the user "Is this ...?".</summary>
            public bool WorthAsking => strength == Strength.Strong || (strength == Strength.Medium && wordSeen);
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
            var argb = new int[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) { Color32 p = pixels[i]; argb[i] = (p.r << 16) | (p.g << 8) | p.b; }
            return SignatureOf(argb, picture.width, picture.height, false);
        }

        /// <summary>
        /// The same for a picture given as plain numbers (hold-to-scan): one ARGB number per pixel, row by row from the
        /// TOP. Only the first width x height numbers are used.
        /// </summary>
        public static Look SignatureOf(int[] argb, int width, int height)
        {
            if (argb == null || width < 16 || height < 16 || argb.Length < width * height) return null;
            return SignatureOf(argb, width, height, true);
        }

        /// <summary>
        /// The look of a picture given as numbers (0xRRGGBB, the top byte is not looked at). `fromTop` = its rows start
        /// at the top; a texture counts them from the bottom. Both ways give the same look for the same picture, and
        /// nothing is copied: this runs several times a second while a product is scanned.
        /// </summary>
        static Look SignatureOf(int[] pixels, int w, int h, bool fromTop)
        {
            var look = new Look();

            // colours: the middle of the picture counts (the edges are mostly the hand and the background)
            int x0 = w / 6, x1 = w - w / 6, y0 = h / 6, y1 = h - h / 6, step = Mathf.Max(1, Mathf.Min(w, h) / 96), counted = 0;
            for (int y = y0; y < y1; y += step)
                for (int x = x0; x < x1; x += step)
                {
                    int p = pixels[(fromTop ? h - 1 - y : y) * w + x];
                    look.colours[(((p >> 16) & 0xFF) >> 6) * 16 + (((p >> 8) & 0xFF) >> 6) * 4 + ((p & 0xFF) >> 6)] += 1f;
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
                            int p = pixels[(fromTop ? h - 1 - py : py) * w + px];
                            sum += ((p >> 16) & 0xFF) * 0.3f + ((p >> 8) & 0xFF) * 0.59f + (p & 0xFF) * 0.11f; n++;
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

        /// <summary>
        /// Keeps the look of a product that has just been named. `side` = "front" or "side"; `productId` = the inventory's
        /// id of the product, when it has one. Several sides of one product are kept next to each other.
        /// </summary>
        public static void Remember(Look look, string productName, string brandName, string cosmeticType, string shadeHex, string shadeName, string finish,
                                    string productId = "", string side = "front")
        {
            if (!Enabled || look == null || (string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(brandName))) return;
            look.productName = (productName ?? "").Trim(); look.brandName = (brandName ?? "").Trim(); look.cosmeticType = (cosmeticType ?? "").Trim();
            look.shadeHex = shadeHex ?? ""; look.shadeName = shadeName ?? ""; look.finish = finish ?? "";
            look.productId = productId ?? ""; look.side = side ?? "";
            look.seenAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            List<Look> items = H().items;
            // the same product seen again from nearly the same side replaces its older look
            items.RemoveAll(old => SameProduct(old, look) && Alike(old, look));
            items.Add(look);
            while (items.Count > Limit) items.RemoveAt(0);
            OfflineCache.Save(CacheKey, H());
        }

        /// <summary>The remembered products this picture may show, best first, each product once. `textOnPack` is what the text reader found (may be empty).</summary>
        public static List<Match> Matches(Look seen, string textOnPack, int maximum = 3)
        {
            return Enabled ? MatchesAmong(H().items, seen, textOnPack, maximum) : new List<Match>();
        }

        /// <summary>The same against a given list of remembered looks (the app's own list is in the phone's storage).</summary>
        public static List<Match> MatchesAmong(IEnumerable<Look> remembered, Look seen, string textOnPack, int maximum = 3)
        {
            var found = new List<Match>();
            if (seen == null || remembered == null) return found;
            string text = " " + Plain(textOnPack) + " ";
            foreach (Look known in remembered)
            {
                if (known == null) continue;
                float colours = ColourOverlap(known, seen);
                int differ = Differences(known.pattern, seen.pattern);
                if (colours < ColourMatch || differ > PatternDifference) continue;
                var match = new Match
                {
                    look = known, score = Mathf.Clamp01(colours - differ * 0.004f),
                    strength = colours >= StrongColourMatch && differ <= StrongPatternDifference ? Strength.Strong : Strength.Medium
                };
                foreach (string word in (Plain(known.productName) + " " + Plain(known.brandName)).Split(' '))
                    if (word.Length >= 4 && text.Contains(" " + word + " ")) { match.wordSeen = true; break; }
                // each product once: its best side
                int at = found.FindIndex(other => SameProduct(other.look, known));
                if (at < 0) found.Add(match);
                else if (Better(match, found[at])) found[at] = match;
            }
            found.Sort((x, y) => Better(x, y) ? -1 : Better(y, x) ? 1 : 0);
            if (found.Count > maximum) found.RemoveRange(maximum, found.Count - maximum);
            return found;
        }

        // a match that is worth asking about comes first, then the stronger one, then the closer one
        static bool Better(Match a, Match b)
        {
            if (a.WorthAsking != b.WorthAsking) return a.WorthAsking;
            if (a.strength != b.strength) return a.strength > b.strength;
            return a.score > b.score + 0.0001f;
        }

        static bool SameProduct(Look a, Look b)
        {
            if (!string.IsNullOrEmpty(a.productId) && !string.IsNullOrEmpty(b.productId)) return a.productId == b.productId;
            return string.Equals(a.productName, b.productName, StringComparison.OrdinalIgnoreCase) && string.Equals(a.brandName, b.brandName, StringComparison.OrdinalIgnoreCase);
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
