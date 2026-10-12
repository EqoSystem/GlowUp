using System;
using System.Collections;
using System.Collections.Generic;
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
    /// AI Smart Scan scene: HOLD TO SCAN.
    ///
    /// The user holds the round button and shows the product; the camera gives several pictures a second and a ring of
    /// five segments round the button fills as the app sees the product's front, its ingredient list, its barcode (or a
    /// registration or batch code), its shade and its other sides. Nobody has to tap once per side. Letting go only
    /// pauses: the ring keeps what it has.
    ///
    ///  - A picture that is too dark, blurred, burnt out by glare or taken while the phone moves is thrown away before
    ///    it is read (FrameQuality). The hint on the camera picture says what to do ("Hold steadier").
    ///  - What the pictures show is put together (ScanEvidence, ScanTextMerger): each line of text in its best reading,
    ///    the ingredient list in the order of the label, even when it passes the camera in pieces.
    ///  - A product the app already knows (by its barcode, its name or the look of its pack) finishes as soon as it is
    ///    recognised: its ingredient list is stored. A new product needs the whole ring; "Finish" is offered as soon as
    ///    the product's front and its ingredient list are read.
    ///  - RECOGNISING, fastest first: the barcode, then the whole name read on the pack (both name the product at
    ///    once), then the look of a pack this phone has scanned before (the app asks "Is this ...?" and the user says
    ///    yes or no), then a shortlist "Is it one of these?" of products whose name can partly be read, with a box to
    ///    type the name. The app never guesses: what it is not sure of, it asks.
    ///  - The AI is asked ONCE per scan, at the end and only when none of that named the product: it gets the best
    ///    picture of each side and all the text. When it cannot tell either, the shortlist opens again with the ways
    ///    on: type the name, scan again or finish without a name; there is never a dead end.
    ///  - GlowUp checks make-up only: as soon as the product is known to be something else, the scan stops and says so.
    ///
    /// "Start over" offers "the same product again" (the name is kept, the pictures are taken anew) and "a different
    /// product" (everything is forgotten). "Type the product in instead" is for packs the camera cannot read, and
    /// "Look it up" finds the ingredient list of a product that has none on it.
    /// Every text, limit and timing below is an Inspector field; the numbers of the quality gate and the ring are in
    /// the Scan Rules asset (GlowUp > Open Scan Rules).
    /// </summary>
    public class SmartScanFlowController : MonoBehaviour
    {
        enum Stage { Idle, Starting, Scanning, Recognising, Leaving }

        [Header("Camera")]
        [SerializeField] private CameraFeedController cameraFeed;
        [SerializeField] private ScanGuideOverlay guide;
        [Tooltip("Shown in the camera area while the camera is off")]
        [SerializeField] private GameObject idlePlaceholder;
        [Tooltip("Draws an outline round each line of text that was read. Optional.")]
        [SerializeField] private TextOutlineOverlay textOutlines;

        [Header("The hold button and its ring")]
        [SerializeField] private HoldToScanButton holdButton;
        [Tooltip("The words under the icon of the hold button")]
        [SerializeField] private TextMeshProUGUI holdLabel;
        [SerializeField] private ScanRingView ring;
        [Tooltip("A short buzz when a segment of the ring becomes full, and a longer one when the scan is done")]
        [SerializeField] private bool vibrate = true;

        [Header("Buttons and texts")]
        [Tooltip("Appears when the product's front and its ingredient list are read: goes on without the rest of the ring")]
        [SerializeField] private Button finishButton;
        [SerializeField] private TextMeshProUGUI finishText;
        [Tooltip("Appears once the ring holds anything: scan again from the start")]
        [SerializeField] private Button startOverButton;
        [SerializeField] private Button manualEntryButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("\"Start over\": the same product, or a different one?")]
        [SerializeField] private OverlayPanel startOverPanel;
        [SerializeField] private Button sameProductButton;
        [SerializeField] private Button differentProductButton;

        [Header("What was found (the checklist)")]
        [SerializeField] private ScanCheckItem productCheck;
        [SerializeField] private ScanCheckItem brandCheck;
        [SerializeField] private ScanCheckItem typeCheck;
        [SerializeField] private ScanCheckItem shadeCheck;
        [Tooltip("A small dot that takes the colour of the shade that was found")]
        [SerializeField] private Image shadeSwatch;
        [SerializeField] private ScanCheckItem ingredientsCheck;
        [Tooltip("For a product without an ingredient list on it: looks the list up")]
        [SerializeField] private Button lookUpButton;
        [Tooltip("The words on that button (they change while it is working)")]
        [SerializeField] private TextMeshProUGUI lookUpLabel;
        [SerializeField] private string lookUpIdleLabel = "Look it up";
        [SerializeField] private string lookUpWorkingLabel = "Looking it up...";

        [Header("Recognising a product without the AI")]
        [Tooltip("The question on the camera picture, \"Is this ...?\", for a pack this phone has scanned before")]
        [SerializeField] private ScanProductQuestion productQuestion;
        [Tooltip("The pop-up \"Is it one of these?\" with its search box")]
        [SerializeField] private ScanShortlistView shortlist;
        [SerializeField] private string shortlistTitle = "Is it one of these?";
        [SerializeField] private string shortlistMessage = "We are not sure which product this is. Tap it, or type its name.";
        [SerializeField] private string shortlistNoneLabel = "None of these";
        [Tooltip("The title of that pop-up after the AI could not name the product either, and when a list is to be looked up for a product without a name")]
        [SerializeField] private string notSureTitle = "Which product is it?";
        [SerializeField] private string notSureNoneLabel = "Finish without a name";
        [SerializeField] private string msgLookUpWhich = "Which product should we look up? Tap it, or type its name.";

        [Header("AI (asked once, at the end, only when the app cannot name the product itself)")]
        [Tooltip("The project's AI settings. Empty = the ones the loading scene uses.")]
        [SerializeField] private GeminiApiConfig aiConfig;
        [Tooltip("On = the AI is shown the best pictures when the app's own lists could not name the product")]
        [SerializeField] private bool aiRecognisesProduct = true;
        [Tooltip("How many pictures the AI gets at most (the best one of each side)")]
        [Range(1, 4)] [SerializeField] private int aiPictures = 3;
        [Tooltip("Longest side, in pixels, of a picture the AI gets (smaller = faster and cheaper)")]
        [SerializeField] private int aiPictureSize = 900;
        [Tooltip("JPG quality of those pictures (lower = smaller and faster)")]
        [Range(40, 95)] [SerializeField] private int aiPictureQuality = 80;
        [Tooltip("The AI's answer counts when it is at least this sure (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float minimumAiConfidence = 0.45f;

        [Header("Reading")]
        [Tooltip("How many pictures a second are looked at while the button is held (the text reader takes the newest one whenever it is free)")]
        [Range(2f, 10f)] [SerializeField] private float looksPerSecond = 6f;
        [Tooltip("Longest side, in pixels, of the picture given to the text reader (smaller = faster)")]
        [SerializeField] private int readPictureSize = 1100;
        [Tooltip("After this many pictures in a row that looked out of focus, one is read anyway (and the camera is asked to focus again): the text reader then decides")]
        [SerializeField] private int blurrySkipsBeforeReadingAnyway = 6;
        [Tooltip("A paused scan is forgotten after this many seconds, and the camera is switched off")]
        [SerializeField] private float pauseResetSeconds = 30f;
        [Tooltip("Held this long without the ring moving on: the user is offered to start over or type the product in")]
        [SerializeField] private float stallSeconds = 12f;
        [Tooltip("How long the 'Got it' message stays before the loading scene opens")]
        [SerializeField] private float recognisedHoldSeconds = 1.1f;

        [Header("Barcode")]
        [Tooltip("On = the camera also looks for the product's barcode (on the phone itself, no internet). A product whose barcode is in the inventory is recognised at once, with its saved ingredient list.")]
        [SerializeField] private bool readBarcode = true;

        [Header("Scenes")]
        [SerializeField] private string resultScene = SceneNames.AiResult;
        [SerializeField] private string backScene = SceneNames.Dashboard;

        [Header("The hold button's words")]
        [SerializeField] private string holdIdleLabel = "Hold to scan";
        [SerializeField] private string holdActiveLabel = "Scanning";
        [SerializeField] private string holdLockedLabel = "Tap to pause";
        [SerializeField] private string finishNormalLabel = "Finish";
        [Tooltip("The Finish button's words after nobody could name the product (it must stay short: the button is narrow)")]
        [SerializeField] private string finishUnnamedLabel = "Finish anyway";

        [Header("Status line")]
        [Tooltip("{0} = how many of the five parts are done")]
        [SerializeField] private string statusHolding = "Holding, {0} of 5";
        [SerializeField] private string statusPaused = "Paused at {0} of 5. Hold the button to go on.";
        [SerializeField] private string statusIdle = "Hold the button and show the product. A quick tap keeps it scanning.";
        [SerializeField] private string statusDone = "Got it! Analysing the product now...";

        [Header("Hints on the camera picture")]
        [SerializeField] private string hintDark = "It is too dark. Turn on the flashlight or move to the light.";
        [SerializeField] private string hintMoving = "Hold steadier";
        [SerializeField] private string hintGlare = "Tilt slightly, there is glare";
        [SerializeField] private string hintBlurry = "Move a little back so the text is sharp";
        [SerializeField] private string hintFront = "Show the front: the brand and the product's name";
        [SerializeField] private string hintIngredients = "Turn to the back: show the ingredient list";
        [SerializeField] private string hintIngredientsMore = "Keep the list in view, moving slowly along it";
        [SerializeField] private string hintCodes = "Show the barcode (on the bottom or the back)";
        [SerializeField] private string hintShade = "Show the shade's name or number";
        [SerializeField] private string hintAround = "Turn the product slowly to show its other sides";
        [SerializeField] private string hintAllDone = "Everything is read";

        [Header("Labels of the checklist")]
        [Tooltip("{0} = how many ingredients were read")]
        [SerializeField] private string ingredientsFoundFormat = "{0} ingredients read";
        [SerializeField] private string ingredientsReadingFormat = "{0} ingredients so far";
        [SerializeField] private string ingredientsLookedUpFormat = "{0} ingredients found";

        [Header("Messages")]
        [SerializeField] private string msgStarting = "Starting the camera...";
        [Tooltip("Said while the AI is asked. Honest about the wait: a product the app has never seen takes the slow way the first time.")]
        [SerializeField] private string msgAiLooking = "This product is new to GlowUp. Recognising it takes a little longer the first time...";
        [Tooltip("The app and the AI could not name the product")]
        [SerializeField] private string msgNotSure = "We could not name this product. Type its name, start over, or tap Finish anyway.";
        [Tooltip("The AI has been asked as often as it may be in one visit of this screen")]
        [SerializeField] private string msgAiLimit = "The AI has been asked as often as it may be for now. Type the product's name, or tap Finish anyway.";
        [Tooltip("Shown when the ingredient list was looked up online as often as it may be in one visit of this screen")]
        [SerializeField] private string msgLookUpLimit = "This product's list was already looked up. Scan the list on the pack, or type it in.";
        [SerializeField] private string msgNeedList = "The ingredient list is not read yet. Show it to the camera, or tap Look it up.";
        [SerializeField] private string msgStalled = "Not getting further? Tap Start over, or type the product in instead.";
        [SerializeField] private string msgPauseReset = "The scan was paused for a while, so it was put back to the start.";
        [SerializeField] private string msgDifferentProduct = "That is a different product: a new scan was started for it.";
        [SerializeField] private string msgSameAgain = "Scanning the same product again. Its name is kept.";
        [SerializeField] private string msgStartedOver = "Ready for a new product.";
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

        // ---------------------------------------------------------------------------------------
        Stage stage = Stage.Idle;
        FrameReader reader;
        readonly ScanEvidence evidence = new ScanEvidence();
        readonly ScanGuidance.Words words = new ScanGuidance.Words();

        // the picture that is being looked at
        int[] pixels; int pixelsW, pixelsH;
        byte[] overview, overviewBefore, detail;
        const int OverviewW = 64, OverviewH = 48, DetailSize = 160;
        float nextLook, pausedFor, sinceProgress, sentSharpness, sentAt;
        int blurrySkips, differentSeen;
        bool readingOut, stalledSaid, finishUnnamed, lookingUp;
        FrameQuality.Fault fault = FrameQuality.Fault.None;
        string lastColour = "", lastHint = "";
        string shownStatus;       // the status line that is on screen (null = a message is there instead)
        string messageOver;       // a message was said while this status held: it stays until the status changes
        const float ReaderAnswerSeconds = 5f;     // a picture the text reader has not answered after this long is given up

        // the answer of the text reader (written on its own thread, taken over in Update)
        readonly object answerLock = new object();
        FrameReading answerReading; bool answerWaiting;

        // the best picture of each zone (for the AI, when it is needed at the end)
        class Kept { public int[] pixels; public int width, height; public float score; }
        readonly Kept[] best = new Kept[ScanZones.Count];

        // what has been found so far
        readonly ProductAiAdvisor.Identity identity = new ProductAiAdvisor.Identity();
        ProductData knownProduct;
        ScanRecord rescanOf;                      // "Scan the same product again": the saved scan this one takes the place of
        readonly List<PackagingMemory.Match> lookMatches = new List<PackagingMemory.Match>();     // packs this phone remembers that look like this one
        readonly HashSet<string> declined = new HashSet<string>();       // products the user said "no" to in this scan
        bool shortlistAnswered;                   // the shortlist was shown in this attempt and the user said "none of these"
        string frontChecked = "";                 // the text of the front the product list was last asked about
        string barcode = "";
        string ingredientText, ingredientSource = "scan";
        string listFrom = "";                     // where the ingredient list came from: "pack", "inventory", "web" or "typed"
        bool listWebBacked;                       // a list looked up online came with pages a live web search really opened
        List<string> sources = new List<string>();
        List<BrandData> brands = new List<BrandData>();

        bool ProductKnown => identity.productName.Length > 0 || (identity.brandName.Length > 0 && identity.cosmeticType.Length > 0);

        void Awake()
        {
            if (holdButton != null) { holdButton.Pressed += OnHoldPressed; holdButton.Released += OnHoldReleased; }
            if (finishButton != null) finishButton.onClick.AddListener(OnFinishTapped);
            if (startOverButton != null) startOverButton.onClick.AddListener(OnStartOverTapped);
            if (sameProductButton != null) sameProductButton.onClick.AddListener(() => { ClosePanel(startOverPanel); StartOver(true, msgSameAgain); });
            if (differentProductButton != null) differentProductButton.onClick.AddListener(() => { ClosePanel(startOverPanel); StartOver(false, msgStartedOver); });
            if (manualEntryButton != null) manualEntryButton.onClick.AddListener(OpenManual);
            if (manualAnalyseButton != null) manualAnalyseButton.onClick.AddListener(AnalyseManual);
            if (lookUpButton != null) lookUpButton.onClick.AddListener(LookUp);
            if (backButton != null) backButton.onClick.AddListener(() => { StopCamera(); SceneNavigator.Go(backScene); });
            if (cameraFeed != null) cameraFeed.Started += OnCameraStarted;
            if (aiConfig != null) ProductAiAdvisor.Config = aiConfig;
            if (shortlist != null) shortlist.Search = (name, brand) => ScanShortlist.Typed(ProductRepository.Index, name, brand);
            CopyWords();
        }

        void CopyWords()
        {
            words.holding = statusHolding; words.paused = statusPaused; words.notStarted = statusIdle; words.done = statusDone;
            words.dark = hintDark; words.moving = hintMoving; words.glare = hintGlare; words.blurry = hintBlurry;
            words.front = hintFront; words.ingredients = hintIngredients; words.ingredientsMore = hintIngredientsMore;
            words.codes = hintCodes; words.shade = hintShade; words.around = hintAround;
        }

        async void Start()
        {
            AiBudget.NewSession();                           // the AI's few questions are counted per visit of this screen
            reader = FrameReader.Create();                   // loads in the background while the user reads the instructions
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Hidden);
            if (idlePlaceholder != null) idlePlaceholder.SetActive(true);
            ResetChecklist();
            if (ring != null) ring.Clear();

            // "Scan the same product again" on the result screen: the product keeps its name, the pictures are taken anew,
            // and the new result will take the place of that scan (no second entry in Recent Scans, no second count)
            rescanOf = ScanSession.RescanOf;
            ScanSession.RescanOf = null;
            ScanSession.ClearPending();
            if (rescanOf != null)
            {
                identity.productName = (rescanOf.productName ?? "").Trim(); identity.brandName = (rescanOf.brandName ?? "").Trim();
                identity.cosmeticType = (rescanOf.cosmeticType ?? "").Trim();
                if (ProductKnown) { evidence.ProductKnown(); ShowIdentity(); }
            }

            // coming back from a scan that could not be analysed, or that was stopped because the product is not make-up
            Say(string.IsNullOrEmpty(ScanSession.LastError) ? statusIdle : ScanSession.LastError);
            ScanSession.LastError = null;
            ShowButtons();
            if (ring != null) ring.Show(evidence);

            // what the app already knows (also offline): the ingredient database, the brands and the product inventory
            try
            {
                _ = IngredientRepository.LoadAll();
                brands = await BrandRepository.LoadActive() ?? new List<BrandData>();
                List<ProductData> products = await ProductRepository.LoadActive() ?? new List<ProductData>();
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

        void OnDestroy()
        {
            if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted;
            if (holdButton != null) { holdButton.Pressed -= OnHoldPressed; holdButton.Released -= OnHoldReleased; }
        }

        // ---------------------------------------------------------------------------------------
        //  The hold button
        // ---------------------------------------------------------------------------------------
        void OnHoldPressed()
        {
            if (stage == Stage.Idle)
            {
                // the first press switches the camera on; holding on (or the tap that locked the button) then scans
                stage = Stage.Starting;
                Say(msgStarting);
                if (cameraFeed != null) cameraFeed.StartCamera(); else OnCameraStarted(false, "No camera view in this scene.");
            }
            pausedFor = 0f; sinceProgress = 0f; stalledSaid = false;
            ShowButtons();
            ShowGuidance();
        }

        /// <summary>The button was let go: the scan pauses, and Finish is offered when there is a list to finish with.</summary>
        void OnHoldReleased() { ShowButtons(); ShowGuidance(); }

        void OnCameraStarted(bool ok, string message)
        {
            if (stage != Stage.Starting) return;
            if (!ok)
            {
                stage = Stage.Idle;
                if (holdButton != null) holdButton.LetGo();
                Say(message + " You can type the product in instead.");
                return;
            }
            if (idlePlaceholder != null) idlePlaceholder.SetActive(false);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Searching);
            stage = Stage.Scanning;
            nextLook = 0f; pausedFor = 0f; sinceProgress = 0f;
            ShowGuidance();
        }

        void StopCamera()
        {
            StopAllCoroutines();
            if (textOutlines != null) textOutlines.Clear();
            if (cameraFeed != null) cameraFeed.StopCamera();
            lock (answerLock) { answerWaiting = false; answerReading = null; }
            readingOut = false;
        }

        /// <summary>The camera goes off and the screen waits for the next press (the ring keeps what it has unless it is cleared separately).</summary>
        void BackToIdle()
        {
            StopCamera();
            stage = Stage.Idle;
            if (holdButton != null) { holdButton.Usable = true; holdButton.LetGo(); }
            if (idlePlaceholder != null) idlePlaceholder.SetActive(true);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Hidden);
            messageOver = null;
            ShowButtons();
        }

        // ---------------------------------------------------------------------------------------
        //  While scanning: one look at the camera after another
        // ---------------------------------------------------------------------------------------
        void Update()
        {
            if (stage != Stage.Scanning) return;
            TakeAnswer();                                    // the text reader may have finished a picture (also while paused)
            if (stage != Stage.Scanning) return;

            bool held = holdButton != null && holdButton.IsHeld;
            if (!held)
            {
                // PAUSE, DON'T RESET: the ring keeps what it has. Only a long pause puts the scan back to the start
                // (the time a pop-up is open does not count: the user is reading or typing).
                if (!PopupOpen) pausedFor += Time.unscaledDeltaTime;
                if (pausedFor > pauseResetSeconds)
                {
                    bool hadSomething = evidence.Pictures > 0;
                    if (hadSomething) StartOver(false, msgPauseReset);
                    BackToIdle();
                    if (!hadSomething) Say(statusIdle);
                }
                return;
            }
            pausedFor = 0f;
            sinceProgress += Time.unscaledDeltaTime;
            if (!stalledSaid && sinceProgress > stallSeconds) { stalledSaid = true; Say(msgStalled); ShowButtons(); }

            if (cameraFeed == null || !cameraFeed.IsPlaying) return;
            if (reader == null || !reader.Available) { if (shownStatus != msgReaderMissing) { Say(msgReaderMissing); shownStatus = msgReaderMissing; } return; }
            if (readingOut && Time.unscaledTime - sentAt > ReaderAnswerSeconds) readingOut = false;      // the reader never answered: go on with the next picture
            if (readingOut || Time.unscaledTime < nextLook || reader.Busy) return;       // always the newest picture, never a queue
            if (!cameraFeed.Grab()) return;                  // the camera has no new picture in this frame: next frame
            nextLook = Time.unscaledTime + 1f / Mathf.Max(1f, looksPerSecond);
            Look();
            ShowGuidance();
        }

        /// <summary>One look: is the picture good enough (the quality gate)? Then it goes to the text reader.</summary>
        void Look()
        {
            RectTransform frame = guide != null ? guide.Frame : null;
            if (!cameraFeed.GreyPictures(frame, ref overview, OverviewW, OverviewH, ref detail, DetailSize)) return;
            FrameQuality.Measure measure = FrameQuality.Of(overview, OverviewW, OverviewH, overviewBefore, detail, DetailSize, DetailSize);
            if (overviewBefore == null || overviewBefore.Length != overview.Length) overviewBefore = new byte[overview.Length];
            Buffer.BlockCopy(overview, 0, overviewBefore, 0, overview.Length);
            fault = FrameQuality.Judge(measure);

            // too dark or moving: nothing in it can be trusted. (The flashlight helper of this screen deals with the dark.)
            if (fault == FrameQuality.Fault.Dark || fault == FrameQuality.Fault.Moving) { blurrySkips = 0; return; }
            if (fault == FrameQuality.Fault.Blurry)
            {
                // out of focus: skipped. After several in a row one is read anyway and the camera is asked to focus again,
                // so a phone whose camera never looks "sharp" to this check can still scan (the text reader then decides).
                if (++blurrySkips < Mathf.Max(1, blurrySkipsBeforeReadingAnyway)) return;
                blurrySkips = 0;
                cameraFeed.Refocus();
            }
            else blurrySkips = 0;
            // glare: the picture is read; lines the reader is not sure of count for nothing (ScanEvidence), and the hint stays

            if (!cameraFeed.UprightArgb(frame, readPictureSize, ref pixels, out pixelsW, out pixelsH)) return;
            string colour = cameraFeed.DominantColourHex();
            if (!string.IsNullOrEmpty(colour)) lastColour = colour;
            sentSharpness = measure.sharpness; sentAt = Time.unscaledTime;
            readingOut = reader.Read(pixels, pixelsW, pixelsH, readBarcode && barcode.Length == 0, OnReaderAnswer);
        }

        // called on the text reader's own thread: only the answer is stored
        void OnReaderAnswer(FrameReading reading, string problem)
        {
            if (problem != null) Debug.LogWarning("[Scan] Could not read this picture: " + problem);
            lock (answerLock) { answerReading = reading ?? new FrameReading(); answerWaiting = true; }
        }

        void TakeAnswer()
        {
            FrameReading reading;
            lock (answerLock)
            {
                if (!answerWaiting) return;
                answerWaiting = false; reading = answerReading; answerReading = null;
            }
            readingOut = false;
            if (reading != null) OnReading(reading);
        }

        /// <summary>A picture was read: what does it show, and what does it do for the ring?</summary>
        void OnReading(FrameReading reading)
        {
            if (textOutlines != null) textOutlines.Show(FrameReader.BoxesOf(reading, ScanEvidence.UsableLine));
            string text = reading.Text(ScanEvidence.UsableLine);

            // ---- what the app's own lists know in this picture (works offline)
            var known = new ScanEvidence.Known { colourHex = lastColour };
            ProductData seen = ProductIn(text);
            if (seen != null)
            {
                if (knownProduct != null && seen.id != knownProduct.id && evidence.Pictures > 0)
                {
                    // A DIFFERENT PRODUCT is in front of the camera now (seen twice, so one misreading never resets a scan)
                    if (++differentSeen >= 2)
                    {
                        StartOver(false, msgDifferentProduct);
                        Adopt(seen);
                        known.productSeen = true;
                    }
                }
                else { differentSeen = 0; if (knownProduct == null) Adopt(seen); known.productSeen = knownProduct != null && seen.id == knownProduct.id; }
            }
            else differentSeen = 0;
            known.brandSeen = BrandIn(text);
            known.noShadeNeeded = identity.cosmeticType.Length > 0 && !ShadeAdvisor.IsColourCosmetic(identity.cosmeticType);

            ScanEvidence.Result result = evidence.Add(reading, known, sentSharpness);
            KeepBest(result);

            // ---- the barcode: a product of the inventory is recognised at once
            if (barcode.Length == 0 && evidence.Barcode.Length > 0) TakeBarcode(evidence.Barcode);
            bool showsPack = result.zones[(int)ScanZone.Front] || (result.zones[(int)ScanZone.Around] && !result.zones[(int)ScanZone.Ingredients]);
            // ---- the whole name, read in pieces: the brand in one picture, the product's name in the next
            if (!ProductKnown && result.zones[(int)ScanZone.Front])
            {
                ProductData whole = ProductInFront();
                if (whole != null) Adopt(whole);
            }
            // ---- the look of the pack: a product this phone has scanned before. The app ASKS ("Is this ...?"), it does not decide.
            if (!ProductKnown && showsPack) RecogniseFromLook();
            if (ProductKnown && productQuestion != null && productQuestion.IsOpen) productQuestion.Hide();
            if (identity.shadeName.Length == 0 && evidence.ShadeText.Length > 0) identity.shadeName = evidence.ShadeText;

            bool[] filled = result.filledNow;
            if (ProductKnown && !evidence.IsFull(ScanZone.Front)) Merge(filled, evidence.ProductKnown());

            // ---- make-up only: stop as soon as the product is known to be something else
            if (StopIfNotMakeup(result.zones[(int)ScanZone.Ingredients] ? null : text)) return;

            // ---- a product of the inventory brings its ingredient list with it: nothing more to scan.
            //      (A list that was read from this very pack comes first: it says what is in the user's own product.)
            if (knownProduct != null && knownProduct.ingredients != null && knownProduct.ingredients.Count >= 3)
            {
                if (evidence.IsFull(ScanZone.Ingredients)) { ShowProgress(filled); FinishAttempt(); return; }
                Merge(filled, evidence.ListKnown());
                ShowProgress(filled);
                UseLookedUpList(knownProduct.ingredients, ListPagesOf(knownProduct), "inventory", false);
                return;
            }

            if (result.useful)
            {
                sinceProgress = 0f;
                if (stalledSaid) { stalledSaid = false; messageOver = null; shownStatus = null; }      // moving again: the status line comes back
            }
            ShowProgress(filled);
            // FINISH BY ITSELF, also while the button is still held. (Not after nobody could name the product: then the
            // user decides between typing the name, starting over and "Finish anyway".)
            if (evidence.AllFull && !finishUnnamed) FinishAttempt();
        }

        static void Merge(bool[] into, bool[] more) { for (int i = 0; i < into.Length && i < more.Length; i++) into[i] |= more[i]; }

        /// <summary>The ring, the checklist and the buttons after a picture; a buzz for each segment that became full.</summary>
        void ShowProgress(bool[] filledNow)
        {
            if (ring != null) ring.Show(evidence);
            ShowIdentity();
            if (ingredientsCheck != null && ingredientText == null)
            {
                int counted = evidence.Text.Counted;
                if (evidence.IsFull(ScanZone.Ingredients)) ingredientsCheck.SetFound(string.Format(ingredientsFoundFormat, counted));
                else if (counted > 0) { ingredientsCheck.SetWorking(true); ingredientsCheck.SetLabel(string.Format(ingredientsReadingFormat, counted)); }
            }
            bool any = false;
            if (filledNow != null) foreach (bool one in filledNow) any |= one;
            if (any && vibrate) Haptics.Tick();
            ShowButtons();
            ShowGuidance();
        }

        /// <summary>Keeps the picture when it is the best one so far of a zone it shows (the AI may need it at the end).</summary>
        void KeepBest(ScanEvidence.Result result)
        {
            if (pixels == null || pixelsW <= 0) return;
            int[] copy = null;
            for (int i = 0; i < ScanZones.Count; i++)
            {
                if (!result.zones[i] || (best[i] != null && best[i].score >= result.score)) continue;
                if (copy == null) { copy = new int[pixelsW * pixelsH]; Array.Copy(pixels, copy, copy.Length); }
                best[i] = new Kept { pixels = copy, width = pixelsW, height = pixelsH, score = result.score };
            }
        }

        // ---------------------------------------------------------------------------------------
        //  What is said on screen
        // ---------------------------------------------------------------------------------------
        void ShowGuidance()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            bool held = stage == Stage.Scanning && holdButton != null && holdButton.IsHeld;
            if (holdLabel != null) holdLabel.text = held ? (holdButton.IsLocked ? holdLockedLabel : holdActiveLabel) : holdIdleLabel;
            if (stage != Stage.Scanning) return;

            // the status line. A message that was said in its place stays until the status itself changes.
            string status = StatusNow();
            if (messageOver != null && status != messageOver) messageOver = null;
            if (messageOver == null && status != shownStatus)
            {
                if (statusText != null) statusText.text = status;
                shownStatus = status;
            }
            string hint = held ? ScanGuidance.Hint(words, fault, evidence) : ScanGuidance.Hint(words, FrameQuality.Fault.None, evidence);
            if (hint.Length == 0) hint = hintAllDone;
            if (hint != lastHint && guide != null) { lastHint = hint; guide.SetHint(hint); }
        }

        /// <summary>"Holding, 2 of 5", "Paused at 2 of 5 ...", or the first instruction.</summary>
        string StatusNow()
        {
            bool held = stage == Stage.Scanning && holdButton != null && holdButton.IsHeld;
            return ScanGuidance.Status(words, held, evidence.Pictures > 0, evidence.FullZones, false);
        }

        /// <summary>
        /// FINISH is offered when the ingredient list is complete (read to its end, or known already), and also for a
        /// list that is only partly read once the user has let go of the button or the scan is not getting further:
        /// nobody is made to wait for a ring that will not fill. START OVER is offered as soon as there is anything to forget.
        /// </summary>
        void ShowButtons()
        {
            bool busy = stage == Stage.Recognising || stage == Stage.Leaving;
            bool held = stage == Stage.Scanning && holdButton != null && holdButton.IsHeld;
            bool listComplete = ingredientText != null || evidence.IsFull(ScanZone.Ingredients);
            bool listStarted = evidence.Text.Counted >= 2;
            if (finishButton != null) finishButton.gameObject.SetActive(!busy && (listComplete || (listStarted && (!held || stalledSaid || finishUnnamed))));
            if (finishText != null) finishText.text = ProductKnown || !finishUnnamed ? finishNormalLabel : finishUnnamedLabel;
            if (startOverButton != null) startOverButton.gameObject.SetActive(!busy && (evidence.Pictures > 0 || ProductKnown || ingredientText != null));
        }

        /// <summary>A message in the place of the status line. While scanning it stays until the status changes (a part of the ring fills, the button is pressed or let go).</summary>
        void Say(string message)
        {
            if (statusText != null) statusText.text = message;
            shownStatus = null;
            messageOver = stage == Stage.Scanning ? StatusNow() : null;
        }

        void ResetChecklist()
        {
            foreach (ScanCheckItem item in new[] { productCheck, brandCheck, typeCheck, shadeCheck, ingredientsCheck }) if (item != null) item.SetPending();
            if (shadeSwatch != null) shadeSwatch.gameObject.SetActive(false);
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

            // the shade: its printed name, what the AI saw, otherwise (for a colour cosmetic) the colour the pictures agree on
            string hex = identity.shadeHex;
            if (hex.Length == 0 && ShadeAdvisor.IsColourCosmetic(identity.cosmeticType)) hex = evidence.ShadeHex.Length > 0 ? evidence.ShadeHex : (ProductKnown ? lastColour : "");
            hex = ShadeAdvisor.ShadeFor(identity.cosmeticType, hex);          // a face powder is white, whatever colour its box has
            if (ShadeAdvisor.IsFacePowder(identity.cosmeticType)) identity.shadeHex = hex;
            if (hex.Length == 0 && identity.shadeName.Length == 0) return;
            string shown = identity.shadeName.Length > 0 ? identity.shadeName : hex;
            if (identity.finish.Length > 0) shown += ", " + identity.finish;
            if (shadeCheck != null) shadeCheck.SetFound(shown);
            if (shadeSwatch != null && hex.Length > 0) { shadeSwatch.gameObject.SetActive(true); shadeSwatch.color = ShadeAdvisor.Parse(hex, Color.gray); }
        }

        // ---------------------------------------------------------------------------------------
        //  Recognising the product from what the app already knows
        // ---------------------------------------------------------------------------------------
        /// <summary>The product of the inventory whose whole name stands in this text (words in any order, small reading mistakes forgiven), or null.</summary>
        static ProductData ProductIn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            List<ProductIndex.Candidate> found = ProductRepository.Index.InText(text, 1);
            return found.Count > 0 && found[0].exactName && found[0].score >= ProductIndex.Sure ? found[0].product : null;
        }

        void Adopt(ProductData product)
        {
            knownProduct = product;
            identity.productName = (product.name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(product.brandName)) identity.brandName = product.brandName.Trim();
            if (!string.IsNullOrWhiteSpace(product.cosmeticType)) identity.cosmeticType = product.cosmeticType.Trim();
        }

        /// <summary>True when a brand the app knows stands in the text. The first one found is taken over when no brand is known yet.</summary>
        bool BrandIn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string seen = " " + TextKey.Normalize(text) + " ";
            foreach (BrandData brand in brands)
            {
                string name = TextKey.Normalize(brand != null ? brand.name : "");
                if (name.Length < 3 || !seen.Contains(" " + name + " ")) continue;
                if (identity.brandName.Length == 0) identity.brandName = brand.name.Trim();
                return true;
            }
            return false;
        }

        /// <summary>The lines of a text, each once (the front of a pack is read many times while it is held up).</summary>
        static string DistinctLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var once = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new System.Text.StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && once.Add(trimmed)) sb.Append(trimmed).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The product whose whole name stands in ALL the text read on the front so far, or null. Asked again only when
        /// that text has changed.
        /// </summary>
        ProductData ProductInFront()
        {
            string all = DistinctLines(evidence.FrontText);
            if (all.Length == 0 || all == frontChecked) return null;
            frontChecked = all;
            return ProductIn(all);
        }

        /// <summary>
        /// Compares the picture with the packs this phone remembers. A strong likeness, or a medium one together with a
        /// word of the product's name read on the pack, is worth a question: "Is this ...?". The user's answer decides;
        /// the look alone never does (two products of one line can look the same). What is less alike than that is kept
        /// for the shortlist at the end.
        /// </summary>
        void RecogniseFromLook()
        {
            if (!PackagingMemory.Enabled || pixels == null) return;
            PackagingMemory.Look look = PackagingMemory.SignatureOf(pixels, pixelsW, pixelsH);
            if (look == null) return;
            foreach (PackagingMemory.Match match in PackagingMemory.Matches(look, DistinctLines(evidence.FrontText), ScanShortlist.Maximum))
            {
                // each remembered product once, with the best likeness seen in this scan
                int at = lookMatches.FindIndex(other => other.look == match.look
                    || (string.Equals(other.look.productName, match.look.productName, StringComparison.OrdinalIgnoreCase) && string.Equals(other.look.brandName, match.look.brandName, StringComparison.OrdinalIgnoreCase)));
                if (at < 0) lookMatches.Add(match);
                else if (match.strength > lookMatches[at].strength || (match.strength == lookMatches[at].strength && (match.wordSeen && !lookMatches[at].wordSeen || match.score > lookMatches[at].score)))
                {
                    match.wordSeen |= lookMatches[at].wordSeen;
                    lookMatches[at] = match;
                }
            }
            if (productQuestion == null || productQuestion.IsOpen || PopupOpen) return;
            foreach (ScanShortlist.Entry entry in ScanShortlist.For(ProductRepository.Index, "", lookMatches.FindAll(match => match.WorthAsking), declined, 1))
            {
                ScanShortlist.Entry asked = entry;
                productQuestion.Ask(asked.productName, asked.brandName,
                    () => { TakeEntry(asked); Named(ContinueScanning); },
                    () => declined.Add(asked.Key));
            }
        }

        /// <summary>The product the user chose (from the question or from the shortlist) becomes the product of this scan.</summary>
        void TakeEntry(ScanShortlist.Entry entry)
        {
            if (entry.product != null) { Adopt(entry.product); return; }
            // a product only this phone remembers. Its shade is NOT taken from memory: the shades of one line look alike,
            // so the shade is read from this pack (its printed name, or its colour).
            identity.productName = entry.productName.Trim();
            if (entry.brandName.Trim().Length > 0) identity.brandName = entry.brandName.Trim();
            if (entry.cosmeticType.Trim().Length > 0) identity.cosmeticType = entry.cosmeticType.Trim();
        }

        /// <summary>The user typed the product's name (and perhaps its brand). A product of the inventory with exactly that name is taken as it is.</summary>
        void TakeTyped(string productName, string brandName)
        {
            ProductData exact = ProductRepository.Index.Exact(brandName, productName);
            if (exact != null) { Adopt(exact); return; }
            knownProduct = null;
            identity.productName = (productName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(brandName)) identity.brandName = brandName.Trim();
        }

        /// <summary>
        /// The product has just been named by the user's answer. The first segment of the ring is full, the make-up check
        /// runs, and then `next` goes on (scanning, finishing, or looking the list up).
        /// </summary>
        void Named(Action next)
        {
            if (productQuestion != null) productQuestion.Hide();
            finishUnnamed = false;
            bool[] filled = evidence.ProductKnown();
            if (stage == Stage.Idle || stage == Stage.Scanning) { if (StopIfNotMakeupNow()) return; }
            ShowProgress(filled);
            next?.Invoke();
        }

        /// <summary>After "Yes, it is this product" during the scan: a product of the inventory with a stored list needs nothing more.</summary>
        void ContinueScanning()
        {
            if (knownProduct == null || knownProduct.ingredients == null || knownProduct.ingredients.Count < 3 || (listFrom != "" && listFrom != "pack")) return;
            if (evidence.IsFull(ScanZone.Ingredients)) { FinishAttempt(); return; }      // the list of this very pack was read: that one is used
            bool[] filled = evidence.ListKnown();
            ShowProgress(filled);
            UseLookedUpList(knownProduct.ingredients, ListPagesOf(knownProduct), "inventory", false);
        }

        bool PopupOpen => (shortlist != null && shortlist.IsOpen) || (startOverPanel != null && startOverPanel.IsOpen) || (manualPanel != null && manualPanel.IsOpen);

        /// <summary>What the app's own lists suggest for this scan: by the words read on the front, and by the look of the pack.</summary>
        List<ScanShortlist.Entry> Suggestions()
        {
            return ScanShortlist.For(ProductRepository.Index, DistinctLines(evidence.FrontText), lookMatches, declined);
        }

        /// <summary>
        /// Opens "Is it one of these?". `named` runs after the user chose or typed a product, `noneOfThese` when they said
        /// none fits. "Scan again" starts a fresh ring.
        /// </summary>
        void OpenShortlist(string title, string message, string noneLabel, Action named, Action noneOfThese)
        {
            if (shortlist == null) { noneOfThese?.Invoke(); return; }
            if (holdButton != null) holdButton.LetGo();
            if (productQuestion != null) productQuestion.Hide();
            shortlist.Show(title, message, Suggestions(), noneLabel,
                (entry, typedName) =>
                {
                    TakeEntry(entry);
                    // the name the user typed for it is remembered on this phone: next time it finds the product at once
                    if (entry.product != null && !string.IsNullOrWhiteSpace(typedName))
                    {
                        try { LocalAliases.Learn(typedName, entry.product); } catch (Exception e) { Debug.LogWarning("Scan: the typed name was not kept: " + e.Message); }
                    }
                    Named(named);
                },
                (typedName, typedBrand) => { TakeTyped(typedName, typedBrand); Named(named); },
                noneOfThese,
                () => StartOver(false, msgStartedOver));
        }

        /// <summary>
        /// Takes over a barcode the camera has read. A product in the inventory with that barcode is the product being
        /// scanned: its name, brand and kind are filled in (its saved ingredient list is used by the caller).
        /// </summary>
        void TakeBarcode(string seen)
        {
            if (MlKitBarcodeReader.Key(seen).Length == 0) return;
            barcode = seen.Trim();
            ProductData p = ProductRepository.Index.ByBarcode(barcode);            // the phone's look-up table: no reading through the list
            if (p != null) Adopt(p);
        }

        /// <summary>
        /// MAKE-UP ONLY. As soon as the scan knows the product is something else (the inventory, the AI or the pack itself
        /// says "shampoo"), the scan stops: the user is told why, and can scan again or type the name in when the app got
        /// it wrong. Nothing is saved or sent to the inventory. True = the scan was stopped.
        /// </summary>
        bool StopIfNotMakeup(string frontText = null)
        {
            if (stage == Stage.Leaving || stage == Stage.Idle) return false;
            return StopIfNotMakeupNow(frontText);
        }

        /// <summary>The same check, also while the camera is off (the user named the product in a pop-up).</summary>
        bool StopIfNotMakeupNow(string frontText = null)
        {
            ProductScope.Verdict verdict = ProductScope.Judge(identity.cosmeticType, identity.productName);
            // nothing recognised yet (offline, or the AI has not been asked): only unmistakable words on the pack count
            if (!verdict.IsNotMakeup && !string.IsNullOrWhiteSpace(frontText) && identity.productName.Length == 0 && identity.cosmeticType.Length == 0)
                verdict = ProductScope.JudgeLabel(frontText);
            if (!verdict.IsNotMakeup) return false;

            StartOver(false, null);
            BackToIdle();
            Say(ProductScope.StopMessage(verdict));
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  Start over
        // ---------------------------------------------------------------------------------------
        void OnStartOverTapped()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            // a product that has a name: the same one again, or a different one? Without a name there is nothing to keep.
            if (ProductKnown && startOverPanel != null) { if (holdButton != null) holdButton.LetGo(); startOverPanel.Show(); }
            else StartOver(false, msgStartedOver);
        }

        static void ClosePanel(OverlayPanel panel) { if (panel != null) panel.Hide(); }

        /// <summary>
        /// A fresh ring. `sameProduct` keeps the product's name, brand and kind (only the pictures and the text are taken
        /// anew); otherwise everything is forgotten. How often the AI was asked in this visit is kept either way.
        /// </summary>
        void StartOver(bool sameProduct, string message)
        {
            evidence.Reset();
            for (int i = 0; i < best.Length; i++) best[i] = null;
            ingredientText = null; ingredientSource = "scan"; listFrom = ""; listWebBacked = false; sources.Clear();
            barcode = ""; lastColour = "";
            lookMatches.Clear(); declined.Clear(); shortlistAnswered = false; frontChecked = "";
            if (productQuestion != null) productQuestion.Hide();
            fault = FrameQuality.Fault.None; blurrySkips = 0; differentSeen = 0; sinceProgress = 0f; stalledSaid = false; pausedFor = 0f;
            finishUnnamed = false; lookingUp = false; shownStatus = null; messageOver = null; lastHint = "";
            lock (answerLock) { answerWaiting = false; answerReading = null; }
            overviewBefore = null;
            if (textOutlines != null) textOutlines.Clear();

            bool keep = sameProduct && ProductKnown;
            if (!keep)
            {
                identity.found = false; identity.confidence = 0f;
                identity.productName = identity.brandName = identity.cosmeticType = identity.shadeHex = identity.shadeName = identity.finish = "";
                knownProduct = null;
                rescanOf = null;                 // a different product is a new scan: nothing is replaced
            }
            else { identity.shadeHex = identity.shadeName = identity.finish = ""; }
            ResetChecklist();
            SetWorking(false);
            if (ring != null) ring.Clear();
            if (keep) { evidence.ProductKnown(); ShowIdentity(); if (ring != null) ring.Show(evidence); }
            ShowButtons();
            if (!string.IsNullOrEmpty(message)) Say(message);
            ShowGuidance();
        }

        // ---------------------------------------------------------------------------------------
        //  Finishing
        // ---------------------------------------------------------------------------------------
        void OnFinishTapped()
        {
            if (stage == Stage.Scanning || stage == Stage.Idle) FinishAttempt();
        }

        /// <summary>
        /// The ring is full, or the user tapped Finish. The list read from the pack is taken; when the app could not name
        /// the product, the AI is asked once with the best pictures; then the analysis starts.
        /// </summary>
        void FinishAttempt()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            if (!TakePackList()) { Say(msgNeedList); return; }
            if (holdButton != null) holdButton.LetGo();

            if (!ProductKnown && !finishUnnamed)
            {
                // 1. what the app's own lists suggest: the user chooses, and no AI is needed
                if (!shortlistAnswered && shortlist != null && Suggestions().Count > 0)
                {
                    OpenShortlist(shortlistTitle, shortlistMessage, shortlistNoneLabel, FinishAttempt, () => { shortlistAnswered = true; FinishAttempt(); });
                    return;
                }
                // 2. the AI, once
                bool canAsk = aiRecognisesProduct && ConnectivityMonitor.Online && ProductAiAdvisor.Available && HasPictures();
                if (canAsk && AiBudget.CanRecognise) { StartCoroutine(RecogniseThenFinish()); return; }
                // online, but the AI has been asked as often as it may be: say so, and offer the other ways on
                if (canAsk) { NotSure(msgAiLimit); return; }
                // offline: the product stays without a name (its ingredients are still checked)
            }
            Finish();
        }

        bool HasPictures() { foreach (Kept kept in best) if (kept != null) return true; return false; }

        /// <summary>
        /// Takes the ingredient list as the camera has read it SO FAR (so a list that grew after an earlier try is not
        /// cut short). A list that was looked up stays as it is. False = there is no list to analyse yet.
        /// </summary>
        bool TakePackList()
        {
            if (ingredientText != null && listFrom != "pack") return true;
            string read = evidence.Text.IngredientText();
            if (evidence.Text.Counted < 2 || read.Length == 0) return ingredientText != null;
            ingredientText = read; ingredientSource = "scan"; listFrom = "pack"; listWebBacked = false; sources.Clear();
            if (ingredientsCheck != null) ingredientsCheck.SetFound(string.Format(ingredientsFoundFormat, evidence.Text.Counted));
            return true;
        }

        /// <summary>The app's own lists could not name the product: the AI is shown the best pictures, ONCE.</summary>
        IEnumerator RecogniseThenFinish()
        {
            stage = Stage.Recognising;
            if (holdButton != null) holdButton.Usable = false;
            ShowButtons();
            SetWorking(true);
            Say(msgAiLooking);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Reading);
            yield return null;                               // let the message show before the pictures are packed

            // the best picture of the front first, then the codes and the shade, the ingredient list last (it is text already)
            var pictures = new List<byte[]>();
            var used = new HashSet<int[]>();
            foreach (ScanZone zone in new[] { ScanZone.Front, ScanZone.Shade, ScanZone.Codes, ScanZone.Around, ScanZone.Ingredients })
            {
                Kept kept = best[(int)zone];
                if (kept == null || pictures.Count >= Mathf.Max(1, aiPictures) || !used.Add(kept.pixels)) continue;
                byte[] jpg = ToJpg(kept);
                if (jpg != null) pictures.Add(jpg);
                yield return null;
            }

            Task<ProductAiAdvisor.Identity> task = null;
            if (pictures.Count > 0)
            {
                AiBudget.TakeRecognition();
                string text = evidence.FrontText.Length > 0 ? evidence.FrontText : evidence.Text.AllText(ScanEvidence.UsableLine);
                task = ProductAiAdvisor.Identify(pictures, text);
            }
            float waited = 0f, limit = ProductAiAdvisor.TimeoutSeconds + 6f;     // EVERY LONG STEP HAS A TIME LIMIT
            while (task != null && !task.IsCompleted && waited < limit) { waited += Time.unscaledDeltaTime; yield return null; }
            SetWorking(false);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Searching);

            if (task != null && task.Status == TaskStatus.RanToCompletion && task.Result != null && task.Result.found && task.Result.confidence >= minimumAiConfidence)
            {
                ProductAiAdvisor.Identity seen = task.Result;
                // what the app's own lists said stays; the AI fills the gaps
                if (identity.productName.Length == 0) identity.productName = seen.productName;
                if (identity.brandName.Length == 0) identity.brandName = seen.brandName;
                if (identity.cosmeticType.Length == 0) identity.cosmeticType = seen.cosmeticType;
                if (seen.shadeHex.Length > 0) identity.shadeHex = seen.shadeHex;
                if (identity.shadeName.Length == 0 && seen.shadeName.Length > 0) identity.shadeName = seen.shadeName;
                if (seen.finish.Length > 0) identity.finish = seen.finish;
                ShowIdentity();
            }
            stage = Stage.Scanning;
            if (holdButton != null) holdButton.Usable = true;
            if (StopIfNotMakeup()) yield break;              // the AI says it is not make-up
            if (!ProductKnown) { NotSure(msgNotSure); yield break; }
            Finish();
        }

        /// <summary>
        /// Nobody could name the product: never a dead end. The shortlist opens with whatever the app's lists suggest, a
        /// box to type the name, "Scan again" and "Finish without a name". The same choices stay on the screen behind it
        /// (Start over, Type the product in, Finish anyway) when the pop-up is closed.
        /// </summary>
        void NotSure(string message)
        {
            finishUnnamed = true;
            stage = cameraFeed != null && cameraFeed.IsPlaying ? Stage.Scanning : Stage.Idle;
            if (holdButton != null) holdButton.Usable = true;
            ShowButtons();
            Say(message);
            if (shortlist != null) OpenShortlist(notSureTitle, message, notSureNoneLabel, FinishAttempt, Finish);
        }

        /// <summary>
        /// The product has a name: what its pack looks like is remembered on this phone, its front and one other side, so
        /// it is known again next time. Only a few numbers per side are kept (colours and a light/dark pattern), no picture.
        /// </summary>
        void RememberLooks()
        {
            if (!ProductKnown || !PackagingMemory.Enabled) return;
            try
            {
                Kept front = best[(int)ScanZone.Front], side = best[(int)ScanZone.Around];
                string id = knownProduct != null ? knownProduct.id ?? "" : "";
                foreach (Kept kept in new[] { front, side != null && (front == null || side.pixels != front.pixels) ? side : null })
                {
                    if (kept == null) continue;
                    PackagingMemory.Look look = PackagingMemory.SignatureOf(kept.pixels, kept.width, kept.height);
                    if (look != null) PackagingMemory.Remember(look, identity.productName, identity.brandName, identity.cosmeticType, "", "", "", id, kept == front ? "front" : "side");
                }
            }
            catch (Exception e) { Debug.LogWarning("Scan: the look of the pack was not kept: " + e.Message); }
        }

        /// <summary>A kept picture as a JPG for the AI: made smaller first, so it travels fast.</summary>
        byte[] ToJpg(Kept kept)
        {
            Texture2D picture = null;
            try
            {
                float shrink = Mathf.Max(1f, Mathf.Max(kept.width, kept.height) / (float)Mathf.Max(256, aiPictureSize));
                int w = Mathf.Max(8, Mathf.RoundToInt(kept.width / shrink)), h = Mathf.Max(8, Mathf.RoundToInt(kept.height / shrink));
                var colours = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int from = Mathf.Min(kept.height - 1, (int)((h - 1 - y) * shrink)) * kept.width;      // a texture counts its rows from the bottom
                    for (int x = 0; x < w; x++)
                    {
                        int p = kept.pixels[from + Mathf.Min(kept.width - 1, (int)(x * shrink))];
                        colours[y * w + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, 255);
                    }
                }
                picture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                picture.SetPixels32(colours); picture.Apply();
                return picture.EncodeToJPG(aiPictureQuality);
            }
            catch (Exception e) { Debug.LogWarning("Scan: " + e.Message); return null; }
            finally { if (picture != null) Destroy(picture); }
        }

        /// <summary>Hands the scan over to the analysis.</summary>
        void Finish()
        {
            if (stage == Stage.Leaving) return;
            TakePackList();
            if (ingredientText == null) return;
            RememberLooks();
            ScanSession.PendingText = ingredientText; ScanSession.PendingSource = ingredientSource;
            ScanSession.PendingShadeHex = identity.shadeHex.Length > 0 ? identity.shadeHex : evidence.ShadeHex.Length > 0 ? evidence.ShadeHex : lastColour;
            ScanSession.PendingProductName = identity.productName; ScanSession.PendingBrandName = identity.brandName;
            ScanSession.PendingCosmeticType = identity.cosmeticType;
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            ScanSession.PendingSources = new List<string>(sources);
            ScanSession.PendingBarcode = barcode;
            ScanSession.PendingListFrom = listFrom; ScanSession.PendingListWebBacked = listWebBacked;
            ScanSession.PendingReplaces = rescanOf;
            StartCoroutine(Recognised());
        }

        IEnumerator Recognised()
        {
            stage = Stage.Leaving;
            if (holdButton != null) holdButton.Usable = false;
            ShowButtons();
            if (ring != null) ring.Show(evidence);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Detected);
            if (vibrate) Haptics.Tick(60);
            Say(statusDone);
            yield return new WaitForSecondsRealtime(recognisedHoldSeconds);
            if (cameraFeed != null) cameraFeed.StopCamera();
            SceneNavigator.GoViaLoading(resultScene, loadingMessage, ScanAnalysisPipeline.Run);
        }

        // ---------------------------------------------------------------------------------------
        //  A product without a list on it: look the list up
        // ---------------------------------------------------------------------------------------
        async void LookUp()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising || lookingUp) return;
            if (identity.productName.Length == 0)
            {
                // A WORN PRODUCT, or one without its pack: the name cannot be read. The user picks it from what little
                // could be read, or types it; after that the list is looked up like for any product.
                if (shortlist != null) OpenShortlist(notSureTitle, msgLookUpWhich, shortlistNoneLabel, LookUp, () => Say(msgLookUpNeedsName));
                else Say(msgLookUpNeedsName);
                return;
            }
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

                // 2. online, from credible sources (a few times per visit of this screen at most)
                if (!ConnectivityMonitor.Online || !ProductAiAdvisor.Available) { Say(msgLookUpOffline); return; }
                if (!AiBudget.TakeLookUp()) { Say(msgLookUpLimit); return; }
                ProductAiAdvisor.IngredientLookup found = await ProductAiAdvisor.FindIngredients(identity.productName, identity.brandName, identity.cosmeticType);
                if (this == null || stage == Stage.Leaving) return;
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

        /// <summary>
        /// The inventory's entry for a product that was recognised, also when the names are not written exactly alike: the
        /// brand must agree, and one name must hold (nearly) all the words of the other. Of several, the one that has an
        /// ingredient list wins. Null when the inventory does not have it.
        /// </summary>
        ProductData InventoryProductFor(string productName, string brandName)
        {
            ProductIndex index = ProductRepository.Index;
            ProductData exact = index.Exact(brandName, productName);
            if (exact != null && exact.ingredients != null && exact.ingredients.Count >= 3) return exact;
            // not written exactly alike: the closest names, as long as the index is sure and the brand agrees
            ProductData bestMatch = exact;
            foreach (ProductIndex.Candidate candidate in index.Search(brandName, productName, 3))
            {
                if (candidate.score < ProductIndex.Sure) break;
                bool hasList = candidate.product.ingredients != null && candidate.product.ingredients.Count >= 3;
                if (bestMatch == null || (hasList && (bestMatch.ingredients == null || bestMatch.ingredients.Count < 3))) bestMatch = candidate.product;
            }
            return bestMatch;
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
            if (this == null || stage == Stage.Leaving || ingredientText != null) return;        // the list was read from the pack in the meantime
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
            evidence.ListKnown();
            if (ring != null) ring.Show(evidence);
            if (holdButton != null) holdButton.LetGo();
            // the list is there: a product that has a name goes straight on; otherwise the usual end (the AI is asked once)
            if (ProductKnown) Finish(); else FinishAttempt();
        }

        // ---------------------------------------------------------------------------------------
        //  Typing the product in
        // ---------------------------------------------------------------------------------------
        void OpenManual()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising || manualPanel == null) return;
            if (holdButton != null) holdButton.LetGo();
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
            if (manualInput != null && string.IsNullOrWhiteSpace(manualInput.text))
            {
                // what the camera has read of the list so far is offered for correcting
                string read = ingredientText ?? string.Join(", ", evidence.Text.CountedNames());
                if (!string.IsNullOrEmpty(read)) manualInput.text = read;
            }
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
            stage = Stage.Leaving;
            StopCamera();
            ScanSession.PendingText = text; ScanSession.PendingSource = "manual"; ScanSession.PendingShadeHex = identity.shadeHex;
            ScanSession.PendingListFrom = "typed"; ScanSession.PendingListWebBacked = false;
            // what the user typed comes first; what the scan found fills the rest
            string typedName = manualProductInput != null ? manualProductInput.text.Trim() : "";
            string typedBrand = manualBrandInput != null ? manualBrandInput.text.Trim() : "";
            string typedType = manualTypeDropdown != null && manualTypeDropdown.value > 0 ? manualTypeDropdown.options[manualTypeDropdown.value].text : "";
            ScanSession.PendingProductName = typedName.Length > 0 ? typedName : identity.productName;
            ScanSession.PendingBrandName = typedBrand.Length > 0 ? typedBrand : identity.brandName;
            ScanSession.PendingCosmeticType = typedType.Length > 0 ? typedType : identity.cosmeticType;
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            ScanSession.PendingBarcode = barcode;
            ScanSession.PendingReplaces = rescanOf;
            SceneNavigator.GoViaLoading(resultScene, loadingMessage, ScanAnalysisPipeline.Run);
        }
    }
}
