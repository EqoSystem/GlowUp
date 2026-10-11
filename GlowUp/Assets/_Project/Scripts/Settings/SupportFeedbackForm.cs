using System;
using System.Text;
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
    /// Contact Support / Feedback: a short form (what it is about, a message, and optionally the last
    /// scan) that opens the phone's email app with the report already written, addressed to the support
    /// email typed here in the Inspector. The report is also copied, so it can be pasted anywhere if the
    /// phone has no email app.
    ///
    /// SET "Support Email" in the Inspector. While it is empty the form can only copy the report.
    /// </summary>
    public class SupportFeedbackForm : MonoBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [Tooltip("Tab 0, 1, 2 = the three kinds of report, named in Category Names")]
        [SerializeField] private ChoiceTabs categoryTabs;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Toggle includeLastScanToggle;
        [SerializeField] private Button sendButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Where reports go")]
        [Tooltip("The address reports are sent to, e.g. support@yourdomain.com")]
        [SerializeField] private string supportEmail = "";
        [Tooltip("{0} = the kind of report")]
        [SerializeField] private string subjectFormat = "GlowUp feedback: {0}";
        [SerializeField] private string[] categoryNames = { "Bug", "Wrong scan result", "Other" };
        [Tooltip("The shortest message that is accepted")]
        [SerializeField] private int minimumCharacters = 10;
        [Tooltip("The scan's ingredient list is cut after this many characters")]
        [SerializeField] private int maximumScanCharacters = 1200;

        [Header("Wording")]
        [SerializeField] private string msgTooShort = "Please describe the problem in a sentence or two.";
        [SerializeField] private string msgOpened = "Your email app is opening with the report. It was also copied, in case the email app does not open.";
        [SerializeField] private string msgCopiedOnly = "The report was copied. Paste it into a message to the GlowUp team.";

        void Awake()
        {
            if (sendButton != null) sendButton.onClick.AddListener(Send);
            if (panel != null) panel.Closed += () => Say("");          // the next time the form opens it starts clean
        }

        void Start() { Say(""); }

        void Send()
        {
            string message = messageInput != null ? (messageInput.text ?? "").Trim() : "";
            if (message.Length < Mathf.Max(1, minimumCharacters)) { Say(msgTooShort); return; }

            int index = categoryTabs != null ? categoryTabs.SelectedIndex : 0;
            string category = categoryNames != null && index >= 0 && index < categoryNames.Length ? categoryNames[index] : "Feedback";
            string subject = string.Format(subjectFormat, category);
            string body = Compose(category, message);

            GUIUtility.systemCopyBuffer = subject + "\n\n" + body;
            if (string.IsNullOrWhiteSpace(supportEmail)) { Say(msgCopiedOnly); return; }

            Application.OpenURL("mailto:" + supportEmail.Trim() + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body));
            Say(msgOpened);
            if (messageInput != null) messageInput.text = "";
        }

        string Compose(string category, string message)
        {
            var body = new StringBuilder();
            body.Append(message).Append("\n\n----\n");
            body.Append("About: ").Append(category).Append('\n');
            body.Append("App version: ").Append(Application.version).Append('\n');
            body.Append("Phone: ").Append(SystemInfo.deviceModel).Append(", ").Append(SystemInfo.operatingSystem).Append('\n');
            string uid = UserProfileRepository.ActiveUid;
            if (!string.IsNullOrEmpty(uid)) body.Append("Account ID: ").Append(uid).Append('\n');

            if (includeLastScanToggle != null && includeLastScanToggle.isOn)
            {
                ScanRecord scan = ScanSession.Current;
                if (scan == null) { var saved = ScanHistoryRepository.Active(); if (saved.Count > 0) scan = saved[0]; }
                if (scan != null)
                {
                    body.Append("\nLast scan: ").Append(scan.Title);
                    if (!string.IsNullOrWhiteSpace(scan.brandName)) body.Append(" (").Append(scan.brandName).Append(')');
                    body.Append("\nResult shown: ").Append(scan.status).Append(". ").Append(scan.summary).Append('\n');
                    string ingredients = scan.ingredients != null ? string.Join(", ", scan.ingredients) : "";
                    if (ingredients.Length > maximumScanCharacters) ingredients = ingredients.Substring(0, Mathf.Max(0, maximumScanCharacters)) + "...";
                    body.Append("Ingredients read: ").Append(ingredients).Append('\n');
                }
            }
            return body.ToString();
        }

        void Say(string message) { if (feedbackText != null) feedbackText.text = message; }
    }
}
