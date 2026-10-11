using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The small sign at the top of the screen that belongs to finger signs (FingerSignNavigator): it shows how many
    /// fingers the camera sees and which screen that opens ("3  Recent Scans"), and a ring round the number fills while
    /// the sign is held. It hides by itself when no sign is seen. This script sits on the UiServices object; what it shows is
    /// the FingerSignCanvas under it, drawn above every screen.
    /// </summary>
    public class FingerSignIndicator : MonoBehaviour
    {
        [Tooltip("The object the sign is drawn on (FingerSignCanvas). It is switched off while nothing is shown, so it costs nothing and never covers a screen.")]
        [SerializeField] private GameObject signObject;
        [SerializeField] private CanvasGroup group;
        [Tooltip("The number of fingers")]
        [SerializeField] private TextMeshProUGUI countText;
        [Tooltip("The name of the screen that number opens")]
        [SerializeField] private TextMeshProUGUI screenText;
        [Tooltip("The ring round the number: it fills while the sign is held")]
        [SerializeField] private Image ring;

        [Header("Wording")]
        [Tooltip("{0} = the name of the screen")]
        [SerializeField] private string holdFormat = "Hold for {0}";
        [SerializeField] private string openingFormat = "Opening {0}";

        [Header("Look")]
        [Tooltip("How fast the sign fades in and out (per second)")]
        [SerializeField] private float fadeSpeed = 9f;

        float target;
        string shownScreen = "";

        void Awake()
        {
            if (group != null) group.alpha = 0f;
            if (signObject != null) signObject.SetActive(false);
        }

        /// <summary>Shows the sign. `progress` 0 to 1 = how far the hold has come.</summary>
        public void Show(int fingers, string screenName, float progress)
        {
            target = 1f;
            shownScreen = screenName ?? "";
            if (signObject != null && !signObject.activeSelf) signObject.SetActive(true);
            if (countText != null) countText.text = fingers.ToString();
            if (screenText != null) screenText.text = string.Format(holdFormat, shownScreen);
            if (ring != null) ring.fillAmount = Mathf.Clamp01(progress);
        }

        /// <summary>The sign was held long enough: say so for a moment, then fade.</summary>
        public void Confirm()
        {
            if (ring != null) ring.fillAmount = 1f;
            if (screenText != null && shownScreen.Length > 0) screenText.text = string.Format(openingFormat, shownScreen);
            target = 0f;
        }

        public void Hide() { target = 0f; }

        void Update()
        {
            if (group == null || signObject == null || !signObject.activeSelf) return;
            if (Mathf.Approximately(group.alpha, target)) { if (target <= 0f) signObject.SetActive(false); return; }
            group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        }
    }
}
