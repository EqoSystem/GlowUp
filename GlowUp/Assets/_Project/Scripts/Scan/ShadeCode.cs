using System.Collections.Generic;
using System.Text;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// SHADE CODE: links the shade code printed on a pack ("12A", "131", "NC20") to one of the product's colours.
    ///
    /// Brands label shades with codes, not colours, so the camera can only read the code. When a product on the website has
    /// its shade codes (optional field "shadeCodes", in the same order as "shades"), the code read on the pack picks the colour:
    ///   1. the same code, ignoring capitals, spaces, dashes, dots, "#", "No." and "Shade" ("12 A", "12-a", "No. 12A" = "12A");
    ///   2. otherwise the same code with the camera's usual mix-ups evened out (O/0, I/l/1, S/5, B/8, Z/2), when exactly ONE of
    ///      the product's codes matches that way ("I2A" = "12A"; "12B" and "128" on one product: no guess).
    /// A code that matches nothing, or matches two codes equally, gives -1: the app then does not pick a colour by itself.
    /// Pure logic: tested in Editor/Tests/ArUpgradeTests.cs.
    /// </summary>
    public static class ShadeCode
    {
        static readonly string[] Prefixes = { "shade", "colour", "color", "no", "nr", "n" };

        /// <summary>The comparable form of a code: letters and digits only, capitals, without a leading "Shade" / "No.".</summary>
        public static string Key(string code)
        {
            string normal = TextKey.Normalize(code);              // lower case, accents off, punctuation as spaces
            if (normal.Length == 0) return "";
            string[] words = normal.Split(' ');
            int first = 0;
            while (first < words.Length - 1 && System.Array.IndexOf(Prefixes, words[first]) >= 0) first++;
            var key = new StringBuilder(normal.Length);
            for (int i = first; i < words.Length; i++) key.Append(words[i]);
            return key.ToString().ToUpperInvariant();
        }

        /// <summary>The key with the camera's mix-ups evened out: O as 0, I and L as 1, S as 5, B as 8, Z as 2.</summary>
        public static string Folded(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var folded = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                switch (c)
                {
                    case 'O': folded.Append('0'); break;
                    case 'I': case 'L': folded.Append('1'); break;
                    case 'S': folded.Append('5'); break;
                    case 'B': folded.Append('8'); break;
                    case 'Z': folded.Append('2'); break;
                    default: folded.Append(c); break;
                }
            }
            return folded.ToString();
        }

        /// <summary>
        /// The position of the code read on the pack among the product's codes, or -1. `read` may hold more than the code
        /// ("120 Classic Ivory"): its first word is tried too.
        /// </summary>
        public static int Find(string read, IList<string> productCodes)
        {
            if (string.IsNullOrWhiteSpace(read) || productCodes == null || productCodes.Count == 0) return -1;
            foreach (string candidate in Candidates(read))
            {
                int exact = Unique(candidate, productCodes, false);
                if (exact >= 0) return exact;
                if (exact == -2) return -1;                       // two equal codes on one product: never guess
            }
            foreach (string candidate in Candidates(read))
            {
                int folded = Unique(candidate, productCodes, true);
                if (folded >= 0) return folded;
                if (folded == -2) return -1;
            }
            return -1;
        }

        /// <summary>The colour (hex) of the code read on the pack, or "" when it cannot be told.</summary>
        public static string HexFor(string read, IList<string> productCodes, IList<string> productShades)
        {
            int at = Find(read, productCodes);
            return at >= 0 && productShades != null && at < productShades.Count ? productShades[at] ?? "" : "";
        }

        static IEnumerable<string> Candidates(string read)
        {
            string whole = Key(read);
            if (whole.Length > 0) yield return whole;
            string normal = TextKey.Normalize(read);
            int space = normal.IndexOf(' ');
            if (space > 0)
            {
                // "120 classic ivory" -> "120"; "shade 12 a" stays whole above
                string firstWord = Key(normal.Substring(0, space));
                if (firstWord.Length > 0 && firstWord != whole) yield return firstWord;
            }
        }

        /// <summary>The index of the one code equal to `key` (folded or not); -1 = none, -2 = more than one.</summary>
        static int Unique(string key, IList<string> codes, bool fold)
        {
            string want = fold ? Folded(key) : key;
            if (want.Length == 0) return -1;
            int found = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                string k = Key(codes[i]);
                if (k.Length == 0) continue;
                if ((fold ? Folded(k) : k) != want) continue;
                if (found >= 0) return -2;
                found = i;
            }
            return found;
        }
    }
}
