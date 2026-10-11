using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Catalogue products ('products' collection, managed on the admin website's Product Catalog page).
    /// Document fields: name, brandId, brandName, cosmeticType (or category), description, status,
    /// minAge, scans, ingredients (array of text), shades (array of hex colours).
    /// Cached on the device so recommendations and alternatives also work offline.
    /// </summary>
    public static class ProductRepository
    {
        const string CacheKey = "products";
        static ProductCacheHolder holder;

        public static async Task<List<ProductData>> LoadActive()
        {
            if (holder == null && OfflineCache.TryLoad(CacheKey, out ProductCacheHolder c)) holder = c;

            if (ConnectivityMonitor.Reachable && await FirebaseBootstrap.Ready)
            {
                try
                {
                    var snap = await FirebaseFirestore.DefaultInstance.Collection("products").GetSnapshotAsync()
                        .OrTimeout(holder != null ? 8f : 25f, "Downloading the product list");
                    TaskLimits.RefuseStale(snap, holder != null);
                    var h = new ProductCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    foreach (var doc in snap.Documents)
                    {
                        var f = new FirestoreFields(doc);
                        string name = f.Str("name", f.Str("productName"));
                        if (name.Length == 0) continue;
                        if (f.Bool("binned")) continue;                  // in the website's Recycle bin
                        h.items.Add(new ProductData
                        {
                            id = f.Id, name = name, brandId = f.Str("brandId"), brandName = f.Str("brandName"),
                            cosmeticType = f.Str("cosmeticType", f.Str("category")), description = f.Str("description"),
                            status = f.Str("status", "Active"), minAge = f.Int("minAge"), scans = f.Long("scans"),
                            source = f.Str("source"), reviewed = f.Bool("reviewed"), barcode = f.Str("barcode"),
                            ingredients = f.List("ingredients"), shades = f.List("shades"), sources = f.List("sources"), fdaNumber = f.Str("fdaNumber")
                        });
                    }
                    holder = h;
                    OfflineCache.Save(CacheKey, h);
                }
                catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Product sync failed, using cache: " + e.Message); }
            }

            await BrandRepository.LoadActive();          // a product is only offered while its brand is Active on the website
            var result = new List<ProductData>();
            if (holder != null)
                foreach (var p in holder.items)
                {
                    if (!string.Equals((p.status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase)) continue;
                    if (BrandRepository.IsSwitchedOff(p.brandId)) continue;
                    // GlowUp is about make-up and face products: a hair, body, nail or fragrance product is never recommended
                    if (GlowUp.Scan.ProductScope.Classify(p.cosmeticType, p.name) == GlowUp.Scan.ProductScope.Kind.OutOfScope) continue;
                    result.Add(p);
                }
            return result;
        }

        /// <summary>Every product the phone knows of, whatever its status (call LoadActive() first). Used to avoid adding a product twice.</summary>
        public static List<ProductData> All() => holder != null ? holder.items : new List<ProductData>();

        /// <summary>Remembers a product this phone has just added from a scan, so the next scan of it is recognised.</summary>
        public static void AddLocal(ProductData product)
        {
            if (product == null) return;
            if (holder == null) holder = new ProductCacheHolder();
            holder.items.RemoveAll(p => p.id == product.id);
            holder.items.Add(product);
            OfflineCache.Save(CacheKey, holder);
        }

        /// <summary>Adds 1 to the product's scan counter (the "App Scans" column on the admin website).</summary>
        public static void RecordScan(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            SyncQueue.EnqueueIncrement("products/" + productId, "scans", 1);
        }
    }
}
