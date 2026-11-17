using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// AR Try-On scene: the front camera with a face guide, the shades of the chosen product, the
    /// shades recommended for the user's skin tone, the finish (matte, satin, glossy, shimmer,
    /// metallic), a strength slider, the before/after split and a product picker.
    /// Only the part of the face the product is made for can be chosen (a lipstick: Lips only).
    /// Opened from the dashboard / bottom bar it first asks which saved product to try; opened from
    /// a result or a recommendation it starts with that product.
    /// </summary>
    public class ArTryOnFlowController : MonoBehaviour
    {
        [Header("Camera and preview")]
        [SerializeField] private CameraFeedController cameraFeed;
        [SerializeField] private FallbackMakeupOverlay makeup;
        [Tooltip("Live face tracking. When it is set, it decides: tracking on phones that support it, the guided preview on the others.")]
        [SerializeField] private ArFaceTrackingController liveTracking;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private string msgAlign = "Line your face up with the guide";
        [SerializeField] private string msgStarting = "Starting the camera...";
        [Tooltip("The light card that fills the camera box until there is a camera picture: it says why the camera is asked for, that it is starting, or that it is off. Without it the box is an empty dark area meanwhile.")]
        [SerializeField] private CameraWaitCard cameraCard;

        [Header("Product")]
        [SerializeField] private TextMeshProUGUI productText;
        [Tooltip("{0} = product name, {1} = cosmetic type")]
        [SerializeField] private string productFormat = "Trying: {0} ({1})";
        [SerializeField] private string productNone = "Choose a product to try on";
        [SerializeField] private Button changeProductButton;

        [Header("Where the colour goes (tabs in this order: Lips, Cheeks, Eyes, Face, Brows)")]
        [SerializeField] private ChoiceTabs areaTabs;
        [Tooltip("On = only the part the product is made for can be chosen (a lipstick: Lips only). Off = every part can be chosen.")]
        [SerializeField] private bool onlyMatchingArea = true;
        [Tooltip("Said in the guided preview when brows are chosen: brow colour needs live face tracking")]
        [SerializeField] private string msgBrowsNeedLive = "Brow colour needs live face tracking, which this phone does not offer. Try lips, cheeks or eyes here.";

        [Header("Finish (tabs in the order of Finish Names)")]
        [SerializeField] private ChoiceTabs finishTabs;
        [Tooltip("The finish of each tab. The names must match the list 'Finishes' on FallbackMakeupOverlay.")]
        [SerializeField] private string[] finishNames = { "matte", "satin", "glossy", "shimmer", "metallic" };
        [Tooltip("The finish used when the scan did not tell the product's finish")]
        [SerializeField] private string defaultFinish = "satin";

        [Header("Before and after")]
        [SerializeField] private BeforeAfterSplit split;

        [Header("Shades")]
        [Tooltip("Hidden when the product has no shades of its own")]
        [SerializeField] private GameObject productShadesGroup;
        [SerializeField] private ShadeButton[] productShadeButtons;
        [SerializeField] private ShadeButton[] recommendedShadeButtons;
        [SerializeField] private TextMeshProUGUI recommendedLabel;
        [Tooltip("{0} = the user's skin tone")]
        [SerializeField] private string recommendedFormat = "Recommended for {0} skin";
        [SerializeField] private string recommendedNoTone = "Recommended shades";

        [Header("Strength")]
        [SerializeField] private Slider intensitySlider;
        [SerializeField] private TextMeshProUGUI intensityValueText;
        [SerializeField] private Button lessButton;
        [SerializeField] private Button moreButton;
        [Range(0f, 1f)] [SerializeField] private float startIntensity = 0.6f;
        [Tooltip("How much one tap on - or + changes the strength")]
        [Range(0.01f, 0.5f)] [SerializeField] private float intensityStep = 0.1f;

        [Header("Product picker")]
        [SerializeField] private OverlayPanel pickerPanel;
        [SerializeField] private RectTransform pickerContent;
        [Tooltip("The pattern tile. Hidden when the app runs and copied once per saved make-up product.")]
        [SerializeField] private ProductTileView pickerTileTemplate;
        [SerializeField] private TextMeshProUGUI pickerEmptyText;
        [Tooltip("Typing here narrows the saved products, and also finds make-up in the product inventory that was never scanned")]
        [SerializeField] private TMP_InputField pickerSearchInput;
        [Tooltip("Letters needed before the product inventory is searched")]
        [SerializeField] private int searchFromLetters = 2;
        [Tooltip("Most inventory products shown for one search")]
        [SerializeField] private int maximumSearchResults = 24;
        [SerializeField] private string msgNothingFound = "No make-up found for that search. Try another word, or scan the product.";
        [SerializeField] private Button pickerScanButton;
        [SerializeField] private CosmeticTypeIcons typeIcons;
        [SerializeField] private string msgNoMakeup = "You have no saved make-up yet. Scan a lipstick, blush, eyeshadow or foundation first, then come back to try it on.";

        [Header("Buttons / scenes")]
        [SerializeField] private Button backButton;
        [SerializeField] private string backScene = SceneNames.Dashboard;
        [SerializeField] private string scanScene = SceneNames.AiSmartAnalysis;

        ScanRecord record;
        UserProfileData profile;
        ShadeButton selected;
        readonly List<GameObject> pickerRows = new List<GameObject>();

        void Awake()
        {
            if (pickerTileTemplate != null) pickerTileTemplate.gameObject.SetActive(false);
            if (backButton != null) backButton.onClick.AddListener(() => { StopCamera(); SceneNavigator.Go(backScene); });
            if (changeProductButton != null) changeProductButton.onClick.AddListener(OpenPicker);
            if (pickerScanButton != null) pickerScanButton.onClick.AddListener(() => { StopCamera(); SceneNavigator.Go(scanScene); });
            if (areaTabs != null) areaTabs.Changed += _ => OnAreaChanged();
            if (finishTabs != null) finishTabs.Changed += index => ApplyFinish(index);
            if (split != null) split.Changed += on => { if (liveTracking != null) liveTracking.SetCompare(on); };
            foreach (ShadeButton b in AllShadeButtons()) if (b != null) b.Clicked += Select;
            if (intensitySlider != null)
            {
                intensitySlider.minValue = 0f; intensitySlider.maxValue = 1f;
                intensitySlider.SetValueWithoutNotify(startIntensity);
                intensitySlider.onValueChanged.AddListener(_ => ApplyIntensity());
            }
            if (lessButton != null) lessButton.onClick.AddListener(() => { if (intensitySlider != null) intensitySlider.value = Mathf.Clamp01(intensitySlider.value - intensityStep); });
            if (moreButton != null) moreButton.onClick.AddListener(() => { if (intensitySlider != null) intensitySlider.value = Mathf.Clamp01(intensitySlider.value + intensityStep); });
            if (cameraFeed != null) cameraFeed.Started += OnCameraStarted;
            if (liveTracking != null) { liveTracking.ModeDecided += OnModeDecided; liveTracking.FollowsFaceChanged += RefreshAreaTabs; }
            if (cameraCard != null) cameraCard.ActionPressed += OnCameraCardAction;
            if (pickerSearchInput != null) pickerSearchInput.onValueChanged.AddListener(_ => FillPicker());
        }

        IEnumerable<ShadeButton> AllShadeButtons()
        {
            if (productShadeButtons != null) foreach (var b in productShadeButtons) yield return b;
            if (recommendedShadeButtons != null) foreach (var b in recommendedShadeButtons) yield return b;
        }

        async void Start()
        {
            Say(msgStarting);
            // until there is a camera picture the camera box shows a light card, never an empty dark area: the phone's own
            // question about the camera then stands over a screen that says what it is for
            ShowCameraCard();
            if (liveTracking != null) liveTracking.Begin();              // it starts the guided preview itself when tracking is not possible
            else if (cameraFeed != null) cameraFeed.StartCamera();
            ApplyIntensity();
            if (split != null && split.IsOn && liveTracking != null) liveTracking.SetCompare(true);

            if (UserProfileRepository.Cached() == null) TextSkeleton.Waiting(this);      // nothing on the phone yet: the fetched texts are bars meanwhile
            try { profile = await UserProfileRepository.Load(); } catch (Exception e) { { } }
            TextSkeleton.Ready(this);
            if (this == null) return;

            record = ProductScope.CanTryOn(ScanSession.TryOn) ? ScanSession.TryOn : null;      // make-up only
            ShowProduct();
            if (record == null) OpenPicker();
        }

        // the phone's own back key always leaves the screen (closing the product picker first), even when the camera picture is wrong
        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (pickerPanel != null && pickerPanel.IsOpen) { pickerPanel.Hide(); return; }
            StopCamera();
            SceneNavigator.Go(backScene);
        }

        void OnDestroy()
        {
            if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted;
            if (liveTracking != null) { liveTracking.ModeDecided -= OnModeDecided; liveTracking.FollowsFaceChanged -= RefreshAreaTabs; }
            // 17 Nov 2026 (owner): the product is forgotten on leaving AR Try-On, so the next visit needs a product chosen again
            ScanSession.TryOn = null;
        }
        void StopCamera()
        {
            if (liveTracking != null) liveTracking.StopTracking();
            if (cameraFeed != null) cameraFeed.StopCamera();
        }
        void Say(string message) { if (statusText != null) statusText.text = message; }

        // ---------------------------------------------------------------------------------------
        //  The camera box before there is a picture (CameraWaitCard)
        // ---------------------------------------------------------------------------------------
        bool saidNoOnce;

        void ShowCameraCard()
        {
            if (cameraCard != null) cameraCard.Show(CameraPermission.Granted ? CameraWaitCard.State.Starting : CameraWaitCard.State.Asking);
        }

        /// <summary>Live tracking shows its picture: the card goes. (The guided preview tells through OnCameraStarted.)</summary>
        void OnModeDecided(bool live)
        {
            modeKnown = true;
            if (live && cameraCard != null) cameraCard.Hide();
            RefreshAreaTabs();
            if (!live && CurrentArea() == ShadeAdvisor.Brows) Say(msgBrowsNeedLive);
        }

        /// <summary>
        /// Which part tabs can be tapped: only the part the product is made for (when that rule is on), and Brows only while live
        /// face tracking runs (the guided preview has no brow shapes). Before the mode is known, Brows counts as possible.
        /// </summary>
        void RefreshAreaTabs()
        {
            if (areaTabs == null) return;
            string area = record != null ? ShadeAdvisor.AreaFor(record.cosmeticType) : null;
            bool guidedOnly = liveTracking == null || (modeKnown && !liveTracking.FollowsFace);
            for (int i = 0; i < ShadeAdvisor.Areas.Length; i++)
            {
                bool matching = !onlyMatchingArea || area == null || ShadeAdvisor.Areas[i] == area;
                bool drawable = ShadeAdvisor.Areas[i] != ShadeAdvisor.Brows || !guidedOnly;
                areaTabs.SetAvailable(i, matching && drawable);
            }
        }

        bool modeKnown;
        string CurrentArea() => areaTabs != null ? ShadeAdvisor.Areas[Mathf.Clamp(areaTabs.SelectedIndex, 0, ShadeAdvisor.Areas.Length - 1)]
            : record != null ? ShadeAdvisor.AreaFor(record.cosmeticType) : ShadeAdvisor.Lips;

        void OnCameraStarted(bool ok, string message)
        {
            Say(ok ? msgAlign : message);
            if (cameraCard == null) return;
            if (ok) { cameraCard.Hide(); return; }
            if (!CameraPermission.Granted)
            {
                // the user said no. Android shows its question once more; after a second no it answers by itself, and only
                // the phone's settings can allow the camera
                bool settingsOnly = saidNoOnce && !CameraPermission.CanAskAgain;
                saidNoOnce = true;
                cameraCard.Show(CameraWaitCard.State.Refused, null, settingsOnly);
            }
            else cameraCard.Show(CameraWaitCard.State.Problem, message);
        }

        /// <summary>The button of the card: ask again, open the phone's settings, or try the camera again.</summary>
        void OnCameraCardAction()
        {
            if (cameraCard != null && cameraCard.OffersSettings) { CameraPermission.OpenAppSettings(); return; }
            RestartCamera();
        }

        void RestartCamera()
        {
            Say(msgStarting);
            ShowCameraCard();
            if (liveTracking != null) liveTracking.Begin();
            else if (cameraFeed != null) { cameraFeed.StopCamera(); cameraFeed.StartCamera(); }
        }

        // back from the phone's settings with the camera allowed: it starts without another tap
        void OnApplicationFocus(bool focused)
        {
            if (focused && cameraCard != null && cameraCard.Current == CameraWaitCard.State.Refused && CameraPermission.Granted) RestartCamera();
        }

        // ---------------------------------------------------------------------------------------
        void ShowProduct()
        {
            if (productText != null)
                productText.text = record == null ? productNone
                    : string.Format(productFormat, record.Title, string.IsNullOrWhiteSpace(record.cosmeticType) ? "Cosmetic" : record.cosmeticType);

            string area = record != null ? ShadeAdvisor.AreaFor(record.cosmeticType) : ShadeAdvisor.Lips;
            if (areaTabs != null) areaTabs.Select(Array.IndexOf(ShadeAdvisor.Areas, area), false);
            RefreshAreaTabs();
            if (area == ShadeAdvisor.Brows && modeKnown && liveTracking != null && !liveTracking.FollowsFace) Say(msgBrowsNeedLive);

            // the finish: what the scan saw on the product, otherwise a sensible guess from the kind of product
            string finish = record != null ? (record.finish ?? "") : "";
            if (Array.IndexOf(finishNames, finish) < 0)
            {
                string type = record != null ? (record.cosmeticType ?? "").ToLowerInvariant() : "";
                finish = type.Contains("gloss") ? "glossy" : type.Contains("foundation") || type.Contains("concealer") ? "matte" : defaultFinish;
            }
            int finishIndex = Mathf.Max(0, Array.IndexOf(finishNames, finish));
            if (finishTabs != null) finishTabs.Select(finishIndex, false);
            ApplyFinish(finishIndex);

            // the product's own shades: the colour picked up when it was scanned, then the catalogue shades
            var own = new List<string>();
            if (record != null)
            {
                if (!string.IsNullOrWhiteSpace(record.shadeHex)) own.Add(record.shadeHex);
                if (record.productShades != null) foreach (string s in record.productShades) if (!own.Contains(s)) own.Add(s);
            }
            // a face powder is always shown in white, also one that was saved before with the colour of its box
            if (record != null && ShadeAdvisor.IsFacePowder(record.cosmeticType)) { own.Clear(); own.Add(ShadeAdvisor.PowderShade); }
            GrowProductButtons(own.Count);
            Fill(productShadeButtons, own);
            ScrollToStart(productShadeButtons);
            if (productShadesGroup != null) productShadesGroup.SetActive(own.Count > 0);

            ShowRecommended(area);
            if (makeup != null) makeup.SetArea(area);

            // start on the product's first shade, or the first recommended one
            ShadeButton first = FirstWithShade(productShadeButtons) ?? FirstWithShade(recommendedShadeButtons);
            if (first != null && record != null) Select(first); else { selected = null; if (makeup != null) makeup.Clear(); }
        }

        void ShowRecommended(string area)
        {
            string tone = profile != null ? profile.skinTone : "";
            Fill(recommendedShadeButtons, ShadeAdvisor.Recommend(tone, area));
            if (recommendedLabel != null) recommendedLabel.text = string.IsNullOrWhiteSpace(tone) ? recommendedNoTone : string.Format(recommendedFormat, tone);
        }

        /// <summary>
        /// A product can have any number of shades (the website allows 100). The scene holds a few swatches; for every shade beyond them a copy
        /// of the first swatch is added to the same row, which scrolls sideways (SideScrollRect).
        /// </summary>
        void GrowProductButtons(int count)
        {
            if (productShadeButtons == null || productShadeButtons.Length == 0 || productShadeButtons[0] == null || count <= productShadeButtons.Length) return;
            ShadeButton pattern = productShadeButtons[0];
            var grown = new List<ShadeButton>(productShadeButtons);
            while (grown.Count < count)
            {
                ShadeButton copy = Instantiate(pattern, pattern.transform.parent);
                copy.name = "Shade" + (grown.Count + 1);
                copy.Clicked += Select;
                grown.Add(copy);
            }
            productShadeButtons = grown.ToArray();
        }

        /// <summary>A new product starts its row of shades at the first one.</summary>
        static void ScrollToStart(ShadeButton[] buttons)
        {
            if (buttons == null || buttons.Length == 0 || buttons[0] == null) return;
            ScrollRect row = buttons[0].GetComponentInParent<ScrollRect>();
            if (row != null && row.horizontal) { row.StopMovement(); row.horizontalNormalizedPosition = 0f; }
        }

        static void Fill(ShadeButton[] buttons, List<string> shades)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].Set(i < shades.Count ? shades[i] : null);
                buttons[i].SetSelected(false);
            }
        }

        static ShadeButton FirstWithShade(ShadeButton[] buttons)
        {
            if (buttons != null) foreach (var b in buttons) if (b != null && b.HasShade) return b;
            return null;
        }

        void OnAreaChanged()
        {
            string area = ShadeAdvisor.Areas[Mathf.Clamp(areaTabs.SelectedIndex, 0, ShadeAdvisor.Areas.Length - 1)];
            ShowRecommended(area);
            if (makeup != null) makeup.SetArea(area);
            // keep the chosen shade if it is one of the product's own; otherwise take the first recommended for the new area
            if (selected == null || Array.IndexOf(recommendedShadeButtons ?? new ShadeButton[0], selected) >= 0)
            {
                ShadeButton first = FirstWithShade(recommendedShadeButtons);
                if (first != null && record != null) Select(first);
            }
        }

        void Select(ShadeButton button)
        {
            selected = button;
            foreach (ShadeButton b in AllShadeButtons()) if (b != null) b.SetSelected(b == button);
            if (makeup != null) makeup.SetShade(button.Shade);
            ArDebugOverlay.ShadeNote = DescribeShade(button);
        }

        /// <summary>For the debug overlay: "12A -> #B5463F" when the product's shade code is known, otherwise the hex.</summary>
        string DescribeShade(ShadeButton button)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(button.Shade);
            if (record == null || record.productShades == null || record.productShadeCodes == null) return hex;
            for (int i = 0; i < record.productShades.Count && i < record.productShadeCodes.Count; i++)
                if (string.Equals(record.productShades[i], hex, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(record.productShadeCodes[i]))
                    return record.productShadeCodes[i].Trim() + " -> " + hex;
            return hex;
        }

        void ApplyFinish(int index)
        {
            if (makeup == null || finishNames == null || finishNames.Length == 0) return;
            makeup.SetFinish(finishNames[Mathf.Clamp(index, 0, finishNames.Length - 1)]);
        }

        void ApplyIntensity()
        {
            float v = intensitySlider != null ? intensitySlider.value : startIntensity;
            if (makeup != null) makeup.SetIntensity(v);
            if (intensityValueText != null) intensityValueText.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        // ---------------------------------------------------------------------------------------
        //  Picking a saved product
        // ---------------------------------------------------------------------------------------
        void OpenPicker()
        {
            if (pickerPanel == null) return;
            pickerPanel.Show();
            if (pickerSearchInput != null) pickerSearchInput.SetTextWithoutNotify("");
            FillPicker();
            LoadInventoryForSearch();
        }

        List<ProductData> inventory;                 // the product inventory, loaded the first time the picker opens (also from the phone's copy when offline)
        async void LoadInventoryForSearch()
        {
            if (inventory != null) return;
            try { inventory = await ProductRepository.LoadActive() ?? new List<ProductData>(); }
            catch (Exception e) { { } inventory = new List<ProductData>(); }
            if (this != null && SearchWords().Length >= searchFromLetters) FillPicker();
        }

        string SearchWords() => pickerSearchInput != null ? (pickerSearchInput.text ?? "").Trim().ToLowerInvariant() : "";
        static bool Holds(string text, string words) => !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(words);

        /// <summary>The saved make-up (narrowed by the search), then make-up from the inventory that the search finds.</summary>
        void FillPicker()
        {
            foreach (var r in pickerRows) if (r != null) Destroy(r);
            pickerRows.Clear();

            string words = SearchWords();
            List<ScanRecord> saved = ScanHistoryRepository.Active().FindAll(ProductScope.CanTryOn);      // skin care, hair and body products cannot be tried on
            if (words.Length > 0) saved = saved.FindAll(r => Holds(r.Title, words) || Holds(r.brandName, words) || Holds(r.cosmeticType, words));

            // not scanned yet: the product can be searched for in the inventory by hand
            if (words.Length >= Mathf.Max(1, searchFromLetters) && inventory != null)
            {
                int found = 0;
                foreach (ProductData product in inventory)
                {
                    if (found >= maximumSearchResults) break;
                    if (product == null || string.IsNullOrWhiteSpace(product.name)) continue;
                    if (!Holds(product.name, words) && !Holds(product.brandName, words) && !Holds(product.cosmeticType, words)) continue;
                    if (saved.Exists(r => string.Equals(r.productId, product.id) || string.Equals((r.productName ?? "").Trim(), product.name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                    ScanRecord made;
                    try
                    {
                        made = ProductAnalysisEngine.Analyse(product.ingredients ?? new List<string>(), "", profile ?? new UserProfileData(), null, product.name, product.brandName, product.cosmeticType);
                    }
                    catch (Exception e) { { } continue; }
                    made.source = "recommendation"; made.brandId = product.brandId; made.productId = product.id;
                    made.productShades = new List<string>(product.shades ?? new List<string>());
                    made.productShadeCodes = new List<string>(product.shadeCodes ?? new List<string>());
                    if (string.IsNullOrEmpty(made.shadeHex) && made.productShades.Count > 0) made.shadeHex = made.productShades[0];
                    if (!ProductScope.CanTryOn(made)) continue;
                    saved.Add(made); found++;
                }
            }

            if (pickerTileTemplate != null && pickerContent != null)
                foreach (ScanRecord r in saved)
                {
                    ProductTileView tile = Instantiate(pickerTileTemplate, pickerContent);
                    tile.gameObject.name = "Pick_" + r.Title;
                    tile.gameObject.SetActive(true);
                    tile.Bind(r, r.Title, r.brandName, string.IsNullOrWhiteSpace(r.cosmeticType) ? "Cosmetic" : r.cosmeticType, typeIcons != null ? typeIcons.For(r.cosmeticType) : null);
                    tile.Clicked += Pick; tile.TryOnClicked += Pick;
                    pickerRows.Add(tile.gameObject);
                }
            if (pickerEmptyText != null) { pickerEmptyText.gameObject.SetActive(saved.Count == 0); pickerEmptyText.text = words.Length > 0 ? msgNothingFound : msgNoMakeup; }
        }

        void Pick(ProductTileView tile)
        {
            record = tile.Data as ScanRecord;
            ScanSession.TryOn = record;
            if (pickerPanel != null) pickerPanel.Hide();
            ShowProduct();
        }
    }
}
