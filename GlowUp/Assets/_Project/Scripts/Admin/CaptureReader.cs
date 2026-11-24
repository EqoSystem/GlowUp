using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Net;
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
                // the same rule as the user's live scan (ProductScope.Judge): a product that is clearly not make-up is stopped, with the same words
                ProductScope.Verdict verdict = ProductScope.Judge("", off.productName);
                if (verdict.IsNotMakeup) { d.Save(); return ProductScope.StopMessage(verdict) + " Nothing was taken from this barcode."; }
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

        /// <summary>The least certainty at which an online match is trusted (the same idea as the user's scan: a wrong product is worse than none).</summary>
        const float MinimumWebConfidence = 0.6f;

        /// <summary>
        /// "Find missing details" on Review &amp; Submit (24 Nov 2026): the photo had the name (maybe the brand) but no ingredient list. The AI web search
        /// (the same one the user's scan uses, with its limits) looks for THIS product by name and brand and brings back the brand, the category and the
        /// ingredient list as a credible page publishes them. Every value comes in as "Needs review" with the page it came from; nothing the staff
        /// member typed or confirmed is replaced. A barcode or a shade code is never invented: most local products have neither online, so those are
        /// read from the pack (Barcode / Shade photo) or typed. Returns one calm line for the status.
        /// </summary>
        public static async Task<string> FillMissing(CaptureDraft d)
        {
            CaptureDraft.Field name = d.Get("name"), brand = d.Get("brand"), kind = d.Get("cosmeticType"), list = d.Get("ingredients");
            if (!name.Has && !brand.Has) return "Type the product name first (or take a photo of the front), then try again.";
            if (list.Has && brand.Has && kind.Has) return "The name, brand, category and ingredients are filled. A barcode or shade code is read from the pack, not found online.";
            if (!ConnectivityMonitor.Online) return "No connection. Try again when the phone is online.";
            if (!ProductAiAdvisor.Available) return "The online look-up is not set up on this phone.";
            if (!AiBudget.TakeLookUp()) return "The online look-up limit is reached for now. Try again later.";

            ProductAiAdvisor.WebMatch match = await ProductAiAdvisor.FindTyped(name.value, brand.value, kind.value);
            if (match == null) return "The online look-up did not answer. Try again, or type the details.";
            if (match.notMakeup) return ProductScope.StopMessage(ProductScope.Judge(match.cosmeticType, match.productName)) + " Nothing was taken.";
            if (!match.same || match.confidence < MinimumWebConfidence)
                return match.question.Length > 0 ? match.question : "The web did not show this exact product, so nothing was filled. Add the brand or take a photo of the ingredient list.";

            string page = match.list != null && match.list.sources != null && match.list.sources.Count > 0 ? match.list.sources[0] : match.page;
            string title = (page ?? "").Split('|')[0].Trim();
            string from = "Web search" + (title.Length > 0 ? ": " + title : "");
            var filled = new List<string>();
            if (!brand.Has && d.Offer("brand", match.brandName, from, "web", true)) filled.Add("brand");
            if (!kind.Has && d.Offer("cosmeticType", match.cosmeticType, from, "web", true)) filled.Add("category");
            if (!list.Has && match.list != null && match.list.found && match.list.ingredients.Count >= 2
                && TakeIngredients(d, string.Join(", ", match.list.ingredients), from, "web", true)) filled.Add("ingredient list");
            d.Save();
            return filled.Count > 0 ? "Found online: " + string.Join(", ", filled) + ". Check each against the pack." : "The product was found, but the page published no ingredient list. Take a photo of the list, or type it.";
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
