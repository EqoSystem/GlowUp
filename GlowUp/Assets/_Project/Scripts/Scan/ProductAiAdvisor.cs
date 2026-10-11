using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Scanning;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// ONLINE ONLY. Three questions the app asks the AI (Gemini) about a whole PRODUCT:
    ///
    ///   Describe        what the product is, who it suits, what to take care with (the "AI" button on a tile);
    ///   Identify        which product a photo shows: name, brand, kind, its own colour and finish
    ///                   (phase 1 of a scan; works without the packaging and without an ingredient list);
    ///   FindIngredients the product's published ingredient list and what its umbrella terms can hide,
    ///                   from credible sources (when the user has no ingredient list to scan).
    ///
    /// Answers to Describe are kept on the phone, so a product that was looked up once can be read again
    /// offline. Nothing here decides whether a product is safe: that stays with ProductAnalysisEngine.
    /// </summary>
    public static class ProductAiAdvisor
    {
        /// <summary>The AI settings (set by the screens that use this, from their Inspector field).</summary>
        public static GeminiApiConfig Config;
        public static float TimeoutSeconds = 25f;
        public static bool SearchTheWeb = true;

        static GeminiApiConfig ActiveConfig => Config != null ? Config : ScanAnalysisPipeline.AiConfig;
        static string Key => GlowUp.Security.SecureProfileStore.GetApiKey();
        public static bool Available => ActiveConfig != null && !string.IsNullOrEmpty(Key);

        // =====================================================================================
        //  Describe
        // =====================================================================================
        [Serializable]
        public class Insight
        {
            public string key = "", text = "", model = "";
            public List<string> sources = new List<string>();
            public bool webSearched;
            public long savedAtUnix;
        }
        [Serializable] class InsightHolder { public List<Insight> items = new List<Insight>(); }

        const string InsightCache = "ai_product_insights";
        const int InsightLimit = 60;
        static InsightHolder insights;

        static InsightHolder Insights()
        {
            if (insights != null) return insights;
            if (!OfflineCache.TryLoad(InsightCache, out insights) || insights == null) insights = new InsightHolder();
            return insights;
        }

        static string InsightKey(string name, string brand) => ((brand ?? "").Trim() + "|" + (name ?? "").Trim()).ToLowerInvariant();

        /// <summary>What the AI said about this product earlier, if it was asked on this phone before (works offline).</summary>
        public static Insight Remembered(string name, string brand)
        {
            string key = InsightKey(name, brand);
            foreach (Insight item in Insights().items) if (item.key == key) return item;
            return null;
        }

        /// <summary>Asks the AI about a product. Returns null when the AI gave no usable answer.</summary>
        public static async Task<Insight> Describe(string name, string brand, string type, List<string> ingredients)
        {
            if (!Available || string.IsNullOrWhiteSpace(name)) return null;
            var q = new StringBuilder();
            q.Append("You are a cosmetic product reference assistant for a consumer safety app used in ").Append(IngredientResearcher.Region).Append(". ");
            q.Append("Tell a shopper about this product in plain words.\nPRODUCT: ");
            if (!string.IsNullOrWhiteSpace(brand)) q.Append(brand.Trim()).Append(' ');
            q.Append(name.Trim());
            if (!string.IsNullOrWhiteSpace(type)) q.Append(" (").Append(type.Trim()).Append(')');
            if (ingredients != null && ingredients.Count > 0)
            {
                q.Append("\nINGREDIENTS READ FROM ITS LABEL: ");
                for (int i = 0; i < ingredients.Count && i < 40; i++) q.Append(i > 0 ? ", " : "").Append(ingredients[i]);
            }
            q.Append("\nUse authoritative sources and prefer these, most trusted first: ").Append(string.Join("; ", IngredientResearcher.PreferredSources)).Append(". ");
            q.Append("Do not invent facts. If you cannot identify the exact product, say so in one sentence and describe only what the ingredient list shows. ");
            q.Append("Reply in plain text with no markdown and no asterisks, at most 130 words, in exactly four short parts, each on its own line and starting with its label: ");
            q.Append("'What it is:', 'Good for:', 'Take care:', 'How to use:'. This is general cosmetic information, not medical advice. ");
            q.Append(ProductScope.AiRules);

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), SearchTheWeb, TimeoutSeconds);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI product description: " + (answer != null ? answer.error : "no answer")); return null; }

            var insight = new Insight
            {
                key = InsightKey(name, brand), text = Clean(answer.text) + "\n\n" + ProductScope.DermatologistLine, model = answer.model, webSearched = answer.usedWebSearch,
                sources = new List<string>(answer.sources), savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            InsightHolder holder = Insights();
            holder.items.RemoveAll(i => i.key == insight.key);
            holder.items.Insert(0, insight);
            if (holder.items.Count > InsightLimit) holder.items.RemoveRange(InsightLimit, holder.items.Count - InsightLimit);
            OfflineCache.Save(InsightCache, holder);
            return insight;
        }

        static string Clean(string text) => (text ?? "").Replace("**", "").Replace("##", "").Replace("\r", "").Trim();

        // =====================================================================================
        //  Identify (phase 1 of a scan)
        // =====================================================================================
        [Serializable]
        public class Identity
        {
            public bool found;
            public string productName = "", brandName = "", cosmeticType = "", shadeHex = "", shadeName = "", finish = "";
            public float confidence;
            public string model = "";
        }

        public static readonly string[] Finishes = { "matte", "satin", "glossy", "shimmer", "metallic" };
        /// <summary>The brand names the website lists (the scanner fills this in), so the AI spells a brand it recognises the same way.</summary>
        public static List<string> KnownBrands = new List<string>();

        /// <summary>
        /// Shows the AI a photo of the product (JPG) and the text the text reader found on it.
        /// Returns what it recognised; `found` is false when it could not tell.
        /// </summary>
        public static async Task<Identity> Identify(byte[] jpegPhoto, string textOnProduct)
        {
            var result = new Identity();
            if (!Available || jpegPhoto == null || jpegPhoto.Length == 0) return result;
            var q = new StringBuilder();
            q.Append("The photo shows a cosmetic product held up to a phone camera. It may be shown with or without its packaging. Identify it.\n");
            q.Append("LOOK FIRST, READ SECOND: decide from what the pack looks like before you use any text. ");
            q.Append("Use everything the packaging shows, not only the words: the logo and its lettering, the colours and pattern of the pack, the shape of the tube, bottle, compact or box, and the cap. A well-known pack can be recognised by its look even when its text cannot be read.\n");
            if (KnownBrands != null && KnownBrands.Count > 0)
                q.Append("BRANDS THIS APP ALREADY LISTS (when the pack is one of these, write the brand exactly like this): ").Append(string.Join(", ", KnownBrands.GetRange(0, Mathf.Min(80, KnownBrands.Count)))).Append("\n");
            if (!string.IsNullOrWhiteSpace(textOnProduct))
                q.Append("TEXT A TEXT READER FOUND ON IT (may contain mistakes):\n").Append(textOnProduct.Length > 600 ? textOnProduct.Substring(0, 600) : textOnProduct).Append('\n');
            q.Append("Reply with ONLY one JSON object, no markdown and no other text, in exactly this shape:\n");
            q.Append("{\"found\":true,\"productName\":\"the product's name as sold, without the brand\",\"brandName\":\"\",");
            q.Append("\"cosmeticType\":\"one of: Lipstick, Lip Gloss, Lip Balm, Foundation, Concealer, Blush, Eyeshadow, Mascara, Moisturizer, Serum, Cleanser, Toner, Sunscreen, Mask, Skincare\",");
            q.Append("\"shadeHex\":\"#RRGGBB of the PRODUCT'S OWN colour (the lipstick bullet, the powder, the liquid), never the packaging. Look at the product itself: the tip of the lipstick, the pan, the liquid. If only the packaging is visible, use the colour a shade name or shade number printed on it stands for, or a colour dot printed on it. A face powder (loose or pressed, not a blush, bronzer or eyeshadow) is always #FFFFFF. Empty when the product's own colour cannot be known\",");
            q.Append("\"shadeName\":\"the shade name or number printed on it, or empty\",");
            q.Append("\"finish\":\"the texture of the colour, one of: matte, satin, glossy, shimmer, metallic; empty when it cannot be seen\",");
            q.Append("\"confidence\":0.0}\n");
            q.Append("confidence is 0 to 1. Do not guess a name or a brand that you can neither read nor clearly recognise: leave it empty instead. ");
            q.Append("If the photo shows no cosmetic product, reply {\"found\":false}.");

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), false, TimeoutSeconds, jpegPhoto);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI product recognition: " + (answer != null ? answer.error : "no answer")); return result; }
            Dictionary<string, object> json = AiResearchClient.JsonObjectIn(answer.text);
            if (json == null) return result;

            result.model = answer.model;
            result.productName = Text(json, "productName"); result.brandName = Text(json, "brandName"); result.cosmeticType = Text(json, "cosmeticType");
            result.shadeName = Text(json, "shadeName");
            string hex = Text(json, "shadeHex");
            result.shadeHex = hex.Length == 7 && hex[0] == '#' && ColorUtility.TryParseHtmlString(hex, out _) ? hex.ToUpperInvariant() : "";
            string finish = Text(json, "finish").ToLowerInvariant();
            result.finish = Array.IndexOf(Finishes, finish) >= 0 ? finish : "";
            if (json.TryGetValue("confidence", out object c)) { try { result.confidence = Mathf.Clamp01(Convert.ToSingle(c, System.Globalization.CultureInfo.InvariantCulture)); } catch { } }
            bool said = json.TryGetValue("found", out object f) && f is bool b && b;
            result.found = said && (result.productName.Length > 0 || result.brandName.Length > 0 || result.cosmeticType.Length > 0);
            return result;
        }

        // =====================================================================================
        //  FindIngredients (no ingredient list to scan)
        // =====================================================================================
        [Serializable]
        public class HiddenAdditive
        {
            public string term = "";                               // the word on the label, for example "Fragrance"
            public List<string> substances = new List<string>();   // what it commonly stands for
            public string note = "";
        }

        public class IngredientLookup
        {
            public bool found;
            public List<string> ingredients = new List<string>();
            public List<HiddenAdditive> hidden = new List<HiddenAdditive>();
            public List<string> sources = new List<string>();
            public bool webSearched;
            public string model = "", note = "";
        }

        /// <summary>Looks for the product's published ingredient list. `found` is false when no credible list turned up.</summary>
        public static async Task<IngredientLookup> FindIngredients(string name, string brand, string type)
        {
            var result = new IngredientLookup();
            if (!Available || string.IsNullOrWhiteSpace(name)) return result;
            var q = new StringBuilder();
            q.Append("Find the full ingredient (INCI) list of this cosmetic product as its maker publishes it.\nPRODUCT: ");
            if (!string.IsNullOrWhiteSpace(brand)) q.Append(brand.Trim()).Append(' ');
            q.Append(name.Trim());
            if (!string.IsNullOrWhiteSpace(type)) q.Append(" (").Append(type.Trim()).Append(')');
            q.Append("\nSTART WITH THE BRAND'S OFFICIAL WEBSITE: search for this product's own page there and take the list from it. ");
            q.Append("Only when the official website does not publish the list, go on to the next sources. In \"sources\" put the page the list really came from first.");
            q.Append("\nUse credible sources only, most trusted first: the brand's official website; the product pages of large retailers; ");
            q.Append("regulators and reference databases (").Append(string.Join("; ", IngredientResearcher.PreferredSources)).Append("); INCIDecoder; Open Beauty Facts.\n");
            q.Append("Also list EXTRA ADDITIVES AND HIDDEN INGREDIENTS: substances such a product contains that the label does not list one by one, ");
            q.Append("because they sit behind an umbrella term (Fragrance / Parfum, Flavor / Aroma, 'may contain' colourants, a preservative system) ");
            q.Append("or are known impurities. List one only when a credible source supports it.\n");
            q.Append("Reply with ONLY one JSON object, no markdown and no other text, in exactly this shape:\n");
            q.Append("{\"found\":true,\"ingredients\":[\"INCI name\"],");
            q.Append("\"hidden\":[{\"term\":\"the word on the label, e.g. Fragrance\",\"substances\":[\"...\"],\"note\":\"one short sentence, naming the source\"}],");
            q.Append("\"sources\":[\"page title | https://address\"],\"note\":\"\"}\n");
            q.Append("NEVER invent an ingredient list. If you cannot find the product's own published list, reply with found false and an empty ingredients array.");

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), SearchTheWeb, TimeoutSeconds + 10f);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI ingredient lookup: " + (answer != null ? answer.error : "no answer")); return result; }
            Dictionary<string, object> json = AiResearchClient.JsonObjectIn(answer.text);
            if (json == null) return result;

            result.model = answer.model; result.webSearched = answer.usedWebSearch; result.note = Text(json, "note");
            foreach (string item in Texts(json, "ingredients")) if (item.Length >= 2 && item.Length <= 90 && result.ingredients.Count < 80) result.ingredients.Add(item);
            if (json.TryGetValue("hidden", out object hiddenObject) && hiddenObject is List<object> hiddenList)
                foreach (object entry in hiddenList)
                {
                    if (!(entry is Dictionary<string, object> h)) continue;
                    var additive = new HiddenAdditive { term = Text(h, "term"), note = Text(h, "note") };
                    foreach (string s in Texts(h, "substances")) if (s.Length >= 2 && s.Length <= 60 && additive.substances.Count < 12) additive.substances.Add(s);
                    if (additive.term.Length >= 2 && additive.substances.Count > 0 && result.hidden.Count < 8) result.hidden.Add(additive);
                }
            // pages the web search really opened come first; the pages the AI only named follow
            result.sources.AddRange(answer.sources);
            foreach (string s in Texts(json, "sources"))
                if (s.Contains("http") && !result.sources.Contains(s) && result.sources.Count < 8) result.sources.Add(s);
            bool said = json.TryGetValue("found", out object f) && f is bool b && b;
            result.found = said && result.ingredients.Count >= 3;
            return result;
        }

        // ---------------------------------------------------------------------------------------
        static string Text(Dictionary<string, object> json, string key)
        {
            return json != null && json.TryGetValue(key, out object v) && v is string s ? s.Trim() : "";
        }

        static List<string> Texts(Dictionary<string, object> json, string key)
        {
            var list = new List<string>();
            if (json != null && json.TryGetValue(key, out object v) && v is List<object> items)
                foreach (object item in items) if (item is string s && s.Trim().Length > 0) list.Add(s.Trim());
            return list;
        }
    }
}
