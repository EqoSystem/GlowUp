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
            /// <summary>The cap or lid is ON (or the product is closed, or still in its box): the make-up itself cannot be seen in the photo.</summary>
            public bool capOn;
            /// <summary>
            /// Where shadeHex was taken from: "product" (the make-up itself: the bullet, the pan, the liquid, also through a
            /// clear case), "packaging" (a colour dot, swatch or shade sticker on the pack), "name" (worked out from the shade
            /// name or number printed on it). "" = no shade.
            /// </summary>
            public string shadeFrom = "";
        }

        public static readonly string[] Finishes = { "matte", "satin", "glossy", "shimmer", "metallic" };
        public static readonly string[] ShadeSources = { "product", "packaging", "name" };
        /// <summary>The brand names the website lists (the scanner fills this in), so the AI spells a brand it recognises the same way.</summary>
        public static List<string> KnownBrands = new List<string>();

        /// <summary>
        /// Shows the AI one clear photo of the product (JPG) and any text already read from it.
        /// Returns what it recognised; `found` is false when it could not tell.
        /// </summary>
        public static async Task<Identity> Identify(byte[] jpegPhoto, string textOnProduct)
        {
            var result = new Identity();
            if (!Available || jpegPhoto == null || jpegPhoto.Length == 0) return result;
            var q = new StringBuilder();
            q.Append("The photo shows one make-up product held up to a phone camera. It may be shown with or without its packaging. Identify it from this single view. This app checks MAKE-UP only, so say honestly when the product is something else.\n");
            q.Append("LOOK FIRST, READ SECOND: decide from what the pack looks like before you use any text. ");
            q.Append("Use everything the packaging shows, not only the words: the logo and its lettering, the colours and pattern of the pack, the shape of the tube, bottle, compact or box, and the cap. A well-known pack can be recognised by its look even when its text cannot be read.\n");
            if (KnownBrands != null && KnownBrands.Count > 0)
                q.Append("BRANDS THIS APP ALREADY LISTS (when the pack is one of these, write the brand exactly like this): ").Append(string.Join(", ", KnownBrands.GetRange(0, Mathf.Min(80, KnownBrands.Count)))).Append("\n");
            if (!string.IsNullOrWhiteSpace(textOnProduct))
                q.Append("TEXT A TEXT READER FOUND ON IT (may contain mistakes):\n").Append(textOnProduct.Length > 900 ? textOnProduct.Substring(0, 900) : textOnProduct).Append('\n');
            q.Append(BrandAndNameRule);
            q.Append("Reply with ONLY one JSON object, no markdown and no other text, in exactly this shape:\n");
            q.Append("{\"found\":true,\"productName\":\"the product's name as sold, without the brand\",\"brandName\":\"\",");
            q.Append("\"cosmeticType\":\"").Append(ProductScope.AiTypeChoices).Append("\",");
            q.Append("\"capOn\":false,");
            q.Append("\"shadeHex\":\"#RRGGBB of the PRODUCT'S OWN colour (the lipstick bullet, the powder, the liquid), never the packaging. Look at the product itself: the tip of the lipstick, the pan, the liquid. If only the packaging is visible, use the colour a shade name or shade number printed on it stands for, or a colour dot printed on it. A face powder (loose or pressed, not a blush, bronzer or eyeshadow) is always #FFFFFF. Empty when the product's own colour cannot be known\",");
            q.Append("\"shadeFrom\":\"where shadeHex comes from, one of: product, packaging, name; empty when shadeHex is empty\",");
            q.Append("\"shadeName\":\"the shade name or number printed on it, or empty\",");
            q.Append("\"finish\":\"the texture of the colour, one of: matte, satin, glossy, shimmer, metallic; empty when it cannot be seen\",");
            q.Append("\"confidence\":0.0}\n");
            q.Append("capOn: true when the cap or lid is ON, or the product is closed or still in its box, so that the make-up itself cannot be seen. ");
            q.Append("A see-through cap or case that shows the make-up counts as NOT on. With capOn true, never take the colour of the cap, the tube or the box for the shade. ");
            q.Append("shadeFrom: \"product\" = you saw the make-up itself (also through a clear case); \"packaging\" = a colour dot, colour swatch or shade sticker printed on the pack; ");
            q.Append("\"name\" = worked out from the shade name or number.\n");
            q.Append("confidence is 0 to 1. Do not guess a name or a brand that you can neither read nor clearly recognise: leave it empty instead. ");
            q.Append("If the photo shows no cosmetic product, reply {\"found\":false}.");

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), false, TimeoutSeconds, jpegPhoto);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI product recognition: " + (answer != null ? answer.error : "no answer")); return result; }
            return IdentityFrom(answer.text, answer.model);
        }

        // =====================================================================================
        //  The AI snapshot (a product the catalogue does not have, one photo of its pack): read by api/scan-snapshot.js, see ScanPhotoService
        // =====================================================================================
        /// <summary>What the AI read from one photo of a pack: who and what it is, and the ingredient list when it is printed and readable.</summary>
        public class PackReading
        {
            public Identity identity = new Identity();
            public List<string> ingredients = new List<string>();
        }

        /// <summary>The "ingredients" list of the AI's answer: trimmed, no repeats, no headings or claims, at most 80 names.</summary>
        public static List<string> IngredientsIn(string answerText)
        {
            var names = new List<string>();
            Dictionary<string, object> json = AiResearchClient.JsonObjectIn(answerText);
            if (json == null || !json.TryGetValue("ingredients", out object raw) || !(raw is System.Collections.IEnumerable list) || raw is string) return names;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in list)
            {
                string name = (item as string ?? "").Trim().Trim('.', ',', ';', ':', '*').Trim();
                if (name.Length < 2 || name.Length > 120) continue;
                if (name.EndsWith(":") || name.StartsWith("ingredient", StringComparison.OrdinalIgnoreCase)) continue;
                if (IngredientStoplist.IsFreeOfClaim(name)) continue;
                if (!seen.Add(name)) continue;
                names.Add(name);
                if (names.Count >= 80) break;
            }
            return names;
        }

        /// <summary>
        /// What tells a BRAND from a PRODUCT NAME, said the same way to every question about a product. On a pack the app
        /// does not know, both are just words: the AI has to be told how to sort them.
        /// </summary>
        const string BrandAndNameRule =
            "BRAND OR PRODUCT NAME? The BRAND is the company or label that makes the product: usually its logo, written the same way on everything it sells. " +
            "The PRODUCT NAME is what this ONE product or line is called, usually printed next to the kind of product (\"SuperStay Matte Ink\", \"Fit Me Matte + Poreless\"). " +
            "Never put the brand into productName. Never use the kind of product alone (\"Lipstick\", \"Loose Powder\"), a slogan, a claim (\"Long lasting\", \"24H\") " +
            "or the shade as the product name. When only one name is printed, decide which of the two it is, and leave the other empty.\n";

        /// <summary>
        /// Reads the AI's answer to Identify (the JSON object in its text). Kept apart from the asking so that the reading
        /// can be tested without the AI.
        /// </summary>
        public static Identity IdentityFrom(string answerText, string model = "")
        {
            var result = new Identity();
            Dictionary<string, object> json = AiResearchClient.JsonObjectIn(answerText);
            if (json == null) return result;

            result.model = model ?? "";
            result.productName = Text(json, "productName"); result.brandName = Text(json, "brandName"); result.cosmeticType = Text(json, "cosmeticType");
            result.productName = WithoutBrand(result.productName, result.brandName);
            result.shadeName = Text(json, "shadeName");
            string hex = Text(json, "shadeHex");
            result.shadeHex = hex.Length == 7 && hex[0] == '#' && ColorUtility.TryParseHtmlString(hex, out _) ? hex.ToUpperInvariant() : "";
            string from = Text(json, "shadeFrom").ToLowerInvariant();
            result.shadeFrom = result.shadeHex.Length > 0 && Array.IndexOf(ShadeSources, from) >= 0 ? from : "";
            result.capOn = Flag(json, "capOn");
            // with the cap on, a colour that is said to be the make-up's own can only be the cap's or the tube's: it is not taken
            if (result.capOn && result.shadeFrom == "product") { result.shadeHex = ""; result.shadeFrom = ""; }
            string finish = Text(json, "finish").ToLowerInvariant();
            result.finish = Array.IndexOf(Finishes, finish) >= 0 ? finish : "";
            result.confidence = Number01(json, "confidence");
            result.found = Flag(json, "found") && (result.productName.Length > 0 || result.brandName.Length > 0 || result.cosmeticType.Length > 0);
            return result;
        }

        /// <summary>
        /// A product name without its brand in front: "Maybelline SuperStay Matte Ink" by Maybelline is "SuperStay Matte Ink".
        /// A "name" that is nothing but the brand is no product name at all.
        /// </summary>
        public static string WithoutBrand(string productName, string brandName)
        {
            string name = (productName ?? "").Trim(), brand = (brandName ?? "").Trim();
            if (name.Length == 0 || brand.Length == 0) return name;
            if (string.Equals(name, brand, StringComparison.OrdinalIgnoreCase)) return "";
            if (name.Length > brand.Length + 1 && name.StartsWith(brand, StringComparison.OrdinalIgnoreCase) && !char.IsLetterOrDigit(name[brand.Length]))
                return name.Substring(brand.Length).TrimStart(' ', '-', ':', ',', '|').Trim();
            return name;
        }

        // =====================================================================================
        //  ConfirmOnWeb (a product the app's own lists do not have)
        // =====================================================================================
        /// <summary>What a live web search says about the product in the photo.</summary>
        public class WebMatch
        {
            /// <summary>The web shows this very product (its name AND its look agree with the photo), and a web search was really made.</summary>
            public bool same;
            public string productName = "", brandName = "", cosmeticType = "";
            /// <summary>The page that shows the product: "page title | https://address".</summary>
            public string page = "";
            public float confidence;
            public string model = "";
            public bool webSearched;
            /// <summary>MORE ABOUT IT: its ingredient list as a credible page publishes it (`found` is false when none does).</summary>
            public IngredientLookup list = new IngredientLookup();
            /// <summary>For a product the user TYPED: what is missing to tell which product is meant ("Which Maybelline lipstick? Add its name."), or "".</summary>
            public string question = "";
            /// <summary>For a product the user TYPED: it is not make-up at all (GlowUp checks make-up only).</summary>
            public bool notMakeup;
        }

        /// <summary>
        /// The app's own lists do not have the product, and a first look only GUESSED its brand and name. This asks the AI
        /// to search the web for the product by those words and by what the pack looks like, to say whether it found the
        /// very same product, how its brand and name are really written, and (in the same answer, so no second wait) its
        /// published ingredient list. Only the photo of the product, the words read on it and its barcode are sent.
        /// </summary>
        public static async Task<WebMatch> ConfirmOnWeb(byte[] jpegPhoto, Identity guess, string textOnProduct, string barcode = "")
        {
            var result = new WebMatch();
            if (!Available || !SearchTheWeb || jpegPhoto == null || jpegPhoto.Length == 0) return result;
            barcode = OpenBeautyFacts.Digits(barcode);
            var q = new StringBuilder();
            q.Append("The photo shows a make-up product held up to a phone camera. A first look named it as below. That may be wrong, and brand and product name may be mixed up.\n");
            if (guess != null)
            {
                q.Append("BRAND GUESS: ").Append(guess.brandName.Length > 0 ? guess.brandName : "(none)").Append('\n');
                q.Append("PRODUCT NAME GUESS: ").Append(guess.productName.Length > 0 ? guess.productName : "(none)").Append('\n');
                if (guess.cosmeticType.Length > 0) q.Append("KIND: ").Append(guess.cosmeticType).Append('\n');
                if (guess.shadeName.Length > 0) q.Append("SHADE PRINTED ON IT: ").Append(guess.shadeName).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(textOnProduct))
                q.Append("TEXT A TEXT READER FOUND ON IT (may contain mistakes):\n").Append(textOnProduct.Length > 700 ? textOnProduct.Substring(0, 700) : textOnProduct).Append('\n');
            if (barcode.Length > 0) q.Append("BARCODE (GTIN / EAN / UPC) printed on the pack: ").Append(barcode).Append(" (it names exactly one product: search for the number too)\n");
            q.Append("STEP 1, SEARCH THE WEB for this product: by the words above and by what the pack in the photo LOOKS like (its logo and lettering, its colours, ");
            q.Append("the shape of the tube, bottle, compact or box). Open product pages (the brand's own website or store first, then large retailers) and compare ");
            q.Append("their product pictures and names with the photo.\n");
            q.Append("STEP 2, IS IT THE SAME PRODUCT? \"same\" is true only when the name AND the look agree with a product you found. A similar product of another ");
            q.Append("brand, or another line of the same brand, is NOT the same: then reply with same false.\n");
            q.Append("STEP 3, when it is the same: write its brand and its product name the way that page sells it. ");
            q.Append(BrandAndNameRule);
            if (KnownBrands != null && KnownBrands.Count > 0)
                q.Append("BRANDS THIS APP ALREADY LISTS (when it is one of these, write the brand exactly like this): ").Append(string.Join(", ", KnownBrands.GetRange(0, Mathf.Min(80, KnownBrands.Count)))).Append("\n");
            AppendListStepAndShape(q, "");
            q.Append("confidence is 0 to 1. NEVER invent a product, a page or an ingredient list. If you cannot find the product, reply {\"same\":false}.");

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), true, TimeoutSeconds + 10f, jpegPhoto);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI web check: " + (answer != null ? answer.error : "no answer")); return result; }
            return WebMatchFrom(answer.text, answer.sources, answer.usedWebSearch, answer.model, guess != null ? guess.brandName : "");
        }

        /// <summary>
        /// THE USER TYPED A PRODUCT (they do not have the pack at hand, or the camera cannot read it). People type the way
        /// they speak: brand and name in one go, in any order, with spelling mistakes, without the shade. This asks the AI
        /// to work out which product is meant, to find it on the web, to write its brand and name the way it is sold, and
        /// to bring its published ingredient list in the same answer. When the words fit several products ("lipstick",
        /// only a brand), it does not guess: it says what is missing. Only what the user typed about the PRODUCT is sent.
        /// </summary>
        public static async Task<WebMatch> FindTyped(string typedProduct, string typedBrand = "", string typedKind = "")
        {
            var result = new WebMatch();
            typedProduct = (typedProduct ?? "").Trim(); typedBrand = (typedBrand ?? "").Trim(); typedKind = (typedKind ?? "").Trim();
            if (!Available || !SearchTheWeb || (typedProduct.Length < 2 && typedBrand.Length < 2)) return result;
            var q = new StringBuilder();
            q.Append("A user of a make-up safety app typed the product they want checked. They do not have its ingredient list. People type the way they speak: ");
            q.Append("the brand and the product name in one go, in any order, with spelling mistakes, and usually without the shade.\n");
            q.Append("TYPED: ").Append(typedProduct.Length > 200 ? typedProduct.Substring(0, 200) : typedProduct).Append('\n');
            if (typedBrand.Length > 0) q.Append("BRAND THEY GAVE: ").Append(typedBrand).Append('\n');
            if (typedKind.Length > 0) q.Append("KIND THEY CHOSE: ").Append(typedKind).Append('\n');
            if (KnownBrands != null && KnownBrands.Count > 0)
                q.Append("BRANDS THIS APP ALREADY LISTS (when it is one of these, write the brand exactly like this): ").Append(string.Join(", ", KnownBrands.GetRange(0, Mathf.Min(80, KnownBrands.Count)))).Append("\n");
            q.Append("STEP 1, SEARCH THE WEB for the make-up product these words mean (the brand's own website or store first, then large retailers).\n");
            q.Append("STEP 2, IS IT CLEAR WHICH PRODUCT? \"same\" is true only when the words clearly mean ONE product you found. When they fit several different products ");
            q.Append("(only a kind such as \"lipstick\", only a brand, or a name several brands use), do NOT pick one: reply with same false and put into \"question\" ");
            q.Append("one short sentence that tells the user what to add, for example \"Which Maybelline lipstick? Add the name printed on it.\" ");
            q.Append("Different shades of one product are the same product. This app checks MAKE-UP only: when the words mean something that is not make-up ");
            q.Append("(a shampoo, a soap, a skin-care cream, a perfume, a medicine), reply with same false and notMakeup true.\n");
            q.Append("STEP 3, when it is clear: write its brand and its product name the way that page sells it. ");
            q.Append(BrandAndNameRule);
            AppendListStepAndShape(q, "\"question\":\"\",\"notMakeup\":false,");
            q.Append("confidence is 0 to 1. NEVER invent a product, a page or an ingredient list. If you cannot find the product, reply {\"same\":false,\"question\":\"...\"}.");

            AiResearchClient.Answer answer = await AiResearchClient.Ask(ActiveConfig, Key, q.ToString(), true, TimeoutSeconds + 10f);
            if (answer == null || !answer.ok) { Debug.LogWarning("AI look-up of a typed product: " + (answer != null ? answer.error : "no answer")); return result; }
            return WebMatchFrom(answer.text, answer.sources, answer.usedWebSearch, answer.model, typedBrand);
        }

        // the last step of both web questions (the product's published ingredient list) and the shape of the answer
        static void AppendListStepAndShape(StringBuilder q, string extraFields)
        {
            q.Append("STEP 4, MORE ABOUT IT: its full ingredient (INCI) list as its maker publishes it. Use credible sources only, most trusted first: the brand's official ");
            q.Append("website or official store page; the product pages of large retailers that reproduce the list printed on the pack; Open Beauty Facts. ");
            q.Append("For products sold in the Philippines, search Watsons Philippines (watsons.com.ph) among the retailer pages. ");
            q.Append("Ingredient DATABASES (INCIDecoder, EWG Skin Deep, CosDNA, CosIng) explain what an ingredient is: do NOT take this product's list from them. ");
            q.Append("The list must be a copy of what is printed on the pack: never reconstruct it from memory or from a similar product. ");
            q.Append("When no credible page shows the list, leave \"ingredients\" empty: the product can still be the same.\n");
            q.Append("Reply with ONLY one JSON object, no markdown and no other text, in exactly this shape:\n");
            q.Append("{\"same\":true,\"brandName\":\"\",\"productName\":\"as sold, without the brand and without the shade\",");
            q.Append("\"cosmeticType\":\"").Append(ProductScope.AiTypeChoices).Append("\",");
            q.Append("\"page\":\"page title | https://address of the page that shows this product\",\"confidence\":0.0,");
            q.Append(extraFields);
            q.Append("\"ingredients\":[\"INCI name\"],");
            q.Append("\"hidden\":[{\"term\":\"the word on the label, e.g. Fragrance\",\"substances\":[\"...\"],\"note\":\"one short sentence, naming the source\"}],");
            q.Append("\"sources\":[\"page title | https://address the ingredient list came from\"],\"note\":\"\"}\n");
        }

        /// <summary>
        /// Reads the AI's answer to ConfirmOnWeb. `pagesOpened` = the pages the web search really opened; `webSearched` =
        /// a live web search was made. WITHOUT a live search nothing is "confirmed on the web": the answer would only be
        /// the AI's memory. Kept apart from the asking so that the reading can be tested without the AI.
        /// </summary>
        public static WebMatch WebMatchFrom(string answerText, List<string> pagesOpened, bool webSearched, string model = "", string brandGuess = "")
        {
            var result = new WebMatch { model = model ?? "", webSearched = webSearched };
            Dictionary<string, object> json = AiResearchClient.JsonObjectIn(answerText);
            if (json == null) return result;
            result.brandName = Text(json, "brandName"); result.cosmeticType = Text(json, "cosmeticType");
            result.productName = WithoutBrand(Text(json, "productName"), result.brandName);
            string page = Text(json, "page");
            result.page = page.Contains("http") ? page : "";
            result.confidence = Number01(json, "confidence");
            result.same = webSearched && Flag(json, "same") && (result.productName.Length > 0 || result.brandName.Length > 0);
            string question = Text(json, "question");
            result.question = question.Length <= 160 ? question : question.Substring(0, 160).TrimEnd() + "...";
            result.notMakeup = Flag(json, "notMakeup");
            if (!result.same) return result;                 // a list for "some similar product" is never taken
            result.list = LookupFrom(json, pagesOpened, webSearched, model, result.brandName.Length > 0 ? result.brandName : brandGuess, false);
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
        /// <summary>`barcode` = the digits under the pack's barcode when the camera read them ("" = not read): they name ONE product exactly, so the search can make sure a page is about this very product and size.</summary>
        public static async Task<IngredientLookup> FindIngredients(string name, string brand, string type, string barcode = "")
        {
            var result = new IngredientLookup();
            barcode = OpenBeautyFacts.Digits(barcode);
            if (!Available || (string.IsNullOrWhiteSpace(name) && barcode.Length == 0)) return result;
            name = name ?? "";
            var q = new StringBuilder();
            q.Append("Find the full ingredient (INCI) list of this cosmetic product as its maker publishes it.\nPRODUCT: ");
            if (!string.IsNullOrWhiteSpace(brand)) q.Append(brand.Trim()).Append(' ');
            q.Append(name.Trim());
            if (!string.IsNullOrWhiteSpace(type)) q.Append(" (").Append(type.Trim()).Append(')');
            if (barcode.Length > 0)
            {
                q.Append("\nBARCODE (GTIN / EAN / UPC) printed on the pack: ").Append(barcode);
                q.Append("\nThe barcode names exactly one product and size. Search for the barcode number as well as for the name, and prefer a page ");
                q.Append("that shows this barcode: then the list is that of this very product and not of a similar one. ");
                q.Append("If the name above is empty or looks misread, take the product's name from the pages that show this barcode.");
            }
            q.Append("\nSTART WITH THE BRAND'S OFFICIAL WEBSITE: search for this product's own page there and take the list from it. ");
            q.Append("Only when the official website does not publish the list, go on to the next sources. In \"sources\" put the page the list really came from first.");
            q.Append("\nUse credible sources only, most trusted first: the brand's official website or official store page; the product pages of large retailers ");
            q.Append("that reproduce the list printed on the pack; Open Beauty Facts. For products sold in the Philippines, include Watsons Philippines (watsons.com.ph) among the retailer pages to search. ");
            q.Append("Ingredient DATABASES (INCIDecoder, EWG Skin Deep, CosDNA, CosIng) explain what an ingredient is: do NOT take this product's list from them. ");
            q.Append("The list must be a copy of what is printed on the pack: never reconstruct it from memory or from a similar product.\n");
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
            return LookupFrom(json, answer.sources, answer.usedWebSearch, answer.model, brand, true);
        }

        /// <summary>
        /// The ingredient list in an answer of the AI, with the rules every looked-up list has to pass: real names only, and
        /// at least one source the app can show that is not an ingredient database. Used by FindIngredients (whose answer must
        /// say "found" itself) and by ConfirmOnWeb.
        /// </summary>
        static IngredientLookup LookupFrom(Dictionary<string, object> json, List<string> pagesOpened, bool webSearched, string model, string brand, bool mustSayFound)
        {
            var result = new IngredientLookup { model = model ?? "", webSearched = webSearched, note = Text(json, "note") };
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
            var named = new List<string>(pagesOpened ?? new List<string>());
            foreach (string s in Texts(json, "sources"))
                if (s.Contains("http") && !named.Contains(s) && named.Count < 8) named.Add(s);
            // A LIST IS ONLY ACCEPTED WITH A SOURCE THE APP CAN SHOW. Pages that can be opened and are not ingredient
            // databases stay, the most trusted first (the brand's site, a retailer, Open Beauty Facts ...). When none is
            // left, the answer is "not found": an AI-written list is never shown as if it were the pack's list.
            result.sources = ListSources.Usable(named, brand);
            // FindIngredients' answer says "found" itself; the answer to ConfirmOnWeb has none: there a list counts when it is there
            bool said = mustSayFound ? Flag(json, "found") : true;
            result.found = said && result.ingredients.Count >= 3 && result.sources.Count > 0;
            if (said && result.ingredients.Count >= 3 && result.sources.Count == 0)
                Debug.LogWarning("AI ingredient lookup: a list came without a source that can be shown, so it was not accepted.");
            return result;
        }

        // ---------------------------------------------------------------------------------------
        static string Text(Dictionary<string, object> json, string key)
        {
            return json != null && json.TryGetValue(key, out object v) && v is string s ? s.Trim() : "";
        }

        // true / "true" (an AI now and then writes a yes as a word)
        static bool Flag(Dictionary<string, object> json, string key)
        {
            if (json == null || !json.TryGetValue(key, out object v) || v == null) return false;
            if (v is bool b) return b;
            return v is string s && s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        static float Number01(Dictionary<string, object> json, string key)
        {
            if (json == null || !json.TryGetValue(key, out object v) || v == null) return 0f;
            try { return Mathf.Clamp01(Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture)); } catch { return 0f; }
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
