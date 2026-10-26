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
    /// Reads both the legacy flat fields and the nested product_identification / official_ingredient_list /
    /// key_ingredient_breakdown schema. Live scans resolve brand + name asynchronously against this same collection.
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

        // ---- server look-ups, kept for the session (cleared when the catalogue changes, and by Settings > Download now)
        const int BrandQueryLimit = 100;                  // the largest brand in the Oct 12 backup had 35 products
        const int BrandPageCap = 5;                       // at most 5 pages of BrandQueryLimit per query
        const float NotFoundMinutes = 3f;                 // a "not found" answer is trusted this long, and then asked again

        /// <summary>One answer of the server: the product, or null = it was not found (then `at` says when).</summary>
        class Looked { public ProductData product; public float at; }
        class OfBrand { public List<ProductData> products; public float at; }

        static readonly Dictionary<string, Looked> lookupCache = new Dictionary<string, Looked>();                  // "brand|name" -> answer
        static readonly Dictionary<string, OfBrand> brandCache = new Dictionary<string, OfBrand>();                 // brand -> its usable products, fetched once
        static readonly Dictionary<string, Task<List<ProductData>>> brandFetching = new Dictionary<string, Task<List<ProductData>>>();

        static void ClearLookups() { lookupCache.Clear(); brandCache.Clear(); }
        static string LookupKey(string brandName, string productName) => TextKey.Normalize(brandName) + "|" + TextKey.Normalize(productName);

        /// <summary>
        /// The products the app may use (make-up, Active, brand Active), from the copy on the phone. When that copy has not
        /// been checked for a while and the phone is online, what changed is downloaded first.
        /// </summary>
        /// <param name="everything">download the whole list now, whatever its age (Settings > Download now)</param>
        public static async Task<List<ProductData>> LoadActive(bool everything = false)
        {
            if (everything) ClearLookups();                        // Settings > Download now: nothing asked earlier is trusted any more
            FromPhone();
            await Refresh(everything);
            await BrandRepository.LoadActive(everything);          // a product is only offered while its brand is Active on the website
            return Usable();
        }

        /// <summary>The products the app may use, from what is on the phone right now (no download). Call LoadActive() once first.
        /// The website mirrors this rule in productVisibility() (GlowUp-Admin_AI_Testing/_Shared/glowupCatalog.js): change one, change the other.</summary>
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
        public static void BrandsChanged() { usable = null; usableIndex = null; ClearLookups(); }

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
                ClearLookups();                 // the catalogue just changed: earlier answers (also "not found") may be out of date
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Product sync failed, using the saved copy: " + e.Message); }
        }

        /// <summary>
        /// Queries the existing Firebase products collection for a product identified by its printed brand and name.
        /// The network requests are asynchronous and use the server so a cache miss is not mistaken for a missing product.
        /// </summary>
        public static async Task<ProductData> FindByBrandAndNameAsync(string brandName, string productName)
        {
            brandName = (brandName ?? "").Trim();
            productName = (productName ?? "").Trim();
            if (TextKey.Normalize(brandName).Length < 2 || productName.Length < 2) return null;       // nothing to ask the server with

            // asked before this session: a found product stays valid; "not found" is trusted for NotFoundMinutes
            string key = LookupKey(brandName, productName);
            bool retryAfterMiss = false;
            if (lookupCache.TryGetValue(key, out Looked earlier))
            {
                if (earlier.product != null || Time.realtimeSinceStartup - earlier.at < NotFoundMinutes * 60f) return earlier.product;
                retryAfterMiss = true;                              // the miss is old: ask the server again (with a fresh list of the brand)
            }
            if (!ConnectivityMonitor.Reachable) throw new InvalidOperationException("Firebase product lookup requires an internet connection.");
            if (!await FirebaseBootstrap.Ready) throw new InvalidOperationException("Firebase is not ready for product lookup.");

            List<ProductData> ofBrand = await ProductsOfBrand(brandName, retryAfterMiss);      // one fetch per brand, whatever the product name is
            List<ProductIndex.Candidate> found = ProductIndex.Build(ofBrand).Search(brandName, productName, 3);
            foreach (ProductIndex.Candidate candidate in found)
                if (candidate != null && candidate.product != null && (candidate.exactName || candidate.score >= ProductIndex.Sure))
                {
                    AddLocal(candidate.product);
                    lookupCache[key] = new Looked { product = candidate.product, at = Time.realtimeSinceStartup };
                    return candidate.product;
                }
            lookupCache[key] = new Looked { product = null, at = Time.realtimeSinceStartup };       // asked and not found: not asked again for a while
            return null;
        }

        /// <summary>The usable products of one brand, from the server: one fetch per brand (name variants and parallel callers share it).</summary>
        /// <param name="refresh">a "not found" answer for this brand has expired: a list older than NotFoundMinutes is fetched again</param>
        static async Task<List<ProductData>> ProductsOfBrand(string brandName, bool refresh)
        {
            string brandKey = TextKey.Normalize(brandName);
            if (brandCache.TryGetValue(brandKey, out OfBrand have) && !(refresh && Time.realtimeSinceStartup - have.at >= NotFoundMinutes * 60f)) return have.products;
            if (!brandFetching.TryGetValue(brandKey, out Task<List<ProductData>> task))
            {
                task = FetchBrand(brandName, brandKey);
                brandFetching[brandKey] = task;
            }
            try { return await task; }
            finally { brandFetching.Remove(brandKey); }
        }

        static async Task<List<ProductData>> FetchBrand(string brandName, string brandKey)
        {
            CollectionReference products = FirebaseFirestore.DefaultInstance.Collection("products");
            var brandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { brandName };
            BrandData knownBrand = BrandRepository.FindByName(brandName);
            if (knownBrand != null && !string.IsNullOrWhiteSpace(knownBrand.name)) brandNames.Add(knownBrand.name.Trim());
            var queries = new List<Task<List<DocumentSnapshot>>>();
            foreach (string name in brandNames)
            {
                queries.Add(AllPages(products.WhereEqualTo("brandName", name), "products of brand \"" + name + "\" (brandName)"));
                queries.Add(AllPages(products.WhereEqualTo("product_identification.brand", name), "products of brand \"" + name + "\" (product_identification.brand)"));
            }
            List<DocumentSnapshot>[] answers = await Task.WhenAll(queries).OrTimeout(12f, "Looking up this product in Firebase");

            var matches = new Dictionary<string, ProductData>(StringComparer.Ordinal);
            foreach (List<DocumentSnapshot> documents in answers)
                foreach (DocumentSnapshot document in documents)
                {
                    ProductData product = Read(document);
                    if (product == null || product.binned || !string.Equals(product.status, "Active", StringComparison.OrdinalIgnoreCase)
                        || BrandRepository.IsSwitchedOff(product.brandId)
                        || !ProductIndex.BrandsAgree(brandName, product.brandName)
                        || !GlowUp.Scan.ProductScope.IsMakeup(product.cosmeticType, product.name)) continue;
                    matches[product.id] = product;
                }
            var list = new List<ProductData>(matches.Values);
            brandCache[brandKey] = new OfBrand { products = list, at = Time.realtimeSinceStartup };
            return list;
        }

        /// <summary>One query in pages of BrandQueryLimit. A page of exactly the limit may have more behind it: the next page is fetched, up to BrandPageCap pages.</summary>
        static async Task<List<DocumentSnapshot>> AllPages(Query query, string label)
        {
            var all = new List<DocumentSnapshot>();
            Query page = query.Limit(BrandQueryLimit);
            for (int i = 0; i < BrandPageCap; i++)
            {
                QuerySnapshot snapshot = await page.GetSnapshotAsync(Source.Server);
                ReadCounter.AddQuery(label, snapshot.Count);
                foreach (DocumentSnapshot document in snapshot.Documents) all.Add(document);
                if (snapshot.Count < BrandQueryLimit) return all;
                if (i == BrandPageCap - 1)
                {
                    Debug.LogWarning("Product look-up: " + label + " still had more after " + BrandPageCap + " pages of " + BrandQueryLimit + "; the list may be incomplete.");
                    break;
                }
                Debug.LogWarning("Product look-up: " + label + " returned exactly " + BrandQueryLimit + " documents; fetching the next page.");
                page = query.StartAfter(all[all.Count - 1]).Limit(BrandQueryLimit);
            }
            return all;
        }

        /// <summary>One product document. Null for a document without a name.</summary>
        static ProductData Read(DocumentSnapshot document)
        {
            var f = new FirestoreFields(document);
            string name = f.NestedStr("product_identification", "product_name", f.Str("name", f.Str("productName")));
            if (name.Length == 0) return null;
            string brandName = f.NestedStr("product_identification", "brand", f.Str("brandName"));
            string type = f.NestedStr("product_identification", "cosmetic_type", f.Str("cosmeticType", f.Str("category")));
            string shade = f.NestedStr("product_identification", "shade_color");
            string ingredientsSource = f.NestedStr("official_ingredient_list", "source", f.Str("source"));
            List<string> ingredients = f.NestedList("official_ingredient_list", "ingredient_list");
            if (ingredients.Count == 0) ingredients = f.List("ingredients");
            List<string> shades = f.List("shades");
            List<string> sources = f.List("sources");
            if (ingredientsSource.Length > 0 && sources.Count == 0) sources.Add(ingredientsSource);
            return new ProductData
            {
                id = f.Id, name = name, brandId = f.Str("brandId"), brandName = brandName,
                cosmeticType = type, description = f.Str("description"),
                status = f.Str("status", "Active"), shadeColor = shade, minAge = f.Int("minAge"), scans = f.Long("scans"),
                source = f.Str("source"), officialIngredientSource = ingredientsSource, reviewed = f.Bool("reviewed"), barcode = f.Str("barcode"),
                ingredients = ingredients,
                skinCareActiveIngredients = f.NestedList("key_ingredient_breakdown", "skin_care_active_ingredients"),
                baseTextureFormulators = f.NestedList("key_ingredient_breakdown", "base_texture_formulators"),
                pigmentsPreservatives = f.NestedList("key_ingredient_breakdown", "pigments_preservatives"),
                shades = shades, shadeCodes = f.List("shadeCodes"), sources = sources, fdaNumber = f.Str("fdaNumber"),
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
            lookupCache.Remove(LookupKey(product.brandName, product.name));      // only this product's own answer; the brands' lists stay
        }

        /// <summary>Adds 1 to the product's scan counter (the "App Scans" column on the admin website).</summary>
        public static void RecordScan(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            SyncQueue.EnqueueIncrement("products/" + productId, "scans", 1);
        }
    }
}
