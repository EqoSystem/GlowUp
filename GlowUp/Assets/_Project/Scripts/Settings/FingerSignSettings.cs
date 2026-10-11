using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// "Finger signs" on the Settings page (Sounds and Motion): which screen each number of raised fingers opens, how
    /// long a sign must be held, and a line that says whether finger signs can work on this phone.
    ///
    /// Each of the five rows has a button that shows the screen for that number; a tap on it moves on to the next
    /// choice (Home, AI Smart Scan, Recent Scans, AR Try-On, Settings, Nothing). "Reset the five numbers" restores
    /// 1 Home, 2 AI Smart Scan, 3 Recent Scans, 4 AR Try-On, 5 Settings. The choices are kept in UiPreferences and read by
    /// FingerSignNavigator (on UiServices).
    /// </summary>
    public class FingerSignSettings : MonoBehaviour
    {
        [Tooltip("The five buttons, for 1 to 5 fingers, in that order")]
        [SerializeField] private Button[] screenButtons = new Button[5];
        [Tooltip("The words on those buttons (the name of the screen)")]
        [SerializeField] private TextMeshProUGUI[] screenLabels = new TextMeshProUGUI[5];
        [SerializeField] private Slider holdSlider;
        [Tooltip("Shows the hold time in seconds")]
        [SerializeField] private TextMeshProUGUI holdValueText;
        [SerializeField] private Button resetButton;
        [Tooltip("Says whether finger signs can work on this phone")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Wording")]
        [Tooltip("{0} = seconds")]
        [SerializeField] private string holdFormat = "{0:0.0} s";
        [SerializeField] private string statusOff = "Off. Switch it on, then hold 1 to 5 fingers up to the selfie camera on Home, Recent Scans, Settings or a result.";
        [SerializeField] private string statusOn = "On. Hold your hand up to the selfie camera, about 30 cm away, and keep the sign still. The picture stays on this phone.";
        [SerializeField] private string statusSameTwice = "Two numbers open the same screen. That works, but one of them could open another screen.";

        bool writing;

        void Awake()
        {
            for (int i = 0; i < screenButtons.Length; i++)
            {
                int fingers = i + 1;
                if (screenButtons[i] != null) screenButtons[i].onClick.AddListener(() => NextScreen(fingers));
            }
            if (holdSlider != null)
            {
                holdSlider.minValue = UiPreferences.ShortestFingerHold; holdSlider.maxValue = UiPreferences.LongestFingerHold; holdSlider.wholeNumbers = false;
                holdSlider.onValueChanged.AddListener(OnHoldMoved);
            }
            if (resetButton != null) resetButton.onClick.AddListener(UiPreferences.ResetFingerSigns);
        }

        void OnEnable() { UiPreferences.Changed += Show; Show(); }
        void OnDisable() { UiPreferences.Changed -= Show; }
        void Update() { ShowStatus(); }        // the reason it cannot work is only known once the camera was tried

        /// <summary>The button of a number moves on to the next screen in the list.</summary>
        void NextScreen(int fingers)
        {
            string[] screens = UiPreferences.FingerSignScreens;
            int at = System.Array.IndexOf(screens, UiPreferences.FingerSignScreen(fingers));
            UiPreferences.SetFingerSignScreen(fingers, screens[(at + 1 + screens.Length) % screens.Length]);
        }

        void OnHoldMoved(float seconds)
        {
            if (writing) return;
            UiPreferences.FingerSignHoldSeconds = seconds;
        }

        void Show()
        {
            for (int i = 0; i < screenLabels.Length; i++)
                if (screenLabels[i] != null) screenLabels[i].text = UiPreferences.FingerSignScreenName(UiPreferences.FingerSignScreen(i + 1));
            float hold = UiPreferences.FingerSignHoldSeconds;
            if (holdSlider != null && !Mathf.Approximately(holdSlider.value, hold)) { writing = true; holdSlider.SetValueWithoutNotify(hold); writing = false; }
            if (holdValueText != null) holdValueText.text = string.Format(holdFormat, hold);
            ShowStatus();
        }

        void ShowStatus()
        {
            if (statusText == null) return;
            string text;
            // a problem is shown even while it is off: a refused camera permission switches it off and says why
            if (!string.IsNullOrEmpty(FingerSignNavigator.Problem)) text = FingerSignNavigator.Problem;
            else if (!UiPreferences.FingerSigns) text = statusOff;
            else text = SameScreenTwice() ? statusSameTwice : statusOn;
            if (statusText.text != text) statusText.text = text;
        }

        static bool SameScreenTwice()
        {
            for (int a = 1; a <= 5; a++)
                for (int b = a + 1; b <= 5; b++)
                {
                    string screen = UiPreferences.FingerSignScreen(a);
                    if (screen.Length > 0 && screen == UiPreferences.FingerSignScreen(b)) return true;
                }
            return false;
        }
    }
}
