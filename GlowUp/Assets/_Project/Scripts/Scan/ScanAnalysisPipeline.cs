using System;
using System.Collections.Generic;
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
    ///   step 2  cross-check with the user's profile (done on the phone: the profile is never sent to the AI)
    ///   step 3  save to Recent Scans and hand the result to the result scene
    /// All of it works offline for ingredients the phone knows. Online it also looks up unknown ingredients and the FDA Philippines list.
    /// </summary>
    public static class ScanAnalysisPipeline
    {
        // set by LoadingSceneController from its Inspector fields
        /// <summary>The AI settings for looking up ingredients nobody has described yet (see IngredientResearcher).</summary>
        public static GeminiApiConfig AiConfig;
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
            // the reader sorts the label text into known ingredients, names it does not know, and everything that is not
            // an ingredient at all (thrown away): see IngredientTextParser
            IngredientTextParser.ParsedList label = IngredientTextParser.Read(ScanSession.PendingText);
            if (label.Count == 0)
            {
                ScanSession.LastError = "We could not find an ingredient list in that. Try again with the list filling the frame, or type the ingredients in.";
                ScanSession.RescanOf = ScanSession.PendingReplaces;     // still the same product: the next try takes the same place
                ScanSession.KeepLightChoice = true;                     // and a flashlight the user switched on comes back on
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
            // Only names that passed the reader's checks are ever shown to the AI: never raw label text.
            if (IngredientResearcher.Enabled && AiConfig != null && ConnectivityMonitor.Online && label.unrecognized.Count > 0)
            {
                try
                {
                    int learned = await IngredientResearcher.ResearchUnknown(label.unrecognized, AiConfig, message => report(1, message));
                    if (learned > 0) label = IngredientTextParser.Read(ScanSession.PendingText);      // what was just learned is known now
                }
                catch (Exception e) { Debug.LogWarning("Ingredient lookup skipped: " + e.Message); }
            }
            List<string> names = label.inOrder;         // every name on the label, known or not, in its order (for the inventory)

            report(2, "Comparing with your skin profile");
            UserProfileData profile = await UserProfileRepository.Load() ?? new UserProfileData();
            // the verdict rests on the KNOWN ingredients only; the names nobody knows are kept beside it
            ScanRecord record = ProductAnalysisEngine.Analyse(label.ingredients, ScanSession.PendingText, profile, brands, null, null, null, label.unrecognized);
            record.mayContain = new List<string>(label.mayContain);
            record.source = string.IsNullOrEmpty(ScanSession.PendingSource) ? "scan" : ScanSession.PendingSource;
            record.shadeHex = ScanSession.PendingShadeHex ?? "";

            // "SCAN THE SAME PRODUCT AGAIN": this result takes the place of the earlier one (the same id), so the product
            // is in Recent Scans once and its scan is counted once. (An earlier scan that is no longer kept: a new scan.)
            ScanRecord earlier = ScanSession.PendingReplaces;
            if (earlier != null && ScanHistoryRepository.Find(earlier.id) == null) earlier = null;
            if (earlier != null) record.id = earlier.id;

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

            // Until here the product's kind is only a guess from the label text. It is KNOWN when the inventory, the scan
            // screen (the AI, a barcode, the user's own choice) or the FDA's record says what the product is.
            bool kindIsKnown = !string.IsNullOrEmpty(record.productId) && !string.IsNullOrWhiteSpace(record.cosmeticType);
            if (ApplyRecognised(record, profile)) kindIsKnown = true;
            if (!string.IsNullOrEmpty(record.fdaStatus)) fdaSearch = null;                        // the inventory had the FDA record
            else if (fdaSearch == null) fdaSearch = StartFdaSearch(record.productName, record.brandName);

            // MAKE-UP ONLY: a product that is clearly something else is not analysed, saved, counted or added to the inventory
            if (StopIfNotMakeup(record, kindIsKnown)) return;

            // the FDA Philippines answer: taken now when it has arrived. Otherwise the result waits a few seconds for it and then
            // opens without it: its badge says "checking" and fills in by itself when the answer comes (FinishFdaLater).
            if (fdaSearch != null)
            {
                if (!fdaSearch.IsCompleted) { report(2, "Checking the FDA Philippines list"); await WaitFor(fdaSearch, FdaExtraWaitSeconds); }
                if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");      // the late answer finds the saved scan by its id
                if (fdaSearch.IsCompleted)
                {
                    if (UseFdaAnswer(record, fdaSearch, kindIsKnown)) kindIsKnown = true;
                    if (StopIfNotMakeup(record, kindIsKnown)) return;                              // the FDA's record says what it is
                }
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
                try { ScannedProductPublisher.PublishIfNew(record, names, kindIsKnown); }
                catch (Exception e) { Debug.LogWarning("Adding the product to the inventory was skipped: " + e.Message); }
            }
            // every scan is saved to Recent Scans automatically; a scan that was done again takes the earlier one's place
            bool replaced = earlier != null && ScanHistoryRepository.Replace(earlier.id, record);
            if (!replaced) ScanHistoryRepository.Add(record);
            // the scan counters on the admin website: ONCE per scan. A scan that was done again only counts for a brand
            // or a product the earlier attempt did not count (it was recognised differently this time).
            if (!string.IsNullOrEmpty(record.brandId) && !(replaced && earlier.brandId == record.brandId)) BrandRepository.RecordScan(record.brandId);
            if (!string.IsNullOrEmpty(record.productId) && !(replaced && earlier.productId == record.productId)) ProductRepository.RecordScan(record.productId);
            // the anonymous note that feeds the admin dashboard: one per scan. For a scan that was done again, a note that
            // is still waiting to be sent gets the new result; one that was sent already stays as it is (the database's
            // rules let the app add a note, not change one), so the dashboard never counts the scan twice.
            if (!replaced) SyncQueue.EnqueueSet("scanActivity/" + record.id, ScanActivity.From(record));
            else SyncQueue.ReplacePendingSet("scanActivity/" + record.id, ScanActivity.From(record));
            ScanSession.Current = record;
            ScanSession.ResultBackScene = SceneNames.Dashboard;
            ScanSession.ClearPending();
        }

        /// <summary>
        /// The scan is stopped when the product is clearly not make-up (its name, the AI, the inventory, the user or the
        /// FDA's record says so). The user is sent back to the scan screen with the reason; nothing is saved or counted.
        /// A kind that was only guessed from the label text never stops a scan.
        /// </summary>
        static bool StopIfNotMakeup(ScanRecord record, bool kindIsKnown)
        {
            ProductScope.Verdict scope = ProductScope.Judge(kindIsKnown ? record.cosmeticType : "", record.productName);
            if (!scope.IsNotMakeup) return false;
            ScanSession.LastError = ProductScope.StopMessage(scope);
            ScanSession.KeepLightChoice = true;
            ScanSession.ClearPending();
            SceneNavigator.Retarget(SceneNames.AiSmartAnalysis);
            return true;
        }

        /// <summary>
        /// What phase 1 of the scan recognised (name, brand, kind, shade, texture) and where a looked-up ingredient list came
        /// from. Returns true when the scan screen said what KIND of product it is (that is more than a guess).
        /// </summary>
        static bool ApplyRecognised(ScanRecord record, UserProfileData profile)
        {
            bool kindGiven = false;
            if (string.IsNullOrEmpty(record.productId))               // a product from the inventory keeps the inventory's details
            {
                if (string.IsNullOrWhiteSpace(record.productName) && !string.IsNullOrWhiteSpace(ScanSession.PendingProductName)) record.productName = ScanSession.PendingProductName.Trim();
                if (string.IsNullOrWhiteSpace(record.brandName) && !string.IsNullOrWhiteSpace(ScanSession.PendingBrandName)) record.brandName = ScanSession.PendingBrandName.Trim();
                if (!string.IsNullOrWhiteSpace(ScanSession.PendingCosmeticType))
                {
                    record.cosmeticType = ScanSession.PendingCosmeticType.Trim();      // what was recognised replaces the guess
                    record.recommendedShades = ShadeAdvisor.Recommend(profile.skinTone, ShadeAdvisor.AreaFor(record.cosmeticType));
                    kindGiven = true;
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
            return kindGiven;
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
        /// <returns>true when the FDA's record gave the product its kind</returns>
        static bool UseFdaAnswer(ScanRecord record, Task<PhFdaVerification.Result> search, bool kindIsKnown)
        {
            if (search.Status != TaskStatus.RanToCompletion || search.Result == null) { Debug.LogWarning("FDA Philippines check skipped: the search ended with a problem."); return false; }
            PhFdaVerification.Result fda = search.Result;
            ApplyFda(record, fda);
            if (!fda.found) return false;
            if (string.IsNullOrWhiteSpace(record.productName)) record.productName = Title(fda.productName);
            if (string.IsNullOrWhiteSpace(record.brandName)) record.brandName = Title(fda.brandName);
            // the kind is taken from the FDA's record only when that record is clearly this product, and nobody has said the kind yet
            string kind = PhFdaVerification.TypeFromUse(fda.intendedUse);
            if (kind.Length == 0 || kindIsKnown || !fda.closeMatch || !string.IsNullOrEmpty(record.productId)) return false;
            record.cosmeticType = kind;
            return true;
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
                if (fda == null) return;
                ApplyFda(record, fda);
                // saved again, unless the scan was done again in the meantime (its place now holds the newer result)
                ScanRecord kept = ScanHistoryRepository.Find(record.id);
                if (kept == record || (kept != null && kept.scannedAtUnix == record.scannedAtUnix)) ScanHistoryRepository.Update(record);
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
    }
}
