using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Brands managed in the admin website ('brands' collection). Only Active brands are returned by LoadActive().
    /// The copy on the phone is kept up to date the same way as the product list: a recent copy is used as it is,
    /// otherwise only the entries that changed are downloaded (see CatalogSync).
    /// </summary>
    public static class BrandRepository
    {
        const string CacheKey = "brands";
        static BrandCacheHolder holder;
        static float lastCheck = -1f;
        static Task running;
        static Dictionary<string, BrandData> byId;

        /// <param name="everything">download the whole list now, whatever its age (Settings > Download now)</param>
        public static async Task<List<BrandData>> LoadActive(bool everything = false)
        {
            FromPhone();
            await Refresh(everything);
            var result = new List<BrandData>();
            if (holder != null) foreach (BrandData b in holder.items) if (IsActive(b.status) && !b.binned) result.Add(b);
            return result;
        }

        /// <summary>Every brand the phone knows of, whatever its status (call LoadActive() first).</summary>
        public static List<BrandData> All() { FromPhone(); return holder != null ? holder.items : new List<BrandData>(); }

        /// <summary>How the copy on the phone stands: for the Settings page.</summary>
        public static CatalogSync.State SyncState { get { FromPhone(); return holder != null && holder.sync != null ? holder.sync : new CatalogSync.State(); } }

        static void FromPhone()
        {
            if (holder != null) return;
            if (OfflineCache.TryLoad(CacheKey, out BrandCacheHolder saved) && saved != null)
            {
                holder = saved;
                if (holder.items == null) holder.items = new List<BrandData>();
                if (holder.sync == null) holder.sync = new CatalogSync.State();
            }
        }

        static void Changed() { byId = null; ProductRepository.BrandsChanged(); }

        /// <summary>The brand document referenced by a product, including inactive or archived brands.</summary>
        public static BrandData FindById(string brandId)
        {
            if (string.IsNullOrEmpty(brandId)) return null;
            FromPhone();
            if (holder == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, BrandData>();
                foreach (BrandData b in holder.items) if (b != null && !string.IsNullOrEmpty(b.id)) byId[b.id] = b;
            }
            return byId.TryGetValue(brandId, out BrandData brand) ? brand : null;
        }

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
                CatalogSync.Pull pull = await CatalogSync.Fetch("brands", state, haveCopy, everything, "Downloading the brand list");
                if (!pull.happened) return;

                var next = new BrandCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync = state };
                var arrived = new HashSet<string>();
                if (!pull.full && holder != null) next.items.AddRange(holder.items);
                foreach (DocumentSnapshot document in pull.documents)
                {
                    arrived.Add(document.Id);
                    next.items.RemoveAll(b => b.id == document.Id);
                    var f = new FirestoreFields(document);
                    bool binned = f.Bool("binned");
                    next.items.Add(new BrandData
                    {
                        id = f.Id, name = f.Str("name", "Unnamed Brand"), category = f.Str("category"),
                        market = f.Str("market", "Global"), scans = f.Long("scans"), binned = binned, aliases = f.List("aliases"),
                        website = f.Str("website"),     // set only by an admin who confirmed the brand owns it: its pages are the maker's own
                        status = binned ? "Inactive" : f.Str("status", "Active")      // a brand in the website's Recycle bin is switched off
                    });
                }
                // a brand this phone has just added from a scan, still on its way to the database, stays known
                if (pull.full && holder != null)
                    foreach (BrandData mine in holder.items)
                        if (!arrived.Contains(mine.id) && SyncQueue.HasPending("brands/" + mine.id)) next.items.Add(mine);

                holder = next;
                lastCheck = Time.realtimeSinceStartup;
                OfflineCache.Save(CacheKey, holder);
                Changed();
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Brand sync failed, using the saved copy: " + e.Message); }
        }

        static bool IsActive(string status) => string.Equals((status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this brand is on the website's list but switched off there (Pending, Inactive or in the Recycle bin).
        /// Its products are then left out of recommendations and alternatives. Call LoadActive() first.
        /// The website mirrors this rule in productVisibility() (GlowUp-Admin_AI_Testing/_Shared/glowupCatalog.js): change one, change the other.
        /// </summary>
        public static bool IsSwitchedOff(string brandId)
        {
            BrandData brand = FindById(brandId);
            return brand != null && (brand.binned || !IsActive(brand.status));
        }

        /// <summary>
        /// The brand with this name, whatever its status (call LoadActive() first), or null. Capitals, accents, punctuation
        /// and spaces do not matter; when no brand has exactly this name, a brand whose name is this one with words added or
        /// left out is taken ("Maybelline" for "Maybelline New York"). An alias of the brand counts as its name.
        /// </summary>
        /// <summary>
        /// The CONFIRMED website of a product's brand (found by its id, else by its name); "" when the brand has none. Only
        /// pages on it are labelled "Manufacturer's own site" (ListSources.SourceKind).
        /// </summary>
        public static string WebsiteOf(string brandId, string brandName)
        {
            BrandData brand = null;
            if (!string.IsNullOrEmpty(brandId)) foreach (BrandData b in All()) if (b != null && b.id == brandId) { brand = b; break; }
            if (brand == null && !string.IsNullOrWhiteSpace(brandName)) brand = FindByName(brandName);
            return brand != null && !brand.binned ? (brand.website ?? "") : "";
        }

        public static BrandData FindByName(string name)
        {
            string wanted = TextKey.Compact(name);
            FromPhone();
            if (wanted.Length == 0 || holder == null) return null;
            BrandData close = null;
            foreach (BrandData b in holder.items)
            {
                if (b == null) continue;
                if (TextKey.Compact(b.name) == wanted) return b;
                if (b.aliases != null) foreach (string alias in b.aliases) if (TextKey.Compact(alias) == wanted) return b;
                if (close == null && TextKey.Normalize(b.name).Length >= 3 && ProductIndex.BrandsAgree(name, b.name)) close = b;
            }
            return close;
        }

        /// <summary>Remembers a brand this phone has just added from a scan.</summary>
        public static void AddLocal(BrandData brand)
        {
            if (brand == null) return;
            FromPhone();
            if (holder == null) holder = new BrandCacheHolder();
            holder.items.RemoveAll(b => b.id == brand.id);
            holder.items.Add(brand);
            OfflineCache.Save(CacheKey, holder);
            Changed();
        }

        /// <summary>Increments the brand's scan counter so the admin dashboard can show it.</summary>
        public static void RecordScan(string brandId)
        {
            if (string.IsNullOrEmpty(brandId)) return;
            SyncQueue.EnqueueIncrement("brands/" + brandId, "scans", 1);
        }
    }
}
