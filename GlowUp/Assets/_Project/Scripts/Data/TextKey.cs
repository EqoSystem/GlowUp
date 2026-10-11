using System.Collections.Generic;
using System.Text;

namespace GlowUp.Data
{
    /// <summary>
    /// One way of turning a name into a comparable key, used everywhere two names are compared (products, brands,
    /// ingredients, search, document ids). "L'Ore\u0301al  PARIS" (with its accent), "l'oreal paris" and "L Oreal Paris" all give "l oreal paris".
    ///
    /// The rule, in order: lower case; accents removed (an accented e becomes e); every character that is not a letter a-z or a digit
    /// becomes a space; several spaces become one; no space at either end.
    /// The admin website uses the same rule (normalizeName in _Shared/glowupRules.js): keep the two the same.
    /// </summary>
    public static class TextKey
    {
        /// <summary>The comparable key of a name ("" for nothing).</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string folded = Fold(text.ToLowerInvariant());
            var key = new StringBuilder(folded.Length);
            bool space = false;
            foreach (char c in folded)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    if (space && key.Length > 0) key.Append(' ');
                    key.Append(c); space = false;
                }
                else space = true;
            }
            if (key.Length > 0) return key.ToString();

            // a name written in another alphabet has no a-z letters at all: it is compared as it is written
            string raw = text.Trim().ToLowerInvariant();
            var kept = new StringBuilder(raw.Length);
            space = false;
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c)) { if (space && kept.Length > 0) kept.Append(' '); kept.Append(c); space = false; }
                else space = true;
            }
            return kept.ToString();
        }

        /// <summary>The key with "_" between the words: the form used inside document ids ("l_oreal_paris").</summary>
        public static string Slug(string text) => Normalize(text).Replace(' ', '_');

        /// <summary>The key without any spaces ("lorealparis"): "Maybelline New York" and "MaybellineNewYork" compare equal.</summary>
        public static string Compact(string text) => Normalize(text).Replace(" ", "");

        /// <summary>The words of the key, in order.</summary>
        public static string[] Words(string text)
        {
            string key = Normalize(text);
            return key.Length == 0 ? new string[0] : key.Split(' ');
        }

        /// <summary>True when both names give the same key (and the key is not empty).</summary>
        public static bool Same(string a, string b)
        {
            string ka = Normalize(a);
            return ka.Length > 0 && ka == Normalize(b);
        }

        // letters that do not come apart into "letter + accent" by themselves
        static readonly Dictionary<char, string> Special = new Dictionary<char, string>
        {
            { '\u00df', "ss" }, { '\u00e6', "ae" }, { '\u0153', "oe" }, { '\u00f8', "o" }, { '\u0111', "d" }, { '\u00f0', "d" }, { '\u0142', "l" }, { '\u00fe', "th" }, { '\u0131', "i" }
        };

        // used only when the phone cannot split accents by itself
        const string AccentedLetters = "\u00e0\u00e1\u00e2\u00e3\u00e4\u00e5\u0101\u0103\u0105\u00e7\u0107\u0109\u010b\u010d\u010f\u00e8\u00e9\u00ea\u00eb\u0113\u0115\u0117\u0119\u011b\u011d\u011f\u0121\u0123\u0125\u00ec\u00ed\u00ee\u00ef\u0129\u012b\u012d\u012f\u0135\u0137\u013a\u013c\u013e\u00f1\u0144\u0146\u0148\u00f2\u00f3\u00f4\u00f5\u00f6\u014d\u014f\u0151\u0155\u0157\u0159\u015b\u015d\u015f\u0161\u0163\u0165\u00f9\u00fa\u00fb\u00fc\u0169\u016b\u016d\u016f\u0171\u0173\u0175\u00fd\u00ff\u0177\u017a\u017c\u017e";
        const string PlainLetters = "aaaaaaaaacccccdeeeeeeeeegggghiiiiiiiijklllnnnnoooooooorrrssssttuuuuuuuuuuwyyyzzz";

        /// <summary>Removes accents from lower-case text.</summary>
        static string Fold(string lower)
        {
            bool plain = true;
            foreach (char c in lower) if (c > 127) { plain = false; break; }
            if (plain) return lower;

            string apart;
            try { apart = lower.Normalize(NormalizationForm.FormD); }
            catch { apart = null; }

            var result = new StringBuilder(lower.Length);
            foreach (char c in apart ?? lower)
            {
                if (c >= '\u0300' && c <= '\u036f') continue;                 // the accent itself
                if (Special.TryGetValue(c, out string replacement)) { result.Append(replacement); continue; }
                if (apart == null)
                {
                    int at = AccentedLetters.IndexOf(c);
                    if (at >= 0 && at < PlainLetters.Length) { result.Append(PlainLetters[at]); continue; }
                }
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
