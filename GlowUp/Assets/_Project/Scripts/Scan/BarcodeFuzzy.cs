using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// A BARCODE THAT WAS READ BADLY (phase 2B, 11 November 2026). The phone's barcode reader (ML Kit) only reports numbers whose bars it
    /// read completely and whose check digit is right. When the bars cannot be read, the number printed under them is often still read
    /// as text, but text reading can miss a digit or take a 3 for an 8. This class
    ///   1. finds such numbers in the text that was read (8 to 14 digits, spaces and dashes between groups allowed),
    ///   2. checks them: a complete EAN-8 / UPC-A / EAN-13 / GTIN-14 with a right check digit is a GOOD read,
    ///   3. for a read that is incomplete or has a wrong check digit, lists the catalogue's barcodes that are nearest to it.
    /// Nearest = at most 2 digits different (same length), one digit missing, extra or swapped with its neighbour, or the read being an
    /// unbroken part (8 digits or more) of the barcode. The caller shows the list for the user to pick from; nothing here confirms a
    /// product. Pure logic: no Unity objects, so it is tested in the Editor.
    /// </summary>
    public static class BarcodeFuzzy
    {
        public const int MinDigits = 8;
        public const int MaxShown = 5;

        public struct Near
        {
            public ProductData product;
            public string code;
            /// <summary>0 to 1: how close the read is (1 = the same number).</summary>
            public float score;
            public int distance;
        }

        static readonly Regex DigitRun = new Regex(@"(?<!\d)\d(?:[ \-]?\d){7,16}(?!\d)", RegexOptions.Compiled);

        /// <summary>The digit runs in `text` that could be a barcode number (spaces and dashes removed), longest first, without repeats.</summary>
        public static List<string> NumbersIn(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;
            foreach (Match m in DigitRun.Matches(text))
            {
                string digits = Digits(m.Value);
                if (digits.Length >= MinDigits && digits.Length <= 14 && !found.Contains(digits)) found.Add(digits);
            }
            found.Sort((a, b) => b.Length.CompareTo(a.Length));
            return found;
        }

        /// <summary>
        /// True for a complete EAN-8, UPC-A, EAN-13 or GTIN-14 whose last digit is the right check digit. A number whose zeros in front
        /// were dropped (the app's barcode key) is padded back first: zeros in front never change the check digit.
        /// </summary>
        public static bool IsValidGtin(string number)
        {
            string d = Digits(number);
            if (d.Length < 7 || d.Length > 14) return false;
            if (d.Length < 8) d = d.PadLeft(8, '0');
            else if (d.Length > 8 && d.Length < 12) d = d.PadLeft(12, '0');
            int sum = 0;
            for (int i = d.Length - 2, weight = 3; i >= 0; i--, weight = weight == 3 ? 1 : 3) sum += (d[i] - '0') * weight;
            int check = (10 - sum % 10) % 10;
            return check == d[d.Length - 1] - '0';
        }

        /// <summary>
        /// The catalogue products whose barcode is nearest to `read`, best first (at most MaxShown). Empty when nothing is near enough.
        /// `catalogue` = barcode key (ProductIndex.BarcodeKey form: digits without leading zeros) with its product.
        /// </summary>
        public static List<Near> Nearest(string read, IEnumerable<KeyValuePair<string, ProductData>> catalogue)
        {
            var list = new List<Near>();
            string key = Digits(read).TrimStart('0');
            if (key.Length < MinDigits - 1 || catalogue == null) return list;
            foreach (KeyValuePair<string, ProductData> entry in catalogue)
            {
                string code = entry.Key ?? "";
                if (code.Length == 0 || entry.Value == null) continue;
                int distance = Distance(key, code);
                if (distance < 0) continue;
                float score = 1f - distance / (float)Math.Max(key.Length, code.Length);
                list.Add(new Near { product = entry.Value, code = code, score = score, distance = distance });
            }
            list.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : b.score.CompareTo(a.score));
            if (list.Count > MaxShown) list.RemoveRange(MaxShown, list.Count - MaxShown);
            return list;
        }

        /// <summary>Every barcode of these products (in the ProductIndex.BarcodeKey form) with its product.</summary>
        public static IEnumerable<KeyValuePair<string, ProductData>> BarcodesOf(IEnumerable<ProductData> products)
        {
            if (products == null) yield break;
            foreach (ProductData p in products)
            {
                string code = p != null ? ProductIndex.BarcodeKey(p.barcode) : "";
                if (code.Length > 0) yield return new KeyValuePair<string, ProductData>(code, p);
            }
        }

        /// <summary>How far the read is from a barcode: -1 = not near. 0 = the same.</summary>
        public static int Distance(string read, string code)
        {
            if (read == code) return 0;
            if (read.Length == code.Length)
            {
                int diff = 0, first = -1;
                for (int i = 0; i < read.Length; i++) if (read[i] != code[i]) { if (first < 0) first = i; diff++; }
                // two neighbours swapped ("12" read as "21") counts as one mistake
                if (diff == 2 && first + 1 < read.Length && read[first] == code[first + 1] && read[first + 1] == code[first]) return 1;
                return diff <= 2 ? diff : -1;
            }
            // a part of the barcode, unbroken (the start or the end of the number was cut off)
            if (read.Length < code.Length && read.Length >= MinDigits && code.Contains(read)) return code.Length - read.Length <= 3 ? 1 : 2;
            // one digit missing or one too many
            if (Math.Abs(read.Length - code.Length) == 1 && OneEditApart(read, code)) return 1;
            return -1;
        }

        static bool OneEditApart(string a, string b)
        {
            if (a.Length > b.Length) { string t = a; a = b; b = t; }
            int i = 0, j = 0; bool skipped = false;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (skipped) return false;
                skipped = true; j++;
            }
            return true;
        }

        static string Digits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }
    }
}
