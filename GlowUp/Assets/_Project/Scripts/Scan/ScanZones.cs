using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>The five parts of the ring of hold-to-scan. Each one fills when the camera has seen that part of the product.</summary>
    public enum ScanZone
    {
        /// <summary>The front: the brand and the product's name.</summary>
        Front = 0,
        /// <summary>The ingredient list (usually on the back).</summary>
        Ingredients = 1,
        /// <summary>The barcode, a registration number or a batch code (usually on the bottom or the back).</summary>
        Codes = 2,
        /// <summary>The shade's name or number, or its colour.</summary>
        Shade = 3,
        /// <summary>The other sides: the product was turned and several different views were seen.</summary>
        Around = 4
    }

    /// <summary>
    /// What tells the zones apart. A picture belongs to a zone because of what is IN it (a brand name, the word
    /// "Ingredients", a barcode, a shade number), never because of how the phone is held.
    /// Pure logic: no camera, no Unity scene.
    /// </summary>
    public static class ScanZones
    {
        public const int Count = 5;

        /// <summary>The zones' names as the user reads them.</summary>
        public static readonly string[] Names = { "Front", "Ingredients", "Barcode", "Shade", "Other sides" };

        // "NN-1000014495482" (FDA Philippines notification), "CPR No. ...", "FDA Reg. No. ...", "Notification No. ..."
        static readonly Regex Registration = new Regex(
            @"\bNN[\s\-]?\d{8,15}\b|\b(?:FDA|CPR|CPN|CMN|LTO)\b[^\n]{0,18}\d{5,}|\b(?:notif(?:ication)?|reg(?:istration)?)\.?\s*(?:no|number|#)\.?\s*:?\s*[A-Z0-9\-]{5,}",
            RegexOptions.IgnoreCase);

        // "LOT 2304A", "Batch No. B23X9", "MFG 05/2026", "EXP 2027-05", "BEST BEFORE 12/27"
        static readonly Regex Batch = new Regex(
            @"\b(?:lot|batch|b\.?\s?n\.?|mfg|mfd|exp|bbe|best before|use by|use before)\b\.?\s*(?:no|number|date)?\.?\s*:?\s*[A-Z0-9][A-Z0-9\-/\. ]{2,}",
            RegexOptions.IgnoreCase);

        // "Shade: 05 Rosewood", "Color No. 3", "#120", "No. 12", and a line that is only a number and a name: "120 Classic Ivory"
        static readonly Regex ShadeWord = new Regex(
            @"\b(?:shade|colou?r|tone|tono|teinte|nuance)\b[ \t]*(?:no|number|name)?\.?[ \t]*[:#]?[ \t]*([A-Z0-9][A-Z0-9 \-']{0,28})",
            RegexOptions.IgnoreCase);
        static readonly Regex ShadeNumber = new Regex(@"(?:^|[\s(])(?:#\s?|N[o\u00b0\u00ba]\.?\s?)(\d{1,3}[A-Z]?)\b(?:\s+([A-Z][A-Za-z']+(?: [A-Z][A-Za-z']+){0,2}))?", RegexOptions.IgnoreCase);
        static readonly Regex ShadeLine = new Regex(@"^\s*(\d{2,3}[A-Z]?)\s+([A-Z][A-Za-z']+(?: [A-Za-z'][A-Za-z']+){0,3})\s*$", RegexOptions.Multiline);

        /// <summary>True when the text holds a registration number, a batch or lot code, or a date code. `what` = the piece that was found.</summary>
        public static bool HasCode(string text, out string what)
        {
            what = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match m = Registration.Match(text);
            if (!m.Success) m = Batch.Match(text);
            if (!m.Success) return false;
            what = m.Value.Trim();
            return true;
        }

        /// <summary>The FDA Philippines notification number in the text ("NN-1000014495482"), or "".</summary>
        public static string RegistrationNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            Match m = Regex.Match(text, @"\bNN[\s\-]?(\d{8,15})\b", RegexOptions.IgnoreCase);
            return m.Success ? "NN-" + m.Groups[1].Value : "";
        }

        /// <summary>
        /// The shade named in the text ("05 Rosewood", "120 Classic Ivory", "12"), or "" when the text names none.
        /// A word such as "colour" inside a sentence ("long lasting colour") is not a shade: what follows it must look
        /// like a name or a number, and must not be label wording.
        /// </summary>
        public static string ShadeIn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            foreach (Match m in ShadeWord.Matches(text))
            {
                string value = Tidy(m.Groups[1].Value);
                if (LooksLikeShade(value)) return value;
            }
            foreach (Match line in ShadeLine.Matches(text))
            {
                string value = Tidy(line.Groups[1].Value + " " + line.Groups[2].Value);
                if (LooksLikeShade(value)) return value;                // "120 Classic Ivory", but not "30 ML" or "24H WEAR"
            }
            Match number = ShadeNumber.Match(text);
            if (number.Success)
            {
                string name = number.Groups[2].Success ? number.Groups[2].Value : "";
                return Tidy(number.Groups[1].Value + (name.Length > 0 && LooksLikeShade(name) ? " " + name : ""));
            }
            return "";
        }

        static string Tidy(string value)
        {
            string cut = value;
            int newline = cut.IndexOf('\n');
            if (newline >= 0) cut = cut.Substring(0, newline);
            return Regex.Replace(cut, @"\s+", " ").Trim(' ', '-', ':', '#');
        }

        /// <summary>A shade is a number, or a short name that is not label wording ("free", "protection", "24h") and not an ingredient.</summary>
        static bool LooksLikeShade(string value)
        {
            string key = TextKey.Normalize(value);
            if (key.Length == 0 || key.Length > 28) return false;
            if (Regex.IsMatch(key, @"^\d{1,3}[a-z]?$")) return true;
            string[] words = key.Split(' ');
            if (words.Length > 4) return false;
            if (IngredientStoplist.Hit(value) != null) return false;                 // "long lasting colour", "color free"
            if (IngredientKnowledge.IsExactName(value)) return false;
            foreach (string word in words) if (word == "of" || word == "the" || word == "and" || word == "for" || word == "with" || word == "that" || word == "your") return false;
            return true;
        }
    }
}
