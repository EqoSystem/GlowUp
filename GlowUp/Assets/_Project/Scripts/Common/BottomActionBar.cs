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

        void Awake()
        {
            if (recentButton != null) recentButton.onClick.AddListener(() => { if (current != Tab.Recent) SceneNavigator.Go(recentScene); });
            if (scanButton != null) scanButton.onClick.AddListener(() => { if (current != Tab.Scan) SceneNavigator.Go(scanScene); });
            if (tryOnButton != null) tryOnButton.onClick.AddListener(() =>
            {
                if (current == Tab.TryOn) return;
                ScanSession.TryOn = null;            // from the bar, Try On always starts by choosing a saved product
                SceneNavigator.Go(tryOnScene);
            });
        }
    }
}
