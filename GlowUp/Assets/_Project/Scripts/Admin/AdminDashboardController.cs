using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
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
        [SerializeField] private TextMeshProUGUI welcomeText;
        [SerializeField] private TextMeshProUGUI accountText;
        [SerializeField] private Button signOutButton;
        [SerializeField] private ConfirmDialog confirmDialog;

        [Header("Capture")]
        [SerializeField] private Button scanButton;
        [SerializeField] private Button captureButton;
        [SerializeField] private Button galleryButton;
        [SerializeField] private Button manualButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private TextMeshProUGUI resumeText;

        [Header("Live status")]
        [SerializeField] private TextMeshProUGUI statusTitle;
        [SerializeField] private TextMeshProUGUI statusDetail;
        [SerializeField] private Image statusDot;
        [SerializeField] private Color syncedColor = new Color(0.20f, 0.62f, 0.42f);
        [SerializeField] private Color busyColor = new Color(0.85f, 0.60f, 0.15f);
        [SerializeField] private Color offlineColor = new Color(0.62f, 0.55f, 0.56f);

        [Header("Recent submissions")]
        [SerializeField] private RectTransform recentContent;
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private TextMeshProUGUI emptyText;
        [SerializeField] private Button viewAllButton;
        [SerializeField] private int recentRows = 3;

        [Header("Bottom bar and website")]
        [SerializeField] private Button websiteButton;
        [SerializeField] private Button navSubmissions;

        [Header("Scenes")]
        [SerializeField] private string loginScene = "AdminLoginScene";
        [SerializeField] private string captureScene = "AdminCaptureScene";
        [SerializeField] private string submissionsScene = "AdminSubmissionsScene";

        readonly List<GameObject> rows = new List<GameObject>();

        async void Start()
        {
            if (rowTemplate != null) rowTemplate.SetActive(false);
            Hook(scanButton, () => OpenCapture(CaptureDraft.Mode.Barcode));
            Hook(captureButton, () => OpenCapture(CaptureDraft.Mode.Photo));
            Hook(galleryButton, () => OpenCapture(CaptureDraft.Mode.Gallery));
            Hook(manualButton, () => OpenCapture(CaptureDraft.Mode.Manual));
            Hook(resumeButton, () => { CaptureDraft d = CaptureDraft.Current; AdminCaptureController.StartMode = d.ModeValue; AdminCaptureController.Resume = true; SceneNavigator.Go(captureScene); });
            Hook(viewAllButton, () => { AdminSubmissionsController.OpenId = ""; SceneNavigator.Go(submissionsScene); });
            Hook(navSubmissions, () => { AdminSubmissionsController.OpenId = ""; SceneNavigator.Go(submissionsScene); });
            Hook(websiteButton, OpenWebsite);
            Hook(signOutButton, AskSignOut);
            SubmissionStore.Changed += Paint;
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
            SubmissionStore.Start();
            Paint();
            // the catalogue on the phone: duplicate warnings and brand matching while capturing (no admin data here)
            try { await BrandRepository.LoadActive(true); await ProductRepository.LoadActive(true); } catch (System.Exception) { }
        }

        void OnDestroy() { SubmissionStore.Changed -= Paint; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && confirmDialog != null && !confirmDialog.IsOpen) AskSignOut();   // Back = leave staff mode (asked first)
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
            string email = StaffAccess.Email;
            if (welcomeText != null) welcomeText.text = "Welcome, Staff!";
            if (accountText != null) accountText.text = string.IsNullOrEmpty(email) ? "Staff account" : email + " · Admin";

            CaptureDraft d = CaptureDraft.Current;
            bool unsent = d.HasContent && string.IsNullOrEmpty(d.submittedId);
            if (resumeButton != null) resumeButton.gameObject.SetActive(unsent);
            if (resumeText != null) resumeText.text = unsent ? $"Unsent capture: {(d.Get("name").Has ? d.Get("name").value : "no name yet")}. Tap to continue." : "";

            // live status: what the listener reports, never assumed
            string sync = ConnectivityMonitor.Online ? SubmissionStore.SyncState : "Offline";
            int attention = SubmissionStore.NeedsAttention(), pending = SubmissionStore.Count("Pending");
            string title = !ConnectivityMonitor.Online ? "Offline" : attention > 0 ? "Needs attention" : sync == "Synced" ? "Synced" : sync;
            if (statusTitle != null) statusTitle.text = title;
            if (statusDetail != null)
                statusDetail.text = !SubmissionStore.Loaded && sync != "Offline" ? "Connecting to the website..."
                    : attention > 0 ? $"{attention} submission{(attention == 1 ? " was" : "s were")} sent back with notes. {pending} waiting for review."
                    : sync == "Offline" ? "Captures are kept on this phone and sent when you are back online."
                    : $"Ready. {pending} waiting for review on the website. Updates here by themselves.";
            if (statusDot != null) statusDot.color = title == "Synced" ? syncedColor : title == "Offline" ? offlineColor : busyColor;

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
                emptyText.text = SubmissionStore.Loaded ? "No submissions yet. Scan or photograph a product to start." : "Loading your submissions...";
            }
        }

        void OpenWebsite()
        {
            const string message = "This opens the GlowUp admin website in your browser, for brands, ingredients, inventory and approvals. Your unsent capture stays on this phone.";
            if (confirmDialog != null) confirmDialog.Ask("Open the admin website?", message, "Open website", () => Application.OpenURL(StaffAccess.WebsiteHome), false, "Stay here");
            else Application.OpenURL(StaffAccess.WebsiteHome);
        }

        void AskSignOut()
        {
            void Out() { StaffAccess.SignOut(); SceneNavigator.Go("LogInScene"); }
            if (confirmDialog != null) confirmDialog.Ask("Sign out of staff access?", "Your unsent capture stays on this phone for this account.", "Sign out", Out, true, "Cancel");
            else Out();
        }
    }
}
