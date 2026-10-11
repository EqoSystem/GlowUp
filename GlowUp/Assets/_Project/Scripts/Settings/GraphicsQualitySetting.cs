using GlowUp.Performance;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Graphics Quality on the Settings page: the two tabs Performance Mode / High Quality, the line
    /// that says what this phone is and who chose the mode, and "Detect automatically".
    /// What each mode switches on is set in the PerformanceProfile asset (Assets/_Project/Resources).
    /// </summary>
    public class GraphicsQualitySetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = Performance Mode, tab 1 = High Quality")]
        [SerializeField] private ChoiceTabs modeTabs;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button detectButton;
        [Tooltip("The asset with the limits and what each mode does. Shown here so it is one click away; the app loads it by itself.")]
        [SerializeField] private PerformanceProfile profile;

        [Header("Wording")]
        [SerializeField] private string performanceName = "Performance Mode";
        [SerializeField] private string highQualityName = "High Quality";
        [Tooltip("{0} = what this phone is, {1} = the mode")]
        [SerializeField] private string chosenAutomatically = "This phone: {0}. {1} was chosen for it automatically.";
        [Tooltip("{0} = what this phone is, {1} = the mode")]
        [SerializeField] private string chosenByUser = "This phone: {0}. You chose {1}.";
        [SerializeField] private string performanceEffect = "Lighter graphics: smoother on budget phones, less heat, longer battery.";
        [SerializeField] private string highQualityEffect = "Full graphics and animations.";

        void Awake()
        {
            if (modeTabs != null) modeTabs.Changed += index => PerformanceSettings.SetMode(index == 0 ? GraphicsMode.Performance : GraphicsMode.HighQuality);
            if (detectButton != null) detectButton.onClick.AddListener(PerformanceSettings.DetectAgain);
        }

        void OnEnable() { PerformanceSettings.Changed += Refresh; }
        void OnDisable() { PerformanceSettings.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            PerformanceSettings.Initialise();
            GraphicsMode mode = PerformanceSettings.Mode;
            if (modeTabs != null) modeTabs.Select(mode == GraphicsMode.Performance ? 0 : 1);

            if (statusText != null)
            {
                string name = mode == GraphicsMode.Performance ? performanceName : highQualityName;
                string who = string.Format(PerformanceSettings.ModeChosenByUser ? chosenByUser : chosenAutomatically, PerformanceSettings.Device.Describe(), name);
                statusText.text = who + "\n" + (mode == GraphicsMode.Performance ? performanceEffect : highQualityEffect);
            }
            if (detectButton != null) detectButton.gameObject.SetActive(PerformanceSettings.ModeChosenByUser || PerformanceSettings.FrameRateChosenByUser);
        }
    }
}
