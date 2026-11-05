using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>One ingredient classification: the id ingredients link to, and the words users read.</summary>
    [Serializable]
    public class ClassificationItem
    {
        public string id = "", name = "";
    }

    /// <summary>The copy of the list that is saved on the phone.</summary>
    [Serializable]
    public class ClassificationHolder
    {
        public List<ClassificationItem> items = new List<ClassificationItem>();
        public long savedAtUnix;
    }

    /// <summary>
    /// The classification list the admin website manages ('ingredientClassifications'). An ingredient links to one by its id, so a
    /// rename on the website changes the badge on every ingredient at the next check. The app only reads it. It is read again at
    /// most once per CatalogSync check, and the copy on the phone is used when offline. An id it does not know gives no badge.
    /// </summary>
    public static class ClassificationRepository
    {
        const string CacheKey = "ingredientClassifications";
        const string Collection = "ingredientClassifications";
        static ClassificationHolder holder;
        static float lastCheck = -1f;
        static Task running;

        static void FromPhone()
        {
            if (holder != null) return;
            if (OfflineCache.TryLoad(CacheKey, out ClassificationHolder saved) && saved != null && saved.items != null) holder = saved;
        }

        /// <summary>The words for a classification id; "" when there is none.</summary>
        public static string NameOf(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            FromPhone();
            if (holder == null) return "";
            foreach (ClassificationItem item in holder.items)
                if (item.id == id) return item.name ?? "";
            return "";
        }

        public static async Task Refresh(bool force = false)
        {
            FromPhone();
            if (!force && CatalogSync.IsFresh(lastCheck)) return;
            if (running != null) { try { await running; } catch (Exception) { } return; }
            running = Sync();
            try { await running; } finally { running = null; }
        }

        static async Task Sync()
        {
            if (!ConnectivityMonitor.Reachable || !await FirebaseBootstrap.Ready) return;
            try
            {
                QuerySnapshot snapshot = await FirebaseFirestore.DefaultInstance.Collection(Collection)
                    .GetSnapshotAsync(Source.Server).OrTimeout(8f, "Downloading the ingredient classifications");
                var next = new ClassificationHolder { savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                int read = 0;
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    read++;
                    var f = new FirestoreFields(document);
                    next.items.Add(new ClassificationItem { id = f.Id, name = f.Str("name") });
                }
                ReadCounter.Add(Collection, read);
                lastCheck = Time.realtimeSinceStartup;
                ConnectivityMonitor.ReportAnswer();
                holder = next;
                OfflineCache.Save(CacheKey, holder);
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("The classifications could not be downloaded, using the copy on the phone: " + e.Message); }
        }
    }
}
