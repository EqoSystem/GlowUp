using System;
using System.Collections.Generic;
using System.Linq;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Result
{
    /// <summary>
    /// AI Smart Analysis Result: shows one analysed product - its verdict (status pill and one line), what the
    /// FDA Philippines list says about it, the compatibility matrix, key-ingredient chips with where the ingredient
    /// list came from (the pages as buttons), "Why this result?" (the long text, behind a button), shades that suit the
    /// user, the advice to see a dermatologist, and a list of alternative products.
    /// Each fact has ONE place on the screen: nothing is said twice.
    /// The same scene shows a new scan and a product opened from Recent Scans.
    /// </summary>
    public class ScanResultFlowController : MonoBehaviour
    {
        [Header("Product")]
        [SerializeField] private TextMeshProUGUI brandText;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI typeText;
        [SerializeField] private TextMeshProUGUI dateText;
        [SerializeField] private Image typeIcon;
        [SerializeField] private StatusPillView status;
        [SerializeField] private TextMeshProUGUI summaryText;
        [SerializeField] private CosmeticTypeIcons typeIcons;
        [SerializeField] private string unknownBrand = "Brand not recognised";
        [Tooltip("{0} = scan date and time")]
        [SerializeField] private string dateFormat = "Scanned {0}";

        [Header("Compatibility matrix")]
        [SerializeField] private CompatibilityIndicator skinType;
        [SerializeField] private CompatibilityIndicator allergens;
        [SerializeField] private CompatibilityIndicator concerns;
        [SerializeField] private CompatibilityIndicator age;

        [Header("Key ingredient chips")]
        [SerializeField] private RectTransform chipContent;
        [Tooltip("The pattern chip. Hidden when the app runs and copied once per highlighted ingredient.")]
        [SerializeField] private IngredientChipView chipTemplate;
        [SerializeField] private int maximumChips = 8;
        [SerializeField] private Button allIngredientsButton;
        [SerializeField] private TextMeshProUGUI allIngredientsLabel;
        [Tooltip("{0} = number of ingredients")]
        [SerializeField] private string allIngredientsFormat = "See all {0} ingredients";
        [SerializeField] private IngredientSheet ingredientSheet;

        [Header("FDA Philippines, and where the ingredient list came from")]
        [Tooltip("The badge that says what the FDA Philippines list answered (in the product card)")]
        [SerializeField] private FdaBadgeView fdaBadge;
        [Tooltip("One short line under the ingredient chips: where the list came from")]
        [SerializeField] private TextMeshProUGUI listSourceText;
        [Tooltip("The row the page buttons are put in")]
        [SerializeField] private RectTransform sourceLinksContent;
        [Tooltip("The pattern button. Hidden when the app runs and copied once per page the list came from.")]
        [SerializeField] private SourceLinkButton sourceLinkTemplate;
        [Tooltip("At most this many page buttons")]
        [SerializeField] private int maximumSourceLinks = 2;
        [SerializeField] private string listFromPack = "Ingredient list read from your pack.";
        [SerializeField] private string listFromInventory = "Ingredient list from the GlowUp product list.";
        [SerializeField] private string listFromWeb = "Ingredient list looked up online. Check it against your pack when you can.";
        [Tooltip("Looked up by the AI without a live web search: the page is one the AI named, not one that was opened")]
        [SerializeField] private string listFromWebUnchecked = "Ingredient list looked up by the AI without a live web search (not web-backed). Check it against your pack.";
        [SerializeField] private string listTyped = "Ingredient list typed in by you.";
        [Tooltip("The list itself came from Open Beauty Facts (no better place had it)")]
        [SerializeField] private string listFromOpenBeautyFacts = "Ingredient list from Open Beauty Facts, a public list that anybody can edit.";
        [Tooltip("Added to that line when the list was not read from the user's own pack")]
        [SerializeField] private string listMayDiffer = "It may differ from the product in your hand after a reformulation, or in another country.";
        [Tooltip("Added when the day the list was looked up is known. {0} = the day")]
        [SerializeField] private string listLookedUpOn = "Looked up {0}.";

        [Header("A second place that has the list (\"Matched 2 sources\")")]
        [Tooltip("The line under \"where the list came from\": hidden when there was nothing to compare the list with")]
        [SerializeField] private TextMeshProUGUI listCheckText;
        [Tooltip("{0} = where the shown list came from, {1} = the second place")]
        [SerializeField] private string listMatched = "Matched 2 sources: {0} and {1}.";
        [Tooltip("{0} = where the shown list came from, {1} = the second place, {2} = how many names differ, {3} = what differs")]
        [SerializeField] private string listDiffers = "{1} lists this product differently ({2} ingredients differ{3}). The list from {0} is shown.";
        [Tooltip("{0} = names only the shown list has")]
        [SerializeField] private string listOnlyHereFormat = "; only here: {0}";
        [Tooltip("{0} = names only the second place has")]
        [SerializeField] private string listOnlyThereFormat = "; only there: {0}";
        [Tooltip("Added for Open Beauty Facts, which anybody can edit. {0} = the day of its last edit")]
        [SerializeField] private string listSecondEdited = "That entry was last edited {0}.";
        [SerializeField] private string nameOfPack = "your pack";
        [SerializeField] private string nameOfInventory = "the GlowUp product list";
        [SerializeField] private string nameOfWeb = "the page it was looked up on";
        [SerializeField] private string nameOfTyped = "what you typed";
        [SerializeField] private string nameOfOpenBeautyFacts = "Open Beauty Facts";

        [Header("Why this result?")]
        [Tooltip("The long text. It stays behind the \"Why this result?\" button (the ExpandableSection on WhyCard opens it)")]
        [SerializeField] private TextMeshProUGUI explanationText;

        [Header("Shades that suit the user")]
        [SerializeField] private GameObject shadeCard;
        [SerializeField] private Image[] shadeSwatches;
        [SerializeField] private TextMeshProUGUI shadeCaption;
        [Tooltip("{0} = the user's skin tone, {1} = the area (lips, cheeks ...)")]
        [SerializeField] private string shadeCaptionFormat = "Picked for {0} skin, for the {1}";
        [SerializeField] private string shadeCaptionNoTone = "Shades for the {1}. Save your skin tone for a closer match.";

        [Header("See a specialist")]
        [SerializeField] private GameObject dermatologistCard;
        [SerializeField] private TextMeshProUGUI dermatologistText;
        [SerializeField] private Button dermatologistButton;
        [SerializeField] private string dermatologistUrl = "https://www.google.com/maps/search/dermatologist+near+me";
        [Tooltip("The advice in this last card. Every result ends with the dermatologist line here (it is no longer repeated inside \"Why this result?\")")]
        [TextArea] [SerializeField] private string flaggedAdvice = "This product has ingredients that may not suit you. " + ProductScope.DermatologistLine;
        [TextArea] [SerializeField] private string standardAdvice = ProductScope.DermatologistLine;

        [Header("Alternatives (button at the top)")]
        [SerializeField] private Button alternativesButton;
        [SerializeField] private OverlayPanel alternativesPanel;
        [SerializeField] private RectTransform alternativesContent;
        [Tooltip("The pattern tile. Hidden when the app runs and copied once per alternative.")]
        [SerializeField] private ProductTileView alternativeTileTemplate;
        [Tooltip("The pop-up the tiles' AI button opens")]
        [SerializeField] private AiInsightPanel aiPanel;
        [SerializeField] private string savedLabel = "Saved";
        [SerializeField] private TextMeshProUGUI alternativesEmptyText;
        [Tooltip("Shown when no product passes the check. The app never falls back to showing a product that did not pass.")]
        [SerializeField] private string msgNoAlternatives = "No make-up products matched your profile yet. Scan another product to check it.";
        [SerializeField] private string msgLoadingAlternatives = "Looking for make-up that matches your profile...";
        [SerializeField] private int maximumAlternatives = 10;

        [Header("Rename (this device only)")]
        [SerializeField] private Button renameButton;
        [SerializeField] private OverlayPanel renamePanel;
        [SerializeField] private TMP_InputField renameInput;
        [SerializeField] private Button renameSaveButton;

        [Header("Buttons / scenes")]
        [SerializeField] private Button backButton;
        [Tooltip("\"Scan again\": asks whether it is the same product or a different one")]
        [SerializeField] private Button scanAnotherButton;
        [Tooltip("The question that button opens. Empty = the scanner opens at once, for a new product.")]
        [SerializeField] private OverlayPanel scanAgainPanel;
        [Tooltip("\"Scan the same product again\": the product keeps its name, and the new result takes the place of this one")]
        [SerializeField] private Button sameProductButton;
        [Tooltip("\"Scan a different product\": a new scan; this result stays in Recent Scans")]
        [SerializeField] private Button differentProductButton;
        [SerializeField] private Button tryOnButton;
        [SerializeField] private string scanScene = SceneNames.AiSmartAnalysis;
        [SerializeField] private string tryOnScene = SceneNames.ArTryOn;
        [SerializeField] private string defaultBackScene = SceneNames.Dashboard;
        [SerializeField] private string msgNothingToShow = "No product to show yet. Scan a product to see its result here.";

        ScanRecord record;
        UserProfileData profile;
        readonly List<GameObject> chips = new List<GameObject>();
        readonly List<GameObject> alternativeRows = new List<GameObject>();
        readonly List<GameObject> sourceLinks = new List<GameObject>();

        void Awake()
        {
            if (chipTemplate != null) chipTemplate.gameObject.SetActive(false);
            if (alternativeTileTemplate != null) alternativeTileTemplate.gameObject.SetActive(false);
            if (sourceLinkTemplate != null) sourceLinkTemplate.gameObject.SetActive(false);
            if (backButton != null) backButton.onClick.AddListener(() => SceneNavigator.Go(string.IsNullOrEmpty(ScanSession.ResultBackScene) ? defaultBackScene : ScanSession.ResultBackScene));
            if (scanAnotherButton != null) scanAnotherButton.onClick.AddListener(() => { if (scanAgainPanel != null && record != null) scanAgainPanel.Show(); else StartNewScan(); });
            if (sameProductButton != null) sameProductButton.onClick.AddListener(() => ScanAgain(true));
            if (differentProductButton != null) differentProductButton.onClick.AddListener(() => ScanAgain(false));
            if (tryOnButton != null) tryOnButton.onClick.AddListener(() => { ScanSession.TryOn = record; SceneNavigator.Go(tryOnScene); });
            if (allIngredientsButton != null) allIngredientsButton.onClick.AddListener(() => { if (ingredientSheet != null && record != null) ingredientSheet.Show(record); });
            if (dermatologistButton != null) dermatologistButton.onClick.AddListener(() => Application.OpenURL(dermatologistUrl));
            if (alternativesButton != null) alternativesButton.onClick.AddListener(ShowAlternatives);
            if (renameButton != null) renameButton.onClick.AddListener(BeginRename);
            if (renameSaveButton != null) renameSaveButton.onClick.AddListener(SaveRename);
        }

        /// <summary>
        /// Opens the scanner again, and the new result takes the place of this one either way (the user's decision of
        /// 12 October 2026). `sameProduct`: the product keeps its name, there is no second entry in Recent Scans and the
        /// scan is not counted twice. A different product: the user changed their mind about this scan, so it leaves
        /// Recent Scans when the new product's result is saved (it can still be brought back there).
        /// </summary>
        void ScanAgain(bool sameProduct)
        {
            ScanSession.RescanOf = record;
            ScanSession.RescanSameProduct = sameProduct;
            ScanSession.KeepLightChoice = true;             // a flashlight the user switched on comes back on
            SceneNavigator.Go(scanScene);
        }

        /// <summary>A scan that replaces nothing (a scene without the "Scan again" question, or no saved result on the screen).</summary>
        void StartNewScan()
        {
            ScanSession.RescanOf = null;
            ScanSession.RescanSameProduct = true;
            ScanSession.KeepLightChoice = true;
            SceneNavigator.Go(scanScene);
        }

        // the FDA Philippines search can end a few seconds after this screen opened: the badge then fills in by itself
        void OnEnable() { PhFdaVerification.CheckEnded += OnFdaCheckEnded; if (fdaBadge != null) fdaBadge.SimilarAnswered += OnFdaSimilarAnswered; }
        void OnDisable() { PhFdaVerification.CheckEnded -= OnFdaCheckEnded; if (fdaBadge != null) fdaBadge.SimilarAnswered -= OnFdaSimilarAnswered; }

        /// <summary>The FDA has a SIMILAR record and the user said whether it is their product: the scan is saved with that answer.</summary>
        void OnFdaSimilarAnswered(bool isMyProduct)
        {
            if (record == null || !PhFdaVerification.ConfirmSimilar(record, isMyProduct)) return;
            try { ScanHistoryRepository.Update(record); } catch (Exception e) { Debug.LogWarning("Result: " + e.Message); }
            ShowProof();
        }
        void OnFdaCheckEnded(string scanId) { if (record != null && record.id == scanId) ShowProof(); }

        async void Start()
        {
            record = ScanSession.Current ?? ScanHistoryRepository.Active().FirstOrDefault();     // nothing handed over: show the latest saved scan
            Show();
            TextSkeleton.Waiting(this);                 // the words are bars until the profile is here (skeleton loading)
            try { profile = await UserProfileRepository.Load(); } catch (Exception e) { Debug.LogWarning("Result: " + e.Message); }
            TextSkeleton.Ready(this);
            if (this == null) return;
            if (record == null) { record = ScanHistoryRepository.Active().FirstOrDefault(); Show(); }
            else ShowShades();
        }

        // ---------------------------------------------------------------------------------------
        void Show()
        {
            bool has = record != null;
            if (tryOnButton != null) tryOnButton.interactable = has && ProductScope.CanTryOn(record);      // AR Try-On is for make-up only
            if (allIngredientsButton != null) allIngredientsButton.interactable = has;
            if (renameButton != null) renameButton.gameObject.SetActive(has);
            if (!has)
            {
                Set(nameText, "No result yet"); Set(brandText, ""); Set(typeText, ""); Set(dateText, "");
                Set(summaryText, msgNothingToShow); Set(explanationText, msgNothingToShow);
                ShowProof();
                if (shadeCard != null) shadeCard.SetActive(false);
                if (dermatologistCard != null) dermatologistCard.SetActive(false);
                return;
            }

            Set(brandText, string.IsNullOrWhiteSpace(record.brandName) ? unknownBrand : record.brandName);
            Set(nameText, record.Title);
            Set(typeText, string.IsNullOrWhiteSpace(record.cosmeticType) ? "Cosmetic" : record.cosmeticType);
            Set(dateText, string.Format(dateFormat, ScanHistoryRepository.FormatTime(record.scannedAtUnix)));
            if (typeIcon != null && typeIcons != null) { var s = typeIcons.For(record.cosmeticType); if (s != null) typeIcon.sprite = s; }
            if (status != null) status.Bind(record.status);
            Set(summaryText, record.summary);
            Set(explanationText, ExplanationShown());

            if (skinType != null) skinType.Bind(record.skinTypeMatch);
            if (allergens != null) allergens.Bind(record.allergenMatch);
            if (concerns != null) concerns.Bind(record.concernMatch);
            if (age != null) age.Bind(record.ageMatch);

            ShowChips();
            ShowProof();
            ShowShades();

            if (dermatologistCard != null) dermatologistCard.SetActive(true);
            Set(dermatologistText, record.dermatologistAdvised ? flaggedAdvice : standardAdvice);
        }

        static void Set(TextMeshProUGUI label, string value) { if (label != null) label.text = value ?? ""; }

        void ShowChips()
        {
            foreach (var c in chips) if (c != null) Destroy(c);
            chips.Clear();
            var findings = record.findings ?? new List<IngredientFinding>();
            if (allIngredientsLabel != null) allIngredientsLabel.text = string.Format(allIngredientsFormat, findings.Count);
            if (chipTemplate == null || chipContent == null) return;

            // flagged ingredients first, then the key actives
            var highlighted = findings.Where(f => f.level == "harmful").Concat(findings.Where(f => f.level == "caution"))
                                      .Concat(findings.Where(f => f.keyActive && f.level == "safe")).Take(maximumChips).ToList();
            if (highlighted.Count == 0) highlighted = findings.Take(Mathf.Min(maximumChips, 4)).ToList();

            foreach (IngredientFinding f in highlighted)
            {
                IngredientChipView chip = Instantiate(chipTemplate, chipContent);
                chip.gameObject.name = "Chip_" + f.name;
                chip.gameObject.SetActive(true);
                chip.Bind(f.name, f.level);
                chip.Clicked += name => { if (ingredientSheet != null) ingredientSheet.Show(record, name); };
                chips.Add(chip.gameObject);
            }
        }

        // ---------------------------------------------------------------------------------------
        //  The FDA Philippines badge, and where the ingredient list came from
        // ---------------------------------------------------------------------------------------
        void ShowProof()
        {
            foreach (var link in sourceLinks) if (link != null) Destroy(link);
            sourceLinks.Clear();
            bool has = record != null;

            // a product that was recommended, not scanned, was never checked with the FDA: its badge is left out
            if (fdaBadge != null)
            {
                bool show = has && !(record.source == "recommendation" && PhFdaVerification.StatusOf(record) == PhFdaVerification.Unavailable);
                fdaBadge.gameObject.SetActive(show);
                if (show) fdaBadge.Bind(record, PhFdaVerification.IsChecking(record.id));
            }

            string from = has ? ListFrom(record) : "";
            if (listSourceText != null)
            {
                listSourceText.gameObject.SetActive(has);
                // a scan saved before "web-backed" was kept is not called "not web-backed": that is simply not known
                bool backedOrUnknown = has && (record.listWebBacked || string.IsNullOrEmpty(record.listFrom));
                string line = from == "typed" ? listTyped : from == "inventory" ? listFromInventory
                            : from == "web" ? (backedOrUnknown ? listFromWeb : listFromWebUnchecked) : listFromPack;
                // a list that was not read from the user's own pack: when it was looked up, and that it may differ
                bool fromOpenBeautyFacts = has && from == "web" && record.ingredientSources != null && record.ingredientSources.Count > 0
                                           && ListSources.KindOf(record.ingredientSources[0], record.brandName) == ListSources.Kind.OpenBeautyFacts;
                if (fromOpenBeautyFacts)
                {
                    line = listFromOpenBeautyFacts;
                    if (record.listDateUnix > 0) line += " " + string.Format(listSecondEdited, DayOf(record.listDateUnix));
                }
                else if (has && from == "web" && record.listDateUnix > 0) line += " " + string.Format(listLookedUpOn, DayOf(record.listDateUnix));
                if (has && (from == "web" || from == "inventory") && !string.IsNullOrWhiteSpace(listMayDiffer)) line += " " + listMayDiffer;
                listSourceText.text = line;
            }
            ShowListCheck(from);

            // the pages the list came from, as buttons (never the FDA's page: that one holds no ingredient lists)
            int shown = 0;
            if (has && sourceLinkTemplate != null && sourceLinksContent != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // the pages the list came from (a looked-up or stored list), then the page of the second place it was checked against
                var pages = new List<string>();
                if (from == "web" || from == "inventory") pages.AddRange(record.ingredientSources ?? new List<string>());
                if (!string.IsNullOrWhiteSpace(record.listCheckSource)) pages.Add(record.listCheckSource);
                int most = Mathf.Max(1, maximumSourceLinks) + (string.IsNullOrWhiteSpace(record.listCheckSource) ? 0 : 1);
                foreach (string source in pages)
                {
                    if (shown >= most) break;
                    if (shown >= Mathf.Max(1, maximumSourceLinks) && source != record.listCheckSource) continue;      // keep the last place for the second source
                    string address = SourceLinkButton.AddressIn(source);
                    if (address.Length == 0 || address.IndexOf("verification.fda.gov.ph", StringComparison.OrdinalIgnoreCase) >= 0 || !seen.Add(address)) continue;
                    SourceLinkButton link = Instantiate(sourceLinkTemplate, sourceLinksContent);
                    link.gameObject.name = "SourceLink_" + SourceLinkButton.SiteName(address);
                    link.gameObject.SetActive(true);
                    link.Bind(source);
                    sourceLinks.Add(link.gameObject);
                    shown++;
                }
            }
            if (sourceLinksContent != null) sourceLinksContent.gameObject.SetActive(shown > 0);
        }

        /// <summary>"12 Oct 2026" for a time kept in Unix seconds.</summary>
        static string DayOf(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().DateTime.ToString("d MMM yyyy");

        /// <summary>
        /// "MATCHED 2 SOURCES", or what a second place lists differently. The line is hidden when the analysis found no
        /// second place to compare the list with (offline, no barcode, a product nobody else lists).
        /// </summary>
        void ShowListCheck(string from)
        {
            if (listCheckText == null) return;
            string outcome = record != null ? (record.listCheck ?? "") : "";
            bool show = outcome == ListSources.Matched || outcome == ListSources.Differs;
            listCheckText.gameObject.SetActive(show);
            if (!show) return;
            string shown = from == "typed" ? nameOfTyped : from == "inventory" ? nameOfInventory : from == "web" ? nameOfWeb : nameOfPack;
            string other = record.listCheckKind == "inventory" ? nameOfInventory : nameOfOpenBeautyFacts;
            string line;
            if (outcome == ListSources.Matched) line = string.Format(listMatched, shown, other);
            else
            {
                string what = "";
                if (record.listOnlyHere != null && record.listOnlyHere.Count > 0) what += string.Format(listOnlyHereFormat, string.Join(", ", record.listOnlyHere));
                if (record.listOnlyThere != null && record.listOnlyThere.Count > 0) what += string.Format(listOnlyThereFormat, string.Join(", ", record.listOnlyThere));
                line = string.Format(listDiffers, shown, Capital(other), Mathf.Max(1, record.listDifferCount), what);
            }
            if (record.listCheckKind != "inventory" && record.listCheckDateUnix > 0) line += " " + string.Format(listSecondEdited, DayOf(record.listCheckDateUnix));
            listCheckText.text = line;
        }

        static string Capital(string text) => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>
        /// Where a scan's ingredient list came from: "pack", "inventory", "web" or "typed". A scan saved before this was
        /// kept is worked out from how it was made.
        /// </summary>
        public static string ListFrom(ScanRecord r)
        {
            if (r == null) return "pack";
            string kept = (r.listFrom ?? "").Trim().ToLowerInvariant();
            if (kept == "pack" || kept == "inventory" || kept == "web" || kept == "typed") return kept;
            if (r.source == "manual") return "typed";
            if (r.source == "recommendation") return "inventory";
            if (r.source != "lookup") return "pack";
            foreach (string s in r.ingredientSources ?? new List<string>())
                if (!string.IsNullOrEmpty(s) && s.IndexOf("GlowUp product inventory", StringComparison.OrdinalIgnoreCase) >= 0) return "inventory";
            return "web";
        }

        /// <summary>
        /// "Why this result?" without the parts that have their own place on this screen: the FDA Philippines paragraph
        /// (now the badge), where the ingredient list came from (now one line and the page buttons) and the closing
        /// dermatologist line (now the last card). Scans saved before that change still carry those parts in their text.
        /// </summary>
        public static string ExplanationForDisplay(string explanation)
        {
            if (string.IsNullOrEmpty(explanation)) return "";
            var kept = new List<string>();
            foreach (string paragraph in explanation.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.None))
            {
                string p = paragraph.Trim();
                if (p.Length == 0) continue;
                if (p.StartsWith("<b>FDA Philippines</b>", StringComparison.Ordinal)) continue;
                if (p.StartsWith("<b>Where the ingredient list came from</b>", StringComparison.Ordinal)) continue;
                if (p == ProductScope.DermatologistLine) continue;
                kept.Add(p);
            }
            return string.Join("\n\n", kept);
        }

        /// <summary>
        /// The long text as this screen shows it. A scene that was built before the FDA badge and the page buttons existed
        /// has no place for them: there the same facts are added to the text in two short lines, so nothing gets lost.
        /// </summary>
        string ExplanationShown()
        {
            string text = ExplanationForDisplay(record.explanation);
            var extra = new List<string>();
            if (fdaBadge == null && !string.IsNullOrWhiteSpace(record.fdaLine)) extra.Add("<b>FDA Philippines</b>\n" + record.fdaLine);
            if (listSourceText == null && ListFrom(record) == "web")
            {
                var pages = new List<string>();
                foreach (string source in record.ingredientSources ?? new List<string>()) { string site = SourceLinkButton.SiteName(SourceLinkButton.AddressIn(source)); if (site.Length > 0 && !pages.Contains(site)) pages.Add(site); }
                extra.Add("<b>Where the ingredient list came from</b>\n" + listFromWeb + (pages.Count > 0 ? " (" + string.Join(", ", pages.Take(3)) + ")" : ""));
            }
            return extra.Count == 0 ? text : (text.Length > 0 ? text + "\n\n" : "") + string.Join("\n\n", extra);
        }

        void ShowShades()
        {
            if (shadeCard == null || record == null) return;
            string area = ShadeAdvisor.AreaFor(record.cosmeticType);
            string tone = profile != null ? profile.skinTone : "";
            List<string> shades = record.recommendedShades != null && record.recommendedShades.Count > 0 && string.IsNullOrWhiteSpace(tone)
                ? record.recommendedShades : ShadeAdvisor.Recommend(tone, area);
            bool colour = ShadeAdvisor.IsColourCosmetic(record.cosmeticType);
            shadeCard.SetActive(colour && shades.Count > 0);
            if (!colour) return;
            for (int i = 0; shadeSwatches != null && i < shadeSwatches.Length; i++)
            {
                if (shadeSwatches[i] == null) continue;
                bool on = i < shades.Count;
                shadeSwatches[i].gameObject.SetActive(on);
                if (on) shadeSwatches[i].color = ShadeAdvisor.Parse(shades[i], Color.gray);
            }
            if (shadeCaption != null)
                shadeCaption.text = string.Format(string.IsNullOrWhiteSpace(tone) ? shadeCaptionNoTone : shadeCaptionFormat, tone, area.ToLowerInvariant());
        }

        // ---------------------------------------------------------------------------------------
        //  Alternatives
        // ---------------------------------------------------------------------------------------
        async void ShowAlternatives()
        {
            if (alternativesPanel == null) return;
            alternativesPanel.Show();
            foreach (var r in alternativeRows) if (r != null) Destroy(r);
            alternativeRows.Clear();
            if (alternativesEmptyText != null) { alternativesEmptyText.gameObject.SetActive(true); alternativesEmptyText.text = msgLoadingAlternatives; }

            List<ProductData> products;
            try { products = await ProductRepository.LoadActive(); await IngredientRepository.LoadAll(); }
            catch (Exception e) { Debug.LogWarning("Alternatives: " + e.Message); products = new List<ProductData>(); }
            if (this == null) return;

            // Make-up of the same kind whose ingredients are not flagged for this user (the same check as the dashboard's
            // recommendations: see ProductRecommender; stricter for a user who saved a skin condition). Worked out on the phone.
            var who = profile ?? new UserProfileData();
            string area = record != null ? ShadeAdvisor.AreaFor(record.cosmeticType) : "";
            bool SameKind(ProductData p)
            {
                if (record == null) return true;
                if (TextKey.Same(p.name, record.productName)) return false;            // not the product itself
                // same kind of product: same type, or at least used on the same part of the face
                return string.Equals(p.cosmeticType, record.cosmeticType, StringComparison.OrdinalIgnoreCase)
                    || (ShadeAdvisor.IsColourCosmetic(p.cosmeticType) && ShadeAdvisor.IsColourCosmetic(record.cosmeticType) && ShadeAdvisor.AreaFor(p.cosmeticType) == area);
            }
            foreach (ProductRecommender.Pick pick in ProductRecommender.For(who, products, maximumAlternatives, SameKind))
            {
                ProductData p = pick.product;
                ScanRecord alt = pick.record;
                if (alternativeTileTemplate == null || alternativesContent == null) continue;
                ProductTileView tile = Instantiate(alternativeTileTemplate, alternativesContent);
                tile.gameObject.name = "Alternative_" + p.name;
                tile.gameObject.SetActive(true);
                tile.Bind(alt, p.name, p.brandName, p.cosmeticType, typeIcons != null ? typeIcons.For(p.cosmeticType) : null);
                tile.SetTryOnAvailable(ProductScope.CanTryOn(alt));                 // AR Try-On is for make-up only
                tile.TryOnClicked += t => { ScanSession.TryOn = t.Data as ScanRecord; SceneNavigator.Go(tryOnScene); };
                tile.AiClicked += t => { if (aiPanel != null) aiPanel.Show(t.Data as ScanRecord); };
                tile.SaveClicked += t =>
                {
                    if (t.Data is ScanRecord saved && string.IsNullOrEmpty(saved.id)) ScanHistoryRepository.Add(saved);
                    t.SetSaveLabel(savedLabel, false);
                };
                alternativeRows.Add(tile.gameObject);
            }
            if (alternativesEmptyText != null)
            {
                alternativesEmptyText.gameObject.SetActive(alternativeRows.Count == 0);
                alternativesEmptyText.text = msgNoAlternatives;
            }
        }

        // ---------------------------------------------------------------------------------------
        //  Rename (local only)
        // ---------------------------------------------------------------------------------------
        void BeginRename()
        {
            if (renamePanel == null || record == null) return;
            renamePanel.Show();
            if (renameInput != null) renameInput.text = record.Title;
        }

        void SaveRename()
        {
            if (record != null && renameInput != null)
            {
                string name = renameInput.text.Trim();
                if (ScanHistoryRepository.Find(record.id) != null) ScanHistoryRepository.Rename(record.id, name);   // saved on this device only
                record.displayName = name;
                Set(nameText, record.Title);
            }
            if (renamePanel != null) renamePanel.Hide();
        }
    }
}
