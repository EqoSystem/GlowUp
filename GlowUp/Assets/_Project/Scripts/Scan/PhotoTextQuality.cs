using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GlowUp.Scan
{
    /// <summary>
    /// IS THE TEXT READ FROM A PRODUCT PHOTO CLEAR ENOUGH TO USE? (7 November 2026)
    ///
    /// The phone's text reader gives no reliable certainty for a still photo, and it can return confident-looking nonsense
    /// ("MAPEM BUPEKGINY WATTE") from a blurred or curved label. So the reading is judged by its WORDS: how many of them are
    /// words a make-up pack really carries (catalogue brands and product names, make-up kinds, common pack words, ingredient
    /// names). Too few known words = "We couldn't read this clearly": nothing is sent, nothing of the reading is shown.
    ///
    /// The clean text keeps only the lines that read as real words; that is what goes to the server and to the admin, never
    /// the raw reading. Pure logic (no Unity objects), so it is tested on its own (PhotoCaptureTests).
    /// </summary>
    public static class PhotoTextQuality
    {
        /// <summary>At least this share of the words must be known words.</summary>
        public static float MinimumKnownShare = 0.3f;
        /// <summary>And at least this many known words in all.</summary>
        public static int MinimumKnownWords = 2;

        public class Verdict
        {
            public bool readable;
            /// <summary>Known words / all words, 0 to 1 (the "confidence" of the reading).</summary>
            public float score;
            public int words, knownWords;
            /// <summary>Only the lines that read as real words, joined by new lines (at most 1500 characters).</summary>
            public string cleanText = "";
        }

        static readonly Regex Word = new Regex(@"[A-Za-zÀ-ÿ]{3,}", RegexOptions.Compiled);
        static readonly Regex Amount = new Regex(@"\b\d+([.,]\d+)?\s?(ml|g|oz|fl|mg)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // words that stand on make-up packs (lower case). Brand and product words come from the catalogue on top of these.
        static readonly HashSet<string> PackWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lipstick", "lip", "lips", "gloss", "tint", "liner", "liquid", "matte", "satin", "shine", "cream", "creamy", "balm", "stain", "color", "colour",
            "foundation", "concealer", "primer", "powder", "pressed", "loose", "blush", "bronzer", "highlighter", "contour", "cushion", "compact",
            "eyeshadow", "shadow", "palette", "eyeliner", "eye", "eyes", "mascara", "lash", "lashes", "brow", "brows", "pencil", "gel", "pomade",
            "setting", "spray", "fixing", "mist", "stick", "pen", "crayon", "serum", "skin", "face", "cheek", "cheeks", "nude", "rose", "pink", "red",
            "coral", "berry", "mauve", "beige", "ivory", "sand", "honey", "natural", "warm", "cool", "neutral", "light", "medium", "dark", "deep", "fair",
            "long", "lasting", "longwear", "wear", "hour", "hours", "waterproof", "water", "resistant", "smudge", "proof", "transfer", "volume", "volumizing",
            "lengthening", "curl", "curling", "define", "full", "coverage", "lightweight", "oil", "free", "control", "hydrating", "moisture", "glow",
            "dewy", "radiant", "poreless", "smooth", "soft", "silky", "velvet", "ink", "super", "stay", "fit", "new", "york", "paris", "london", "beauty",
            "cosmetics", "cosmetic", "makeup", "make", "professional", "pro", "studio", "edition", "limited", "mini", "net", "weight", "made", "korea",
            "philippines", "france", "italy", "china", "japan", "usa", "shade", "tone", "finish", "spf", "pa", "broad", "spectrum", "dermatologically", "tested",
            "ingredients", "ingredient", "directions", "apply", "warning", "caution", "use", "only", "external", "keep", "out", "reach", "children",
            "store", "cool", "dry", "place", "manufactured", "distributed", "imported", "by", "for", "and", "with", "the", "batch", "exp", "mfg", "lot",
            // ingredient words that stand on nearly every list
            "aqua", "glycerin", "dimethicone", "mica", "titanium", "dioxide", "iron", "oxides", "silica", "talc", "tocopherol", "tocopheryl", "acetate",
            "phenoxyethanol", "paraffin", "cera", "alba", "wax", "isododecane", "trimethylsiloxysilicate", "nylon", "kaolin", "butylene", "glycol",
            "caprylyl", "ethylhexyl", "stearate", "alcohol", "parfum", "fragrance", "polyethylene", "magnesium", "zinc", "acid", "sodium", "extract",
            "seed", "butter", "copolymer", "crosspolymer", "methicone", "silicate", "alumina", "hydroxide", "bismuth", "oxychloride", "lanolin", "castor"
        };

        /// <summary>
        /// `isKnownWord` = extra known words (catalogue brands, product names, ingredient names), lower case; may be null.
        /// </summary>
        public static Verdict Judge(string text, Func<string, bool> isKnownWord)
            => Judge(text, isKnownWord, MinimumKnownWords, MinimumKnownShare);

        /// <summary>Same, with the thresholds given (PhotoTextReader judges each turned reading with one known word).</summary>
        public static Verdict Judge(string text, Func<string, bool> isKnownWord, int minimumKnownWords, float minimumKnownShare)
        {
            var verdict = new Verdict();
            if (string.IsNullOrWhiteSpace(text)) return verdict;
            var kept = new StringBuilder();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                int lineWords = 0, lineKnown = 0;
                foreach (Match m in Word.Matches(line))
                {
                    lineWords++;
                    if (Known(m.Value, isKnownWord)) lineKnown++;
                }
                verdict.words += lineWords; verdict.knownWords += lineKnown;
                bool keep = (lineWords > 0 && lineKnown > 0 && lineKnown >= lineWords * 0.5f) || (lineWords <= 1 && Amount.IsMatch(line));
                if (keep && kept.Length + line.Length + 1 <= 1500) { if (kept.Length > 0) kept.Append('\n'); kept.Append(Tidy(line)); }
            }
            verdict.score = verdict.words == 0 ? 0f : (float)verdict.knownWords / verdict.words;
            verdict.readable = verdict.knownWords >= minimumKnownWords && verdict.score >= minimumKnownShare;
            verdict.cleanText = verdict.readable ? kept.ToString() : "";
            return verdict;
        }

        /// <summary>Is this word one a make-up pack carries (pack words, or `isKnownWord`)?</summary>
        public static bool IsKnown(string word, Func<string, bool> isKnownWord) => Known(word, isKnownWord);

        static bool Known(string word, Func<string, bool> isKnownWord)
        {
            if (PackWords.Contains(word)) return true;
            string lower = word.ToLowerInvariant();
            return isKnownWord != null && isKnownWord(lower);
        }

        /// <summary>Spaces tidied, printable characters only (the line is data, never markup).</summary>
        static string Tidy(string line)
        {
            var sb = new StringBuilder(line.Length);
            foreach (char c in line) sb.Append(char.IsControl(c) || c == '<' || c == '>' ? ' ' : c);
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }
    }

    /// <summary>
    /// A BRAND NAME AS THE CATALOGUE SPELLS IT (7 November 2026). "MAYBELLINE NEWYORK" from a label becomes "Maybelline New York"
    /// when the catalogue has that brand (spaces, capitals and one or two misread letters do not matter). A brand the catalogue does
    /// not have keeps its own words, with ordinary capitals ("HAPPY SKIN" = "Happy Skin") and a few glued words split again.
    /// </summary>
    public static class BrandNameNormalizer
    {
        static readonly KeyValuePair<string, string>[] Glued =
        {
            new KeyValuePair<string, string>("newyork", "New York"), new KeyValuePair<string, string>("losangeles", "Los Angeles"),
            new KeyValuePair<string, string>("sanfrancisco", "San Francisco"), new KeyValuePair<string, string>("hongkong", "Hong Kong")
        };

        /// <summary>Lower-case letters and digits only (accents removed): "L'Oréal Paris" = "lorealparis".</summary>
        public static string Squash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in decomposed)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>The catalogue's own spelling when a catalogue brand matches; otherwise the read name tidied.</summary>
        public static string Normalize(string read, IEnumerable<string> catalogueBrands)
        {
            string raw = (read ?? "").Trim();
            if (raw.Length == 0) return "";
            string key = Squash(raw);
            if (key.Length >= 2 && catalogueBrands != null)
            {
                string best = null; int bestEdits = int.MaxValue;
                foreach (string brand in catalogueBrands)
                {
                    string other = Squash(brand);
                    if (other.Length < 2) continue;
                    if (other == key) return brand.Trim();
                    // one misread letter in every eight (at least one) is forgiven, never for very short names
                    if (other.Length < 5 || Math.Abs(other.Length - key.Length) > 2) continue;
                    int edits = Edits(key, other);
                    if (edits <= Math.Max(1, other.Length / 8) && edits < bestEdits) { best = brand.Trim(); bestEdits = edits; }
                }
                if (best != null) return best;
            }
            return Tidy(raw);
        }

        /// <summary>Spaces made single, glued place names split, ALL CAPS or all lower case words given ordinary capitals.</summary>
        public static string Tidy(string raw)
        {
            string s = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            bool shouting = s == s.ToUpperInvariant(), quiet = s == s.ToLowerInvariant();   // judged BEFORE glued words are split
            foreach (var glued in Glued) s = Regex.Replace(s, glued.Key, glued.Value, RegexOptions.IgnoreCase);
            if (!shouting && !quiet) return s;                       // mixed capitals are the brand's own way of writing it
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length == 0) continue;
                var sb = new StringBuilder(w.Length);
                bool start = true;
                foreach (char c in w)
                {
                    sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                    start = !char.IsLetterOrDigit(c) && c != '\'';    // "l'oreal" = "L'oreal", "fit-me" = "Fit-Me"
                }
                words[i] = sb.ToString();
            }
            return string.Join(" ", words);
        }

        static int Edits(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int previous = row[0]; row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int keep = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                    previous = keep;
                }
            }
            return row[b.Length];
        }
    }
}
