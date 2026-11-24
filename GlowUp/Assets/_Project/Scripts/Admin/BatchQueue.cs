using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// The staff batch: many products captured in a row (Batch Mode in Scanner Preferences: scanning goes on after each barcode, or
    /// several gallery photos at once, one product per photo). Every item is its own CaptureDraft with its own capture id, so each is
    /// sent as ONE record and a retry never makes a second one. Items that have a name, a brand and a make-up category are "Ready";
    /// the rest need details (the staff member opens them one by one). "Submit ready" sends the ready ones, one after another, and
    /// each lands in the website's Waiting for review like a single capture. The text of unsent items is kept on the phone (this
    /// account only); photos are kept in memory for the session.
    /// </summary>
    public static class BatchQueue
    {
        public const int MaxItems = 300;       // 24 Nov 2026: a file import can hold hundreds of products

        public class Item
        {
            public CaptureDraft draft;
            public Texture2D photo;                    // may be null (live barcode scans have no photo)
            public string state = "";                  // "", "sending", "sent", "failed"
            public string message = "";
            public bool Sent => state == "sent";
            public bool Ready => Complete(draft);
        }

        public static readonly List<Item> Items = new List<Item>();
        public static event Action Changed;
        static string loadedFor = "";
        static bool sending;

        [Serializable] class Store { public List<CaptureDraft> drafts = new List<CaptureDraft>(); }

        static string PathOf(string uid) => Path.Combine(Application.persistentDataPath, "staff_batch_" + uid + ".json");

        /// <summary>The same test the single capture uses: a name, a brand, a category, and not clearly something other than make-up.</summary>
        public static bool Complete(CaptureDraft d)
        {
            if (d == null || !d.Get("name").Has || !d.Get("brand").Has || !d.Get("cosmeticType").Has) return false;
            string type = d.Get("cosmeticType").value, name = d.Get("name").value;
            return !ProductScope.Judge(type, name).IsNotMakeup;          // the user's live-scan rule: only a clearly not-make-up product is refused
        }

        public static void EnsureLoaded()
        {
            string uid = StaffAccess.Uid ?? "";
            if (loadedFor == uid) return;
            loadedFor = uid;
            Items.Clear();
            try
            {
                string path = PathOf(uid);
                if (string.IsNullOrEmpty(uid) || !File.Exists(path)) return;
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(path));
                if (store?.drafts == null) return;
                foreach (CaptureDraft d in store.drafts) if (d != null && d.uid == uid && !string.IsNullOrEmpty(d.captureId)) { d.batchItem = true; Items.Add(new Item { draft = d }); }
            }
            catch (Exception) { }
        }

        public static int Count { get { EnsureLoaded(); int n = 0; foreach (Item i in Items) if (!i.Sent) n++; return n; } }
        public static int ReadyCount { get { EnsureLoaded(); int n = 0; foreach (Item i in Items) if (!i.Sent && i.state != "sending" && i.Ready) n++; return n; } }
        public static int NeedDetails { get { EnsureLoaded(); int n = 0; foreach (Item i in Items) if (!i.Sent && !i.Ready) n++; return n; } }
        public static bool IsSending => sending;

        public static bool Contains(string barcode)
        {
            EnsureLoaded();
            string digits = new string(Array.FindAll((barcode ?? "").ToCharArray(), char.IsDigit));
            if (digits.Length == 0) return false;
            foreach (Item i in Items)
            {
                string b = new string(Array.FindAll(i.draft.Get("barcode").value.ToCharArray(), char.IsDigit));
                if (b.Length > 0 && b == digits) return true;
            }
            return false;
        }

        public static bool Full { get { EnsureLoaded(); return Items.Count >= MaxItems; } }

        public static Item Add(CaptureDraft d, Texture2D photo)
        {
            EnsureLoaded();
            d.batchItem = true;
            var item = new Item { draft = d, photo = photo };
            Items.Insert(0, item);
            Save(); Changed?.Invoke();
            return item;
        }

        /// <summary>One gallery photo becomes one product: its barcode is looked up; without a barcode its text is read.</summary>
        public static async Task<Item> AddPhoto(Texture2D photo)
        {
            CaptureDraft d = CaptureDraft.Blank(CaptureDraft.Mode.Gallery);
            string code = await CaptureReader.Barcode(photo);
            if (!string.IsNullOrEmpty(OpenBeautyFacts.Digits(code))) await CaptureReader.FromBarcode(d, code, true);
            else
            {
                string text = await CaptureReader.Text(photo);
                if (!string.IsNullOrWhiteSpace(text)) CaptureReader.FromText(d, "Front", text);
            }
            return Add(d, photo);
        }

        public static void Remove(Item item)
        {
            if (item == null) return;
            if (item.photo != null) UnityEngine.Object.Destroy(item.photo);
            Items.Remove(item);
            Save(); Changed?.Invoke();
        }

        public static void Clear()
        {
            foreach (Item i in Items) if (i.photo != null) UnityEngine.Object.Destroy(i.photo);
            Items.Clear();
            Save(); Changed?.Invoke();
        }

        public static void Touch() { Save(); Changed?.Invoke(); }

        /// <summary>Sends every ready item, one after another. Returns how many were accepted (sent or queued by Firestore).</summary>
        public static async Task<int> SubmitReady()
        {
            EnsureLoaded();
            if (sending) return 0;
            sending = true;
            int sent = 0;
            try
            {
                foreach (Item item in new List<Item>(Items))
                {
                    if (item.Sent || !item.Ready) continue;
                    item.state = "sending"; item.message = ""; Changed?.Invoke();
                    // the evidence photos of a submission are read from CaptureDraft.Photos: put this item's photo there for the call
                    var keep = new List<KeyValuePair<string, Texture2D>>(CaptureDraft.Photos);
                    CaptureDraft.Photos.Clear();
                    if (item.photo != null) CaptureDraft.Photos.Add(new KeyValuePair<string, Texture2D>(item.draft.ModeValue == CaptureDraft.Mode.Barcode ? "Barcode" : "Front", item.photo));
                    SubmissionStore.Outcome o;
                    try { o = await SubmissionStore.Submit(item.draft); }
                    catch (Exception) { o = new SubmissionStore.Outcome { message = "It was not sent. Check the connection." }; }
                    finally { CaptureDraft.Photos.Clear(); CaptureDraft.Photos.AddRange(keep); }
                    if (o.confirmed || o.queued)
                    {
                        item.state = "sent"; item.draft.submittedId = o.id; item.draft.submittedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        item.message = o.confirmed ? "" : "Not yet synced";
                        sent++;
                    }
                    else { item.state = "failed"; item.message = o.message; }
                    Save(); Changed?.Invoke();
                }
            }
            finally { sending = false; Changed?.Invoke(); }
            SubmissionStore.Start();
            return sent;
        }

        /// <summary>Removes the items that were sent (the list shows what is still to do).</summary>
        public static void DropSent()
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].Sent) { if (Items[i].photo != null) UnityEngine.Object.Destroy(Items[i].photo); Items.RemoveAt(i); }
            Save(); Changed?.Invoke();
        }

        static void Save()
        {
            string uid = StaffAccess.Uid ?? "";
            if (string.IsNullOrEmpty(uid)) return;
            try
            {
                var store = new Store();
                foreach (Item i in Items) if (!i.Sent) store.drafts.Add(i.draft);
                if (store.drafts.Count == 0) { if (File.Exists(PathOf(uid))) File.Delete(PathOf(uid)); return; }
                File.WriteAllText(PathOf(uid), JsonUtility.ToJson(store));
            }
            catch (Exception) { }
        }
    }
}
