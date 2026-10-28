using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// "IS IT ONE OF THESE?" The few products a scan may be about, best first, for the user to choose from.
    ///
    /// The app only takes a product for granted when it is sure: its barcode, or its whole name read on the pack.
    /// Everything less certain is put before the user instead of being guessed:
    ///   - products whose name can partly be read in the text on the pack (ProductIndex.InText),
    ///   - products this phone has scanned before whose pack looks like this one (PackagingMemory),
    ///   - products that fit what the user types into the search box (also half-typed, and by a name this phone learned).
    /// A product that is found by its look AND by its name comes first. Slow and right is better than fast and wrong:
    /// nothing in this list is used until the user taps it.
    ///
    /// Pure logic (no screen, no storage), so it is tested in Editor/Tests/RecognitionTests.cs.
    /// </summary>
    public static class ScanShortlist
    {
        /// <summary>One product offered to the user.</summary>
        public class Entry
        {
            /// <summary>The inventory's product, or null for a product only this phone remembers.</summary>
            public ProductData product;
            public string productName = "", brandName = "", cosmeticType = "";
            /// <summary>0 to 1: how well it fits.</summary>
            public float score;
            /// <summary>Found by the look of the pack / by words read on the pack / by what the user typed.</summary>
            public bool byLook, byText, byTyping;
            /// <summary>The remembered look it was found by (null otherwise).</summary>
            public PackagingMemory.Look look;

            /// <summary>The same product gets the same key, whichever way it was found.</summary>
            public string Key => TextKey.Normalize(brandName) + "|" + TextKey.Normalize(productName);
            /// <summary>"Maybelline, Foundation": the line under the product's name.</summary>
            public string Detail
            {
                get
                {
                    string brand = (brandName ?? "").Trim(), kind = (cosmeticType ?? "").Trim();
                    return brand.Length > 0 && kind.Length > 0 ? brand + ", " + kind : brand.Length > 0 ? brand : kind;
                }
            }
        }

        /// <summary>How many products are offered at most.</summary>
        public static int Maximum = 3;
        /// <summary>The score of a product found by a strong / a medium likeness of its pack.</summary>
        public static float LookStrong = 0.9f, LookMedium = 0.6f;
        /// <summary>Added when a word of the product's name was read on the pack as well, and when both the look and the text point to it.</summary>
        public static float WordBonus = 0.15f, BothBonus = 0.1f;

        /// <summary>
        /// The products a scan may be about: from the text read on the front of the pack and from the packs this phone
        /// remembers. `declined` = the keys of products the user already said "no" to in this scan.
        /// </summary>
        public static List<Entry> For(ProductIndex index, string frontText, IList<PackagingMemory.Match> looks, ICollection<string> declined = null, int maximum = 0)
        {
            if (maximum <= 0) maximum = Maximum;
            var found = new List<Entry>();
            if (index != null && !string.IsNullOrWhiteSpace(frontText))
                foreach (ProductIndex.Candidate candidate in index.InText(frontText, maximum + 3))
                {
                    if (candidate.score < ProductIndex.Possible) continue;
                    Entry entry = Of(candidate.product); entry.score = Math.Min(1f, candidate.score); entry.byText = true;
                    Add(found, entry);
                }
            if (looks != null)
                foreach (PackagingMemory.Match match in looks)
                {
                    if (match == null || match.look == null || match.strength == PackagingMemory.Strength.None) continue;
                    ProductData known = index != null ? index.ById(match.look.productId) ?? index.Exact(match.look.brandName, match.look.productName) : null;
                    Entry entry = known != null ? Of(known) : new Entry
                    {
                        productName = match.look.productName ?? "", brandName = match.look.brandName ?? "", cosmeticType = match.look.cosmeticType ?? ""
                    };
                    if (entry.productName.Trim().Length == 0) continue;
                    entry.byLook = true; entry.look = match.look;
                    entry.score = Math.Min(1f, (match.strength == PackagingMemory.Strength.Strong ? LookStrong : LookMedium) + (match.wordSeen ? WordBonus : 0f));
                    Add(found, entry);
                }
            if (declined != null && declined.Count > 0) found.RemoveAll(entry => declined.Contains(entry.Key));
            return Best(found, maximum);
        }

        /// <summary>
        /// The products that fit what the user typed into the search box, also while it is only half typed: the closest
        /// names first (a name this phone learned counts as the product's own), then every product whose name holds all
        /// the typed words. A typed brand leaves out the products of other brands.
        /// </summary>
        public static List<Entry> Typed(ProductIndex index, string typedName, string typedBrand, int maximum = 0)
        {
            if (maximum <= 0) maximum = Maximum;
            var found = new List<Entry>();
            string[] nameWords = TextKey.Words(typedName), brandWords = TextKey.Words(typedBrand);
            if (index == null || (nameWords.Length == 0 && brandWords.Length == 0)) return found;

            if (nameWords.Length > 0)
            {
                ProductData exact = index.Exact(typedBrand, typedName);
                if (exact != null) { Entry entry = Of(exact); entry.score = 1f; entry.byTyping = true; Add(found, entry); }
                foreach (ProductIndex.Candidate candidate in index.Search(typedBrand, typedName, maximum + 3))
                {
                    if (candidate.score < ProductIndex.Possible) continue;
                    // a name that is only close (a typing mistake, a few of its words) stays below a name that holds every typed word
                    Entry entry = Of(candidate.product); entry.score = Math.Min(CloseNameAtMost, candidate.score); entry.byTyping = true;
                    Add(found, entry);
                }
            }
            // still typing: every typed word must start a word of the name (three letters and more may also stand inside one)
            int typedLetters = LettersOf(nameWords);
            foreach (ProductData product in index.Products)
            {
                if (product == null) continue;
                string[] itsBrand = TextKey.Words(product.brandName), itsName = TextKey.Words(product.name);
                if (brandWords.Length > 0 && !AllIn(brandWords, itsBrand, null)) continue;
                // without a brand box filled in, a typed word may also be a word of the brand ("maybelline fit me")
                if (nameWords.Length > 0 && !AllIn(nameWords, itsName, brandWords.Length == 0 ? itsBrand : null)) continue;
                // the more of the name is typed, the better it fits (a brand alone gives every product of the brand the same score)
                Entry entry = Of(product); entry.byTyping = true;
                entry.score = HoldsEveryWord + (0.95f - HoldsEveryWord) * typedLetters / Math.Max(1, Math.Max(typedLetters, LettersOf(itsName)));
                Add(found, entry);
            }
            return Best(found, maximum);
        }

        // the search box: a name that holds every typed word scores from 0.7 up (0.95 when the whole name is typed);
        // a name that is only close to what was typed scores below that; the exact name scores 1
        const float HoldsEveryWord = 0.7f, CloseNameAtMost = 0.69f;

        static int LettersOf(string[] words) { int n = 0; foreach (string word in words) n += word.Length; return n; }

        static bool AllIn(string[] typed, string[] words, string[] moreWords)
        {
            foreach (string part in typed)
                if (!In(part, words) && !(moreWords != null && In(part, moreWords))) return false;
            return typed.Length > 0;
        }

        static bool In(string typed, string[] words)
        {
            foreach (string word in words)
                if (word.StartsWith(typed, StringComparison.Ordinal) || (typed.Length >= 3 && word.IndexOf(typed, StringComparison.Ordinal) >= 0)) return true;
            return false;
        }

        public static Entry Of(ProductData product)
        {
            return new Entry { product = product, productName = (product.name ?? "").Trim(), brandName = (product.brandName ?? "").Trim(), cosmeticType = (product.cosmeticType ?? "").Trim() };
        }

        /// <summary>Adds an entry; a product that is in the list already is not added twice (what the two ways of finding it say is put together).</summary>
        static void Add(List<Entry> found, Entry entry)
        {
            string key = entry.Key;
            // two products of the inventory with the same name and a different kind ("Fit Me": a foundation and a concealer) are two choices
            Entry had = found.Find(other => other.Key == key && !(other.product != null && entry.product != null && other.product.id != entry.product.id));
            if (had == null) { found.Add(entry); return; }
            // the look of the pack and the words on it point to the same product: that counts for more than either alone
            bool another = (entry.byLook && !had.byLook) || (entry.byText && !had.byText);
            bool lookAndText = (had.byLook || entry.byLook) && (had.byText || entry.byText);
            had.score = Math.Min(1f, Math.Max(had.score, entry.score) + (another && lookAndText ? BothBonus : 0f));
            had.byLook |= entry.byLook; had.byText |= entry.byText; had.byTyping |= entry.byTyping;
            if (had.look == null) had.look = entry.look;
            if (had.product == null && entry.product != null) { had.product = entry.product; had.cosmeticType = entry.cosmeticType; }
        }

        static List<Entry> Best(List<Entry> found, int maximum)
        {
            found.Sort((x, y) =>
            {
                if (Math.Abs(y.score - x.score) > 0.001f) return y.score.CompareTo(x.score);
                int ways = Ways(y).CompareTo(Ways(x));                                     // found in more ways first
                if (ways != 0) return ways;
                int length = x.productName.Length.CompareTo(y.productName.Length);        // the shorter name fits what was typed more closely
                return length != 0 ? length : string.CompareOrdinal(x.Key, y.Key);
            });
            if (found.Count > maximum) found.RemoveRange(maximum, found.Count - maximum);
            return found;
        }

        static int Ways(Entry entry) => (entry.byLook ? 1 : 0) + (entry.byText ? 1 : 0) + (entry.byTyping ? 1 : 0);
    }
}
