using GlowUp.AppSettings;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// Staff Settings (AdminSettingsScene): who is signed in, Scanner Preferences, Submissions, the admin website, About and Sign out.
    /// Back and Escape return to the scanner. Sign out and the website both ask first.
    /// </summary>
    public class AdminSettingsController : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI accountNameText;
        [SerializeField] private TextMeshProUGUI accountEmailText;
        [SerializeField] private Button prefsButton;
        [SerializeField] private Button submissionsButton;
        [SerializeField] private Button websiteButton;
        [SerializeField] private Button aboutButton;
        [SerializeField] private Button signOutButton;
        [SerializeField] private TextMeshProUGUI aboutCaption;
        [SerializeField] private ConfirmDialog confirmDialog;
        [SerializeField] private string dashboardScene = "AdminDashboardScene";
        [SerializeField] private string prefsScene = "AdminScannerPrefsScene";
        [SerializeField] private string submissionsScene = "AdminSubmissionsScene";
        [SerializeField] private string loginScene = "LogInScene";

        void Start()
        {
            string email = StaffAccess.Email;
            if (accountNameText != null) accountNameText.text = "Admin";
            if (accountEmailText != null) accountEmailText.text = string.IsNullOrEmpty(email) ? "Staff account" : email;
            if (aboutCaption != null) aboutCaption.text = "Version " + Application.version;
            Hook(backButton, Back);
            Hook(prefsButton, () => SceneNavigator.Go(prefsScene));
            Hook(submissionsButton, () => { AdminSubmissionsController.OpenId = ""; SceneNavigator.Go(submissionsScene); });
            Hook(websiteButton, OpenWebsite);
            Hook(aboutButton, () => StaffToast.Info(this, "GlowUp staff scanner, version " + Application.version + "."));
            Hook(signOutButton, AskSignOut);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && (confirmDialog == null || !confirmDialog.IsOpen)) Back();
        }

        static void Hook(Button b, UnityEngine.Events.UnityAction a) { if (b != null) b.onClick.AddListener(a); }

        void Back() { SceneNavigator.Go(dashboardScene); }

        void OpenWebsite()
        {
            const string message = "This opens the GlowUp admin website in your browser, for brands, ingredients, inventory and approvals. Your unsent capture stays on this phone.";
            if (confirmDialog != null) confirmDialog.Ask("Open the admin website?", message, "Open website", () => Application.OpenURL(StaffAccess.WebsiteHome), false, "Stay here");
            else Application.OpenURL(StaffAccess.WebsiteHome);
        }

        void AskSignOut()
        {
            void Out() { StaffAccess.SignOut(); SceneNavigator.Go(loginScene); }
            if (confirmDialog != null) confirmDialog.Ask("Sign out of staff access?", "Your unsent capture stays on this phone for this account.", "Sign out", Out, true, "Cancel");
            else Out();
        }
    }
}
