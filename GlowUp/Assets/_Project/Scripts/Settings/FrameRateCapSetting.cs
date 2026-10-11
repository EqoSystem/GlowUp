using GlowUp.Performance;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// FPS Cap on the Settings page: two tabs that lock the frame rate (30 = saves battery and keeps
    /// budget phones cool, 60 = smoother). The two numbers come from the PerformanceProfile asset.
    /// </summary>
    public class FrameRateCapSetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = the battery-saving frame rate, tab 1 = the smooth one")]
        [SerializeField] private ChoiceTabs frameRateTabs;
        [SerializeField] private TextMeshProUGUI lowLabel;
        [SerializeField] private TextMeshProUGUI highLabel;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Wording")]
        [Tooltip("{0} = frames per second")]
        [SerializeField] private string tabFormat = "{0} FPS";
        [Tooltip("{0} = frames per second")]
        [SerializeField] private string lowEffect = "{0} frames per second: saves battery and keeps the phone cooler.";
        [Tooltip("{0} = frames per second")]
        [SerializeField] private string highEffect = "{0} frames per second: smoother animations, uses more battery.";
        [SerializeField] private string followsMode = " It follows the graphics mode until you pick one.";

        int low = 30, high = 60;

        void Awake()
        {
            if (frameRateTabs != null) frameRateTabs.Changed += index => PerformanceSettings.SetFrameRate(index == 0 ? low : high);
        }

        void OnEnable() { PerformanceSettings.Changed += Refresh; }
        void OnDisable() { PerformanceSettings.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            PerformanceSettings.Initialise();
            PerformanceProfile profile = PerformanceSettings.Profile;
            if (profile != null) { low = Mathf.Max(15, profile.batterySaverFrameRate); high = Mathf.Max(low + 1, profile.smoothFrameRate); }
            if (lowLabel != null) lowLabel.text = string.Format(tabFormat, low);
            if (highLabel != null) highLabel.text = string.Format(tabFormat, high);

            bool isLow = PerformanceSettings.FrameRate <= (low + high) / 2;
            if (frameRateTabs != null) frameRateTabs.Select(isLow ? 0 : 1);
            if (statusText != null)
                statusText.text = string.Format(isLow ? lowEffect : highEffect, PerformanceSettings.FrameRate)
                                + (PerformanceSettings.FrameRateChosenByUser ? "" : followsMode);
        }
    }
}
