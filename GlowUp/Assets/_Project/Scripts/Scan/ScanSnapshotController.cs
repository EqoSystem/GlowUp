using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// "NOT IN OUR CATALOG YET": A PHOTO OF THE PRODUCT (reworked 7 November 2026, Google Lens-like). When a scanned product is not in
    /// the catalogue, the user may take ONE photo of it:
    ///
    ///   1. The photo is shown whole, with Retake and Use photo (PhotoPreview). Before the first photo is ever sent, one line says
    ///      that GlowUp reviews it to add the product to the catalogue.
    ///   2. Use photo: the phone reads the text on the WHOLE photo (not only the scan frame). PhotoTextQuality judges the reading by
    ///      its words; too few real words = "We couldn't read this clearly" with Retake or Type it in. Nothing of an unclear reading
    ///      is ever shown, and nothing is sent.
    ///   3. The photo is cut to the part with the product's text (the full frame is thrown away) and sent, with the clean reading, to
    ///      GlowUp's server (api/scan-snapshot.js). The AI there only NAMES the product; the server cuts the ingredient list out of
    ///      real product pages it opens (the brand's own site first, then shops). A list printed on the pack and read on the phone
    ///      is used first. No list = the user is asked to type it in. The AI never writes a list.
    ///   4. The brand is written as the catalogue spells it ("MAYBELLINE NEWYORK" = "Maybelline New York").
    ///   5. The product is staged in "pending_products" (photo, the fields, the list, its page and kind, the time, the sender's id);
    ///      the result says "Unverified - pending review" with "Unverified source · site". An admin approves, edits or rejects it.
    ///
    /// Limits as before: 30 s between photos, 5 a day per phone (the server checks the same per account). Every word is an
    /// Inspector field of this object ("Scan Snapshot" in the Hierarchy).
    /// </summary>
    public class ScanSnapshotController : MonoBehaviour
    {
        [Header("The pop-up (built by the scan scene builder)")]
        [SerializeField] private OverlayPanel panel;
        [Tooltip("The sentence that tells the user why the pop-up is there")]
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private Button takePhotoButton;
        [SerializeField] private TextMeshProUGUI takePhotoLabel;
        [Tooltip("Old: the spinner that sat outside the button. Kept hidden; the spinner is now inside the button (ButtonBusy).")]
        [SerializeField] private RectTransform spinner;
        [Tooltip("What happened, or what to do next (shown as the app's status message)")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button cancelButton;

        [Header("Camera")]
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("Not used for the photo any more (the whole picture is read). Kept for older scenes.")]
        [SerializeField] private RectTransform frame;

        [Header("The photo (cut to the product, sent, and kept for the admin)")]
        [Tooltip("The longer side of the photo that is read on the phone, in pixels")]
        [Range(720, 1920)] [SerializeField] private int readSide = 1600;
        [Tooltip("The longer side of the cut photo that is sent, in pixels")]
        [Range(480, 1920)] [SerializeField] private int longestSide = 1080;
        [Tooltip("The cut photo is squeezed (lower JPG quality, then smaller) until it is at most this many bytes. 80000 = 80 KB.")]
        [Range(20000, 80000)] [SerializeField] private int maximumPhotoBytes = 80000;
        [Range(40, 95)] [SerializeField] private int startQuality = 80;
        [Range(20, 80)] [SerializeField] private int lowestQuality = 45;
        [Tooltip("Room kept around the product's text when the photo is cut, as a share of the photo")]
        [Range(0f, 0.3f)] [SerializeField] private float cutMargin = 0.08f;
        [Tooltip("On = the admin sees the (cut) photo next to the reading on the website. Readable only by admins and the sender.")]
        [SerializeField] private bool keepPhotoForAdmin = true;

        [Header("The server function (holds the AI key; this phone holds none for this path)")]
        [SerializeField] private string functionUrl = "https://glowup-admin-snowy.vercel.app/api/scan-snapshot";
        [Range(10f, 58f)] [SerializeField] private float functionTimeoutSeconds = 58f;

        [Header("Limits")]
        [Min(0f)] [SerializeField] private float rememberMissMinutes = 30f;
        [Min(1f)] [SerializeField] private float keepStagedHours = 24f;
        [Tooltip("The server's naming of the product counts when it is at least this sure (0 to 1)")]
        [Range(0f, 1f)] [SerializeField] private float minimumConfidence = 0.35f;
        [Tooltip("A list read from the pack on the phone is used when at least this many of its names are known ingredients")]
        [Min(3)] [SerializeField] private int printedListMinimum = 5;

        [Header("Wording (7 November 2026: plain words; no technical terms)")]
        [SerializeField] private string msgPromptV2 = "This product isn't in our catalog yet.";
        [SerializeField] private string msgIntroV2 = "Take one photo of the front of the pack. We'll look it up and show you what we find.";
        [SerializeField] private string idleLabel = "Take a photo of the product";
        [SerializeField] private string busyLabel = "Analyzing Packaging...";
        [Tooltip("Shown instead when the long words do not fit beside the spinner")]
        [SerializeField] private string busyShortLabel = "Analyzing…";
        [Tooltip("{0} = seconds left")]
        [SerializeField] private string coolingLabel = "Wait {0} s";
        [SerializeField] private string previewTitle = "Check your photo";
        [SerializeField] private string previewNotice = "Your product photo will be reviewed by GlowUp to add it to the catalog.";
        [SerializeField] private string retakeLabel = "Retake";
        [SerializeField] private string usePhotoLabel = "Use photo";
        [SerializeField] private string notNowLabel = "Not now";
        [SerializeField] private string typeItInLabel = "Type it in";
        [SerializeField] private string msgReading = "Reading text…";
        [SerializeField] private string msgCatalog = "Searching catalog…";
        [Tooltip("{0} = the product, in medium weight")]
        [SerializeField] private string msgSearching = "Searching for {0}…";
        [SerializeField] private string msgUnclear = "We couldn't read this clearly. Retake the photo in good light, or type the product in.";
        [Tooltip("New13 item 8: the captured picture was (nearly) black: it is not shown")]
        [SerializeField] private string msgTooDark = "Photo is too dark. Turn on the flashlight or move to the light, then retake it.";
        [SerializeField] private string msgTooBlurry = "Photo is too blurry. Hold the phone steady and retake it.";
        [SerializeField] private string msgNotReady = "The camera wasn't ready. Wait a moment, then take the photo again.";
        [SerializeField] private string msgNoCameraV2 = "The camera isn't running. Close this, tap the scan button, then try again.";
        [SerializeField] private string msgOfflineV2 = "You're offline. Try again when you're connected, or type the product in.";
        [SerializeField] private string msgNotAvailable = "Photo look-up isn't available right now. You can type the product in.";
        [SerializeField] private string msgSignInV2 = "Sign in again to send a photo, or type the product in.";
        [Tooltip("{0} = seconds")]
        [SerializeField] private string msgCooldownV2 = "Wait {0} seconds before the next photo.";
        [Tooltip("{0} = photos per day")]
        [SerializeField] private string msgDailyLimitV2 = "You've sent {0} photos today. Try again tomorrow, or type the product in.";
        [SerializeField] private string msgBusyV2 = "Photo look-up is busy today. Type the product in, or try again tomorrow.";
        [SerializeField] private string msgDownV2 = "The photo couldn't be looked up.";
        [SerializeField] private string msgNothingV2 = "We couldn't recognise a product in this photo. Retake it showing the front of the pack, or type it in.";
        [Tooltip("{0} = the product")]
        [SerializeField] private string msgNoListV2 = "Found {0}, but couldn't find an ingredient list. You can type it in.";
        [SerializeField] private string msgFromYouV2 = "You already sent this product. It is waiting for review.";

        /// <summary>The photo gave a result: listed = the catalogue has it after all; otherwise staged = the entry waiting for review.</summary>
        public event Action<ProductData, PendingProductData> Analysed;
        /// <summary>A product was named but no list was found: the scan screen keeps the name.</summary>
        public event Action<ProductAiAdvisor.Identity> NameOnly;
        /// <summary>"Type it in": the scan screen opens its type-it-in sheet.</summary>
        public event Action TypeItIn;
        /// <summary>
        /// PHOTO MODE (10 November 2026): "Take photo" no longer captures at once. It closes this pop-up and asks the scan screen to switch
        /// to photo mode (a capture button instead of the live-scan button, the camera fully visible). The scan screen then calls CaptureNow().
        /// </summary>
        public event Action CaptureModeRequested;
        /// <summary>The text read inside the user's box matched products in the catalogue: the scan screen shows them as a list to pick from.
        /// It answers with MatchesAnswered(true) when one was picked, MatchesAnswered(false) for "None of these" (the photo is then looked up online).</summary>
        public event Action<List<ScanShortlist.Entry>> CatalogMatches;

        public const float CooldownSeconds = 30f;
        public const int DailyLimit = 5;
        const string DayKey = "snapshot.day", CountKey = "snapshot.count", NoticeKey = "snapshot.noticeSeen";
        const string DefaultFunctionUrl = "https://glowup-admin-snowy.vercel.app/api/scan-snapshot";

        string brandHint = "", productHint = "";
        bool busy;
        float lockedUntil;
        int lastShownSecond = -1;
        int attempt;                                        // a "Not now" during a look-up makes its answer be ignored
        Texture2D photo;
        PhotoPreview preview;
        StatusMessage status;

        public bool IsOpen => panel != null && panel.IsOpen;

        void Awake()
        {
            if (takePhotoLabel != null && !takePhotoLabel.enableAutoSizing)
            {
                takePhotoLabel.enableWordWrapping = false; takePhotoLabel.enableAutoSizing = true;
                takePhotoLabel.fontSizeMax = takePhotoLabel.fontSize; takePhotoLabel.fontSizeMin = 30f;
                takePhotoLabel.overflowMode = TextOverflowModes.Overflow;
            }
            if (takePhotoButton != null) takePhotoButton.onClick.AddListener(Take);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (spinner != null) spinner.gameObject.SetActive(false);         // replaced by the spinner inside the button
            if (statusText != null) status = StatusMessage.Wrap(statusText);
            ApplyNumbers();
        }

        void OnDestroy() { DropPhoto(); }

        static string Today => DateTime.UtcNow.ToString("yyyyMMdd");
        static int SentToday() => PlayerPrefs.GetString(DayKey, "") == Today ? PlayerPrefs.GetInt(CountKey, 0) : 0;
        static void CountOne() { PlayerPrefs.SetString(DayKey, Today); PlayerPrefs.SetInt(CountKey, SentToday() + 1); PlayerPrefs.Save(); }
        static bool NoticeSeen => PlayerPrefs.GetInt(NoticeKey, 0) == 1;

        void OnValidate() { if (lowestQuality > startQuality) lowestQuality = startQuality; }

        void ApplyNumbers()
        {
            PendingProductRepository.RememberMissMinutes = rememberMissMinutes;
            PendingProductRepository.KeepHours = keepStagedHours;
            ScanPhotoService.FunctionUrl = string.IsNullOrWhiteSpace(functionUrl) ? DefaultFunctionUrl : functionUrl.Trim();
            ScanPhotoService.TimeoutSeconds = functionTimeoutSeconds;
        }

        /// <summary>Opens the pop-up (the scan screen calls this when the catalogue does not have the product).</summary>
        public void Offer(string why = null, string brandHint = "", string productHint = "")
        {
            if (panel == null) return;
            this.brandHint = brandHint ?? ""; this.productHint = productHint ?? "";
            if (promptText != null) promptText.text = msgPromptV2;          // the scan screen's own old sentence named the AI: plain words here
            Say(StatusMessage.Kind.Info, msgIntroV2);
            Paint();
            if (!panel.IsOpen) panel.Show();
        }

        public void Close()
        {
            attempt++;
            if (preview != null) preview.Hide();
            DropPhoto();
            busy = false;
            Paint();
            if (panel != null && panel.IsOpen) panel.Hide();
        }

        void Update()
        {
            if (!busy && Time.unscaledTime < lockedUntil)
            {
                int left = Mathf.CeilToInt(lockedUntil - Time.unscaledTime);
                if (left != lastShownSecond) { lastShownSecond = left; Paint(); }
            }
            else if (lastShownSecond != -1 && !busy) { lastShownSecond = -1; Paint(); }
        }

        /// <summary>The Take photo button: busy while reading, "Wait n s" (spinner inside, not pressable) while cooling down.</summary>
        void Paint()
        {
            bool cooling = Time.unscaledTime < lockedUntil;
            if (takePhotoButton == null) return;
            if (busy) ButtonBusy.Set(takePhotoButton, takePhotoLabel, true, busyLabel, busyShortLabel);
            else if (cooling) ButtonBusy.Set(takePhotoButton, takePhotoLabel, true, string.Format(coolingLabel, Mathf.CeilToInt(lockedUntil - Time.unscaledTime)));
            else ButtonBusy.Set(takePhotoButton, takePhotoLabel, false, idleLabel);
        }

        void Say(StatusMessage.Kind kind, string words, bool offerTyping = false)
        {
            if (status != null) status.Show(kind, words, offerTyping ? typeItInLabel : null, offerTyping ? (Action)OpenTyping : null);
        }

        void OpenTyping()
        {
            Close();
            TypeItIn?.Invoke();
        }

        // ---------------------------------------------------------------------------------------
        //  1. The photo, shown whole
        // ---------------------------------------------------------------------------------------
        void Take()
        {
            if (busy || Time.unscaledTime < lockedUntil) return;
            if (CaptureModeRequested != null)
            {
                // nothing is captured yet: the pop-up closes so the camera is fully visible, and the scan screen shows the capture button
                attempt++;
                if (panel != null && panel.IsOpen) panel.Hide();
                CaptureModeRequested.Invoke();
                return;
            }
            CaptureNow();
        }

        /// <summary>The capture button of photo mode: takes the photo now and opens "Check your photo" with the box to adjust.</summary>
        public void CaptureNow()
        {
            if (busy || Time.unscaledTime < lockedUntil) { if (Time.unscaledTime < lockedUntil) Toast(string.Format(msgCooldownV2, Mathf.CeilToInt(lockedUntil - Time.unscaledTime))); return; }
            if (CaptureModeRequested != null)
            {
                // in photo mode the pop-up is closed: problems are said in a toast
                if (cameraFeed == null || !cameraFeed.IsPlaying) { Toast(msgNoCameraV2); return; }
                if (!ConnectivityMonitor.Online) { Toast(msgOfflineV2); return; }
                if (SentToday() >= DailyLimit) { Toast(string.Format(msgDailyLimitV2, DailyLimit)); return; }
            }
            if (cameraFeed == null || !cameraFeed.IsPlaying) { Say(StatusMessage.Kind.Error, msgNoCameraV2, true); return; }
            if (!ConnectivityMonitor.Online) { Say(StatusMessage.Kind.Error, msgOfflineV2, true); return; }
            if (SentToday() >= DailyLimit) { Say(StatusMessage.Kind.Error, string.Format(msgDailyLimitV2, DailyLimit), true); return; }
            DropPhoto();
            photo = cameraFeed.CaptureUpright(null, readSide);                   // the whole picture, not only the scan frame
            if (photo == null) { Say(StatusMessage.Kind.Error, msgNoCameraV2, true); return; }
            // New13 item 8: a black, unready or very blurry picture is never shown as "Check your photo": the user retakes it
            PhotoCaptureCheck.Verdict quality = PhotoCaptureCheck.Judge(photo);
            if (quality != PhotoCaptureCheck.Verdict.Ok)
            {
                DropPhoto();
                string why = quality == PhotoCaptureCheck.Verdict.TooDark ? msgTooDark : quality == PhotoCaptureCheck.Verdict.TooBlurry ? msgTooBlurry : msgNotReady;
                if (CaptureModeRequested != null) Toast(why, FormalToast.Kind.Warning); else Say(StatusMessage.Kind.Error, why, true);
                return;
            }
            if (preview == null)
            {
                preview = PhotoPreview.Build(panel != null ? panel.transform : transform, promptText, previewTitle, previewNotice, retakeLabel, usePhotoLabel, notNowLabel);
                if (preview == null) { DropPhoto(); Say(StatusMessage.Kind.Error, msgNotAvailable, true); return; }
                preview.Retake += Retake;
                preview.Use += UsePhoto;
                preview.NotNow += BackToCapture;
                preview.Closed += BackToCapture;
            }
            preview.Show(photo, !NoticeSeen);
        }

        void Retake()
        {
            if (CaptureModeRequested == null) { preview.Hide(); DropPhoto(); CaptureNow(); return; }
            BackToCapture();
        }

        /// <summary>Back to photo mode (Retake, Not now, the X, a tap beside the card, the phone's back key): a running reading is cancelled.</summary>
        void BackToCapture()
        {
            attempt++;
            busy = false; Paint();
            if (preview != null) preview.Hide();
            DropPhoto();
            if (choice != null) { choice.TrySetResult(true); choice = null; }
            if (CaptureModeRequested != null) CaptureModeRequested.Invoke();
        }

        /// <summary>True while "Check your photo" is open (the scan screen's back key closes it first).</summary>
        public bool PreviewOpen => preview != null && preview.IsOpen;
        public void ClosePreviewByBack() { if (PreviewOpen) BackToCapture(); }

        FormalToast toast;
        void Toast(string words, FormalToast.Kind kind = FormalToast.Kind.Error)
        {
            if (toast == null) toast = FormalToast.OnScreen(transform, promptText);
            if (toast != null) toast.Show(kind, StatusMessage.Plain(words));
        }

        TaskCompletionSource<bool> choice;
        /// <summary>The scan screen's answer to CatalogMatches: true = a product was picked (this look-up ends), false = none of these.</summary>
        public void MatchesAnswered(bool picked) { if (choice != null) { choice.TrySetResult(picked); } }

        /// <summary>The part of the photo inside the user's box, as a new texture (the whole photo when the box covers it).</summary>
        static Texture2D CutTo(Texture2D source, Rect area)
        {
            int px = Mathf.Clamp(Mathf.FloorToInt(area.xMin * source.width), 0, source.width - 16), py = Mathf.Clamp(Mathf.FloorToInt(area.yMin * source.height), 0, source.height - 16);
            int pw = Mathf.Clamp(Mathf.CeilToInt(area.width * source.width), 16, source.width - px), ph = Mathf.Clamp(Mathf.CeilToInt(area.height * source.height), 16, source.height - py);
            var cut = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            cut.SetPixels(source.GetPixels(px, py, pw, ph));
            cut.Apply(false);
            return cut;
        }

        Texture2D shownPhoto;          // the whole photo while `photo` holds the part inside the box

        void DropPhoto()
        {
            if (shownPhoto != null) { Destroy(shownPhoto); shownPhoto = null; }
            if (photo != null) { Destroy(photo); photo = null; }
        }

        // ---------------------------------------------------------------------------------------
        //  2. to 5. Use photo
        // ---------------------------------------------------------------------------------------
        async void UsePhoto()
        {
            if (busy || photo == null) return;
            if (!ConnectivityMonitor.Online) { preview.Problem(msgOfflineV2, typeItInLabel, OpenTyping, true); return; }
            if (string.IsNullOrEmpty(ScanPhotoService.FunctionUrl)) { preview.Problem(msgNotAvailable, typeItInLabel, OpenTyping, false); return; }
            int mine = ++attempt;
            busy = true;
            Paint();
            preview.SetBusy(true, busyLabel, busyShortLabel);
            preview.Say(StatusMessage.Kind.Progress, msgReading);
            try
            {
                // the phone reads ONLY the part inside the user's box (10 November 2026)
                Rect area = preview.CropArea;
                if (area.width < 0.999f || area.height < 0.999f)
                {
                    Texture2D cut = CutTo(photo, area);
                    if (shownPhoto != null && shownPhoto != photo) Destroy(shownPhoto);
                    shownPhoto = photo;                                  // the card keeps showing the whole photo (it was destroyed here: black picture)
                    photo = cut;
                }
                // New13 item 9: read upright AND turned a quarter each way, like the live scan (sideways words on a bottle)
                PhotoTextReader.Result read = await PhotoTextReader.Read(photo, KnownWord, () => this == null || mine != attempt);
                if (this == null || mine != attempt) return;
                PhotoTextQuality.Verdict verdict = read.verdict;
                // an unclear reading of the phone no longer stops the look-up (11 November 2026): the server's vision reads the picture itself,
                // as Google Lens does. Nothing of the unclear reading is shown or sent (cleanText stays empty).
                if (verdict.readable) preview.ShowTextShapes(read.boxes);                    // the lines that were read, outlined inside the box

                // the catalogue first, with the same matcher as the live scan: the closest products to pick from
                if (CatalogMatches != null && verdict.cleanText.Length > 0)
                {
                    preview.Say(StatusMessage.Kind.Progress, msgCatalog);
                    List<ScanShortlist.Entry> matches = ScanShortlist.For(ProductRepository.Index, verdict.cleanText, null, null, 6);
                    if (matches.Count > 0)
                    {
                        await Task.Delay(350);                          // the outlines stay in view for a moment
                        if (this == null || mine != attempt) return;
                        choice = new TaskCompletionSource<bool>();
                        Texture2D keep = shownPhoto != null ? shownPhoto : photo;
                        preview.Hide();
                        CatalogMatches.Invoke(matches);
                        bool picked = await choice.Task;
                        choice = null;
                        if (this == null || mine != attempt) return;
                        if (picked) { Close(); return; }
                        preview.Show(keep, false);                      // "None of these": the photo is looked up online
                        preview.SetBusy(true, busyLabel, busyShortLabel);
                    }
                }

                // the photo cut to the product's text; the full frame is thrown away
                byte[] jpeg = CutAndSqueeze(photo, read.boxes);
                if (shownPhoto != null) { Destroy(photo); photo = null; }       // only the part cut for reading; the card keeps the whole photo
                if (jpeg == null) { preview.Problem(msgUnclear, typeItInLabel, OpenTyping, false); return; }

                // a list printed on the pack and read on the phone comes first (not the AI's)
                List<string> printed = PrintedList(read.rawText);
                string ocrBrand = BrandIn(verdict.cleanText);

                // from here on the photo leaves the phone (after the user's tap); the cool-down starts now
                lockedUntil = Time.unscaledTime + CooldownSeconds;
                CountOne();
                PlayerPrefs.SetInt(NoticeKey, 1); PlayerPrefs.Save();
                string lookingFor = (ocrBrand.Length > 0 ? ocrBrand : brandHint) + " " + productHint;
                preview.Say(StatusMessage.Kind.Progress, lookingFor.Trim().Length > 0
                    ? string.Format(msgSearching, "<b>" + StatusMessage.Plain(lookingFor.Trim()) + "</b>") : msgReading);

                ScanPhotoService.Reading answer = await ScanPhotoService.Read(jpeg, ocrBrand.Length > 0 ? ocrBrand : brandHint, productHint, verdict.cleanText);
                if (this == null || mine != attempt) return;
                switch (answer.outcome)
                {
                    case ScanPhotoService.Outcome.Offline: preview.Problem(msgOfflineV2, typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.NotSetUp: preview.Problem(msgNotAvailable, typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.SignIn: preview.Problem(msgSignInV2, typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.Cooldown: preview.Problem(string.Format(msgCooldownV2, (int)CooldownSeconds), typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.DailyLimit: preview.Problem(string.Format(msgDailyLimitV2, DailyLimit), typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.Busy: preview.Problem(msgBusyV2, typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.NotFound: preview.Problem(msgNothingV2, typeItInLabel, OpenTyping, false); return;
                    case ScanPhotoService.Outcome.Down: preview.Problem(msgDownV2, typeItInLabel, OpenTyping, false); return;
                }

                var who = new ProductAiAdvisor.Identity
                {
                    found = true, brandName = BrandNameNormalizer.Normalize(answer.brand.Length > 0 ? answer.brand : ocrBrand, CatalogueBrands()),
                    productName = answer.productName, cosmeticType = answer.cosmeticType, shadeHex = answer.shadeHex, confidence = answer.confidence, model = answer.model
                };
                if (who.confidence < minimumConfidence || (who.productName.Length == 0 && who.brandName.Length == 0)) { preview.Problem(msgNothingV2, typeItInLabel, OpenTyping, false); return; }

                // the catalogue has it after all (the label was hard to read live): its own entry is used
                ProductData listed = who.productName.Length > 0 ? ProductRepository.Index.Exact(who.brandName, who.productName) : null;
                if (listed != null) { Close(); Analysed?.Invoke(listed, null); return; }

                // the user's OWN earlier photo of this product (another user's entry never is)
                PendingProductData staged = who.productName.Length > 0 ? await PendingProductRepository.FindAsync(who.brandName, who.productName) : null;
                if (this == null || mine != attempt) return;
                if (staged != null && staged.ingredients.Count > 0) { Close(); Analysed?.Invoke(null, staged); return; }

                // MAKE-UP ONLY, as everywhere else
                ProductScope.Verdict scope = ProductScope.Judge(who.cosmeticType, who.productName);
                if (scope.IsNotMakeup) { preview.Problem(ProductScope.StopMessage(scope), null, null, false); return; }

                bool fromPack = printed.Count > 0;
                List<string> list = fromPack ? printed : answer.ingredients;
                var sources = new List<string>();
                if (!fromPack && answer.ingredientSourceUrl.Length > 0) sources.Add(answer.ingredientSourceUrl);
                foreach (string url in answer.sourceUrls) if (!sources.Contains(url) && sources.Count < 5) sources.Add(url);
                var record = new PendingProductRecord
                {
                    name = Limit(who.productName.Length > 0 ? who.productName : productHint, 100), brandName = Limit(who.brandName, 60), cosmeticType = Limit(who.cosmeticType, 40),
                    shadeHex = who.shadeHex ?? "", ingredients = Limited(list), confidence = who.confidence, model = Limit(who.model, 60),
                    variant = Limit(answer.variant, 60), size = Limit(answer.size, 40), sourceUrls = sources,
                    ocrText = Limit(verdict.cleanText, 1500), ocrBrand = Limit(ocrBrand, 60),
                    ingredientsFrom = fromPack ? "printed" : list.Count > 0 ? "page" : "none",
                    ingredientSourceUrl = !fromPack && list.Count > 0 ? answer.ingredientSourceUrl : "",
                    ingredientSourceKind = !fromPack && list.Count > 0 ? answer.ingredientSourceKind : ""
                };
                PendingProductData made = record.name.Length > 0 ? PendingProductRepository.Submit(record, keepPhotoForAdmin ? jpeg : null, maximumPhotoBytes) : null;

                if (list.Count == 0)
                {
                    // named, but no page had a list: the user types it (the entry still waits for review, with its photo)
                    string name = (who.brandName + " " + who.productName).Trim();
                    preview.Problem(string.Format(msgNoListV2, name.Length > 0 ? StatusMessage.Plain(name) : "the product"), typeItInLabel, () => { NameOnly?.Invoke(who); OpenTyping(); }, false);
                    NameOnly?.Invoke(who);
                    return;
                }
                if (made == null) { preview.Problem(msgNothingV2, typeItInLabel, OpenTyping, false); return; }
                Close();
                Analysed?.Invoke(null, made);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Product photo look-up failed: " + e.Message);
                if (this != null && preview != null && mine == attempt) preview.Problem(msgDownV2, typeItInLabel, OpenTyping, false);
            }
            finally
            {
                if (this != null && mine == attempt)
                {
                    busy = false;
                    Paint();
                    if (preview != null && preview.IsOpen) preview.SetBusy(false, "", "");
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------------------------------
        HashSet<string> vocabulary;

        /// <summary>A word the catalogue knows (brands, product names) or an ingredient name: the reading is judged by these.</summary>
        bool KnownWord(string lower)
        {
            if (vocabulary == null)
            {
                vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void AddWords(string text) { foreach (string w in (text ?? "").Split(new[] { ' ', '-', '/', ',', '.', '\'', '(', ')', '+', '&' }, StringSplitOptions.RemoveEmptyEntries)) if (w.Length >= 3) vocabulary.Add(w); }
                try
                {
                    foreach (ProductData p in ProductRepository.Every.Products) { AddWords(p.name); AddWords(p.brandName); }
                    foreach (BrandData b in BrandRepository.All()) AddWords(b.name);
                }
                catch (Exception e) { Debug.LogWarning("Photo check: the catalogue words are not available: " + e.Message); }
            }
            if (vocabulary.Contains(lower)) return true;
            try { return lower.Length >= 4 && IngredientKnowledge.IsExactName(lower); } catch (Exception) { return false; }
        }

        List<string> catalogueBrands;
        List<string> CatalogueBrands()
        {
            if (catalogueBrands != null) return catalogueBrands;
            catalogueBrands = new List<string>();
            try
            {
                foreach (BrandData b in BrandRepository.All()) if (!string.IsNullOrWhiteSpace(b.name) && !catalogueBrands.Contains(b.name)) catalogueBrands.Add(b.name);
                foreach (ProductData p in ProductRepository.Every.Products) if (!string.IsNullOrWhiteSpace(p.brandName) && !catalogueBrands.Contains(p.brandName)) catalogueBrands.Add(p.brandName);
            }
            catch (Exception e) { Debug.LogWarning("Photo check: the brand list is not available: " + e.Message); }
            return catalogueBrands;
        }

        /// <summary>A catalogue brand named in the clean reading, as the catalogue spells it ("" when none).</summary>
        string BrandIn(string clean)
        {
            if (string.IsNullOrEmpty(clean)) return "";
            string squashed = BrandNameNormalizer.Squash(clean);
            string best = "";
            foreach (string brand in CatalogueBrands())
            {
                string key = BrandNameNormalizer.Squash(brand);
                if (key.Length >= 4 && squashed.Contains(key) && brand.Length > best.Length) best = brand;
            }
            if (best.Length > 0) return best;
            // the first line is often the brand: tidied, and matched with small misreads forgiven
            string first = clean.Split('\n')[0];
            string normal = BrandNameNormalizer.Normalize(first, CatalogueBrands());
            return CatalogueBrands().Contains(normal) ? normal : "";
        }

        /// <summary>The ingredient list printed on the pack, read on the phone; empty unless enough of its names are real ingredients.</summary>
        List<string> PrintedList(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || !IngredientTextParser.HasListShape(text)) return new List<string>();
                List<string> names = IngredientTextParser.Parse(text);
                return IngredientTextParser.CountRecognised(names) >= printedListMinimum ? names : new List<string>();
            }
            catch (Exception) { return new List<string>(); }
        }

        /// <summary>
        /// The part of the photo with the product's text (all read lines, plus a margin), as a JPG of at most maximumPhotoBytes.
        /// `boxes` are 0 to 1 from the photo's bottom-left corner. Without boxes the middle of the photo is kept.
        /// </summary>
        byte[] CutAndSqueeze(Texture2D source, Rect[] boxes)
        {
            Rect area = new Rect(0f, 0f, 1f, 1f);                     // no text boxes: the whole part inside the user's box goes to the server's vision
            if (boxes != null && boxes.Length > 0)
            {
                float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
                foreach (Rect b in boxes) { x0 = Mathf.Min(x0, b.xMin); y0 = Mathf.Min(y0, b.yMin); x1 = Mathf.Max(x1, b.xMax); y1 = Mathf.Max(y1, b.yMax); }
                area = Rect.MinMaxRect(Mathf.Clamp01(x0 - cutMargin), Mathf.Clamp01(y0 - cutMargin), Mathf.Clamp01(x1 + cutMargin), Mathf.Clamp01(y1 + cutMargin));
            }
            int px = Mathf.FloorToInt(area.xMin * source.width), py = Mathf.FloorToInt(area.yMin * source.height);
            int pw = Mathf.Clamp(Mathf.CeilToInt(area.width * source.width), 16, source.width - px), ph = Mathf.Clamp(Mathf.CeilToInt(area.height * source.height), 16, source.height - py);
            if (pw < 16 || ph < 16) return null;
            int side = longestSide;
            for (int round = 0; round < 4; round++)
            {
                float shrink = Mathf.Max(1f, Mathf.Max(pw, ph) / (float)side);
                int ow = Mathf.Max(16, Mathf.RoundToInt(pw / shrink)), oh = Mathf.Max(16, Mathf.RoundToInt(ph / shrink));
                var cut = new Texture2D(ow, oh, TextureFormat.RGB24, false);
                try
                {
                    var pixels = new Color32[ow * oh];
                    Color32[] all = source.GetPixels32();
                    for (int y = 0; y < oh; y++)
                    {
                        int sy = py + Mathf.Min(ph - 1, (int)(y * shrink));
                        for (int x = 0; x < ow; x++) pixels[y * ow + x] = all[sy * source.width + px + Mathf.Min(pw - 1, (int)(x * shrink))];
                    }
                    cut.SetPixels32(pixels); cut.Apply(false);
                    byte[] best = null;
                    for (int quality = startQuality; quality >= lowestQuality; quality -= 10)
                    {
                        best = cut.EncodeToJPG(quality);
                        if (best.Length <= maximumPhotoBytes) return best;
                    }
                    if (round == 3) return best != null && best.Length <= maximumPhotoBytes ? best : null;
                }
                finally { Destroy(cut); }
                side = Mathf.Max(320, Mathf.RoundToInt(side * 0.75f));
            }
            return null;
        }

        static string Limit(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s.Trim() : s.Substring(0, max).Trim();

        static List<string> Limited(List<string> names)
        {
            var list = new List<string>();
            foreach (string n in names ?? new List<string>()) if (!string.IsNullOrWhiteSpace(n) && list.Count < 80) list.Add(Limit(n, 120));
            return list;
        }
    }
}
