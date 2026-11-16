using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Stores Firestore writes made while offline and replays them when the connection is back.
    /// Add one instance (DontDestroyOnLoad) – usually on the same object as ConnectivityMonitor.
    ///
    /// A write the database refuses for good (for example the security rules do not allow it, or the
    /// document it updates was deleted on the admin website) is dropped with a warning, so it can
    /// never block the writes queued behind it. A write that fails for a passing reason (no
    /// connection, not signed in yet) stays in the queue and is tried again later.
    /// </summary>
    public class SyncQueue : MonoBehaviour
    {
        const string CacheKey = "pending_writes";
        public static SyncQueue Instance { get; private set; }

        [Tooltip("A write that keeps failing for an unknown reason is dropped after this many tries")]
        [SerializeField] private int maximumAttempts = 8;

        PendingWriteHolder queue = new PendingWriteHolder();
        bool flushing;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            if (OfflineCache.TryLoad(CacheKey, out PendingWriteHolder h)) queue = h;
        }

        void OnEnable() => ConnectivityMonitor.ConnectivityChanged += OnConn;
        void OnDisable() => ConnectivityMonitor.ConnectivityChanged -= OnConn;
        void Start() { if (ConnectivityMonitor.Online) _ = Flush(); }
        void OnConn(bool online) { if (online) _ = Flush(); StateChanged?.Invoke(); }

        // ---- for the Settings page (Sync status / Sync Now) ----
        const string LastSyncKey = "GlowUp.Sync.LastSyncedUnix";
        /// <summary>Raised when the number of waiting changes, or the "sending now" state, changes.</summary>
        public static event Action StateChanged;
        /// <summary>How many changes are waiting to be sent to the database.</summary>
        public static int PendingCount => Ensure().queue.items.Count;
        public static bool IsSyncing => Instance != null && Instance.flushing;
        /// <summary>When everything waiting was last sent (Unix seconds; 0 = never on this phone).</summary>
        public static long LastSyncedUnix => long.TryParse(PlayerPrefs.GetString(LastSyncKey, "0"), out long t) ? t : 0;
        /// <summary>Sends everything that is waiting now (does nothing while offline).</summary>
        public static Task SyncNow() => Ensure().Flush(true);

        /// <summary>Drops waiting writes whose path starts with `pathPrefix` (used when an account is deleted).</summary>
        public static void RemoveWhere(string pathPrefix)
        {
            if (string.IsNullOrEmpty(pathPrefix)) return;
            SyncQueue q = Ensure();
            int removed = q.queue.items.RemoveAll(w => w != null && w.path != null && w.path.StartsWith(pathPrefix, StringComparison.Ordinal));
            if (removed == 0) return;
            OfflineCache.Save(CacheKey, q.queue);
            StateChanged?.Invoke();
        }

        /// <summary>True while a write to exactly this path is still waiting to be sent.</summary>
        public static bool HasPending(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (PendingWrite w in Ensure().queue.items) if (w != null && w.path == path) return true;
            return false;
        }

        public static void EnqueueSet(string path, object data)
        {
            Ensure().Add(new PendingWrite { op = "set", path = path, json = JsonUtility.ToJson(data) });
        }
        /// <summary>
        /// A write to this document that is still WAITING gets the new content instead of a second write being queued
        /// behind it (a scan that was done again before the first result was sent). Returns false when nothing is waiting
        /// for that path, or when that write is being sent at this moment: the caller then decides what to do.
        /// </summary>
        public static bool ReplacePendingSet(string path, object data)
        {
            if (string.IsNullOrEmpty(path)) return false;
            SyncQueue q = Ensure();
            for (int i = q.queue.items.Count - 1; i >= 0; i--)
            {
                PendingWrite w = q.queue.items[i];
                if (w == null || w.op != "set" || w.path != path) continue;
                if (i == 0 && q.flushing) return false;              // on its way already
                w.json = JsonUtility.ToJson(data);
                OfflineCache.Save(CacheKey, q.queue);
                return true;
            }
            return false;
        }
        public static void EnqueueIncrement(string path, string field, long amount)
        {
            Ensure().Add(new PendingWrite { op = "increment", path = path, field = field, amount = amount });
        }
        public static void EnqueueDelete(string path)
        {
            Ensure().Add(new PendingWrite { op = "delete", path = path });
        }

        static SyncQueue Ensure()
        {
            if (Instance == null) new GameObject("SyncQueue").AddComponent<SyncQueue>();
            return Instance;
        }

        void Add(PendingWrite w)
        {
            queue.items.Add(w);
            OfflineCache.Save(CacheKey, queue);
            StateChanged?.Invoke();
            if (ConnectivityMonitor.Online) _ = Flush();
        }

        /// <param name="markEvenWhenEmpty">Sync Now: an empty queue while online also counts as "synced just now".</param>
        public async Task Flush(bool markEvenWhenEmpty = false)
        {
            if (flushing) return;
            if (queue.items.Count == 0 && !(markEvenWhenEmpty && ConnectivityMonitor.Online)) return;
            flushing = true;
            bool reachedDatabase = false;
            StateChanged?.Invoke();
            try
            {
                if (!await FirebaseBootstrap.Ready) return;
                reachedDatabase = ConnectivityMonitor.Online;
                var db = FirebaseFirestore.DefaultInstance;
                while (queue.items.Count > 0 && ConnectivityMonitor.Online)
                {
                    var w = queue.items[0];
                    try
                    {
                        var doc = db.Document(w.path);
                        switch (w.op)
                        {
                            case "set":
                                await doc.SetAsync(JsonToDict(w.json), SetOptions.MergeAll);
                                break;
                            case "increment":
                                await doc.UpdateAsync(w.field, FieldValue.Increment(w.amount));
                                break;
                            case "delete":
                                await doc.DeleteAsync();
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        w.attempts++;
                        if (!ShouldDrop(e, w))
                        {
                            // a passing problem: keep it at the head of the queue and try again later
                            OfflineCache.Save(CacheKey, queue);
                            Debug.LogWarning($"SyncQueue: will retry {w.op} {w.path} later: {e.Message}");
                            break;
                        }
                        Debug.LogWarning($"SyncQueue: dropped {w.op} {w.path}: {e.Message}");
                    }
                    queue.items.RemoveAt(0);
                    OfflineCache.Save(CacheKey, queue);
                }
            }
            catch (Exception e) { Debug.LogWarning("SyncQueue flush stopped: " + e.Message); }
            finally
            {
                flushing = false;
                if (reachedDatabase && queue.items.Count == 0)
                {
                    PlayerPrefs.SetString(LastSyncKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                    PlayerPrefs.Save();
                }
                StateChanged?.Invoke();
            }
        }

        /// <summary>True when trying this write again can never succeed.</summary>
        bool ShouldDrop(Exception e, PendingWrite w)
        {
            var fe = e as FirestoreException ?? e.InnerException as FirestoreException;
            if (fe != null)
                switch (fe.ErrorCode)
                {
                    case FirestoreError.NotFound:              // e.g. the brand was deleted on the website
                    case FirestoreError.InvalidArgument:
                    case FirestoreError.FailedPrecondition:
                    case FirestoreError.OutOfRange:
                        return true;
                    case FirestoreError.PermissionDenied:
                    case FirestoreError.Unauthenticated:
                        // refused while properly signed in = the rules do not allow it. Not signed in to Firebase
                        // (for example after an offline sign-in) = wait until the user signs in online.
                        return SignedIn();
                    case FirestoreError.Unavailable:
                    case FirestoreError.DeadlineExceeded:
                    case FirestoreError.Cancelled:
                    case FirestoreError.Aborted:
                    case FirestoreError.ResourceExhausted:
                        return false;                          // the network or the server: always worth another try
                }
            return w.attempts >= Mathf.Max(1, maximumAttempts);   // anything else: give up after a number of tries
        }

        static bool SignedIn()
        {
            try { return FirebaseAuth.DefaultInstance.CurrentUser != null; }
            catch { return false; }
        }

        /// <summary>Minimal flat JSON -> dictionary (strings, numbers, bools, string arrays).</summary>
        internal static Dictionary<string, object> JsonToDict(string json)
        {
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            return parsed ?? new Dictionary<string, object>();
        }
    }
}
