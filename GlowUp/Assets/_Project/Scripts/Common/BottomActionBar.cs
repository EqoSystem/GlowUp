using GlowUp.Scan;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The three shortcut buttons at the bottom of the main screens:
    /// Recent Scans | Start Scan | Try On. Each button's destination scene is an Inspector field.
    /// Tapping the screen you are already on does nothing.
    /// </summary>
    public class BottomActionBar : MonoBehaviour
    {
        public enum Tab { None, Recent, Scan, TryOn }

        [SerializeField] private Button recentButton;
        [SerializeField] private Button scanButton;
        [SerializeField] private Button tryOnButton;

        [Header("Where each button goes")]
        [SerializeField] private string recentScene = SceneNames.RecentScans;
        [SerializeField] private string scanScene = SceneNames.AiSmartAnalysis;
        [SerializeField] private string tryOnScene = SceneNames.ArTryOn;

        [Tooltip("The screen this bar is on (its own button is switched off)")]
        [SerializeField] private Tab current = Tab.None;

        LiquidTabBar pill;

        void Awake()
        {
            pill = GetComponent<LiquidTabBar>();
            // the screen's card keeps clear of the camera hole through the ONE safe-area system (Assets/SafeAreaFix, 12 November 2026);
            // the earlier SafeAreaFitter on this card only is no longer added (it would apply the inset twice)
            if (recentButton != null) recentButton.onClick.AddListener(() => { if (current != Tab.Recent) Open(recentScene, 0); });
            if (scanButton != null) scanButton.onClick.AddListener(() => { if (current != Tab.Scan) Open(scanScene, 1); });
            if (tryOnButton != null) tryOnButton.onClick.AddListener(() =>
            {
                if (current == Tab.TryOn) return;
                // from the bar there is no product chosen yet: Try On says where to pick one (17 Nov 2026), the bar's pill stays where it is
                ScanSession.TryOn = null;
                SayNeedsProduct();
            });
            TryOnGate.Blocked += SayNeedsProduct;    // a swipe or another button asked for AR Try-On without a product
        }

        void OnDestroy() { TryOnGate.Blocked -= SayNeedsProduct; }

        void SayNeedsProduct()
        {
            if (this == null || !isActiveAndEnabled) return;
            if (pill != null) pill.Release();
            // big and in the middle of the screen, always on screen for at least 1.5 seconds (the owner, 12 November 2026)
            ScreenNotice notice = ScreenNotice.On(transform, GetComponentInChildren<TMPro.TMP_Text>(true));
            if (notice != null) notice.ShowCentre(TryOnGate.Message, "Scan now", () => Open(scanScene, 1));
        }

        /// <summary>
        /// Opens the screen of a button. Nothing covers the screen while the next one is made ready, so the bar answers
        /// the tap itself: its pink pill sets off towards the tapped icon at once.
        /// </summary>
        void Open(string scene, int slot)
        {
            if (ScreenTransition.IsBusy) return;
            SceneNavigator.Go(scene);
            if (pill != null) pill.Commit(slot);
        }
    }
}
