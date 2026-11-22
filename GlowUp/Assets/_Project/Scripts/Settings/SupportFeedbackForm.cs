using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Contact Support / Feedback Form (Firebase Firestore free tier).
    /// Sends reports directly to the "supportReports" collection in Firestore.
    /// Every submission outcome displays a centered modal popup (via CenteredFeedbackPopup)
    /// to ensure no code path ends silently.
    /// </summary>
    public class SupportFeedbackForm : MonoBehaviour
    {
        public const string Collection = "supportReports";

        [SerializeField] private OverlayPanel panel;
        [Tooltip("Tab 0, 1, 2, 3 = the categories of report, named in Category Names")]
        [SerializeField] private ChoiceTabs categoryTabs;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Toggle includeLastScanToggle;
        [SerializeField] private Button sendButton;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [Tooltip("The row with the support address; tapping it opens the mail app. When empty, a line is made at run time.")]
        [SerializeField] private Button supportEmailButton;

        [Header("Report Settings")]
        [SerializeField] private string[] categoryNames = { "Bug", "Wrong scan result", "Suggestion", "Other" };
        [SerializeField] private int minimumCharacters = 3;
        [SerializeField] private int maximumCharacters = 2000;
        [Tooltip("The scan's ingredient list is cut after this many characters")]
        [SerializeField] private int maximumScanCharacters = 1200;
        [Tooltip("Timeout in seconds before treating Firestore write as queued offline")]
        [SerializeField] private float sendTimeoutSeconds = 8f;
        [Tooltip("How long the success popup remains visible before auto-closing (seconds)")]
        [SerializeField] private float autoCloseSuccessSeconds = 2.5f;

        [Header("Outcome Messages")]
        [SerializeField] private string msgSuccess = "Report sent. Thank you for helping us improve GlowUp.";
        [Tooltip("The green toast shown low on the screen after a report is sent")]
        [SerializeField] private string msgSentToast = "Report Sent!";
        [SerializeField] private string msgSentTitle = "Report sent!";
        [SerializeField] private string msgSentDone = "Thank you. Your report went straight to the GlowUp team. Their reply will appear in your Notifications.";
        [SerializeField] private string msgValidation = "Please choose a category and describe what happened.";
        [SerializeField] private string msgFailure = "We couldn't send your report. Please try again.";
        [SerializeField] private string msgTimeout = "Your report is still being sent. It is saved on this phone and goes out by itself as soon as the connection allows. You can leave this screen: a message tells you when it has arrived.";
        [SerializeField] private string msgLateDelivered = "Your report has now reached the GlowUp team. Their reply will appear in your Notifications.";
        [SerializeField] private string msgSignIn = "Sign in to send a report, so the GlowUp team can reply to you.";
        [SerializeField] private string msgSending = "Sending...";
        [SerializeField] private string defaultSendButtonText = "Send Report";

        // State tracking
        private string currentReportDocId;
        private bool sending;
        private TextMeshProUGUI sendLabel;
        private CenteredFeedbackPopup popup;
        private string pendingPopupMessage;
        private string pendingPopupTitle;
        private Action pendingPopupAction;
        private float pendingPopupAutoClose;

        void Awake()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(() => _ = SendReportAsync());
                sendLabel = sendButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (panel != null)
            {
                panel.Closed += OnPanelClosed;
            }

            EnsureReportDocId();
            AddLimitAndEmail();
        }

        // ---- New22 (20 November 2026): a character limit with a counter, and the support address that opens Gmail ----
        /// <summary>The longest report a person can type. Long enough for steps to reproduce, short enough to read at a glance.</summary>
        public const int ReportLimit = 500;
        public const string SupportAddress = "GlowUpAdminSupport@gmail.com";

        /// <summary>Applies the native character limit and adds the e-mail line under the toggle.</summary>
        private void AddLimitAndEmail()
        {
            if (maximumCharacters <= 0 || maximumCharacters > ReportLimit) maximumCharacters = ReportLimit;
            if (messageInput == null) return;
            messageInput.characterLimit = maximumCharacters;
            if (supportEmailButton != null)
            {
                supportEmailButton.onClick.AddListener(() => OpenSupportEmail(SupportAddress, "GlowUp support"));
                return;
            }
            TextMeshProUGUI style = feedbackText != null ? feedbackText : sendLabel;
            if (style == null) return;

            // the toggle sits inside its own row: the line goes after that row, never inside it
            Transform after = includeLastScanToggle != null ? includeLastScanToggle.transform.parent : messageInput.transform;
            if (after == null || after == messageInput.transform.parent) after = messageInput.transform;
            TextMeshProUGUI email = MakeLine("SupportEmailLink", style, after, TextAlignmentOptions.Left);
            email.text = "Or email us: <u>" + SupportAddress + "</u>";
            email.raycastTarget = true;
            var layout = email.gameObject.GetComponent<LayoutElement>() ?? email.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(layout.minHeight, 144f);         // 144 units = 48 dp at the 1080-wide layout
            var button = email.gameObject.AddComponent<Button>();
            button.targetGraphic = email;
            button.onClick.AddListener(() => OpenSupportEmail(SupportAddress, "GlowUp support"));
        }

        private static TextMeshProUGUI MakeLine(string name, TextMeshProUGUI style, Transform after, TextAlignmentOptions align)
        {
            TextMeshProUGUI line = Instantiate(style, after.parent);
            line.name = name;
            line.gameObject.SetActive(true);
            line.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            line.alignment = align;
            line.fontSize = Mathf.Max(22f, style.fontSize * 0.85f);
            line.enableWordWrapping = true;
            line.raycastTarget = false;
            line.text = "";
            return line;
        }

        /// <summary>Opens Gmail's compose screen to the support address; any other mail app (or the chooser) when Gmail is missing.</summary>
        public static void OpenSupportEmail(string address, string subject)
        {
            string mailto = "mailto:" + address + "?subject=" + Uri.EscapeDataString(subject ?? "");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (AndroidJavaObject uri = uriClass.CallStatic<AndroidJavaObject>("parse", mailto))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SENDTO", uri))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    intent.Call<AndroidJavaObject>("setPackage", "com.google.android.gm");
                    activity.Call("startActivity", intent);
                    return;
                }
            }
            catch (Exception) { /* Gmail is not installed: fall back to the phone's own choice of mail app */ }
#endif
            Application.OpenURL(mailto);
        }

        void OnEnable()
        {
            EnsureReportDocId();
            CheckPendingPopup();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) CheckPendingPopup();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus) CheckPendingPopup();
        }

        /// <summary>
        /// Generates a unique document ID for this report session if one does not exist.
        /// Reusing this ID makes retries safe and avoids duplicate documents in Firestore.
        /// </summary>
        private void EnsureReportDocId()
        {
            if (string.IsNullOrEmpty(currentReportDocId))
            {
                currentReportDocId = Guid.NewGuid().ToString("N");
                { }
            }
        }

        private CenteredFeedbackPopup Popup
        {
            get
            {
                if (popup == null) popup = CenteredFeedbackPopup.Get(transform);
                return popup;
            }
        }

        private void ShowCenteredPopup(string title, string message, string okText = "OK", float autoCloseSeconds = 0f, Action onClosed = null)
        {
            if (!Application.isPlaying) return;

            // If app is not in foreground or popup cannot be shown immediately, store as pending
            if (!Application.isFocused)
            {
                pendingPopupTitle = title;
                pendingPopupMessage = message;
                pendingPopupAutoClose = autoCloseSeconds;
                pendingPopupAction = onClosed;
                return;
            }

            var p = Popup;
            if (p != null)
            {
                p.Show(title, message, okText, autoCloseSeconds, onClosed);
            }
            else
            {
                { }
            }
        }

        private void CheckPendingPopup()
        {
            if (!string.IsNullOrEmpty(pendingPopupMessage))
            {
                string t = pendingPopupTitle;
                string m = pendingPopupMessage;
                float ac = pendingPopupAutoClose;
                Action a = pendingPopupAction;

                pendingPopupTitle = null;
                pendingPopupMessage = null;
                pendingPopupAutoClose = 0f;
                pendingPopupAction = null;

                ShowCenteredPopup(t, m, "OK", ac, a);
            }
        }

        private void OnPanelClosed()
        {
            // If the user closed the panel during an active send, do not discard fields yet;
            // the centered popup is on the root canvas and will still notify them of the result.
            if (!sending && string.IsNullOrEmpty(currentReportDocId))
            {
                EnsureReportDocId();
            }
        }

        /// <summary>
        /// Main handler for sending the support report to Firebase Firestore.
        /// Enforces validation, single-ID idempotency, timeout detection, and centered UI feedback.
        /// </summary>
        public async Task SendReportAsync()
        {
            // Prevent double taps
            if (sending) return;

            string message = messageInput != null ? (messageInput.text ?? "").Trim() : "";
            int selectedCategoryIndex = categoryTabs != null ? categoryTabs.SelectedIndex : -1;

            // 1. VALIDATION: Check category and message
            bool categoryValid = selectedCategoryIndex >= 0 && categoryNames != null && selectedCategoryIndex < categoryNames.Length;
            bool descriptionValid = !string.IsNullOrEmpty(message) && message.Length >= Mathf.Max(1, minimumCharacters);

            if (!categoryValid || !descriptionValid)
            {
                { }
                ShowCenteredPopup("Incomplete Report", msgValidation, "OK");
                return; // Keep form open, keep existing input intact
            }

            if (message.Length > maximumCharacters)
            {
                message = message.Substring(0, maximumCharacters);
            }

            string category = categoryNames[selectedCategoryIndex];

            // 2. PREPARE SENDING STATE
            sending = true;
            if (sendButton != null) sendButton.interactable = false;
            if (sendLabel != null) sendLabel.text = msgSending;

            EnsureReportDocId();
            string docId = currentReportDocId;

            try
            {
                // Retrieve authenticated user info if available
                string uid = "anonymous";
                string userName = "GlowUp User";
                try
                {
                    if (await FirebaseBootstrap.Ready && FirebaseAuth.DefaultInstance != null && FirebaseAuth.DefaultInstance.CurrentUser != null)
                    {
                        var user = FirebaseAuth.DefaultInstance.CurrentUser;
                        uid = user.UserId;
                        userName = WhoSent(user);
                    }
                }
                catch (Exception)
                {
                    uid = "anonymous";
                }

                // the database only takes reports from a signed-in account (rule: uid == the sign-in's id)
                if (uid == "anonymous")
                {
                    ShowCenteredPopup("Sign in first", msgSignIn, "OK");
                    return;
                }

                // Build report data dictionary
                var reportData = new Dictionary<string, object>
                {
                    { "uid", uid },
                    { "userName", userName },
                    { "category", category },
                    { "message", message },
                    { "status", "new" },
                    { "appVersion", Application.version ?? "1.0" },
                    { "device", $"{SystemInfo.deviceModel}, {SystemInfo.operatingSystem}".Trim() },
                    { "createdAtUnix", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                    { "createdAt", FieldValue.ServerTimestamp }
                };

                // Requirement 7: Include last scan if requested; if attaching fails, still proceed
                try
                {
                    Dictionary<string, object> scanData = LastScan();
                    if (scanData != null)
                    {
                        reportData["lastScan"] = scanData;
                    }
                }
                catch (Exception scanEx)
                {
                    { }
                }

                { }

                // 3. EXECUTE FIRESTORE WRITE WITH 8-SECOND TIMEOUT WATCHDOG
                var firestore = FirebaseFirestore.DefaultInstance;
                var docRef = firestore.Collection(Collection).Document(docId);
                Task writeTask = docRef.SetAsync(reportData);

                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(sendTimeoutSeconds));
                Task completedTask = await Task.WhenAny(writeTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // TIMEOUT: Firestore queues offline writes and will sync when back online
                    { }
                    // The write is queued by Firestore and goes out when the phone is online again. The form is cleared and
                    // a new id is used for the next report, so a second tap cannot send the same report twice.
                    if (messageInput != null) messageInput.text = "";
                    currentReportDocId = null;
                    if (panel != null && panel.IsOpen) panel.Hide();
                    ShowCenteredPopup("Still processing", msgTimeout, "OK");
                    WatchLateDelivery(writeTask);
                }
                else
                {
                    // Await writeTask to capture any genuine exception (e.g., permission denied)
                    await writeTask;

                    { }

                    // SUCCESS OUTCOME (New20): the form closes and a green toast low on the screen says "Report Sent!".
                    // The centre popup stays as the fallback when the toast layer cannot be made.
                    void ClearForm()
                    {
                        if (messageInput != null) messageInput.text = "";
                        if (categoryTabs != null) categoryTabs.Select(0, false);
                        currentReportDocId = null; // Generate a fresh ID for the next report
                        if (panel != null && panel.IsOpen) panel.Hide();
                    }
                    // 22 Nov 2026 (owner): a centred "Report sent!" with a tick and Okay, the same as the website's "Reply sent!"
                    ClearForm();
                    if (Application.isFocused && Popup != null) Popup.ShowDone(msgSentTitle, msgSentDone, "Okay");
                    else ShowCenteredPopup(msgSentTitle, msgSentDone, "Okay", 0f);
                }
            }
            catch (Exception ex)
            {
                // FAILURE OUTCOME (permission-denied, server exception, etc.)
                { }
                string why = ex.Message ?? "";
                string reason = why.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0 ? "The server refused the report (permission denied)."
                    : why.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 || why.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ? "The server could not be reached."
                    : "Reason: " + (why.Length > 120 ? why.Substring(0, 120) : why);
                ShowCenteredPopup(
                    "Send Error",
                    msgFailure + " " + reason,
                    "OK"
                );
                // Keep the form open, KEEP currentReportDocId and user input intact for retry
            }
            finally
            {
                // Reset button state
                sending = false;
                if (sendButton != null) sendButton.interactable = true;
                if (sendLabel != null) sendLabel.text = defaultSendButtonText;
            }
        }

        /// <summary>A report that was still going out when the wait ended: when the database finally takes it, a toast says so.</summary>
        async void WatchLateDelivery(Task write)
        {
            try
            {
                await write;
                var live = FormalToast.OnScreen(transform, sendLabel);
                if (live != null) live.Show(FormalToast.Kind.Success, msgLateDelivered);
            }
            catch (Exception) { /* refused or cancelled: the queue keeps it, nothing more to say here */ }
        }

        /// <summary>
        /// Gathers display name from UserProfileRepository or Firebase User.
        /// </summary>
        private static string WhoSent(FirebaseUser user)
        {
            try
            {
                UserProfileData profile = UserProfileRepository.Current;
                string name = profile != null ? ($"{profile.firstName ?? ""} {profile.lastName ?? ""}".Trim()) : "";
                if (string.IsNullOrEmpty(name) && user != null) name = (user.DisplayName ?? "").Trim();
                if (string.IsNullOrEmpty(name) && user != null) name = (user.Email ?? "").Trim();
                return string.IsNullOrEmpty(name) ? "GlowUp User" : (name.Length > 100 ? name.Substring(0, 100) : name);
            }
            catch
            {
                return "GlowUp User";
            }
        }

        /// <summary>
        /// Reads the user's most recent scan record if "Include my last scan" toggle is active.
        /// </summary>
        private Dictionary<string, object> LastScan()
        {
            if (includeLastScanToggle == null || !includeLastScanToggle.isOn) return null;

            ScanRecord scan = ScanSession.Current;
            if (scan == null)
            {
                var saved = ScanHistoryRepository.Active();
                if (saved != null && saved.Count > 0) scan = saved[0];
            }

            if (scan == null) return null;

            string ingredients = scan.ingredients != null ? string.Join(", ", scan.ingredients) : "";
            if (ingredients.Length > maximumScanCharacters)
            {
                ingredients = ingredients.Substring(0, Mathf.Max(0, maximumScanCharacters)) + "...";
            }

            return new Dictionary<string, object>
            {
                { "product", scan.Title ?? "" },
                { "brand", scan.brandName ?? "" },
                { "result", scan.status ?? "" },
                { "summary", scan.summary ?? "" },
                { "ingredients", ingredients }
            };
        }
    }
}
