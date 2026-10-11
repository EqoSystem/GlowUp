using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// Recognises a scanned product as one from the admin website's Brand &amp; Product Inventory.
    /// An ingredient label rarely shows the product's name, so the match is made on the ingredients:
    /// when the scanned list and a catalogue product's list are nearly the same, it is that product.
    /// A match gives the scan its real name, brand, type and shades, and adds 1 to the product's
    /// "App Scans" on the website.
    /// The three limits below are set from the Inspector of LoadingSceneController.
    /// </summary>
    public static class ProductMatcher
    {
        /// <summary>At least this many ingredients must be the same (a short list proves nothing).</summary>
        public static int MinimumShared = 4;
        /// <summary>At least this share (0-1) of the catalogue product's ingredients must be in the scan.</summary>
        public static float MinimumShareOfProduct = 0.7f;
        /// <summary>At least this share (0-1) of the scanned ingredients must belong to the catalogue product.</summary>
        public static float MinimumShareOfScan = 0.6f;

        /// <summary>
        /// The catalogue product this ingredient list belongs to, or null when none is close enough.
        /// `knownBrandId`: the brand whose name was read on the label, if any - products of other brands are then skipped.
        /// </summary>
        public static ProductData Find(List<string> scannedIngredients, List<ProductData> catalogue, string knownBrandId = null)
        {
            if (catalogue == null || catalogue.Count == 0) return null;
            HashSet<string> scan = Keys(scannedIngredients);
            int needed = System.Math.Max(1, MinimumShared);
            if (scan.Count < needed) return null;

            ProductData best = null;
            float bestScore = 0f;
            foreach (ProductData product in catalogue)
            {
                if (product == null || product.ingredients == null) continue;
                if (!string.IsNullOrEmpty(knownBrandId) && !string.IsNullOrEmpty(product.brandId) && product.brandId != knownBrandId) continue;

                HashSet<string> listed = Keys(product.ingredients);
                if (listed.Count < needed) continue;
                int shared = 0;
                foreach (string key in listed) if (scan.Contains(key)) shared++;
                if (shared < needed) continue;

                float ofProduct = shared / (float)listed.Count, ofScan = shared / (float)scan.Count;
                if (ofProduct < MinimumShareOfProduct || ofScan < MinimumShareOfScan) continue;

                float score = 2f * ofProduct * ofScan / (ofProduct + ofScan);      // 1 = identical lists
                if (score > bestScore) { bestScore = score; best = product; }
            }
            return best;
        }

        /// <summary>
        /// One comparable key per ingredient. Names are first resolved through the ingredient database, so
        /// "Aqua", "Water" and "Eau" (or a slightly misread spelling) count as the same ingredient.
        /// </summary>
        static HashSet<string> Keys(List<string> names)
        {
            var keys = new HashSet<string>();
            if (names == null) return keys;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                IngredientData known = IngredientTextParser.MatchEntry(name).data;
                string key = IngredientKnowledge.Key(known != null && !string.IsNullOrEmpty(known.name) ? known.name : name);
                if (key.Length > 0) keys.Add(key);
            }
            return keys;
        }
    }
}
