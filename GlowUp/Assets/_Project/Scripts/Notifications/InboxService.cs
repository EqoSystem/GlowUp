using System;
using System.Collections.Generic;
using System.Linq;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Notifications
{
    /// <summary>
    /// The app's Notifications (New22, 20 November 2026), live from Firestore:
    ///   - users/{uid}/notifications: the admins' replies to this user's reports (only admins can create one; the user reads,
    ///     marks read and deletes their own: firestore.rules);
    ///   - announcements: notices that the website adds when an admin saves a product or an ingredient (shown live to every user).
    /// One listener per collection for the whole app (a DontDestroyOnLoad object), started on the first request and stopped on
    /// sign-out or when the app closes. Deleting an announcement hides it on this phone only (it belongs to everyone); a reply is
    /// deleted from the account. "Read" for announcements is kept on this phone too.
    /// </summary>
    public class InboxService : MonoBehaviour
    {
        public enum Source { Personal, Announcement }

        public class Item
        {
            public string id;
            public Source source;
            public string kind;         // reply | product | ingredient
            public string title, body;
            public DateTime createdAt;
            public bool read;
        }

        const int Limit = 50;
        const string HiddenKey = "GlowUp.Inbox.Hidden.", ReadKey = "GlowUp.Inbox.Read.";

        public static InboxService Instance { get; private set; }
        /// <summary>Raised on Unity's main thread whenever the list changes.</summary>
        public static event Action Changed;

        readonly List<Item> personal = new List<Item>(), announcements = new List<Item>();
        ListenerRegistration personalListener, announcementListener, stateListener;
        string uid;
        HashSet<string> hidden = new HashSet<string>(), readHere = new HashSet<string>();
        bool dirty;

        public bool Loaded { get; private set; }
        public bool Failed { get; private set; }

        /// <summary>The service, started once (safe to call from any scene).</summary>
        public static InboxService Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("InboxService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<InboxService>();
            return Instance;
        }

        async void Start()
        {
            if (!await FirebaseBootstrap.Ready) { Failed = true; Loaded = true; dirty = true; return; }
            FirebaseAuth.DefaultInstance.StateChanged += OnAuthChanged;
            OnAuthChanged(null, EventArgs.Empty);
        }

        void OnAuthChanged(object sender, EventArgs e)
        {
            string now = FirebaseAuth.DefaultInstance.CurrentUser?.UserId;
            if (now == uid) return;
            Stop();
            uid = now;
            Attach();
        }

        int generation;               // raised by Stop(): a watcher of an older set of listeners does nothing
        int retries;

        /// <summary>
        /// Starts the three live listeners for the signed-in account. Real time (27 Nov 2026 fix): a listener that the server ends
        /// with an error (a permission check made before the sign-in had settled, a dropped connection the SDK gives up on) used
        /// to stay dead until the app restarted, so a reply to a report only appeared after a restart. Each listener is now
        /// watched and the set is attached again after a short pause, as long as the same account is signed in.
        /// </summary>
        void Attach()
        {
            if (string.IsNullOrEmpty(uid)) { Loaded = false; dirty = true; return; }
            hidden = LoadSet(HiddenKey + uid); readHere = LoadSet(ReadKey + uid);
            var db = FirebaseFirestore.DefaultInstance;
            personalListener = db.Collection("users").Document(uid).Collection("notifications")
                .OrderByDescending("createdAt").Limit(Limit)
                .Listen(snap => { personal.Clear(); foreach (var d in snap.Documents) personal.Add(Read(d, Source.Personal)); Loaded = true; Failed = false; dirty = true; });
            // 22 Nov 2026: which announcements this account has read or removed is kept in the account (users/{uid}/inboxState/state),
            // not only on this phone, so a new install or a second phone shows the same notifications and the same unread count
            stateListener = db.Collection("users").Document(uid).Collection("inboxState").Document("state")
                .Listen(snap =>
                {
                    if (snap == null || !snap.Exists) return;
                    var f = snap.ToDictionary();
                    foreach (string id in IdsOf(f, "hidden")) hidden.Add(id);
                    foreach (string id in IdsOf(f, "read")) readHere.Add(id);
                    SaveSet(HiddenKey + uid, hidden); SaveSet(ReadKey + uid, readHere);
                    dirty = true;
                });
            announcementListener = db.Collection("announcements").OrderByDescending("createdAt").Limit(Limit)
                .Listen(snap => { announcements.Clear(); foreach (var d in snap.Documents) announcements.Add(Read(d, Source.Announcement)); Loaded = true; Failed = false; dirty = true; retries = 0; });
            int mine = generation;
            Watch(personalListener, mine); Watch(stateListener, mine); Watch(announcementListener, mine);
        }

        async void Watch(ListenerRegistration listener, int mine)
        {
            if (listener == null) return;
            try { await listener.ListenerTask; }
            catch (Exception) { }                                               // ended with an error: attach again below
            if (this == null || mine != generation || string.IsNullOrEmpty(uid)) return;      // stopped on purpose (sign-out, new account)
            Failed = true; dirty = true;
            float wait = Mathf.Min(30f, 2f * (1 << Mathf.Min(retries, 4)));      // 2, 4, 8, 16, 30 seconds
            retries++;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(wait));
            if (this == null || mine != generation || string.IsNullOrEmpty(uid)) return;
            string account = uid;
            Stop();
            uid = account;
            Attach();
        }

        void Stop()
        {
            generation++;
            personalListener?.Stop(); personalListener = null;
            announcementListener?.Stop(); announcementListener = null;
            stateListener?.Stop(); stateListener = null;
            personal.Clear(); announcements.Clear();
        }

        void Update()
        {
            // listeners answer on Unity's main thread already; the change is announced once per frame
            if (!dirty) return;
            dirty = false;
            Changed?.Invoke();
        }

        void OnDestroy()
        {
            if (FirebaseBootstrap.Ready.IsCompleted && FirebaseBootstrap.Ready.Result) FirebaseAuth.DefaultInstance.StateChanged -= OnAuthChanged;
            Stop();
            if (Instance == this) Instance = null;
        }

        static Item Read(DocumentSnapshot d, Source source)
        {
            var f = d.ToDictionary();
            string S(string k) => f.TryGetValue(k, out object v) && v is string s ? s : "";
            var item = new Item { id = d.Id, source = source, kind = S("kind"), title = S("title"), body = S("body") };
            if (f.TryGetValue("createdAt", out object t) && t is Timestamp ts) item.createdAt = ts.ToDateTime().ToLocalTime();
            else item.createdAt = DateTime.Now;          // just written: the server time arrives a moment later
            item.read = f.TryGetValue("read", out object r) && r is bool b && b;
            return item;
        }

        /// <summary>Every notification still shown, newest first.</summary>
        public List<Item> Items()
        {
            return personal.Concat(announcements.Where(a => !hidden.Contains(a.id)).Select(a => { a.read = readHere.Contains(a.id); return a; }))
                           .OrderByDescending(i => i.createdAt).ToList();
        }

        public int UnreadCount => Items().Count(i => !i.read);

        public async void MarkRead(Item item)
        {
            if (item == null || item.read) return;
            item.read = true; dirty = true;
            if (item.source == Source.Announcement) { readHere.Add(item.id); SaveSet(ReadKey + uid, readHere); PushState("read", item.id); return; }
            try { await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).Collection("notifications").Document(item.id).UpdateAsync("read", true); }
            catch (Exception) { /* offline: the database library sends it later */ }
        }

        /// <summary>22 Nov 2026 (panel): every notification shown becomes read (one write for the announcements, one per reply).</summary>
        public void MarkAllRead()
        {
            foreach (Item item in Items()) if (!item.read) MarkRead(item);
            Changed?.Invoke();
        }

        /// <summary>22 Nov 2026 (panel): removes every notification shown (replies from the account, announcements from this phone's list). Returns how many failed.</summary>
        public async System.Threading.Tasks.Task<int> DeleteAll()
        {
            int failed = 0;
            foreach (Item item in Items()) if (!await Delete(item)) failed++;
            Changed?.Invoke();
            return failed;
        }

        /// <summary>Removes a notification: a reply from the account, an announcement from this phone's list. Returns false when it failed.</summary>
        public async System.Threading.Tasks.Task<bool> Delete(Item item)
        {
            if (item == null) return false;
            if (item.source == Source.Announcement) { hidden.Add(item.id); SaveSet(HiddenKey + uid, hidden); dirty = true; PushState("hidden", item.id); return true; }
            personal.RemoveAll(p => p.id == item.id); dirty = true;
            try { await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).Collection("notifications").Document(item.id).DeleteAsync(); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Adds one id to this account's read / hidden list (a merge: two phones adding at once keep both). Offline, the library sends it later.</summary>
        async void PushState(string field, string id)
        {
            if (string.IsNullOrEmpty(uid)) return;
            try
            {
                await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).Collection("inboxState").Document("state")
                    .SetAsync(new Dictionary<string, object> { { field, FieldValue.ArrayUnion(id) } }, SetOptions.MergeAll);
            }
            catch (Exception) { /* the phone keeps its own copy; the next change tries again */ }
        }

        static IEnumerable<string> IdsOf(IDictionary<string, object> f, string key)
        {
            if (f.TryGetValue(key, out object v) && v is IEnumerable<object> list)
                foreach (object o in list) if (o is string s && s.Length > 0 && s.Length <= 80) yield return s;
        }

        static HashSet<string> LoadSet(string key)
        {
            string raw = PlayerPrefs.GetString(key, "");
            return new HashSet<string>(raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        }
        static void SaveSet(string key, HashSet<string> set)
        {
            // only ids (no personal data); the newest 200 are plenty
            PlayerPrefs.SetString(key, string.Join("|", set.Reverse().Take(200)));
            PlayerPrefs.Save();
        }
    }
}
