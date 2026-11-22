using System;
using System.Collections.Generic;
using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// ONLINE. When a scanned product is not in the website's Brand &amp; Product Inventory, this adds it:
    /// the ingredient list exactly as read from the label, plus the name, brand and type that are known
    /// (from the label or from the AI). If its brand is new, the brand is added as well.
    ///
    /// Everything it adds has status "Pending", source "scan" and "not reviewed", so:
    ///   - it shows on the website at once, marked as added by a scan and waiting for review;
    ///   - the app does NOT recommend it to anyone until an admin approves it there.
    /// A product that is already listed (also while still Pending, switched off or in the Recycle bin) is
    /// NOT added twice: the scan is counted on the existing entry instead. It is looked for by its barcode,
    /// by its brand and name (capitals, accents and punctuation do not matter; aliases count), and by its
    /// ingredient list. A new entry gets an id made from its brand and name ("scan_maybelline__superstay_matte_ink"),
    /// so even two phones scanning the same new product at the same moment end up on ONE entry.
    /// GlowUp lists MAKE-UP ONLY. A product is added only when it is clearly make-up AND its kind is one of the
    /// make-up kinds the app knows (ProductScope.MakeupType). Skin care, hair, body, nail and fragrance products
    /// are never added, and neither is a product whose kind is unclear or was only guessed from the label text.
    /// Settings are Inspector fields on LoadingSceneController.
    /// </summary>
    public static class ScannedProductPublisher
    {
        public static bool Enabled = true;
        /// <summary>Only add a product whose name is known. Off = nameless scans are added too, as "Unnamed ...".</summary>
        public static bool RequireProductName = true;

        /// <summary>
        /// Makes sure the scanned product is in the inventory. Sets record.productId to the inventory entry
        /// (existing or newly added) so the caller can count the scan. A looked-up web list for an existing
        /// product without ingredients is saved as a pending website suggestion rather than overwriting its entry.
        /// </summary>
        /// <param name="kindIsKnown">false when the product's kind was only guessed from the label text: such a product is not added</param>
        public static void PublishIfNew(ScanRecord record, List<string> labelIngredients, bool kindIsKnown = true)
        {
            if (!Enabled || record == null) return;
            bool named = !string.IsNullOrWhiteSpace(record.productName);

            // ---- already listed (also when it is not Active: waiting for review, switched off, in the Recycle bin)? ----
            // Then the scan is counted there and nothing is added. Looked for by barcode, by brand and name, by ingredients.
            ProductIndex every = ProductRepository.Every;
            ProductData listed = !string.IsNullOrEmpty(record.productId) ? every.ById(record.productId) : null;
            if (listed == null) listed = every.ByBarcode(record.barcode);
            if (listed == null && named) listed = every.Exact(record.brandName, record.productName);
            if (listed == null && named) listed = every.ById(ProductIndex.ScanId(record.brandName, record.productName));
            if (listed == null && labelIngredients != null && labelIngredients.Count >= Mathf.Max(1, ProductMatcher.MinimumShared))
                listed = ProductMatcher.Find(labelIngredients, ProductRepository.All(), null);
            if (listed != null)
            {
                record.productId = listed.id;
                if ((listed.ingredients == null || listed.ingredients.Count == 0)
                    && labelIngredients != null && labelIngredients.Count >= Mathf.Max(3, ProductMatcher.MinimumShared)
                    && record.listFrom == "web" && record.listWebBacked)
                {
                    QueueIngredientSuggestion(record, listed, labelIngredients);
                }
                return;
            }
            if (!string.IsNullOrEmpty(record.productId)) return;

            if (labelIngredients == null || labelIngredients.Count < Mathf.Max(1, ProductMatcher.MinimumShared)) return;   // too short a list to add
            string uid = UserProfileRepository.CurrentUid;
            if (!named && RequireProductName) return;
            // MAKE-UP ONLY: the inventory takes a product only when it is clearly make-up and of a kind the app knows.
            // A kind that was merely guessed from the label text counts only when the product's own name says it too.
            string type = ProductScope.MakeupType(kindIsKnown ? record.cosmeticType : "", record.productName);
            if (type.Length == 0) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");     // the inventory entry is named after the scan
            if (AdminAccount.IsAdmin && named && PublishAsAdmin(record, labelIngredients, type, uid, now)) return;

            // ---- the brand ----
            string brandId = record.brandId ?? "", brandName = (record.brandName ?? "").Trim();
            if (brandId.Length == 0 && brandName.Length >= 2)
            {
                BrandData known = BrandRepository.FindByName(brandName);
                if (known != null) { brandId = known.id; brandName = known.name; }
                else
                {
                    brandId = ProductIndex.ScanBrandId(brandName);          // always the same id for the same brand name
                    var brand = new ScannedBrandRecord
                    {
                        name = Limit(brandName, 60), category = "Makeup", market = "",
                        status = "Pending", scans = 0, source = "scan", reviewed = false, addedBy = uid, addedAtUnix = now
                    };
                    if (!string.IsNullOrEmpty(uid)) SyncQueue.EnqueueSet("brands/" + brandId, brand);
                    BrandRepository.AddLocal(new BrandData { id = brandId, name = brand.name, category = brand.category, market = "", status = "Pending" });
                }
                record.brandId = brandId;
            }

            // ---- the product ----
            string name = named ? record.productName.Trim() : "Unnamed " + type.ToLowerInvariant() + " (scanned)";
            var shades = new List<string>();
            if (!string.IsNullOrWhiteSpace(record.shadeHex) && ShadeAdvisor.IsColourCosmetic(type)) shades.Add(record.shadeHex.Trim());

            var ingredients = new List<string>();
            foreach (string ingredient in labelIngredients)
                if (!string.IsNullOrWhiteSpace(ingredient) && ingredients.Count < 80) ingredients.Add(Limit(ingredient.Trim(), 120));

            // the id is made from the brand and the name, so the same product always lands on the same entry
            // (a product without a name keeps an id of its own)
            string productId = named ? ProductIndex.ScanId(brandName, name) : "scan_" + record.id;
            if (productId.Length == 0) productId = "scan_" + record.id;
            var product = new ScannedProductRecord
            {
                name = Limit(name, 100), brandId = brandId, brandName = Limit(brandName, 60), cosmeticType = Limit(type, 40),
                description = Limit((record.aiEnhanced ? "Added automatically from an app scan. Name and type suggested by the AI." : "Added automatically from an app scan.")
                    + FdaNote(record), 400),
                ingredients = ingredients, shades = shades, minAge = 0,
                sources = SourcesFor(record),
                status = "Pending", scans = 0, source = "scan", reviewed = false, addedBy = uid, addedAtUnix = now
            };
            // the barcode the camera saw goes with it, so the next scan of this product is found by its barcode
            string barcode = MlKitBarcodeReader.Key(record.barcode).Length > 0 ? Limit(record.barcode.Trim(), 20) : "";
            if (!string.IsNullOrEmpty(uid))
            {
                if (barcode.Length > 0) SyncQueue.EnqueueSet("products/" + productId, ScannedProductWithBarcodeRecord.From(product, barcode));
                else SyncQueue.EnqueueSet("products/" + productId, product);
            }
            ProductRepository.AddLocal(new ProductData
            {
                id = productId, name = product.name, brandId = brandId, brandName = product.brandName, cosmeticType = product.cosmeticType,
                description = product.description, status = "Pending", ingredients = new List<string>(ingredients), shades = new List<string>(shades),
                sources = new List<string>(product.sources), source = "scan", reviewed = false, barcode = barcode
            });
            record.productId = productId;
        }

        /// <summary>
        /// Quick registration (an admin signed in to the app): the scanned product goes to the catalogue now, with everything the user's
        /// skin profile is matched against. It is Active and reviewed when it is complete (brand and name known, a printed make-up kind,
        /// at least 4 ingredients read from the pack or taken from the catalogue); anything less is saved Pending for the website's
        /// review queue. Returns false when it cannot (the normal Pending path then runs).
        /// </summary>
        static bool PublishAsAdmin(ScanRecord record, List<string> labelIngredients, string type, string uid, long now)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            string name = record.productName.Trim(), brandName = (record.brandName ?? "").Trim();
            var ingredients = new List<string>();
            foreach (string ingredient in labelIngredients)
                if (!string.IsNullOrWhiteSpace(ingredient) && ingredients.Count < 80) ingredients.Add(Limit(ingredient.Trim(), 120));

            bool fromPack = record.listFrom == "pack" || record.listFrom == "inventory";
            bool complete = brandName.Length >= 2 && ingredients.Count >= 4 && fromPack;
            string status = complete ? "Active" : "Pending";

            // ---- the brand: existing, or new (Active and reviewed when the product is complete) ----
            string brandId = record.brandId ?? "";
            if (brandName.Length >= 2)
            {
                BrandData known = BrandRepository.FindByName(brandName);
                if (known != null) { brandId = known.id; brandName = known.name; }
                else
                {
                    brandId = ProductIndex.ScanBrandId(brandName);
                    SyncQueue.EnqueueSet("brands/" + brandId, new AdminScannedBrandRecord
                    {
                        name = Limit(brandName, 60), status = status, reviewed = complete, reviewedBy = complete ? uid : "",
                        reviewedAtUnix = complete ? now : 0, addedBy = uid, addedAtUnix = now
                    });
                    BrandRepository.AddLocal(new BrandData { id = brandId, name = Limit(brandName, 60), category = "Makeup", market = "Global", status = status });
                }
                record.brandId = brandId;
            }

            var shades = new List<string>();
            if (!string.IsNullOrWhiteSpace(record.shadeHex) && ShadeAdvisor.IsColourCosmetic(type)) shades.Add(record.shadeHex.Trim());

            // ids into the ingredient library: the app finds each ingredient's rating, allergen family and skin-type notes through them
            var ids = new List<string>();
            foreach (string ingredient in ingredients)
            {
                IngredientData data = IngredientRepository.Find(ingredient);
                if (data != null && !string.IsNullOrEmpty(data.id) && !ids.Contains(data.id) && ids.Count < 300) ids.Add(data.id);
            }
            string barcode = MlKitBarcodeReader.Key(record.barcode).Length > 0 ? Limit(record.barcode.Trim(), 20) : "";
            var tags = new List<string> { Slug(type) };
            if (brandName.Length > 0) tags.Add(Slug(brandName));
            if (shades.Count > 0) tags.Add("has-shades");
            if (record.fdaChecked && !string.IsNullOrEmpty(record.fdaNumber)) tags.Add("fda-listed");
            tags.RemoveAll(t => string.IsNullOrEmpty(t));

            string productId = ProductIndex.ScanId(brandName, name);
            if (productId.Length == 0) productId = "scan_" + record.id;
            var product = new AdminScannedProductRecord
            {
                name = Limit(name, 100), brandId = brandId, brandName = Limit(brandName, 60), cosmeticType = Limit(type, 40), barcode = barcode,
                description = Limit("Registered from Smart Scan by an admin." + FdaNote(record), 400),
                ingredients = ingredients, ingredientIds = ids, ingredientCount = ingredients.Count, shades = shades, sources = SourcesFor(record), tags = tags,
                status = status, reviewed = complete, reviewedBy = complete ? uid : "", reviewedAtUnix = complete ? now : 0, addedBy = uid, addedAtUnix = now
            };
            SyncQueue.EnqueueSet("products/" + productId, product);       // sent at once while online; kept and sent later when offline
            ProductRepository.AddLocal(new ProductData
            {
                id = productId, name = product.name, brandId = brandId, brandName = product.brandName, cosmeticType = product.cosmeticType,
                description = product.description, status = status, ingredients = new List<string>(ingredients), shades = new List<string>(shades),
                sources = new List<string>(product.sources), source = "admin", reviewed = complete, barcode = barcode
            });
            record.productId = productId;
            FormalToast.ShowAfterNextScene(FormalToast.Kind.Info, $"Sending {product.name} to the catalogue...");
            WatchDelivery("products/" + productId, product.name, complete);
            return true;
        }

        /// <summary>
        /// Real time: the toast says "registered" only when the database has really taken the write (the queue no longer holds it),
        /// and says so plainly when it is still waiting (no connection) or was refused. The website's inventory is a live listener, so the
        /// product appears there by itself the moment the write lands.
        /// </summary>
        static async void WatchDelivery(string path, string name, bool complete)
        {
            const float waitSeconds = 25f;
            float until = Time.realtimeSinceStartup + waitSeconds;
            while (SyncQueue.HasPending(path) && Time.realtimeSinceStartup < until) await System.Threading.Tasks.Task.Delay(400);
            FormalToast toast = FormalToast.Current;
            if (toast == null) return;
            if (!SyncQueue.HasPending(path))
                toast.Show(complete ? FormalToast.Kind.Success : FormalToast.Kind.Info,
                    complete ? $"Registered in the catalogue: {name}. It is live for users." : $"Saved as Pending: {name}. It needs a review on the website (brand, or a list read from the pack).");
            else
                toast.Show(FormalToast.Kind.Warning, $"{name} is saved on this phone and goes to the catalogue as soon as the connection allows.");
        }

        static string Slug(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '-');
            string s = sb.ToString().Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Length > 30 ? s.Substring(0, 30).Trim('-') : s;
        }

        static void QueueIngredientSuggestion(ScanRecord record, ProductData listed, List<string> labelIngredients)
        {
            string uid = UserProfileRepository.CurrentUid;
            if (string.IsNullOrEmpty(uid)) return;

            var ingredients = new List<string>();
            foreach (string ingredient in labelIngredients)
                if (!string.IsNullOrWhiteSpace(ingredient) && ingredients.Count < 80) ingredients.Add(Limit(ingredient.Trim(), 120));
            if (ingredients.Count < 3) return;

            var sources = new List<string>();
            foreach (string source in record.ingredientSources ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(source) && sources.Count < 10) sources.Add(Limit(source.Trim(), 500));
            if (sources.Count == 0) return;

            var suggestion = new ProductIngredientSuggestionRecord
            {
                productId = listed.id,
                productName = Limit(listed.name, 100),
                brandName = Limit(listed.brandName, 60),
                ingredients = ingredients,
                sources = sources,
                addedBy = uid,
                addedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            SyncQueue.EnqueueSet("productIngredientSuggestions/" + Guid.NewGuid().ToString("N"), suggestion);
        }

        static List<string> SourcesFor(ScanRecord record)
        {
            var sources = new List<string>();
            foreach (string source in record.ingredientSources ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(source) && sources.Count < 10) sources.Add(Limit(source.Trim(), 500));
            return sources;
        }

        /// <summary>
        /// What the description says about the FDA Philippines list. Only a record that is clearly this product is quoted
        /// (the website approves an entry that carries such a line by itself); a record that is merely similar is not.
        /// </summary>
        static string FdaNote(ScanRecord record)
        {
            switch (PhFdaVerification.StatusOf(record))
            {
                case PhFdaVerification.Registered:
                case PhFdaVerification.Expired: return string.IsNullOrEmpty(record.fdaLine) ? "" : " " + record.fdaLine;
                case PhFdaVerification.Similar: return " A similar record is on the FDA Philippines list: check it by hand.";
                case PhFdaVerification.None: return " Not found on the FDA Philippines list under this name.";
                default: return "";
            }
        }

        static string Limit(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max).TrimEnd();
    }
}
