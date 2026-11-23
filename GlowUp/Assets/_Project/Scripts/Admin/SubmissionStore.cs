using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Data;
using GlowUp.Scan;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// The staff submissions, in ONE place with the website: a submission IS a product document (products/{captureId}) saved
    /// Pending with reviewed = false, which is exactly the website's "Waiting for review" queue. The website's review actions
    /// (Approve, Send back, Reject, edits) change that same document, and this class listens to it live (Firestore snapshot
    /// listener, scoped to the staff member's own documents: addedBy == uid). Nothing here invents a status: what the app
    /// shows is what the server says.
    ///
    /// Photos: the free plan has no file storage, so up to 3 small JPEGs go to captureEvidence/{captureId} (admins only,
    /// see firestore.rules). The product itself never carries pictures (every phone downloads the catalogue).
    /// </summary>
    public static class SubmissionStore
    {
        // ================= live list =================
        public class Info
        {
            public string id, name, brand, type, barcode, status, reviewState, reviewNote, method, source, shade;
            public int ingredientCount;
            public string ingredientsText;
            public long addedUnix;
            public bool reviewed, binned, pendingWrite;
            public string Label => LabelOf(this);
            public string Group => GroupOf(this);
        }

        public static readonly List<Info> Items = new List<Info>();
        /// <summary>"Synced", "Syncing", "Offline" or "Retry needed": from the listener's own metadata, never assumed.</summary>
        public static string SyncState { get; private set; } = "Offline";
        public static bool Loaded { get; private set; }
        public static string LastError { get; private set; } = "";
        public static event Action Changed;

        static ListenerRegistration listener;
        static string listeningFor = "";

        public static void Start()
        {
            string uid = StaffAccess.Uid;
            if (string.IsNullOrEmpty(uid) || (listener != null && listeningFor == uid)) return;
            Stop();
            listeningFor = uid;
            SyncState = "Syncing";
            try
            {
                Query q = FirebaseFirestore.DefaultInstance.Collection("products").WhereEqualTo("addedBy", uid).Limit(300);
                listener = q.Listen(MetadataChanges.Include, snap => GlowUp.AppSettings.MainThread.Run(() => Take(snap)));
            }
            catch (Exception e) { SyncState = "Retry needed"; LastError = e.Message; Changed?.Invoke(); }
        }

        public static void Stop()
        {
            try { listener?.Stop(); } catch (Exception) { }
            listener = null; listeningFor = ""; Loaded = false; Items.Clear(); SyncState = "Offline";
        }

        static void Take(QuerySnapshot snap)
        {
            if (snap == null) return;
            Items.Clear();
            foreach (DocumentSnapshot d in snap.Documents)
            {
                Dictionary<string, object> f = d.ToDictionary();
                var i = new Info
                {
                    id = d.Id, name = S(f, "name"), brand = S(f, "brandName"), type = S(f, "cosmeticType"), barcode = S(f, "barcode"),
                    status = S(f, "status"), reviewState = S(f, "reviewState"), reviewNote = S(f, "reviewNote"), method = S(f, "captureMethod"),
                    source = S(f, "source"), reviewed = B(f, "reviewed"), binned = B(f, "binned"), addedUnix = L(f, "addedAtUnix"),
                    pendingWrite = d.Metadata.HasPendingWrites
                };
                List<string> ingredients = Strings(f, "ingredients");
                i.ingredientCount = ingredients.Count; i.ingredientsText = string.Join(", ", ingredients);
                List<string> shades = Strings(f, "shadeCodes");
                i.shade = shades.Count > 0 ? shades[0] : "";
                Items.Add(i);
            }
            Items.Sort((a, b) => b.addedUnix.CompareTo(a.addedUnix));
            Loaded = true;
            SyncState = snap.Metadata.HasPendingWrites ? "Syncing" : snap.Metadata.IsFromCache ? "Offline" : "Synced";
            Changed?.Invoke();
        }

        public static Info Find(string id) { foreach (Info i in Items) if (i.id == id) return i; return null; }

        /// <summary>The status in the website's own words (products: status, reviewed, reviewState, binned).</summary>
        public static string LabelOf(Info i)
        {
            if (i.pendingWrite) return "Not yet synced";
            if (i.binned) return i.reviewState == "rejected" ? "Rejected" : "Removed";
            if (i.reviewState == "changes") return "Changes requested";
            if (!i.reviewed) return i.reviewState == "resubmitted" ? "Resubmitted" : "Pending review";
            if (i.status == "Active") return "Approved";
            return string.IsNullOrEmpty(i.status) ? "Pending review" : i.status;
        }
        public static string GroupOf(Info i)
        {
            string l = LabelOf(i);
            if (l == "Approved") return "Approved";
            if (l == "Rejected" || l == "Removed" || l == "Inactive") return "Rejected";
            return "Pending";
        }
        public static int Count(string group) { int n = 0; foreach (Info i in Items) if (group == "All" || i.Group == group) n++; return n; }
        public static int NeedsAttention() { int n = 0; foreach (Info i in Items) if (i.Label == "Changes requested") n++; return n; }

        // ================= submit =================
        public class Outcome { public bool confirmed, queued; public string id = "", message = ""; }

        /// <summary>
        /// Writes the submission. confirmed = the server accepted it (only then the app says "Submitted").
        /// queued = no answer in time: the write stays in Firestore's own queue and lands by itself (same id, no duplicate).
        /// </summary>
        public static async Task<Outcome> Submit(CaptureDraft d, float waitSeconds = 20f)
        {
            var o = new Outcome { id = d.captureId };
            string uid = StaffAccess.Uid;
            if (string.IsNullOrEmpty(uid)) { o.message = StaffAccess.Message(StaffAccess.Result.NotSignedIn); return o; }
            if (!await FirebaseBootstrap.Ready) { o.message = "GlowUp is not reachable. Try again when you are online."; return o; }
            FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string name = CaptureDraft.Limit(d.Get("name").value, 100);
            string brandName = CaptureDraft.Limit(d.Get("brand").value, 60);
            string type = CaptureDraft.Limit(d.Get("cosmeticType").value, 40);
            string barcode = CaptureDraft.Limit(new string(Array.FindAll(d.Get("barcode").value.ToCharArray(), char.IsDigit)), 14);
            string shadeCode = CaptureDraft.Limit(d.Get("shade").value.ToUpperInvariant(), 16);
            List<string> ingredients = SplitList(d.Get("ingredients").value);

            // ---- brand: a listed one by name, else a Pending brand for the website to check (never Active from here) ----
            string brandId = "";
            if (brandName.Length >= 2)
            {
                BrandData known = BrandRepository.FindByName(brandName);
                if (known != null) { brandId = known.id; brandName = known.name; }
                else
                {
                    brandId = ProductIndex.ScanBrandId(brandName);
                    if (brandId.Length > 0)
                    {
                        try
                        {
                            DocumentReference bref = db.Collection("brands").Document(brandId);
                            DocumentSnapshot existing = await bref.GetSnapshotAsync().OrTimeout(8f, "Checking the brand");
                            if (!existing.Exists)
                                await bref.SetAsync(new Dictionary<string, object>
                                {
                                    { "name", brandName }, { "category", "Makeup" }, { "market", "Global" }, { "status", "Pending" }, { "reviewed", false },
                                    { "source", "staff-capture" }, { "scans", 0 }, { "addedBy", uid }, { "addedAtUnix", now }, { "folder", "" },
                                    { "createdAt", FieldValue.ServerTimestamp }, { "updatedAt", FieldValue.ServerTimestamp }
                                }).OrTimeout(waitSeconds, "Saving the brand");
                        }
                        catch (Exception) { /* the product still goes; the website shows "brand not known" and the admin picks one */ }
                    }
                }
            }

            var ids = new List<string>();
            foreach (string ingredient in ingredients)
            {
                IngredientData data = IngredientRepository.Find(ingredient);
                if (data != null && !string.IsNullOrEmpty(data.id) && !ids.Contains(data.id) && ids.Count < 300) ids.Add(data.id);
            }

            var origins = new Dictionary<string, object>();
            foreach (CaptureDraft.Field f in d.fields)
            {
                string key = f.key == "shade" ? "" : f.key;              // the website keeps origins for its own form fields
                if (key.Length == 0 || !f.Has) continue;
                origins[key] = new Dictionary<string, object>
                {
                    { "state", string.IsNullOrEmpty(f.state) ? "detected" : f.state }, { "from", CaptureDraft.Limit(f.from, 80) },
                    { "method", string.IsNullOrEmpty(f.method) ? "lookup" : f.method }, { "atUnix", now }
                };
            }

            var sources = new List<object>();
            foreach (CaptureDraft.Field f in d.fields)
                if (f.Has && f.from.StartsWith("Open Beauty Facts") && barcode.Length > 0)
                { sources.Add("Open Beauty Facts | https://world.openbeautyfacts.org/product/" + barcode); break; }

            var tags = new List<object>();
            if (type.Length > 0) tags.Add(Slug(type));
            if (brandName.Length > 0) tags.Add(Slug(brandName));
            tags.Add("staff-capture");

            var product = new Dictionary<string, object>
            {
                { "name", name }, { "brandId", brandId }, { "brandName", brandName }, { "cosmeticType", type }, { "description", "" },
                { "ingredients", ToObjects(ingredients) }, { "ingredientIds", ToObjects(ids) }, { "ingredientCount", ingredients.Count },
                { "shades", new List<object>() }, { "shadeCodes", shadeCode.Length > 0 ? new List<object> { shadeCode } : new List<object>() },
                { "sources", sources }, { "tags", tags }, { "minAge", 0 }, { "status", "Pending" }, { "scans", 0 },
                { "source", "staff-capture" }, { "reviewed", false }, { "reviewState", "" }, { "addedBy", uid }, { "addedAtUnix", now }, { "folder", "" },
                { "fieldOrigins", origins }, { "ingredientsRaw", CaptureDraft.Limit(d.rawText, 4000) }, { "ingredientsRawFrom", d.rawText.Length > 0 ? CaptureDraft.Limit(d.rawFrom, 120) : "" },
                { "captureId", d.captureId }, { "captureMethod", d.mode }, { "evidenceCount", Math.Min(CaptureDraft.Photos.Count, 3) },
                { "possibleDuplicateOf", d.duplicateOf ?? "" },
                { "createdAt", FieldValue.ServerTimestamp }, { "updatedAt", FieldValue.ServerTimestamp }
            };
            if (barcode.Length > 0) product["barcode"] = barcode;

            DocumentReference pref = db.Collection("products").Document(d.captureId);
            Task write = pref.SetAsync(product);
            Task first = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(waitSeconds)));
            if (first != write) { o.queued = true; o.message = "No answer from the server yet. It is kept on this phone as \"Not yet synced\" and is sent by itself when the connection allows."; }
            else if (write.IsFaulted)
            {
                string why = write.Exception?.GetBaseException()?.Message ?? "";
                o.message = why.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "The server refused it: this account has no staff access." : "It was not submitted. Check the connection and try again.";
                return o;
            }
            else o.confirmed = true;

            // ---- evidence photos (best effort: the submission does not depend on them) ----
            if (o.confirmed && CaptureDraft.Photos.Count > 0)
            {
                try
                {
                    var photos = new List<object>();
                    int total = 0;
                    foreach (var p in CaptureDraft.Photos)
                    {
                        if (photos.Count >= 3 || p.Value == null) break;
                        string b64 = Convert.ToBase64String(SmallJpeg(p.Value, 640, 60));
                        if (total + b64.Length > 800000) break;                 // a document holds at most 1 MB
                        total += b64.Length;
                        photos.Add(new Dictionary<string, object> { { "role", p.Key }, { "jpeg", b64 } });
                    }
                    await db.Collection("captureEvidence").Document(d.captureId).SetAsync(new Dictionary<string, object>
                    { { "by", uid }, { "atUnix", now }, { "productId", d.captureId }, { "photos", photos } }).OrTimeout(waitSeconds, "Sending the photos");
                }
                catch (Exception) { o.message = "Submitted. The photos could not be attached; the reviewer sees the text that was read."; }
            }
            return o;
        }

        // ================= helpers =================
        public static List<string> SplitList(string text)
        {
            var list = new List<string>();
            var seen = new HashSet<string>();
            foreach (string raw in (text ?? "").Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = raw.Trim().Trim('.');
                if (n.Length < 2 || n.Length > 120 || !seen.Add(n.ToLowerInvariant())) continue;
                list.Add(n);
                if (list.Count >= 300) break;
            }
            return list;
        }

        static byte[] SmallJpeg(Texture2D source, int maxSide, int quality)
        {
            int w = source.width, h = source.height;
            float scale = Mathf.Min(1f, maxSide / (float)Mathf.Max(w, h));
            if (scale >= 1f) return source.EncodeToJPG(quality);
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            RenderTexture rt = RenderTexture.GetTemporary(nw, nh, 0);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
            var small = new Texture2D(nw, nh, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, nw, nh), 0, 0); small.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            byte[] jpg = small.EncodeToJPG(quality);
            UnityEngine.Object.Destroy(small);
            return jpg;
        }

        static List<object> ToObjects(List<string> list) { var o = new List<object>(); foreach (string s in list) o.Add(s); return o; }
        static string S(Dictionary<string, object> f, string k) => f.TryGetValue(k, out object v) && v != null ? v.ToString() : "";
        static bool B(Dictionary<string, object> f, string k) => f.TryGetValue(k, out object v) && v is bool b && b;
        static long L(Dictionary<string, object> f, string k) => f.TryGetValue(k, out object v) && v != null && long.TryParse(v.ToString(), out long n) ? n : 0;
        static List<string> Strings(Dictionary<string, object> f, string k)
        {
            var list = new List<string>();
            if (f.TryGetValue(k, out object v) && v is IEnumerable<object> e) foreach (object x in e) if (x != null) list.Add(x.ToString());
            return list;
        }
        static string Slug(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '-');
            string s = sb.ToString().Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Length > 30 ? s.Substring(0, 30).Trim('-') : s;
        }

        public static string Ago(long unix)
        {
            if (unix <= 0) return "";
            TimeSpan t = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
            if (t.TotalMinutes < 1) return "just now";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + "m ago";
            if (t.TotalDays < 1) return (int)t.TotalHours + "h ago";
            return (int)t.TotalDays + "d ago";
        }
    }
}
