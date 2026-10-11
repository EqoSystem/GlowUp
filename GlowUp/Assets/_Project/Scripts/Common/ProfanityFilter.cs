using System.Collections.Generic;
using System.Text;

namespace GlowUp.Common
{
    /// <summary>
    /// Says whether a name the user typed (the username on the dashboard) is appropriate: nothing sexual
    /// and nothing offensive, in English or Filipino. Spellings that try to slip through are caught too:
    /// capital letters, digits or signs used as letters ("@" for a, "0" for o, "1" for i, "3" for e,
    /// "5" and "$" for s), dots or underscores between letters, and repeated letters.
    ///
    /// To block another word, add it to Blocked (words that are only bad as a whole name go in WholeOnly,
    /// so "ass" does not block "cassie"). Lower-case letters only.
    /// </summary>
    public static class ProfanityFilter
    {
        // blocked wherever they appear inside the name
        static readonly string[] Blocked =
        {
            // English: sexual and crude
            "fuck", "phuck", "fck", "shit", "bitch", "btch", "biatch", "cunt", "cock", "dick", "penis", "vagina", "pussy",
            "boob", "tits", "titty", "nipple", "porn", "sex", "horny", "nude", "naked", "slut", "whore", "hooker", "blowjob",
            "handjob", "cum", "jizz", "dildo", "rape", "rapist", "molest", "pedo", "incest", "orgasm", "erotic", "fetish", "milf",
            "wank", "jerkoff", "bastard", "asshole", "douche", "prick", "twat", "bollock", "nazi", "hitler",
            // English: slurs
            "nigg", "nigr", "faggot", "retard", "chink", "tranny",
            // Filipino
            "puta", "putang", "tangina", "pakyu", "pakshet", "gago", "gaga", "tarantado", "ulol", "ulul", "bobo", "tanga",
            "kupal", "pokpok", "puke", "puki", "pekpek", "titi", "tite", "burat", "bayag", "kantot", "kantut", "jakol", "salsal", "iyot", "iyut",
            "hindot", "bilat", "leche", "letse", "punyeta", "libog", "kingina"
        };

        // blocked only when they are the whole name (too short, or part of ordinary names and words)
        static readonly string[] WholeOnly = { "ass", "arse", "tit", "fu", "fuk", "kkk", "hoe", "fag", "anal", "anus", "spic", "kike", "dyke", "yawa", "buang", "pisti", "pesti" };

        // ordinary names and words that contain a blocked word by accident
        static readonly string[] Harmless =
        {
            "essex", "sussex", "middlesex", "unisex", "cucumber", "cumin", "cumber", "document", "circum", "cumul", "scunthorpe",
            "titan", "title", "titus", "petite", "appetite", "cocktail", "peacock", "hancock", "cockatoo", "dickens", "dickson", "benedick",
            "tangerine", "tangent", "grape", "drape", "scrape", "trape", "therapist", "torpedo", "swank", "thorny", "skunk"
        };

        static readonly Dictionary<char, char> LookAlikes = new Dictionary<char, char>
        {
            { '0', 'o' }, { '1', 'i' }, { '!', 'i' }, { '3', 'e' }, { '4', 'a' }, { '@', 'a' }, { '5', 's' }, { '$', 's' }, { '7', 't' }, { '8', 'b' }, { '9', 'g' }
        };

        /// <summary>True when the name contains nothing sexual or offensive.</summary>
        public static bool IsClean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string plain = Normalise(name, false), squeezed = Normalise(name, true);
            return !IsBad(plain) && !IsBad(squeezed);
        }

        static bool IsBad(string text)
        {
            if (text.Length == 0) return false;
            foreach (string word in WholeOnly) if (text == word) return true;
            string cleared = text;
            foreach (string ok in Harmless) cleared = cleared.Replace(ok, " ");
            foreach (string word in Blocked) if (cleared.Contains(word)) return true;
            return false;
        }

        // lower case, look-alike characters turned into letters, everything that is not a letter dropped;
        // `squeeze` also turns runs of the same letter into one ("fuuuck" -> "fuck")
        static string Normalise(string name, bool squeeze)
        {
            var sb = new StringBuilder(name.Length);
            char last = '\0';
            foreach (char raw in name.ToLowerInvariant())
            {
                char c = LookAlikes.TryGetValue(raw, out char mapped) ? mapped : raw;
                if (c < 'a' || c > 'z') continue;
                if (squeeze && c == last) continue;
                sb.Append(c); last = c;
            }
            return sb.ToString();
        }
    }
}
