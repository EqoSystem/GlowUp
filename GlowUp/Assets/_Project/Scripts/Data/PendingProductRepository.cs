using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// What the app writes to "pending_products": a product the AI read from ONE photo, waiting for an admin.
    /// Field names follow the rest of the database (camelCase). Every field here is allowed by firestore.rules; add none
    /// without adding it there too (JsonUtility writes every public field).
    /// </summary>
    [Serializable]
    public class PendingProductRecord
    {
        public string name = "", brandName = "", cosmeticType = "", shadeHex = "";
        public string variant = "", size = "";                 // shade or finish, net content: kept apart from the name (rules: 60 and 40 characters)
        public List<string> sourceUrls = new List<string>();   // https only, at most 5, under 300 characters each (the rules check it)
        public List<string> ingredients = new List<string>();
        public string key = "";                  // brand and name as one comparable text: the same product gets the same entry
        public string status = "pending";        // "pending" until an admin approves it ("approved" then)
        public string source = "snapshot";
        public bool isApproved;                  // false from the app; only an admin sets it to true
        public bool hasImage;                    // a small photo is in pending_product_images under the same id
        public float confidence;                 // how sure the AI was, 0 to 1
        public string model = "";
        public string createdByUid = "";
        public long createdAtUnix;
    }

    /// <summary>The photo of a staged product, in its own document so that phones never download it (only admins read it).</summary>
    [Serializable]
    public class PendingProductImageRecord
    {
        public string jpegBase64 = "";
        public int bytes;
        public string createdByUid = "";
        public long createdAtUnix;
    }

    /// <summary>A staged product as the app keeps it on the phone.</summary>
    [Serializable]
    public class PendingProductData
    {
        public string id = "", name = "", brandName = "", cosmeticType = "", shadeHex = "", model = "";
        public string createdByUid = "";         // who sent it: only the sender's own entry is ever used for a verdict
        public List<string> ingredients = new List<string>();
        public bool isApproved;
        public long savedAtUnix;
    }

    [Serializable]
    class PendingProductCache
    {
        public List<PendingProductData> items = new List<PendingProductData>();
        public List<string> missKeys = new List<string>();
        public List<long> missAtUnix = new List<long>();
    }

    /// <summary>
    /// The staging area for products the app's catalogue does not have yet ("pending_products", see FIRESTORE_SCHEMA.md).
    ///
    ///   OWN ENTRIES ONLY: an entry another user staged is unverified, and anyone signed in can stage the first one for a product, so it is
    ///   never used for a verdict on this phone (it would be a way to hide an allergen from other users). Only the sender's own entry is
    ///   found again; for everybody else the product is read from their own photo, and an admin's approval puts it in the catalogue.
    ///   Find:   the copy on the phone first (0 reads); then ONE read of the document whose id is made from the brand and the
    ///           name, so every user who snaps the same product lands on the same entry. A miss is remembered for a while, so the
    ///           same unknown product does not cost a read on every scan.
    ///   Submit: queued through SyncQueue (works offline). The photo goes to its own document, read by admins only.
    ///   An admin approves the entry on the website: it then moves into "products" and the app finds it in its catalogue.
    /// The numbers are set by ScanSnapshotController (Inspector of the "Scan Snapshot" object in the scan scene).
    /// </summary>
    public static class PendingProductRepository
    {
        const string CacheKey = "pending_products";
        public const string Collection = "pending_products";
        public const string ImageCollection = "pending_product_images";

        /// <summary>A product that was looked up and not found is not looked up again for this many minutes.</summary>
        public static float RememberMissMinutes = 30f;
        /// <summary>A staged product kept on the phone is looked at again after this many hours (it may have been approved).</summary>
        public static float KeepHours = 24f;

        static PendingProductCache cache;

        static PendingProductCache Cache
        {
            get
            {
                if (cache != null) return cache;
                if (!OfflineCache.TryLoad(CacheKey, out cache) || cache == null) cache = new PendingProductCache();
                if (cache.items == null) cache.items = new List<PendingProductData>();
                if (cache.missKeys == null || cache.missAtUnix == null || cache.missKeys.Count != cache.missAtUnix.Count) { cache.missKeys = new List<string>(); cache.missAtUnix = new List<long>(); }
                return cache;
            }
        }

        /// <summary>The id of the staged entry of a product: "snap_" + brand + "__" + name, letters and digits only.</summary>
        public static string IdFor(string brandName, string productName)
        {
            string brand = TextKey.Slug(brandName), name = TextKey.Slug(productName);
            if (name.Length == 0) return "";
            string id = "snap_" + (brand.Length > 0 ? brand : "nobrand") + "__" + name;
            return id.Length > 110 ? id.Substring(0, 110) : id;
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>True when the entry was sent from this account (an entry without a sender is nobody's).</summary>
        static bool IsMine(string senderUid) => !string.IsNullOrEmpty(senderUid) && senderUid == UserProfileRepository.CurrentUid;

        /// <summary>The copy on the phone, or null (no read).</summary>
        public static PendingProductData FindLocal(string brandName, string productName)
        {
            string id = IdFor(brandName, productName);
            if (id.Length == 0) return null;
            PendingProductData kept = Cache.items.Find(p => p.id == id);
            if (kept == null || !IsMine(kept.createdByUid)) return null;       // a copy of someone else's entry (older builds kept them) is not used
            return Now - kept.savedAtUnix <= KeepHours * 3600f ? kept : null;
        }

        /// <summary>
        /// The staged entry of a product that is still waiting for approval, or null. Costs at most ONE read (and none when the
        /// phone has a fresh copy, or looked recently and found nothing).
        /// </summary>
        public static async Task<PendingProductData> FindAsync(string brandName, string productName)
        {
            string id = IdFor(brandName, productName);
            if (id.Length == 0) return null;
            PendingProductData local = FindLocal(brandName, productName);
            if (local != null) return local.isApproved ? null : local;
            int miss = Cache.missKeys.IndexOf(id);
            if (miss >= 0 && Now - Cache.missAtUnix[miss] <= RememberMissMinutes * 60f) return null;
            if (!ConnectivityMonitor.Reachable || !await FirebaseBootstrap.Ready) return null;
            try
            {
                DocumentSnapshot snapshot = await FirebaseFirestore.DefaultInstance.Collection(Collection).Document(id).GetSnapshotAsync().OrTimeout(8f, "Looking for the product among the photos of other users");
                ReadCounter.Add("pending_products lookup", 1);
                if (!snapshot.Exists) { RememberMiss(id); return null; }
                var f = new FirestoreFields(snapshot);
                if (!IsMine(f.Str("createdByUid"))) { RememberMiss(id); return null; }        // another user's entry: unverified, not used
                var found = new PendingProductData
                {
                    id = id, name = f.Str("name"), brandName = f.Str("brandName"), cosmeticType = f.Str("cosmeticType"), shadeHex = f.Str("shadeHex"),
                    model = f.Str("model"), createdByUid = f.Str("createdByUid"), ingredients = f.List("ingredients"), isApproved = f.Bool("isApproved"), savedAtUnix = Now
                };
                Keep(found);
                return found.isApproved ? null : found;
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Pending products: the look-up failed: " + e.Message); return null; }
        }

        /// <summary>Stages a product the AI read from a photo. The photo (JPG bytes, may be null) goes to its own document.</summary>
        public static PendingProductData Submit(PendingProductRecord record, byte[] jpeg, int maximumImageBytes)
        {
            string id = IdFor(record.brandName, record.name);
            if (id.Length == 0) return null;
            string uid = UserProfileRepository.CurrentUid;
            record.key = id.Substring(5);
            record.createdByUid = uid ?? "";
            record.createdAtUnix = Now;
            record.status = "pending"; record.source = "snapshot"; record.isApproved = false;
            bool image = jpeg != null && jpeg.Length > 0 && jpeg.Length <= maximumImageBytes;
            record.hasImage = image;
            if (!string.IsNullOrEmpty(uid))
            {
                SyncQueue.EnqueueSet(Collection + "/" + id, record);
                if (image) SyncQueue.EnqueueSet(ImageCollection + "/" + id, new PendingProductImageRecord { jpegBase64 = Convert.ToBase64String(jpeg), bytes = jpeg.Length, createdByUid = uid, createdAtUnix = record.createdAtUnix });
            }
            var kept = new PendingProductData
            {
                id = id, name = record.name, brandName = record.brandName, cosmeticType = record.cosmeticType, shadeHex = record.shadeHex,
                model = record.model, createdByUid = uid ?? "", ingredients = new List<string>(record.ingredients), isApproved = false, savedAtUnix = Now
            };
            Keep(kept);
            return kept;
        }

        static void Keep(PendingProductData item)
        {
            PendingProductCache c = Cache;
            c.items.RemoveAll(p => p.id == item.id);
            c.items.Add(item);
            if (c.items.Count > 200) c.items.RemoveRange(0, c.items.Count - 200);
            int miss = c.missKeys.IndexOf(item.id);
            if (miss >= 0) { c.missKeys.RemoveAt(miss); c.missAtUnix.RemoveAt(miss); }
            OfflineCache.Save(CacheKey, c);
        }

        static void RememberMiss(string id)
        {
            PendingProductCache c = Cache;
            int at = c.missKeys.IndexOf(id);
            if (at >= 0) c.missAtUnix[at] = Now; else { c.missKeys.Add(id); c.missAtUnix.Add(Now); }
            if (c.missKeys.Count > 300) { c.missKeys.RemoveAt(0); c.missAtUnix.RemoveAt(0); }
            OfflineCache.Save(CacheKey, c);
        }
    }
}
