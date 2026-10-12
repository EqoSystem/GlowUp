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
    /// The app only USES a product that is make-up, Active, not in the Recycle bin, and whose brand is Active.
    ///
    /// Keeping the copy up to date: the list is not downloaded again and again. A copy that was checked a few minutes
    /// ago is used as it is; otherwise only the entries that changed are downloaded (see CatalogSync).
    /// Finding a product: through the look-up tables <see cref="Index"/> (the products the app may use) and
    /// <see cref="Every"/> (every product the database holds, so nothing is added twice).
    /// </summary>
    public static class ProductRepository
    {
        const string CacheKey = "products";
        static ProductCacheHolder holder;
        static float lastCheck = -1f;             // Time.realtimeSinceStartup of the last look at the database that got an answer
        static Task running;                      // one look at a time: whoever asks meanwhile waits for the same one
        static List<ProductData> usable;
        static ProductIndex usableIndex, everyIndex;

        /// <summary>
        /// The products the app may use (make-up, Active, brand Active), from the copy on the phone. When that copy has not
        /// been checked for a while and the phone is online, what changed is downloaded first.
        /// </summary>
        /// <param name="everything">download the whole list now, whatever its age (Settings > Download now)</param>
        public static async Task<List<ProductData>> LoadActive(bool everything = false)
        {
            FromPhone();
            await Refresh(everything);
            await BrandRepository.LoadActive(everything);          // a product is only offered while its brand is Active on the website
            return Usable();
        }

        /// <summary>The products the app may use, from what is on the phone right now (no download). Call LoadActive() once first.</summary>
        public static List<ProductData> Usable()
        {
            FromPhone();
            if (usable != null) return usable;
            var list = new List<ProductData>();
            if (holder != null)
                foreach (ProductData p in holder.items)
                {
                    if (p.binned) continue;                                // in the website's Recycle bin
                    if (!string.Equals((p.status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase)) continue;
                    if (BrandRepository.IsSwitchedOff(p.brandId)) continue;
                    // GlowUp is about MAKE-UP ONLY: skin care, hair, body, nail and fragrance products are never shown or
                    // recommended, and neither is a product whose kind is unclear (it waits for an admin to look at it)
                    if (!GlowUp.Scan.ProductScope.IsMakeup(p.cosmeticType, p.name)) continue;
                    list.Add(p);
                }
            return usable = list;
        }

        /// <summary>The look-up table of the products the app may use: by barcode, by name, by alias, by word.</summary>
        public static ProductIndex Index => usableIndex ?? (usableIndex = ProductIndex.Build(Usable(), LocalAliases.All()));

        /// <summary>
        /// The look-up table of EVERY product the phone knows of, whatever its status (waiting for review, switched off, in
        /// the Recycle bin, not make-up). Used so that a product is never added a second time.
        /// </summary>
        public static ProductIndex Every => everyIndex ?? (everyIndex = ProductIndex.Build(All()));

        /// <summary>Every product the phone knows of, whatever its status (call LoadActive() first).</summary>
        public static List<ProductData> All() { FromPhone(); return holder != null ? holder.items : new List<ProductData>(); }

        /// <summary>How the copy on the phone stands: for the Settings page.</summary>
        public static CatalogSync.State SyncState { get { FromPhone(); return holder != null && holder.sync != null ? holder.sync : new CatalogSync.State(); } }

        static void FromPhone()
        {
            if (holder != null) return;
            if (OfflineCache.TryLoad(CacheKey, out ProductCacheHolder saved) && saved != null)
            {
                holder = saved;
                if (holder.items == null) holder.items = new List<ProductData>();
                if (holder.sync == null) holder.sync = new CatalogSync.State();
            }
        }

        static void Changed() { usable = null; usableIndex = null; everyIndex = null; }

        /// <summary>Called by LocalAliases when this phone learned another name for a product: the look-up table is made anew.</summary>
        public static void AliasesChanged() { usableIndex = null; }

        /// <summary>Called by BrandRepository when a brand was switched on or off: the usable products change with it.</summary>
        public static void BrandsChanged() { usable = null; usableIndex = null; }

        static async Task Refresh(bool everything)
        {
            if (!everything && holder != null && CatalogSync.IsFresh(lastCheck)) return;
            if (running != null)
            {
                try { await running; } catch (Exception) { }
                if (!everything) return;
            }
            running = Sync(everything);
            try { await running; } finally { running = null; }
        }

        static async Task Sync(bool everything)
        {
            if (!ConnectivityMonitor.Reachable || !await FirebaseBootstrap.Ready) return;
            try
            {
                bool haveCopy = holder != null && holder.items.Count > 0;
                CatalogSync.State state = holder != null && holder.sync != null ? holder.sync.Clone() : new CatalogSync.State();
                CatalogSync.Pull pull = await CatalogSync.Fetch("products", state, haveCopy, everything, "Downloading the product list");
                if (!pull.happened) return;

                var next = new ProductCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync = state };
                var arrived = new HashSet<string>();
                if (!pull.full && holder != null) next.items.AddRange(holder.items);
                foreach (DocumentSnapshot document in pull.documents)
                {
                    arrived.Add(document.Id);
                    next.items.RemoveAll(p => p.id == document.Id);              // the new version replaces the old one
                    ProductData product = Read(document);
                    if (product != null) next.items.Add(product);
                }
                // a product this phone has just added from a scan, still on its way to the database, stays known
                if (pull.full && holder != null)
                    foreach (ProductData mine in holder.items)
                        if (mine.source == "scan" && !arrived.Contains(mine.id) && SyncQueue.HasPending("products/" + mine.id)) next.items.Add(mine);

                holder = next;
                lastCheck = Time.realtimeSinceStartup;
                OfflineCache.Save(CacheKey, holder);
                Changed();
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Product sync failed, using the saved copy: " + e.Message); }
        }

        /// <summary>One product document. Null for a document without a name.</summary>
        static ProductData Read(DocumentSnapshot document)
        {
            var f = new FirestoreFields(document);
            string name = f.Str("name", f.Str("productName"));
            if (name.Length == 0) return null;
            return new ProductData
            {
                id = f.Id, name = name, brandId = f.Str("brandId"), brandName = f.Str("brandName"),
                cosmeticType = f.Str("cosmeticType", f.Str("category")), description = f.Str("description"),
                status = f.Str("status", "Active"), minAge = f.Int("minAge"), scans = f.Long("scans"),
                source = f.Str("source"), reviewed = f.Bool("reviewed"), barcode = f.Str("barcode"),
                ingredients = f.List("ingredients"), shades = f.List("shades"), sources = f.List("sources"), fdaNumber = f.Str("fdaNumber"),
                binned = f.Bool("binned"), aliases = f.List("aliases")
            };
        }

        /// <summary>Remembers a product this phone has just added from a scan, so the next scan of it is recognised.</summary>
        public static void AddLocal(ProductData product)
        {
            if (product == null) return;
            FromPhone();
            if (holder == null) holder = new ProductCacheHolder();
            holder.items.RemoveAll(p => p.id == product.id);
            holder.items.Add(product);
            OfflineCache.Save(CacheKey, holder);
            Changed();
        }

        /// <summary>Adds 1 to the product's scan counter (the "App Scans" column on the admin website).</summary>
        public static void RecordScan(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            SyncQueue.EnqueueIncrement("products/" + productId, "scans", 1);
        }
    }
}
