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
        public bool chronic;                     // conditions only: offered to users as a Chronic Skin Condition (the rest are only used by the ingredient rules)
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
        static ListItem Chronic(string id, string label, params string[] keywords) =>
            new ListItem { id = id, label = label, keywords = new List<string>(keywords), chronic = true };

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

        // The official lists (owner, 23 November 2026). The first 15 are the Chronic Skin Conditions users pick from; the rest are the
        // general Skin Conditions, used only by the ingredient rules. Ids never change (ingredients carry them); the ids that existed
        // before (acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact) keep their words, so old rules and saved profiles still match.
        static List<ListItem> BuiltInConditions() => new List<ListItem>
        {
            Chronic("atopic", "Atopic Dermatitis", "atopic", "eczema"),
            Chronic("psoriasis", "Psoriasis", "psoriasis"),
            Chronic("rosacea", "Rosacea", "rosacea"),
            Chronic("seborrheic", "Seborrheic Dermatitis", "seborrheic"),
            Chronic("allergiccontact", "Allergic Contact Dermatitis", "allergic contact", "contact"),
            Chronic("irritantcontact", "Irritant Contact Dermatitis", "irritant contact", "contact"),
            Chronic("hidradenitis", "Hidradenitis Suppurativa", "hidradenitis"),
            Chronic("lichenplanus", "Lichen Planus", "lichen planus"),
            Chronic("chronicurticaria", "Chronic Urticaria", "chronic urticaria", "urticaria", "hives"),
            Chronic("alopecia", "Alopecia Areata", "alopecia"),
            Chronic("vitiligo", "Vitiligo", "vitiligo"),
            Chronic("prurigo", "Chronic Prurigo", "prurigo"),
            Chronic("lupus", "Cutaneous Lupus Erythematosus", "lupus"),
            Chronic("lichensclerosus", "Lichen Sclerosus", "lichen sclerosus"),
            Chronic("ichthyosis", "Ichthyosis", "ichthyosis"),
            Make("acne", "Acne", "acne"),
            Make("eczema", "Eczema", "eczema", "atopic"),
            Make("contact", "Contact Dermatitis", "contact"),
            Make("folliculitis", "Folliculitis", "folliculitis"),
            Make("fungal", "Fungal Skin Infection", "fungal", "tinea", "ringworm"),
            Make("bacterial", "Bacterial Skin Infection", "bacterial"),
            Make("viral", "Viral Skin Infection", "viral"),
            Make("urticaria", "Urticaria (Hives)", "urticaria", "hives"),
            Make("melasma", "Melasma", "melasma", "hyperpigmentation"),
            Make("hyperpigmentation", "Hyperpigmentation", "hyperpigmentation"),
            Make("keratosispilaris", "Keratosis Pilaris", "keratosis pilaris"),
            Make("scabies", "Scabies", "scabies"),
            Make("impetigo", "Impetigo", "impetigo"),
            Make("warts", "Skin Warts", "wart"),
            Make("coldsores", "Cold Sores", "cold sore")
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

        /// <summary>The words for an allergen family id ("fragrance" -> "Fragrance / Parfum"); the id itself when the list does not know it.</summary>
        public static string AllergenLabel(string id) => LabelIn(AllergenItems, id);
        /// <summary>The words for a highlight id; the id itself when the list does not know it.</summary>
        public static string HighlightLabel(string id) => LabelIn(HighlightItems, id);
        static string LabelIn(List<ListItem> items, string id)
        {
            ListItem found = items.FirstOrDefault(i => string.Equals(i.id, id, StringComparison.OrdinalIgnoreCase));
            return found != null && !string.IsNullOrWhiteSpace(found.label) ? found.label : id;
        }

        /// <summary>The allergen families to offer (not retired), in the admin's order.</summary>
        public static List<string> AllergenLabels() => AllergenItems.Where(i => !i.retired && !string.IsNullOrWhiteSpace(i.label)).Select(i => i.label).ToList();

        /// <summary>The skin conditions to offer (not retired), in the admin's order.</summary>
        public static List<string> ConditionLabels()
        {
            List<ListItem> all = ConditionItems;
            bool anyFlag = all.Any(i => i.chronic);         // a list saved before the chronic flag existed offers every condition, as it always did
            return all.Where(i => !i.retired && !string.IsNullOrWhiteSpace(i.label) && (!anyFlag || i.chronic)).Select(i => i.label).ToList();
        }

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
            catch (Exception e) { TaskLimits.NoteFailure(e); { } }
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
                if (map.TryGetValue("chronic", out object c) && c is bool chronic) item.chronic = chronic;
                if (map.TryGetValue("keywords", out object k) && k is IEnumerable words && !(k is string))
                    foreach (object w in words) if (w != null && w.ToString().Trim().Length > 0) item.keywords.Add(w.ToString().Trim().ToLowerInvariant());
                items.Add(item);
            }
            return items;
        }
    }
}
