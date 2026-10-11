using System;
using System.Collections;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Scanning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The loading scene: tells the user to wait while their product is scanned and analysed.
    /// It runs the work it was handed (see SceneNavigator.GoViaLoading), ticks the steps off one by
    /// one, then opens the result scene. Texts, timings and the AI settings are Inspector fields.
    /// </summary>
    public class LoadingSceneController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI tipText;
        [SerializeField] private Image progressFill;
        [SerializeField] private RectTransform spinner;
        [Tooltip("One row per step, in order. Each ticks when its step is done.")]
        [SerializeField] private RequirementIndicatorItem[] steps;
        [SerializeField] private Button cancelButton;

        [Header("Behaviour")]
        [SerializeField] private string defaultMessage = "Scanning and analysing your product";
        [Tooltip("Opened when there is nowhere else to go, and by the Cancel button")]
        [SerializeField] private string fallbackScene = SceneNames.Dashboard;
        [SerializeField] private string cancelScene = SceneNames.AiSmartAnalysis;
        [Tooltip("The loading screen is shown at least this long, so it never just flashes by")]
        [SerializeField] private float minimumShowSeconds = 2.2f;
        [SerializeField] private float spinnerDegreesPerSecond = 220f;
        [SerializeField] private float progressSmoothing = 4f;
        [SerializeField] private string[] tips =
        {
            "Tip: patch-test any new product on a small area first.",
            "Tip: \"Fragrance\" on a label can hide many ingredients.",
            "Tip: ingredients are listed from most to least."
        };
        [SerializeField] private float tipSwapSeconds = 2.5f;
        [SerializeField] private string failedMessage = "Something went wrong while analysing. Please try again.";

        [Header("Recognising products from the website's inventory")]
        [Tooltip("Compare every scan with the products listed on the admin website. A match gives the scan its real name, brand and shades, and counts as an App Scan for that product.")]
        [SerializeField] private bool recogniseCatalogueProducts = true;
        [Tooltip("At least this many ingredients must be the same")]
        [SerializeField] private int minimumSharedIngredients = 4;
        [Tooltip("At least this share of the listed product's ingredients must be found in the scan")]
        [Range(0.3f, 1f)] [SerializeField] private float minimumShareOfProduct = 0.7f;
        [Tooltip("At least this share of the scanned ingredients must belong to the listed product")]
        [Range(0.3f, 1f)] [SerializeField] private float minimumShareOfScan = 0.6f;

        [Header("Keeping the ingredient list up to date")]
        [Tooltip("While online, the ingredient list is downloaded from the website's database again after this many minutes. Offline, the copy saved on the phone is used.")]
        [SerializeField] private float ingredientRefreshMinutes = 10f;

        [Header("Looking up unknown ingredients (online only)")]
        [Tooltip("When a scan has ingredients that are in neither the database nor the app's built-in list, ask the AI about them and add what it finds to the database, marked 'AI, not reviewed'.")]
        [SerializeField] private bool lookUpUnknownIngredients = true;
        [Tooltip("At most this many ingredients are looked up per scan")]
        [Range(1, 15)] [SerializeField] private int maximumLookupsPerScan = 6;
        [SerializeField] private float lookupTimeoutSeconds = 25f;
        [Tooltip("Let the AI search the web and report the pages it used. If the AI key does not offer web search, the app asks without it.")]
        [SerializeField] private bool lookupSearchesTheWeb = true;
        [Tooltip("Also check each new ingredient name against the US FDA's public substance registry")]
        [SerializeField] private bool lookupChecksFdaRegistry = true;
        [Tooltip("Where the app is used. The AI gives this country's rules priority.")]
        [SerializeField] private string lookupRegion = "the Philippines";
        [Tooltip("Sources the AI is told to prefer, most trusted first")]
        [SerializeField] private string[] lookupPreferredSources =
        {
            "fda.gov (US Food and Drug Administration)",
            "fda.gov.ph (FDA Philippines) and the ASEAN Cosmetic Directive ingredient annexes",
            "ec.europa.eu CosIng (EU cosmetic ingredient database)",
            "cir-safety.org (Cosmetic Ingredient Review)",
            "pubchem.ncbi.nlm.nih.gov (PubChem)"
        };

        [Header("Adding scanned products to the website's inventory (online only)")]
        [Tooltip("A scanned product that is not in the Brand & Product Inventory is added to it as 'Pending' for an admin to review.")]
        [SerializeField] private bool addScannedProductsToInventory = true;
        [Tooltip("On: only products whose name is known (from the label or the AI) are added. Off: nameless scans are added too.")]
        [SerializeField] private bool onlyAddProductsWithAName = true;

        [Header("FDA Philippines check (online only)")]
        [Tooltip("Ask the FDA Philippines list of notified cosmetics whether the scanned product is registered")]
        [SerializeField] private bool checkFdaPhilippines = true;
        [Tooltip("One search of the FDA's site takes 12 to 15 seconds. A search without an answer after this many seconds is given up (the badge then says the check was unavailable).")]
        [SerializeField] private float fdaSearchTimeoutSeconds = 25f;
        [Tooltip("The search runs beside the other steps. When they are done, the result waits at most this many more seconds for it; after that the result opens and its FDA badge fills in by itself when the answer arrives.")]
        [SerializeField] private float fdaExtraWaitSeconds = 4f;

        [Header("AI second opinion (used only when online)")]
        [SerializeField] private bool useAiWhenOnline = true;
        [SerializeField] private GeminiApiConfig aiConfig;
        [Tooltip("If the AI has not answered after this many seconds, the result is shown without it")]
        [SerializeField] private float aiTimeoutSeconds = 20f;

        float targetProgress, shownProgress;
        int currentStep = -1;
        bool cancelled;

        void Awake()
        {
            ScanAnalysisPipeline.UseAi = useAiWhenOnline;
            ScanAnalysisPipeline.AiConfig = aiConfig;
            ScanAnalysisPipeline.AiTimeoutSeconds = aiTimeoutSeconds;
            ScanAnalysisPipeline.RecogniseCatalogueProducts = recogniseCatalogueProducts;
            ProductMatcher.MinimumShared = minimumSharedIngredients;
            ProductMatcher.MinimumShareOfProduct = minimumShareOfProduct;
            ProductMatcher.MinimumShareOfScan = minimumShareOfScan;
            IngredientRepository.RefreshMinutes = ingredientRefreshMinutes;
            IngredientResearcher.Enabled = lookUpUnknownIngredients;
            IngredientResearcher.MaximumPerScan = maximumLookupsPerScan;
            IngredientResearcher.TimeoutSeconds = lookupTimeoutSeconds;
            IngredientResearcher.SearchTheWeb = lookupSearchesTheWeb;
            IngredientResearcher.CheckFdaRegistry = lookupChecksFdaRegistry;
            if (!string.IsNullOrWhiteSpace(lookupRegion)) IngredientResearcher.Region = lookupRegion.Trim();
            if (lookupPreferredSources != null && lookupPreferredSources.Length > 0) IngredientResearcher.PreferredSources = lookupPreferredSources;
            ScannedProductPublisher.Enabled = addScannedProductsToInventory;
            ScannedProductPublisher.RequireProductName = onlyAddProductsWithAName;
            PhFdaVerification.Enabled = checkFdaPhilippines;
            PhFdaVerification.TimeoutSeconds = Mathf.Max(5f, fdaSearchTimeoutSeconds);
            ScanAnalysisPipeline.FdaExtraWaitSeconds = Mathf.Max(0f, fdaExtraWaitSeconds);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        void Start()
        {
            if (messageText != null)
                messageText.text = string.IsNullOrEmpty(SceneNavigator.PendingMessage) ? defaultMessage : SceneNavigator.PendingMessage;
            if (tipText != null && tips.Length > 0) tipText.text = tips[0];
            if (progressFill != null) progressFill.fillAmount = 0f;
            StartCoroutine(Run());
            if (tips.Length > 1) StartCoroutine(SwapTips());
        }

        void Update()
        {
            if (spinner != null) spinner.Rotate(0, 0, -spinnerDegreesPerSecond * Time.unscaledDeltaTime);
            shownProgress = Mathf.Lerp(shownProgress, targetProgress, 1f - Mathf.Exp(-progressSmoothing * Time.unscaledDeltaTime));
            if (progressFill != null) progressFill.fillAmount = shownProgress;
        }

        IEnumerator SwapTips()
        {
            int i = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(tipSwapSeconds);
                i = (i + 1) % tips.Length;
                if (tipText != null) tipText.text = tips[i];
            }
        }

        IEnumerator Run()
        {
            float started = Time.unscaledTime;
            var work = SceneNavigator.PendingWork;
            bool failed = false;

            if (work != null)
            {
                Task task = null;
                try { task = work(Report); }
                catch (Exception e) { Debug.LogError("Loading work failed to start: " + e); failed = true; }
                if (task != null)
                {
                    while (!task.IsCompleted) yield return null;
                    if (task.IsFaulted) { Debug.LogError("Loading work failed: " + task.Exception); failed = true; }
                }
            }
            else Report(steps != null ? steps.Length - 1 : 0, null);

            if (cancelled) yield break;

            if (failed)
            {
                if (messageText != null) messageText.text = failedMessage;
                ScanSession.LastError = failedMessage;
                yield return new WaitForSecondsRealtime(2f);
                Leave(cancelScene);
                yield break;
            }

            // tick the last step and let the bar fill before leaving
            Report(steps != null ? steps.Length : 1, null);
            float wait = Mathf.Max(0.5f, minimumShowSeconds - (Time.unscaledTime - started));
            yield return new WaitForSecondsRealtime(wait);
            if (!cancelled) Leave(string.IsNullOrEmpty(SceneNavigator.PendingTarget) ? fallbackScene : SceneNavigator.PendingTarget);
        }

        /// <summary>Called by the work: step `index` has started (so every earlier step is done).</summary>
        void Report(int index, string message)
        {
            int count = steps != null ? steps.Length : 0;
            if (index > currentStep)
            {
                currentStep = index;
                for (int i = 0; i < count; i++) if (steps[i] != null) steps[i].SetMet(i < index);
                targetProgress = count > 0 ? Mathf.Clamp01((index + 0.5f) / count) : 1f;
                if (index >= count) targetProgress = 1f;
            }
            if (!string.IsNullOrEmpty(message) && messageText != null) messageText.text = message;
        }

        void Cancel()
        {
            cancelled = true;
            StopAllCoroutines();
            Leave(cancelScene);
        }

        void Leave(string scene)
        {
            SceneNavigator.ClearPending();
            SceneNavigator.Go(string.IsNullOrEmpty(scene) ? fallbackScene : scene);
        }
    }
}
