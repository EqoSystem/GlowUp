using System;
using System.Collections.Generic;

namespace GlowUp.Data
{
    /// <summary>
    /// A look-up table over the product list, built once when the list is loaded, so that finding a product never means
    /// reading through the whole list (and never means asking the database):
    ///
    ///   by barcode            the digits under the barcode
    ///   by brand and name     both as comparable keys (capitals, accents, punctuation and spaces do not matter)
    ///   by alias              other names the product is known under (old names, misspellings, what a user confirmed)
    ///   by word               every word of every name, to find the closest products for a name that is not exact,
    ///                         or for the text the camera read on a pack
    ///
    /// Pure logic: no Unity, no internet. ProductRepository keeps two of these: one of the products the app may use
    /// (make-up, Active) and one of every product the database holds (to never add a product twice).
    /// </summary>
    public class ProductIndex
    {
        /// <summary>A product that may be the one looked for, with how sure the index is (0 to 1).</summary>
        public class Candidate
        {
            public ProductData product;
            public float score;
            /// <summary>The brand that was asked for is this product's brand.</summary>
            public bool brandAgrees;
            /// <summary>The name that was asked for is exactly this product's name (or one of its aliases).</summary>
            public bool exactName;
            /// <summary>exactName is true although numbers or claims of the name ("SPF 16", "30H") were not read: they are small print, often missed.</summary>
            public bool leftOutWeak;
            /// <summary>How much of what was read this product explains (the weight of the words they share): the tie-breaker between equal scores.</summary>
            public float evidence;
        }

        /// <summary>From this score on a candidate is taken as "this is the product" without asking the user.</summary>
        public const float Sure = 0.86f;
        /// <summary>From this score on a candidate is worth showing in a shortlist.</summary>
        public const float Possible = 0.45f;

        readonly List<ProductData> all = new List<ProductData>();
        readonly Dictionary<string, ProductData> byId = new Dictionary<string, ProductData>();
        readonly Dictionary<string, ProductData> byBarcode = new Dictionary<string, ProductData>();
        readonly Dictionary<string, List<ProductData>> byName = new Dictionary<string, List<ProductData>>();     // name key or alias key
        readonly Dictionary<string, List<ProductData>> byCompact = new Dictionary<string, List<ProductData>>();  // the same keys without spaces: "super stay" = "superstay"
        readonly Dictionary<string, List<ProductData>> byWord = new Dictionary<string, List<ProductData>>();
        readonly Dictionary<string, Words> wordsOf = new Dictionary<string, Words>();                            // product id -> the words of its name
        readonly Dictionary<string, Words> brandOf = new Dictionary<string, Words>();                            // product id -> the words of its brand
        readonly Dictionary<string, List<string>> wordsBySquash = new Dictionary<string, List<string>>();        // a word with the camera's mistakes evened out -> the words it can be
        readonly Dictionary<string, float> weight = new Dictionary<string, float>();                             // word -> how telling it is

        /// <summary>The words of a name, each also in its "squashed" form (see TextKey.Squash): worked out once, compared many times.</summary>
        class Words
        {
            public readonly string[] plain, squashed;
            public readonly string squashedTogether;
            public Words(string key)
            {
                plain = string.IsNullOrEmpty(key) ? new string[0] : key.Split(' ');
                squashed = new string[plain.Length];
                for (int i = 0; i < plain.Length; i++) squashed[i] = TextKey.Squash(plain[i]);
                squashedTogether = TextKey.Squash(key ?? "");
            }
            public int Length => plain.Length;
        }

        public int Count => all.Count;
        public IReadOnlyList<ProductData> Products => all;

        /// <param name="extraAliases">names this phone has learned for a product (alias key -> product id); may be null</param>
        public static ProductIndex Build(IEnumerable<ProductData> products, IDictionary<string, string> extraAliases = null)
        {
            var index = new ProductIndex();
            if (products == null) return index;
            foreach (ProductData p in products)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.name) || string.IsNullOrEmpty(p.id) || index.byId.ContainsKey(p.id)) continue;
                index.all.Add(p);
                index.byId[p.id] = p;

                string code = BarcodeKey(p.barcode);
                if (code.Length > 0 && !index.byBarcode.ContainsKey(code)) index.byBarcode[code] = p;

                string nameKey = TextKey.Normalize(p.name);
                index.AddName(nameKey, p);
                if (p.aliases != null) foreach (string alias in p.aliases) index.AddName(TextKey.Normalize(alias), p);

                var words = new Words(nameKey);
                index.wordsOf[p.id] = words;
                index.brandOf[p.id] = new Words(TextKey.Normalize(p.brandName));
                var once = new HashSet<string>();
                for (int i = 0; i < words.Length; i++)
                {
                    string word = words.plain[i];
                    if (word.Length < 2 || !once.Add(word)) continue;
                    if (!index.byWord.TryGetValue(word, out List<ProductData> list))
                    {
                        index.byWord[word] = list = new List<ProductData>();
                        if (word.Length >= 3)
                        {
                            if (!index.wordsBySquash.TryGetValue(words.squashed[i], out List<string> alike)) index.wordsBySquash[words.squashed[i]] = alike = new List<string>();
                            alike.Add(word);
                        }
                    }
                    list.Add(p);
                }
            }
            if (extraAliases != null)
                foreach (KeyValuePair<string, string> alias in extraAliases)
                    if (index.byId.TryGetValue(alias.Value ?? "", out ProductData p)) index.AddName(TextKey.Normalize(alias.Key), p);

            // a word that stands in many names ("matte", "lipstick") tells little; a rare one ("superstay") tells a lot
            foreach (KeyValuePair<string, List<ProductData>> entry in index.byWord)
                index.weight[entry.Key] = (float)Math.Log(1.0 + (double)Math.Max(1, index.all.Count) / entry.Value.Count) + 0.2f;
            return index;
        }

        void AddName(string key, ProductData p)
        {
            if (key.Length == 0) return;
            if (!byName.TryGetValue(key, out List<ProductData> list)) byName[key] = list = new List<ProductData>();
            if (!list.Contains(p)) list.Add(p);
            string compact = key.Replace(" ", "");
            if (compact.Length < 4) return;
            if (!byCompact.TryGetValue(compact, out List<ProductData> same)) byCompact[compact] = same = new List<ProductData>();
            if (!same.Contains(p)) same.Add(p);
        }

        /// <summary>The products named like this key, also when the pack spaces the name differently ("Super Stay" for "Superstay").</summary>
        bool NamedLike(string key, out List<ProductData> list)
        {
            if (byName.TryGetValue(key, out list)) return true;
            string compact = key.Replace(" ", "");
            return compact.Length >= 4 && byCompact.TryGetValue(compact, out list);
        }

        // ---------------------------------------------------------------------------------------
        //  Exact look-ups
        // ---------------------------------------------------------------------------------------
        public ProductData ById(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out ProductData p) ? p : null;

        /// <summary>The product with this barcode, or null.</summary>
        public ProductData ByBarcode(string barcode)
        {
            string code = BarcodeKey(barcode);
            return code.Length > 0 && byBarcode.TryGetValue(code, out ProductData p) ? p : null;
        }

        /// <summary>
        /// The product with exactly this name (or alias), whatever the capitals, accents and punctuation. When a brand is
        /// given, a product of another brand is not it. Of several, the one with an ingredient list comes first.
        /// </summary>
        public ProductData Exact(string brandName, string productName, string kindHint = "")
        {
            string key = TextKey.Normalize(productName);
            if (key.Length == 0 || !NamedLike(key, out List<ProductData> list)) return null;
            // without a brand, a name only identifies a product when it is more than one word and only one product has it
            // ("Lip Tint" could be anybody's)
            if (TextKey.Normalize(brandName).Length == 0 && (list.Count != 1 || key.IndexOf(' ') < 0)) return null;
            ProductData best = null;
            bool bestFits = false;
            foreach (ProductData p in list)
            {
                if (!BrandsAgree(brandName, p.brandName)) continue;
                // of products that share the name, the one of the kind read on the pack comes first, then the one with an ingredient list
                bool fits = !string.IsNullOrWhiteSpace(kindHint) && SameKind(p.cosmeticType, kindHint);
                if (best == null || (fits && !bestFits) || (fits == bestFits && HasList(p) && !HasList(best))) { best = p; bestFits = fits; }
            }
            return best;
        }

        /// <summary>
        /// The products of this brand that have exactly this name and DIFFERENT kinds of make-up, when the kind read on the pack does not
        /// pick one of them: the user has to say which one it is. Empty when there is no such clash.
        /// </summary>
        public List<ProductData> SameNameDifferentKind(string brandName, string productName, string kindHint = "")
        {
            var same = new List<ProductData>();
            string key = TextKey.Normalize(productName);
            if (key.Length == 0 || !NamedLike(key, out List<ProductData> list)) return same;
            var kinds = new HashSet<string>();
            foreach (ProductData p in list)
            {
                if (!BrandsAgree(brandName, p.brandName)) continue;
                same.Add(p);
                string kind = TextKey.Normalize(p.cosmeticType);
                if (kind.Length > 0) kinds.Add(kind);
            }
            if (kinds.Count < 2) return new List<ProductData>();
            if (!string.IsNullOrWhiteSpace(kindHint) && same.FindAll(p => SameKind(p.cosmeticType, kindHint)).Count == 1) return new List<ProductData>();
            return same;
        }

        // ---------------------------------------------------------------------------------------
        //  Closest products
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// The products whose name is closest to a name that was recognised (by the AI, typed by the user ...), best
        /// first. A product of another brand than the one given is left out. Scores: 1 = the same name.
        /// `kindHint` = the kind of make-up read on the pack ("Foundation"): it picks between products that have the same name and
        /// a different kind (see ResolveKindClash).
        /// </summary>
        public List<Candidate> Search(string brandName, string productName, int maximum = 3, string kindHint = "")
        {
            var found = new List<Candidate>();
            string key = TextKey.Normalize(productName);
            if (key.Length == 0) return found;
            var asked = new Words(key);

            var scores = new Dictionary<string, float>();
            var evidence = new Dictionary<string, float>();
            foreach (ProductData p in Holding(asked))
            {
                if (!BrandsAgree(brandName, p.brandName)) continue;
                Words its = wordsOf[p.id];
                Words askedHere = JoinedLike(asked, its), itsHere = JoinedLike(its, asked);
                float shared = 0f, askedTotal = 0f, itsTotal = 0f;
                for (int i = 0; i < askedHere.Length; i++) { float w = Weight(askedHere.plain[i]); askedTotal += w; if (Has(itsHere, askedHere.plain[i], askedHere.squashed[i])) shared += w; }
                foreach (string word in itsHere.plain) itsTotal += Weight(word);
                if (shared <= 0f || askedTotal <= 0f || itsTotal <= 0f) continue;
                float ofAsked = shared / askedTotal, ofIts = Math.Min(1f, shared / itsTotal);
                // one name may be the short form of the other ("Superstay Matte Ink" / "Superstay Matte Ink Liquid Lipstick"):
                // the words of the shorter name count most
                float score = 0.7f * Math.Max(ofAsked, ofIts) + 0.3f * Math.Min(ofAsked, ofIts);
                if (!scores.TryGetValue(p.id, out float had) || score > had) { scores[p.id] = score; evidence[p.id] = shared; }
            }
            if (NamedLike(key, out List<ProductData> exact))
                foreach (ProductData p in exact) if (BrandsAgree(brandName, p.brandName)) scores[p.id] = 1f;

            foreach (KeyValuePair<string, float> entry in scores)
            {
                ProductData p = byId[entry.Key];
                found.Add(new Candidate
                {
                    product = p, score = entry.Value, exactName = entry.Value >= 0.999f, evidence = evidence.TryGetValue(entry.Key, out float e) ? e : 0f,
                    brandAgrees = !string.IsNullOrWhiteSpace(brandName) && !string.IsNullOrWhiteSpace(p.brandName) && BrandsAgree(brandName, p.brandName)
                });
            }
            return Best(found, maximum, kindHint);
        }

        /// <summary>
        /// The products whose name can be read in a text the camera found on a pack (brand, product name, claims, in any
        /// order, with reading mistakes). Best first. Score 1 = every word of the product's name is in the text.
        /// `kindHint` = the kind of make-up read on the pack, see Search.
        /// </summary>
        public List<Candidate> InText(string labelText, int maximum = 3, string kindHint = "")
        {
            var found = new List<Candidate>();
            string key = TextKey.Normalize(labelText);
            if (key.Length == 0) return found;
            var seen = new Words(key);

            foreach (ProductData p in Holding(seen))
            {
                Words ownWords = wordsOf[p.id];
                if (ownWords.Length == 0) continue;
                // the pack may space the name differently: "SUPER STAY" on the pack, "Superstay" in the list (or the other way round)
                Words seenHere = JoinedLike(seen, ownWords), its = JoinedLike(ownWords, seen);
                float shared = 0f, itsTotal = 0f; int sharedWords = 0, strongWords = 0, strongShared = 0;
                for (int i = 0; i < its.Length; i++)
                {
                    float w = Weight(its.plain[i]); itsTotal += w;
                    bool strong = !Weak(its.plain[i]);
                    if (strong) strongWords++;
                    if (Has(seenHere, its.plain[i], its.squashed[i])) { shared += w; sharedWords++; if (strong) strongShared++; }
                }
                if (itsTotal <= 0f || sharedWords == 0) continue;
                // the brand printed on the pack: its words are in the text, or it stands there written as one word
                Words brand = brandOf[p.id];
                bool brandSeen = brand.Length > 0 && brand.squashedTogether.Length >= 2
                              && (AllIn(brand, seen) || seen.squashedTogether.Contains(brand.squashedTogether));
                float score = shared / itsTotal;
                if (sharedWords < 2 && !(brandSeen && its.Length == 1)) score *= 0.5f;       // one shared word proves little
                if (!brandSeen) score *= 0.85f;
                // every word of the name is on the pack, or every word that matters (two at least): "SPF 16" printed small is often not read
                bool wholeName = sharedWords == its.Length, allThatMatters = strongWords >= 2 && strongShared == strongWords;
                found.Add(new Candidate { product = p, score = score, brandAgrees = brandSeen, exactName = wholeName || allThatMatters, leftOutWeak = !wholeName && allThatMatters, evidence = shared });
            }
            return Best(found, maximum, kindHint);
        }

        /// <summary>
        /// "Super Stay" and "Superstay" are one name written two ways. Where two neighbouring words of `words` run together into a word
        /// that `other` holds, they are joined, so both names compare word for word. `words` itself is left as it is.
        /// </summary>
        static Words JoinedLike(Words words, Words other)
        {
            if (words.Length < 2) return words;
            var result = new List<string>(words.Length);
            bool joined = false;
            for (int i = 0; i < words.Length; i++)
            {
                if (i + 1 < words.Length)
                {
                    string run = words.plain[i] + words.plain[i + 1];
                    if (run.Length >= 5 && Has(other, run, TextKey.Squash(run))) { result.Add(run); joined = true; i++; continue; }
                }
                result.Add(words.plain[i]);
            }
            return joined ? new Words(string.Join(" ", result)) : words;
        }

        /// <summary>Every product that holds at least one of these words (also with a small reading mistake).</summary>
        IEnumerable<ProductData> Holding(Words words)
        {
            var once = new HashSet<string>();
            for (int i = 0; i < words.Length; i++)
            {
                string word = words.plain[i];
                // two words that run together into a word of a name ("super" "stay" -> "superstay")
                if (i + 1 < words.Length && (word + words.plain[i + 1]).Length >= 5 && byWord.TryGetValue(word + words.plain[i + 1], out List<ProductData> glued))
                    foreach (ProductData p in glued) if (once.Add(p.id)) yield return p;
                if (word.Length < 2) continue;
                if (byWord.TryGetValue(word, out List<ProductData> list))
                    foreach (ProductData p in list) if (once.Add(p.id)) yield return p;
                // the same word with what a camera mixes up evened out ("1nk" for "ink")
                if (word.Length >= 3 && wordsBySquash.TryGetValue(words.squashed[i], out List<string> alike))
                    foreach (string other in alike)
                        if (other != word) foreach (ProductData p in byWord[other]) if (once.Add(p.id)) yield return p;
                // a word of the list that is this word with one letter read wrongly
                if (word.Length < 5) continue;
                foreach (KeyValuePair<string, List<ProductData>> entry in byWord)
                {
                    string other = entry.Key;
                    if (other.Length < 5 || other[0] != word[0] || other == word || !OneLetterApart(other, word)) continue;
                    foreach (ProductData p in entry.Value) if (once.Add(p.id)) yield return p;
                }
            }
        }

        /// <summary>How little a number or a claim counts: "SPF 16" or "30H" in a name is small print on the pack, so a pack read without it is not a different product.</summary>
        const float WeakWeight = 0.25f;

        float Weight(string word) => Weak(word) ? WeakWeight : weight.TryGetValue(word, out float w) ? w : 1.5f;

        /// <summary>A number ("16", "30h") or a claim word ("spf", "uva", "pa", "ml"): weak evidence of which product it is.</summary>
        static bool Weak(string word) =>
            word.Length > 0 && (char.IsDigit(word[0]) || word == "spf" || word == "uva" || word == "uvb" || word == "pa" || word == "ml" || word == "oz");

        /// <summary>True when the name holds this word: letter for letter, with a camera's mix-up evened out, or (five letters and more) with one letter wrong.</summary>
        static bool Has(Words name, string word, string squashedWord)
        {
            for (int i = 0; i < name.Length; i++)
            {
                string its = name.plain[i];
                if (its == word) return true;
                if (its.Length >= 3 && word.Length >= 3 && name.squashed[i] == squashedWord) return true;
                if (its.Length >= 5 && word.Length >= 5 && its[0] == word[0] && OneLetterApart(its, word)) return true;
            }
            return false;
        }

        static bool AllIn(Words words, Words text)
        {
            for (int i = 0; i < words.Length; i++) if (!Has(text, words.plain[i], words.squashed[i])) return false;
            return words.Length > 0;
        }

        /// <summary>At most one letter changed, added or left out.</summary>
        static bool OneLetterApart(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++; else if (b.Length > a.Length) j++; else { i++; j++; }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }

        /// <summary>How far the best candidate must be ahead of the next one to be taken as "this is the product".</summary>
        public const float ClearLead = 0.1f;

        static List<Candidate> Best(List<Candidate> found, int maximum, string kindHint = "")
        {
            found.Sort((x, y) =>
            {
                if (Math.Abs(y.score - x.score) > 0.001f) return y.score.CompareTo(x.score);
                if (Math.Abs(y.evidence - x.evidence) > 0.001f) return y.evidence.CompareTo(x.evidence);     // the one that explains more of what was read
                int byList = HasList(y.product).CompareTo(HasList(x.product));          // one with an ingredient list first
                return byList != 0 ? byList : string.CompareOrdinal(x.product.id, y.product.id);
            });
            ResolveKindClash(found, kindHint);
            // A name that fits two different products about equally well ("Fit Me": the foundation or the concealer?) does not
            // say which one is meant: neither is "sure" then, and the user has to choose. (An exact name stays sure.)
            if (found.Count >= 2 && (!found[0].exactName || found[0].leftOutWeak) && found[0].score >= Sure && found[0].score - found[1].score < ClearLead
                && TextKey.Normalize(found[0].product.name) != TextKey.Normalize(found[1].product.name))
            {
                float cap = Sure - 0.01f;
                foreach (Candidate candidate in found) if (candidate.score > cap) candidate.score = cap;
            }
            if (found.Count > maximum) found.RemoveRange(maximum, found.Count - maximum);
            return found;
        }

        /// <summary>
        /// Products of one brand with the SAME name and a different kind of make-up ("Fit Me" as a foundation and as a concealer): the name
        /// does not say which one is meant. When the kind read on the pack ("Foundation") fits exactly one of them, that one is taken and the
        /// others drop out. Otherwise none of them is taken for granted: their scores are held below Sure and they are no exact name, so the
        /// user is asked which one it is. `found` is sorted, best first.
        /// </summary>
        static void ResolveKindClash(List<Candidate> found, string kindHint)
        {
            if (found.Count < 2) return;
            Candidate top = found[0];
            string name = TextKey.Normalize(top.product.name);
            var group = new List<Candidate>();
            foreach (Candidate c in found)
                if (top.score - c.score < ClearLead && TextKey.Normalize(c.product.name) == name && TextKey.Normalize(c.product.brandName) == TextKey.Normalize(top.product.brandName)) group.Add(c);
            if (group.Count < 2) return;
            var kinds = new HashSet<string>();
            foreach (Candidate c in group) { string kind = TextKey.Normalize(c.product.cosmeticType); if (kind.Length > 0) kinds.Add(kind); }
            if (kinds.Count < 2) return;                    // one kind (or none given): the same product listed twice, not a clash

            if (!string.IsNullOrWhiteSpace(kindHint))
            {
                var fitting = group.FindAll(c => SameKind(c.product.cosmeticType, kindHint));
                if (fitting.Count == 1) { foreach (Candidate c in group) if (c != fitting[0]) found.Remove(c); return; }
            }
            foreach (Candidate c in group) { c.exactName = false; c.score = Math.Min(c.score, Sure - 0.01f); }
        }

        /// <summary>"Foundation" fits "Liquid Foundation" and the other way round.</summary>
        static bool SameKind(string a, string b)
        {
            string x = TextKey.Normalize(a), y = TextKey.Normalize(b);
            if (x.Length == 0 || y.Length == 0) return false;
            return x == y || (" " + x + " ").Contains(" " + y + " ") || (" " + y + " ").Contains(" " + x + " ");
        }

        static bool HasList(ProductData p) => p != null && p.ingredients != null && p.ingredients.Count >= 3;

        // ---------------------------------------------------------------------------------------
        //  Keys
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// The comparable form of a barcode: its digits without the zeros in front (the same product carries "012345678905"
        /// on one pack and "0012345678905" on another). "" when that is not 6 to 14 digits: then it is not a product barcode.
        /// </summary>
        public static string BarcodeKey(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return "";
            string trimmed = barcode.Trim();
            // When scanning a QR code containing a URL or query string, look for a standard 6-14 digit barcode/product sequence
            if (trimmed.Contains("/") || trimmed.Contains("?") || trimmed.Contains("=") || trimmed.Contains(":"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\b\d{6,14}\b");
                if (match.Success)
                {
                    string k = match.Value.TrimStart('0');
                    if (k.Length >= 6 && k.Length <= 14) return k;
                }
            }
            var digits = new System.Text.StringBuilder();
            foreach (char c in trimmed) if (c >= '0' && c <= '9') digits.Append(c);
            string key = digits.ToString().TrimStart('0');
            return key.Length >= 6 && key.Length <= 14 ? key : "";
        }

        /// <summary>
        /// True when two brand names mean the same brand: the same once capitals, accents and spaces are taken out, or one is
        /// the other with words added ("Maybelline" / "Maybelline New York"). An empty name agrees with any brand.
        /// </summary>
        public static bool BrandsAgree(string a, string b)
        {
            string ka = TextKey.Normalize(a), kb = TextKey.Normalize(b);
            if (ka.Length == 0 || kb.Length == 0) return true;
            if (ka == kb || ka.Replace(" ", "") == kb.Replace(" ", "")) return true;
            string longer = " " + (ka.Length >= kb.Length ? ka : kb) + " ", shorter = " " + (ka.Length >= kb.Length ? kb : ka) + " ";
            return shorter.Trim().Length >= 3 && longer.Contains(shorter);
        }

        /// <summary>
        /// The id a product gets when the app adds it from a scan: always the same for the same brand and name, so the
        /// same product can never be added twice ("scan_maybelline__superstay_matte_ink").
        /// </summary>
        public static string ScanId(string brandName, string productName)
        {
            string brand = TextKey.Slug(brandName), name = TextKey.Slug(productName);
            if (name.Length == 0) return "";
            string id = "scan_" + (brand.Length > 0 ? brand : "nobrand") + "__" + name;
            return id.Length > 110 ? id.Substring(0, 110) : id;
        }

        /// <summary>The id a brand gets when the app adds it from a scan ("scan_maybelline").</summary>
        public static string ScanBrandId(string brandName)
        {
            string brand = TextKey.Slug(brandName);
            if (brand.Length == 0) return "";
            string id = "scan_" + brand;
            return id.Length > 90 ? id.Substring(0, 90) : id;
        }
    }
}
