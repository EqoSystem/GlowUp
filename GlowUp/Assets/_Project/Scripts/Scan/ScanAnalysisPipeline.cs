using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Scanning;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// The work the loading scene runs after a scan:
    ///   step 0  split the label text into ingredients
    ///   step 1  look them up (website database, then built-in knowledge)
    ///   step 2  cross-check with the user's profile (+ an AI second opinion when online)
    ///   step 3  save to Recent Scans and hand the result to the result scene
    /// Everything except the AI second opinion works offline.
    /// </summary>
    public static class ScanAnalysisPipeline
    {
        // set by LoadingSceneController from its Inspector fields
        public static bool UseAi = true;
        public static GeminiApiConfig AiConfig;
        public static float AiTimeoutSeconds = 20f;
        /// <summary>Compare each scan with the website's product inventory (see ProductMatcher).</summary>
        public static bool RecogniseCatalogueProducts = true;
        /// <summary>
        /// The FDA Philippines search runs beside the other steps. When they are done, the result waits at most this many
        /// more seconds for it; after that the result opens and its FDA badge fills in by itself when the answer arrives.
        /// </summary>
        public static float FdaExtraWaitSeconds = 4f;

        public static async Task Run(Action<int, string> report)
        {
            ScanSession.LastError = null;

            report(0, "Reading the ingredient list");
            List<string> names = IngredientTextParser.Parse(ScanSession.PendingText);
            if (names.Count == 0)
            {
                ScanSession.LastError = "We could not find an ingredient list in that. Try again with the list filling the frame, or type the ingredients in.";
                SceneNavigator.Retarget(SceneNames.AiSmartAnalysis);
                return;
            }
            await Task.Yield();

            // ONLINE: is the product on the Philippine FDA's list of notified cosmetics? One search of the FDA's site takes 12 to 15
            // seconds, so it starts NOW and runs beside the other steps. (Not for a list that came from the app's own inventory:
            // such a product usually carries its FDA record already, see ApplyCatalogueProduct.)
            Task<PhFdaVerification.Result> fdaSearch = ScanSession.PendingListFrom == "inventory" ? null
                : StartFdaSearch(ScanSession.PendingProductName, ScanSession.PendingBrandName);

            report(1, "Checking our ingredient database");
            await IngredientRepository.LoadAll();
            List<BrandData> brands = await BrandRepository.LoadActive();

            // ONLINE: ingredients nobody has described yet are looked up (AI + web search + the FDA's substance
            // registry) and added to the shared database, marked "AI, not reviewed". Offline they stay "not checked".
            if (IngredientResearcher.Enabled && AiConfig != null && ConnectivityMonitor.Online)
            {
                try { await IngredientResearcher.ResearchUnknown(names, AiConfig, message => report(1, message)); }
                catch (Exception e) { Debug.LogWarning("Ingredient lookup skipped: " + e.Message); }
            }

            report(2, "Comparing with your skin profile");
            UserProfileData profile = await UserProfileRepository.Load() ?? new UserProfileData();
            ScanRecord record = ProductAnalysisEngine.Analyse(names, ScanSession.PendingText, profile, brands);
            record.source = string.IsNullOrEmpty(ScanSession.PendingSource) ? "scan" : ScanSession.PendingSource;
            record.shadeHex = ScanSession.PendingShadeHex ?? "";

            // is it a product from the website's inventory? (its ingredient list is nearly the same)
            if (RecogniseCatalogueProducts)
            {
                try
                {
                    ProductData known = ProductMatcher.Find(names, await ProductRepository.LoadActive(), record.brandId);
                    if (known != null) ApplyCatalogueProduct(record, known, profile);
                }
                catch (Exception e) { Debug.LogWarning("Product recognition skipped: " + e.Message); }
            }

            ApplyRecognised(record, profile);
            if (!string.IsNullOrEmpty(record.fdaStatus)) fdaSearch = null;                        // the inventory had the FDA record
            else if (fdaSearch == null) fdaSearch = StartFdaSearch(record.productName, record.brandName);

            if (UseAi && AiConfig != null && ConnectivityMonitor.Online)
            {
                report(2, "Asking the AI for a second opinion");
                try { await EnhanceWithAi(record, profile); }
                catch (Exception e) { Debug.LogWarning("AI second opinion skipped: " + e.Message); }
            }

            // the FDA Philippines answer: taken now when it has arrived. Otherwise the result waits a few seconds for it and then
            // opens without it: its badge says "checking" and fills in by itself when the answer comes (FinishFdaLater).
            if (fdaSearch != null)
            {
                if (!fdaSearch.IsCompleted) { report(2, "Checking the FDA Philippines list"); await WaitFor(fdaSearch, FdaExtraWaitSeconds); }
                if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");      // the late answer finds the saved scan by its id
                if (fdaSearch.IsCompleted) UseFdaAnswer(record, fdaSearch);
                else FinishFdaLater(record, fdaSearch);
            }

            // make-up and face products come first: the notice for anything else, for skin care and for skin conditions,
            // and the dermatologist line at the end of every result (see ProductScope)
            string scopeNotice = ProductScope.Apply(record, profile);

            // the user's own Ingredient Safety Alerts (Settings page): checked on the phone, shown as soon as the result opens
            try
            {
                ScanSession.Alerts = SafetyAlertEvaluator.Evaluate(record, names, profile);
                if (scopeNotice.Length > 0) ScanSession.Alerts.Insert(0, new SafetyAlert { trigger = SafetyAlert.Notice, detail = scopeNotice });
                ScanSession.AlertPending = ScanSession.Alerts.Count > 0;
            }
            catch (Exception e) { ScanSession.AlertPending = false; Debug.LogWarning("Safety alerts skipped: " + e.Message); }

            report(3, "Preparing your result");
            // ONLINE: a product the inventory does not have yet is added to it as "Pending" (see ScannedProductPublisher)
            if (ConnectivityMonitor.Online)
            {
                try { ScannedProductPublisher.PublishIfNew(record, names); }
                catch (Exception e) { Debug.LogWarning("Adding the product to the inventory was skipped: " + e.Message); }
            }
            ScanHistoryRepository.Add(record);                       // every scan is saved to Recent Scans automatically
            if (!string.IsNullOrEmpty(record.brandId)) BrandRepository.RecordScan(record.brandId);   // the brand's scan counter on the admin website
            if (!string.IsNullOrEmpty(record.productId)) ProductRepository.RecordScan(record.productId); // the product's "App Scans" on the admin website
            SyncQueue.EnqueueSet("scanActivity/" + record.id, ScanActivity.From(record));           // anonymous: feeds the admin dashboard
            ScanSession.Current = record;
            ScanSession.ResultBackScene = SceneNames.Dashboard;
            ScanSession.ClearPending();
        }

        /// <summary>What phase 1 of the scan recognised (name, brand, kind, shade, texture) and where a looked-up ingredient list came from.</summary>
        static void ApplyRecognised(ScanRecord record, UserProfileData profile)
        {
            if (string.IsNullOrEmpty(record.productId))               // a product from the inventory keeps the inventory's details
            {
                if (string.IsNullOrWhiteSpace(record.productName) && !string.IsNullOrWhiteSpace(ScanSession.PendingProductName)) record.productName = ScanSession.PendingProductName.Trim();
                if (string.IsNullOrWhiteSpace(record.brandName) && !string.IsNullOrWhiteSpace(ScanSession.PendingBrandName)) record.brandName = ScanSession.PendingBrandName.Trim();
                if (!string.IsNullOrWhiteSpace(ScanSession.PendingCosmeticType) && (string.IsNullOrWhiteSpace(record.cosmeticType) || record.cosmeticType == "Skincare"))
                {
                    record.cosmeticType = ScanSession.PendingCosmeticType.Trim();
                    record.recommendedShades = ShadeAdvisor.Recommend(profile.skinTone, ShadeAdvisor.AreaFor(record.cosmeticType));
                }
            }
            record.shadeName = ScanSession.PendingShadeName ?? "";
            record.finish = ScanSession.PendingFinish ?? "";
            record.barcode = ScanSession.PendingBarcode ?? "";
            record.ingredientSources = new List<string>(ScanSession.PendingSources ?? new List<string>());
            // where the list came from. The result screen says it in one short line and offers the pages as buttons
            // (it used to be a paragraph with the web addresses inside "Why this result?")
            record.listFrom = !string.IsNullOrEmpty(ScanSession.PendingListFrom) ? ScanSession.PendingListFrom
                : record.source == "manual" ? "typed" : record.source == "lookup" ? "web" : "pack";
            record.listWebBacked = ScanSession.PendingListWebBacked;
        }

        /// <summary>The scan is a known product: take its name, brand, type and shades from the inventory.</summary>
        static void ApplyCatalogueProduct(ScanRecord record, ProductData known, UserProfileData profile)
        {
            record.productId = known.id;
            record.productName = known.name;
            if (!string.IsNullOrWhiteSpace(known.brandName)) record.brandName = known.brandName;
            if (string.IsNullOrEmpty(record.brandId)) record.brandId = known.brandId ?? "";
            if (!string.IsNullOrWhiteSpace(known.cosmeticType))
            {
                record.cosmeticType = known.cosmeticType;
                record.recommendedShades = ShadeAdvisor.Recommend(profile.skinTone, ShadeAdvisor.AreaFor(record.cosmeticType));
            }
            record.productShades = new List<string>(known.shades ?? new List<string>());

            // the FDA Philippines record the website keeps with the product: no search is needed, and it is there offline too
            PhFdaVerification.Result onFile = PhFdaVerification.FromInventory(known.fdaNumber, known.description);
            if (onFile != null) ApplyFda(record, onFile);

            // the product's own minimum age (set on the website)
            if (known.minAge > 0 && profile.age > 0 && profile.age < known.minAge)
            {
                record.ageMatch = new CompatibilityItem { level = "caution", summary = $"This product is listed for ages {known.minAge} and up." };
                if (record.status == "safe")
                {
                    record.status = "caution";
                    record.summary = $"The ingredients look fine, but this product is listed for ages {known.minAge} and up.";
                }
                record.dermatologistAdvised = true;
            }
        }

        // ---------------------------------------------------------------------------------------
        //  AI second opinion (Gemini). The rule-based result above stays the base; the AI adds a
        //  plain-language explanation, product / type guesses and anything our database missed.
        // ---------------------------------------------------------------------------------------
        static async Task EnhanceWithAi(ScanRecord record, UserProfileData profile)
        {
            string apiKey = GlowUp.Security.SecureProfileStore.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) return;

            var client = new GeminiApiClient(AiConfig, apiKey);
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(5f, AiTimeoutSeconds))))
            {
                GeminiScanResult result = await client.AnalyzeAsync(BuildRequest(record, profile), cts.Token);
                if (result == null || !result.Success || result.Analysis == null) return;
                Merge(record, result.Analysis);
            }
        }

        static string BuildRequest(ScanRecord record, UserProfileData profile)
        {
            var p = new StringBuilder();
            p.Append("You are a cosmetic ingredient safety assistant. Analyse this ingredient list for the user below. ");
            p.Append("Reply with ONLY one JSON object, no markdown, with exactly these fields: ");
            p.Append("ProductSummary (string, 1-2 short sentences), IsSafeForUser (boolean), ");
            p.Append("ProductName (string, empty if unknown), BrandName (string, empty if unknown), ");
            p.Append("CosmeticType (one of: Lipstick, Lip Gloss, Lip Balm, Foundation, Concealer, Blush, Eyeshadow, Mascara, Moisturizer, Serum, Cleanser, Toner, Sunscreen, Mask, Skincare), ");
            p.Append("RawIngredients (array of strings), ");
            p.Append("SafetyFlags (array of objects with Ingredient, Severity which is low or medium or high, and Reason), ");
            p.Append("MatchedAllergens (array of strings), KeyActives (array of at most 6 strings), ");
            p.Append("HiddenIngredientAlerts (array of objects with LabelTerm, WhyItsOpaque, CommonlyHiddenSubstances as an array of strings, RelevanceToUserProfile), ");
            p.Append("RecommendationNotes (array of short strings), RegulatoryInsights (empty array), ");
            p.Append("Explanation (string, 3-5 plain sentences on why this product suits or conflicts with THIS user). ");
            p.Append("Only flag an ingredient when it genuinely matters for this user's allergens, skin type, skin conditions or age. ");
            p.Append("Do not diagnose or give medical advice. ").Append(ProductScope.AiRules).Append("\n\n");
            p.Append("INGREDIENTS (read by a camera, may contain spelling errors): ").Append(string.Join(", ", record.ingredients)).Append("\n\n");
            p.Append("USER: age ").Append(profile.age > 0 ? profile.age.ToString() : "unknown");
            p.Append("; skin type ").Append(Or(profile.skinType));
            p.Append("; skin tone ").Append(Or(profile.skinTone));
            p.Append("; skin conditions ").Append(profile.skinConditions != null && profile.skinConditions.Count > 0 ? string.Join(", ", profile.skinConditions) : "none");
            var realAllergens = profile.RealAllergens();
            p.Append("; known allergens ").Append(realAllergens.Count > 0 ? string.Join(", ", realAllergens) : "none").Append('.');

            return "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"" + JsonEscape(p.ToString()) + "\"}]}]," +
                   "\"generationConfig\":{\"temperature\":0.2,\"responseMimeType\":\"application/json\"}}";
        }

        static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "unknown" : s;

        static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append(' '); else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Starts the look-up in the FDA Philippines Verification Portal and returns at once (null when it cannot be done:
        /// offline, or switched off). A product the app could name is searched by name and brand; one it could not name is
        /// searched by the lines read from the label.
        /// </summary>
        static Task<PhFdaVerification.Result> StartFdaSearch(string productName, string brandName)
        {
            if (!ConnectivityMonitor.Online || !PhFdaVerification.Enabled) return null;
            try
            {
                return !string.IsNullOrWhiteSpace(productName) && productName.Trim().Length >= 3
                    ? PhFdaVerification.Find(productName, brandName, ScanSession.PendingShadeName)
                    : PhFdaVerification.FindInText(ScanSession.PendingText);
            }
            catch (Exception e) { Debug.LogWarning("FDA Philippines check skipped: " + e.Message); return null; }
        }

        /// <summary>Waits until the task has ended, but not longer than `seconds`.</summary>
        static async Task WaitFor(Task task, float seconds)
        {
            float waited = 0f;
            while (!task.IsCompleted && waited < seconds) { await Task.Yield(); waited += Time.unscaledDeltaTime; }
        }

        /// <summary>
        /// The FDA's answer arrived in time. It is kept on the scan in its own fields (the result screen shows it as a
        /// badge; it used to be a paragraph at the top of "Why this result?"), and what it says fills the gaps: name, brand, kind.
        /// </summary>
        static void UseFdaAnswer(ScanRecord record, Task<PhFdaVerification.Result> search)
        {
            if (search.Status != TaskStatus.RanToCompletion || search.Result == null) { Debug.LogWarning("FDA Philippines check skipped: the search ended with a problem."); return; }
            PhFdaVerification.Result fda = search.Result;
            ApplyFda(record, fda);
            if (!fda.found) return;
            if (string.IsNullOrWhiteSpace(record.productName)) record.productName = Title(fda.productName);
            if (string.IsNullOrWhiteSpace(record.brandName)) record.brandName = Title(fda.brandName);
            string kind = PhFdaVerification.TypeFromUse(fda.intendedUse);
            if (kind.Length > 0 && string.IsNullOrEmpty(record.productId) && (string.IsNullOrWhiteSpace(record.cosmeticType) || record.cosmeticType == "Skincare")) record.cosmeticType = kind;
        }

        /// <summary>
        /// The FDA's answer was not there when the result opened. The search runs on; when it ends, the answer is written
        /// into the scan, the saved scan is saved again (phone and cloud), and the result screen fills its badge in.
        /// </summary>
        static async void FinishFdaLater(ScanRecord record, Task<PhFdaVerification.Result> search)
        {
            PhFdaVerification.SetChecking(record.id, true);
            try
            {
                PhFdaVerification.Result fda = await search;
                if (fda != null) { ApplyFda(record, fda); ScanHistoryRepository.Update(record); }
            }
            catch (Exception e) { Debug.LogWarning("FDA Philippines check skipped: " + e.Message); }
            finally { PhFdaVerification.SetChecking(record.id, false); }
        }

        /// <summary>
        /// Writes the FDA Philippines answer into the scan. When the FDA's site did not answer at all, the scan is left as
        /// "not checked" (the badge then says the check was unavailable, not that no record exists).
        /// </summary>
        static void ApplyFda(ScanRecord record, PhFdaVerification.Result fda)
        {
            record.fdaChecked = fda.answered;
            record.fdaStatus = fda.Status;
            if (!fda.found) return;
            record.fdaNumber = fda.number; record.fdaLine = fda.Line;
            record.fdaProductName = Title(fda.productName); record.fdaCompany = fda.company; record.fdaValidUntil = fda.validUntil;
        }

        /// <summary>"MAYBELLINE NEW YORK FIT ME" -> "Maybelline New York Fit Me" (the FDA writes names in capitals).</summary>
        static string Title(string text)
        {
            string lower = (text ?? "").Trim().ToLowerInvariant();
            return lower.Length == 0 ? "" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        static void Merge(ScanRecord record, ProductAnalysis ai)
        {
            record.aiEnhanced = true;
            if (string.IsNullOrWhiteSpace(record.productName) && !string.IsNullOrWhiteSpace(ai.ProductName)) record.productName = ai.ProductName.Trim();
            if (string.IsNullOrWhiteSpace(record.brandName) && !string.IsNullOrWhiteSpace(ai.BrandName)) record.brandName = ai.BrandName.Trim();
            if (string.IsNullOrEmpty(record.productId)                                         // a recognised product keeps the type set on the website
                && (string.IsNullOrWhiteSpace(record.cosmeticType) || record.cosmeticType == "Skincare") && !string.IsNullOrWhiteSpace(ai.CosmeticType))
                record.cosmeticType = ai.CosmeticType.Trim();

            // anything the AI flags that our own check had no opinion on
            foreach (SafetyFlag flag in ai.SafetyFlags ?? new List<SafetyFlag>())
            {
                if (flag == null || string.IsNullOrWhiteSpace(flag.Ingredient)) continue;
                var finding = record.findings.FirstOrDefault(f => string.Equals(f.name, flag.Ingredient.Trim(), StringComparison.OrdinalIgnoreCase)
                                                               || string.Equals(f.techName, flag.Ingredient.Trim(), StringComparison.OrdinalIgnoreCase));
                if (finding == null || (finding.level != "unknown" && finding.level != "safe")) continue;
                string severity = (flag.Severity ?? "").ToLowerInvariant();
                if (severity != "high" && severity != "medium") continue;
                finding.level = "caution";
                finding.reason = "AI note: " + (flag.Reason ?? "may not suit your profile.").Trim();
                if (record.status == "safe") { record.status = "caution"; record.summary = "The AI flagged something to check."; }
            }

            foreach (string active in ai.KeyActives ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(active) && record.keyActives.Count < 8 && !record.keyActives.Contains(active.Trim(), StringComparer.OrdinalIgnoreCase))
                    record.keyActives.Add(active.Trim());

            foreach (HiddenIngredientAlert alert in ai.HiddenIngredientAlerts ?? new List<HiddenIngredientAlert>())
            {
                if (alert == null || string.IsNullOrWhiteSpace(alert.LabelTerm) || string.IsNullOrWhiteSpace(alert.WhyItsOpaque)) continue;
                string line = $"\"{alert.LabelTerm.Trim()}\": {alert.WhyItsOpaque.Trim()}";
                if (alert.CommonlyHiddenSubstances != null && alert.CommonlyHiddenSubstances.Count > 0)
                {
                    line += " It can contain: " + string.Join(", ", alert.CommonlyHiddenSubstances.Take(8)) + ".";
                    // remembered on the phone and sent to the shared database, so the next scan knows it without the AI (also offline)
                    try { HiddenAdditives.Learn(alert.LabelTerm, alert.CommonlyHiddenSubstances, alert.WhyItsOpaque, null, false, "second opinion"); }
                    catch (Exception e) { Debug.LogWarning("Hidden ingredients were not saved: " + e.Message); }
                }
                if (!record.hiddenAlerts.Any(h => h.IndexOf(alert.LabelTerm.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) record.hiddenAlerts.Add(line);
            }

            string insight = !string.IsNullOrWhiteSpace(ai.Explanation) ? ai.Explanation.Trim() : (ai.ProductSummary ?? "").Trim();
            if (insight.Length > 0)
            {
                string disclaimer = "\n\nThis is guidance, not a medical opinion.";
                int cut = record.explanation != null ? record.explanation.LastIndexOf(disclaimer, StringComparison.Ordinal) : -1;
                string body = cut >= 0 ? record.explanation.Substring(0, cut) : (record.explanation ?? "");
                string tail = cut >= 0 ? record.explanation.Substring(cut) : "";
                record.explanation = body + "\n\n<b>AI second opinion</b>\n" + insight + tail;
            }
        }
    }
}
