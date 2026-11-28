using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Review
{
    /// <summary>
    /// SEND FOR REVIEW (redesign plan, 27 November 2026). A product GlowUp could not find, sent to the admins as text only:
    /// barcode, brand, product name, the ingredient text read ON THIS PHONE from a photo (camera or gallery) and checked by the
    /// user, and a short note. No photo and nothing about the user's skin profile leaves the phone.
    /// The report is a support report of the kind "New product": it shows in the website's Review queue and the admin's reply
    /// reaches the user's Notifications. Firestore keeps the write on the phone while offline and sends it when online
    /// (the same document id, so it never arrives twice).
    /// Navigation rules: Back asks first only when something was entered; Back on the dialog keeps editing; leaving releases
    /// the camera; a camera that is not allowed offers Open Settings and the gallery; sending shows progress with Cancel and
    /// offers Retry after <see cref="retryAfterSeconds"/>; a sent report offers Scan another and Home.
    /// </summary>
    public class SendForReviewController : MonoBehaviour
    {
        public const string Category = "New product";

        [Header("Form")]
        [SerializeField] private Button backButton;
        [SerializeField] private GameObject formPanel;
        [SerializeField] private ScrollRect formScroll;
        [SerializeField] private TMP_InputField barcodeInput;
        [SerializeField] private TMP_InputField brandInput;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private TMP_InputField ingredientInput;
        [SerializeField] private TMP_InputField noteInput;
        [SerializeField] private Button scanButton;
        [SerializeField] private Button takePhotoButton;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button sendButton;
        [SerializeField] private TextMeshProUGUI sendLabel;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Messages on the form (each shows only when it applies)")]
        [SerializeField] private GameObject cameraOffCard;
        [SerializeField] private Button openSettingsButton;
        [SerializeField] private Button cameraOffGalleryButton;
        [SerializeField] private GameObject offlineCard;
        [SerializeField] private GameObject failedCard;
        [SerializeField] private Button retryButton;
        [SerializeField] private GameObject sendingCard;
        [SerializeField] private TextMeshProUGUI sendingText;
        [SerializeField] private Button cancelSendButton;

        [Header("Camera (barcode or label photo)")]
        [SerializeField] private GameObject cameraPanel;
        [SerializeField] private CameraFeedController cameraFeed;
        [SerializeField] private RectTransform scanFrame;
        [SerializeField] private TextMeshProUGUI cameraHint;
        [SerializeField] private Button shutterButton;
        [SerializeField] private Button cameraCancelButton;

        [Header("Sent")]
        [SerializeField] private GameObject sentPanel;
        [SerializeField] private TextMeshProUGUI sentText;
        [SerializeField] private Button scanAnotherButton;
        [SerializeField] private Button homeButton;

        [SerializeField] private ConfirmDialog confirmDialog;

        [Header("Settings")]
        [Tooltip("Seconds of sending before Retry is offered")]
        [SerializeField] private float retryAfterSeconds = 15f;
        [SerializeField] private float barcodeEverySeconds = 0.4f;
        [SerializeField] private int ingredientLimit = 4000;
        [SerializeField] private int noteLimit = 500;

        [Header("Words")]
        [SerializeField] private string msgNeedOne = "Enter a barcode or a product name first.";
        [SerializeField] private string msgBadBarcode = "A barcode has 6 to 14 digits. Correct it, or leave it empty and give the product name.";
        [SerializeField] private string msgSignIn = "Sign in first: reports come from a GlowUp account.";
        [SerializeField] private string msgReading = "Reading the text on this phone...";
        [SerializeField] private string msgReadDone = "Read on this phone. Check and correct it. The photo is not sent.";
        [SerializeField] private string msgNothingRead = "No text was found in that photo. Hold the label flat, in good light, and try again, or type it.";
        [SerializeField] private string msgBarcodeHint = "Hold the barcode inside the frame";
        [SerializeField] private string msgPhotoHint = "Fill the frame with the ingredient list, then tap the button";
        [SerializeField] private string msgBarcodeRead = "Barcode read. Check the digits.";
        [SerializeField] private string msgSending = "Sending...";
        [SerializeField] private string msgStillSending = "Still sending. Retry checks again; the report is never sent twice.";
        [SerializeField] private string msgStoppedWaiting = "Stopped waiting. Your entries are kept. If the report already left, it arrives only once.";
        [SerializeField] private string msgSent = "An admin will check it. The reply appears in Notifications.";
        [SerializeField] private string msgSentOffline = "No internet: your report is kept on this phone and sent when you are online. The reply appears in Notifications.";
        [SerializeField] private string confirmTitle = "Discard this report?";
        [SerializeField] private string confirmMessage = "What you entered is not sent.";
        [SerializeField] private string confirmDiscard = "Discard";
        [SerializeField] private string confirmKeep = "Keep editing";

        enum CameraMode { None, Barcode, Photo }
        CameraMode mode = CameraMode.None;
        string docId;
        bool sending, waiting, reading, busyFrame, sent, leaving;
        float nextRead;
        (string barcode, string brand, string name) prefill;
        MlKitBarcodeReader barcodeReader;
        MlKitTextReader textReader;

        void Awake()
        {
            prefill = SendForReviewHandoff.Take();
            Set(barcodeInput, prefill.barcode); Set(brandInput, prefill.brand); Set(nameInput, prefill.name);
            if (ingredientInput != null) ingredientInput.characterLimit = ingredientLimit;
            if (noteInput != null) noteInput.characterLimit = noteLimit;

            if (backButton != null) backButton.onClick.AddListener(Back);
            if (scanButton != null) scanButton.onClick.AddListener(() => OpenCamera(CameraMode.Barcode));
            if (takePhotoButton != null) takePhotoButton.onClick.AddListener(() => OpenCamera(CameraMode.Photo));
            if (galleryButton != null) galleryButton.onClick.AddListener(FromGallery);
            if (cameraOffGalleryButton != null) cameraOffGalleryButton.onClick.AddListener(FromGallery);
            if (openSettingsButton != null) openSettingsButton.onClick.AddListener(CameraPermission.OpenAppSettings);
            if (sendButton != null) sendButton.onClick.AddListener(() => _ = Send());
            if (retryButton != null) retryButton.onClick.AddListener(() => _ = Send());
            if (cancelSendButton != null) cancelSendButton.onClick.AddListener(StopWaiting);
            if (shutterButton != null) shutterButton.onClick.AddListener(() => _ = TakePhoto());
            if (cameraCancelButton != null) cameraCancelButton.onClick.AddListener(CloseCamera);
            if (scanAnotherButton != null) scanAnotherButton.onClick.AddListener(() => Leave(SceneNames.AiSmartAnalysis));
            if (homeButton != null) homeButton.onClick.AddListener(() => Leave(SceneNames.Dashboard));
            if (cameraFeed != null) cameraFeed.Started += OnCameraStarted;

            Show(cameraOffCard, false); Show(offlineCard, false); Show(failedCard, false); Show(sendingCard, false);
            Show(cameraPanel, false); Show(sentPanel, false); Show(formPanel, true);
            Say("");
        }

        void OnDestroy()
        {
            if (cameraFeed != null) { cameraFeed.Started -= OnCameraStarted; cameraFeed.StopCamera(); }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (confirmDialog != null && confirmDialog.IsOpen) return;               // the dialog's own Back = Keep editing
                if (cameraPanel != null && cameraPanel.activeSelf) { CloseCamera(); return; }
                Back();
                return;
            }
            if (mode == CameraMode.Barcode && !busyFrame && cameraFeed != null && cameraFeed.IsPlaying && Time.realtimeSinceStartup >= nextRead)
            {
                nextRead = Time.realtimeSinceStartup + barcodeEverySeconds;
                _ = TryLiveBarcode();
            }
        }

        // ---------------------------------------------------------------------------------------
        //  Leaving
        // ---------------------------------------------------------------------------------------
        bool Entered()
        {
            if (sent) return false;
            return Changed(barcodeInput, prefill.barcode) || Changed(brandInput, prefill.brand) || Changed(nameInput, prefill.name)
                || T(ingredientInput).Length > 0 || T(noteInput).Length > 0;
        }

        static bool Changed(TMP_InputField f, string from) => T(f) != (from ?? "").Trim();

        void Back()
        {
            if (leaving) return;
            if (!Entered()) { LeaveBack(); return; }
            if (confirmDialog == null) { LeaveBack(); return; }
            confirmDialog.Ask(confirmTitle, confirmMessage, confirmDiscard, LeaveBack, null, true, confirmKeep);
        }

        void LeaveBack()
        {
            if (leaving) return;
            leaving = true;
            StopCamera();
            if (!SceneNavigator.Back()) SceneNavigator.Go(SceneNames.AiSmartAnalysis);
        }

        void Leave(string scene)
        {
            if (leaving) return;
            leaving = true;
            StopCamera();
            SceneNavigator.Go(scene);
        }

        // ---------------------------------------------------------------------------------------
        //  Camera: live barcode, or one photo of the label (read on the phone, never sent)
        // ---------------------------------------------------------------------------------------
        void OpenCamera(CameraMode wanted)
        {
            if (cameraFeed == null || reading) return;
            Show(cameraOffCard, false);
            mode = wanted;
            Show(cameraPanel, true);
            if (shutterButton != null) shutterButton.gameObject.SetActive(wanted == CameraMode.Photo);
            Hint(wanted == CameraMode.Barcode ? msgBarcodeHint : msgPhotoHint);
            cameraFeed.StartCamera();
        }

        void OnCameraStarted(bool ok, string why)
        {
            if (ok) return;
            CloseCamera();
            Show(cameraOffCard, true);
            if (formScroll != null) formScroll.verticalNormalizedPosition = 1f;
        }

        void CloseCamera()
        {
            StopCamera();
            Show(cameraPanel, false);
        }

        void StopCamera()
        {
            mode = CameraMode.None;
            if (cameraFeed != null) cameraFeed.StopCamera();
        }

        async Task TryLiveBarcode()
        {
            Texture2D frame = cameraFeed.CaptureUpright(scanFrame, 1100);
            if (frame == null) return;
            busyFrame = true;
            string code;
            try { code = await ReadBarcode(frame); }
            finally { Destroy(frame); busyFrame = false; }
            if (this == null || mode != CameraMode.Barcode) return;
            code = (code ?? "").Trim();
            if (MlKitBarcodeReader.Key(code).Length == 0) return;
            Haptics.Tick(40);
            Set(barcodeInput, Digits(code));
            CloseCamera();
            Say(msgBarcodeRead);
        }

        async Task TakePhoto()
        {
            if (cameraFeed == null || !cameraFeed.IsPlaying || reading) return;
            Texture2D photo = cameraFeed.CaptureUpright(scanFrame, 1600);
            if (photo == null) return;
            CloseCamera();
            await ReadLabel(photo);
        }

        void FromGallery()
        {
            if (reading) return;
            GlowUp.Admin.GalleryPicker.Pick(tex => { if (tex != null) _ = ReadLabel(tex); }, problem => Say(problem));
        }

        async Task ReadLabel(Texture2D photo)
        {
            reading = true;
            Say(msgReading);
            string text = "", barcode = "";
            try
            {
                text = await ReadText(photo);
                if (T(barcodeInput).Length == 0) barcode = await ReadBarcode(photo);
            }
            finally { if (photo != null) Destroy(photo); reading = false; }
            if (this == null) return;
            if (MlKitBarcodeReader.Key(barcode).Length > 0) Set(barcodeInput, Digits(barcode));
            string list = IngredientPart(text);
            if (list.Length == 0) { Say(msgNothingRead); return; }
            Set(ingredientInput, list.Length > ingredientLimit ? list.Substring(0, ingredientLimit) : list);
            Say(msgReadDone);
        }

        Task<string> ReadBarcode(Texture2D picture)
        {
            var done = new TaskCompletionSource<string>();
            if (picture == null || (barcodeReader == null && !MlKitBarcodeReader.TryCreate(out barcodeReader))) { done.SetResult(""); return done.Task; }
            barcodeReader.Read(picture, code => done.TrySetResult(code ?? ""));
            return done.Task;
        }

        Task<string> ReadText(Texture2D picture)
        {
            var done = new TaskCompletionSource<string>();
            if (picture == null || (textReader == null && !MlKitTextReader.TryCreate(out textReader))) { done.SetResult(""); return done.Task; }
            textReader.Read(picture, (text, boxes, problem) => done.TrySetResult(text ?? ""));
            return done.Task;
        }

        /// <summary>The part of the label from "Ingredients" on, as one line (the whole text when that word is not there).</summary>
        public static string IngredientPart(string text)
        {
            string t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            int at = t.IndexOf("ingredient", StringComparison.OrdinalIgnoreCase);
            if (at < 0) at = t.IndexOf("ingrédient", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                int colon = t.IndexOf(':', at);
                t = colon > at && colon - at < 30 ? t.Substring(colon + 1) : t.Substring(at);
            }
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t.Trim(' ', ':', '.', ';');
        }

        static string Digits(string code)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in code ?? "") if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------
        //  Sending
        // ---------------------------------------------------------------------------------------
        async Task Send()
        {
            if (sending) return;
            string barcode = Digits(T(barcodeInput)), brand = Limit(T(brandInput), 60), name = Limit(T(nameInput), 100);
            string ingredients = Limit(T(ingredientInput), ingredientLimit), note = Limit(T(noteInput), noteLimit);
            if (barcode.Length == 0 && name.Length == 0) { Say(msgNeedOne); return; }
            if (barcode.Length > 0 && MlKitBarcodeReader.Key(barcode).Length == 0) { Say(msgBadBarcode); return; }

            FirebaseUser user = null;
            try { if (await FirebaseBootstrap.Ready) user = FirebaseAuth.DefaultInstance.CurrentUser; } catch (Exception) { }
            if (this == null) return;
            if (user == null) { Say(msgSignIn); return; }

            if (string.IsNullOrEmpty(docId)) docId = Guid.NewGuid().ToString("N");        // the same id on every retry: never two reports
            string title = (brand + " " + name).Trim();
            string message = note.Length >= 3 ? note : ("New product: " + (title.Length > 0 ? title : "barcode " + barcode));
            var report = new Dictionary<string, object>
            {
                { "uid", user.UserId },
                { "userName", WhoSent(user) },
                { "category", Category },
                { "message", Limit(message, 2000) },
                { "status", "new" },
                { "appVersion", Limit(Application.version ?? "", 40) },
                { "device", Limit($"{SystemInfo.deviceModel}, {SystemInfo.operatingSystem}".Trim(), 200) },
                { "createdAtUnix", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "createdAt", FieldValue.ServerTimestamp },
                { "barcode", barcode },
                { "brand", brand },
                { "productName", name },
                { "ingredientText", ingredients },
                { "note", note }
            };

            sending = true; waiting = true;
            bool offline = !ConnectivityMonitor.Online;
            Show(failedCard, false); Show(offlineCard, false);
            SetSendBusy(true);
            Task write;
            try { write = FirebaseFirestore.DefaultInstance.Collection(SupportReportsCollection).Document(docId).SetAsync(report); }
            catch (Exception) { Failed(); return; }

            if (offline)
            {
                // Firestore keeps the write on this phone and sends it when the connection is back
                sending = false; waiting = false; SetSendBusy(false);
                Sent(true);
                _ = WatchLate(write);
                return;
            }
            Show(sendingCard, true);
            if (sendingText != null) sendingText.text = msgSending;
            float until = Time.realtimeSinceStartup + Mathf.Max(3f, retryAfterSeconds);
            while (!write.IsCompleted && waiting && Time.realtimeSinceStartup < until) await Task.Delay(200);
            if (this == null) return;
            sending = false;
            if (!waiting) return;                     // the user tapped Cancel
            waiting = false;
            Show(sendingCard, false);
            SetSendBusy(false);
            if (!write.IsCompleted)
            {
                Show(failedCard, true);
                Say(msgStillSending);
                _ = WatchLate(write);
                return;
            }
            if (write.IsFaulted || write.IsCanceled) { Failed(); return; }
            Sent(false);
        }

        const string SupportReportsCollection = GlowUp.AppSettings.SupportFeedbackForm.Collection;

        async Task WatchLate(Task write)
        {
            try { await write; } catch (Exception) { return; }
            if (this == null) return;
            if (!sent) Sent(false);
            else if (sentText != null) sentText.text = msgSent;
        }

        void StopWaiting()
        {
            waiting = false; sending = false;
            Show(sendingCard, false);
            SetSendBusy(false);
            Show(failedCard, true);
            Say(msgStoppedWaiting);
        }

        void Failed()
        {
            sending = false; waiting = false;
            Show(sendingCard, false);
            SetSendBusy(false);
            Show(failedCard, true);
            Say("");
        }

        void Sent(bool offline)
        {
            sent = true;
            StopCamera();
            Show(cameraPanel, false); Show(formPanel, false); Show(sentPanel, true);
            Show(offlineCard, offline);
            if (sentText != null) sentText.text = offline ? msgSentOffline : msgSent;
        }

        void SetSendBusy(bool busy)
        {
            if (sendButton != null) sendButton.interactable = !busy;
            if (sendLabel != null) sendLabel.text = busy ? msgSending : "Confirm and send";
        }

        internal static string WhoSent(FirebaseUser user)
        {
            try
            {
                var p = GlowUp.Data.UserProfileRepository.Current;
                string name = p != null ? $"{p.firstName ?? ""} {p.lastName ?? ""}".Trim() : "";
                if (name.Length == 0) name = (user.DisplayName ?? "").Trim();
                if (name.Length == 0) name = (user.Email ?? "").Trim();
                return Limit(name.Length == 0 ? "GlowUp User" : name, 100);
            }
            catch (Exception) { return "GlowUp User"; }
        }

        // ---------------------------------------------------------------------------------------
        void Say(string words) { if (feedbackText != null) { feedbackText.text = words ?? ""; feedbackText.gameObject.SetActive(!string.IsNullOrEmpty(words)); } }
        void Hint(string words) { if (cameraHint != null) cameraHint.text = words ?? ""; }
        static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
        static void Set(TMP_InputField f, string value) { if (f != null) f.text = value ?? ""; }
        static string T(TMP_InputField f) => f != null ? (f.text ?? "").Trim() : "";
        static string Limit(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max).TrimEnd();
    }
}
