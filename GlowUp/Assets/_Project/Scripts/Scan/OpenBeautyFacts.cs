using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Scan
{
    /// <summary>
    /// A SECOND OPINION on a product's ingredient list: Open Beauty Facts (https://world.openbeautyfacts.org), a public,
    /// volunteer-made list of cosmetics that is looked up by the product's BARCODE. No key is needed and nothing about
    /// the user is sent: only the barcode.
    ///
    /// It is the last of the places a list may come from (see ListSources): it is used to check a list the app already
    /// has ("Matched 2 sources", or what differs), and as the list itself only when nothing better was found. Anybody
    /// can edit it, so the day of its last edit is always shown with it.
    ///
    /// What was measured on 12 October 2026: one look-up takes 0.6 to 5 seconds; the answer says "status 1" when the
    /// product is there and 0 when it is not; many products are there WITHOUT an ingredient list; some lists give each
    /// name in several languages with "/" between them. Answers are kept on the phone (30 days when found, 3 days when
    /// not), so the same product is not asked twice.
    /// </summary>
    public static class OpenBeautyFacts
    {
        public static bool Enabled = true;
        /// <summary>{0} = the barcode's digits.</summary>
        public static string Address = "https://world.openbeautyfacts.org/api/v2/product/{0}.json?fields=code,product_name,brands,ingredients_text,ingredients_text_en,last_modified_t";
        public const string ProductPage = "https://world.openbeautyfacts.org/product/{0}";
        public const string Name = "Open Beauty Facts";
        public static float TimeoutSeconds = 10f;
        public static int KeepFoundDays = 30, KeepNoneDays = 3;

        public class Result
        {
            /// <summary>The site answered (whether it has the product or not). False = offline, too slow, or an error.</summary>
            public bool answered;
            /// <summary>The site has the product AND an ingredient list for it.</summary>
            public bool found;
            public string barcode = "", productName = "", brandName = "";
            /// <summary>The list as the site gives it (label text: it goes through the app's ingredient reader like a pack does).</summary>
            public string ingredientText = "";
            /// <summary>When the entry was last edited (Unix seconds; 0 = not known).</summary>
            public long lastEditedUnix;
            public string Page => string.Format(ProductPage, barcode);
            /// <summary>"Open Beauty Facts | https://..." : the form every source of a list is written in.</summary>
            public string Source => Name + " | " + Page;
        }

        /// <summary>The digits of a barcode ("" when they cannot be a product barcode: 8 to 14 digits).</summary>
        public static string Digits(string barcode)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in barcode ?? "") if (c >= '0' && c <= '9') sb.Append(c);
            return sb.Length >= 8 && sb.Length <= 14 ? sb.ToString() : "";
        }

        /// <summary>Looks a barcode up. Never throws: when the site cannot be reached the answer says so (`answered` false).</summary>
        public static async Task<Result> Find(string barcode)
        {
            string code = Digits(barcode);
            var none = new Result { barcode = code };
            if (!Enabled || code.Length == 0) return none;
            if (TryRecall(code, out Result kept)) return kept;
            try
            {
                using (var request = UnityWebRequest.Get(string.Format(Address, code)))
                {
                    request.timeout = Mathf.Max(3, Mathf.RoundToInt(TimeoutSeconds));             // EVERY LONG STEP HAS A TIME LIMIT
                    request.SetRequestHeader("Accept", "application/json");
                    // the site asks every app to say who it is; the app says so truthfully
                    request.SetRequestHeader("User-Agent", "GlowUp/" + Application.version + " (Android app; student capstone project)");
                    await request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    {
                        { }
                        return none;
                    }
                    Result result = Parse(request.downloadHandler.text, code);
                    if (result.answered) Keep(result);
                    return result;
                }
            }
            catch (Exception e)
            {
                { }
                return none;
            }
        }

        /// <summary>The site's answer, read. An answer that cannot be understood counts as "did not answer".</summary>
        public static Result Parse(string json, string barcode)
        {
            var result = new Result { barcode = Digits(barcode) };
            Dictionary<string, object> root = null;
            try { root = MiniJson.Deserialize(json ?? "") as Dictionary<string, object>; } catch (Exception) { }
            if (root == null || !root.TryGetValue("status", out object status)) return result;
            result.answered = true;
            if (Number(status) != 1 || !root.TryGetValue("product", out object productObject) || !(productObject is Dictionary<string, object> product)) return result;
            result.productName = Text(product, "product_name");
            result.brandName = Text(product, "brands").Split(',')[0].Trim();
            // the list in English when the site has it, otherwise the list as it was typed in
            string list = Text(product, "ingredients_text_en");
            if (list.Length == 0) list = Text(product, "ingredients_text");
            result.ingredientText = list;
            if (product.TryGetValue("last_modified_t", out object edited)) result.lastEditedUnix = Math.Max(0, Number(edited));
            // "found" needs a list that is one: a few words are not a list
            result.found = list.Length >= 12 && list.IndexOf(',') >= 0;
            return result;
        }

        static string Text(Dictionary<string, object> json, string key) => json.TryGetValue(key, out object v) && v is string s ? s.Trim() : "";

        /// <summary>A whole number out of whatever the answer holds it as (a number, or a number written as text). -1 when it is none.</summary>
        static long Number(object value)
        {
            if (value == null || value is bool) return -1;
            if (value is string text) return long.TryParse(text.Trim(), out long parsed) ? parsed : -1;
            try { return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { return -1; }
        }

        // =====================================================================================
        //  Answers kept on the phone
        // =====================================================================================
        [Serializable]
        class KeptAnswer
        {
            public string barcode = "", productName = "", brandName = "", ingredientText = "";
            public bool found;
            public long lastEditedUnix, savedAtUnix;
        }
        [Serializable] class KeptAnswers { public List<KeptAnswer> items = new List<KeptAnswer>(); }

        const string KeptFile = "obf_answers";
        const int KeepMost = 100;
        static KeptAnswers keptAnswers;

        static KeptAnswers Kept()
        {
            if (keptAnswers == null && !OfflineCache.TryLoad(KeptFile, out keptAnswers)) keptAnswers = new KeptAnswers();
            return keptAnswers ?? (keptAnswers = new KeptAnswers());
        }

        static bool TryRecall(string code, out Result result)
        {
            result = null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (KeptAnswer k in Kept().items)
            {
                if (k == null || k.barcode != code) continue;
                long age = now - k.savedAtUnix;
                if (age < 0 || age > (k.found ? KeepFoundDays : KeepNoneDays) * 86400L) return false;
                result = new Result
                {
                    answered = true, found = k.found, barcode = code, productName = k.productName ?? "", brandName = k.brandName ?? "",
                    ingredientText = k.ingredientText ?? "", lastEditedUnix = k.lastEditedUnix
                };
                return true;
            }
            return false;
        }

        static void Keep(Result r)
        {
            try
            {
                KeptAnswers all = Kept();
                all.items.RemoveAll(k => k == null || k.barcode == r.barcode);
                all.items.Add(new KeptAnswer
                {
                    barcode = r.barcode, found = r.found, productName = r.productName, brandName = r.brandName,
                    ingredientText = r.ingredientText.Length > 4000 ? r.ingredientText.Substring(0, 4000) : r.ingredientText,
                    lastEditedUnix = r.lastEditedUnix, savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                while (all.items.Count > KeepMost) all.items.RemoveAt(0);
                OfflineCache.Save(KeptFile, all);
            }
            catch (Exception e) { { } }
        }
    }
}
