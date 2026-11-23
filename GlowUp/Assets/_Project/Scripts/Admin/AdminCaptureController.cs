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

        [Header("Review form (one row per value)")]
        [SerializeField] private GameObject formPanel;
        [SerializeField] private TMP_InputField[] fieldInputs;   // name, brand, cosmeticType, barcode, ingredients, shade
        [SerializeField] private TextMeshProUGUI[] fieldStates;
        [SerializeField] private Button[] checkButtons;
        [SerializeField] private TextMeshProUGUI noticeText;
        [SerializeField] private TextMeshProUGUI rawText;
        [SerializeField] private Button addPhotosButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private TextMeshProUGUI submitLabel;
        [SerializeField] private TextMeshProUGUI resultText;
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
        bool scanning, busy, torchOn, painting;
        float nextRead, cameraAskedAt;
        readonly List<GameObject> thumbs = new List<GameObject>();

        void Start()
        {
            draft = Resume ? CaptureDraft.Current : CaptureDraft.Start(StartMode, false);
            if (!Resume) CaptureDraft.ReleasePhotos();
            draft.mode = StartMode.ToString(); draft.Save();
            Resume = false;

            Hook(backButton, Back);
            Hook(galleryButton, PickFromGallery);
            Hook(shutterButton, () => _ = Shutter());
            Hook(torchButton, ToggleTorch);
            Hook(continueButton, () => _ = ReadAndReview());
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
                if (fieldInputs[i] != null) fieldInputs[i].onEndEdit.AddListener(v => { if (painting) return; draft.Type(key, v); draft.Save(); PaintForm(); });
            }
            for (int i = 0; checkButtons != null && i < checkButtons.Length && i < CaptureDraft.Keys.Length; i++)
            { string key = CaptureDraft.Keys[i]; Hook(checkButtons[i], () => { draft.Confirm(key); draft.Save(); PaintForm(); }); }
            if (thumbTemplate != null) thumbTemplate.SetActive(false);
            if (afterSubmit != null) afterSubmit.SetActive(false);

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
            Hint(barcode ? (CaptureReader.CanReadBarcodes ? "Align the barcode within the frame. It reads by itself." : "This phone cannot read barcodes here: take a photo, or type the digits.")
                : gallery ? "Tap Add photo, then say what each photo shows. Photos stay on this phone; up to 3 small copies go to the reviewer."
                : "Choose what the photo shows, then tap the round button. Front, ingredient list and barcode give the best result.");
            PaintRoles(); PaintThumbs();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !(confirmDialog != null && confirmDialog.IsOpen)) { Back(); return; }
            if (capturePanel == null || !capturePanel.activeSelf || cameraFeed == null || StartMode == CaptureDraft.Mode.Gallery) return;

            if (!cameraFeed.IsPlaying && cameraAskedAt > 0 && Time.realtimeSinceStartup - cameraAskedAt > 4f)
            {
                cameraAskedAt = 0;
                Hint("The camera is not available. Allow GlowUp to use the camera in the phone's Settings, or use Upload from Gallery or Type details instead.");
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
            if (noticeText != null) noticeText.text = notice ?? "";
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
            if (noticeText != null && !string.IsNullOrEmpty(draft.duplicateOf) && !noticeText.text.Contains("Already in GlowUp"))
                noticeText.text = $"Already in GlowUp as \"{draft.duplicateName}\". Submit only for a different version (size, shade, formula). " + noticeText.text;
            painting = false;
        }

        async Task Submit()
        {
            if (busy) return;
            // what the website needs to file it: a name, a brand, a category (the rest may stay Missing)
            foreach (string key in new[] { "name", "brand", "cosmeticType" })
                if (!draft.Get(key).Has) { Notice($"{CaptureDraft.Label(key)} is needed. Type it from the pack."); FocusField(key); return; }
            string type = draft.Get("cosmeticType").value, name = draft.Get("name").value;
            if (!ProductScope.IsMakeup(type, name) && !ProductScope.IsMakeup("", type))
            { Notice("GlowUp lists make-up and face products only. Check the category (for example Lipstick, Foundation, Mascara)."); FocusField("cosmeticType"); return; }
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
            if (resultText != null) resultText.text = "Sending to the website...";
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
                if (submitLabel != null) submitLabel.text = "Submit to Website";
                if (resultText != null) resultText.text = o.message;
            }
        }

        void ShowSubmitted(bool confirmed, string extra)
        {
            if (submitButton != null) submitButton.gameObject.SetActive(false);
            if (afterSubmit != null) afterSubmit.SetActive(true);
            string when = DateTimeOffset.FromUnixTimeSeconds(Math.Max(draft.submittedUnix, 1)).ToLocalTime().ToString("d MMM yyyy, HH:mm");
            if (resultText != null)
                resultText.text = (confirmed || SubmissionStore.Find(draft.submittedId) != null
                    ? $"Submitted. Reference {draft.submittedId} · {when}. Status: Pending review on the website."
                    : $"Saved as Not yet synced. Reference {draft.submittedId}. It is sent by itself when the connection allows.")
                    + (string.IsNullOrEmpty(extra) ? "" : " " + extra);
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
            if (continueLabel != null) continueLabel.text = CaptureDraft.Photos.Count > 0 ? $"Read & review ({CaptureDraft.Photos.Count})" : "Read & review";
        }

        void Hint(string text) { if (hintText != null) hintText.text = text ?? ""; }
        void Notice(string text) { if (noticeText != null) noticeText.text = text ?? ""; }
        void FocusField(string key)
        {
            int i = Array.IndexOf(CaptureDraft.Keys, key);
            if (fieldInputs != null && i >= 0 && i < fieldInputs.Length && fieldInputs[i] != null) fieldInputs[i].ActivateInputField();
        }

        void Back()
        {
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

        void OnDestroy() { if (torchOn) Flashlight.TrySet(false, out _); }
    }
}
