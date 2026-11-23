using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Net;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// One screen for the four ways in (AdminCaptureScene): Scan Barcode (live, the phone's own reader), Capture Product
    /// (photos labelled front / back / ingredients / barcode / shade), Upload from Gallery (up to 6 photos, labelled the same
    /// way) and Manual Entry. Everything ends in the same review form, where each value shows where it came from and whether
    /// it was checked; only missing values need typing. Submit writes ONE record (the capture id), says "Submitted" only
    /// after the server accepted it, and the website shows it in Waiting for review at once.
    /// </summary>
    public class AdminCaptureController : MonoBehaviour
    {
        public static CaptureDraft.Mode StartMode = CaptureDraft.Mode.Barcode;
        public static bool Resume;
        public static bool OpenBatch;            // the dashboard asked for the batch list

        [Header("Header")]
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private ConfirmDialog confirmDialog;

        [Header("Camera and photos")]
        [SerializeField] private GameObject capturePanel;
        [SerializeField] private CameraFeedController cameraFeed;
        [SerializeField] private RawImage galleryPreview;
        [SerializeField] private RectTransform scanFrame;
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button shutterButton;
        [SerializeField] private Button torchButton;
        [SerializeField] private GameObject roleBar;
        [SerializeField] private Button[] roleButtons;          // Front, Back, Ingredients, Barcode, Shade
        [SerializeField] private RectTransform thumbsContent;
        [SerializeField] private GameObject thumbTemplate;
        [SerializeField] private Button continueButton;
        [SerializeField] private TextMeshProUGUI continueLabel;
        [SerializeField] private Button typeInsteadButton;
        [SerializeField] private Button tipsButton;
        [SerializeField] private OverlayPanel tipsPanel;
        [SerializeField] private ScanLineSweep scanLine;
        [SerializeField] private GameObject checklist;                  // Focus / Lighting / Clear Image, live from the camera
        [SerializeField] private Image checkFocus;
        [SerializeField] private Image checkLighting;
        [SerializeField] private Image checkClear;

        [Header("Batch (Batch Mode in Scanner Preferences)")]
        [SerializeField] private GameObject batchPanel;
        [SerializeField] private RectTransform batchContent;
        [SerializeField] private GameObject batchRowTemplate;
        [SerializeField] private TextMeshProUGUI batchEmptyText;
        [SerializeField] private Button batchSubmitButton;
        [SerializeField] private TextMeshProUGUI batchSubmitLabel;
        [SerializeField] private Button batchAddButton;
        [SerializeField] private Button batchClearButton;

        [Header("Review form (one row per value)")]
        [SerializeField] private GameObject formPanel;
        [SerializeField] private TMP_InputField[] fieldInputs;   // name, brand, cosmeticType, barcode, ingredients, shade
        [SerializeField] private TextMeshProUGUI[] fieldStates;
        [SerializeField] private Button[] checkButtons;
        [SerializeField] private RawImage resultThumb;                  // the scan result card at the top of the form
        [SerializeField] private TextMeshProUGUI resultName;
        [SerializeField] private TextMeshProUGUI resultSub;
        [SerializeField] private TextMeshProUGUI resultPillText;
        [SerializeField] private Image resultPill;
        [SerializeField] private Button tryAgainButton;
        [SerializeField] private TextMeshProUGUI rawText;
        [SerializeField] private Button addPhotosButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private TextMeshProUGUI submitLabel;
        [SerializeField] private GameObject afterSubmit;
        [SerializeField] private Button viewStatusButton;
        [SerializeField] private Button newCaptureButton;

        [Header("Behaviour")]
        [SerializeField] private float barcodeEverySeconds = 0.45f;
        [SerializeField] private string dashboardScene = "AdminDashboardScene";
        [SerializeField] private string submissionsScene = "AdminSubmissionsScene";

        static readonly string[] Roles = { "Front", "Back", "Ingredients", "Barcode", "Shade" };
        static readonly Color Missing = new Color(0.78f, 0.30f, 0.38f), Found = new Color(0.80f, 0.52f, 0.10f), Checked = new Color(0.20f, 0.56f, 0.38f), Typed = new Color(0.45f, 0.40f, 0.42f);

        CaptureDraft draft;
        string role = "Front";
        bool scanning, busy, torchOn, painting, flashPending, focusPending;
        float nextCheck, steadySince;
        static readonly Color Good = new Color(0.20f, 0.62f, 0.42f), Idle = new Color(0.70f, 0.66f, 0.67f);
        float nextRead, cameraAskedAt;
        readonly List<GameObject> thumbs = new List<GameObject>();
        readonly List<GameObject> batchRows = new List<GameObject>();
        BatchQueue.Item editing;                 // the batch item open in the review form
        bool batchFromDashboard;

        void Start()
        {
            bool openBatch = OpenBatch; OpenBatch = false;
            if (openBatch) draft = CaptureDraft.Current;                          // the batch list: the single unsent draft is left alone
            else
            {
                draft = Resume ? CaptureDraft.Current : CaptureDraft.Start(StartMode, false);
                if (!Resume) CaptureDraft.ReleasePhotos();
                draft.mode = StartMode.ToString(); draft.Save();
            }
            Resume = false;

            Hook(backButton, Back);
            Hook(galleryButton, PickFromGallery);
            Hook(shutterButton, () => _ = Shutter());
            Hook(torchButton, ToggleTorch);
            Hook(tipsButton, () => { if (tipsPanel != null) tipsPanel.Show(); });
            Hook(tryAgainButton, TryAgain);
            Hook(continueButton, () => { if (BatchBarcode) ShowBatch(); else _ = ReadAndReview(); });
            Hook(batchSubmitButton, () => _ = SubmitBatch());
            Hook(batchAddButton, () => { if (StartMode == CaptureDraft.Mode.Gallery || batchFromDashboard) PickBatch(); else ShowCapture(StartMode); });
            Hook(batchClearButton, AskClearBatch);
            BatchQueue.Changed += PaintBatch;
            Hook(typeInsteadButton, () => ShowForm("Type the details you have. The rest stays Missing for the website to fill."));
            Hook(addPhotosButton, () => ShowCapture(CaptureDraft.Mode.Photo));
            Hook(submitButton, () => _ = Submit());
            Hook(viewStatusButton, () => { AdminSubmissionsController.OpenId = draft.submittedId; Finish(submissionsScene); });
            Hook(newCaptureButton, () => { CaptureDraft.Clear(); CaptureDraft.ReleasePhotos(); Resume = false; SceneNavigator.Go(SceneNavigatorCurrent()); });
            for (int i = 0; roleButtons != null && i < roleButtons.Length && i < Roles.Length; i++)
            { string r = Roles[i]; Hook(roleButtons[i], () => { role = r; PaintRoles(); }); }
            for (int i = 0; fieldInputs != null && i < fieldInputs.Length && i < CaptureDraft.Keys.Length; i++)
            {
                string key = CaptureDraft.Keys[i];
                if (fieldInputs[i] != null) fieldInputs[i].onEndEdit.AddListener(v => { if (painting) return; draft.Type(key, v); draft.Save(); if (editing != null) BatchQueue.Touch(); PaintForm(); });
            }
            for (int i = 0; checkButtons != null && i < checkButtons.Length && i < CaptureDraft.Keys.Length; i++)
            { string key = CaptureDraft.Keys[i]; Hook(checkButtons[i], () => { draft.Confirm(key); draft.Save(); if (editing != null) BatchQueue.Touch(); PaintForm(); }); }
            if (thumbTemplate != null) thumbTemplate.SetActive(false);
            if (afterSubmit != null) afterSubmit.SetActive(false);

            if (batchPanel != null) batchPanel.SetActive(false);
            if (openBatch) { batchFromDashboard = true; StartMode = CaptureDraft.Mode.Gallery; ShowBatch(); return; }
            if (!string.IsNullOrEmpty(draft.submittedId)) { ShowForm(""); ShowSubmitted(true, ""); return; }
            if (StartMode == CaptureDraft.Mode.Manual) ShowForm("Fill in what you know. Category and ingredients can stay empty if the pack does not show them.");
            else ShowCapture(StartMode);
        }

        string SceneNavigatorCurrent() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        static void Hook(Button b, UnityEngine.Events.UnityAction a) { if (b != null) b.onClick.AddListener(a); }

        // ======================= capture =======================
        void ShowCapture(CaptureDraft.Mode mode)
        {
            StartMode = mode; draft.mode = mode.ToString(); draft.Save();
            if (formPanel != null) formPanel.SetActive(false);
            if (batchPanel != null) batchPanel.SetActive(false);
            if (capturePanel != null) capturePanel.SetActive(true);
            bool gallery = mode == CaptureDraft.Mode.Gallery, barcode = mode == CaptureDraft.Mode.Barcode;
            if (titleText != null) titleText.text = barcode ? "Scan Barcode" : gallery ? "Upload from Gallery" : "Capture Product";
            if (roleBar != null) roleBar.SetActive(!barcode);
            role = barcode ? "Barcode" : gallery ? "Front" : role;
            if (galleryPreview != null) galleryPreview.gameObject.SetActive(gallery);
            if (shutterButton != null) shutterButton.gameObject.SetActive(!gallery);
            if (torchButton != null) torchButton.gameObject.SetActive(!gallery);
            if (cameraFeed != null)
            {
                if (gallery) cameraFeed.StopCamera();
                else { cameraFeed.StartCamera(); cameraAskedAt = Time.realtimeSinceStartup; }
            }
            scanning = barcode;
            Pill(barcode ? (ScannerPrefs.BatchMode ? "Batch: scan one product after another" : "Align the barcode within the frame") : gallery ? "Choose photos from your gallery" : "Keep the product in frame");
            if (barcode && !CaptureReader.CanReadBarcodes) Warn("This phone cannot read barcodes here: take a photo, or type the digits.");
            if (scanFrame != null) scanFrame.gameObject.SetActive(ScannerPrefs.GuideFrame && !gallery);
            if (scanLine != null) scanLine.Allowed = barcode;
            if (checklist != null) checklist.SetActive(!gallery && !barcode);
            flashPending = !gallery && ScannerPrefs.Flash && !torchOn;
            focusPending = !gallery && ScannerPrefs.AutoFocus;
            PaintRoles(); PaintThumbs();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (tipsPanel != null && tipsPanel.IsOpen) { tipsPanel.Hide(); return; }          // Back closes the tips first
                if (!(confirmDialog != null && confirmDialog.IsOpen)) { Back(); return; }
            }
            if (capturePanel == null || !capturePanel.activeSelf || cameraFeed == null || StartMode == CaptureDraft.Mode.Gallery) return;

            if (!cameraFeed.IsPlaying && cameraAskedAt > 0 && Time.realtimeSinceStartup - cameraAskedAt > 4f)
            {
                cameraAskedAt = 0;
                Warn("The camera is not available. Allow GlowUp to use the camera in the phone's Settings, or use Upload from Gallery or Type details instead.");
            }
            if (cameraFeed.IsPlaying)
            {
                if (flashPending) { flashPending = false; torchOn = true; if (!Flashlight.TrySet(true, out string why)) { torchOn = false; if (!string.IsNullOrEmpty(why)) Info(why); } }
                if (focusPending) { focusPending = false; cameraFeed.Refocus(); }
                if (checklist != null && checklist.activeSelf && Time.unscaledTime >= nextCheck) { nextCheck = Time.unscaledTime + 0.3f; PaintChecklist(); }
            }
            if (scanning && !busy && cameraFeed.IsPlaying && Time.realtimeSinceStartup >= nextRead)
            {
                nextRead = Time.realtimeSinceStartup + barcodeEverySeconds;
                _ = TryLiveBarcode();
            }
        }

        async Task TryLiveBarcode()
        {
            Texture2D frame = cameraFeed.CaptureUpright(scanFrame, 1100);
            if (frame == null) return;
            busy = true;
            string code;
            try { code = await CaptureReader.Barcode(frame); }
            finally { Destroy(frame); busy = false; }
            if (this == null || !scanning || string.IsNullOrEmpty(OpenBeautyFacts.Digits(code))) return;
            if (ScannerPrefs.BatchMode) { await AddScannedToBatch(code); return; }          // Batch Mode: scanning goes on
            scanning = false;
            Handheld.Vibrate();
            cameraFeed.StopCamera();
            Hint($"Barcode {code}. Looking it up...");
            busy = true;
            string said = await CaptureReader.FromBarcode(draft, code, true);
            busy = false;
            ShowForm(said);
        }

        async Task Shutter()
        {
            if (busy || cameraFeed == null || !cameraFeed.IsPlaying) { Hint("The camera is not ready yet."); return; }
            if (CaptureDraft.Photos.Count >= CaptureDraft.MaxPhotos) { Hint($"At most {CaptureDraft.MaxPhotos} photos. Remove one, or continue."); return; }
            Texture2D photo = cameraFeed.CaptureUpright(scanFrame, 1600);
            if (photo == null) { Hint("The picture could not be taken. Hold still and try again."); return; }
            if (StartMode == CaptureDraft.Mode.Barcode)
            {
                busy = true; Hint("Reading the barcode in the photo...");
                string code = await CaptureReader.Barcode(photo);
                busy = false;
                if (string.IsNullOrEmpty(OpenBeautyFacts.Digits(code))) { Destroy(photo); Hint("No barcode in that photo. Move closer, keep the bars sharp, or type the digits."); return; }
                CaptureDraft.Photos.Add(new KeyValuePair<string, Texture2D>("Barcode", photo));
                scanning = false; cameraFeed.StopCamera();
                ShowForm(await CaptureReader.FromBarcode(draft, code, true));
                return;
            }
            CaptureDraft.Photos.Add(new KeyValuePair<string, Texture2D>(role, photo));
            Hint($"{role} photo added ({CaptureDraft.Photos.Count}). Take another, or tap Read & review.");
            PaintThumbs();
        }

        void PickFromGallery()
        {
            if (ScannerPrefs.BatchMode && StartMode == CaptureDraft.Mode.Gallery) { PickBatch(); return; }
            if (CaptureDraft.Photos.Count >= CaptureDraft.MaxPhotos) { Hint($"At most {CaptureDraft.MaxPhotos} photos. Remove one first."); return; }
            GalleryPicker.Pick(tex =>
            {
                if (this == null) { if (tex != null) Destroy(tex); return; }
                if (tex == null) { Hint("No photo was chosen."); return; }
                string r = StartMode == CaptureDraft.Mode.Barcode ? "Barcode" : role;
                CaptureDraft.Photos.Add(new KeyValuePair<string, Texture2D>(r, tex));
                if (galleryPreview != null) galleryPreview.texture = tex;
                if (StartMode == CaptureDraft.Mode.Barcode) { _ = ReadAndReview(); return; }
                Hint($"{r} photo added ({CaptureDraft.Photos.Count}). Change its label below if needed, add more, or tap Read & review.");
                PaintThumbs();
            }, problem => Hint(problem));
        }

        void ToggleTorch()
        {
            torchOn = !torchOn;
            if (!Flashlight.TrySet(torchOn, out string problem)) { torchOn = false; Hint(string.IsNullOrEmpty(problem) ? "This phone has no light to switch on." : problem); }
        }

        /// <summary>Reads every photo once (barcode photos for digits, the rest for text), then opens the review form.</summary>
        async Task ReadAndReview()
        {
            if (busy) return;
            if (CaptureDraft.Photos.Count == 0 && !draft.HasContent) { Hint("Take or add at least one photo first, or tap Type details instead."); return; }
            busy = true;
            if (continueButton != null) continueButton.interactable = false;
            if (cameraFeed != null) cameraFeed.StopCamera();
            var said = new List<string>();
            try
            {
                for (int i = 0; i < CaptureDraft.Photos.Count; i++)
                {
                    var p = CaptureDraft.Photos[i];
                    Hint($"Reading photo {i + 1} of {CaptureDraft.Photos.Count} ({p.Key})...");
                    if (p.Key == "Barcode")
                    {
                        string code = await CaptureReader.Barcode(p.Value);
                        said.Add(string.IsNullOrEmpty(OpenBeautyFacts.Digits(code)) ? "No barcode found in the barcode photo." : await CaptureReader.FromBarcode(draft, code, true));
                    }
                    else
                    {
                        string text = await CaptureReader.Text(p.Value);
                        if (!CaptureReader.CanReadText) { said.Add("This phone cannot read text here: type the details from the pack."); break; }
                        said.Add(CaptureReader.FromText(draft, p.Key, text));
                    }
                }
            }
            catch (Exception) { said.Add("A photo could not be read. Type what is missing."); }
            finally { busy = false; if (continueButton != null) continueButton.interactable = true; }
            ShowForm(string.Join(" ", said));
        }

        // ======================= review form =======================
        void ShowForm(string notice)
        {
            scanning = false;
            if (cameraFeed != null) cameraFeed.StopCamera();
            if (torchOn) { Flashlight.TrySet(false, out _); torchOn = false; }
            if (capturePanel != null) capturePanel.SetActive(false);
            if (formPanel != null) formPanel.SetActive(true);
            if (titleText != null) titleText.text = StartMode == CaptureDraft.Mode.Manual && CaptureDraft.Photos.Count == 0 ? "Manual Entry" : "Review & Submit";
            if (!string.IsNullOrWhiteSpace(notice)) Info(notice);
            PaintForm();
        }

        void PaintForm()
        {
            painting = true;
            for (int i = 0; i < CaptureDraft.Keys.Length; i++)
            {
                CaptureDraft.Field f = draft.Get(CaptureDraft.Keys[i]);
                if (fieldInputs != null && i < fieldInputs.Length && fieldInputs[i] != null && !fieldInputs[i].isFocused) fieldInputs[i].SetTextWithoutNotify(f.value);
                if (fieldStates != null && i < fieldStates.Length && fieldStates[i] != null)
                {
                    string state = !f.Has ? "Missing" : f.state == "verified" ? "Checked" : f.state == "typed" ? "Typed" : f.state == "review" ? "Needs review" : "Detected";
                    fieldStates[i].text = state + (f.Has && !string.IsNullOrEmpty(f.from) && f.state != "typed" ? " · " + f.from : "");
                    fieldStates[i].color = !f.Has ? Missing : f.state == "verified" ? Checked : f.state == "typed" ? Typed : Found;
                }
                if (checkButtons != null && i < checkButtons.Length && checkButtons[i] != null)
                    checkButtons[i].gameObject.SetActive(f.Has && (f.state == "detected" || f.state == "review"));
            }
            if (rawText != null)
                rawText.text = draft.rawText.Length > 0 ? $"Text as read ({draft.rawFrom}):\n{draft.rawText}" :
                    CaptureDraft.Photos.Count > 0 ? $"{CaptureDraft.Photos.Count} photo(s) attached." : "No photos. Add photos so the reviewer can compare.";
            PaintResultCard(string.IsNullOrEmpty(draft.submittedId) ? null : "submitted");
            painting = false;
        }

        /// <summary>The "Scan Result" card at the top of the form: the first photo, the name, the barcode or brand, and one status pill.</summary>
        void PaintResultCard(string submitted)
        {
            CaptureDraft.Field name = draft.Get("name"), brand = draft.Get("brand"), code = draft.Get("barcode");
            if (resultName != null) resultName.text = name.Has ? name.value : "Product name needed";
            if (resultSub != null) resultSub.text = code.Has ? "Barcode " + code.value : brand.Has ? brand.value : "No barcode yet";
            if (resultThumb != null)
            {
                Texture t = CaptureDraft.Photos.Count > 0 ? CaptureDraft.Photos[0].Value : null;
                resultThumb.texture = t; resultThumb.color = t != null ? Color.white : new Color(1, 1, 1, 0f);
            }
            string pill; Color tint;
            if (submitted != null) { pill = "Submitted"; tint = Good; }
            else if (!string.IsNullOrEmpty(draft.duplicateOf)) { pill = "Already in GlowUp"; tint = Found; }
            else if (name.Has && name.state != "typed") { pill = "Product Found"; tint = Good; }
            else { pill = "Details needed"; tint = Missing; }
            if (resultPillText != null) { resultPillText.text = pill; resultPillText.color = tint; }
            if (resultPill != null) resultPill.color = new Color(tint.r, tint.g, tint.b, 0.16f);
        }

        async Task Submit()
        {
            if (busy) return;
            // what the website needs to file it: a name, a brand, a category (the rest may stay Missing)
            foreach (string key in new[] { "name", "brand", "cosmeticType" })
                if (!draft.Get(key).Has) { Notice($"{CaptureDraft.Label(key)} is needed. Type it from the pack."); FocusField(key); return; }
            string type = draft.Get("cosmeticType").value, name = draft.Get("name").value;
            // the same rule as the user's live scan: only a product that is clearly not make-up is stopped; an unclear one goes to the website to be decided
            ProductScope.Verdict scope = ProductScope.Judge(type, name);
            if (scope.IsNotMakeup) { Notice(ProductScope.StopMessage(scope)); FocusField("cosmeticType"); return; }
            if (editing != null)
            {   // a batch item: it is kept in the batch, and sent with "Send ready"
                editing.state = ""; BatchQueue.Touch();
                CaptureDraft.Photos.Clear(); editing = null;
                Info("Saved to the batch.");
                ShowBatch();
                return;
            }
            if (!ConnectivityMonitor.Online) Notice("You are offline: the submission is kept as \"Not yet synced\" and is sent when you are back online.");
            if (!string.IsNullOrEmpty(draft.duplicateOf) && confirmDialog != null)
            {
                confirmDialog.Ask("Possible duplicate", $"GlowUp already lists \"{draft.duplicateName}\" with this barcode. Submit this as a different version for the website to compare?",
                    "Submit anyway", () => _ = DoSubmit(), false, "Cancel");
                return;
            }
            await DoSubmit();
        }

        async Task DoSubmit()
        {
            busy = true;
            if (submitButton != null) submitButton.interactable = false;               // one tap = one submission
            if (submitLabel != null) submitLabel.text = "Submitting...";
            SubmissionStore.Outcome o;
            try { o = await SubmissionStore.Submit(draft); }
            catch (Exception) { o = new SubmissionStore.Outcome { message = "It was not submitted. Check the connection and try again." }; }
            busy = false;
            if (this == null) return;
            if (o.confirmed || o.queued)
            {
                draft.submittedId = o.id; draft.submittedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); draft.Save();
                SubmissionStore.Start();
                ShowSubmitted(o.confirmed, o.message);
            }
            else
            {
                if (submitButton != null) submitButton.interactable = true;
                if (submitLabel != null) submitLabel.text = "Save Product";
                Warn(o.message);
            }
        }

        void ShowSubmitted(bool confirmed, string extra)
        {
            if (submitButton != null) submitButton.gameObject.SetActive(false);
            if (tryAgainButton != null) tryAgainButton.gameObject.SetActive(false);
            if (afterSubmit != null) afterSubmit.SetActive(true);
            string when = DateTimeOffset.FromUnixTimeSeconds(Math.Max(draft.submittedUnix, 1)).ToLocalTime().ToString("d MMM yyyy, HH:mm");
            bool sent = confirmed || SubmissionStore.Find(draft.submittedId) != null;
            PaintResultCard("submitted");
            if (resultSub != null) resultSub.text = (sent ? "Pending review on the website" : "Not yet synced") + " · " + when;
            if (resultPillText != null && !sent) { resultPillText.text = "Not yet synced"; resultPillText.color = Found; }
            if (sent) StaffToast.Ok(this, "Submitted. It is waiting for review on the website.");
            else StaffToast.Warn(this, "Saved as Not yet synced. It is sent by itself when the connection allows." + (string.IsNullOrEmpty(extra) ? "" : " " + extra));
        }

        // ======================= batch =======================
        bool BatchBarcode => ScannerPrefs.BatchMode && StartMode == CaptureDraft.Mode.Barcode;
        static string BatchLabel() => BatchQueue.Count > 0 ? $"Review batch ({BatchQueue.Count})" : "Review batch";

        async Task AddScannedToBatch(string code)
        {
            if (BatchQueue.Contains(code)) { Info("Already in this batch."); nextRead = Time.realtimeSinceStartup + 2f; return; }
            if (BatchQueue.Full) { scanning = false; Warn($"The batch is full ({BatchQueue.MaxItems}). Review it and send what is ready."); return; }
            busy = true;
            Handheld.Vibrate();
            try
            {
                CaptureDraft d = CaptureDraft.Blank(CaptureDraft.Mode.Barcode);
                await CaptureReader.FromBarcode(d, code, true);
                BatchQueue.Add(d, null);
                string name = d.Get("name").Has ? d.Get("name").value : "barcode " + d.Get("barcode").value;
                if (this != null) Info($"Added {name}. {BatchQueue.Count} in the batch.");
            }
            finally { busy = false; }
            nextRead = Time.realtimeSinceStartup + 1.2f;
            if (this != null && continueLabel != null) continueLabel.text = BatchLabel();
        }

        /// <summary>Several gallery photos at once: each becomes one product in the batch.</summary>
        void PickBatch()
        {
            int room = BatchQueue.MaxItems - BatchQueue.Items.Count;
            if (room <= 0) { Warn($"The batch is full ({BatchQueue.MaxItems}). Send what is ready first."); return; }
            var incoming = new List<Texture2D>();
            GalleryPicker.PickMany(Mathf.Min(12, room), tex => { if (tex != null) incoming.Add(tex); }, count => _ = ReadIncoming(incoming, count), problem => Warn(problem));
        }

        async Task ReadIncoming(List<Texture2D> incoming, int count)
        {
            if (incoming.Count == 0) { if (count == 0 && this != null) Info("No photos were chosen."); return; }
            busy = true;
            int n = 0;
            try
            {
                foreach (Texture2D tex in incoming)
                {
                    n++;
                    if (this != null) Pill($"Reading photo {n} of {incoming.Count}...");
                    await BatchQueue.AddPhoto(tex);
                }
            }
            catch (Exception) { if (this != null) Warn("A photo could not be read. The others were added."); }
            finally { busy = false; }
            if (this == null) return;
            Pill("Choose photos from your gallery");
            Info($"{n} photo{(n == 1 ? "" : "s")} added to the batch.");
            ShowBatch();
        }

        void ShowBatch()
        {
            scanning = false;
            if (cameraFeed != null) cameraFeed.StopCamera();
            if (torchOn) { Flashlight.TrySet(false, out _); torchOn = false; }
            if (capturePanel != null) capturePanel.SetActive(false);
            if (formPanel != null) formPanel.SetActive(false);
            if (batchPanel != null) batchPanel.SetActive(true);
            if (!BatchQueue.IsSending) BatchQueue.DropSent();
            PaintBatch();
        }

        void PaintBatch()
        {
            if (this == null || batchPanel == null || !batchPanel.activeSelf) return;
            foreach (GameObject r in batchRows) if (r != null) Destroy(r);
            batchRows.Clear();
            if (batchRowTemplate != null) batchRowTemplate.SetActive(false);
            BatchQueue.EnsureLoaded();
            if (titleText != null) titleText.text = BatchQueue.Count > 0 ? $"Batch ({BatchQueue.Count})" : "Batch";
            foreach (BatchQueue.Item item in BatchQueue.Items)
            {
                if (batchRowTemplate == null || batchContent == null) break;
                GameObject row = Instantiate(batchRowTemplate, batchContent);
                row.SetActive(true); row.name = "Batch_" + item.draft.captureId;
                CaptureDraft d = item.draft;
                string status = item.state == "sending" ? "Sending..." : item.Sent ? (string.IsNullOrEmpty(item.message) ? "Sent" : item.message) : item.state == "failed" ? "Not sent" : item.Ready ? "Ready" : "Needs details";
                Color tint = item.state == "sending" ? Found : item.Sent ? Good : item.state == "failed" ? Missing : item.Ready ? Good : Missing;
                foreach (TextMeshProUGUI t in row.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (t.name == "NameText") t.text = d.Get("name").Has ? d.Get("name").value : "Name needed";
                    else if (t.name == "BrandText") t.text = d.Get("brand").Has ? d.Get("brand").value : "Brand needed";
                    else if (t.name == "TimeText") t.text = d.Get("barcode").Has ? d.Get("barcode").value : "No barcode";
                    else if (t.name == "StatusText") { t.text = status; t.color = tint; }
                }
                foreach (Image img in row.GetComponentsInChildren<Image>(true)) if (img.name == "StatusPill") img.color = new Color(tint.r, tint.g, tint.b, 0.16f);
                BatchQueue.Item captured = item;
                Button b = row.GetComponent<Button>();
                if (b != null) b.onClick.AddListener(() => OpenBatchItem(captured));
                batchRows.Add(row);
            }
            if (batchEmptyText != null)
            {
                batchEmptyText.gameObject.SetActive(batchRows.Count == 0);
                batchEmptyText.text = ScannerPrefs.BatchMode ? "Nothing in the batch yet. Scan products one after another, or add several gallery photos." : "Nothing in the batch. Turn on Batch Mode in Scanner Preferences.";
            }
            int ready = BatchQueue.ReadyCount;
            if (batchSubmitLabel != null) batchSubmitLabel.text = BatchQueue.IsSending ? "Sending..." : ready > 0 ? $"Send ready ({ready})" : "Nothing ready to send";
            if (batchSubmitButton != null) batchSubmitButton.interactable = ready > 0 && !BatchQueue.IsSending;
            if (batchClearButton != null) batchClearButton.interactable = BatchQueue.Items.Count > 0 && !BatchQueue.IsSending;
            if (batchAddButton != null) batchAddButton.gameObject.SetActive(batchFromDashboard ? ScannerPrefs.BatchMode : true);
        }

        void OpenBatchItem(BatchQueue.Item item)
        {
            if (item.Sent) { Info("That one was sent. It is on the website."); return; }
            if (item.state == "sending") { Info("It is being sent."); return; }
            editing = item; draft = item.draft;
            CaptureDraft.Photos.Clear();
            if (item.photo != null) CaptureDraft.Photos.Add(new KeyValuePair<string, Texture2D>("Front", item.photo));
            if (batchPanel != null) batchPanel.SetActive(false);
            ShowForm(item.Ready ? "" : "Add what is missing: a name, a brand and a make-up category.");
            if (titleText != null) titleText.text = "Batch item";
            if (submitLabel != null) submitLabel.text = "Save to batch";
            TextMeshProUGUI again = tryAgainButton != null ? tryAgainButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (again != null) again.text = "Remove from batch";
        }

        async Task SubmitBatch()
        {
            if (BatchQueue.IsSending) return;
            int need = BatchQueue.NeedDetails;
            if (BatchQueue.ReadyCount == 0) { Warn(need > 0 ? $"{need} still need a name, a brand and a category. Tap one to add them." : "Nothing is ready to send."); return; }
            if (!ConnectivityMonitor.Online) Info("You are offline: they are kept and sent when you are back online.");
            PaintBatch();
            int sent = await BatchQueue.SubmitReady();
            if (this == null) return;
            int failed = 0;
            foreach (BatchQueue.Item i in BatchQueue.Items) if (i.state == "failed") failed++;
            if (sent > 0) StaffToast.Ok(this, $"Sent {sent}. Waiting for review on the website." + (need > 0 ? $" {need} still need details." : ""));
            if (failed > 0) Warn($"{failed} could not be sent. Tap Send ready to try again.");
            PaintBatch();
        }

        void AskClearBatch()
        {
            void Clear() { BatchQueue.Clear(); Info("The batch was cleared."); PaintBatch(); }
            if (confirmDialog != null) confirmDialog.Ask("Clear the batch?", "The products in this batch that were not sent are removed from this phone.", "Clear", Clear, true, "Keep");
            else Clear();
        }

        // ======================= small parts =======================
        void PaintRoles()
        {
            for (int i = 0; roleButtons != null && i < roleButtons.Length && i < Roles.Length; i++)
            {
                if (roleButtons[i] == null) continue;
                var img = roleButtons[i].targetGraphic as Image;
                if (img != null) img.color = Roles[i] == role ? new Color(0.86f, 0.45f, 0.55f, 1f) : new Color(1, 1, 1, 0.9f);
                var label = roleButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.color = Roles[i] == role ? Color.white : new Color(0.35f, 0.28f, 0.30f);
            }
        }

        void PaintThumbs()
        {
            foreach (GameObject t in thumbs) if (t != null) Destroy(t);
            thumbs.Clear();
            if (thumbsContent == null || thumbTemplate == null) return;
            for (int i = 0; i < CaptureDraft.Photos.Count; i++)
            {
                int at = i;
                GameObject t = Instantiate(thumbTemplate, thumbsContent);
                t.SetActive(true); t.name = "Photo_" + (i + 1);
                foreach (RawImage r in t.GetComponentsInChildren<RawImage>(true)) if (r.name == "Picture") r.texture = CaptureDraft.Photos[i].Value;
                foreach (TextMeshProUGUI l in t.GetComponentsInChildren<TextMeshProUGUI>(true)) if (l.name == "RoleText") l.text = CaptureDraft.Photos[i].Key;
                foreach (Button b in t.GetComponentsInChildren<Button>(true))
                {
                    if (b.name == "RemoveButton") b.onClick.AddListener(() => { if (CaptureDraft.Photos[at].Value != null) Destroy(CaptureDraft.Photos[at].Value); CaptureDraft.Photos.RemoveAt(at); PaintThumbs(); });
                    else if (b.name == "RoleButton") b.onClick.AddListener(() =>
                    {   // next label in the list: the staff member says what the photo shows
                        int next = (Array.IndexOf(Roles, CaptureDraft.Photos[at].Key) + 1) % Roles.Length;
                        CaptureDraft.Photos[at] = new KeyValuePair<string, Texture2D>(Roles[next], CaptureDraft.Photos[at].Value);
                        PaintThumbs();
                    });
                }
                thumbs.Add(t);
            }
            if (continueLabel != null) continueLabel.text = BatchBarcode ? BatchLabel() : CaptureDraft.Photos.Count > 0 ? $"Read & review ({CaptureDraft.Photos.Count})" : "Read & review";
        }

        // 23 Nov 2026: this screen says things with the app's toast only (FormalToast places it and keeps it clear of buttons); the pill
        // on the camera is a fixed instruction, and nothing is written to the device log.
        void Pill(string text) { if (hintText != null) hintText.text = text ?? ""; }
        void Info(string text) { StaffToast.Info(this, text); }
        void Warn(string text) { StaffToast.Warn(this, text); }
        void Hint(string text) { StaffToast.Info(this, text); }
        void Notice(string text) { StaffToast.Warn(this, text); }

        void TryAgain()
        {
            if (editing != null) { CaptureDraft.Photos.Clear(); BatchQueue.Remove(editing); editing = null; Info("Removed from the batch."); ShowBatch(); return; }
            CaptureDraft.Clear(); CaptureDraft.ReleasePhotos();
            CaptureDraft.Mode again = StartMode == CaptureDraft.Mode.Manual ? CaptureDraft.Mode.Photo : StartMode;
            draft = CaptureDraft.Start(again, false);
            ShowCapture(again);
        }

        /// <summary>Focus (the picture holds still), Lighting (neither dark nor blown out), Clear Image (both): measured live from the camera.</summary>
        void PaintChecklist()
        {
            if (cameraFeed == null || !cameraFeed.Grab()) return;
            float brightness = cameraFeed.MeasureBrightness();
            bool still = cameraFeed.LastMotion < 0.035f;
            steadySince = still ? (steadySince > 0 ? steadySince : Time.unscaledTime) : 0f;
            bool focus = still && Time.unscaledTime - steadySince >= 0.5f;
            bool light = brightness > 0.22f && brightness < 0.93f;
            if (checkFocus != null) checkFocus.color = focus ? Good : Idle;
            if (checkLighting != null) checkLighting.color = light ? Good : Idle;
            if (checkClear != null) checkClear.color = focus && light ? Good : Idle;
        }
        void FocusField(string key)
        {
            int i = Array.IndexOf(CaptureDraft.Keys, key);
            if (fieldInputs != null && i >= 0 && i < fieldInputs.Length && fieldInputs[i] != null) fieldInputs[i].ActivateInputField();
        }

        void Back()
        {
            if (editing != null) { CaptureDraft.Photos.Clear(); editing = null; ShowBatch(); return; }                // from a batch item back to the list
            if (batchPanel != null && batchPanel.activeSelf)
            {
                if (batchFromDashboard) Finish(dashboardScene); else ShowCapture(StartMode == CaptureDraft.Mode.Manual ? CaptureDraft.Mode.Photo : StartMode);
                return;
            }
            // the draft is already saved on the phone; leaving never loses it
            if (formPanel != null && formPanel.activeSelf && StartMode != CaptureDraft.Mode.Manual && string.IsNullOrEmpty(draft.submittedId)) { ShowCapture(StartMode); return; }
            Finish(dashboardScene);
        }

        void Finish(string scene)
        {
            if (cameraFeed != null) cameraFeed.StopCamera();
            if (torchOn) Flashlight.TrySet(false, out _);
            if (!string.IsNullOrEmpty(draft.submittedId)) { CaptureDraft.Clear(); CaptureDraft.ReleasePhotos(); }
            SceneNavigator.Go(scene);
        }

        void OnDestroy() { BatchQueue.Changed -= PaintBatch; if (torchOn) Flashlight.TrySet(false, out _); }
    }
}
