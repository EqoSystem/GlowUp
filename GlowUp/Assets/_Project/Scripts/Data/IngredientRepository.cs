using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Ingredient database shared with the admin website ('ingredients' collection).
    ///
    /// ONLINE: the list is downloaded from Cloud Firestore and saved on the phone. While the app stays
    /// open it is downloaded again every <see cref="RefreshMinutes"/> minutes (and straight away the
    /// first time the phone is online), so changes made on the website reach the app.
    /// OFFLINE: the copy saved on the phone is used, so scanning keeps working without a connection.
    ///
    /// Lookup matches techName / name / aliases, whatever the capital letters. The website writes name,
    /// techName and description; every other field is optional.
    ///
    /// Entries found by the app's own AI lookup (source "ai") that an admin has not reviewed yet are
    /// kept apart: an entry typed or approved by an admin always wins over them.
    /// </summary>
    public static class IngredientRepository
    {
        const string CacheKey = "ingredients";
        static IngredientCacheHolder holder;
        static Dictionary<string, IngredientData> index;
        static float lastServerSync = -1f;          // Time.realtimeSinceStartup of the last successful download (this run of the app)

        /// <summary>How long a downloaded list counts as fresh, in minutes. Set from LoadingSceneController's Inspector.</summary>
        public static float RefreshMinutes = 10f;

        public static async Task<IngredientCacheHolder> LoadAll(bool forceRefresh = false)
        {
            // 1. the copy saved on the phone (instant, works offline)
            if (holder == null && OfflineCache.TryLoad(CacheKey, out IngredientCacheHolder cached)) { holder = cached; Rebuild(); }

            // 2. a fresh copy from the cloud, when online and the one we have is not recent
            bool recent = lastServerSync >= 0f && Time.realtimeSinceStartup - lastServerSync < Mathf.Max(0f, RefreshMinutes) * 60f;
            if (holder != null && recent && !forceRefresh) return holder;

            if (ConnectivityMonitor.Reachable && await FirebaseBootstrap.Ready)
            {
                try
                {
                    // with a copy on the phone the wait is short: a slow connection must not hold up a scan
                    var snap = await FirebaseFirestore.DefaultInstance.Collection("ingredients").GetSnapshotAsync()
                        .OrTimeout(holder != null ? 8f : 25f, "Downloading the ingredient list");
                    TaskLimits.RefuseStale(snap, holder != null);
                    var fresh = new IngredientCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    var ids = new HashSet<string>();
                    foreach (var doc in snap.Documents)
                    {
                        var f = new FirestoreFields(doc);
                        ids.Add(f.Id);
                        if (f.Bool("binned")) continue;                  // in the website's Recycle bin: the app's built-in knowledge is used instead
                        fresh.items.Add(new IngredientData
                        {
                            id = f.Id, name = f.Str("name"), techName = f.Str("techName"), description = f.Str("description"),
                            riskLevel = f.Str("riskLevel"), cautionNote = f.Str("cautionNote"), commonUses = f.Str("commonUses"),
                            minAge = f.Int("minAge"), keyActive = f.Bool("keyActive"), umbrella = f.Bool("umbrella"),
                            aliases = f.List("aliases"), allergenTags = f.List("allergenTags"),
                            avoidForSkinTypes = f.List("avoidForSkinTypes"), avoidForConditions = f.List("avoidForConditions"),
                            goodForSkinTypes = f.List("goodForSkinTypes"), goodForConditions = f.List("goodForConditions"),
                            source = f.Str("source"), reviewed = f.Bool("reviewed"), sources = f.List("sources"), fdaUnii = f.Str("fdaUnii")
                        });
                    }
                    GlowUp.Notifications.IngredientRuleWatcher.OnDownloaded(fresh.items);     // tells the user when an admin changed the rules
                    // entries this phone's AI lookup found that have not reached the cloud yet stay available
                    if (holder != null)
                        foreach (var mine in holder.items)
                            if (mine.source == "ai" && !string.IsNullOrEmpty(mine.id) && !ids.Contains(mine.id)) fresh.items.Add(mine);

                    holder = fresh;
                    lastServerSync = Time.realtimeSinceStartup;
                    OfflineCache.Save(CacheKey, holder);
                }
                catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Ingredient sync failed, using the saved copy: " + e.Message); }
            }
            Rebuild();
            return holder ?? new IngredientCacheHolder();
        }

        public static string Normalize(string s) => (s ?? "").Trim().ToLowerInvariant();

        static void Rebuild()
        {
            index = new Dictionary<string, IngredientData>();
            if (holder == null) return;
            // first everything an admin typed or approved, then the unreviewed AI finds for names still free
            foreach (var i in holder.items) if (!i.IsUnreviewedAi()) AddToIndex(i);
            foreach (var i in holder.items) if (i.IsUnreviewedAi()) AddToIndex(i);
        }

        static void AddToIndex(IngredientData i)
        {
            Add(i.techName, i); Add(i.name, i);
            if (i.aliases != null) foreach (var a in i.aliases) Add(a, i);
        }
        static void Add(string key, IngredientData d) { var k = Normalize(key); if (k.Length > 0 && !index.ContainsKey(k)) index[k] = d; }

        /// <summary>The website's entry for this ingredient name, or null.</summary>
        public static IngredientData Find(string scannedName)
        {
            if (index == null) Rebuild();
            return index != null && index.TryGetValue(Normalize(scannedName), out var d) ? d : null;
        }

        /// <summary>
        /// Adds an entry the app's AI lookup has just found, so the scan in progress (and later scans on
        /// this phone, online or offline) can use it. Sending it to the cloud is the caller's job.
        /// </summary>
        public static void AddLocal(IngredientData found)
        {
            if (found == null || string.IsNullOrWhiteSpace(found.name)) return;
            if (holder == null) holder = new IngredientCacheHolder();
            holder.items.RemoveAll(i => !string.IsNullOrEmpty(found.id) && i.id == found.id);
            holder.items.Add(found);
            OfflineCache.Save(CacheKey, holder);
            Rebuild();
        }

        /// <summary>How many ingredients came from the website (0 when it has never synced).</summary>
        public static int Count => holder != null ? holder.items.Count : 0;
    }
}
