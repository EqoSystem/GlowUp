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
            // phase 1D: the screen's card (the bar's parent) keeps clear of the phone's notch and gesture bar
            if (transform.parent is RectTransform card && card.anchorMin == Vector2.zero && card.anchorMax == Vector2.one) SafeAreaFitter.On(card);
            if (recentButton != null) recentButton.onClick.AddListener(() => { if (current != Tab.Recent) Open(recentScene, 0); });
            if (scanButton != null) scanButton.onClick.AddListener(() => { if (current != Tab.Scan) Open(scanScene, 1); });
            if (tryOnButton != null) tryOnButton.onClick.AddListener(() =>
            {
                if (current == Tab.TryOn) return;
                ScanSession.TryOn = null;            // from the bar, Try On always starts by choosing a saved product
                Open(tryOnScene, 2);
            });
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
