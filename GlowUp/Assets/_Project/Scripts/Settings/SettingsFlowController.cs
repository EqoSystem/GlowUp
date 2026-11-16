using GlowUp.Common;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The Settings page itself: the Back button (and the phone's own back key) return to the screen
    /// the user came from, and the list starts at the top. Each group of settings on the page has its
    /// own script, on the objects under "SettingsFeatures" in the Hierarchy.
    /// </summary>
    public class SettingsFlowController : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private ScrollRect list;
        [Tooltip("Where Back goes")]
        [SerializeField] private string previousScene = SceneNames.Dashboard;
        [Tooltip("The phone's back key (Android) does the same as the Back button")]
        [SerializeField] private bool phoneBackKeyGoesBack = true;
        [Tooltip("Pop-ups of this page. While one is open, the back key closes it instead of leaving the page.")]
        [SerializeField] private OverlayPanel[] popups;

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(GoBack);
        }

        void Start()
        {
            UiWording.Retitle("Privacy Policy & Terms", UiWording.LegalTitle);           // one name everywhere (10 November 2026), without rebuilding the scene
            if (list != null) list.verticalNormalizedPosition = 1f;
        }

        void Update()
        {
            if (!phoneBackKeyGoesBack || !Input.GetKeyDown(KeyCode.Escape)) return;
            if (popups != null)
                foreach (OverlayPanel popup in popups)
                    if (popup != null && popup.IsOpen) { popup.Hide(); return; }
            GoBack();
        }

        public void GoBack() { SceneNavigator.Go(previousScene); }
    }
}
