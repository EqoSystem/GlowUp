using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// "Volume keys" on the Settings page (Camera Helpers). On / Off, saved in UiPreferences.VolumeKey (On = Both, Off = Off; the older
    /// Up / Down choices count as On).
    /// New20 (owner, 17 November 2026): the user chooses what each key does. "Volume +" is photo capture (default) or live scan, and
    /// volume − is the other one; the key of the mode already on is the trigger, and both keys together open or close "Type it in".
    /// The choice shows only while the keys are on. Only the AI Smart Scan screen uses the keys; everywhere else they change the volume.
    /// </summary>
    public class VolumeButtonSetting : MonoBehaviour
    {
        [Tooltip("Tab 0 = On, tab 1 = Off (scenes built before 17 November 2026; a newer scene uses the switch below)")]
        [SerializeField] private ChoiceTabs tabs;
        [Tooltip("17 November 2026: On / Off is a switch like every other row of Camera Helpers")]
        [SerializeField] private UnityEngine.UI.Toggle onSwitch;
        [SerializeField] private TextMeshProUGUI statusText;
        [Tooltip("New20: what volume + does. Tab 0 = photo capture, tab 1 = live scan (volume − does the other)")]
        [SerializeField] private ChoiceTabs upTabs;
        [Tooltip("The row that holds the label and the tabs above: hidden while the keys are off")]
        [SerializeField] private GameObject upRow;
        const int OffIndex = 1;

        [Header("Wording")]
        [SerializeField] private string offEffect = "The volume keys only change the volume.";

        void Awake()
        {
            if (tabs != null) tabs.Changed += index => UiPreferences.VolumeKey = index == 0 ? UiPreferences.VolumeKeys.Both : UiPreferences.VolumeKeys.Off;
            if (onSwitch != null) onSwitch.onValueChanged.AddListener(on => UiPreferences.VolumeKey = on ? UiPreferences.VolumeKeys.Both : UiPreferences.VolumeKeys.Off);
            if (upTabs != null) upTabs.Changed += index => UiPreferences.VolumeUpTakesPhoto = index == 0;
        }

        void OnEnable() { UiPreferences.Changed += Refresh; }
        void OnDisable() { UiPreferences.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            bool on = UiPreferences.VolumeKey != UiPreferences.VolumeKeys.Off;
            bool upPhoto = UiPreferences.VolumeUpTakesPhoto;
            if (tabs != null) tabs.Select(on ? 0 : OffIndex);
            if (onSwitch != null && onSwitch.isOn != on) onSwitch.SetIsOnWithoutNotify(on);
            if (upTabs != null) upTabs.Select(upPhoto ? 0 : 1);
            if (upRow != null && upRow.activeSelf != on) upRow.SetActive(on);
            if (statusText != null) statusText.text = on ? UiPreferences.VolumeKeysLine(upPhoto) : offEffect;
        }
    }
}
