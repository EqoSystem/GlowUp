using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// "Volume button does:" on the Settings page (Camera Helpers; phase 2A, 11 November 2026): three tabs, Capture photo,
    /// Start/stop scan and Do nothing, saved in UiPreferences.VolumeButton, and a line that says what a press does.
    /// Only the AI Smart Scan screen uses the buttons; everywhere else they change the volume.
    /// </summary>
    public class VolumeButtonSetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = Capture photo, tab 1 = Start/stop scan, tab 2 = Do nothing")]
        [SerializeField] private ChoiceTabs tabs;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Wording")]
        [SerializeField] private string captureEffect = "On AI Smart Scan, a press of volume up or down takes a photo of the product.";
        [SerializeField] private string scanEffect = "On AI Smart Scan, a press of volume up or down starts or pauses the scan.";
        [SerializeField] private string nothingEffect = "The volume buttons only change the volume.";

        void Awake()
        {
            if (tabs != null) tabs.Changed += index => UiPreferences.VolumeButton = (UiPreferences.VolumeButtonAction)Mathf.Clamp(index, 0, 2);
        }

        void OnEnable() { UiPreferences.Changed += Refresh; }
        void OnDisable() { UiPreferences.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            UiPreferences.VolumeButtonAction now = UiPreferences.VolumeButton;
            if (tabs != null) tabs.Select((int)now);
            if (statusText != null)
                statusText.text = now == UiPreferences.VolumeButtonAction.CapturePhoto ? captureEffect
                                : now == UiPreferences.VolumeButtonAction.StartStopScan ? scanEffect : nothingEffect;
        }
    }
}
