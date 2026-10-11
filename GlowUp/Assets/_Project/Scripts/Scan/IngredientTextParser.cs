using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GlowUp.Scan
{
    /// <summary>
    /// Splits the text of an ingredient list (read by the camera or typed by the user) into
    /// individual ingredient names, cleaning up the usual noise ("Ingredients:", line breaks,
    /// percentages, "may contain", trailing full stops).
    /// </summary>
    public static class IngredientTextParser
    {
        // ---- what is NOT an ingredient ----
        // names the app knows as brands or products (the scanner fills this in): a brand printed on the pack is never an ingredient
        static readonly HashSet<string> notIngredients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // where an ingredient list ends on a label
        static readonly Regex ListEnd = new Regex(@"\b(directions|how to use|to use|usage|warning|warnings|caution|precaution|for external use|keep out of|store in|storage|made in|manufactured|distributed|imported|marketed|net wt|net weight|net vol|batch|lot no|exp(iry)?\.? ?date|best before|www\.|http|customer care)\b", RegexOptions.IgnoreCase);
        // words of a label that are never an ingredient name
        static readonly Regex LabelWords = new Regex(@"\b(new|net|wt|ml|oz|fl|gram|grams|spf|pa|shade|color|colour|no|dermatologically|tested|hypoallergenic|cruelty|free|vegan|waterproof|long|lasting|wear|hours?|hrs?|matte|finish|cosmetics?|beauty|inc|corp|co|ltd|philippines|korea|japan|usa|france|china|barcode|fda|notification)\b", RegexOptions.IgnoreCase);

        /// <summary>The scanner tells the reader which names are brands and products, so they are never taken for ingredients.</summary>
        public static void SetNotIngredients(IEnumerable<string> names)
        {
            notIngredients.Clear();
            if (names == null) return;
            foreach (string name in names) { string key = Key(name); if (key.Length >= 2) notIngredients.Add(key); }
        }

        static string Key(string name) => Regex.Replace((name ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

        /// <summary>
        /// True when the text has the SHAPE of an ingredient list: the word "Ingredients" (also INCI, Composition), or several
        /// names in a row with commas between them. The front of a pack (brand, product name, claims) has neither.
        /// </summary>
        public static bool HasListShape(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (Regex.IsMatch(text, @"\b(ingredients?|ingredientes|inci|composition)\b", RegexOptions.IgnoreCase)) return true;
            int commas = 0; foreach (char c in text) if (c == ',' || c == ';') commas++;
            return commas >= 4;
        }

        /// <summary>True for a name that is a brand, a product, or ordinary label wording rather than an ingredient.</summary>
        static bool NotAnIngredient(string name)
        {
            string key = Key(name);
            if (key.Length == 0 || notIngredients.Contains(key)) return true;
            if (IngredientKnowledge.IsKnown(name)) return false;                 // a known ingredient always counts, whatever it looks like
            // every word is label wording ("New", "Long Lasting", "Net Wt"): not an ingredient
            string left = LabelWords.Replace(key, " ").Trim();
            return left.Length < 2;
        }

        public static List<string> Parse(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            string t = text.Replace('\r', '\n');

            // start after the word "Ingredients" when the label has it
            int start = t.IndexOf("ingredients", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = t.IndexOf("ingredientes", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int colon = t.IndexOfAny(new[] { ':', '\n' }, start);
                t = t.Substring(colon > start && colon - start < 20 ? colon + 1 : start + "ingredients".Length);
            }

            // the list ends where the label goes on to something else (directions, warnings, who made it ...)
            Match end = ListEnd.Match(t);
            if (end.Success && end.Index > 12) t = t.Substring(0, end.Index);

            // A list separated by commas wraps over several lines; a list typed one-per-line has no commas.
            bool commaSeparated = t.IndexOf(',') >= 0 || t.IndexOf(';') >= 0;
            if (commaSeparated) t = t.Replace('\n', ' ');
            t = Regex.Replace(t, @"\bmay contain\b|\[\s*\+\s*/\s*-\s*\]|\(\s*\+\s*/\s*-\s*\)", ",", RegexOptions.IgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawItem in t.Split(new[] { ',', ';', '\n', '•', '·', '*' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // "Water (Aqua)" -> the name outside the brackets, and the name inside as a second chance
                string outside = Regex.Replace(rawItem, @"\([^)]*\)", " ");
                foreach (string candidate in new[] { outside, Inside(rawItem) })
                {
                    string name = Clean(candidate);
                    if (name.Length == 0 || NotAnIngredient(name) || !seen.Add(name)) continue;
                    result.Add(name);
                    break;                              // one name per item is enough
                }
            }
            return result;
        }

        static string Inside(string item)
        {
            var m = Regex.Match(item, @"\(([^)]*)\)");
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string c = Regex.Replace(s, @"\d+([.,]\d+)?\s*%", " ");          // percentages
            c = Regex.Replace(c, @"[^\p{L}\p{N}\s\-/'.]", " ");               // stray symbols from the camera
            c = Regex.Replace(c, @"\s+", " ").Trim().Trim('.', '-', '/', '\'').Trim();
            if (c.Length < 2 || c.Length > 60) return "";
            int letters = 0; foreach (char ch in c) if (char.IsLetter(ch)) letters++;
            if (letters < 2 || letters < c.Length * 0.5f) return "";          // mostly digits / noise
            if (c.Split(' ').Length > 7) return "";                           // a sentence, not an ingredient

            // the project's own normaliser maps common synonyms (parfum -> Fragrance, aqua -> Water)
            // (a "/" is turned into a space first, because the normaliser would glue "Caprylic/Capric" into one word)
            string normalised = IngredientNormalizer.Normalize(c.Replace('/', ' '));
            return string.IsNullOrWhiteSpace(normalised) ? c : normalised;
        }

        /// <summary>How many of these names are real, recognised ingredients (used to decide whether a camera read worked).</summary>
        public static int CountRecognised(List<string> names)
        {
            int n = 0;
            foreach (string name in names) if (IngredientKnowledge.IsKnown(name)) n++;
            return n;
        }
    }
}
