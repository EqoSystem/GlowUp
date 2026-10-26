using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
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
    /// AI Smart Scan scene: tap once to start, tap again to pause.
    ///
    /// The scan follows the fast on-device flow: camera frame, local OCR of the primary label, catalogue match, then
    /// local ingredient-safety analysis and the result. A clear front view is usually enough; a barcode is optional.
    /// The ring is progress feedback, not a prerequisite for product recognition.
    ///
    ///  - A picture that is too dark, blurred, burnt out by glare or taken while the phone moves is thrown away before
    ///    it is read (FrameQuality). The hint on the camera picture says what to do ("Hold steadier").
    ///  - OCR lines are filtered to the primary label before they can match a product or be merged into scan evidence.
    ///    Ingredient lists are supplied by the product catalogue, not parsed from camera frames.
    ///  - The cached Firebase catalogue is tried first (including offline); a miss triggers an asynchronous Firebase
    ///    lookup by brand and product name. "Look it up" retries that same catalogue path.
    ///  - Barcode and exact text matches can identify a cached product immediately. If OCR is unsure, the app offers a
    ///    shortlist or manual product entry instead of silently guessing.
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

        [Header("The scan button and its ring")]
        [SerializeField] private HoldToScanButton holdButton;
        [Tooltip("The words under the scan button")]
        [SerializeField] private TextMeshProUGUI scanButtonLabel;
        [SerializeField] private ScanRingView ring;
        [Tooltip("A short buzz when a segment of the ring becomes full, and a longer one when the scan is done")]
        [SerializeField] private bool vibrate = true;

        [Header("Buttons and texts")]
        [Tooltip("Continue once a product and ingredient list are available")]
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

        [Header("AI photo fallback: a product the catalogue does not have")]
        [Tooltip("The Scan Snapshot object (its own object in the Hierarchy, with every number and word in its Inspector)")]
        [SerializeField] private ScanSnapshotController snapshot;
        [Tooltip("On = when the catalogue has no such product (and no other user has staged it), the photo pop-up is offered")]
        [SerializeField] private bool offerSnapshotOnCatalogMiss = true;
        [Tooltip("On = when the scan is stuck and no product has been named, the photo pop-up is offered once")]
        [SerializeField] private bool offerSnapshotWhenStuck = true;
        [Tooltip("The camera button on the scan screen: a photo of the product is sent to the web search. It appears when the scan takes long.")]
        [SerializeField] private Button photoButton;
        [Tooltip("The photo button appears after the scan has run this many seconds without finishing")]
        [Min(1f)] [SerializeField] private float photoButtonAfterSeconds = 6f;
        [SerializeField] private string msgPhotoHint = "Slow or read wrong? Tap the camera button to take a photo of the product.";
        [SerializeField] private string msgPhotoOffer = "Not reading it right? Take a photo of the product: GlowUp's AI searches the web for it.";
        [SerializeField] private string msgSnapshotStuck = "Not getting further? Take one photo of the pack and the AI will read it.";
        [SerializeField] private string msgFoundStagedOwn = "Found it among the products you sent. It is waiting for an admin's approval.";

        [Header("Recognising a product without the AI")]
        [Tooltip("The pop-up \"Is it one of these?\" with its search box")]
        [SerializeField] private ScanShortlistView shortlist;
        [SerializeField] private string shortlistTitle = "Is it one of these?";
        [SerializeField] private string shortlistMessage = "We are not sure which product this is. Tap it, or type its name.";
        [SerializeField] private string shortlistNoneLabel = "None of these";
        [Tooltip("The title of that pop-up after the AI could not name the product either, and when a list is to be looked up for a product without a name")]
        [SerializeField] private string notSureTitle = "Which product is it?";
        [SerializeField] private string notSureNoneLabel = "Finish without a name";
        [SerializeField] private string msgLookUpWhich = "Which product should we look up? Tap it, or type its name.";

        [Header("AI product recognition")]
        [Tooltip("The project's AI settings. Empty = the ones the loading scene uses.")]
        [SerializeField] private GeminiApiConfig aiConfig;
        [Tooltip("On = the AI is shown the best pictures when the app's own lists could not name the product")]
        [SerializeField] private bool aiRecognisesProduct = true;
        [Tooltip("Longest side, in pixels, of a picture the AI gets (smaller = faster and cheaper)")]
        [SerializeField] private int aiPictureSize = 900;
        [Tooltip("JPG quality of those pictures (lower = smaller and faster)")]
        [Range(40, 95)] [SerializeField] private int aiPictureQuality = 80;
        [Tooltip("The AI's answer counts when it is at least this sure (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float minimumAiConfidence = 0.45f;

        [Header("AI helps WHILE the scan goes on (so a scan takes seconds)")]
        [Tooltip("On = when the app's own lists have not named the product after the seconds below, the AI is shown ONE picture in the background while the scan goes on. Its answer fills in the brand, the product name, the kind and the shade, and says whether the cap is on. Off = the AI is asked only at the end, when the scan finishes without a name.")]
        [SerializeField] private bool aiHelpsEarly = true;
        [Tooltip("Shortest delay before asking the AI about the first usable camera picture.")]
        [Range(0f, 2f)] [SerializeField] private float aiHelpAfterSeconds = 0f;
        [Tooltip("On = a product the AI named that is NOT in the app's lists is then checked on the web: a web search looks for it by its words and by what the pack looks like, says how its brand and its name are really written, and brings its published ingredient list in the same answer.")]
        [SerializeField] private bool aiChecksTheWeb = true;
        [SerializeField] private string msgWebChecking = "Found it. Checking it on the web...";
        [SerializeField] private string msgAiUnavailable = "The Firebase catalogue had no match, and GlowUp's AI web lookup is not configured on this device. Type the product in or try again later.";
        [SerializeField] private string msgWebNoMatch = "The web search could not verify this exact product and ingredient list. Check the label or type the product in.";
        [Tooltip("Said once when the AI sees that the cap is on and no shade can be seen")]
        [SerializeField] private string msgCapOn = "The cap is on, so the shade cannot be seen. Take it off if you can, or show the shade label.";
        [Tooltip("What the shade row of the checklist says meanwhile")]
        [SerializeField] private string shadeCapOnLabel = "Cap on: open it to show the shade";

        [Header("Reading")]
        [Tooltip("How many camera pictures a second are checked while scanning (the text reader takes the newest one whenever it is free)")]
        [Range(2f, 24f)] [SerializeField] private float looksPerSecond = 12f;
        [Tooltip("The product's brand and name read from the label must be the same (ignoring case and spacing) on two readings in a row, at least this many seconds apart, before the server is asked. Typed names and the Look up button never wait.")]
        [Min(0f)] [SerializeField] private float lookupStableSeconds = 0.5f;
        [Tooltip("Longest side, in pixels, of the picture given to the text reader (smaller = faster)")]
        [SerializeField] private int readPictureSize = 900;
        [Tooltip("After this many pictures in a row that looked out of focus, one is read anyway (and the camera is asked to focus again): the text reader then decides")]
        [SerializeField] private int blurrySkipsBeforeReadingAnyway = 2;
        [Tooltip("A paused scan is forgotten after this many seconds, and the camera is switched off")]
        [SerializeField] private float pauseResetSeconds = 30f;

        [Header("Focus lock and text tracer")]
        [Tooltip("The focus helper of this screen: the padlock button and the focus lock that comes by itself (ScanFocusAssistant). Optional.")]
        [SerializeField] private ScanFocusAssistant focusAssistant;
        [Tooltip("The corner marks that show the block of text the tracer follows (TextTraceView). Optional.")]
        [SerializeField] private TextTraceView textTrace;
        [Tooltip("On = when the letters of the text in view are small, every other picture the text reader gets is a CLOSE LOOK: only that block of text, enlarged. Off = every picture is the whole frame.")]
        [SerializeField] private bool closeLooksAtSmallPrint = true;
        [Tooltip("Letters lower than this many camera pixels count as small print")]
        [Range(8f, 60f)] [SerializeField] private float smallPrintBelowPixels = 24f;
        [Tooltip("A close look is enlarged until the letters are about this many pixels high")]
        [Range(16f, 80f)] [SerializeField] private float closeLookLetterPixels = 32f;
        [Tooltip("A close look is enlarged this many times at most (more only smears the letters)")]
        [Range(1f, 4f)] [SerializeField] private float closeLookMostEnlarged = 2.5f;

        [Header("Scan readout (for testing on a phone)")]
        [Tooltip("On = a small line on the camera picture says what the scanner has to work with: the camera's picture size, how many of its pixels lie in the frame, how sharp the picture is, how high the letters are, how sure the text reader is, and whether the focus is locked. Switch it on, build, and read (or screenshot) the line when a scan is slow.")]
        [SerializeField] private bool showScanReadout = false;
        [Tooltip("The dark strip that holds the line (inactive in the Hierarchy; it appears while the switch above is on and the button is held)")]
        [SerializeField] private GameObject scanReadout;
        [SerializeField] private TextMeshProUGUI scanReadoutText;
        [Tooltip("Held this long without the ring moving on: the user is offered to start over or type the product in")]
        [SerializeField] private float stallSeconds = 12f;
        [Tooltip("How long the 'Got it' message stays before the loading scene opens")]
        [SerializeField] private float recognisedHoldSeconds = 0.5f;

        [Header("Barcode")]
        [Tooltip("On = the camera also looks for the product's barcode (on the phone itself, no internet). A product whose barcode is in the inventory is recognised at once, with its saved ingredient list.")]
        [SerializeField] private bool readBarcode = true;

        [Header("Scenes")]
        [SerializeField] private string resultScene = SceneNames.AiResult;
        [SerializeField] private string backScene = SceneNames.Dashboard;

        [Header("The scan button's words")]
        [SerializeField] private string scanIdleLabel = "Tap to scan";
        [SerializeField] private string scanPauseLabel = "Tap to pause";

        [Header("The Start Scan button: locked while the app is busy")]
        [Tooltip("On = from the moment the button is tapped until the result is ready, the button takes no more taps, looks dimmed, and the ring keeps moving. Two taps in a row can never start two scans.")]
        [SerializeField] private bool lockButtonWhileBusy = true;
        [Tooltip("The words on the button while the camera is starting")]
        [SerializeField] private string startingLabel = "Starting...";
        [Tooltip("The words on the button while the product is being analyzed (and while the result screen is being opened)")]
        [SerializeField] private string busyLabel = "Analyzing...";

        [Header("When the scan is stuck: what to try next")]
        [Tooltip("Said one after the other (every few seconds) once the scan is not getting further. Say what the user can do: more light, a clean lens, a steady hand, typing the name in.")]
        [SerializeField] private string[] stuckTips =
        {
            "Try more light: turn towards a window, or switch the flashlight on.",
            "Wipe the camera lens with a soft cloth.",
            "Hold the phone steady, about 15 to 20 cm from the label, and let it focus.",
            "Still nothing? Tap Type it in and write the product's name."
        };
        [Tooltip("Seconds between two tips")]
        [Min(1f)] [SerializeField] private float stuckTipSeconds = 4f;
        [Tooltip("On = the Type it in button pulses gently while tips are shown, so the way out is easy to see")]
        [SerializeField] private bool pulseManualButtonWhenStuck = true;
        [Tooltip("How big the pulse is (0.05 = 5 percent)")]
        [Range(0f, 0.2f)] [SerializeField] private float manualPulseSize = 0.05f;
        [SerializeField] private string finishNormalLabel = "Finish";
        [Tooltip("The Finish button's words after nobody could name the product (it must stay short: the button is narrow)")]
        [SerializeField] private string finishUnnamedLabel = "Finish anyway";

        [Header("Status line")]
        [Tooltip("Shown while the single product view is being analyzed")]
        [SerializeField] private string statusScanning = "Recognizing the product...";
        [SerializeField] private string statusPaused = "Paused. Tap once to continue.";
        [SerializeField] private string statusIdle = "Tap once to start scanning.";
        [SerializeField] private string statusDone = "Got it! Analysing the product now...";

        [Header("Hints on the camera picture")]
        [SerializeField] private string hintDark = "It is too dark. Turn on the flashlight or move to the light.";
        [SerializeField] private string hintMoving = "Hold steadier";
        [SerializeField] private string hintGlare = "Tilt slightly, there is glare";
        [SerializeField] private string hintBlurry = "Move a little back so the text is sharp";
        [SerializeField] private string hintFront = "Show the front: the brand and the product's name";
        [SerializeField] private string hintIngredients = "Checking GlowUp's Firebase catalogue for the ingredient list.";
        [SerializeField] private string hintIngredientsMore = "Tap Look it up to retry the catalogue search.";
        [SerializeField] private string hintCodes = "Show the barcode (on the bottom or the back)";
        [SerializeField] private string hintShade = "Show the shade's name or number";
        [SerializeField] private string hintAround = "Show one clear view of the product. No extra angles are needed.";
        [SerializeField] private string hintAllDone = "Product recognized.";
        [Tooltip("The hints for what the ring still misses")]
        [SerializeField] private string hintBrand = "The brand is not read yet: show the brand name on the front";
        [SerializeField] private string hintName = "Keep the product name inside the frame";
        [SerializeField] private string hintKind = "Show where the pack says what it is (lipstick, foundation, powder...)";
        [SerializeField] private string hintBarcode = "Almost done. Show the barcode too if the pack has one";

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
        [Header("Name and brand read, kind and shade not")]
        [SerializeField] private string similarTitle = "Is it one of these?";
        [SerializeField] private string similarMessage = "We read the name and the brand but not the kind of make-up or the shade. Tap the product if it is one of these, for a faster and surer result.";
        [SerializeField] private string similarNoneLabel = "None of these";
        [SerializeField] private string msgNoSimilar = "No similar product is saved. Try scanning again, or tap Finish to go on with what was read.";
        [SerializeField] private string msgNotSure = "We could not name this product. Type its name, start over, or tap Finish anyway.";
        [Tooltip("The AI has been asked as often as it may be in one visit of this screen")]
        [SerializeField] private string msgAiLimit = "The AI has been asked as often as it may be for now. Type the product's name, or tap Finish anyway.";
        [SerializeField] private string msgNeedList = "The ingredient list is not read yet. Show it to the camera, or tap Look it up.";
        [SerializeField] private string msgStalled = "Not getting further? Tap Start over, or type the product in instead.";
        [SerializeField] private string msgPauseReset = "The scan was paused for a while, so it was put back to the start.";
        [SerializeField] private string msgDifferentProduct = "That is a different product: a new scan was started for it.";
        [SerializeField] private string msgSameAgain = "Scanning the same product again. Its name is kept.";
        [SerializeField] private string msgStartedOver = "Ready for a new product.";
        [SerializeField] private string msgReaderMissing = "The text reader is not available on this device. Type the ingredients instead.";
        [Header("Typing a product in: its name is enough, the list is looked up")]
        [Tooltip("Nothing was typed that names a product or an ingredient")]
        [SerializeField] private string msgManualNothing = "Type the product's name (its brand helps), or its ingredient list.";
        [Tooltip("{0} = the product as typed")]
        [SerializeField] private string msgManualLooking = "Looking for {0}...";
        [Tooltip("{0} = the product as it is sold")]
        [SerializeField] private string msgManualNoList = "Found {0}, but no trustworthy page shows its ingredient list. Type the list from the pack.";
        [SerializeField] private string msgLookUpNeedsName = "Show the front of the product first, so we know which product to look up.";
        [SerializeField] private string msgLookingUp = "Looking up this product's ingredient list...";
        [SerializeField] private string msgFirebaseMiss = "No matching product was found in GlowUp's Firebase catalogue. Re-align the brand and product name, or type the product in.";
        [SerializeField] private string msgFirebaseNoIngredients = "GlowUp found this product, but its ingredient list is not saved in the catalogue yet.";
        [SerializeField] private string msgFirebaseUnavailable = "GlowUp's product catalogue could not be reached. Check your connection and try again.";
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
        bool photoHintSaid;
        // the shade is the colour the camera keeps seeing: it is taken once it has stayed the same for a while and then it stays
        string shadeCandidate = "", lockedShade = "";
        int shadeStableCount;
        const int ShadeStableReadings = 5;
        bool snapshotOffered;                        // the photo pop-up is offered at most once per scan
        float stuckTipTimer; int stuckTipIndex;
        FrameReader reader;
        readonly ScanEvidence evidence = new ScanEvidence();
        readonly ScanGuidance.Words words = new ScanGuidance.Words();

        // the picture that is being looked at
        int[] pixels; int pixelsW, pixelsH;
        int pixelsOfScan = -1;                    // the scan `pixels` was taken in: after "Start over" it still holds the product before
        // the text tracer: the block of text it follows, and the picture of a close look (its own, so `pixels` stays the whole frame)
        readonly TextTracer tracer = new TextTracer();
        int[] closePixels; int closeW, closeH;
        Rect sentPart = new Rect(0f, 0f, 1f, 1f);
        bool sentCloseLook;
        float framePixelsHigh;
        // for the scan readout
        int frameWide, frameHigh, readLines, closeLooksSent;
        float readSure, lastSharpness, lastEnlarge = 1f;
        byte[] overview, overviewBefore, detail;
        const int OverviewW = 64, OverviewH = 48, DetailSize = 160;
        float nextLook, pausedFor, sinceProgress, sentSharpness, sentAt;
        int blurrySkips, differentSeen;
        bool readingOut, stalledSaid, finishUnnamed, lookingUp, lookupRequested, firebaseLookupOut;
        FrameQuality.Fault fault = FrameQuality.Fault.None;
        string lastColour = "", lastHint = "";
        string shownStatus;       // the status line that is on screen (null = a message is there instead)
        string messageOver;       // a message was said while this status held: it stays until the status changes
        const float ReaderAnswerSeconds = 5f;     // a picture the text reader has not answered after this long is given up

        // the answer of the text reader (written on its own thread, taken over in Update)
        readonly object answerLock = new object();
        FrameReading answerReading; bool answerWaiting;
        readonly object barcodeLock = new object();
        string earlyBarcodeDetected;

        // the best picture of each zone (for the AI, when it is needed at the end)
        class Kept { public int[] pixels; public int width, height; public float score; }
        readonly Kept[] best = new Kept[ScanZones.Count];

        // what has been found so far
        readonly ProductAiAdvisor.Identity identity = new ProductAiAdvisor.Identity();
        ProductData knownProduct;
        ScanRecord rescanOf;                      // "Scan again" on a result: the saved scan this one takes the place of
        bool rescanSame = true;                   // ... true = the same product again, false = a different product (it replaces that scan too)
        readonly HashSet<string> declined = new HashSet<string>();       // products the user said "no" to in this scan
        int scanNumber;
        bool detailsAnswered;                     // the "similar products" question (name and brand read, kind and shade not) was shown or answered in this attempt
        bool shortlistAnswered;                   // the shortlist was shown in this attempt and the user said "none of these"
        string frontChecked = "";                 // the text of the front the product list was last asked about
        string firebaseLookupKey = "";
        string stableKey = ""; float stableSince;     // the name the label gave on the last reading, and when it first showed
        bool readCounterStarted;                      // the first scan of this screen was told to ReadCounter
        string barcode = "";
        string ingredientText, ingredientSource = "scan";
        string listFrom = "";                     // where the ingredient list came from: "pack", "inventory", "web" or "typed"
        bool listWebBacked;                       // a list looked up online came with pages a live web search really opened
        List<string> sources = new List<string>();
        List<BrandData> brands = new List<BrandData>();

        // the AI's early look, the web check and the look for the shade: all three run in the background while the scan goes on
        float scanningFor, earlyAskedAt, earlyTook = -1f, webTook = -1f;
        bool earlyAsked, earlyOut, webAsked, webOut, shadeOut, aiNamed, capSaid;
        byte[] earlyJpg;                                  // the picture the AI was shown (the web check is shown the same one)
        ProductAiAdvisor.IngredientLookup webList;        // a confirmed source-backed list, kept until the scan can use it
        readonly CapShadeWatch capWatch = new CapShadeWatch();

        bool ProductKnown => identity.productName.Length > 0 || (identity.brandName.Length > 0 && identity.cosmeticType.Length > 0);

        void Awake()
        {
            if (holdButton != null) { holdButton.ScanStarted += OnScanStarted; holdButton.ScanPaused += OnScanPaused; }
            if (finishButton != null) finishButton.onClick.AddListener(OnFinishTapped);
            if (startOverButton != null) startOverButton.onClick.AddListener(OnStartOverTapped);
            if (sameProductButton != null) sameProductButton.onClick.AddListener(() => { ClosePanel(startOverPanel); StartOver(true, msgSameAgain); });
            if (differentProductButton != null) differentProductButton.onClick.AddListener(() => { ClosePanel(startOverPanel); StartOver(false, msgStartedOver); });
            if (manualEntryButton != null) manualEntryButton.onClick.AddListener(OpenManual);
            if (photoButton != null) { photoButton.onClick.AddListener(OnPhotoButton); photoButton.gameObject.SetActive(false); }
            if (snapshot != null) { snapshot.Analysed += OnSnapshotAnalysed; snapshot.NameOnly += OnSnapshotNameOnly; }
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
            words.scanning = statusScanning; words.paused = statusPaused; words.notStarted = statusIdle; words.done = statusDone;
            words.dark = hintDark; words.moving = hintMoving; words.glare = hintGlare; words.blurry = hintBlurry;
            words.front = hintFront; words.ingredients = hintIngredients; words.ingredientsMore = hintIngredientsMore;
            words.codes = hintCodes; words.shade = hintShade; words.around = hintAround;
            words.brandMissing = hintBrand; words.nameMissing = hintName; words.kindMissing = hintKind; words.barcodeOptional = hintBarcode;
        }

        async void Start()
        {
            AiBudget.NewSession();                           // the AI's few questions are counted per visit of this screen
            reader = FrameReader.Create();                   // loads in the background while the user reads the instructions
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Hidden);
            if (idlePlaceholder != null) idlePlaceholder.SetActive(true);
            ResetChecklist();
            if (ring != null) ring.Clear();

            // "Scan again" on the result screen: the new result will take the place of that scan. For the SAME product it
            // keeps its name and the pictures are taken anew (no second entry in Recent Scans, no second count); for a
            // DIFFERENT product nothing is taken over, and that scan leaves Recent Scans when the new one is saved.
            rescanOf = ScanSession.RescanOf; rescanSame = ScanSession.RescanSameProduct;
            ScanSession.RescanOf = null; ScanSession.RescanSameProduct = true;
            ScanSession.ClearPending();
            // A scan again starts from NOTHING: the user usually rescans because the app recognised the product wrongly, so the
            // name, brand, kind and shade of the earlier scan are not taken over, and that product is not offered again in the
            // shortlist of this scan (the earlier result is still replaced by the new one).
            if (rescanOf != null)
            {
                string earlierKey = TextKey.Normalize(rescanOf.brandName) + "|" + TextKey.Normalize(rescanOf.productName);
                if (earlierKey.Length > 1) declined.Add(earlierKey);
            }

            // coming back from a scan that could not be analysed, or that was stopped because the product is not make-up
            Say(string.IsNullOrEmpty(ScanSession.LastError) ? statusIdle : ScanSession.LastError);
            ScanSession.LastError = null;
            ShowButtons();
            ShowRing();

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
            if (snapshot != null) { snapshot.Analysed -= OnSnapshotAnalysed; snapshot.NameOnly -= OnSnapshotNameOnly; }
            if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted;
            if (holdButton != null) { holdButton.ScanStarted -= OnScanStarted; holdButton.ScanPaused -= OnScanPaused; }
        }

        // ---------------------------------------------------------------------------------------
        //  The tap-to-toggle scan button
        // ---------------------------------------------------------------------------------------
        void OnScanStarted()
        {
            if (stage == Stage.Idle)
            {
                // the first tap switches the camera on; later taps pause or resume the scan
                stage = Stage.Starting;
                Say(msgStarting);
                PaintButtonState();                          // locked at once: no second tap while the camera starts
                if (cameraFeed != null) cameraFeed.StartCamera(); else OnCameraStarted(false, "No camera view in this scene.");
            }
            pausedFor = 0f; sinceProgress = 0f; stalledSaid = false;
            ShowButtons();
            ShowGuidance();
        }

        /// <summary>The button was let go: the scan pauses, and Finish is offered when there is a list to finish with.</summary>
        void OnScanPaused() { ShowButtons(); ShowGuidance(); }

        void OnCameraStarted(bool ok, string message)
        {
            if (stage != Stage.Starting) return;
            if (!ok)
            {
                stage = Stage.Idle;
                if (holdButton != null) holdButton.LetGo();
                PaintButtonState();
                Say(message + " Check that GlowUp may use the camera in the phone's settings, or type the product in instead.");
                return;
            }
            if (idlePlaceholder != null) idlePlaceholder.SetActive(false);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Searching);
            stage = Stage.Scanning;
            if (!readCounterStarted) { readCounterStarted = true; ReadCounter.NewScan(); }
            nextLook = 0f; pausedFor = 0f; sinceProgress = 0f;
            PaintButtonState();
            ShowGuidance();
        }

        void StopCamera()
        {
            StopAllCoroutines();
            if (textOutlines != null) textOutlines.Clear();
            tracer.Reset(); sentCloseLook = false;
            if (textTrace != null) textTrace.Clear();
            if (cameraFeed != null) cameraFeed.StopCamera();
            lock (answerLock) { answerWaiting = false; answerReading = null; }
            lock (barcodeLock) { earlyBarcodeDetected = null; }
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
        /// <summary>While the scan is stuck, the Type it in button pulses gently: it is the way out.</summary>
        void LateUpdate()
        {
            ShowPhotoButtonIfSlow();
            if (manualEntryButton == null) return;
            bool pulse = pulseManualButtonWhenStuck && stalledSaid && stage == Stage.Scanning;
            float size = pulse ? 1f + manualPulseSize * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)) : 1f;
            Transform t = manualEntryButton.transform;
            if (!Mathf.Approximately(t.localScale.x, size)) t.localScale = Vector3.one * size;
        }

        /// <summary>The camera button shows while a scan has run for a while without finishing (wrong or slow): it takes ONE photo for the web search.</summary>
        void ShowPhotoButtonIfSlow()
        {
            if (photoButton == null || snapshot == null) return;
            bool want = stage == Stage.Scanning && scanningFor >= photoButtonAfterSeconds && !snapshot.IsOpen;
            if (want == photoButton.gameObject.activeSelf) return;
            photoButton.gameObject.SetActive(want);
            if (want && !photoHintSaid) { photoHintSaid = true; Say(msgPhotoHint); }
        }

        void OnPhotoButton()
        {
            if (snapshot == null || stage != Stage.Scanning) return;
            if (holdButton != null) holdButton.LetGo();        // reading pauses while the pop-up is open
            snapshot.Offer(msgPhotoOffer, identity.brandName, identity.productName);
        }

        void Update()
        {
            if (stage != Stage.Scanning) return;
            TakeEarlyBarcode();                              // fast early barcode / QR recognition
            if (stage != Stage.Scanning) return;
            TakeAnswer();                                    // the text reader may have finished a picture (also while paused)
            if (stage != Stage.Scanning) return;
            if (stage != Stage.Scanning) return;

            bool scanning = holdButton != null && holdButton.IsScanning;
            if (!scanning)
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
            if (!stalledSaid && sinceProgress > stallSeconds)
            {
                stalledSaid = true; stuckTipTimer = 0f; stuckTipIndex = 0; Say(msgStalled); ShowButtons();
                if (offerSnapshotWhenStuck && !ProductKnown) OfferSnapshot(msgSnapshotStuck);
            }
            if (stalledSaid && stuckTips != null && stuckTips.Length > 0)
            {
                stuckTipTimer += Time.unscaledDeltaTime;
                if (stuckTipTimer >= stuckTipSeconds) { stuckTipTimer = 0f; Say(stuckTips[stuckTipIndex % stuckTips.Length]); stuckTipIndex++; }
            }
            scanningFor += Time.unscaledDeltaTime;
            if (cameraFeed == null || !cameraFeed.IsPlaying) return;
            if (reader == null || !reader.Available) { if (shownStatus != msgReaderMissing) { Say(msgReaderMissing); shownStatus = msgReaderMissing; } return; }
            if (readingOut && Time.unscaledTime - sentAt > ReaderAnswerSeconds) readingOut = false;      // the reader never answered: go on with the next picture
            if (readingOut || Time.unscaledTime < nextLook || reader.Busy) return;       // always the newest picture, never a queue
            if (!cameraFeed.Grab()) return;                  // the camera has no new picture in this frame: next frame
            nextLook = Time.unscaledTime + 1f / Mathf.Max(1f, looksPerSecond);
            Look();
            MaybeAskEarly();
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

            // the focus helper sees every look: it locks the focus when the camera keeps losing it, and renews a lock that went soft
            bool moving = fault == FrameQuality.Fault.Moving;
            bool judged = fault != FrameQuality.Fault.Dark && !moving && measure.edges >= FrameQuality.MinimumEdges;
            if (focusAssistant != null) focusAssistant.Looked(moving, judged, judged && measure.sharpness < FrameQuality.BlurBelow);
            if (moving && tracer.Following) { tracer.Moved(); ShowTrace(); }       // the text is no longer where it was
            lastSharpness = measure.sharpness;
            ShowReadout();

            // too dark: nothing in it can be trusted. (The flashlight helper of this screen deals with the dark.)
            if (fault == FrameQuality.Fault.Dark) { blurrySkips = 0; return; }

            // Moving: text is smeared, but if a barcode/QR code is needed and movement is moderate (< 0.25f), read it!
            bool wantBarcodeNow = readBarcode && barcode.Length == 0;
            if (moving && (!wantBarcodeNow || measure.motion > 0.25f)) { blurrySkips = 0; return; }
            if (fault == FrameQuality.Fault.Blurry && !wantBarcodeNow)
            {
                // out of focus: skipped. After several in a row one is read anyway and the camera is asked to focus again,
                // so a phone whose camera never looks "sharp" to this check can still scan (the text reader then decides).
                if (++blurrySkips < Mathf.Max(1, blurrySkipsBeforeReadingAnyway)) return;
                blurrySkips = 0;
                if (focusAssistant == null || !focusAssistant.LooksAfterFocus) cameraFeed.Refocus();      // a locked focus is renewed by the focus helper
            }
            else blurrySkips = 0;
            // glare: the picture is read; lines the reader is not sure of count for nothing (ScanEvidence), and the hint stays

            // THE TEXT TRACER: while it follows small print, every other picture is a close look at that block, enlarged
            tracer.CloseLooks = closeLooksAtSmallPrint; tracer.UsableLine = ScanEvidence.UsableLine;
            tracer.SmallBelowPixels = smallPrintBelowPixels; tracer.GoodPixels = closeLookLetterPixels; tracer.MostEnlarged = closeLookMostEnlarged;
            sentCloseLook = tracer.Next(out sentPart, out float enlarge);
            if (sentCloseLook && !cameraFeed.UprightArgb(frame, sentPart, enlarge, readPictureSize, ref closePixels, out closeW, out closeH, out _)) sentCloseLook = false;
            if (!sentCloseLook)
            {
                sentPart = new Rect(0f, 0f, 1f, 1f);
                if (!cameraFeed.UprightArgb(frame, readPictureSize, ref pixels, out pixelsW, out pixelsH)) return;
                pixelsOfScan = scanNumber;
                if (cameraFeed.FramePixels(frame, out int wide, out int high)) { framePixelsHigh = high; frameWide = wide; frameHigh = high; }
                string colour = cameraFeed.DominantColourHex(frame);         // only what is inside the scan frame, not the hand or the table round it
                if (!string.IsNullOrEmpty(colour)) lastColour = colour;
                StabiliseShade(colour);
                if (capWatch.Hidden && capWatch.Look(colour)) { lockedShade = ""; shadeCandidate = ""; shadeStableCount = 0; ShadeLook(); }    // a new colour stays in view: the cap is probably off now
            }
            if (sentCloseLook) { closeLooksSent++; lastEnlarge = enlarge; }
            sentSharpness = measure.sharpness; sentAt = Time.unscaledTime;
            readingOut = sentCloseLook ? reader.Read(closePixels, closeW, closeH, false, OnReaderAnswer)             // a block of text: no barcode is looked for
                                       : reader.Read(pixels, pixelsW, pixelsH, wantBarcodeNow, OnReaderAnswer, OnEarlyBarcode);
        }

        /// <summary>Shows where the text tracer is, and tells the focus helper to focus there (on the text, not just the middle of the frame).</summary>
        void ShowTrace()
        {
            if (textTrace != null) textTrace.Show(tracer.Following, tracer.Block, tracer.SmallPrint && closeLooksAtSmallPrint);
            if (focusAssistant != null)
                focusAssistant.FocusOn = tracer.Following && textTrace != null && textTrace.Box != null ? textTrace.Box : (guide != null ? guide.Frame : null);
        }

        /// <summary>
        /// The scan readout: one small line of numbers on the camera picture, for finding out on a phone WHY a scan is slow
        /// (letters too small, picture soft, reader unsure). Only while "Show Scan Readout" is on.
        /// </summary>
        void ShowReadout()
        {
            if (scanReadout != null && scanReadout.activeSelf != showScanReadout) scanReadout.SetActive(showScanReadout);
            if (!showScanReadout || scanReadoutText == null || cameraFeed == null) return;
            Vector2Int camera = cameraFeed.PictureSize;
            string picture = fault == FrameQuality.Fault.None ? "ok" : fault.ToString().ToLowerInvariant();
            string letters = tracer.Following ? Mathf.RoundToInt(tracer.LetterPixels) + " px" + (tracer.SmallPrint && closeLooksAtSmallPrint ? " (close look x" + lastEnlarge.ToString("0.0") + ")" : "") : "-";
            string focus = !cameraFeed.CanLockFocus ? "fixed" : cameraFeed.FocusLocked ? "locked" : "automatic";
            scanReadoutText.text = "Camera " + camera.x + " x " + camera.y + "   frame " + frameWide + " x " + frameHigh + " px   picture " + picture
                                   + "   sharp " + lastSharpness.ToString("0.00") + "\nLetters " + letters + "   lines " + readLines + "   sure " + readSure.ToString("0.00")
                                   + "   close looks " + closeLooksSent + "   focus " + focus
                                   + "\nScan " + scanningFor.ToString("0.0") + " s   AI " + (earlyOut ? "looking" : earlyTook >= 0f ? earlyTook.ToString("0.0") + " s" : earlyAsked ? "no answer" : "-")
                                   + "   web " + (webOut ? "searching" : webTook >= 0f ? webTook.ToString("0.0") + " s" : "-") + (capWatch.Hidden ? "   cap on" : "");
        }

        // called on the text reader's own thread: only the answer is stored
        void OnReaderAnswer(FrameReading reading, string problem)
        {
            if (problem != null) Debug.LogWarning("[Scan] Could not read this picture: " + problem);
            lock (answerLock) { answerReading = reading ?? new FrameReading(); answerWaiting = true; }
        }

        // called on the barcode reader's thread as soon as a barcode/QR code is found (10-25ms)
        void OnEarlyBarcode(string codeDetails)
        {
            if (string.IsNullOrEmpty(codeDetails) || barcode.Length > 0) return;
            string code = codeDetails.Split('\t')[0].Trim();
            if (code.Length == 0) return;
            lock (barcodeLock) { earlyBarcodeDetected = code; }
        }

        void TakeEarlyBarcode()
        {
            string code = null;
            lock (barcodeLock)
            {
                if (!string.IsNullOrEmpty(earlyBarcodeDetected))
                {
                    code = earlyBarcodeDetected;
                    earlyBarcodeDetected = null;
                }
            }
            if (!string.IsNullOrEmpty(code) && barcode.Length == 0)
            {
                TakeBarcode(code);
                if (barcode.Length > 0)
                {
                    if (guide != null) guide.SetState(ScanGuideOverlay.State.Detected);
                    if (vibrate) Haptics.Tick();
                    bool[] filled = evidence.ProductKnown();
                    ShowProgress(filled);
                    // Instant finish if product with ingredients is in inventory (QR code speed)
                    if (knownProduct != null && knownProduct.ingredients != null && knownProduct.ingredients.Count >= 3)
                    {
                        evidence.ListKnown();
                        UseLookedUpList(knownProduct.ingredients, ListPagesOf(knownProduct), "inventory", false);
                    }
                    else MaybeCheckTheWeb();
                }
            }
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
            // a close look showed one part of the frame: its places become places of the frame, like every other reading's
            bool closeLook = sentCloseLook;
            if (closeLook) TextTracer.ToFrame(reading, sentPart);
            reading = ScanEvidence.PrimaryLabelOnly(reading);
            tracer.Take(reading, framePixelsHigh, closeLook);
            ShowTrace();
            readLines = 0; readSure = 0f;
            foreach (FrameReading.Line line in reading.lines)
                if (line != null && line.confidence >= ScanEvidence.UsableLine && !string.IsNullOrWhiteSpace(line.text)) { readLines++; readSure += line.confidence; }
            if (readLines > 0) readSure /= readLines;
            ShowReadout();
            if (textOutlines != null) textOutlines.Show(FrameReader.BoxesOf(reading, ScanEvidence.UsableLine));
            string text = reading.Text(ScanEvidence.UsableLine);

            // ---- what the app's own lists know from the primary label (works offline)
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

            ScanEvidence.Result result = evidence.Add(reading, known, sentSharpness, closeLook, false);
            if (!closeLook) KeepBest(result);                // the kept pictures show the whole frame (`pixels`), never a close look

            // ---- the barcode: a product of the inventory is recognised at once
            if (barcode.Length == 0 && evidence.Barcode.Length > 0) TakeBarcode(evidence.Barcode);
            // ---- the whole name, read in pieces: the brand in one picture, the product's name in the next
            if (!ProductKnown && result.zones[(int)ScanZone.Front])
            {
                ProductData whole = ProductInFront();
                if (whole != null) Adopt(whole);
            }
            if (knownProduct == null) ReadPrimaryIdentityFromOcr();
            StartFirebaseProductLookup(false);
            // ---- the look of the pack: a product this phone has scanned before. The app ASKS ("Is this ...?"), it does not decide.
            if (identity.shadeName.Length == 0 && evidence.ShadeText.Length > 0) identity.shadeName = evidence.ShadeText;

            bool[] filled = result.filledNow;
            if (ProductKnown && !evidence.IsFull(ScanZone.Front)) Merge(filled, evidence.ProductKnown());

            // ---- make-up only: stop as soon as the product is known to be something else
            if (StopIfNotMakeup(result.zones[(int)ScanZone.Ingredients] ? null : text)) return;

            // ---- a product of the inventory brings its ingredient list with it: nothing more to scan.
            //      The confirmed catalogue list avoids carrying forward OCR fragments from multiple frames.
            if (knownProduct != null && knownProduct.ingredients != null && knownProduct.ingredients.Count >= 3)
            {
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
            if ((evidence.AllFull || RingComplete) && !finishUnnamed && !webOut) FinishAttempt();
        }

        // ---------------------------------------------------------------------------------------
        //  The ring: what was DETECTED (brand, product name, cosmetic type, barcode, ingredient list)
        // ---------------------------------------------------------------------------------------
        bool IsScanning => stage == Stage.Scanning && holdButton != null && holdButton.IsScanning;

        /// <summary>The five segments, 0 to 1 each, in the ring's order.</summary>
        float[] RingParts()
        {
            return new[]
            {
                identity.brandName.Length > 0 ? 1f : 0f,
                identity.productName.Length > 0 ? 1f : 0f,
                identity.cosmeticType.Length > 0 ? 1f : 0f,
                barcode.Length > 0 || evidence.Barcode.Length > 0 ? 1f : 0f,
                ingredientText != null ? 1f : evidence.Progress(ScanZone.Ingredients)
            };
        }

        int RingCount { get { int n = 0; foreach (float part in RingParts()) if (part >= 1f) n++; return n; } }

        /// <summary>Brand, name, kind and ingredient list are all detected. (The barcode lights up only when one is visible; a pack without one is still complete.)</summary>
        bool RingComplete { get { float[] p = RingParts(); return p[0] >= 1f && p[1] >= 1f && p[2] >= 1f && p[4] >= 1f; } }

        void ShowRing() {         if (ring != null) ring.ShowParts(RingParts(), IsScanning); }

        static void Merge(bool[] into, bool[] more) { for (int i = 0; i < into.Length && i < more.Length; i++) into[i] |= more[i]; }

        /// <summary>The ring, the checklist and the buttons after a picture; a buzz for each segment that became full.</summary>
        void ShowProgress(bool[] filledNow)
        {
            ShowRing();
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
        /// <summary>
        /// The Start Scan button's state, in one place: while the camera starts or the product is analyzed it is LOCKED (no
        /// taps, dimmed, "Starting..." / "Analyzing..." on it, the ring keeps moving); otherwise it says "Tap to scan" or "Tap to pause".
        /// </summary>
        void PaintButtonState()
        {
            bool busy = stage == Stage.Starting || stage == Stage.Recognising || stage == Stage.Leaving;
            bool locked = lockButtonWhileBusy && busy;
            if (holdButton != null) holdButton.TapsIgnored = locked;
            bool scanning = stage == Stage.Scanning && holdButton != null && holdButton.IsScanning;
            if (ring != null) ring.SetScanning(scanning || locked);
            if (scanButtonLabel != null)
                scanButtonLabel.text = locked ? (stage == Stage.Starting ? startingLabel : busyLabel) : (scanning ? scanPauseLabel : scanIdleLabel);
        }

        void ShowGuidance()
        {
            PaintButtonState();
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            bool scanning = stage == Stage.Scanning && holdButton != null && holdButton.IsScanning;
            if (stage != Stage.Scanning) return;

            // the status line. A message that was said in its place stays until the status itself changes.
            string status = StatusNow();
            if (messageOver != null && status != messageOver) messageOver = null;
            if (messageOver == null && status != shownStatus)
            {
                if (statusText != null) statusText.text = status;
                shownStatus = status;
            }
            // the hint follows the RING (brand, name, kind, ingredient list, barcode), so it never asks for something the ring does not show
            float[] parts = RingParts();
            var ringNow = new ScanGuidance.Ring { brand = parts[0] >= 1f, name = parts[1] >= 1f, kind = parts[2] >= 1f, barcode = parts[3] >= 1f, list = parts[4] };
            string hint = webOut ? msgWebChecking : ScanGuidance.HintForRing(words, scanning ? fault : FrameQuality.Fault.None, ringNow);
            if (hint.Length == 0) hint = hintAllDone;
            if (hint != lastHint && guide != null) { lastHint = hint; guide.SetHint(hint); }
        }

        /// <summary>"Scanning, 2 of 5", "Paused at 2 of 5 ...", or the first instruction.</summary>
        string StatusNow()
        {
            bool scanning = stage == Stage.Scanning && holdButton != null && holdButton.IsScanning;
            return ScanGuidance.Status(words, scanning, evidence.Pictures > 0, false);
        }

        /// <summary>
        /// FINISH is offered when the ingredient list is complete (read to its end, or known already), and also for a
        /// list that is only partly read once the user has let go of the button or the scan is not getting further:
        /// nobody is made to wait for a ring that will not fill. START OVER is offered as soon as there is anything to forget.
        /// </summary>
        void ShowButtons()
        {
            PaintButtonState();
            bool busy = stage == Stage.Recognising || stage == Stage.Leaving;
            bool scanning = stage == Stage.Scanning && holdButton != null && holdButton.IsScanning;
            bool listComplete = ingredientText != null;
            if (finishButton != null) finishButton.gameObject.SetActive(!busy && listComplete);
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

        /// <summary>
        /// The colour the camera sees changes with every picture (light, hand, angle). It becomes the shade only after ShadeStableReadings readings in a
        /// row agree, and then it stays (until Start over, or the cap comes off), so the shown colour does not keep changing.
        /// </summary>
        void StabiliseShade(string colour)
        {
            if (lockedShade.Length > 0 || string.IsNullOrEmpty(colour) || capWatch.Hidden) return;
            if (shadeCandidate.Length > 0 && SameColour(shadeCandidate, colour)) shadeStableCount++;
            else { shadeCandidate = colour; shadeStableCount = 1; }
            if (shadeStableCount >= ShadeStableReadings) { lockedShade = shadeCandidate; ShowIdentity(); }
        }

        static bool SameColour(string a, string b)
        {
            Color x = ShadeAdvisor.Parse(a, Color.clear), y = ShadeAdvisor.Parse(b, Color.clear);
            return Mathf.Abs(x.r - y.r) < 0.06f && Mathf.Abs(x.g - y.g) < 0.06f && Mathf.Abs(x.b - y.b) < 0.06f;
        }

        void ShowIdentity()
        {
            if (productCheck != null && identity.productName.Length > 0) productCheck.SetFound(identity.productName);
            if (brandCheck != null && identity.brandName.Length > 0) brandCheck.SetFound(identity.brandName);
            if (typeCheck != null && identity.cosmeticType.Length > 0) typeCheck.SetFound(identity.cosmeticType);

            // the shade: its printed name, what the AI saw, otherwise (for a colour cosmetic) the colour the pictures agree on
            // WITH THE CAP ON (the AI saw it) the colour the camera sees is the cap's or the tube's: it is never the shade.
            string hex = identity.shadeHex;
            if (hex.Length == 0 && ShadeAdvisor.IsColourCosmetic(identity.cosmeticType) && !capWatch.Hidden)
                hex = lockedShade;                       // not the colour of this very picture: the one that stayed the same
            hex = ShadeAdvisor.ShadeFor(identity.cosmeticType, hex);          // a face powder is white, whatever colour its box has
            if (ShadeAdvisor.IsFacePowder(identity.cosmeticType)) identity.shadeHex = hex;
            if (hex.Length == 0 && identity.shadeName.Length == 0)
            {
                if (capWatch.Hidden && shadeCheck != null && !shadeCheck.Found) shadeCheck.SetLabel(shadeCapOnLabel);
                return;
            }
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

        void ReadPrimaryIdentityFromOcr()
        {
            string front = DistinctLines(evidence.FrontText);
            if (front.Length == 0) return;
            if (identity.brandName.Length == 0) BrandIn(front);
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = ProductScope.TypeFromLabel(front);
            if (identity.productName.Length > 0) return;

            string best = "";
            foreach (string line in front.Split('\n'))
            {
                string candidate = line.Trim();
                if (identity.brandName.Length > 0)
                {
                    candidate = Regex.Replace(candidate, Regex.Escape(identity.brandName), " ", RegexOptions.IgnoreCase);
                    foreach (BrandData brand in brands)
                    {
                        if (brand == null || string.IsNullOrWhiteSpace(brand.name) || !ProductIndex.BrandsAgree(identity.brandName, brand.name)) continue;
                        candidate = Regex.Replace(candidate, Regex.Escape(brand.name ?? ""), " ", RegexOptions.IgnoreCase);
                        if (brand.aliases != null)
                            foreach (string alias in brand.aliases)
                                if (!string.IsNullOrWhiteSpace(alias))
                                    candidate = Regex.Replace(candidate, Regex.Escape(alias), " ", RegexOptions.IgnoreCase);
                    }
                }
                foreach (string type in ProductScope.MakeupTypes)
                    candidate = Regex.Replace(candidate, @"\b" + Regex.Escape(type) + @"\b", " ", RegexOptions.IgnoreCase);
                candidate = Regex.Replace(candidate, @"\b(new|improved|shade|color|colour|cosmetic|beauty|spf|uva|uvb)\b", " ", RegexOptions.IgnoreCase);
                candidate = Regex.Replace(candidate, @"\s+", " ").Trim(' ', '-', ':', ',', '|');
                if (candidate.Length < 3 || candidate.Length > 80 || IngredientStoplist.SmallPrintHit(candidate) != null
                    || IngredientStoplist.WrongShape(candidate)) continue;
                if (candidate.Length > best.Length) best = candidate;
            }
            if (best.Length > 0) identity.productName = best;
        }

        /// <param name="confirmed">the name was settled by a step that already checked it (shortlist, AI answer, list lookup): no wait</param>
        void StartFirebaseProductLookup(bool confirmed)
        {
            if (stage != Stage.Scanning || firebaseLookupOut || knownProduct != null || identity.brandName.Length < 2 || identity.productName.Length < 2) return;
            string key = TextKey.Normalize(identity.brandName) + "|" + TextKey.Normalize(identity.productName);
            if (key == firebaseLookupKey) return;
            // the label is read piece by piece and often misread: the per-reading call asks the server only when the same brand and name
            // showed up on two readings in a row, lookupStableSeconds apart (the first sighting only starts the clock).
            // A label that never reads the same twice is covered by the Look up button and manual type-in, which never wait.
            if (!confirmed)
            {
                if (key != stableKey) { stableKey = key; stableSince = Time.unscaledTime; return; }
                if (Time.unscaledTime - stableSince < lookupStableSeconds) return;
            }
            firebaseLookupKey = key;
            firebaseLookupOut = true;
            FirebaseProductLookup(identity.brandName, identity.productName, scanNumber, key);
        }

        async void FirebaseProductLookup(string brand, string name, int scan, string key)
        {
            try
            {
                ProductData product = ProductRepository.Index.Exact(brand, name);
                if (product == null || product.ingredients == null || product.ingredients.Count == 0)
                {
                    ProductData current = await ProductRepository.FindByBrandAndNameAsync(brand, name);
                    if (current != null) product = current;
                }
                if (this == null || scan != scanNumber || stage != Stage.Scanning || key != firebaseLookupKey) return;
                if (product != null)
                {
                    Adopt(product);
                    ShowIdentity();
                    if (HasStoredList(product))
                    {
                        UseLookedUpList(product.ingredients, ListPagesOf(product), "inventory", false);
                        return;
                    }
                }

                // not in the catalogue: the user's own earlier photo of it may be staged (at most one read; none when looked at recently). Entries of other users are never used.
                if (product == null)
                {
                    PendingProductData staged = await PendingProductRepository.FindAsync(brand, name);
                    if (this == null || scan != scanNumber || stage != Stage.Scanning) return;
                    if (staged != null && staged.ingredients.Count > 0) { Say(msgFoundStagedOwn); UseStaged(staged); return; }
                }

                if (await SearchWebAfterCatalogueMiss(brand, name, scan)) return;
                if (this != null && scan == scanNumber && stage == Stage.Scanning)
                    Say(!ConnectivityMonitor.Online ? msgFirebaseUnavailable
                        : AiCanLook ? msgWebNoMatch
                        : !ProductAiAdvisor.Available ? msgAiUnavailable
                        : product == null ? msgFirebaseMiss : msgFirebaseNoIngredients);
                if (product == null && offerSnapshotOnCatalogMiss && this != null && scan == scanNumber && stage == Stage.Scanning) OfferSnapshot(null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Firebase product lookup failed: " + e.Message);
                if (this != null && scan == scanNumber && stage == Stage.Scanning) Say(msgFirebaseUnavailable);
            }
            finally
            {
                if (this != null && scan == scanNumber) firebaseLookupOut = false;
            }
        }

        async Task<bool> SearchWebAfterCatalogueMiss(string brand, string name, int scan)
        {
            if (!aiRecognisesProduct || !aiChecksTheWeb || !ConnectivityMonitor.Online || !ProductAiAdvisor.Available
                || !AiBudget.CanLookUp || stage != Stage.Scanning || scan != scanNumber)
                return false;

            Kept shot = best[(int)ScanZone.Front] ?? (pixels != null && pixelsOfScan == scanNumber
                ? new Kept { pixels = pixels, width = pixelsW, height = pixelsH }
                : null);
            byte[] jpeg = shot != null ? ToJpg(shot) : null;
            if (jpeg == null || jpeg.Length == 0) return false;

            webOut = true;
            float askedAt = Time.unscaledTime;
            Say(msgWebChecking);
            ShowGuidance();
            try
            {
                string text = DistinctLines(evidence.FrontText);
                ProductAiAdvisor.Identity guess;
                if (aiNamed && earlyJpg != null)
                {
                    guess = new ProductAiAdvisor.Identity
                    {
                        found = true,
                        productName = identity.productName,
                        brandName = identity.brandName,
                        cosmeticType = identity.cosmeticType,
                        shadeName = identity.shadeName
                    };
                }
                else
                {
                    if (!AiBudget.TakeRecognition()) return false;
                    ProductAiAdvisor.Identity visual = await ProductAiAdvisor.Identify(jpeg, text);
                    if (this == null || scan != scanNumber || stage != Stage.Scanning) return false;
                    if (visual != null && visual.found && visual.confidence >= minimumAiConfidence)
                    {
                        if (!string.IsNullOrWhiteSpace(visual.productName)) identity.productName = visual.productName.Trim();
                        if (!string.IsNullOrWhiteSpace(visual.brandName)) identity.brandName = visual.brandName.Trim();
                        if (!string.IsNullOrWhiteSpace(visual.cosmeticType)) identity.cosmeticType = visual.cosmeticType.Trim();
                        if (!string.IsNullOrWhiteSpace(visual.shadeName)) identity.shadeName = visual.shadeName.Trim();
                        if (!string.IsNullOrWhiteSpace(visual.shadeHex)) { identity.shadeHex = visual.shadeHex; identity.shadeFrom = visual.shadeFrom; }
                        aiNamed = visual.productName.Length > 0;
                        ShowIdentity();

                        ProductData corrected = InventoryProductFor(identity.productName, identity.brandName);
                        if (!HasStoredList(corrected) && identity.productName.Length > 1 && identity.brandName.Length > 1)
                            corrected = await ProductRepository.FindByBrandAndNameAsync(identity.brandName, identity.productName);
                        if (this == null || scan != scanNumber || stage != Stage.Scanning) return false;
                        if (HasStoredList(corrected))
                        {
                            Adopt(corrected);
                            UseLookedUpList(corrected.ingredients, ListPagesOf(corrected), "inventory", false);
                            return true;
                        }
                        if (corrected != null) Adopt(corrected);
                    }

                    guess = new ProductAiAdvisor.Identity
                    {
                        found = identity.productName.Length > 0 || identity.brandName.Length > 0,
                        productName = identity.productName,
                        brandName = identity.brandName,
                        cosmeticType = identity.cosmeticType,
                        shadeName = identity.shadeName
                    };
                }

                if (!AiBudget.TakeLookUp()) return false;
                ProductAiAdvisor.WebMatch match = await ProductAiAdvisor.ConfirmOnWeb(jpeg, guess, text, barcode);
                if (this == null || scan != scanNumber || stage != Stage.Scanning) return false;
                webTook = Time.unscaledTime - askedAt;
                if (match == null || !match.same || match.confidence < minimumAiConfidence)
                {
                    if (match != null && !string.IsNullOrWhiteSpace(match.question)) Say(match.question);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(match.productName)) identity.productName = match.productName.Trim();
                if (!string.IsNullOrWhiteSpace(match.brandName)) identity.brandName = match.brandName.Trim();
                if (!string.IsNullOrWhiteSpace(match.cosmeticType)) identity.cosmeticType = match.cosmeticType.Trim();
                if (match.notMakeup && StopIfNotMakeupNow()) return true;
                ShowIdentity();
                AdoptIfListed();

                ProductData listed = knownProduct ?? InventoryProductFor(identity.productName, identity.brandName);
                if (HasStoredList(listed))
                {
                    Adopt(listed);
                    UseLookedUpList(listed.ingredients, ListPagesOf(listed), "inventory", false);
                    return true;
                }
                if (match.list == null || !match.list.found || match.list.sources == null || match.list.sources.Count == 0)
                    return false;

                foreach (ProductAiAdvisor.HiddenAdditive hidden in match.list.hidden)
                {
                    try { HiddenAdditives.Learn(hidden.term, hidden.substances, hidden.note, match.list.sources, match.list.webSearched, match.list.model); }
                    catch (Exception e) { Debug.LogWarning("Hidden ingredients were not saved: " + e.Message); }
                }
                UseLookedUpList(match.list.ingredients, match.list.sources, "web", match.list.webSearched);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Scan: the Firebase-miss product web search failed: " + e.Message);
                return false;
            }
            finally
            {
                if (this != null && scan == scanNumber)
                {
                    webOut = false;
                    if (stage != Stage.Leaving && stage != Stage.Idle)
                    {
                        messageOver = null;
                        shownStatus = null;
                        ShowGuidance();
                    }
                }
            }
        }

        void Adopt(ProductData product)
        {
            knownProduct = product;
            identity.productName = (product.name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(product.brandName)) identity.brandName = product.brandName.Trim();
            if (!string.IsNullOrWhiteSpace(product.cosmeticType)) identity.cosmeticType = product.cosmeticType.Trim();
        }

        async Task<bool> SearchTypedProductOnWeb(string productName, string brandName, string kind, bool showInManualPanel)
        {
            if (!aiChecksTheWeb || !ConnectivityMonitor.Online || !ProductAiAdvisor.Available || !AiBudget.TakeLookUp())
                return false;

            int scan = scanNumber;
            webOut = true;
            Say(msgWebChecking);
            try
            {
                ProductAiAdvisor.WebMatch match = await ProductAiAdvisor.FindTyped(productName, brandName, kind);
                if (this == null || scan != scanNumber || stage == Stage.Leaving) return false;
                if (match != null && match.notMakeup)
                {
                    string message = ProductScope.StopMessage(ProductScope.Judge(match.cosmeticType, match.productName));
                    if (showInManualPanel) ManualSay(message); else Say(message);
                    return false;
                }
                if (match == null || !match.same || match.confidence < minimumAiConfidence)
                {
                    if (showInManualPanel && match != null && match.question.Length > 0) ManualSay(match.question);
                    else if (!showInManualPanel && match != null && match.question.Length > 0) Say(match.question);
                    return false;
                }
                if (match.list == null || !match.list.found || match.list.sources == null || match.list.sources.Count == 0)
                    return false;

                identity.productName = match.productName.Trim();
                identity.brandName = match.brandName.Trim();
                if (!string.IsNullOrWhiteSpace(match.cosmeticType)) identity.cosmeticType = match.cosmeticType.Trim();
                ShowIdentity();
                UseLookedUpList(match.list.ingredients, match.list.sources, "web", match.list.webSearched);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Scan: typed-product web search failed: " + e.Message);
                return false;
            }
            finally
            {
                if (this != null && scan == scanNumber)
                {
                    webOut = false;
                    if (stage != Stage.Leaving && stage != Stage.Idle)
                    {
                        messageOver = null;
                        shownStatus = null;
                        ShowGuidance();
                    }
                }
            }
        }

        /// <summary>True when a brand the app knows stands in the text. The first one found is taken over when no brand is known yet.</summary>
        bool BrandIn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string seen = " " + TextKey.Normalize(text) + " ";
            string[] lines = text.Split('\n');
            foreach (BrandData brand in brands)
            {
                string name = TextKey.Normalize(brand != null ? brand.name : "");
                if (name.Length < 3) continue;
                bool found = seen.Contains(" " + name + " ");
                if (!found && brand.aliases != null)
                    foreach (string alias in brand.aliases)
                    {
                        string key = TextKey.Normalize(alias);
                        if (key.Length >= 3 && seen.Contains(" " + key + " ")) { found = true; break; }
                    }
                if (!found)
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (ProductIndex.BrandsAgree(line, brand.name)) { found = true; break; }
                        if (brand.aliases != null)
                            foreach (string alias in brand.aliases)
                                if (ProductIndex.BrandsAgree(line, alias)) { found = true; break; }
                        if (found) break;
                    }
                if (!found) continue;
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
            finishUnnamed = false;
            bool[] filled = evidence.ProductKnown();
            if (stage == Stage.Idle || stage == Stage.Scanning) { if (StopIfNotMakeupNow()) return; }
            ShowProgress(filled);
            if (stage == Stage.Scanning) MaybeCheckTheWeb();
            next?.Invoke();
        }

        /// <summary>After "Yes, it is this product" during the scan: a product of the inventory with a stored list needs nothing more.</summary>
        void ContinueScanning()
        {
            if (knownProduct == null || knownProduct.ingredients == null || knownProduct.ingredients.Count < 3 || (listFrom != "" && listFrom != "pack")) return;
            bool[] filled = evidence.ListKnown();
            ShowProgress(filled);
            UseLookedUpList(knownProduct.ingredients, ListPagesOf(knownProduct), "inventory", false);
        }

        bool PopupOpen => (shortlist != null && shortlist.IsOpen) || (startOverPanel != null && startOverPanel.IsOpen) || (manualPanel != null && manualPanel.IsOpen)
                          || (snapshot != null && snapshot.IsOpen);

        /// <summary>What the app's own lists suggest for this scan: by the words read on the front, and by the look of the pack.</summary>
        List<ScanShortlist.Entry> Suggestions()
        {
            return ScanShortlist.For(ProductRepository.Index, DistinctLines(evidence.FrontText), null, declined);
        }

        /// <summary>
        /// Opens "Is it one of these?". `named` runs after the user chose or typed a product, `noneOfThese` when they said
        /// none fits. "Scan again" starts a fresh ring.
        /// </summary>
        void OpenShortlist(string title, string message, string noneLabel, Action named, Action noneOfThese, List<ScanShortlist.Entry> entries = null)
        {
            if (shortlist == null) { noneOfThese?.Invoke(); return; }
            if (holdButton != null) holdButton.LetGo();
            shortlist.Show(title, message, entries ?? Suggestions(), noneLabel,
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
        /// Takes over a barcode or QR code the camera has read. A product in the inventory with that barcode or code is the product being
        /// scanned: its name, brand and kind are filled in (its saved ingredient list is used by the caller).
        /// </summary>
        void TakeBarcode(string seen)
        {
            if (string.IsNullOrWhiteSpace(seen)) return;
            string raw = seen.Trim();
            string key = MlKitBarcodeReader.Key(raw);
            ProductData p = null;
            if (key.Length > 0)
            {
                barcode = key;
                p = ProductRepository.Index.ByBarcode(key);
            }
            if (p == null)
            {
                p = ProductRepository.Index.ById(raw) ?? ProductRepository.Index.Exact("", raw);
                if (p != null && barcode.Length == 0) barcode = raw;
            }
            if (p != null)
            {
                Adopt(p);
                evidence.ProductKnown();
            }
            else if (key.Length > 0)
            {
                NameFromBarcode(key);
            }
        }

        /// <summary>
        /// A barcode the inventory does not know: Open Beauty Facts is asked what product carries it (only the barcode's
        /// digits are sent). Its answer fills in the name and the brand ONLY where the scan has read nothing itself, so
        /// what is printed on the user's own pack always comes first. The ingredient list is not taken here: the pack is
        /// still being read, and the look-up (which also gets the barcode) follows the order of trust.
        /// </summary>
        void NameFromBarcode(string code)
        {
            // Barcode decoding remains on-device. Product details are resolved only through the local/Firebase catalogue.
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
            snapshotOffered = false;
            evidence.Reset();
            for (int i = 0; i < best.Length; i++) best[i] = null;
            ingredientText = null; ingredientSource = "scan"; listFrom = ""; listWebBacked = false; sources.Clear();
            barcode = ""; lastColour = "";
            shadeCandidate = lockedShade = ""; shadeStableCount = 0; photoHintSaid = false;
            declined.Clear(); shortlistAnswered = false; detailsAnswered = false; frontChecked = "";
            scanNumber++;
            ReadCounter.NewScan();
            fault = FrameQuality.Fault.None; blurrySkips = 0; differentSeen = 0; sinceProgress = 0f; stalledSaid = false; pausedFor = 0f;
            finishUnnamed = false; lookingUp = false; lookupRequested = false; shownStatus = null; messageOver = null; lastHint = "";
            lock (answerLock) { answerWaiting = false; answerReading = null; }
            lock (barcodeLock) { earlyBarcodeDetected = null; }
            overviewBefore = null;
            tracer.Reset(); sentCloseLook = false;
            if (textTrace != null) textTrace.Clear();
            if (textOutlines != null) textOutlines.Clear();
            // the AI's early look, the web check and the look for the shade belong to the scan before (scanNumber tells their answers so)
            scanningFor = 0f; earlyTook = webTook = -1f;
            firebaseLookupOut = false; firebaseLookupKey = ""; stableKey = "";
            earlyAsked = earlyOut = webAsked = webOut = shadeOut = aiNamed = capSaid = false;
            earlyJpg = null; webList = null;
            capWatch.Reset();

            bool keep = sameProduct && ProductKnown;
            if (!keep)
            {
                identity.found = false; identity.confidence = 0f;
                identity.productName = identity.brandName = identity.cosmeticType = identity.shadeHex = identity.shadeName = identity.finish = "";
                knownProduct = null;
                rescanSame = false;              // a different product: it still takes the place of the scan this one was started from
            }
            else { identity.shadeHex = identity.shadeName = identity.finish = ""; }
            identity.capOn = false; identity.shadeFrom = "";
            ResetChecklist();
            SetWorking(false);
            if (ring != null) ring.Clear();
            if (keep) { evidence.ProductKnown(); ShowIdentity(); ShowRing(); }
            ShowButtons();
            if (!string.IsNullOrEmpty(message)) Say(message);
            ShowGuidance();
        }

        // ---------------------------------------------------------------------------------------
        //  The AI helps while the scan goes on
        // ---------------------------------------------------------------------------------------
        bool AiCanLook => aiRecognisesProduct && ProductAiAdvisor.Available && ConnectivityMonitor.Online;
        const float EarlyLookWaitSeconds = 10f;           // Finish waits this long at most for an early look that is on its way

        /// <summary>
        /// A product the app's own lists do not have has no name the app could read: on its pack brand, name and claims
        /// are all just words. Waiting for the end to ask the AI made such a scan long. So the AI is asked EARLY, once, in
        /// the background: after the app's lists and the barcode had their moment, with one picture, while the user goes
        /// on showing the product. (It counts as one of the AI's few questions per visit, like the question at the end.)
        /// </summary>
        void MaybeAskEarly()
        {
            if (!aiHelpsEarly || earlyAsked || identity.productName.Length > 0 || knownProduct != null) return;
            if (scanningFor < aiHelpAfterSeconds || PopupOpen || pixels == null || pixelsW <= 0 || pixelsOfScan != scanNumber) return;
            if (!AiCanLook || !AiBudget.CanRecognise) return;
            EarlyLook();
        }

        async void EarlyLook()
        {
            earlyAsked = true;
            int scan = scanNumber;
            // the best picture of the front so far; otherwise the newest picture that was good enough to be read
            Kept shot = best[(int)ScanZone.Front] ?? new Kept { pixels = pixels, width = pixelsW, height = pixelsH };
            byte[] jpg = ToJpg(shot);
            if (jpg == null || !AiBudget.TakeRecognition()) return;
            earlyJpg = jpg; earlyOut = true; earlyAskedAt = Time.unscaledTime;
            SetWorking(true);
            string text = evidence.FrontText.Length > 0 ? evidence.FrontText : evidence.Text.AllText(ScanEvidence.UsableLine);
            ProductAiAdvisor.Identity seen = null;
            try { seen = await ProductAiAdvisor.Identify(jpg, text); }
            catch (Exception e) { Debug.LogWarning("Scan: the AI's early look failed: " + e.Message); }
            if (this == null || scan != scanNumber) return;          // the screen was left, or a new scan was started meanwhile
            earlyOut = false; earlyTook = Time.unscaledTime - earlyAskedAt;
            SetWorking(false);
            if (stage == Stage.Leaving || stage == Stage.Idle) return;
            if (!TakeAiAnswer(seen)) return;
            if (stage != Stage.Scanning) return;                     // the scan is being finished: that step goes on with what is known now
            if (StopIfNotMakeup()) return;                           // the AI says it is not make-up
            AdoptIfListed();
            ShowProgress(ProductKnown ? evidence.ProductKnown() : null);
            if (PopupOpen) return;
            if (HasStoredList(knownProduct)) { ContinueScanning(); return; }       // a product of the inventory brings its list with it
            MaybeCheckTheWeb();
            // the ingredient list may have been read while the AI was looking: then everything is there
            if ((evidence.AllFull || RingComplete) && !finishUnnamed && !webOut) FinishAttempt();
        }

        /// <summary>
        /// Takes over what the AI saw. What the app's own lists or the user said stays; the AI fills the gaps. It also says
        /// whether the cap is on. False = it saw nothing, or was not sure enough.
        /// </summary>
        bool TakeAiAnswer(ProductAiAdvisor.Identity seen)
        {
            if (seen == null || !seen.found || seen.confidence < minimumAiConfidence) return false;
            if (identity.productName.Length == 0 && seen.productName.Length > 0) { identity.productName = seen.productName; aiNamed = true; }
            if (identity.brandName.Length == 0) identity.brandName = seen.brandName;
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = seen.cosmeticType;
            if (seen.shadeHex.Length > 0) { identity.shadeHex = seen.shadeHex; identity.shadeFrom = seen.shadeFrom; }
            if (identity.shadeName.Length == 0 && seen.shadeName.Length > 0) identity.shadeName = seen.shadeName;
            if (seen.finish.Length > 0) identity.finish = seen.finish;
            identity.capOn = seen.capOn;

            // THE CAP IS ON and no shade could be seen (no shade sticker or colour dot on the pack either): the colour in
            // view is the pack's. It is not shown as the shade, the user is told once, and the colour is watched: when a
            // different one stays in view, the cap is probably off (CapShadeWatch, ShadeLook).
            if (seen.capOn && identity.shadeHex.Length == 0 && ShadeAdvisor.IsColourCosmetic(identity.cosmeticType))
            {
                capWatch.CapSeen(lastColour);
                if (!capSaid) { capSaid = true; Say(msgCapOn); }
            }
            else capWatch.Solved();
            ShowIdentity();
            return true;
        }

        static bool HasStoredList(ProductData product) => product != null && product.ingredients != null && product.ingredients.Count >= 3;

        /// <summary>The AI named a product the inventory has: it is taken with everything the inventory knows about it.</summary>
        void AdoptIfListed()
        {
            if (knownProduct != null || identity.productName.Length == 0) return;
            ProductData listed = InventoryProductFor(identity.productName, identity.brandName);
            if (listed != null) Adopt(listed);
        }

        /// <summary>
        /// A named product without a stored list is checked online once. AI visual guesses are confirmed against the
        /// photo; names from OCR, barcode or the user are checked as typed product names. Only a source-backed list is used.
        /// </summary>
        void MaybeCheckTheWeb() { StartFirebaseProductLookup(true); }

        async void WebCheck()
        {
            webAsked = true;
            if (!AiBudget.TakeLookUp()) return;
            int scan = scanNumber;
            webOut = true; float askedAt = Time.unscaledTime;
            Say(msgWebChecking);
            ShowGuidance();
            string text = evidence.FrontText.Length > 0 ? evidence.FrontText : evidence.Text.AllText(ScanEvidence.UsableLine);
            ProductAiAdvisor.WebMatch match = null;
            ProductAiAdvisor.IngredientLookup list = null;
            try
            {
                if (aiNamed)
                {
                    match = await ProductAiAdvisor.ConfirmOnWeb(earlyJpg, identity, text, barcode);
                    if (match != null && match.same && match.confidence >= minimumAiConfidence)
                        list = match.list;
                }
                else
                {
                    match = await ProductAiAdvisor.FindTyped(identity.productName, identity.brandName, identity.cosmeticType);
                    if (match != null && match.same && match.confidence >= minimumAiConfidence)
                        list = match.list;
                }
            }
            catch (Exception e) { Debug.LogWarning("Scan: the web check failed: " + e.Message); }
            if (this == null || scan != scanNumber) return;
            webOut = false; webTook = Time.unscaledTime - askedAt;
            messageOver = null; shownStatus = null;
            ShowGuidance();
            if (stage == Stage.Leaving || stage == Stage.Idle) return;
            if (match == null || !match.same || match.confidence < minimumAiConfidence)
            {
                if (lookupRequested) StartRequestedLookup();
                else if (!aiNamed && match != null && match.question.Length > 0) Say(match.question);
                else if (barcode.Length > 0)
                {
                    bool found = await UseOpenBeautyFacts();
                    if (!found && this != null && stage == Stage.Scanning && (evidence.AllFull || RingComplete) && !finishUnnamed) FinishAttempt();
                }
                else if ((evidence.AllFull || RingComplete) && !finishUnnamed) FinishAttempt();
                return;       // not confirmed: the first look stays as it is
            }

            // The confirmed web result may fill a missing kind for OCR/user-named products too. Only an AI visual guess may
            // replace a name the app already read or the user chose.
            if (match != null && identity.cosmeticType.Length == 0 && match.cosmeticType.Length > 0)
                identity.cosmeticType = match.cosmeticType;
            if (aiNamed && match != null && knownProduct == null)
            {
                if (match.productName.Length > 0) identity.productName = match.productName;
                if (match.brandName.Length > 0) identity.brandName = match.brandName;
            }
            AdoptIfListed();
            if (StopIfNotMakeup()) return;
            ShowProgress(ProductKnown ? evidence.ProductKnown() : null);
            bool free = stage == Stage.Scanning && !PopupOpen;
            if (HasStoredList(knownProduct))
            {
                if (lookupRequested)
                {
                    lookupRequested = false;
                    UseLookedUpList(knownProduct.ingredients, ListPagesOf(knownProduct), "inventory", false);
                }
                else if (free) ContinueScanning();
                return;
            }

            // MORE ABOUT IT: its published ingredient list came in the same answer
            if (list == null || !list.found || (ingredientText != null && listFrom != "pack"))
            {
                if (lookupRequested) StartRequestedLookup();
                else if (barcode.Length > 0)
                {
                    bool found = await UseOpenBeautyFacts();
                    if (!found && this != null && stage == Stage.Scanning && (evidence.AllFull || RingComplete) && !finishUnnamed) FinishAttempt();
                }
                else if ((evidence.AllFull || RingComplete) && !finishUnnamed) FinishAttempt();
                return;
            }
            foreach (ProductAiAdvisor.HiddenAdditive hidden in list.hidden)
            {
                try { HiddenAdditives.Learn(hidden.term, hidden.substances, hidden.note, list.sources, list.webSearched, list.model); }
                catch (Exception e) { Debug.LogWarning("Hidden ingredients were not saved: " + e.Message); }
            }
            // This web result has already been confirmed as this product and includes a source-backed list. Prefer it to
            // an OCR list that may have accumulated fragments from several frames.
            if (lookupRequested)
            {
                lookupRequested = false;
                UseLookedUpList(list.ingredients, list.sources, "web", list.webSearched);
            }
            else if (free) UseLookedUpList(list.ingredients, list.sources, "web", list.webSearched);
            else webList = list;
        }

        /// <summary>The list the web check brought earlier, when there is still no other. True = it was used (the scan goes on to its end).</summary>
        bool UseKeptWebList()
        {
            if (webList == null || !webList.found || ingredientText != null) return false;
            ProductAiAdvisor.IngredientLookup list = webList;
            webList = null;
            UseLookedUpList(list.ingredients, list.sources, "web", list.webSearched);
            return true;
        }

        /// <summary>
        /// A new colour has stayed in view after the AI saw the cap on: the user has probably taken it off. The AI is shown
        /// that picture and asked for the shade (when the product has a name and a question is left); otherwise the colour
        /// the camera sees is taken, as it was before the AI knew about caps.
        /// </summary>
        async void ShadeLook()
        {
            if (shadeOut) return;
            if (!AiCanLook || !AiBudget.CanRecognise || identity.productName.Length == 0 || pixels == null || pixelsW <= 0 || pixelsOfScan != scanNumber)
            {
                capWatch.Solved();
                ShowIdentity();
                return;
            }
            byte[] jpg = ToJpg(new Kept { pixels = pixels, width = pixelsW, height = pixelsH });
            if (jpg == null || !AiBudget.TakeRecognition()) { capWatch.Solved(); ShowIdentity(); return; }
            int scan = scanNumber;
            shadeOut = true;
            ProductAiAdvisor.Identity seen = null;
            try { seen = await ProductAiAdvisor.Identify(jpg, ""); }
            catch (Exception e) { Debug.LogWarning("Scan: the AI's look for the shade failed: " + e.Message); }
            if (this == null || scan != scanNumber) return;
            shadeOut = false;
            if (stage == Stage.Leaving) return;
            if (seen != null && seen.found && seen.shadeHex.Length > 0)
            {
                identity.shadeHex = seen.shadeHex; identity.shadeFrom = seen.shadeFrom; identity.capOn = false;
                if (seen.finish.Length > 0) identity.finish = seen.finish;
                capWatch.Solved();
            }
            else if (seen != null && seen.found && seen.capOn) capWatch.CapSeen(lastColour);      // still closed: what is in view is the pack
            else capWatch.Solved();                                                              // open, but no colour named: the camera's own colour is taken
            ShowIdentity();
        }

        /// <summary>Finish was tapped while the AI's early look is still on its way: wait for it (with a time limit), then finish.</summary>
        IEnumerator FinishAfterEarlyLook()
        {
            stage = Stage.Recognising;
            if (holdButton != null) holdButton.Usable = false;
            ShowButtons();
            SetWorking(true);
            Say(msgAiLooking);
            float waited = 0f;                                                   // EVERY LONG STEP HAS A TIME LIMIT
            while (earlyOut && waited < EarlyLookWaitSeconds) { waited += Time.unscaledDeltaTime; yield return null; }
            earlyOut = false;                                                    // not waited for again (a late answer still fills the gaps)
            SetWorking(false);
            stage = Stage.Scanning;
            if (holdButton != null) holdButton.Usable = true;
            if (StopIfNotMakeup()) yield break;
            AdoptIfListed();
            if (ProductKnown) ShowProgress(evidence.ProductKnown());
            // a product of the inventory brings its confirmed list with it
            if (HasStoredList(knownProduct) && (listFrom == "" || listFrom == "pack")) { ContinueScanning(); yield break; }
            FinishAttempt();
        }

        // ---------------------------------------------------------------------------------------
        //  Finishing
        // ---------------------------------------------------------------------------------------
        void OnFinishTapped()
        {
            if (stage == Stage.Scanning || stage == Stage.Idle) FinishAttempt();
        }

        /// <summary>
        /// The user tapped Finish after the Firebase catalogue supplied an ingredient list.
        /// </summary>
        void FinishAttempt()
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            if (UseKeptWebList()) return;                  // a confirmed source-backed list outranks partial or noisy OCR
            if (!TakePackList())
            {
                Say(msgNeedList);
                return;
            }
            if (holdButton != null) holdButton.LetGo();

            // the AI's early look is still on its way and the product has no name yet: its answer is waited for (a few
            // seconds at most), the AI is not asked twice
            if (earlyOut && identity.productName.Length == 0 && !finishUnnamed) { StartCoroutine(FinishAfterEarlyLook()); return; }

            if (!ProductKnown && !finishUnnamed)
            {
                // 1. what the app's own lists suggest: the user chooses, and no AI is needed
                if (!shortlistAnswered && shortlist != null && Suggestions().Count > 0)
                {
                    OpenShortlist(shortlistTitle, shortlistMessage, shortlistNoneLabel, FinishAttempt, () => { shortlistAnswered = true; FinishAttempt(); });
                    return;
                }
                // 2. the AI, once
                bool canAsk = false;
                if (canAsk && AiBudget.CanRecognise) { StartCoroutine(RecogniseThenFinish()); return; }
                // online, but the AI has been asked as often as it may be: say so, and offer the other ways on
                if (canAsk) { NotSure(msgAiLimit); return; }
                // No connection, no configured AI key, or no usable picture: keep the name-entry and finish-without-name
                // choices visible instead of silently analysing an unnamed product.
                NotSure(msgNotSure);
                return;
            }
            // The name and the brand were read but neither the kind of make-up nor the shade: the result would be thin and the AI may have
            // named the wrong product. Products of the inventory with a similar name and brand are offered for a fast, sure result.
            if (!detailsAnswered && NameWithoutDetails())
            {
                detailsAnswered = true;
                List<ScanShortlist.Entry> similar = ScanShortlist.Typed(ProductRepository.Index, identity.productName, identity.brandName)
                    .FindAll(e => !declined.Contains(e.Key));
                if (similar.Count > 0 && shortlist != null)
                {
                    OpenShortlist(similarTitle, similarMessage, similarNoneLabel, FinishAttempt, FinishAttempt, similar);
                    return;
                }
                // nothing similar is saved: say so, and let the user scan again; Finish once more goes on with what was read
                stage = cameraFeed != null && cameraFeed.IsPlaying ? Stage.Scanning : Stage.Idle;
                if (holdButton != null) holdButton.Usable = true;
                ShowButtons();
                Say(msgNoSimilar);
                return;
            }
            Finish();
        }

        /// <summary>True when the scan has a product name and a brand from the camera or the AI, but no kind of make-up and no shade, and it is not a product of the inventory.</summary>
        bool NameWithoutDetails()
        {
            if (knownProduct != null) return false;
            if (identity.productName.Length == 0 || identity.brandName.Length == 0) return false;
            if (identity.cosmeticType.Length > 0 || identity.shadeHex.Length > 0 || identity.shadeName.Length > 0) return false;
            if (evidence.ShadeText.Length > 0 || evidence.ShadeHex.Length > 0 || lockedShade.Length > 0) return false;
            return true;
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

            Kept shot = best[(int)ScanZone.Front] ?? (pixels != null && pixelsOfScan == scanNumber
                ? new Kept { pixels = pixels, width = pixelsW, height = pixelsH }
                : null);
            byte[] jpg = shot != null ? ToJpg(shot) : null;
            Task<ProductAiAdvisor.Identity> task = null;
            if (jpg != null)
            {
                AiBudget.TakeRecognition();
                string text = evidence.FrontText.Length > 0 ? evidence.FrontText : evidence.Text.AllText(ScanEvidence.UsableLine);
                task = ProductAiAdvisor.Identify(jpg, text);
            }
            float waited = 0f, limit = ProductAiAdvisor.TimeoutSeconds + 6f;     // EVERY LONG STEP HAS A TIME LIMIT
            while (task != null && !task.IsCompleted && waited < limit) { waited += Time.unscaledDeltaTime; yield return null; }
            SetWorking(false);
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Searching);

            if (task != null && task.Status == TaskStatus.RanToCompletion) TakeAiAnswer(task.Result);
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
            ScanSession.PendingText = ingredientText; ScanSession.PendingSource = ingredientSource;
            // with the cap on, the colour the camera saw is the pack's: no shade is handed on rather than a wrong one
            ScanSession.PendingShadeHex = identity.shadeHex.Length > 0 ? identity.shadeHex : capWatch.Hidden ? "" : lockedShade.Length > 0 ? lockedShade : evidence.ShadeHex.Length > 0 ? evidence.ShadeHex : lastColour;
            ScanSession.PendingProductName = identity.productName; ScanSession.PendingBrandName = identity.brandName;
            ScanSession.PendingCosmeticType = identity.cosmeticType;
            ScanSession.PendingOfficialIngredientSource = knownProduct != null ? knownProduct.officialIngredientSource : "";
            ScanSession.PendingProductId = knownProduct != null ? knownProduct.id : "";
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            ScanSession.PendingSources = new List<string>(sources);
            ScanSession.PendingBarcode = barcode;
            ScanSession.PendingListFrom = listFrom; ScanSession.PendingListWebBacked = listWebBacked;
            ScanSession.PendingReplaces = rescanOf; ScanSession.PendingReplacesSame = rescanSame;
            StartCoroutine(Recognised());
        }

        IEnumerator Recognised()
        {
            stage = Stage.Leaving;
            if (holdButton != null) holdButton.Usable = false;
            ShowButtons();
            ShowRing();
            if (guide != null) guide.SetState(ScanGuideOverlay.State.Detected);
            if (vibrate) Haptics.Tick(60);
            Say(statusDone);
            float leaveAt = Time.unscaledTime + recognisedHoldSeconds;
            while (Time.unscaledTime < leaveAt) yield return null;
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
                // First use the existing on-device catalogue; otherwise query the existing Firebase products collection.
                ProductData product = knownProduct != null && knownProduct.ingredients != null && knownProduct.ingredients.Count >= 3 ? knownProduct : InventoryProductFor(identity.productName, identity.brandName);
                if (product != null && product.ingredients != null && product.ingredients.Count >= 3)
                {
                    UseLookedUpList(product.ingredients, ListPagesOf(product), "inventory", false);
                    return;
                }
                if (identity.brandName.Length < 2) { Say(msgFirebaseMiss); return; }
                ProductData fromFirebase = await ProductRepository.FindByBrandAndNameAsync(identity.brandName, identity.productName);
                if (this == null || stage == Stage.Leaving) return;
                if (fromFirebase == null)
                {
                    if (await SearchTypedProductOnWeb(identity.productName, identity.brandName, identity.cosmeticType, false)) return;
                    Say(msgFirebaseMiss);
                    return;
                }
                Adopt(fromFirebase);
                ShowIdentity();
                if (fromFirebase.ingredients == null || fromFirebase.ingredients.Count == 0)
                {
                    if (await SearchTypedProductOnWeb(identity.productName, identity.brandName, identity.cosmeticType, false)) return;
                    Say(msgFirebaseNoIngredients);
                    return;
                }
                UseLookedUpList(fromFirebase.ingredients, fromFirebase.sources, "inventory", false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Ingredient look-up: " + e.Message);
                if (this != null) Say(msgFirebaseUnavailable);
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

        void StartRequestedLookup()
        {
            lookupRequested = false;
            if (this != null && stage != Stage.Leaving && stage != Stage.Idle) LookUp();
        }

        /// <summary>
        /// No trustworthy page had the list. When the scan read the product's barcode, Open Beauty Facts is asked: a public
        /// list that anybody can edit, so it comes last. True = it had the list, and the scan goes on with it.
        /// </summary>
        async Task<bool> UseOpenBeautyFacts()
        {
            if (barcode.Length == 0 || !OpenBeautyFacts.Enabled || !ConnectivityMonitor.Online) return false;
            OpenBeautyFacts.Result there = await OpenBeautyFacts.Find(barcode);
            if (this == null || stage == Stage.Leaving || ingredientText != null) return true;      // left, or the list was read from the pack in the meantime
            if (there == null || !there.found) return false;
            IngredientTextParser.ParsedList list = IngredientTextParser.Read(there.ingredientText);
            if (list.inOrder.Count < 3) return false;
            UseLookedUpList(new List<string>(list.inOrder), new List<string> { there.Source }, "web", true);
            return true;
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
        // ---------------------------------------------------------------------------------------
        //  The AI photo fallback (ScanSnapshotController)
        // ---------------------------------------------------------------------------------------
        void OfferSnapshot(string why)
        {
            if (snapshot == null || snapshotOffered || stage != Stage.Scanning) return;
            snapshotOffered = true;
            if (holdButton != null) holdButton.LetGo();        // reading pauses while the pop-up is open
            snapshot.Offer(why, identity.brandName, identity.productName);        // the live scan comes first; what it read goes along as a hint
        }

        void OnSnapshotAnalysed(ProductData listed, PendingProductData staged)
        {
            if (stage == Stage.Leaving || stage == Stage.Recognising) return;
            if (listed != null)
            {
                Adopt(listed);
                ShowIdentity();
                if (HasStoredList(listed)) UseLookedUpList(listed.ingredients, ListPagesOf(listed), "inventory", false);
                else Say(msgFirebaseNoIngredients);
                return;
            }
            if (staged != null) UseStaged(staged);
        }

        void OnSnapshotNameOnly(ProductAiAdvisor.Identity who)
        {
            if (who == null || stage == Stage.Leaving) return;
            if (identity.brandName.Length == 0) identity.brandName = who.brandName ?? "";
            if (identity.productName.Length == 0) identity.productName = who.productName ?? "";
            if (identity.cosmeticType.Length == 0) identity.cosmeticType = who.cosmeticType ?? "";
            if (identity.shadeHex.Length == 0 && !string.IsNullOrEmpty(who.shadeHex)) identity.shadeHex = who.shadeHex;
            ShowIdentity();
        }

        /// <summary>A staged product (waiting for approval) with its list: analysed at once, the result says PENDING APPROVAL.</summary>
        void UseStaged(PendingProductData staged)
        {
            if (!string.IsNullOrEmpty(staged.brandName)) identity.brandName = staged.brandName;
            if (!string.IsNullOrEmpty(staged.name)) identity.productName = staged.name;
            if (!string.IsNullOrEmpty(staged.cosmeticType)) identity.cosmeticType = staged.cosmeticType;
            if (identity.shadeHex.Length == 0 && !string.IsNullOrEmpty(staged.shadeHex)) identity.shadeHex = staged.shadeHex;
            ShowIdentity();
            UseLookedUpList(staged.ingredients, new List<string>(), "pending", false);
        }

        void UseLookedUpList(List<string> ingredients, List<string> from, string kind, bool webBacked)
        {
            ingredientText = string.Join(", ", ingredients);
            ingredientSource = "lookup";
            listFrom = kind; listWebBacked = webBacked;
            sources = new List<string>(from ?? new List<string>());
            if (ingredientsCheck != null) ingredientsCheck.SetFound(string.Format(ingredientsLookedUpFormat, ingredients.Count));
            evidence.ListKnown();
            ShowRing();
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

        bool manualBusy;

        void ManualSay(string message) { if (manualFeedbackText != null) manualFeedbackText.text = message ?? ""; }

        /// <summary>
        /// "Analyse" on the type-it-in pop-up. The app works out WHAT was typed (TypedEntry): an ingredient list is analysed
        /// as it is; a product's name is enough too, its list is then looked up (the app's own product list, then the web).
        /// </summary>
        void AnalyseManual()
        {
            if (manualBusy || stage == Stage.Leaving) return;
            string text = manualInput != null ? manualInput.text : "";
            string typedName = manualProductInput != null ? manualProductInput.text.Trim() : "";
            string typedBrand = manualBrandInput != null ? manualBrandInput.text.Trim() : "";
            string typedType = manualTypeDropdown != null && manualTypeDropdown.value > 0 ? manualTypeDropdown.options[manualTypeDropdown.value].text : "";

            TypedEntry.Reading typed = TypedEntry.Understand(text);
            if (typed.kind == TypedEntry.Kind.Ingredients) { AnalyseTypedList(text, typedName, typedBrand, typedType); return; }

            // NO INGREDIENT LIST WAS TYPED: the product's name is enough (in its own box, or typed into the big one)
            string product = typedName.Length > 0 ? typedName : typed.product;
            if (product.Length < 2 && typedBrand.Length < 2) { ManualSay(msgManualNothing); return; }
            ManualLookUp(product, typedBrand, typedType);
        }

        /// <summary>
        /// The user typed a product without its ingredient list. 1. The app's own product list (also offline). 2. The web:
        /// the AI works out which product is meant, writes its brand and name as it is sold (shown in the boxes, so the
        /// user sees what was understood) and brings its published list. When the words fit several products it asks back
        /// instead of guessing. Every way out says what to do next; the look-up has the AI's own time limit.
        /// </summary>
        async void ManualLookUp(string product, string brand, string kind)
        {
            manualBusy = true;
            if (manualAnalyseButton != null) manualAnalyseButton.interactable = false;
            string shown = ((brand.Length > 0 && product.IndexOf(brand, StringComparison.OrdinalIgnoreCase) < 0 ? brand + " " : "") + product).Trim();
            ManualSay(string.Format(msgManualLooking, shown));
            try
            {
                ProductData listed = InventoryProductFor(product, brand);          // the app's own product list (also offline)
                if (!HasStoredList(listed)) listed = ProductIn(shown) ?? listed;      // brand and name typed in one go
                if (HasStoredList(listed)) { UseTypedProduct(listed, kind); return; }

                if (brand.Length < 2) { ManualSay("Enter both the product name and brand to search GlowUp's Firebase catalogue."); return; }
                ProductData fromFirebase = await ProductRepository.FindByBrandAndNameAsync(brand, product);
                if (this == null || stage == Stage.Leaving) return;
                if (manualPanel != null && !manualPanel.IsOpen) return;
                if (fromFirebase == null)
                {
                    if (await SearchTypedProductOnWeb(product, brand, kind, true)) return;
                    ManualSay(string.Format(msgManualNoList, shown));
                    return;
                }
                if (fromFirebase.ingredients == null || fromFirebase.ingredients.Count == 0)
                {
                    if (await SearchTypedProductOnWeb(product, brand, kind, true)) return;
                    ManualSay(msgFirebaseNoIngredients);
                    return;
                }
                if (kind.Length > 0 && ProductScope.Judge(kind, product).IsNotMakeup)
                {
                    ManualSay(ProductScope.StopMessage(ProductScope.Judge(kind, product)));
                    return;
                }
                if (manualProductInput != null) manualProductInput.text = fromFirebase.name;
                if (manualBrandInput != null) manualBrandInput.text = fromFirebase.brandName;
                if (kind.Length > 0 && string.IsNullOrWhiteSpace(fromFirebase.cosmeticType)) fromFirebase.cosmeticType = kind;
                UseTypedProduct(fromFirebase, kind);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Scan: looking a typed product up failed: " + e.Message);
                if (this != null) ManualSay(msgFirebaseUnavailable);
            }
            finally
            {
                manualBusy = false;
                if (this != null && manualAnalyseButton != null) manualAnalyseButton.interactable = true;
            }
        }

        /// <summary>The typed product is one the app's own list has, with its ingredient list: it is analysed with that list.</summary>
        void UseTypedProduct(ProductData listed, string kind)
        {
            Adopt(listed);
            if (identity.cosmeticType.Length == 0 && kind.Length > 0) identity.cosmeticType = kind;
            ProductScope.Verdict verdict = ProductScope.Judge(identity.cosmeticType, identity.productName);
            if (verdict.IsNotMakeup) { ManualSay(ProductScope.StopMessage(verdict)); return; }
            ManualSay("");
            ClosePanel(manualPanel);
            UseLookedUpList(listed.ingredients, ListPagesOf(listed), "inventory", false);
        }

        /// <summary>An ingredient list was typed (one known ingredient is enough): it goes to the analysis as it is.</summary>
        void AnalyseTypedList(string text, string typedName, string typedBrand, string typedType)
        {
            stage = Stage.Leaving;
            PaintButtonState();
            StopCamera();
            ScanSession.PendingText = text; ScanSession.PendingSource = "manual"; ScanSession.PendingShadeHex = identity.shadeHex;
            ScanSession.PendingListFrom = "typed"; ScanSession.PendingListWebBacked = false;
            // what the user typed comes first; what the scan found fills the rest
            ScanSession.PendingProductName = typedName.Length > 0 ? typedName : identity.productName;
            ScanSession.PendingBrandName = typedBrand.Length > 0 ? typedBrand : identity.brandName;
            ScanSession.PendingCosmeticType = typedType.Length > 0 ? typedType : identity.cosmeticType;
            ScanSession.PendingProductId = "";
            ScanSession.PendingOfficialIngredientSource = "";
            ScanSession.PendingSources = new List<string>();
            ScanSession.PendingShadeName = identity.shadeName; ScanSession.PendingFinish = identity.finish;
            ScanSession.PendingBarcode = barcode;
            ScanSession.PendingReplaces = rescanOf; ScanSession.PendingReplacesSame = rescanSame;
            SceneNavigator.GoViaLoading(resultScene, loadingMessage, ScanAnalysisPipeline.Run);
        }
    }
}
