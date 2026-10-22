using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>One entry of an editable list: an allergen family, a skin condition or a highlight.</summary>
    [Serializable]
    public class ListItem
    {
        public string id = "";                   // never changes: ingredients store it (allergenTags, avoidForConditions, highlights)
        public string label = "";                // what users read; the admin can rename it
        public List<string> keywords = new List<string>();   // lower-case words the app looks for in what a user saved
        public bool retired;                     // hidden from the pickers, but still matches what is already saved
    }

    [Serializable]
    public class ListsHolder
    {
        public long version;
        public long savedAtUnix;
        public List<ListItem> allergens = new List<ListItem>();
        public List<ListItem> conditions = new List<ListItem>();
        public List<ListItem> highlights = new List<ListItem>();
    }

    /// <summary>
    /// The lists the admin website edits (Ingredient Curator, "Edit list"): Allergen Family, Skin Conditions and Highlights.
    ///
    ///   They live in ONE document, <c>config/lists</c>. The app reads it about once per list check (see CatalogSync.RefreshMinutes),
    ///   keeps a copy on the phone, and uses the lists built into the app when the document is missing or cannot be reached.
    ///   Cost: 1 read per check (counted by ReadCounter), nothing more.
    ///
    ///   Ids never change. Ingredients carry ids; a user's saved profile carries the label text the user ticked. The app finds
    ///   the item of a saved text again through its keywords (the website keeps a renamed item's old label as a keyword).
    ///   A retired item is not offered any more, but it still matches what is saved.
    /// </summary>
    public static class ListsRepository
    {
        const string CacheKey = "config_lists";
        const string BuiltInHighlightKeyActive = "keyActive", BuiltInHighlightUmbrella = "umbrella";

        static ListsHolder holder;               // the copy from the phone or the cloud; null = the lists built into the app
        static bool fromPhoneTried;
        static float lastCheck = -1f;            // Time.realtimeSinceStartup of the last look that got an answer
        static Task running;

        /// <summary>Raised on the main context when a changed list arrived from the cloud.</summary>
        public static event Action Changed;

        /// <summary>Goes up by one whenever the lists change.</summary>
        public static int Version { get; private set; }

        // ------------------------------------------------------------------ the lists built into the app (also the fallback)

        static ListItem Make(string id, string label, params string[] keywords) =>
            new ListItem { id = id, label = label, keywords = new List<string>(keywords) };

        static List<ListItem> BuiltInAllergens() => new List<ListItem>
        {
            Make("fragrance", "Fragrance / Parfum", "fragrance", "parfum", "perfume"),
            Make("paraben", "Parabens", "paraben"),
            Make("sulfate", "Sulfates (SLS / SLES)", "sulfate", "sulphate", "sls", "sles"),
            Make("formaldehyde", "Formaldehyde releasers", "formaldehyde"),
            Make("lanolin", "Lanolin", "lanolin"),
            Make("essentialoil", "Essential oils", "essential oil", "essential oils"),
            Make("nut", "Nut-derived oils", "nut")
        };

        static List<ListItem> BuiltInConditions() => new List<ListItem>
        {
            Make("acne", "Acne", "acne"),
            Make("eczema", "Eczema (Atopic Dermatitis)", "eczema", "atopic"),
            Make("psoriasis", "Psoriasis", "psoriasis"),
            Make("rosacea", "Rosacea", "rosacea"),
            Make("seborrheic", "Seborrheic Dermatitis", "seborrheic"),
            Make("melasma", "Melasma / Hyperpigmentation", "melasma", "hyperpigmentation"),
            Make("contact", "Contact Dermatitis", "contact")
        };

        static List<ListItem> BuiltInHighlights() => new List<ListItem>
        {
            Make(BuiltInHighlightKeyActive, "Key active"),
            Make(BuiltInHighlightUmbrella, "Umbrella term")
        };

        // ------------------------------------------------------------------ what the rest of the app asks

        static List<ListItem> AllergenItems { get { FromPhone(); return holder != null && holder.allergens.Count > 0 ? holder.allergens : BuiltInAllergens(); } }
        static List<ListItem> ConditionItems { get { FromPhone(); return holder != null && holder.conditions.Count > 0 ? holder.conditions : BuiltInConditions(); } }
        static List<ListItem> HighlightItems { get { FromPhone(); return holder != null && holder.highlights.Count > 0 ? holder.highlights : BuiltInHighlights(); } }

        /// <summary>The allergen families to offer (not retired), in the admin's order.</summary>
        public static List<string> AllergenLabels() => AllergenItems.Where(i => !i.retired && !string.IsNullOrWhiteSpace(i.label)).Select(i => i.label).ToList();

        /// <summary>The skin conditions to offer (not retired), in the admin's order.</summary>
        public static List<string> ConditionLabels() => ConditionItems.Where(i => !i.retired && !string.IsNullOrWhiteSpace(i.label)).Select(i => i.label).ToList();

        /// <summary>
        /// Every allergen family, retired ones too, as { id, word, word, ... } with lower-case words: a user's saved allergen
        /// that contains one of the words belongs to that family. (Same shape the analysis used when the list was fixed.)
        /// </summary>
        public static string[][] AllergenFamilies()
        {
            return AllergenItems.Where(i => !string.IsNullOrWhiteSpace(i.id))
                .Select(i => new[] { i.id }.Concat(WordsOf(i)).ToArray()).ToArray();
        }

        /// <summary>The words that stand for a condition id (the id itself and the item's keywords), lower case.</summary>
        public static List<string> ConditionWords(string conditionId)
        {
            string id = (conditionId ?? "").Trim().ToLowerInvariant();
            var words = new List<string>();
            if (id.Length > 0) words.Add(id);
            foreach (ListItem item in ConditionItems)
                if (string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase))
                    foreach (string w in WordsOf(item)) if (!words.Contains(w)) words.Add(w);
            return words;
        }

        /// <summary>True when an ingredient carries a custom highlight that is still offered (not retired). Such an ingredient shows up among a result's chips like a key active.</summary>
        public static bool HasActiveHighlight(List<string> ingredientHighlights)
        {
            if (ingredientHighlights == null || ingredientHighlights.Count == 0) return false;
            foreach (string id in ingredientHighlights)
                foreach (ListItem item in HighlightItems)
                    if (!item.retired && string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(item.id, BuiltInHighlightKeyActive, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(item.id, BuiltInHighlightUmbrella, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The words of an item: its keywords, or its label when it has none. Lower case, no empty ones.</summary>
        static IEnumerable<string> WordsOf(ListItem item)
        {
            var words = (item.keywords ?? new List<string>()).Select(w => (w ?? "").Trim().ToLowerInvariant()).Where(w => w.Length > 0).Distinct().ToList();
            if (words.Count == 0 && !string.IsNullOrWhiteSpace(item.label)) words.Add(item.label.Trim().ToLowerInvariant());
            return words;
        }

        // ------------------------------------------------------------------ the copy on the phone and the cloud

        static void FromPhone()
        {
            if (fromPhoneTried) return;
            fromPhoneTried = true;
            if (holder == null && OfflineCache.TryLoad(CacheKey, out ListsHolder saved) && saved != null)
            {
                saved.allergens = saved.allergens ?? new List<ListItem>();
                saved.conditions = saved.conditions ?? new List<ListItem>();
                saved.highlights = saved.highlights ?? new List<ListItem>();
                holder = saved;
            }
        }

        /// <summary>
        /// Looks at the cloud's lists when the copy has not been checked for a few minutes (or <paramref name="force"/>).
        /// Never throws: without a connection the copy on the phone, or the lists built into the app, are used.
        /// </summary>
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
                DocumentSnapshot snapshot = await FirebaseFirestore.DefaultInstance.Collection("config").Document("lists")
                    .GetSnapshotAsync(Source.Server).OrTimeout(8f, "Downloading the lists");
                ReadCounter.Add("config/lists", 1);
                lastCheck = Time.realtimeSinceStartup;
                ConnectivityMonitor.ReportAnswer();
                if (!snapshot.Exists)
                {
                    if (holder != null) { holder = null; OfflineCache.Save(CacheKey, new ListsHolder()); Version++; Changed?.Invoke(); }
                    return;
                }
                Dictionary<string, object> data = snapshot.ToDictionary();
                long version = data.TryGetValue("version", out object v) && v != null && long.TryParse(v.ToString(), out long parsed) ? parsed : 0;
                if (holder != null && holder.version == version) return;      // nothing changed since the copy on the phone
                var next = new ListsHolder
                {
                    version = version, savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    allergens = ReadItems(data, "allergens"), conditions = ReadItems(data, "conditions"), highlights = ReadItems(data, "highlights")
                };
                holder = next;
                OfflineCache.Save(CacheKey, holder);
                Version++;
                Changed?.Invoke();
            }
            catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("The lists could not be downloaded, using the copy on the phone: " + e.Message); }
        }

        static List<ListItem> ReadItems(Dictionary<string, object> data, string key)
        {
            var items = new List<ListItem>();
            if (!data.TryGetValue(key, out object raw) || !(raw is IEnumerable list) || raw is string) return items;
            foreach (object entry in list)
            {
                if (!(entry is Dictionary<string, object> map)) continue;
                string id = map.TryGetValue("id", out object i) && i != null ? i.ToString().Trim() : "";
                string label = map.TryGetValue("label", out object l) && l != null ? l.ToString().Trim() : "";
                if (id.Length == 0 || label.Length == 0) continue;
                var item = new ListItem { id = id, label = label };
                if (map.TryGetValue("retired", out object r) && r is bool retired) item.retired = retired;
                if (map.TryGetValue("keywords", out object k) && k is IEnumerable words && !(k is string))
                    foreach (object w in words) if (w != null && w.ToString().Trim().Length > 0) item.keywords.Add(w.ToString().Trim().ToLowerInvariant());
                items.Add(item);
            }
            return items;
        }
    }
}
