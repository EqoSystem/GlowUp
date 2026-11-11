using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// What is being scanned / shown right now. It is how the scan, loading, result,
    /// recent-scans and AR scenes hand a product to each other.
    /// </summary>
    public static class ScanSession
    {
        /// <summary>The ingredient text waiting to be analysed (read by the camera or typed in).</summary>
        public static string PendingText;
        /// <summary>"scan" (camera) or "manual" (typed).</summary>
        public static string PendingSource = "scan";
        /// <summary>The main colour seen in the scanned photo, if any (hex).</summary>
        public static string PendingShadeHex;

        // ---- what phase 1 of the scan recognised about the product (all "" when it did not) ----
        public static string PendingProductName = "", PendingBrandName = "", PendingCosmeticType = "";
        public static string PendingProductId = "";
        public static string PendingOfficialIngredientSource = "";
        /// <summary>The shade's name or number printed on the product, and its texture (matte, satin, glossy, shimmer, metallic).</summary>
        public static string PendingShadeName = "", PendingFinish = "";
        /// <summary>The number under the product's barcode, when the camera saw it ("" = not seen).</summary>
        public static string PendingBarcode = "";
        /// <summary>The pages the ingredient list came from when it was looked up instead of read from the label.</summary>
        public static System.Collections.Generic.List<string> PendingSources = new System.Collections.Generic.List<string>();
        /// <summary>Where the ingredient list came from: "pack" (read by the camera), "inventory", "web" (looked up online) or "typed". "" = worked out from PendingSource.</summary>
        public static string PendingListFrom = "";
        /// <summary>A list that was looked up online: true when it came with pages a live web search really opened.</summary>
        public static bool PendingListWebBacked;

        /// <summary>
        /// The saved scan the one waiting to be analysed takes the place of ("Scan the same product again"). The new result
        /// keeps its place in Recent Scans, and the scan counters are not added to a second time. Null = a new scan.
        /// </summary>
        public static ScanRecord PendingReplaces;
        /// <summary>
        /// True = it is the same product scanned again (the result keeps the earlier scan's id, name and count).
        /// False = "Scan a different product": the user changed their mind, so the earlier scan leaves Recent Scans and
        /// the new product's scan is saved as a scan of its own (the earlier one can still be brought back there).
        /// </summary>
        public static bool PendingReplacesSame = true;

        /// <summary>Forgets what is waiting to be analysed.</summary>
        public static void ClearPending()
        {
            PendingText = null; PendingShadeHex = null; PendingSource = "scan";
            PendingProductName = PendingBrandName = PendingCosmeticType = PendingProductId = PendingOfficialIngredientSource = PendingShadeName = PendingFinish = PendingBarcode = "";
            PendingSources = new System.Collections.Generic.List<string>();
            PendingListFrom = ""; PendingListWebBacked = false;
            PendingReplaces = null; PendingReplacesSame = true;
        }

        /// <summary>
        /// "Scan again" was chosen on the result screen: the saved scan the new one takes the place of. The scan screen
        /// takes it over when it opens and empties this. For the same product the name is kept and the pictures are taken anew.
        /// </summary>
        public static ScanRecord RescanOf;
        /// <summary>With RescanOf: true = "Scan the same product again", false = "Scan a different product" (it replaces that scan too).</summary>
        public static bool RescanSameProduct = true;

        /// <summary>The product the result scene shows.</summary>
        public static ScanRecord Current;
        /// <summary>
        /// What the web search of a product photo found (11 November 2026). Set by the photo look-up just before the scan finishes;
        /// the scan screen then opens the Web Search Result screen instead of the usual result. Null = no web search in this scan.
        /// </summary>
        public static WebSearchResult WebResult;
        /// <summary>Where the result scene's Back button goes (dashboard after a new scan, recent scans when opened from the list).</summary>
        public static string ResultBackScene;

        /// <summary>The product chosen for AR Try-On. Null = let the user pick one from their saved scans.</summary>
        public static ScanRecord TryOn;

        /// <summary>The flashlight on the scan screen: what the user chose with its button in the last visit (0 = nothing, 1 = on, -1 = off).</summary>
        public static int LightChoice;
        /// <summary>True when the next visit of the scan screen is the same scan done again: the user's flashlight choice is kept for it.</summary>
        public static bool KeepLightChoice;

        /// <summary>A message for the scan scene to show when an analysis could not be done.</summary>
        public static string LastError;

        /// <summary>Ingredient Safety Alerts for the scan that was just analysed (the triggers the user switched on in Settings).</summary>
        public static System.Collections.Generic.List<SafetyAlert> Alerts = new System.Collections.Generic.List<SafetyAlert>();
        /// <summary>True until the result scene has shown those alerts once (so reopening an old scan does not alert again).</summary>
        public static bool AlertPending;
    }
}
