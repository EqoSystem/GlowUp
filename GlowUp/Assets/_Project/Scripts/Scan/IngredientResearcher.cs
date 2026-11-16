using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Scanning;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// ONLINE ONLY. When a scan contains ingredients that are in neither the website's database nor the
    /// app's built-in list, this looks them up:
    ///   1. the AI is asked about them, searching the web and preferring the regulators and reference
    ///      databases listed in Preferred Sources (US FDA, FDA Philippines / ASEAN, EU CosIng ...);
    ///   2. each name is checked against the US FDA's public substance registry;
    ///   3. what was found is saved on the phone (so the scan in progress can use it) and sent to the
    ///      shared 'ingredients' collection, where it shows on the website's Ingredient Curator.
    ///
    /// Everything it adds is marked source "ai" and "not reviewed". Until an admin reviews an entry on
    /// the website, the app shows its description but never lets it declare an ingredient safe: an
    /// unreviewed AI entry can add a warning, it cannot remove one.
    /// All settings are Inspector fields on LoadingSceneController.
    /// </summary>
    public static class IngredientResearcher
    {
        public static bool Enabled = true;
        /// <summary>At most this many unknown ingredients are looked up per scan (keeps the wait short).</summary>
        public static int MaximumPerScan = 6;
        public static float TimeoutSeconds = 25f;
        public static bool SearchTheWeb = true;
        public static bool CheckFdaRegistry = true;
        /// <summary>Where the app is used; the AI gives this country's rules priority.</summary>
        public static string Region = "the Philippines";
        /// <summary>Sources the AI is told to prefer, most trusted first.</summary>
        public static string[] PreferredSources =
        {
            "fda.gov (US Food and Drug Administration)",
            "fda.gov.ph (FDA Philippines) and the ASEAN Cosmetic Directive ingredient annexes",
            "ec.europa.eu CosIng (EU cosmetic ingredient database)",
            "cir-safety.org (Cosmetic Ingredient Review)",
            "pubchem.ncbi.nlm.nih.gov (PubChem)",
            "watsons.com.ph (Watsons Philippines product listings)"
        };

        static readonly string[] AllergenTags = { "fragrance", "paraben", "sulfate", "formaldehyde", "lanolin", "essentialoil", "nut" };
        static readonly string[] SkinTypes = { "Oily", "Normal", "Dry", "Mix" };
        static readonly string[] Conditions = { "acne", "eczema", "psoriasis", "rosacea", "seborrheic", "melasma", "contact" };
        static readonly string[] RiskLevels = { "safe", "caution", "harmful" };

        /// <summary>The names in this list that nothing is known about yet (each once, junk left out).</summary>
        public static List<string> Unknown(List<string> labelNames)
        {
            var unknown = new List<string>();
            var seen = new HashSet<string>();
            foreach (string raw in labelNames ?? new List<string>())
            {
                string name = (raw ?? "").Trim();
                string key = IngredientKnowledge.Key(name);
                if (key.Length < 4 || key.Length > 70 || !seen.Add(key)) continue;
                if (!name.Any(char.IsLetter) || name.Count(char.IsDigit) > name.Count(char.IsLetter)) continue;      // "12345", "50ml" ...
                if (IngredientKnowledge.Resolve(name) == null) unknown.Add(name);
            }
            return unknown;
        }

        /// <summary>
        /// Looks up the unknown ingredients of a scan and adds what it finds to the phone's list and to the
        /// shared database. Returns how many were added. `say` shows a line on the loading screen.
        /// </summary>
        public static async Task<int> ResearchUnknown(List<string> labelNames, GeminiApiConfig config, Action<string> say)
        {
            if (!Enabled || config == null) return 0;
            List<string> unknown = Unknown(labelNames).Take(Mathf.Max(1, MaximumPerScan)).ToList();
            if (unknown.Count == 0) return 0;

            string apiKey = GlowUp.Security.SecureProfileStore.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) return 0;

            say?.Invoke(unknown.Count == 1 ? "Looking up 1 ingredient we have not seen before" : $"Looking up {unknown.Count} ingredients we have not seen before");
            AiResearchClient.Answer answer = await AiResearchClient.Ask(config, apiKey, Question(unknown), SearchTheWeb, TimeoutSeconds);
            if (!answer.ok) { { } return 0; }

            var root = AiResearchClient.JsonObjectIn(answer.text);
            var items = root != null && root.TryGetValue("ingredients", out object list) ? list as List<object> : null;
            if (items == null) { { } return 0; }

            string uid = UserProfileRepository.CurrentUid;          // null when not signed in to the cloud: then it is kept on this phone only
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int added = 0;
            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> found)) continue;
                IngredientData data = Read(found, unknown, answer);
                if (data == null) continue;
                if (IngredientKnowledge.Resolve(data.techName) != null || IngredientKnowledge.Resolve(data.name) != null) continue;   // known under its proper spelling after all

                if (CheckFdaRegistry) data.fdaUnii = await FdaSubstanceRegistry.FindUnii(string.IsNullOrEmpty(data.techName) ? data.name : data.techName);

                IngredientRepository.AddLocal(data);
                if (!string.IsNullOrEmpty(uid))
                    SyncQueue.EnqueueSet("ingredients/" + data.id, AiIngredientRecord.From(data, uid, now, answer.usedWebSearch, answer.model));
                added++;
            }
            return added;
        }

        // ---------------------------------------------------------------------------------------
        static string Question(List<string> names)
        {
            var q = new StringBuilder();
            q.Append("You are a cosmetic-ingredient reference assistant for a consumer safety app used in ").Append(Region).Append(". ");
            q.Append("Look up each ingredient below. Use authoritative sources and prefer these, most trusted first: ");
            q.Append(string.Join("; ", PreferredSources)).Append(". ");
            q.Append("Do not guess: if you cannot confirm what an ingredient is, set \"found\" to false for it.\n\n");
            q.Append("INGREDIENTS (read from a product label by a camera, so the spelling may be slightly wrong):\n");
            for (int i = 0; i < names.Count; i++) q.Append(i + 1).Append(". ").Append(names[i]).Append('\n');
            q.Append("\nReply with ONLY one JSON object, no markdown and no other text, in exactly this shape:\n");
            q.Append("{\"ingredients\":[{\"query\":\"the name exactly as given above\",\"found\":true,");
            q.Append("\"name\":\"the common name consumers know\",\"techName\":\"the INCI / label name, correctly spelled\",");
            q.Append("\"description\":\"one or two plain sentences: what it is and what it does in a cosmetic\",");
            q.Append("\"commonUses\":\"kinds of product, e.g. serums, moisturisers\",");
            q.Append("\"riskLevel\":\"safe or caution or harmful\",");
            q.Append("\"cautionNote\":\"a short phrase that completes the sentence 'It ...', or empty\",");
            q.Append("\"regulatoryNote\":\"if a regulator (US FDA, FDA Philippines / ASEAN Cosmetic Directive, EU) prohibits or restricts it in cosmetics, say which one and how; otherwise empty\",");
            q.Append("\"allergenTags\":[any of: fragrance, paraben, sulfate, formaldehyde, lanolin, essentialoil, nut],");
            q.Append("\"avoidForSkinTypes\":[any of: Oily, Normal, Dry, Mix],\"goodForSkinTypes\":[any of: Oily, Normal, Dry, Mix],");
            q.Append("\"avoidForConditions\":[any of: acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact],");
            q.Append("\"goodForConditions\":[any of: acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact],");
            q.Append("\"minAge\":0,\"keyActive\":false,\"umbrella\":false,\"aliases\":[\"other spellings used on labels\"],");
            q.Append("\"sources\":[\"full web address of each page you relied on\"]}]}\n");
            q.Append("Rules: use riskLevel \"harmful\" only when a regulator prohibits the ingredient in cosmetics or it is widely recognised as unsafe at cosmetic use levels; ");
            q.Append("\"caution\" when it is restricted, or a common irritant or allergen; otherwise \"safe\". ");
            q.Append("\"umbrella\" is true only for label terms that can hide several undisclosed ingredients (like Fragrance). ");
            q.Append("Keep the description under 240 characters. One entry per ingredient, in the same order.");
            return q.ToString();
        }

        /// <summary>One entry of the AI's answer, cleaned and limited to the words the app understands. Null when unusable.</summary>
        static IngredientData Read(Dictionary<string, object> found, List<string> asked, AiResearchClient.Answer answer)
        {
            if (found.TryGetValue("found", out object f) && f is bool isFound && !isFound) return null;
            string query = Text(found, "query", 120);
            string techName = Text(found, "techName", 120);
            string name = Text(found, "name", 80);
            string description = Text(found, "description", 320);
            if (techName.Length == 0) techName = query;
            if (name.Length == 0) name = techName;
            if (name.Length < 3 || description.Length < 10) return null;
            // An entry is only made for ONE real ingredient. Two names in one ("Carbomer & Triethanolamine"), label
            // wording or a code never becomes an ingredient entry, whatever the AI says about it.
            if (!IsOneIngredientName(techName) || !IsOneIngredientName(name)) return null;

            // it must be an answer to something we asked (the AI must not add ingredients of its own)
            string queryKey = IngredientKnowledge.Key(query);
            if (!asked.Any(a => IngredientKnowledge.Key(a) == queryKey)) return null;

            string regulatory = Text(found, "regulatoryNote", 240);
            if (regulatory.Length > 0) description = (description.TrimEnd('.') + ". " + regulatory).Trim();

            string risk = Text(found, "riskLevel", 12).ToLowerInvariant();
            var data = new IngredientData
            {
                id = "ai_" + IngredientKnowledge.Key(techName).Replace(' ', '_'),
                name = name, techName = techName, description = description,
                commonUses = Text(found, "commonUses", 160),
                riskLevel = RiskLevels.Contains(risk) ? risk : "caution",        // unclear answer: err on the careful side
                cautionNote = CleanNote(Text(found, "cautionNote", 160)),
                minAge = Mathf.Clamp(Number(found, "minAge"), 0, 99),
                keyActive = Flag(found, "keyActive"), umbrella = Flag(found, "umbrella"),
                aliases = Words(found, "aliases", null, 8, 80),
                allergenTags = Words(found, "allergenTags", AllergenTags, 7, 20),
                avoidForSkinTypes = Words(found, "avoidForSkinTypes", SkinTypes, 4, 12),
                goodForSkinTypes = Words(found, "goodForSkinTypes", SkinTypes, 4, 12),
                avoidForConditions = Words(found, "avoidForConditions", Conditions, 7, 20),
                goodForConditions = Words(found, "goodForConditions", Conditions, 7, 20),
                source = "ai", reviewed = false
            };
            if (data.id.Length > 90) data.id = data.id.Substring(0, 90);
            if (!data.aliases.Any(a => IngredientKnowledge.Key(a) == queryKey) && IngredientKnowledge.Key(techName) != queryKey && IngredientKnowledge.Key(name) != queryKey)
                data.aliases.Insert(0, query);                                   // the label's own spelling must find the entry again

            // pages the web search really used come first; addresses the AI only wrote down come after
            data.sources = new List<string>(answer.sources);
            foreach (string address in Words(found, "sources", null, 6, 300))
                if ((address.StartsWith("https://") || address.StartsWith("http://")) && data.sources.Count < 8 && !data.sources.Any(s => s.EndsWith(address)))
                    data.sources.Add(address);
            return data;
        }

        /// <summary>True when the text can be the name of one single ingredient: no joined names, nothing from the stoplist, the right shape.</summary>
        public static bool IsOneIngredientName(string name)
        {
            string text = (name ?? "").Trim();
            if (text.Length < 3) return false;
            if (text.IndexOf(',') >= 0 || text.IndexOf(';') >= 0 || text.IndexOf('&') >= 0 || text.IndexOf('+') >= 0) return false;
            string lower = " " + text.ToLowerInvariant() + " ";
            if (lower.Contains(" and ") || lower.Contains(" / ") || lower.Contains(" or ")) return false;
            if (IngredientKnowledge.IsExactName(text)) return true;
            return IngredientStoplist.Hit(text) == null && !IngredientStoplist.WrongShape(text);
        }

        static string CleanNote(string note)
        {
            note = note.Trim().TrimEnd('.');
            if (note.StartsWith("It ", StringComparison.OrdinalIgnoreCase)) note = note.Substring(3);
            return note.Length > 0 ? char.ToLowerInvariant(note[0]) + note.Substring(1) : "";
        }

        static string Text(Dictionary<string, object> d, string key, int max)
        {
            string s = d.TryGetValue(key, out object v) && v != null ? Plain(v).Replace('\n', ' ').Replace('\r', ' ').Trim() : "";
            return s.Length > max ? s.Substring(0, max).TrimEnd() : s;
        }

        /// <summary>
        /// A value from the AI's answer as text. The AI sometimes answers with a list where a sentence was asked for
        /// (commonUses: ["serums", "toners"]); the items are then joined with commas. Anything that is not text,
        /// a number or such a list gives "" (never the name of a C# type).
        /// </summary>
        static string Plain(object value)
        {
            if (value is string text) return text;
            if (value is System.Collections.IDictionary) return "";
            if (value is System.Collections.IEnumerable list)
            {
                var parts = new List<string>();
                foreach (object item in list)
                {
                    string part = item == null || item is System.Collections.IDictionary || (!(item is string) && item is System.Collections.IEnumerable) ? "" : item.ToString().Trim();
                    if (part.Length > 0) parts.Add(part);
                }
                return string.Join(", ", parts);
            }
            return value is bool ? "" : value.ToString();
        }

        static int Number(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return 0;
            if (v is long l) return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, l));
            if (v is double x) return (int)x;
            return int.TryParse(v.ToString(), out int parsed) ? parsed : 0;
        }

        static bool Flag(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is bool b && b;

        /// <summary>A list of short texts; when `allowed` is given only those words are kept (in the app's own spelling).</summary>
        static List<string> Words(Dictionary<string, object> d, string key, string[] allowed, int maxItems, int maxLength)
        {
            var words = new List<string>();
            if (!d.TryGetValue(key, out object v) || !(v is List<object> items)) return words;
            foreach (object item in items)
            {
                string word = item == null ? "" : item.ToString().Trim();
                if (word.Length == 0 || word.Length > maxLength) continue;
                if (allowed != null)
                {
                    word = allowed.FirstOrDefault(a => string.Equals(a, word, StringComparison.OrdinalIgnoreCase));
                    if (word == null) continue;
                }
                if (!words.Contains(word) && words.Count < maxItems) words.Add(word);
            }
            return words;
        }
    }
}
