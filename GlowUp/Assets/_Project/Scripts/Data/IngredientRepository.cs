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
    /// ONLINE: the list is downloaded from Cloud Firestore once and saved on the phone. After that only the
    /// entries that CHANGED are downloaded (see CatalogSync), and a copy that was checked a few minutes ago is
    /// used as it is, so changes made on the website reach the app without downloading the whole list each time.
    /// OFFLINE: the copy saved on the phone is used, so scanning keeps working without a connection.
    ///
    /// Lookup matches techName / name / aliases, whatever the capital letters. The website writes name and
    /// description; every other field is optional. (techName is the old INCI field: since the field change of
    /// November 2026 its value is one of the aliases, so only entries from the app's AI look-up still have it.)
    ///
    /// Entries found by the app's own AI lookup (source "ai") that an admin has not reviewed yet are
    /// kept apart: an entry typed or approved by an admin always wins over them.
    /// </summary>
    public static class IngredientRepository
    {
        const string CacheKey = "ingredients";
        static IngredientCacheHolder holder;
        static Dictionary<string, IngredientData> index;
        static float lastCheck = -1f;               // Time.realtimeSinceStartup of the last look at the database that got an answer
        static Task running;                        // one look at a time: whoever asks meanwhile waits for the same one

        /// <param name="everything">download the whole list now, whatever its age (Settings > Download now)</param>
        public static async Task<IngredientCacheHolder> LoadAll(bool everything = false)
        {
            // 1. the copy saved on the phone (instant, works offline)
            FromPhone();

            // 2. what changed in the cloud, when online and the copy has not been checked for a while
            if (everything || holder == null || !CatalogSync.IsFresh(lastCheck))
            {
                if (running != null)
                {
                    try { await running; } catch (Exception) { }
                    if (everything) { running = Sync(true); try { await running; } finally { running = null; } }
                }
                else
                {
                    running = Sync(everything);
                    try { await running; } finally { running = null; }
                }
            }
            if (index == null) Rebuild();
            return holder ?? new IngredientCacheHolder();
        }

        /// <summary>How the copy on the phone stands: for the Settings page.</summary>
        public static CatalogSync.State SyncState { get { FromPhone(); return holder != null && holder.sync != null ? holder.sync : new CatalogSync.State(); } }

        static void FromPhone()
        {
            if (holder != null) return;
            if (OfflineCache.TryLoad(CacheKey, out IngredientCacheHolder saved) && saved != null)
            {
                holder = saved;
                if (holder.items == null) holder.items = new List<IngredientData>();
                if (holder.sync == null) holder.sync = new CatalogSync.State();
                Rebuild();
            }
        }

        static async Task Sync(bool everything)
        {
            if (!ConnectivityMonitor.Reachable || !await FirebaseBootstrap.Ready) return;
            _ = ClassificationRepository.Refresh();          // on its own: a rename changes no ingredient, and it must not hold up a scan
            try
            {
                bool haveCopy = holder != null && holder.items.Count > 0;
                CatalogSync.State state = holder != null && holder.sync != null ? holder.sync.Clone() : new CatalogSync.State();
                // with a copy on the phone the wait is short: a slow connection must not hold up a scan
                CatalogSync.Pull pull = await CatalogSync.Fetch("ingredients", state, haveCopy, everything, "Downloading the ingredient list");
                if (!pull.happened) return;

                var next = new IngredientCacheHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), sync = state };
                var arrived = new HashSet<string>();
                if (!pull.full && holder != null) next.items.AddRange(holder.items);
                foreach (DocumentSnapshot document in pull.documents)
                {
                    var f = new FirestoreFields(document);
                    arrived.Add(f.Id);
                    next.items.RemoveAll(i => i.id == f.Id);         // the new version replaces the old one
                    if (f.Bool("binned")) continue;                  // in the website's Recycle bin: the app's built-in knowledge is used instead
                    next.items.Add(new IngredientData
                    {
                        id = f.Id, name = f.Str("name"), techName = f.Str("techName"), description = f.Str("description"),
                        classificationId = f.Str("classificationId"), purpose = f.Str("purpose"), chemicalDescription = f.Str("chemicalDescription"),
                        riskLevel = f.Str("riskLevel"), cautionNote = f.Str("cautionNote"), frequentUseAllergy = f.Str("frequentUseAllergy"), commonUses = f.Str("commonUses"),
                        minAge = f.Int("minAge"), keyActive = f.Bool("keyActive"), umbrella = f.Bool("umbrella"),
                        aliases = f.List("aliases"), allergenTags = f.List("allergenTags"), highlights = f.List("highlights"),
                        avoidForSkinTypes = f.List("avoidForSkinTypes"), avoidForConditions = f.List("avoidForConditions"),
                        goodForSkinTypes = f.List("goodForSkinTypes"), goodForConditions = f.List("goodForConditions"),
                        source = f.Str("source"), reviewed = f.Bool("reviewed"), sources = f.List("sources"),
                        fdaUnii = f.Str("fdaUnii"), generic = f.Bool("generic")
                    });
                }
                // entries this phone's AI lookup found that are still on their way to the cloud stay available
                if (pull.full && holder != null)
                    foreach (IngredientData mine in holder.items)
                        if (mine.source == "ai" && !string.IsNullOrEmpty(mine.id) && !arrived.Contains(mine.id) && SyncQueue.HasPending("ingredients/" + mine.id))
                            next.items.Add(mine);

                holder = next;
                lastCheck = Time.realtimeSinceStartup;
                OfflineCache.Save(CacheKey, holder);
                Rebuild();
                GlowUp.Notifications.IngredientRuleWatcher.OnDownloaded(holder.items);     // tells the user when an admin changed the rules
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Ingredient sync failed, using the saved copy: " + e.Message); }
        }

        /// <summary>The comparable key of a name (the same rule as everywhere else: capitals, accents and punctuation do not matter).</summary>
        public static string Normalize(string s) => TextKey.Normalize(s);

        /// <summary>Goes up by one every time the list changes (so anything built from it knows when to rebuild).</summary>
        public static int Version { get; private set; }

        static void Rebuild()
        {
            index = new Dictionary<string, IngredientData>();
            Version++;
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
        public static IngredientData Find(string scannedName) => FindByKey(Normalize(scannedName));

        /// <summary>The website's entry under this name key (see Normalize), or null.</summary>
        public static IngredientData FindByKey(string key)
        {
            if (index == null) Rebuild();
            return index != null && !string.IsNullOrEmpty(key) && index.TryGetValue(key, out var d) ? d : null;
        }

        /// <summary>Every name key the website's list answers to (names, label names and synonyms).</summary>
        public static IEnumerable<string> Keys()
        {
            if (index == null) Rebuild();
            return index != null ? (IEnumerable<string>)index.Keys : new string[0];
        }

        /// <summary>
        /// Adds an entry the app's AI lookup has just found, so the scan in progress (and later scans on
        /// this phone, online or offline) can use it. Sending it to the cloud is the caller's job.
        /// </summary>
        public static void AddLocal(IngredientData found)
        {
            if (found == null || string.IsNullOrWhiteSpace(found.name)) return;
            FromPhone();
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
