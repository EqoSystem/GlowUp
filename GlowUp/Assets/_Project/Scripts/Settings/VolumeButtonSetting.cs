using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// "Volume keys as shutter" on the Settings page (Camera Helpers; 11 November 2026): four tabs, Both keys, Volume up, Volume down and Off,
    /// saved in UiPreferences.VolumeKey, and a line that says what a press does. On AI Smart Scan the chosen key starts / pauses the live
    /// scan, and takes the photo in photo mode. Only that screen uses the keys; everywhere else they change the volume.
    /// </summary>
    public class VolumeButtonSetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = Both keys, tab 1 = Volume up, tab 2 = Volume down, tab 3 = Off")]
        [SerializeField] private ChoiceTabs tabs;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Wording")]
        [SerializeField] private string bothEffect = "On AI Smart Scan, volume up or volume down starts or pauses the live scan, and takes the photo in photo mode.";
        [SerializeField] private string upEffect = "On AI Smart Scan, volume up starts or pauses the live scan, and takes the photo in photo mode. Volume down changes the volume.";
        [SerializeField] private string downEffect = "On AI Smart Scan, volume down starts or pauses the live scan, and takes the photo in photo mode. Volume up changes the volume.";
        [SerializeField] private string offEffect = "The volume keys only change the volume.";

        void Awake()
        {
            if (tabs != null) tabs.Changed += index => UiPreferences.VolumeKey = (UiPreferences.VolumeKeys)Mathf.Clamp(index, 0, 3);
        }

        void OnEnable() { UiPreferences.Changed += Refresh; }
        void OnDisable() { UiPreferences.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            UiPreferences.VolumeKeys now = UiPreferences.VolumeKey;
            if (tabs != null) tabs.Select((int)now);
            if (statusText != null)
                statusText.text = now == UiPreferences.VolumeKeys.Both ? bothEffect : now == UiPreferences.VolumeKeys.Up ? upEffect
                                : now == UiPreferences.VolumeKeys.Down ? downEffect : offEffect;
        }
    }
}
