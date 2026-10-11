using System;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE STOPLIST: words and phrases on a label that are never part of an ingredient name. A piece of label text
    /// that holds one of them is thrown away by the ingredient reader (IngredientTextParser): it is not counted, not
    /// shown and never sent to the AI or to the database.
    ///
    /// To change the list, add or remove a line below. Write each entry in small letters, without punctuation
    /// ("net wt", not "Net Wt."): the text is compared after capitals, accents and punctuation are taken out.
    /// An entry only matches WHOLE WORDS: "ml" matches "30 ml" but not "Emulsifier".
    /// One exception protects real ingredients: a piece that is exactly a name the app knows is always kept.
    /// </summary>
    public static class IngredientStoplist
    {
        /// <summary>Headers of the list itself.</summary>
        public static readonly string[] Headers =
        {
            "ingredients", "ingredient", "ingredientes", "inci", "composition", "contains", "may contain",
            "active ingredients", "inactive ingredients", "other ingredients", "sangkap", "mga sangkap"
        };

        /// <summary>How much is in the pack.</summary>
        public static readonly string[] WeightAndSize =
        {
            "net wt", "net weight", "net vol", "net volume", "net content", "net contents", "netto", "ml", "fl oz", "oz", "gram", "grams", "kg", "mg"
        };

        /// <summary>Who made it and where.</summary>
        public static readonly string[] MakerAndOrigin =
        {
            "made in", "product of", "manufactured", "manufacturer", "distributed", "distributor", "imported", "importer", "marketed",
            "exclusively", "packed by", "repacked", "licensed", "under license", "trademark", "all rights reserved", "copyright",
            "inc", "corp", "corporation", "co ltd", "ltd", "limited", "company", "enterprises", "trading", "industries", "laboratories",
            "philippines", "manila", "quezon city", "makati", "paris", "new york", "london", "seoul", "tokyo"
        };

        /// <summary>Codes, dates and registration numbers.</summary>
        public static readonly string[] CodesAndDates =
        {
            "batch", "lot", "lot no", "lot number", "mfg", "mfd", "exp", "expiry", "expiration", "best before", "use before", "use by",
            "barcode", "notification no", "cpr no", "reg no", "fda", "lto"
        };

        /// <summary>Web, phone and social media.</summary>
        public static readonly string[] Contact =
        {
            "www", "http", "https", "com", "email", "e mail", "tel", "telephone", "hotline", "customer care", "customer service",
            "facebook", "instagram", "tiktok", "website", "follow us", "visit", "scan me", "qr"
        };

        /// <summary>How to use it, and warnings.</summary>
        public static readonly string[] DirectionsAndWarnings =
        {
            "directions", "direction", "how to use", "to use", "usage", "apply", "warning", "warnings", "caution", "cautions",
            "precaution", "precautions", "for external use", "external use only", "keep out of reach", "keep away", "avoid contact",
            "discontinue", "store in", "storage", "do not", "if irritation", "rinse", "children", "consult", "see bottom",
            "see packaging", "see box", "please recycle", "recycle", "shake well", "patch test"
        };

        /// <summary>Advertising words.</summary>
        public static readonly string[] Marketing =
        {
            "new", "improved", "long lasting", "longlasting", "long wear", "longwear", "waterproof", "water proof", "smudge proof",
            "smudgeproof", "transfer proof", "transferproof", "kiss proof", "dermatologist tested", "dermatologically tested",
            "ophthalmologist tested", "clinically tested", "allergy tested", "hypoallergenic", "non comedogenic", "noncomedogenic",
            "cruelty free", "vegan", "halal", "free", "hour", "hours", "hr", "hrs", "24h", "12h", "all day", "matte", "glossy", "satin",
            "finish", "shade", "shades", "color", "colour", "cosmetic", "cosmetics", "beauty", "spf", "pa", "uva", "uvb", "broad spectrum"
        };

        static readonly string[][] Groups = { Headers, WeightAndSize, MakerAndOrigin, CodesAndDates, Contact, DirectionsAndWarnings, Marketing };

        /// <summary>The stoplist entry this text holds, or null. `text` may be raw label text.</summary>
        public static string Hit(string text)
        {
            string key = " " + TextKey.Normalize(text) + " ";
            if (key.Length <= 2) return null;
            foreach (string[] group in Groups)
                foreach (string entry in group)
                    if (key.IndexOf(" " + entry + " ", StringComparison.Ordinal) >= 0) return entry;
            return null;
        }

        /// <summary>Where an ingredient list ENDS on a label: the text from one of these on is not read.</summary>
        public static readonly Regex ListEnd = new Regex(
            @"\b(directions?|how to use|to use|usage|warnings?|cautions?|precautions?|for external use|keep out of|store in|storage|made in|product of|" +
            @"manufactured|distributed|imported|marketed|net wt|net weight|net vol|net content|batch|lot no|mfg|mfd|exp(iry)?\.? ?date|best before|" +
            @"www\.|https?:|customer care|dermatologically tested|dermatologist tested)\b", RegexOptions.IgnoreCase);

        /// <summary>The word a label puts in front of its ingredient list.</summary>
        public static readonly Regex ListStart = new Regex(@"\b(ingredients?|ingredientes|ingr\S{1,2}dients|inci|composition|sangkap)\b\s*[:;.\-]?", RegexOptions.IgnoreCase);

        /// <summary>What a label writes in front of the colourants that depend on the shade.</summary>
        public static readonly Regex MayContain = new Regex(@"\bmay contain\b\s*:?|\bpeut contenir\b\s*:?|[\[\(]\s*\+\s*/\s*-\s*[\]\)]?|\+\s*/\s*-", RegexOptions.IgnoreCase);

        /// <summary>
        /// Simple checks on the SHAPE of a piece: true when it cannot be an ingredient name (a number, a code, a phone
        /// number, a sentence, far too short or too long). `piece` is the cleaned piece of label text.
        /// </summary>
        public static bool WrongShape(string piece)
        {
            string p = (piece ?? "").Trim();
            if (ColourIndex.IsMatch(p)) return false;                                       // "CI 77491": a colourant, listed by its Colour Index number
            if (p.Length < 3 || p.Length > MaximumLength) return true;
            int letters = 0;
            foreach (char c in p) if (char.IsLetter(c)) letters++;
            if (letters < 3 || letters < p.Length * 0.5f) return true;                       // mostly digits or symbols
            if (p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > MaximumWords) return true;   // a sentence
            bool holdsColourIndex = Regex.IsMatch(p, @"\bC\.?I\.?\s*\d{5}\b", RegexOptions.IgnoreCase);
            if (!holdsColourIndex && Regex.IsMatch(p, @"\d{4,}")) return true;               // phone numbers and long codes
            if (!holdsColourIndex && CodeLike(p)) return true;                              // batch and lot codes such as L2304A
            return false;
        }

        /// <summary>The longest text that can still be one ingredient name, and the most words it can have.</summary>
        public static int MaximumLength = 70, MaximumWords = 7;

        /// <summary>A colourant written as its Colour Index number and nothing else ("CI 77491", "C.I. 15850", "Ci77891").</summary>
        public static readonly Regex ColourIndex = new Regex(@"^C\.?I\.?\s*\d{5}$", RegexOptions.IgnoreCase);

        /// <summary>True when one word of the piece is a code: three or more digits and at most two letters ("L2304A", "B23X9").</summary>
        static bool CodeLike(string piece)
        {
            foreach (string word in piece.Split(new[] { ' ', '/', '-', '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int letters = 0, digits = 0;
                foreach (char c in word) { if (char.IsLetter(c)) letters++; else if (char.IsDigit(c)) digits++; }
                if (digits >= 3 && letters >= 1 && letters <= 2) return true;
            }
            return false;
        }
    }
}
