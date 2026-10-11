using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// How the phone keeps its copies of the database's lists (products, brands, ingredients) up to date WITHOUT
    /// downloading everything every time.
    ///
    ///   The first time (no copy on the phone yet): everything is downloaded once.
    ///   After that: only what CHANGED since the last look is downloaded. An entry counts as changed when its
    ///     "updatedAt" or "createdAt" (stamped by the website) or its "addedAtUnix" (stamped by the app) is newer than
    ///     the newest stamp the phone has seen. Then the database is asked how MANY entries the list has: when that is
    ///     not the number the phone knows, something was deleted (or added by hand without a stamp), and everything is
    ///     downloaded once more.
    ///   Once a day (see FullDownloadEveryHours) everything is downloaded again anyway, as a safety net for changes
    ///     made by hand in the Firebase console, which carry no stamp.
    ///   A list that was checked a few minutes ago (RefreshMinutes) is not checked again: the copy is used as it is.
    ///
    /// What this saves: a look for changes costs a handful of reads; a full download costs one read per entry
    /// (about 700 with the present lists). The copy on the phone always stays usable offline.
    /// The two numbers are set in the Scan Rules asset (GlowUp > Open Scan Rules).
    /// </summary>
    public static class CatalogSync
    {
        /// <summary>A list that was checked less than this many minutes ago is used without asking the database again.</summary>
        public static float RefreshMinutes = 10f;
        /// <summary>After this many hours everything is downloaded again once.</summary>
        public static float FullDownloadEveryHours = 24f;

        /// <summary>What the phone remembers about one list between two looks. Saved with the list.</summary>
        [Serializable]
        public class State
        {
            public long lastFullUnix;          // when everything was last downloaded (this phone's clock)
            public long lastCheckedUnix;       // when the database last answered at all (this phone's clock)
            public long newestServerMs;        // the newest updatedAt / createdAt seen: the DATABASE's clock, in milliseconds
            public long newestAddedUnix;       // the newest addedAtUnix seen (entries the app added carry this stamp instead)
            public List<string> ids = new List<string>();     // the id of every entry the database is known to hold
            public int reads;                  // entries downloaded by the last look (shown on the Settings page)

            /// <summary>A copy to work on: the saved state only changes when a look at the database succeeded.</summary>
            public State Clone() => new State
            {
                lastFullUnix = lastFullUnix, lastCheckedUnix = lastCheckedUnix, newestServerMs = newestServerMs,
                newestAddedUnix = newestAddedUnix, ids = new List<string>(ids ?? new List<string>()), reads = reads
            };
        }

        /// <summary>What one look at the database brought.</summary>
        public class Pull
        {
            /// <summary>The database answered.</summary>
            public bool happened;
            /// <summary>True: `documents` is the whole list (replace the copy). False: only the entries that changed (add them, or replace by id).</summary>
            public bool full;
            public List<DocumentSnapshot> documents = new List<DocumentSnapshot>();
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const long OverlapMs = 5 * 60 * 1000;            // stamps this close to the newest one are asked for again (costs a read or two, loses nothing)
        const long AddedOverlapSeconds = 24 * 3600;      // "addedAtUnix" comes from other phones' clocks, which can be wrong by hours

        /// <summary>True when the list was checked recently enough to be used as it is.</summary>
        public static bool IsFresh(float lastCheckRealtime)
        {
            return lastCheckRealtime >= 0f && Time.realtimeSinceStartup - lastCheckRealtime < Mathf.Max(0f, RefreshMinutes) * 60f;
        }

        /// <summary>
        /// Asks the database for one list. `state` is updated when the database answered. Throws when it did not
        /// (no connection, too slow): the caller then keeps using its copy.
        /// </summary>
        /// <param name="haveCopy">the phone already holds a copy of this list</param>
        /// <param name="everything">download the whole list whatever the state says (Settings > Download now)</param>
        public static async Task<Pull> Fetch(string collection, State state, bool haveCopy, bool everything, string what)
        {
            var pull = new Pull();
            CollectionReference list = FirebaseFirestore.DefaultInstance.Collection(collection);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool full = everything || !haveCopy || state.lastFullUnix <= 0 || state.ids == null || state.ids.Count == 0
                     || now - state.lastFullUnix > Mathf.Max(1f, FullDownloadEveryHours) * 3600f;

            if (!full)
            {
                // ---- only what changed since the newest stamp the phone has seen
                Timestamp since = Timestamp.FromDateTime(Epoch.AddMilliseconds(Math.Max(0, state.newestServerMs - OverlapMs)));
                long addedSince = Math.Max(0, state.newestAddedUnix - AddedOverlapSeconds);
                Task<QuerySnapshot> updated = list.WhereGreaterThan("updatedAt", since).GetSnapshotAsync(Source.Server);
                Task<QuerySnapshot> created = list.WhereGreaterThan("createdAt", since).GetSnapshotAsync(Source.Server);
                Task<QuerySnapshot> added = list.WhereGreaterThan("addedAtUnix", addedSince).GetSnapshotAsync(Source.Server);
                Task<AggregateQuerySnapshot> counted = list.Count.GetSnapshotAsync(AggregateSource.Server);
                await Task.WhenAll(updated, created, added, counted).OrTimeout(10f, what);

                var known = new HashSet<string>(state.ids);
                var seen = new HashSet<string>();
                foreach (QuerySnapshot answer in new[] { updated.Result, created.Result, added.Result })
                    foreach (DocumentSnapshot document in answer.Documents)
                        if (seen.Add(document.Id)) { pull.documents.Add(document); known.Add(document.Id); }

                // ---- was anything deleted (or added without a stamp)? Then the number of entries no longer fits.
                if (counted.Result.Count == known.Count)
                {
                    state.ids = new List<string>(known);
                    Stamp(state, pull.documents);
                    state.lastCheckedUnix = now; state.reads = pull.documents.Count;
                    pull.happened = true;
                    ConnectivityMonitor.ReportAnswer();
                    return pull;
                }
                pull.documents.Clear();
                full = true;
            }

            // ---- everything
            QuerySnapshot all = await list.GetSnapshotAsync(Source.Server).OrTimeout(haveCopy ? 12f : 25f, what);
            foreach (DocumentSnapshot document in all.Documents) pull.documents.Add(document);
            state.ids = new List<string>();
            foreach (DocumentSnapshot document in pull.documents) state.ids.Add(document.Id);
            state.newestServerMs = 0; state.newestAddedUnix = 0;
            Stamp(state, pull.documents);
            // a list without a single stamp: remember "yesterday" by this phone's clock, so that the next look asks for a small range
            if (state.newestServerMs == 0) state.newestServerMs = (now - 24 * 3600) * 1000;
            state.lastFullUnix = now; state.lastCheckedUnix = now; state.reads = pull.documents.Count;
            pull.full = true; pull.happened = true;
            ConnectivityMonitor.ReportAnswer();
            return pull;
        }

        /// <summary>Moves the state's "newest stamp seen" forward to the newest stamp in these entries.</summary>
        static void Stamp(State state, List<DocumentSnapshot> documents)
        {
            foreach (DocumentSnapshot document in documents)
            {
                if (!document.Exists) continue;
                Dictionary<string, object> fields = document.ToDictionary();
                foreach (string key in new[] { "updatedAt", "createdAt", "binnedAt", "reviewedAt" })
                    if (fields.TryGetValue(key, out object value) && value is Timestamp stamp)
                    {
                        long ms = (long)(stamp.ToDateTime().ToUniversalTime() - Epoch).TotalMilliseconds;
                        if (ms > state.newestServerMs) state.newestServerMs = ms;
                    }
                if (fields.TryGetValue("addedAtUnix", out object addedValue) && addedValue != null && long.TryParse(addedValue.ToString(), out long added)
                    && added > state.newestAddedUnix && added < DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 24 * 3600)
                    state.newestAddedUnix = added;
            }
        }

        /// <summary>"3 minutes ago", "2 hours ago", "never": for the Settings page.</summary>
        public static string Ago(long unixSeconds)
        {
            if (unixSeconds <= 0) return "never";
            long seconds = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixSeconds);
            if (seconds < 90) return "just now";
            if (seconds < 3600) return (seconds / 60) + " minutes ago";
            if (seconds < 2 * 86400) return (seconds / 3600) + (seconds / 3600 == 1 ? " hour ago" : " hours ago");
            return (seconds / 86400) + " days ago";
        }
    }
}
