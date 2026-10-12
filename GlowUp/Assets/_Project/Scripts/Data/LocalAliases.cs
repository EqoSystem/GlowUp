using System;
using System.Collections.Generic;

namespace GlowUp.Data
{
    /// <summary>
    /// Names THIS PHONE has learned for a product. When the user types a name on the scan screen ("fitme foundation")
    /// and then picks the product that was meant from the shortlist, the typed name is kept here, so the next time
    /// that name finds the product at once.
    ///
    /// They stay on the phone (the newest 300): nothing is sent to the database, and the website's own "aliases" of a
    /// product are not touched. ProductRepository hands them to its look-up table (ProductIndex).
    /// </summary>
    public static class LocalAliases
    {
        const string CacheKey = "product_aliases";
        const int Limit = 300;
        /// <summary>A name shorter than this many letters and digits is not kept (it would match too much).</summary>
        public const int ShortestName = 4;

        [Serializable] class Entry { public string alias = "", productId = ""; public long learnedAtUnix; }
        [Serializable] class Holder { public List<Entry> items = new List<Entry>(); }

        static Holder holder;
        static Holder H() { if (holder == null && !OfflineCache.TryLoad(CacheKey, out holder)) holder = new Holder(); return holder ?? (holder = new Holder()); }

        public static int Count => H().items.Count;

        /// <summary>The learned names, each with the id of its product (the newest wins when a name was learned twice).</summary>
        public static Dictionary<string, string> All()
        {
            var all = new Dictionary<string, string>();
            foreach (Entry entry in H().items)
                if (entry != null && !string.IsNullOrEmpty(entry.alias) && !string.IsNullOrEmpty(entry.productId)) all[entry.alias] = entry.productId;
            return all;
        }

        /// <summary>
        /// True when a typed name is worth keeping as another name of this product: it is long enough, and it is not
        /// simply the product's own name (or one of the names the website already lists for it).
        /// </summary>
        public static bool WorthKeeping(string typedName, ProductData product)
        {
            string key = TextKey.Normalize(typedName);
            if (product == null || string.IsNullOrEmpty(product.id) || TextKey.Compact(typedName).Length < ShortestName) return false;
            if (key == TextKey.Normalize(product.name)) return false;
            if (product.aliases != null) foreach (string alias in product.aliases) if (key == TextKey.Normalize(alias)) return false;
            return true;
        }

        /// <summary>Keeps `typedName` as another name of `product`. Returns false when there was nothing to learn.</summary>
        public static bool Learn(string typedName, ProductData product)
        {
            if (!WorthKeeping(typedName, product)) return false;
            string key = TextKey.Normalize(typedName);
            List<Entry> items = H().items;
            items.RemoveAll(old => old == null || TextKey.Normalize(old.alias) == key);
            items.Add(new Entry { alias = typedName.Trim(), productId = product.id, learnedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            while (items.Count > Limit) items.RemoveAt(0);
            OfflineCache.Save(CacheKey, H());
            ProductRepository.AliasesChanged();
            return true;
        }
    }
}
