using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Recent-scan history. The list on the device is what the app shows (so it works offline);
    /// each new scan is also sent to users/{uid}/scans through the SyncQueue.
    /// - Rename is local only (displayName): the database and the admin website are not touched.
    /// - Remove / Clear move items to a trash that can be restored; "Delete forever" empties it.
    /// </summary>
    public static class ScanHistoryRepository
    {
        static ScanHistoryHolder holder;
        static string holderKey;

        /// <summary>Each account has its own history file on the device.</summary>
        static string CacheKey => "scan_history_" + (UserProfileRepository.ActiveUid ?? "guest");

        static ScanHistoryHolder H()
        {
            string key = CacheKey;
            if (holder == null || holderKey != key)
            {
                holderKey = key;
                holder = OfflineCache.TryLoad(key, out ScanHistoryHolder h) && h != null ? h : new ScanHistoryHolder();
            }
            return holder;
        }
        static void Persist() { var h = H(); OfflineCache.Save(holderKey, h); }

        /// <summary>Saved scans, newest first.</summary>
        public static List<ScanRecord> Active() => H().items.Where(r => !r.deleted).OrderByDescending(r => r.scannedAtUnix).ToList();
        /// <summary>Removed scans that can still be restored.</summary>
        public static List<ScanRecord> Trash() => H().items.Where(r => r.deleted).ToList();

        public static ScanRecord Find(string id) => string.IsNullOrEmpty(id) ? null : H().items.FirstOrDefault(r => r.id == id);

        public static ScanRecord Add(ScanRecord r)
        {
            if (string.IsNullOrEmpty(r.id)) r.id = Guid.NewGuid().ToString("N");
            if (r.scannedAtUnix == 0) r.scannedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            H().items.Add(r);
            Persist();
            string uid = UserProfileRepository.ActiveUid;
            if (uid != null) SyncQueue.EnqueueSet($"users/{uid}/scans/{r.id}", r);
            return r;
        }

        /// <summary>
        /// Saves a scan again after something was added to it (the FDA Philippines answer can arrive a little later than
        /// the result). The phone's copy and the user's cloud copy are both written. A scan that is no longer kept is left alone.
        /// </summary>
        public static void Update(ScanRecord r)
        {
            if (r == null || string.IsNullOrEmpty(r.id)) return;
            ScanHistoryHolder h = H();
            int at = h.items.FindIndex(x => x != null && x.id == r.id);
            if (at < 0) return;
            h.items[at] = r;
            Persist();
            string uid = UserProfileRepository.ActiveUid;
            if (uid != null) SyncQueue.EnqueueSet($"users/{uid}/scans/{r.id}", r);
        }

        /// <summary>
        /// "Scan the same product again": the new result takes the PLACE of the earlier one, so the product is in Recent
        /// Scans once and not twice. It keeps the earlier scan's id and the user's own name for it, and gets the time of
        /// the new scan. Returns false (and saves nothing) when the earlier scan is no longer kept: it is then a new scan.
        /// </summary>
        public static bool Replace(string earlierId, ScanRecord r)
        {
            if (r == null || string.IsNullOrEmpty(earlierId)) return false;
            ScanHistoryHolder h = H();
            int at = h.items.FindIndex(x => x != null && x.id == earlierId);
            if (at < 0) return false;
            r.id = earlierId;
            if (string.IsNullOrEmpty(r.displayName)) r.displayName = h.items[at].displayName;
            if (r.scannedAtUnix == 0) r.scannedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            r.deleted = false;
            h.items[at] = r;
            Persist();
            string uid = UserProfileRepository.ActiveUid;
            if (uid != null)
            {
                // the earlier result may still be waiting to be sent: it is replaced in the queue, not sent as well
                string path = $"users/{uid}/scans/{r.id}";
                if (!SyncQueue.ReplacePendingSet(path, r)) SyncQueue.EnqueueSet(path, r);
            }
            return true;
        }

        public static void Remove(string id) { var r = Find(id); if (r != null) { r.deleted = true; Persist(); } }
        public static void ClearAll() { foreach (var r in H().items) r.deleted = true; Persist(); }
        public static void Restore(string id) { var r = Find(id); if (r != null) { r.deleted = false; Persist(); } }
        public static void RestoreAll() { foreach (var r in H().items) r.deleted = false; Persist(); }

        /// <summary>Removes one account's whole scan history from THIS PHONE (used when that account is deleted). The database is not touched here.</summary>
        public static void DeleteLocal(string uid)
        {
            string key = "scan_history_" + (string.IsNullOrEmpty(uid) ? "guest" : uid);
            OfflineCache.Delete(key);
            if (holderKey == key) { holder = null; holderKey = null; }
        }

        /// <summary>Renames locally only - the saved database and the admin website are not touched.</summary>
        public static void Rename(string id, string newName) { var r = Find(id); if (r != null) { r.displayName = (newName ?? "").Trim(); Persist(); } }

        /// <summary>Permanently deletes everything in the trash (also from the user's cloud history).</summary>
        public static void PurgeTrash()
        {
            string uid = UserProfileRepository.ActiveUid;
            foreach (var r in H().items.Where(x => x.deleted).ToList())
                if (uid != null) SyncQueue.EnqueueDelete($"users/{uid}/scans/{r.id}");
            H().items.RemoveAll(r => r.deleted);
            Persist();
        }

        /// <summary>Human-readable scan time in the device's own format, e.g. "9 Oct 2026, 21:41".</summary>
        public static string FormatTime(long unixSeconds)
        {
            if (unixSeconds <= 0) return "";
            var local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().DateTime;
            return local.ToString("d MMM yyyy, h:mm tt");
        }
    }
}
