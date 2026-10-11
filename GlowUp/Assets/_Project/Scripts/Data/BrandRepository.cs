using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>Brands managed in the admin website ('brands' collection). Only Active brands are returned.</summary>
    public static class BrandRepository
    {
        const string CacheKey = "brands";
        static BrandCacheHolder holder;

        public static async Task<List<BrandData>> LoadActive()
        {
            if (holder == null && OfflineCache.TryLoad(CacheKey, out BrandCacheHolder c)) holder = c;

            if (ConnectivityMonitor.Reachable && await FirebaseBootstrap.Ready)
            {
                try
                {
                    var snap = await FirebaseFirestore.DefaultInstance.Collection("brands").GetSnapshotAsync()
                        .OrTimeout(holder != null ? 8f : 25f, "Downloading the brand list");
                    TaskLimits.RefuseStale(snap, holder != null);
                    var h = new BrandCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    foreach (var doc in snap.Documents)
                    {
                        var f = new FirestoreFields(doc);
                        h.items.Add(new BrandData
                        {
                            id = f.Id, name = f.Str("name", "Unnamed Brand"), category = f.Str("category", "Skincare"),
                            market = f.Str("market", "Global"), scans = f.Long("scans"),
                            status = f.Bool("binned") ? "Inactive" : f.Str("status", "Active")      // a brand in the website's Recycle bin is switched off
                        });
                    }
                    holder = h;
                    OfflineCache.Save(CacheKey, h);
                }
                catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Brand sync failed, using cache: " + e.Message); }
            }
            var result = new List<BrandData>();
            if (holder != null) foreach (var b in holder.items) if (IsActive(b.status)) result.Add(b);
            return result;
        }

        static bool IsActive(string status) => string.Equals((status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this brand is on the website's list but switched off there (Pending or Inactive).
        /// Its products are then left out of recommendations and alternatives. Call LoadActive() first.
        /// </summary>
        public static bool IsSwitchedOff(string brandId)
        {
            if (string.IsNullOrEmpty(brandId) || holder == null) return false;
            foreach (var b in holder.items) if (b.id == brandId) return !IsActive(b.status);
            return false;
        }

        /// <summary>The brand with this name, whatever its status (call LoadActive() first), or null.</summary>
        public static BrandData FindByName(string name)
        {
            string wanted = (name ?? "").Trim();
            if (wanted.Length == 0 || holder == null) return null;
            foreach (var b in holder.items) if (string.Equals((b.name ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>Remembers a brand this phone has just added from a scan.</summary>
        public static void AddLocal(BrandData brand)
        {
            if (brand == null) return;
            if (holder == null) holder = new BrandCacheHolder();
            holder.items.RemoveAll(b => b.id == brand.id);
            holder.items.Add(brand);
            OfflineCache.Save(CacheKey, holder);
        }

        /// <summary>Increments the brand's scan counter so the admin dashboard can show it.</summary>
        public static void RecordScan(string brandId)
        {
            if (string.IsNullOrEmpty(brandId)) return;
            SyncQueue.EnqueueIncrement("brands/" + brandId, "scans", 1);
        }
    }
}
