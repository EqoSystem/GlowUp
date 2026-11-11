using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Net;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Contact support (10 November 2026): the report goes STRAIGHT to the GlowUp team. It is written to the Firestore collection
    /// "supportReports", which the admin website's Dashboard lists (panel "Support reports"). No e-mail app, nothing copied.
    ///
    /// A report = the kind (Bug / Wrong scan result / Suggestion / Other; Suggestion added 11 November 2026, phase 4: the website's Dashboard
    /// lists those in its "Suggestions" panel too), the message (3 to 2000 characters, any words, any language), the account id,
    /// the app version and phone model, and - only while "Include my last scan" is on - the last scan's product, result and ingredients.
    /// The security rules let a signed-in user CREATE their own report only (not read, change or delete any).
    /// Sent: a toast "Report sent. Thank you.", the form is emptied and closes. Not sent: the text stays and a toast offers Retry.
    /// </summary>
    public class SupportFeedbackForm : MonoBehaviour
    {
        public const string Collection = "supportReports";

        [SerializeField] private OverlayPanel panel;
        [Tooltip("Tab 0, 1, 2, 3 = the four kinds of report, named in Category Names")]
        [SerializeField] private ChoiceTabs categoryTabs;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Toggle includeLastScanToggle;
        [SerializeField] private Button sendButton;
        [Tooltip("Old message line of the form. Not used for messages any more (they are toasts); its place shows the tips.")]
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Old e-mail way (no longer used; kept so a saved scene loads)")]
        [SerializeField] private string supportEmail = "";
        [SerializeField] private string subjectFormat = "GlowUp feedback: {0}";

        [Header("Report")]
        [SerializeField] private string[] categoryNames = { "Bug", "Wrong scan result", "Suggestion", "Other" };
        [SerializeField] private int minimumCharacters = 3;
        [SerializeField] private int maximumCharacters = 2000;
        [Tooltip("The scan's ingredient list is cut after this many characters")]
        [SerializeField] private int maximumScanCharacters = 1200;
        [SerializeField] private float sendWaitSeconds = 15f;

        [Header("Wording")]
        [SerializeField] private string introWords = "Tell us what went wrong. Your report goes straight to the GlowUp team.";
        [SerializeField] private string msgTooShort = "Write at least a few words.";
        [SerializeField] private string msgSent = "Report sent. Thank you!";
        [SerializeField] private string msgNotSent = "Your report couldn't be sent. Check your connection and try again.";
        [SerializeField] private string msgSignIn = "Sign in with an internet connection to send a report.";
        [SerializeField] private string msgSending = "Sending…";
        [SerializeField] private string tipsTitle = "Tips";

        bool sending;
        FormalToast toast;
        TextMeshProUGUI tips;
        TextMeshProUGUI sendLabel;

        void Awake()
        {
            if (sendButton != null) { sendButton.onClick.AddListener(() => _ = Send()); sendLabel = sendButton.GetComponentInChildren<TextMeshProUGUI>(true); }
            if (minimumCharacters < 1 || minimumCharacters > 3) minimumCharacters = 3;      // a scene saved earlier asked for 10: any short message is fine now
            if (maximumCharacters < 3) maximumCharacters = 2000;
            Retell();
            if (categoryTabs != null) categoryTabs.Changed += _ => PaintTips();
        }

        void Start() { PaintTips(); }

        /// <summary>The words and parts of a scene built for the old e-mail way, corrected without rebuilding it.</summary>
        void Retell()
        {
            Transform box = panel != null ? panel.transform : transform;
            foreach (TextMeshProUGUI t in box.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t != null && t.text != null && t.text.IndexOf("email app", StringComparison.OrdinalIgnoreCase) >= 0) t.text = introWords;
            if (sendLabel != null) sendLabel.text = "Send Report";
            // the old message line becomes the tips list, in the same place (under the box and the switch, above Send)
            tips = feedbackText;
            if (tips != null)
            {
                tips.enableAutoSizing = true; tips.fontSizeMax = Mathf.Max(24f, tips.fontSize); tips.fontSizeMin = 20f;
                tips.overflowMode = TextOverflowModes.Ellipsis;
                tips.richText = true;
            }
        }

        void PaintTips()
        {
            if (tips == null) return;
            string[] list = SupportSuggestions.For(categoryTabs != null ? categoryTabs.SelectedIndex : 0);
            if (list.Length == 0) { tips.text = ""; tips.gameObject.SetActive(false); return; }
            tips.text = "<b>" + tipsTitle + "</b>\n• " + string.Join("\n• ", list);
            tips.gameObject.SetActive(true);
        }

        FormalToast Toast()
        {
            if (toast == null) toast = FormalToast.OnScreen(transform, sendLabel != null ? sendLabel : tips);
            return toast;
        }

        async Task Send()
        {
            if (sending) return;
            string message = messageInput != null ? (messageInput.text ?? "").Trim() : "";
            if (message.Length < minimumCharacters) { Toast()?.Show(FormalToast.Kind.Error, msgTooShort); return; }
            if (message.Length > maximumCharacters) message = message.Substring(0, maximumCharacters);

            FirebaseUser user = null;
            try { if (await FirebaseBootstrap.Ready) user = FirebaseAuth.DefaultInstance.CurrentUser; }
            catch (Exception e) { Debug.LogWarning("Support: sign-in state unknown: " + e.Message); }
            if (this == null) return;
            if (user == null || !ConnectivityMonitor.Online) { Toast()?.Show(FormalToast.Kind.Error, msgSignIn, "Retry", () => _ = Send()); return; }

            int index = categoryTabs != null ? categoryTabs.SelectedIndex : 0;
            string category = categoryNames != null && index >= 0 && index < categoryNames.Length ? categoryNames[index] : "Other";
            var report = new Dictionary<string, object>
            {
                { "uid", user.UserId },
                { "category", category },
                { "message", message },
                { "status", "new" },
                { "appVersion", Application.version ?? "" },
                { "device", (SystemInfo.deviceModel + ", " + SystemInfo.operatingSystem).Trim() },
                { "createdAtUnix", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "createdAt", FieldValue.ServerTimestamp }
            };
            Dictionary<string, object> scan = LastScan();
            if (scan != null) report["lastScan"] = scan;

            sending = true;
            if (sendButton != null) sendButton.interactable = false;
            if (sendLabel != null) sendLabel.text = msgSending;
            try
            {
                await FirebaseFirestore.DefaultInstance.Collection(Collection).Document().SetAsync(report).OrTimeout(sendWaitSeconds, "Sending the report");
                if (this == null) return;
                if (messageInput != null) messageInput.text = "";
                if (categoryTabs != null) categoryTabs.Select(0, true);             // the form is reset for the next report
                Toast()?.Show(FormalToast.Kind.Success, msgSent);
                if (panel != null && panel.IsOpen) panel.Hide();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Support: the report was not sent: " + e.Message);
                if (this == null) return;
                Toast()?.Show(FormalToast.Kind.Error, msgNotSent, "Retry", () => _ = Send());     // the message stays in the box
            }
            finally
            {
                if (this != null)
                {
                    sending = false;
                    if (sendButton != null) sendButton.interactable = true;
                    if (sendLabel != null) sendLabel.text = "Send Report";
                }
            }
        }

        /// <summary>The last scan, only while "Include my last scan" is on; null otherwise.</summary>
        Dictionary<string, object> LastScan()
        {
            if (includeLastScanToggle == null || !includeLastScanToggle.isOn) return null;
            ScanRecord scan = ScanSession.Current;
            if (scan == null) { var saved = ScanHistoryRepository.Active(); if (saved.Count > 0) scan = saved[0]; }
            if (scan == null) return null;
            string ingredients = scan.ingredients != null ? string.Join(", ", scan.ingredients) : "";
            if (ingredients.Length > maximumScanCharacters) ingredients = ingredients.Substring(0, Mathf.Max(0, maximumScanCharacters)) + "...";
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
