using System.Collections.Generic;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// The staff member's own submissions (AdminSubmissionsScene), live: All / Pending / Approved / Rejected, and a detail
    /// pop-up with what the website decided (status, reviewer's note). The list changes by itself when a reviewer acts on the
    /// website (Firestore listener in SubmissionStore). Editing and approving stay on the website ("Open on the website").
    /// </summary>
    public class AdminSubmissionsController : MonoBehaviour
    {
        public static string OpenId = "";

        [SerializeField] private Button backButton;
        [SerializeField] private Button[] tabButtons;             // All, Pending, Approved, Rejected
        [SerializeField] private TextMeshProUGUI syncText;
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private TextMeshProUGUI emptyText;

        [Header("Detail pop-up")]
        [SerializeField] private OverlayPanel detailPanel;
        [SerializeField] private TextMeshProUGUI detailName;
        [SerializeField] private TextMeshProUGUI detailBrand;
        [SerializeField] private TextMeshProUGUI detailStatus;
        [SerializeField] private TextMeshProUGUI detailInfo;
        [SerializeField] private TextMeshProUGUI detailNote;
        [SerializeField] private Button openWebsiteButton;
        [SerializeField] private string dashboardScene = "AdminDashboardScene";

        static readonly string[] Tabs = { "All", "Pending", "Approved", "Rejected" };
        string tab = "All", shownId = "";
        readonly List<GameObject> rows = new List<GameObject>();

        void Start()
        {
            if (rowTemplate != null) rowTemplate.SetActive(false);
            if (backButton != null) backButton.onClick.AddListener(Back);
            for (int i = 0; tabButtons != null && i < tabButtons.Length && i < Tabs.Length; i++)
            { string t = Tabs[i]; if (tabButtons[i] != null) tabButtons[i].onClick.AddListener(() => { tab = t; Paint(); }); }
            if (openWebsiteButton != null) openWebsiteButton.onClick.AddListener(OpenOnWebsite);
            if (StaffAccess.User == null) { SceneNavigator.Go("AdminLoginScene"); return; }
            SubmissionStore.Changed += Paint;
            SubmissionStore.Start();
            Paint();
        }

        void OnDestroy() { SubmissionStore.Changed -= Paint; }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;
            if (detailPanel != null && detailPanel.IsOpen) detailPanel.Hide(); else Back();   // Back closes the pop-up first, never more
        }

        void Back() { SceneNavigator.Go(dashboardScene); }

        void Paint()
        {
            if (this == null) return;
            for (int i = 0; tabButtons != null && i < tabButtons.Length && i < Tabs.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                bool on = Tabs[i] == tab;
                var label = tabButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) { label.text = $"{Tabs[i]} ({SubmissionStore.Count(Tabs[i])})"; label.color = on ? Color.white : new Color(0.35f, 0.28f, 0.30f); }
                if (tabButtons[i].targetGraphic is Image img) img.color = on ? new Color(0.86f, 0.45f, 0.55f) : new Color(1, 1, 1, 0.9f);
            }
            if (syncText != null) syncText.text = SubmissionStore.SyncState == "Synced" ? "Live · updates from the website appear here by themselves"
                : SubmissionStore.SyncState == "Offline" ? "Offline · showing what this phone last received" : SubmissionStore.SyncState + "...";

            foreach (GameObject r in rows) if (r != null) Destroy(r);
            rows.Clear();
            if (content != null && rowTemplate != null)
                foreach (SubmissionStore.Info info in SubmissionStore.Items)
                {
                    if (tab != "All" && info.Group != tab) continue;
                    GameObject row = Instantiate(rowTemplate, content);
                    row.SetActive(true); row.name = "Row_" + info.id;
                    SubmissionRows.Fill(row, info);
                    string id = info.id;
                    if (row.TryGetComponent(out Button b)) b.onClick.AddListener(() => ShowDetail(id));
                    rows.Add(row);
                }
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(rows.Count == 0);
                emptyText.text = !SubmissionStore.Loaded ? "Loading your submissions..." : tab == "All" ? "No submissions yet." : $"Nothing in {tab}.";
            }
            if (!string.IsNullOrEmpty(OpenId) && SubmissionStore.Find(OpenId) != null) { string id = OpenId; OpenId = ""; ShowDetail(id); }
            else if (detailPanel != null && detailPanel.IsOpen && shownId.Length > 0) FillDetail(SubmissionStore.Find(shownId));   // live while open
        }

        void ShowDetail(string id)
        {
            SubmissionStore.Info info = SubmissionStore.Find(id);
            if (info == null) return;
            shownId = id;
            FillDetail(info);
            if (detailPanel != null) detailPanel.Show();
        }

        void FillDetail(SubmissionStore.Info i)
        {
            if (i == null) return;
            if (detailName != null) detailName.text = string.IsNullOrEmpty(i.name) ? "(no name)" : i.name;
            if (detailBrand != null) detailBrand.text = string.IsNullOrEmpty(i.brand) ? "Brand not known" : i.brand;
            if (detailStatus != null) { detailStatus.text = i.Label; detailStatus.color = SubmissionRows.ColorOf(i.Label); }
            string when = i.addedUnix > 0 ? System.DateTimeOffset.FromUnixTimeSeconds(i.addedUnix).ToLocalTime().ToString("d MMM yyyy, HH:mm") : "";
            if (detailInfo != null)
                detailInfo.text = $"<b>Category</b>  {Or(i.type)}\n<b>Ingredients</b>  {(i.ingredientCount > 0 ? i.ingredientCount + ": " + Cut(i.ingredientsText, 160) : "not given")}\n"
                    + $"<b>Shade</b>  {Or(i.shade)}\n<b>Barcode</b>  {Or(i.barcode)}\n\n<b>Submission ID</b>  {i.id}\n<b>Submitted</b>  {when}\n"
                    + $"<b>Source</b>  {(i.source == "staff-capture" ? "Mobile app (staff capture)" : i.source == "admin" ? "Smart Scan (admin)" : Or(i.source))}";
            if (detailNote != null)
            {
                bool note = !string.IsNullOrEmpty(i.reviewNote) && (i.reviewState == "changes" || i.reviewState == "rejected");
                detailNote.gameObject.SetActive(note);
                detailNote.text = note ? (i.reviewState == "changes" ? "Reviewer's note: " : "Reason: ") + i.reviewNote + (i.reviewState == "changes" ? "\nCorrect it on the website, or capture the product again." : "") : "";
            }
        }

        void OpenOnWebsite()
        {
            SubmissionStore.Info i = SubmissionStore.Find(shownId);
            string q = i != null && !string.IsNullOrEmpty(i.name) ? "?q=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(i.name) : "";
            Application.OpenURL(StaffAccess.WebsiteInventory + q);
        }

        static string Or(string s) => string.IsNullOrEmpty(s) ? "not given" : s;
        static string Cut(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
