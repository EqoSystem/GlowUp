using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Scan
{
    /// <summary>
    /// Looks a product up in the Philippine FDA's public Verification Portal (https://verification.fda.gov.ph),
    /// the same search a person can type into on that site:
    ///     https://verification.fda.gov.ph/api/search?q=maybelline+fit+me
    /// The answer lists the cosmetic products NOTIFIED with the FDA (local and imported alike): notification
    /// number, product name, brand, variants, intended use, the company that notified it and until when the
    /// notification is valid.
    ///
    /// What it tells: the product is on the FDA's list (a credible source for its name, brand and kind).
    /// What it does NOT tell: the ingredient list, or anything about safety for one person's skin. The
    /// ingredient check stays the app's own.
    /// Needs the internet. Any failure simply means "not checked".
    /// </summary>
    public static class PhFdaVerification
    {
        public static bool Enabled = true;
        public static string Address = "https://verification.fda.gov.ph/api/search";
        /// <summary>The page a person can open to check the same thing by hand.</summary>
        public const string Portal = "https://verification.fda.gov.ph";
        /// <summary>One search of the FDA's site takes 12 to 15 seconds (measured October 2026). A shorter limit means no answer, ever.</summary>
        public static float TimeoutSeconds = 25f;

        public class Result
        {
            public bool found;
            public string number = "", productName = "", brandName = "", variants = "", intendedUse = "", company = "", decisionDate = "", validUntil = "";
            public bool valid = true;
            /// <summary>
            /// True when the FDA's record is clearly THIS product: every word that was asked for is in its name, and most of
            /// its own words were asked for. False = only a record with a similar name (the user is shown it and judges).
            /// </summary>
            public bool closeMatch;
            /// <summary>True when the FDA's site answered at least one search. False = no connection, too slow, or it refused: then nothing is known.</summary>
            public bool answered;
            /// <summary>The answer in one word: "registered", "expired", "similar", "none"; "" when the site did not answer.</summary>
            public string Status => found ? (!closeMatch ? Similar : valid ? Registered : Expired) : answered ? None : "";
            /// <summary>One line for the result screen and for the website's inventory.</summary>
            public string Line => !found ? "" :
                $"FDA Philippines: notified as \"{productName}\" ({number}), by {company}. " + (validUntil.Length > 0 ? (valid ? "Valid until " : "Notification ended ") + validUntil + "." : "");
        }

        /// <summary>
        /// The FDA's entry for this product, or a result with found = false.
        /// One search takes the FDA's site 12 to 15 seconds, so as few as possible are made:
        ///   1. everything the BRAND has registered, in one search; the closest name is then picked here on the phone;
        ///   2. only when the brand is not known, or the FDA lists nothing under it: the first two words of the product's
        ///      name, then the whole name.
        /// An answer is kept (for this run of the app, and on the phone for some days), so the same product is not searched
        /// twice. A search that fails (no connection, too slow, refused) ends the attempt: nothing is known then.
        /// `shadeName` (optional): the FDA lists each shade of a product as a record of its own; of several records that
        /// fit equally well, the one that names this shade is taken.
        /// </summary>
        public static async Task<Result> Find(string productName, string brandName, string shadeName = "")
        {
            var none = new Result();
            string name = Clean(productName), brand = Clean(brandName);
            if (!Enabled || name.Length < 3) return none;
            // the product name usually carries the brand already; when it does not, the brand is put in front
            string query = brand.Length > 0 && name.IndexOf(brand, StringComparison.OrdinalIgnoreCase) < 0 ? brand + " " + name : name;
            string shade = Clean(shadeName);
            string key = (shade.Length > 0 ? query + " | " + shade : query).ToLowerInvariant();
            if (remembered.TryGetValue(key, out Result earlier)) return earlier;
            if (TryRecall(key, out Result kept)) { remembered[key] = kept; return kept; }

            // the name without the brand in front of it
            string bare = brand.Length > 0 && name.StartsWith(brand + " ", StringComparison.OrdinalIgnoreCase) ? name.Substring(brand.Length + 1).Trim() : name;
            string[] words = bare.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tries = new List<string>();
            // a very short brand ("MAC") would also find every name that merely holds those letters: its first product word goes with it
            string brandSearch = brand.Length >= 4 ? brand : brand.Length >= 2 && words.Length >= 1 ? brand + " " + words[0] : "";
            if (brandSearch.Length > 0) tries.Add(brandSearch);
            if (words.Length >= 2) tries.Add(words[0] + " " + words[1]);
            tries.Add(bare);
            var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string phrase in tries)
            {
                if (phrase.Length < 3 || !asked.Add(phrase)) continue;
                List<object> entries = await Search(phrase);
                if (entries == null) break;                                   // the site did not answer: asking again at once would only take longer
                none.answered = true;
                Result best = Best(entries, query, brand, shade);
                if (best.found) { best.answered = true; Keep(key, best); return best; }
                // the FDA lists this brand, and the product is not among its records: that is the answer
                if (phrase == brandSearch && entries.Count > 0) break;
            }
            if (none.answered) Keep(key, none);       // "no record" is an answer too; a search that failed is tried again next time
            return none;
        }

        /// <summary>
        /// For a product the app could not name: the lines read from the label are tried as searches (the longest first,
        /// three at most). Only an entry whose brand AND most of whose name stand on the label is accepted.
        /// </summary>
        public static async Task<Result> FindInText(string labelText)
        {
            var none = new Result();
            if (!Enabled || string.IsNullOrWhiteSpace(labelText)) return none;
            var lines = new List<string>();
            foreach (string raw in labelText.Split('\n'))
            {
                string line = Clean(raw);
                if (line.Length < 6 || line.Length > 60 || line.Split(' ').Length < 2 || line.IndexOf(',') >= 0) continue;      // an ingredient list has commas
                lines.Add(line);
            }
            lines.Sort((a, b) => b.Length.CompareTo(a.Length));
            string everything = " " + Clean(labelText).ToLowerInvariant() + " ";
            for (int i = 0; i < lines.Count && i < 3; i++)
            {
                List<object> entries = await Search(lines[i]);
                if (entries == null) break;                                   // the site did not answer: more searches would only take longer
                none.answered = true;
                Result best = Best(entries, lines[i], "", "");
                if (!best.found) continue;
                // the brand must really be on the label, not just one shared word
                if (best.brandName.Length >= 2 && everything.Contains(" " + best.brandName.ToLowerInvariant() + " ")) { best.answered = true; return best; }
            }
            return none;
        }

        // =====================================================================================
        //  Answers kept on the phone: a product that was checked is not searched again, and its badge also shows offline
        // =====================================================================================
        [Serializable]
        class KeptAnswer
        {
            public string key = "", number = "", productName = "", brandName = "", variants = "", intendedUse = "", company = "", decisionDate = "", validUntil = "";
            public bool found, closeMatch;
            public long savedAtUnix;
        }
        [Serializable] class KeptAnswers { public List<KeptAnswer> items = new List<KeptAnswer>(); }

        const string KeptFile = "fda_answers";
        const int KeepMost = 150;
        /// <summary>Days a found record is trusted before the FDA's site is asked again, and days for "no record".</summary>
        public static int KeepFoundDays = 30, KeepNoneDays = 3;
        static KeptAnswers keptAnswers;

        static KeptAnswers Kept()
        {
            if (keptAnswers == null && !OfflineCache.TryLoad(KeptFile, out keptAnswers)) keptAnswers = new KeptAnswers();
            return keptAnswers ?? (keptAnswers = new KeptAnswers());
        }

        static bool Past(string validUntil) =>
            DateTime.TryParse(validUntil, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime until) && until.Date < DateTime.UtcNow.Date;

        static bool TryRecall(string key, out Result result)
        {
            result = null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (KeptAnswer k in Kept().items)
            {
                if (k == null || k.key != key) continue;
                long age = now - k.savedAtUnix;
                if (age < 0 || age > (k.found ? KeepFoundDays : KeepNoneDays) * 86400L) return false;
                result = new Result
                {
                    found = k.found, answered = true, closeMatch = k.closeMatch, number = k.number ?? "", productName = k.productName ?? "", brandName = k.brandName ?? "",
                    variants = k.variants ?? "", intendedUse = k.intendedUse ?? "", company = k.company ?? "", decisionDate = k.decisionDate ?? "", validUntil = k.validUntil ?? ""
                };
                result.valid = !Past(result.validUntil);          // a record can end between two scans
                return true;
            }
            return false;
        }

        static void Keep(string key, Result r)
        {
            remembered[key] = r;
            try
            {
                KeptAnswers all = Kept();
                all.items.RemoveAll(k => k == null || k.key == key);
                all.items.Add(new KeptAnswer
                {
                    key = key, found = r.found, closeMatch = r.closeMatch, number = r.number, productName = r.productName, brandName = r.brandName, variants = r.variants,
                    intendedUse = r.intendedUse, company = r.company, decisionDate = r.decisionDate, validUntil = r.validUntil, savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                while (all.items.Count > KeepMost) all.items.RemoveAt(0);
                OfflineCache.Save(KeptFile, all);
            }
            catch (Exception e) { Debug.LogWarning("FDA Philippines: the answer was not kept on the phone: " + e.Message); }
        }

        // =====================================================================================
        //  A check that is still running when the result opens: the result screen shows "checking" and fills the badge in when it ends
        // =====================================================================================
        static readonly HashSet<string> checking = new HashSet<string>();
        /// <summary>Raised when a check that ran on after the result opened has ended. The text is the scan's id.</summary>
        public static event Action<string> CheckEnded;
        public static bool IsChecking(string scanId) => !string.IsNullOrEmpty(scanId) && checking.Contains(scanId);
        public static void SetChecking(string scanId, bool on)
        {
            if (string.IsNullOrEmpty(scanId)) return;
            if (on) { checking.Add(scanId); return; }
            if (checking.Remove(scanId)) CheckEnded?.Invoke(scanId);
        }

        /// <summary>True when at least 6 in 10 of the words of the FDA's product name are among the words that were asked for.</summary>
        static bool MostWordsAsked(string entryName, string[] asked)
        {
            string askedText = " " + string.Join(" ", asked) + " ";
            int own = 0, found = 0;
            foreach (string word in Clean(entryName).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 2) continue;
                own++;
                if (askedText.Contains(" " + word + " ")) found++;
            }
            return own > 0 && found >= own * 0.6f;
        }

        static readonly Dictionary<string, Result> remembered = new Dictionary<string, Result>();

        // =====================================================================================
        //  The answer as a scan keeps it (ScanRecord.fdaStatus and its neighbours)
        // =====================================================================================
        public const string Registered = "registered", Similar = "similar", Expired = "expired", None = "none", Unavailable = "unavailable";

        /// <summary>
        /// The FDA record a product of the inventory carries: the website keeps the notification number on the product and
        /// writes the FDA's line into its description. Null when the product has no number on file.
        /// </summary>
        public static Result FromInventory(string fdaNumber, string description)
        {
            if (string.IsNullOrWhiteSpace(fdaNumber)) return null;
            var r = new Result { found = true, answered = true, closeMatch = true, number = fdaNumber.Trim() };
            Match m = Regex.Match(description ?? "", "FDA Philippines: notified as \"(.+?)\" \\((.+?)\\), by (.+?)\\.+ *(?:(Valid until|Notification ended) ([^.]+)\\.|$)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                r.productName = m.Groups[1].Value.Trim(); r.company = m.Groups[3].Value.Trim().TrimEnd('.');
                r.validUntil = m.Groups[5].Value.Trim();
            }
            bool ended = m.Success && m.Groups[4].Value.StartsWith("Notification", StringComparison.OrdinalIgnoreCase);
            bool past = DateTime.TryParse(r.validUntil, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime until) && until.Date < DateTime.UtcNow.Date;
            r.valid = !ended && !past;
            return r;
        }

        /// <summary>
        /// What the FDA Philippines check said about a scan: Registered, Similar, Expired, None or Unavailable. A scan saved
        /// before the answer was kept as a word of its own is worked out from what it does hold.
        /// </summary>
        public static string StatusOf(ScanRecord record)
        {
            if (record == null) return Unavailable;
            string kept = (record.fdaStatus ?? "").Trim().ToLowerInvariant();
            if (kept == Registered || kept == Similar || kept == Expired || kept == None) return kept;
            if (!string.IsNullOrWhiteSpace(record.fdaNumber))
                return (record.fdaLine ?? "").IndexOf("Notification ended", StringComparison.OrdinalIgnoreCase) >= 0 ? Expired : Registered;
            return record.fdaChecked ? None : Unavailable;
        }

        /// <summary>
        /// The user looked at a SIMILAR record and said whether it is their product. Yes: the scan counts as registered
        /// (or expired, by the record's date). No: that record is taken off the scan, which then says "no record found".
        /// Returns false when the scan had no similar record to answer about.
        /// </summary>
        public static bool ConfirmSimilar(ScanRecord record, bool isMyProduct)
        {
            if (record == null || StatusOf(record) != Similar) return false;
            if (isMyProduct) record.fdaStatus = Past(ValidUntilOf(record)) ? Expired : Registered;
            else
            {
                record.fdaStatus = None; record.fdaChecked = true;
                record.fdaNumber = ""; record.fdaLine = ""; record.fdaProductName = ""; record.fdaCompany = ""; record.fdaValidUntil = "";
            }
            return true;
        }

        /// <summary>Until when the FDA's record is valid, as it was written down ("" when not known).</summary>
        public static string ValidUntilOf(ScanRecord record)
        {
            if (record == null) return "";
            if (!string.IsNullOrWhiteSpace(record.fdaValidUntil)) return record.fdaValidUntil.Trim();
            Match m = Regex.Match(record.fdaLine ?? "", @"(?:Valid until|Notification ended)\s+([^.]+)\.", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>The product's name on the FDA's record ("" when not known).</summary>
        public static string RecordNameOf(ScanRecord record)
        {
            if (record == null) return "";
            if (!string.IsNullOrWhiteSpace(record.fdaProductName)) return record.fdaProductName.Trim();
            Match m = Regex.Match(record.fdaLine ?? "", "notified as \"(.+?)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>The company on the FDA's record ("" when not known).</summary>
        public static string CompanyOf(ScanRecord record)
        {
            if (record == null) return "";
            if (!string.IsNullOrWhiteSpace(record.fdaCompany)) return record.fdaCompany.Trim();
            Match m = Regex.Match(record.fdaLine ?? "", @"\), by (.+?)\.+ *(?:Valid until|Notification ended|$)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>"2028-04-16 00:00:00" or "April 16, 2028" gives "16 Apr 2028". A text that is not a date is given back as it is.</summary>
        public static string NiceDate(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return "";
            return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime date)
                ? date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : raw;
        }

        static string Clean(string text)
        {
            var sb = new System.Text.StringBuilder();
            bool space = true;
            foreach (char c in text ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '&' || c == '-') { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// How the app names itself to the FDA's site ("" = "GlowUp/[version] (Android app; student capstone project)").
        /// Keep it truthful: it is there so the site's keepers can see who is asking.
        /// </summary>
        public static string RequestName = "";
        static string AppName() => !string.IsNullOrWhiteSpace(RequestName) ? RequestName.Trim() : "GlowUp/" + Application.version + " (Android app; student capstone project)";

        // what the site answered in this run of the app, by search words: several products of one brand need one search only
        static readonly Dictionary<string, List<object>> searched = new Dictionary<string, List<object>>();

        /// <summary>The cosmetics the FDA's site lists for these words. An empty list = it answered "none"; null = it did not answer.</summary>
        static async Task<List<object>> Search(string query)
        {
            string words = (query ?? "").Trim().ToLowerInvariant();
            if (searched.TryGetValue(words, out List<object> before)) return before;
            List<object> entries = await SearchSite(query);
            if (entries != null) { if (searched.Count >= 12) searched.Clear(); searched[words] = entries; }
            return entries;
        }

        static async Task<List<object>> SearchSite(string query)
        {
            try
            {
                using (var request = UnityWebRequest.Get(Address + "?q=" + UnityWebRequest.EscapeURL(query)))
                {
                    request.timeout = Mathf.Max(3, Mathf.RoundToInt(TimeoutSeconds));
                    request.SetRequestHeader("Accept", "application/json");
                    // The FDA's site refuses requests that do not say who is asking (it answers "403 Forbidden" to the name Unity
                    // sends by itself, and to no name at all). The app says honestly who it is; it does not pretend to be a browser.
                    request.SetRequestHeader("User-Agent", AppName());
                    await request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    {
                        Debug.LogWarning("FDA Philippines check: no answer (HTTP " + request.responseCode + (string.IsNullOrEmpty(request.error) ? "" : ", " + request.error) + ").");
                        return null;
                    }
                    var root = MiniJson.Deserialize(request.downloadHandler.text) as Dictionary<string, object>;
                    if (root == null) return null;
                    // the site answered: an answer with no cosmetics in it is an EMPTY list (null stays for "no answer at all")
                    if (!root.TryGetValue("cosmetic_NN", out object list) || list == null) return new List<object>();
                    if (list is Dictionary<string, object> keyed) return new List<object>(keyed.Values);      // the portal sometimes answers with a numbered list
                    return list as List<object> ?? new List<object>();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("FDA Philippines check skipped: " + e.Message);
                return null;
            }
        }

        /// <summary>The entry that shares the most words with what was asked for (at least two, and at least half of the asked words).</summary>
        static Result Best(List<object> entries, string query, string brand, string shade)
        {
            string shadeWords = string.IsNullOrWhiteSpace(shade) ? "" : " " + shade.Trim().ToLowerInvariant() + " ";
            var best = new Result();
            if (entries == null) return best;
            string[] asked = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float bestScore = 0f;
            foreach (object entry in entries)
            {
                if (!(entry is Dictionary<string, object> row)) continue;
                string product = Field(row, "PRODUCT_NAME"), entryBrand = Field(row, "BRAND_NAME");
                string haystack = " " + Clean(product + " " + entryBrand).ToLowerInvariant() + " ";
                int shared = 0, counted = 0;
                foreach (string word in asked)
                {
                    if (word.Length < 2) continue;
                    counted++;
                    if (haystack.Contains(" " + word + " ")) shared++;
                }
                if (counted == 0 || shared < 2 || shared < counted * 0.5f) continue;
                if (brand.Length > 0 && Clean(entryBrand).IndexOf(brand, StringComparison.OrdinalIgnoreCase) < 0 && brand.IndexOf(Clean(entryBrand), StringComparison.OrdinalIgnoreCase) < 0) continue;
                // more shared words win; of two equal ones the shorter name is the closer match
                float score = shared * 10f - product.Length * 0.01f + (Field(row, "NOTIFICATION_DECISION") == "1" ? 0.5f : 0f)
                            + (shadeWords.Length > 2 && haystack.Contains(shadeWords) ? 5f : 0f);       // the record of the user's own shade breaks a tie
                if (score <= bestScore) continue;
                bestScore = score;
                best = new Result
                {
                    found = true, number = Field(row, "ACCOUNTCODE"), productName = product, brandName = entryBrand, variants = Field(row, "PROD_VARIANTS"),
                    intendedUse = Field(row, "PRODUCT_INTENDED_USE"), company = Field(row, "COMPANY_NAME"),
                    decisionDate = Field(row, "NOTIFICATION_DECISION_DATE"), validUntil = Field(row, "NOTIFICATION_VALIDITY")
                };
                best.valid = !(DateTime.TryParse(best.validUntil, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime until) && until.Date < DateTime.UtcNow.Date);
                // a CLOSE match: every word that was asked for is in the entry, and most of the entry's own words were asked for
                best.closeMatch = shared == counted && MostWordsAsked(product, asked);
            }
            return best;
        }

        static string Field(Dictionary<string, object> row, string key)
        {
            string text = row.TryGetValue(key, out object v) && v != null ? v.ToString() : "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text == "N/A" ? "" : text;
        }

        /// <summary>"MAKE UP - EYELINER", "MAKEUP SETTING POWDER" -> a cosmetic type the app knows ("" when none fits).</summary>
        public static string TypeFromUse(string intendedUse)
        {
            string use = (intendedUse ?? "").ToLowerInvariant();
            string[,] kinds =
            {
                { "lipstick", "Lipstick" }, { "lip gloss", "Lip Gloss" }, { "lip tint", "Lip Tint" }, { "lip balm", "Lip Balm" }, { "lip", "Lipstick" },
                { "foundation", "Foundation" }, { "concealer", "Concealer" }, { "primer", "Primer" }, { "blush", "Blush" }, { "bronzer", "Bronzer" },
                { "highlighter", "Highlighter" }, { "eyeshadow", "Eyeshadow" }, { "eye shadow", "Eyeshadow" }, { "eyeliner", "Eyeliner" }, { "mascara", "Mascara" },
                { "brow", "Brow" }, { "powder", "Powder" }, { "sunscreen", "Sunscreen" }, { "serum", "Serum" }, { "toner", "Toner" }, { "cleanser", "Cleanser" },
                { "moisturi", "Moisturizer" }, { "mask", "Mask" }
            };
            for (int i = 0; i < kinds.GetLength(0); i++) if (use.Contains(kinds[i, 0])) return kinds[i, 1];
            return "";
        }
    }
}
