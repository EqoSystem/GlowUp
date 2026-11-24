using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Performance;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// The staff capture dashboard (AdminDashboardScene): deliberately small. Scan Barcode, Capture Product and Upload from
    /// Gallery first, Manual Entry as a fallback, a live status line, the latest submissions and one link to the website for
    /// everything else (brands, ingredients, inventory, approval stay on the website). Access is checked on the server each
    /// time the screen opens; a session that ended or lost its access goes back to the staff sign-in.
    /// </summary>
    public class AdminDashboardController : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private Button settingsButton;
        [SerializeField] private ConfirmDialog confirmDialog;

        [Header("Scanner preview")]
        [SerializeField] private Button previewButton;
        [SerializeField] private Button flashChip;
        [SerializeField] private RectTransform previewRect, belowRect;   // the camera view, and everything under it (moves down when the view grows)
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private CameraFeedController cameraFeed;        // the live camera view (same component as the AI Smart Scan)
        [SerializeField] private RectTransform guideFrame;

        [Header("Capture")]
        [SerializeField] private Button scanButton;
        [SerializeField] private Button captureButton;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button manualButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private TextMeshProUGUI resumeText;
        [SerializeField] private Button batchButton;
        [Tooltip("Import file: a Word, PDF, Excel, JSON, CSV or TXT list of products with ingredients becomes the batch")]
        [SerializeField] private Button importButton;
        [SerializeField] private TextMeshProUGUI batchText;
        [SerializeField] private Button tipsButton;
        [SerializeField] private OverlayPanel tipsPanel;

        [Header("Live status bar")]
        [SerializeField] private Button statusBar;
        [SerializeField] private TextMeshProUGUI onlineText;
        [SerializeField] private Image onlineDot;
        [SerializeField] private TextMeshProUGUI totalScansText;
        [SerializeField] private TextMeshProUGUI syncText;
        [SerializeField] private Color syncedColor = new Color(0.20f, 0.62f, 0.42f);
        [SerializeField] private Color busyColor = new Color(0.85f, 0.60f, 0.15f);
        [SerializeField] private Color offlineColor = new Color(0.62f, 0.55f, 0.56f);

        [Header("Recent submissions")]
        [SerializeField] private RectTransform recentContent;
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private TextMeshProUGUI emptyText;
        [SerializeField] private Button viewAllButton;
        [SerializeField] private int recentRows = 3;

        [Header("Scenes")]
        [SerializeField] private string loginScene = "AdminLoginScene";
        [SerializeField] private string captureScene = "AdminCaptureScene";
        [SerializeField] private string submissionsScene = "AdminSubmissionsScene";
        [SerializeField] private string settingsScene = "AdminSettingsScene";

        readonly List<GameObject> rows = new List<GameObject>();

        async void Start()
        {
            if (rowTemplate != null) rowTemplate.SetActive(false);
            // The camera view has two shapes: short and wide for a barcode (Scan), tall for a photo of the pack (Camera). A button for the
            // shape that is not showing switches to it; the button of the shape that is showing starts that capture. Tapping the view starts it too.
            Hook(scanButton, () => OpenCapture(CaptureDraft.Mode.Barcode));          // 24 Nov 2026: one tap opens the barcode scanner (the dashboard has no camera view of its own)
            Hook(captureButton, () => OpenCapture(CaptureDraft.Mode.Photo));
            Hook(galleryButton, () => OpenCapture(CaptureDraft.Mode.Gallery));
            Hook(manualButton, () => OpenCapture(CaptureDraft.Mode.Manual));
            Hook(resumeButton, () => { CaptureDraft d = CaptureDraft.Current; AdminCaptureController.StartMode = d.ModeValue; AdminCaptureController.Resume = true; SceneNavigator.Go(captureScene); });
            Hook(batchButton, () => { AdminCaptureController.OpenBatch = true; SceneNavigator.Go(captureScene); });
            Hook(importButton, ImportFile);
            BatchQueue.Changed += Paint;
            Hook(viewAllButton, OpenSubmissions);
            Hook(statusBar, OpenSubmissions);
            Hook(previewButton, () => OpenCapture(photoView ? CaptureDraft.Mode.Photo : CaptureDraft.Mode.Barcode));
            Hook(settingsButton, () => SceneNavigator.Go(settingsScene));
            Hook(tipsButton, () => { if (tipsPanel != null) tipsPanel.Show(); });
            Hook(flashChip, () => { ScannerPrefs.Flash = !ScannerPrefs.Flash; });
            SubmissionStore.Changed += Paint;
            WebsitePresence.Changed += Paint;
            ScannerPrefs.Changed += Paint;
            ConnectivityMonitor.ConnectivityChanged += OnConnection;
            Paint();

            StaffAccess.Result r = await StaffAccess.Verify();
            if (this == null) return;
            if (r == StaffAccess.Result.NotSignedIn || r == StaffAccess.Result.NotStaff || r == StaffAccess.Result.Disabled)
            {
                StaffAccess.SignOut();
                FormalToast.ShowAfterNextScene(FormalToast.Kind.Warning, StaffAccess.Message(r));
                SceneNavigator.Go(loginScene);
                return;
            }
            StaffAccess.RememberStaffSession();
            SubmissionStore.Start();
            WebsitePresence.Start();
            Paint();
            // the catalogue on the phone: duplicate warnings and brand matching while capturing (no admin data here)
            try { await BrandRepository.LoadActive(true); await ProductRepository.LoadActive(true); } catch (System.Exception) { }
        }

        bool importing;

        /// <summary>Import file: pick a file, add its products to the batch as "Needs review", then show the batch to check and send.</summary>
        void ImportFile()
        {
            if (importing) return;
            importing = true;
            StaffToast.Info(this, "Choose a file with your product list.");
            ProductFileImport.Pick((added, message) =>
            {
                importing = false;
                if (this == null) return;
                if (!string.IsNullOrEmpty(message)) { if (added > 0) StaffToast.Ok(this, message); else StaffToast.Warn(this, message); }
                if (added > 0) { AdminCaptureController.OpenBatch = true; SceneNavigator.Go(captureScene); }
            });
        }

        void OnDestroy() { BatchQueue.Changed -= Paint; SubmissionStore.Changed -= Paint; WebsitePresence.Changed -= Paint; ScannerPrefs.Changed -= Paint; ConnectivityMonitor.ConnectivityChanged -= OnConnection; }

        void OnConnection(bool online) { Paint(); }

        static void OpenSubmissions() { AdminSubmissionsController.OpenId = ""; SceneNavigator.Go("AdminSubmissionsScene"); }

        float nextPresence;

        const float PhotoExtra = 200f, GrowSeconds = 0.22f;
        bool photoView;
        float grown;                       // 0 = barcode shape, PhotoExtra = photo shape
        float baseHeight = -1f;

        void SetPhotoView(bool photo)
        {
            photoView = photo;
            if (hintText != null) hintText.text = photo ? "Show the front: the brand and the product's name" : "Align the barcode, or take a photo";
        }

        void GrowView()
        {
            if (previewRect == null) return;
            if (baseHeight < 0f) baseHeight = previewRect.sizeDelta.y;
            float target = photoView ? PhotoExtra : 0f;
            if (Mathf.Approximately(grown, target)) return;
            grown = PerformanceSettings.ReduceEffects ? target : Mathf.MoveTowards(grown, target, PhotoExtra / GrowSeconds * Time.unscaledDeltaTime);
            previewRect.sizeDelta = new Vector2(previewRect.sizeDelta.x, baseHeight + grown);
            if (belowRect != null) belowRect.offsetMax = new Vector2(belowRect.offsetMax.x, -grown);
        }

        void Update()
        {
            GrowView();
            if (Time.unscaledTime >= nextPresence) { nextPresence = Time.unscaledTime + 20f; WebsitePresence.Recheck(); }   // a closed site sends no snapshot: re-check the age
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (tipsPanel != null && tipsPanel.IsOpen) { tipsPanel.Hide(); return; }                  // Back closes the tips first
            if (confirmDialog != null && !confirmDialog.IsOpen) AskSignOut();                          // then Back = leave staff mode (asked first)
        }

        static void Hook(Button b, UnityEngine.Events.UnityAction a) { if (b != null) b.onClick.AddListener(a); }

        void OpenCapture(CaptureDraft.Mode mode)
        {
            CaptureDraft d = CaptureDraft.Current;
            AdminCaptureController.StartMode = mode;
            AdminCaptureController.Resume = d.HasContent && string.IsNullOrEmpty(d.submittedId) && d.ModeValue == mode;
            SceneNavigator.Go(captureScene);
        }

        void Paint()
        {
            if (this == null) return;
            CaptureDraft d = CaptureDraft.Current;
            bool unsent = d.HasContent && string.IsNullOrEmpty(d.submittedId);
            if (resumeButton != null) resumeButton.gameObject.SetActive(unsent);
            if (resumeText != null) resumeText.text = unsent ? $"Unsent: {(d.Get("name").Has ? d.Get("name").value : "no name yet")}" : "";

            int waiting = BatchQueue.Count, readyNow = BatchQueue.ReadyCount;
            if (batchButton != null) batchButton.gameObject.SetActive(waiting > 0);
            if (batchText != null) batchText.text = waiting > 0 ? $"Batch: {waiting} waiting ({readyNow} ready)" : "";

            // live status bar: what the listener and the connection report, never assumed
            // "Online" = this phone has a connection AND at least one admin has the website open (see WebsitePresence)
            bool online = ConnectivityMonitor.Online && WebsitePresence.AnyOpen;
            string sync = ConnectivityMonitor.Online ? SubmissionStore.SyncState : "Offline";
            int attention = SubmissionStore.NeedsAttention();
            string syncWord = !ConnectivityMonitor.Online ? "Offline" : attention > 0 ? "Needs attention" : sync;
            if (onlineText != null) onlineText.text = online ? "Online" : "Offline";
            if (onlineDot != null) onlineDot.color = online ? syncedColor : offlineColor;
            if (totalScansText != null) totalScansText.text = "Total Scans: " + (SubmissionStore.Loaded ? SubmissionStore.Items.Count.ToString() : "-");
            if (syncText != null) { syncText.text = syncWord; syncText.color = syncWord == "Synced" ? syncedColor : syncWord == "Offline" ? offlineColor : busyColor; }
            if (flashChip != null && flashChip.targetGraphic != null) flashChip.targetGraphic.color = ScannerPrefs.Flash ? new Color(0.95f, 0.36f, 0.58f, 0.9f) : new Color(0, 0, 0, 0.5f);   // pink = light will be on when scanning
            if (guideFrame != null) guideFrame.gameObject.SetActive(ScannerPrefs.GuideFrame);

            PaintRecent();
        }

        void PaintRecent()
        {
            if (recentContent == null || rowTemplate == null) return;
            foreach (GameObject r in rows) if (r != null) Destroy(r);
            rows.Clear();
            int n = Mathf.Min(recentRows, SubmissionStore.Items.Count);
            for (int i = 0; i < n; i++)
            {
                SubmissionStore.Info info = SubmissionStore.Items[i];
                GameObject row = Instantiate(rowTemplate, recentContent);
                row.name = "Row_" + info.id;
                row.SetActive(true);
                SubmissionRows.Fill(row, info);
                string id = info.id;
                Button b = row.GetComponent<Button>();
                if (b != null) b.onClick.AddListener(() => { AdminSubmissionsController.OpenId = id; SceneNavigator.Go(submissionsScene); });
                rows.Add(row);
            }
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(n == 0);
                emptyText.text = SubmissionStore.Loaded ? "No submissions yet." : "Loading your submissions...";
            }
        }

        void AskSignOut()
        {
            void Out() { StaffAccess.SignOut(); SceneNavigator.Go("LogInScene"); }
            if (confirmDialog != null) confirmDialog.Ask("Sign out of staff access?", "Your unsent capture stays on this phone for this account.", "Sign out", Out, true, "Cancel");
            else Out();
        }
    }
}
