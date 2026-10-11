using GlowUp.Common;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Put on a Button: tapping it opens the pop-up chosen in the Inspector (Privacy Policy and Terms,
    /// Contact Support ...).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class OpenPanelButton : MonoBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [Tooltip("Optional: a scroll list inside the pop-up that should start at the top each time")]
        [SerializeField] private ScrollRect scrollToTop;

        void Awake() { GetComponent<Button>().onClick.AddListener(Open); }

        public void Open()
        {
            if (panel == null) { Debug.LogWarning("OpenPanelButton: no pop-up chosen", this); return; }
            panel.Show();
            if (scrollToTop != null) scrollToTop.verticalNormalizedPosition = 1f;
        }
    }
}
