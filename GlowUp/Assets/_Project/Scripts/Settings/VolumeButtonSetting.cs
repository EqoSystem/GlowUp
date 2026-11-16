using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// "Volume keys" on the Settings page (Camera Helpers). New17: On / Off, saved with the other settings in UiPreferences.VolumeKey
    /// (On = Both, Off = Off; the older Up / Down choices count as On). On AI Smart Scan, while it is on: volume down chooses photo
    /// capture, volume up chooses live scanning, and both keys together take the photo or start / pause the scan. Only that screen uses
    /// the keys; everywhere else, and while this is off, they change the volume.
    /// </summary>
    public class VolumeButtonSetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = On, tab 1 = Off")]
        [SerializeField] private ChoiceTabs tabs;
        [SerializeField] private TextMeshProUGUI statusText;
        const int OffIndex = 1;

        [Header("Wording")]
        [SerializeField] private string onEffect = "On AI Smart Scan: volume down = photo capture, volume up = live scan, both keys together = take the photo or start / pause the scan.";
        [SerializeField] private string offEffect = "The volume keys only change the volume.";

        void Awake()
        {
            if (tabs != null) tabs.Changed += index => UiPreferences.VolumeKey = index == 0 ? UiPreferences.VolumeKeys.Both : UiPreferences.VolumeKeys.Off;
        }

        void OnEnable() { UiPreferences.Changed += Refresh; }
        void OnDisable() { UiPreferences.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            bool on = UiPreferences.VolumeKey != UiPreferences.VolumeKeys.Off;
            if (tabs != null) tabs.Select(on ? 0 : OffIndex);
            if (statusText != null) statusText.text = on ? onEffect : offEffect;
        }
    }
}
