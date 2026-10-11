using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Scanning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// AI Smart Scan scene. A scan has two phases, shown as a checklist with gold check marks:
    ///
    ///  Phase 1, the product: the user shows the front of the product. The text on it is read and
    ///     compared with the brands and products the app knows; when the phone is online the AI also
    ///     looks at the picture. Found one by one: product name, brand, kind of cosmetic, shade and
    ///     texture. "Skip" goes on without it.
    ///  Phase 2, the ingredients: the user shows the ingredient list and it is read. If the product
    ///     has no list on it (or no packaging), "No list on it? Look it up" finds the product's
    ///     published ingredient list: first in the app's own inventory (works offline), then online
    ///     from credible sources. Hidden additives found on the way are saved for later scans.
    ///
    ///  The user may also show the ingredient list first: its check mark lights up and the main
    ///  button becomes "Finish".
    ///
    /// Before Start Scan the camera is off. "Type the ingredients instead" opens a box for labels the
    /// camera cannot read. Every text, threshold and timing below is an Inspector field.
    /// </summary>
    public class SmartScanFlowController : MonoBehaviour
    {
        enum Phase { Product, Ingredients }

        [Header("Camera")]
        [SerializeField] private CameraFeedController cameraFeed;
        [SerializeField] private ScanGuideOverlay guide;
        [Tooltip("Shown in the camera area before Start Scan is tapped")]
        [SerializeField] private GameObject idlePlaceholder;
        [Tooltip("Draws an outline round each line of text that was read. Optional.")]
        [SerializeField] private TextOutlineOverlay textOutlines;

        [Header("Buttons and texts")]
        [SerializeField] private Button startScanButton;
        [SerializeField] private TextMeshProUGUI startScanLabel;
        [SerializeField] private Button manualEntryButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Phase 1: the product")]
        [SerializeField] private ScanCheckItem productCheck;
        [SerializeField] private ScanCheckItem brandCheck;
        [SerializeField] private ScanCheckItem typeCheck;
        [SerializeField] private ScanCheckItem shadeCheck;
        [Tooltip("A small dot that takes the colour of the shade that was found")]
        [SerializeField] private Image shadeSwatch;
        [Tooltip("The phase's part of the checklist (it fades a little while the other phase is busy)")]
        [SerializeField] private CanvasGroup phase1Group;
        [Tooltip("Goes on to the ingredients without knowing the product")]
        [SerializeField] private Button skipButton;

        [Header("Phase 2: the ingredients")]
        [SerializeField] private ScanCheckItem ingredientsCheck;
        [SerializeField] private CanvasGroup phase2Group;
        [Tooltip("For a product without an ingredient list on it: looks the list up")]
        [SerializeField] private Button lookUpButton;
        [Tooltip("The words on that button (they change while it is working)")]
        [SerializeField] private TextMeshProUGUI lookUpLabel;
        [SerializeField] private string lookUpIdleLabel = "Look it up";
        [SerializeField] private string lookUpWorkingLabel = "Looking it up...";
        [Range(0f, 1f)] [SerializeField] private float idlePhaseAlpha = 0.5f;

        [Header("AI (used when the phone is online)")]
        [Tooltip("The project's AI settings. Empty = the ones the loading scene uses.")]
        [SerializeField] private GeminiApiConfig aiConfig;
        [Tooltip("On = the AI looks at the picture to recognise the product")]
        [SerializeField] private bool aiRecognisesProduct = true;
        [Tooltip("At most this many pictures are shown to the AI in one scan")]
        [SerializeField] private int maximumAiLooks = 4;
        [SerializeField] private float secondsBetweenAiLooks = 3.5f;
        [Tooltip("JPG quality of the picture the AI gets (lower = smaller and faster)")]
        [Range(40, 95)] [SerializeField] private int aiPictureQuality = 80;
        [Tooltip("The AI's answer counts when it is at least this sure (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float minimumAiConfidence = 0.45f;

        [Header("Reading")]
        [Tooltip("The phone counts as steady below this amount of picture change (0 = frozen)")]
        [Range(0.001f, 0.1f)] [SerializeField] private float steadyMotionThreshold = 0.018f;
        [Tooltip("How long the phone must stay steady before a read is tried")]
        [SerializeField] private float steadySeconds = 0.6f;
        [SerializeField] private float secondsBetweenReads = 1.6f;
        [Tooltip("A read counts as an ingredient list when at least this many ingredients we know are found")]
        [SerializeField] private int minimumKnownIngredients = 2;
        [Tooltip("...or when at least this many ingredient-like names are found")]
        [SerializeField] private int minimumListedNames = 5;
        [Tooltip("Longest side, in pixels, of the picture given to the text reader (smaller = faster)")]
        [SerializeField] private int readPictureSize = 1100;
        [Tooltip("How long the 'recognised' message stays before the loading scene opens")]
        [SerializeField] private float recognisedHoldSeconds = 1.1f;

        [Header("Scenes")]
        [SerializeField] private string resultScene = SceneNames.AiResult;
        [SerializeField] private string backScene = SceneNames.Dashboard;

        [Header("Labels and hints")]
        [SerializeField] private string startButtonLabel = "Start Scan";
        [SerializeField] private string readNowLabel = "Read Now";
        [SerializeField] private string finishLabel = "Finish";
        [Tooltip("The main button after a scan was stopped because the product is not make-up")]
        [SerializeField] private string scanAgainLabel = "Scan again";
        [SerializeField] private string hintProduct = "Show the front of the product and hold steady";
        [SerializeField] private string hintIngredients = "Now fit the ingredient list inside the frame";
        [Tooltip("{0} = how many ingredients were read")]
        [SerializeField] private string ingredientsFoundFormat = "{0} ingredients read";
        [SerializeField] private string ingredientsLookedUpFormat = "{0} ingredients found";

        [Header("Messages")]
        [SerializeField] private string msgBeforeStart = "Tap Start Scan, then show the front of the product.";
        [SerializeField] private string msgStarting = "Starting the camera...";
        [SerializeField] private string msgPhase1 = "Step 1: show the front of the product. Hold steady.";
        [SerializeField] private string msgProductNotYet = "Still looking for the product's name. Move closer and avoid glare, or tap Skip.";
        [SerializeField] private string msgAiLooking = "Recognising the product...";
        [SerializeField] private string msgPhase2 = "Step 2: now show the ingredient list.";
        [SerializeField] private string msgListFirst = "Ingredients read. Show the front of the product to name it, or tap Finish.";
        [SerializeField] private string msgTooDark = "It is too dark, or the lens is covered.";
        [SerializeField] private string msgNotYet = "Could not read the list yet. Move closer, avoid glare and hold steady.";
        [SerializeField] private string msgDone = "Got it! Analysing the product now...";
        [SerializeField] private string msgReaderLoading = "Getting the text reader ready...";
        [SerializeField] private string msgReaderMissing = "The text reader is not available on this device. Type the ingredients instead.";
        [SerializeField] private string msgNeedIngredients = "Type or paste at least two ingredients, separated by commas.";
        [SerializeField] private string msgLookUpNeedsName = "Show the front of the product first, so we know which product to look up.";
        [SerializeField] private string msgLookingUp = "Looking up this product's ingredient list...";
        [SerializeField] private string msgLookUpOffline = "This product is not in the app's list yet. Connect to the internet to look it up, or type the ingredients.";
        [SerializeField] private string msgNoListFound = "No trustworthy ingredient list was found for this product. Scan the list or type it in.";
        [Tooltip("No list was found: the FDA Philippines list is asked, so the user at least learns whether the product is registered")]
        [SerializeField] private string msgNoListCheckingFda = "No ingredient list found yet. Checking the FDA Philippines list...";
        [Tooltip("{0} = what the FDA Philippines list says about the product (one of the four sentences below)")]
        [SerializeField] private string msgNoListButFda = "No published ingredient list was found. {0} Scan the list on the pack, or type it in.";
        [SerializeField] private string fdaSaysRegistered = "FDA Philippines: registered.";
        [SerializeField] private string fdaSaysExpired = "FDA Philippines: its registration has expired.";
        [SerializeField] private string fdaSaysSimilar = "FDA Philippines has a similar record: check the name.";
        [SerializeField] private string fdaSaysNone = "No FDA Philippines record was found either.";
        [SerializeField] private string loadingMessage = "Scanning and analysing your product";

        bool scanning, leaving, readRequested, lookingUp;
        float steadyFor, sinceLastRead, readerWait, lastAiLook = -100f;
        int aiLooks;
        Phase phase = Phase.Product;

        // what has been found so far
        readonly ProductAiAdvisor.Identity identity = new ProductAiAdvisor.Identity();
        ProductData knownProduct;

        [Header("Barcode")]
        [Tooltip("On = while scanning, the camera also looks for the product's barcode (on the phone itself, no internet). A product whose barcode is in the inventory is recognised at once, with its saved ingredient list.")]
        [SerializeField] private bool readBarcode = true;
        MlKitBarcodeReader barcodeReader; bool barcodeReaderAsked;
        string barcode = "";                      // the number under the barcode, once the camera has seen it
        volatile string barcodeSeen;              // set by the barcode reader (not on Unity's thread); TakeBarcode() takes it over
        string ingredientText, ingredientSource = "scan", lastColour = "";
        string listFrom = "";                     // where the ingredient list came from: "pack", "inventory", "web" or "typed"
        bool listWebBacked;                       // a list looked up online came with pages a live web search really opened
        List<string> sources = new List<string>();
        Task<ProductAiAdvisor.Identity> aiTask;
        List<BrandData> brands = new List<BrandData>();
        List<ProductData> products = new List<ProductData>();

        void Awake()
        {
            if (startScanButton != null) startScanButton.onClick.AddListener(OnMainButton);
            if (manualEntryButton != null) manualEntryButton.onClick.AddListener(OpenManual);
            if (manualAnalyseButton != null) manualAnalyseButton.onClick.AddListener(AnalyseManual);
            if (skipButton != null) skipButton.onClick.AddListener(Skip);
            if (lookUpButton != null) lookUpButton.onClick.AddListener(LookUp);
            if (backButton != null) backButton.onClick.AddListener(() => { StopScanning(); SceneNavigator.Go(backScene); });
            if (cameraFeed != null) cameraFeed.Started += OnCameraStarted;
            if (aiConfig != null) ProductAiAdvisor.Config = aiConfig;
        }

        [Header("Type-it-in box")]
        [SerializeField] private OverlayPanel manualPanel;
        [SerializeField] private TMP_InputField manualInput;
        [Tooltip("Optional boxes for what the camera or the AI could not tell")]
        [SerializeField] private TMP_InputField manualProductInput;
        [SerializeField] private TMP_InputField manualBrandInput;
        [SerializeField] private TMP_Dropdown manualTypeDropdown;
        [Tooltip("The first choice of the kind list, meaning: not chosen")]
        [SerializeField] private string manualTypeNone = "Cosmetic type";
        [Tooltip("The kinds offered in the list. GlowUp checks make-up only: a kind that is not make-up is left out of the list even when it is typed here.")]
        [SerializeField] private string[] manualTypes =
        {
            "Lipstick", "Lip Gloss", "Lip Tint", "Lip Liner", "Foundation", "Concealer", "Primer", "Powder", "Blush", "Bronzer", "Highlighter",
            "Contour", "Eyeshadow", "Eyeliner", "Mascara", "Brow", "Setting Spray"
        };
        [SerializeField] private Button manualAnalyseButton;
        [SerializeField] private TextMeshProUGUI manualFeedbackText;

        async void Start()
        {
            OcrService.Shared.Initialise();                  // loads in the background while the user reads the instructions
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Hidden);
            if (idlePlaceholder != null) idlePlaceholder.SetActive(true);
            if (startScanLabel != null) startScanLabel.text = startButtonLabel;
            ScanSession.ClearPending();
            ResetChecklist();

            // coming back from a scan that could not be analysed
            Say(string.IsNullOrEmpty(ScanSession.LastError) ? msgBeforeStart : ScanSession.LastError);
            ScanSession.LastError = null;

            // what the app already knows (also offline): the ingredient database, the brands and the product inventory
            try
            {
                _ = IngredientRepository.LoadAll();
                brands = await BrandRepository.LoadActive() ?? new List<BrandData>();
                products = await ProductRepository.LoadActive() ?? new List<ProductData>();
                // the text reader must never take a brand or a product name for an ingredient
                var labelNames = new List<string>();
                foreach (BrandData b in brands) if (b != null) labelNames.Add(b.name);
                foreach (ProductData p in products) if (p != null) { labelNames.Add(p.name); labelNames.Add(p.brandName); }
                IngredientTextParser.SetNotIngredients(labelNames);
                var brandNames = new List<string>();
                foreach (BrandData b in brands) if (b != null && !string.IsNullOrWhiteSpace(b.name)) brandNames.Add(b.name.Trim());
                ProductAiAdvisor.KnownBrands = brandNames;
            }
            catch (Exception e) { Debug.LogWarning("Scan: brands and products could not be loaded: " + e.Message); }
        }

        void OnDestroy() { if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted; }

        // ---------------------------------------------------------------------------------------
        //  Buttons
        // ---------------------------------------------------------------------------------------
        void OnMainButton()
        {
            if (leaving) return;
            if (!scanning)
            {
                scanning = true; steadyFor = 0f; sinceLastRead = 0f; readerWait = 0f;
                Say(msgStarting);
                if (startScanButton != null) startScanButton.interactable = false;
                if (cameraFeed != null) cameraFeed.StartCamera(); else OnCameraStarted(false, "No camera view in this scene.");
            }
            else if (ingredientText != null) Finish();          // the list is already read: go on without the product's name
            else readRequested = true;
        }

        void Skip()
        {
            if (leaving || !scanning || phase != Phase.Product) return;
            if (ingredientText != null) Finish(); else EnterIngredients();
        }

        void OnCameraStarted(bool ok, string message)
        {
            if (startScanButton != null) startScanButton.interactable = true;
            if (!ok)
            {
                scanning = false;
                Say(message + " You can type the ingredients instead.");
                return;
            }
            if (idlePlaceholder != null) idlePlaceholder.SetActive(false);
            if (startScanLabel != null) startScanLabel.text = readNowLabel;
            phase = Phase.Product;
            ShowPhase();
            Guide(ScanGuideOverlay.State.Searching);
            Say(msgPhase1);
            StartCoroutine(ReadLoop());
        }

        void StopScanning()
        {
            scanning = false;
            StopAllCoroutines();
            if (textOutlines != null) textOutlines.Clear();
            if (cameraFeed != null) cameraFeed.StopCamera();
        }

        // ---------------------------------------------------------------------------------------
        //  Reading
        // ---------------------------------------------------------------------------------------
        IEnumerator ReadLoop()
        {
            var tick = new WaitForSecondsRealtime(0.2f);
            while (scanning && !leaving)
            {
                yield return tick;
                if (cameraFeed == null || !cameraFeed.IsPlaying) continue;

                float motion = cameraFeed.MeasureMotion();
                sinceLastRead += 0.2f;
                steadyFor = motion < steadyMotionThreshold ? steadyFor + 0.2f : 0f;

                // an answer of the AI may have arrived in the meantime
                if (CollectAiAnswer() && (StopIfNotMakeup() || TryLeavePhase1())) yield break;
                // the AI looked several times without naming it: the FDA Philippines list is asked with what was read
                if (CollectFdaAnswer(lastLabelText) && (StopIfNotMakeup() || TryLeavePhase1())) yield break;

                if (cameraFeed.MeasureBrightness() < 0.06f) { Say(msgTooDark); continue; }

                bool timeToRead = readRequested || (steadyFor >= steadySeconds && sinceLastRead >= secondsBetweenReads);
                if (!timeToRead) continue;

                if (!OcrService.Shared.Ready)
                {
                    readerWait += 0.2f; readRequested = false;
                    Say(readerWait > 12f || !string.IsNullOrEmpty(OcrService.Shared.Error) ? msgReaderMissing : msgReaderLoading);
                    continue;
                }

                readRequested = false; sinceLastRead = 0f; steadyFor = 0f;
                Guide(ScanGuideOverlay.State.Reading);
                yield return null;                           // let the "Reading..." state show before the read

                Texture2D picture = cameraFeed.CaptureUpright(guide != null ? guide.Frame : null, readPictureSize);
                string colour = cameraFeed.DominantColourHex();
                if (!string.IsNullOrEmpty(colour)) lastColour = colour;
                string text = "";
                // APPEARANCE FIRST: while the text reader is still working, the look of the pack is compared with what this
                // phone remembers, and the picture goes to the AI. A pack that is known by its look needs no text at all.
                if (picture != null && phase == Phase.Product && !ProductKnown)
                {
                    RecogniseFromLook(picture, "");
                    if (ProductKnown) ShowIdentity(); else AskAi(picture, "");
                }
                if (picture != null) LookForBarcode(picture);
                if (picture != null)
                {
                    OcrRead read = OcrService.Shared.Read(picture);
                    float waitedForReader = 0f;
                    while (!read.Done && waitedForReader < 12f) { waitedForReader += Time.unscaledDeltaTime; yield return null; }
                    text = read.Text;
                    if (!scanning || leaving) { Destroy(picture); yield break; }
                    if (textOutlines != null) textOutlines.Show(read.Boxes);
                }
                if (TakeBarcode()) { if (picture != null) Destroy(picture); yield break; }      // the barcode gave the product AND its saved list

                bool isList = LooksLikeIngredients(text);
                if (isList)
                {
                    ingredientText = text; ingredientSource = "scan"; sources.Clear(); listFrom = "pack"; listWebBacked = false;
                    if (ingredientsCheck != null) ingredientsCheck.SetFound(string.Format(ingredientsFoundFormat, IngredientTextParser.Read(text).ingredients.Count));
                }

                if (phase == Phase.Ingredients)
                {
                    if (picture != null) Destroy(picture);
                    if (isList) { Finish(); yield break; }
                    Guide(ScanGuideOverlay.State.Searching);
                    Say(msgNotYet);
                    cameraFeed.Refocus();                    // nothing readable yet: the picture may be out of focus
                    continue;
                }

                // ---- phase 1: who made this, and what is it? ----
                RecogniseFromText(text);
                // the look of the packaging: a product this phone has scanned before is known again at once (also offline)
                if (!isList && picture != null) RecogniseFromLook(picture, text);
                if (!isList && picture != null) AskAi(picture, text);
                if (!isList) lastLabelText = text;                    // kept for the FDA Philippines search
                if (picture != null) Destroy(picture);
                ShowIdentity();
                if (StopIfNotMakeup(isList ? null : text) || TryLeavePhase1()) yield break;

                Guide(ScanGuideOverlay.State.Searching);
                if (ingredientText != null)
                {
                    if (startScanLabel != null) startScanLabel.text = finishLabel;
                    Say(msgListFirst);
                }
                else
                {
                    Say(aiTask != null ? msgAiLooking : msgProductNotYet);
                    if (aiTask == null) cameraFeed.Refocus();
                }
            }
        }

        bool LooksLikeIngredients(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            IngredientTextParser.ParsedList list = IngredientTextParser.Read(text);
            // enough ingredients the app knows...
            if (list.ingredients.Count >= minimumKnownIngredients) return true;
            // ...or at least one of them among enough names in the SHAPE of a list (the word "Ingredients", or commas between
            // the names): the front of a pack, with its brand and product name on lines of their own, is not an ingredient list
            return list.ingredients.Count >= 1 && list.Count >= minimumListedNames && IngredientTextParser.HasListShape(text);
        }

        // ---------------------------------------------------------------------------------------
        //  Phase 1: recognising the product
        // ---------------------------------------------------------------------------------------
        bool ProductKnown => identity.productName.Length > 0 || (identity.brandName.Length > 0 && identity.cosmeticType.Length > 0);

        /// <summary>
        /// MAKE-UP ONLY. As soon as the scan knows the product is something else (the AI, the FDA's record or the inventory
        /// said so, or the pack itself says "shampoo"), the scan stops: the user is told why, and can scan again or type
        /// the name in when the app got it wrong. Nothing is saved or sent to the inventory. True = the scan was stopped.
        /// </summary>
        bool StopIfNotMakeup(string frontText = null)
        {
            if (leaving || !scanning) return false;
            ProductScope.Verdict verdict = ProductScope.Judge(identity.cosmeticType, identity.productName);
            // nothing recognised yet (offline, or the AI has not answered): only unmistakable words on the pack count
            if (!verdict.IsNotMakeup && !string.IsNullOrWhiteSpace(frontText) && identity.productName.Length == 0 && identity.cosmeticType.Length == 0)
                verdict = ProductScope.JudgeLabel(frontText);
            if (!verdict.IsNotMakeup) return false;

            StopScanning();
            ResetScan();
            if (idlePlaceholder != null) idlePlaceholder.SetActive(true);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Hidden);
            if (startScanLabel != null) startScanLabel.text = scanAgainLabel;
            if (startScanButton != null) startScanButton.interactable = true;
            Say(ProductScope.StopMessage(verdict));
            return true;
        }

        /// <summary>Forgets everything the scan has found so far, so the next scan starts clean. (How often the AI was asked is kept.)</summary>
        void ResetScan()
        {
            identity.found = false; identity.confidence = 0f;
            identity.productName = identity.brandName = identity.cosmeticType = identity.shadeHex = identity.shadeName = identity.finish = "";
            knownProduct = null; ingredientText = null; ingredientSource = "scan"; lastColour = "";
            listFrom = ""; listWebBacked = false; sources.Clear();
            barcode = ""; barcodeSeen = null;
            aiTask = null; fdaTask = null; fdaAsked = false; lastLabelText = ""; lastLook = null;
            readRequested = false; lookingUp = false; steadyFor = 0f; sinceLastRead = 0f;
            phase = Phase.Product;
            ResetChecklist();
            SetWorking(false);
        }

        /// <summary>Compares the text on the product with the brands and products the app knows (works offline).</summary>
        void RecogniseFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string seen = " " + Plain(text) + " ";
            if (knownProduct == null)
            {
                // the phone's look-up table finds the product whose whole name stands in the text (words in any order,
                // small reading mistakes forgiven); no reading through the whole list
                List<ProductIndex.Candidate> found = ProductRepository.Index.InText(text, 1);
                if (found.Count > 0 && found[0].exactName && found[0].score >= ProductIndex.Sure)
                {
                    ProductData product = found[0].product;
                    knownProduct = product;
                    identity.productName = product.name.Trim();
                    if (!string.IsNullOrWhiteSpace(product.brandName)) identity.brandName = product.brandName.Trim();
                    if (!string.IsNullOrWhiteSpace(product.cosmeticType)) identity.cosmeticType = product.cosmeticType.Trim();
                }
            }
            if (identity.brandName.Length == 0)
                foreach (BrandData brand in brands)
                {
                    string name = Plain(brand != null ? brand.name : "");
                    if (name.Length < 3 || !seen.Contains(" " + name + " ")) continue;
                    identity.brandName = brand.name.Trim();
                    break;
                }
        }

        /// <summary>Lower case, with everything that is not a letter or a digit turned into single spaces.</summary>
        static string Plain(string text)
        {
            var sb = new StringBuilder((text ?? "").Length);
            bool space = true;
            foreach (char c in text ?? "")
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Shows the picture to the AI (online only, a few times per scan at most). The answer is picked up by CollectAiAnswer.</summary>
        void AskAi(Texture2D picture, string text)
        {
            if (!aiRecognisesProduct || aiTask != null || ProductKnown && identity.shadeHex.Length > 0) return;
            if (aiLooks >= maximumAiLooks || Time.unscaledTime - lastAiLook < secondsBetweenAiLooks) return;
            if (!ConnectivityMonitor.Online || !ProductAiAdvisor.Available) return;
            byte[] jpeg;
            try { jpeg = picture.EncodeToJPG(aiPictureQuality); } catch (Exception e) { Debug.LogWarning("Scan: " + e.Message); return; }
            aiLooks++; lastAiLook = Time.unscaledTime;
            SetWorking(true);
            aiTask = ProductAiAdvisor.Identify(jpeg, text);
        }

        [Header("FDA Philippines (when the AI cannot name the product)")]
        [Tooltip("After this many looks of the AI without a product name, the FDA Philippines list is searched with the text that was read")]
        [SerializeField] private int fdaAfterAiLooks = 3;
        Task<PhFdaVerification.Result> fdaTask;
        string lastLabelText = "";
        bool fdaAsked;

        /// <summary>The AI looked several times and still has no name: ask the FDA Philippines list. True when it named the product.</summary>
        bool CollectFdaAnswer(string text)
        {
            if (fdaTask == null)
            {
                if (fdaAsked || ProductKnown || aiTask != null || aiLooks < Mathf.Max(1, fdaAfterAiLooks) || !ConnectivityMonitor.Online || string.IsNullOrWhiteSpace(text)) return false;
                fdaAsked = true;
                SetWorking(true);
                fdaTask = PhFdaVerification.FindInText(text);
                return false;
            }
            if (!fdaTask.IsCompleted) return false;
            Task<PhFdaVerification.Result> task = fdaTask;
            fdaTask = null;
            SetWorking(false);
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null || !task.Result.found) return false;
            PhFdaVerification.Result fda = task.Result;
            var names = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
            if (identity.productName.Length == 0) identity.productName = names.ToTitleCase(fda.productName.ToLowerInvariant());
            if (identity.brandName.Length == 0) identity.brandName = names.ToTitleCase(fda.brandName.ToLowerInvariant());
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = PhFdaVerification.TypeFromUse(fda.intendedUse);
            ShowIdentity();
            return true;
        }

        /// <summary>True when an answer of the AI has just been taken over.</summary>
        bool CollectAiAnswer()
        {
            if (aiTask == null || !aiTask.IsCompleted) return false;
            Task<ProductAiAdvisor.Identity> task = aiTask;
            aiTask = null;
            SetWorking(false);
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null || !task.Result.found || task.Result.confidence < minimumAiConfidence) return false;
            ProductAiAdvisor.Identity seen = task.Result;
            // what the app's own lists said stays; the AI fills the gaps
            if (identity.productName.Length == 0) identity.productName = seen.productName;
            if (identity.brandName.Length == 0) identity.brandName = seen.brandName;
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = seen.cosmeticType;
            if (seen.shadeHex.Length > 0) identity.shadeHex = seen.shadeHex;
            if (seen.shadeName.Length > 0) identity.shadeName = seen.shadeName;
            if (seen.finish.Length > 0) identity.finish = seen.finish;
            ShowIdentity();
            return true;
        }

        void SetWorking(bool on)
        {
            foreach (ScanCheckItem item in new[] { productCheck, brandCheck, typeCheck, shadeCheck }) if (item != null) item.SetWorking(on);
        }

        void ShowIdentity()
        {
            if (productCheck != null && identity.productName.Length > 0) productCheck.SetFound(identity.productName);
            if (brandCheck != null && identity.brandName.Length > 0) brandCheck.SetFound(identity.brandName);
            if (typeCheck != null && identity.cosmeticType.Length > 0) typeCheck.SetFound(identity.cosmeticType);

            // the shade: what the AI saw, otherwise (for a colour cosmetic) the strongest colour in the picture
            string hex = identity.shadeHex;
            if (hex.Length == 0 && ProductKnown && ShadeAdvisor.IsColourCosmetic(identity.cosmeticType)) hex = lastColour;
            hex = ShadeAdvisor.ShadeFor(identity.cosmeticType, hex);          // a face powder is white, whatever colour its box has
            if (ShadeAdvisor.IsFacePowder(identity.cosmeticType)) identity.shadeHex = hex;
            if (hex.Length == 0) return;
            string words = identity.shadeName.Length > 0 ? identity.shadeName : hex;
            if (identity.finish.Length > 0) words += ", " + identity.finish;
            if (shadeCheck != null) shadeCheck.SetFound(words);
            if (shadeSwatch != null) { shadeSwatch.gameObject.SetActive(true); shadeSwatch.color = ShadeAdvisor.Parse(hex, Color.gray); }
        }

        /// <summary>Phase 1 is done when the product is known. Returns true when the scan is finished altogether.</summary>
        PackagingMemory.Look lastLook;          // what the front of the pack looked like in the latest picture

        /// <summary>Compares the picture with the packagings this phone remembers; fills in the product when it knows it.</summary>
        void RecogniseFromLook(Texture2D picture, string text)
        {
            if (!PackagingMemory.Enabled) return;
            lastLook = PackagingMemory.SignatureOf(picture);
            if (ProductKnown || lastLook == null) return;
            PackagingMemory.Look known = PackagingMemory.Recall(lastLook, text);
            if (known == null) return;
            if (identity.productName.Length == 0) identity.productName = known.productName;
            if (identity.brandName.Length == 0) identity.brandName = known.brandName;
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = known.cosmeticType;
            if (identity.shadeHex.Length == 0) identity.shadeHex = known.shadeHex;
            if (identity.shadeName.Length == 0) identity.shadeName = known.shadeName;
            if (identity.finish.Length == 0) identity.finish = known.finish;
        }

        bool TryLeavePhase1()
        {
            if (phase != Phase.Product || !ProductKnown) return false;
            // the product has a name now: remember what its packaging looks like for next time
            if (lastLook != null) { PackagingMemory.Remember(lastLook, identity.productName, identity.brandName, identity.cosmeticType, identity.shadeHex, identity.shadeName, identity.finish); lastLook = null; }
            if (ingredientText != null) { Finish(); return true; }
            EnterIngredients();
            return false;
        }

        [Header("Ingredient list by itself")]
        [Tooltip("On = as soon as the product is recognised, its ingredient list is looked up without a tap: first in the app's own inventory, then (online) on the maker's official website and other credible sources. The user can still scan the list on the pack; whichever comes first is used.")]
        [SerializeField] private bool lookUpByItself = true;

        void EnterIngredients()
        {
            phase = Phase.Ingredients;
            ShowPhase();
            Guide(ScanGuideOverlay.State.Searching);
            Say(msgPhase2);
            steadyFor = 0f; sinceLastRead = 0f;
            // the product has a name: fetch its ingredient list right away, while the user turns the pack round
            if (lookUpByItself && identity.productName.Length > 0 && ingredientText == null) LookUp();
        }

        void ShowPhase()
        {
            if (phase1Group != null) phase1Group.alpha = phase == Phase.Product ? 1f : idlePhaseAlpha;
            if (phase2Group != null) phase2Group.alpha = phase == Phase.Ingredients || ingredientText != null ? 1f : idlePhaseAlpha;
            if (skipButton != null) skipButton.gameObject.SetActive(phase == Phase.Product);
        }

        void ResetChecklist()
        {
            foreach (ScanCheckItem item in new[] { productCheck, brandCheck, typeCheck, shadeCheck, ingredientsCheck }) if (item != null) item.SetPending();
            if (shadeSwatch != null) shadeSwatch.gameObject.SetActive(false);
            if (phase1Group != null) phase1Group.alpha = 1f;
            if (phase2Group != null) phase2Group.alpha = 1f;
        }

        void Guide(ScanGuideOverlay.State state)
        {
            if (guide == null) return;
            guide.SetState(state);
            if (state == ScanGuideOverlay.State.Searching) guide.SetHint(phase == Phase.Product ? hintProduct : hintIngredients);
        }

        // ---------------------------------------------------------------------------------------
        //  Phase 2 without a list on the product: look the list up
        // ---------------------------------------------------------------------------------------
        async void LookUp()
        {
            if (leaving || lookingUp) return;
            if (identity.productName.Length == 0) { Say(msgLookUpNeedsName); return; }
            lookingUp = true;
            if (lookUpButton != null) lookUpButton.interactable = false;
            if (lookUpLabel != null) lookUpLabel.text = lookUpWorkingLabel;
            if (ingredientsCheck != null) ingredientsCheck.SetWorking(true);
            Say(msgLookingUp);
            try
            {
                // 1. the app's own inventory (also offline)
                ProductData product = knownProduct != null && knownProduct.ingredients != null && knownProduct.ingredients.Count >= 3 ? knownProduct : InventoryProductFor(identity.productName, identity.brandName);
                if (product != null && product.ingredients != null && product.ingredients.Count >= 3)
                {
                    UseLookedUpList(product.ingredients, ListPagesOf(product), "inventory", false);
                    return;
                }

                // 2. online, from credible sources
                if (!ConnectivityMonitor.Online || !ProductAiAdvisor.Available) { Say(msgLookUpOffline); return; }
                ProductAiAdvisor.IngredientLookup found = await ProductAiAdvisor.FindIngredients(identity.productName, identity.brandName, identity.cosmeticType);
                if (this == null || leaving) return;
                if (found == null || !found.found) { await SayWhatFdaKnows(); return; }

                // additives that hide behind words like "Fragrance": kept on the phone and sent to the shared database
                foreach (ProductAiAdvisor.HiddenAdditive hidden in found.hidden)
                {
                    try { HiddenAdditives.Learn(hidden.term, hidden.substances, hidden.note, found.sources, found.webSearched, found.model); }
                    catch (Exception e) { Debug.LogWarning("Hidden ingredients were not saved: " + e.Message); }
                }
                UseLookedUpList(found.ingredients, found.sources, "web", found.webSearched);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Ingredient look-up: " + e.Message);
                if (this != null) Say(msgNoListFound);
            }
            finally
            {
                lookingUp = false;
                if (this != null)
                {
                    if (lookUpButton != null) lookUpButton.interactable = true;
                    if (lookUpLabel != null) lookUpLabel.text = lookUpIdleLabel;
                    if (ingredientsCheck != null) ingredientsCheck.SetWorking(false);
                }
            }
        }

        /// <summary>Asks the phone's barcode reader to look at the picture (only until a barcode has been seen).</summary>
        void LookForBarcode(Texture2D picture)
        {
            if (!readBarcode || barcode.Length > 0) return;
            if (!barcodeReaderAsked) { barcodeReaderAsked = true; MlKitBarcodeReader.TryCreate(out barcodeReader); }
            if (barcodeReader != null) barcodeReader.Read(picture, code => { if (!string.IsNullOrEmpty(code)) barcodeSeen = code; });
        }

        /// <summary>
        /// Takes over a barcode the reader has seen. A product in the inventory with that barcode is the product being
        /// scanned: its name, brand and kind are filled in. Returns true when its saved ingredient list was used as well,
        /// which finishes the scan.
        /// </summary>
        bool TakeBarcode()
        {
            string seen = barcodeSeen;
            if (string.IsNullOrEmpty(seen) || barcode.Length > 0) return false;
            barcodeSeen = null;
            string key = MlKitBarcodeReader.Key(seen);
            if (key.Length == 0) return false;
            barcode = seen.Trim();
            ProductData p = ProductRepository.Index.ByBarcode(barcode);            // the phone's look-up table: no reading through the list
            if (p != null)
            {
                knownProduct = p;
                identity.productName = p.name.Trim();
                if (!string.IsNullOrWhiteSpace(p.brandName)) identity.brandName = p.brandName.Trim();
                if (!string.IsNullOrWhiteSpace(p.cosmeticType)) identity.cosmeticType = p.cosmeticType.Trim();
                ShowIdentity();
                // its list is saved already: reuse it instead of reading the pack again
                if (ingredientText == null && !leaving && p.ingredients != null && p.ingredients.Count >= 3)
                {
                    UseLookedUpList(p.ingredients, ListPagesOf(p), "inventory", false);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The inventory's entry for a product that was recognised, also when the names are not written exactly alike: the
        /// brand must agree, and one name must hold (nearly) all the words of the other. Of several, the one that has an
        /// ingredient list and shares the most words wins. Null when the inventory does not have it.
        /// </summary>
        ProductData InventoryProductFor(string productName, string brandName)
        {
            ProductIndex index = ProductRepository.Index;
            ProductData exact = index.Exact(brandName, productName);
            if (exact != null && exact.ingredients != null && exact.ingredients.Count >= 3) return exact;
            // not written exactly alike: the closest names, as long as the index is sure and the brand agrees
            ProductData best = exact;
            foreach (ProductIndex.Candidate candidate in index.Search(brandName, productName, 3))
            {
                if (candidate.score < ProductIndex.Sure) break;
                bool hasList = candidate.product.ingredients != null && candidate.product.ingredients.Count >= 3;
                if (best == null || (hasList && (best.ingredients == null || best.ingredients.Count < 3))) best = candidate.product;
            }
            return best;
        }

        /// <summary>
        /// The pages an inventory product's ingredient list came from, as the website recorded them. The FDA's own page is
        /// left out: it holds no ingredient lists (the FDA record has its own badge on the result screen).
        /// </summary>
        static List<string> ListPagesOf(ProductData product)
        {
            var pages = new List<string>();
            if (product != null && product.sources != null)
                foreach (string source in product.sources)
                    if (!string.IsNullOrWhiteSpace(source) && source.IndexOf("verification.fda.gov.ph", StringComparison.OrdinalIgnoreCase) < 0) pages.Add(source.Trim());
            return pages;
        }

        /// <summary>
        /// No ingredient list could be found for the product. The user is at least told what the FDA Philippines list says
        /// about it (registered or not), and what to do next: scan the list on the pack, or type it in.
        /// </summary>
        async Task SayWhatFdaKnows()
        {
            if (!ConnectivityMonitor.Online || !PhFdaVerification.Enabled) { Say(msgNoListFound); return; }
            Say(msgNoListCheckingFda);
            PhFdaVerification.Result fda = await PhFdaVerification.Find(identity.productName, identity.brandName, identity.shadeName);
            if (this == null || leaving || ingredientText != null) return;        // the list was read from the pack in the meantime
            string known = fda.Status == PhFdaVerification.Registered ? fdaSaysRegistered : fda.Status == PhFdaVerification.Expired ? fdaSaysExpired
                         : fda.Status == PhFdaVerification.Similar ? fdaSaysSimilar : fda.Status == PhFdaVerification.None ? fdaSaysNone : "";
            Say(known.Length > 0 ? string.Format(msgNoListButFda, known) : msgNoListFound);
        }

        /// <param name="kind">"inventory" (the app's own product list) or "web" (looked up online)</param>
        /// <param name="webBacked">a list from the web came with pages a live web search really opened</param>
        void UseLookedUpList(List<string> ingredients, List<string> from, string kind, bool webBacked)
        {
            ingredientText = string.Join(", ", ingredients);
            ingredientSource = "lookup";
            listFrom = kind; listWebBacked = webBacked;
            sources = new List<string>(from ?? new List<string>());
            if (ingredientsCheck != null) ingredientsCheck.SetFound(string.Format(ingredientsLookedUpFormat, ingredients.Count));
            Finish();
        }

        // ---------------------------------------------------------------------------------------
        //  Handing over to the analysis
        // ---------------------------------------------------------------------------------------
        void Finish()
        {
            if (leaving || ingredientText == null) return;
            ScanSession.PendingText = ingredientText; ScanSession.PendingSource = ingredientSource;
            ScanSession.PendingShadeHex = identity.shadeHex.Length > 0 ? identity.shadeHex : lastColour;
            ScanSession.PendingProductName = identity.productName; ScanSession.PendingBrandName = identity.brandName;
            ScanSession.PendingCosmeticType = identity.cosmeticType;
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            ScanSession.PendingSources = new List<string>(sources);
            ScanSession.PendingBarcode = barcode;
            ScanSession.PendingListFrom = listFrom; ScanSession.PendingListWebBacked = listWebBacked;
            StartCoroutine(Recognised());
        }

        IEnumerator Recognised()
        {
            leaving = true;
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Detected);
            if (phase1Group != null) phase1Group.alpha = 1f;
            if (phase2Group != null) phase2Group.alpha = 1f;
            Say(msgDone);
            if (startScanButton != null) startScanButton.interactable = false;
            yield return new WaitForSecondsRealtime(recognisedHoldSeconds);
            if (cameraFeed != null) cameraFeed.StopCamera();
            SceneNavigator.GoViaLoading(resultScene, loadingMessage, ScanAnalysisPipeline.Run);
        }

        // ---------------------------------------------------------------------------------------
        //  Typing the ingredients in
        // ---------------------------------------------------------------------------------------
        void OpenManual()
        {
            if (leaving || manualPanel == null) return;
            if (manualFeedbackText != null) manualFeedbackText.text = "";
            // what the scan already found is filled in, so the user only types what is missing
            if (manualProductInput != null && string.IsNullOrWhiteSpace(manualProductInput.text)) manualProductInput.text = identity.productName;
            if (manualBrandInput != null && string.IsNullOrWhiteSpace(manualBrandInput.text)) manualBrandInput.text = identity.brandName;
            if (manualTypeDropdown != null)
            {
                var options = new List<string> { manualTypeNone };
                foreach (string kind in manualTypes ?? new string[0])
                    if (ProductScope.IsMakeup(kind, "")) options.Add(kind);          // a scene built earlier may still list skin care kinds
                int keep = manualTypeDropdown.options.Count == options.Count ? manualTypeDropdown.value : 0;
                manualTypeDropdown.ClearOptions(); manualTypeDropdown.AddOptions(options);
                int found = options.FindIndex(o => string.Equals(o, identity.cosmeticType, StringComparison.OrdinalIgnoreCase));
                manualTypeDropdown.SetValueWithoutNotify(keep > 0 ? keep : Mathf.Max(0, found));
                manualTypeDropdown.RefreshShownValue();
            }
            if (manualInput != null && string.IsNullOrWhiteSpace(manualInput.text) && ingredientText != null) manualInput.text = ingredientText;
            manualPanel.Show();
        }

        void AnalyseManual()
        {
            string text = manualInput != null ? manualInput.text : "";
            if (IngredientTextParser.Parse(text).Count < 2)
            {
                if (manualFeedbackText != null) manualFeedbackText.text = msgNeedIngredients;
                return;
            }
            leaving = true;
            StopScanning();
            ScanSession.PendingText = text; ScanSession.PendingSource = "manual"; ScanSession.PendingShadeHex = identity.shadeHex;
            ScanSession.PendingListFrom = "typed"; ScanSession.PendingListWebBacked = false;
            // what the user typed comes first; what phase 1 found fills the rest
            string typedName = manualProductInput != null ? manualProductInput.text.Trim() : "";
            string typedBrand = manualBrandInput != null ? manualBrandInput.text.Trim() : "";
            string typedType = manualTypeDropdown != null && manualTypeDropdown.value > 0 ? manualTypeDropdown.options[manualTypeDropdown.value].text : "";
            ScanSession.PendingProductName = typedName.Length > 0 ? typedName : identity.productName;
            ScanSession.PendingBrandName = typedBrand.Length > 0 ? typedBrand : identity.brandName;
            ScanSession.PendingCosmeticType = typedType.Length > 0 ? typedType : identity.cosmeticType;
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            SceneNavigator.GoViaLoading(resultScene, loadingMessage, ScanAnalysisPipeline.Run);
        }

        void Say(string message) { if (statusText != null) statusText.text = message; }
    }
}
