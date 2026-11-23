using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// Turns what the staff member captured into draft values, each marked with its source:
    ///   barcode  -> the GlowUp catalogue on the phone (possible duplicate), then Open Beauty Facts;
    ///   photos   -> the phone's own text reader (Google ML Kit, offline): ingredient list, brand, category, shade code.
    /// Text read from a photo is never "certain": it is offered as Needs review. A value typed by the staff member is never replaced.
    /// </summary>
    public static class CaptureReader
    {
        static MlKitBarcodeReader barcodeReader;
        static MlKitTextReader textReader;
        public static bool CanReadBarcodes => barcodeReader != null || MlKitBarcodeReader.TryCreate(out barcodeReader);
        public static bool CanReadText => textReader != null || MlKitTextReader.TryCreate(out textReader);

        public static Task<string> Barcode(Texture2D picture)
        {
            var done = new TaskCompletionSource<string>();
            if (picture == null || !CanReadBarcodes) { done.SetResult(""); return done.Task; }
            barcodeReader.Read(picture, code => done.TrySetResult(code ?? ""));
            return done.Task;
        }

        public static Task<string> Text(Texture2D picture)
        {
            var done = new TaskCompletionSource<string>();
            if (picture == null || !CanReadText) { done.SetResult(""); return done.Task; }
            textReader.Read(picture, (text, boxes, problem) => done.TrySetResult(text ?? ""));
            return done.Task;
        }

        /// <summary>A barcode was read or typed: look it up. Returns a short line for the status text.</summary>
        public static async Task<string> FromBarcode(CaptureDraft d, string code, bool scanned)
        {
            string digits = OpenBeautyFacts.Digits(code);
            if (digits.Length == 0) return "That is not a product barcode (8 to 14 digits).";
            // the digits are kept exactly as read, leading zeros included
            d.Offer("barcode", new string(Array.FindAll(code.ToCharArray(), char.IsDigit)), scanned ? "Barcode scan" : "Typed barcode", "barcode", false);

            ProductData listed = ProductRepository.Every.ByBarcode(digits);
            string said = "";
            if (listed != null)
            {
                d.duplicateOf = listed.id; d.duplicateName = listed.name;
                d.Offer("name", listed.name, "GlowUp catalogue", "lookup", false);
                d.Offer("brand", listed.brandName, "GlowUp catalogue", "lookup", false);
                d.Offer("cosmeticType", listed.cosmeticType, "GlowUp catalogue", "lookup", false);
                said = $"Already in GlowUp as \"{listed.name}\". Submit only if this pack is a different version (size, shade, new formula).";
            }

            OpenBeautyFacts.Result off = await OpenBeautyFacts.Find(digits);
            if (off.answered && (off.found || off.productName.Length > 0))
            {
                d.Offer("name", off.productName, "Open Beauty Facts", "barcode", false);
                d.Offer("brand", off.brandName, "Open Beauty Facts", "barcode", false);
                if (off.ingredientText.Length > 0) TakeIngredients(d, off.ingredientText, "Open Beauty Facts", "barcode", false);
                if (said.Length == 0) said = "Found on Open Beauty Facts. Check each value against the pack.";
            }
            else if (said.Length == 0)
                said = off.answered ? "This barcode is not known yet. Take photos of the front and the ingredient list, or type the details." :
                                      "The barcode is kept. The online look-up did not answer: take photos or type the details.";
            d.Save();
            return said;
        }

        /// <summary>Text read from one photo, used according to what the photo shows.</summary>
        public static string FromText(CaptureDraft d, string role, string text)
        {
            text = text ?? "";
            if (text.Trim().Length == 0) return "No text could be read from that photo.";
            var said = new List<string>();
            bool ingredientPhoto = role == "Ingredients" || role == "Back" || Regex.IsMatch(text, @"\b(ingredients?|ingr[eé]dients|composition|inci)\b", RegexOptions.IgnoreCase);
            if (ingredientPhoto && TakeIngredients(d, text, "Photo of the label (text reading)", "ocr", true)) said.Add("ingredient list");

            // brand: a brand of the catalogue whose name is printed on the pack
            if (!d.Get("brand").Has)
                foreach (BrandData b in BrandRepository.All())
                {
                    if (b == null || b.binned || string.IsNullOrEmpty(b.name) || b.name.Length < 3) continue;
                    if (Regex.IsMatch(text, @"(^|\W)" + Regex.Escape(b.name) + @"($|\W)", RegexOptions.IgnoreCase))
                    { d.Offer("brand", b.name, "Photo of the pack (text reading)", "ocr", true); said.Add("brand"); break; }
                }

            string type = ProductScope.MakeupType("", text);
            if (!string.IsNullOrEmpty(type) && d.Offer("cosmeticType", type, "Photo of the pack (text reading)", "ocr", true)) said.Add("category");

            if (role == "Front" && !d.Get("name").Has)
            {
                string brand = d.Get("brand").value;
                foreach (string line in text.Split('\n'))
                {
                    string l = line.Trim();
                    if (l.Length < 4 || l.Length > 60 || !Regex.IsMatch(l, "[A-Za-z]{3}")) continue;
                    if (brand.Length > 0 && l.IndexOf(brand, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    d.Offer("name", l, "Photo of the front (text reading)", "ocr", true); said.Add("name"); break;
                }
            }
            if (role == "Shade" || role == "Front")
            {
                Match m = Regex.Match(text.ToUpperInvariant(), @"(?<![A-Z0-9])([A-Z]{0,2}\d{1,3}[A-Z]?)(?![A-Z0-9])");
                if (m.Success && role == "Shade" && d.Offer("shade", m.Groups[1].Value, "Photo of the shade label (text reading)", "ocr", true)) said.Add("shade code");
            }
            d.Save();
            return said.Count > 0 ? "Read: " + string.Join(", ", said) + ". Check them against the pack." : "Text was read, but no product details were recognised in it.";
        }

        /// <summary>The ingredient list in the order printed. The text exactly as read is kept next to it for the reviewer.</summary>
        static bool TakeIngredients(CaptureDraft d, string text, string from, string method, bool uncertain)
        {
            IngredientTextParser.ParsedList read = IngredientTextParser.Read(text);
            List<string> names = IngredientTextParser.Parse(text);
            if (names == null || names.Count < 2) return false;
            bool weak = uncertain || read.unrecognized.Count > read.ingredients.Count;
            if (!d.Offer("ingredients", string.Join(", ", names), from, method, weak)) return false;
            d.rawText = CaptureDraft.Limit(text, 4000); d.rawFrom = from;
            return true;
        }
    }
}
