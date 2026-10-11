using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The Settings row where the user picks one of the tap sounds. Tapping a name chooses that sound
    /// and plays it once so the user hears it. The sounds themselves (and their names) are on
    /// UiServices > UiSoundPlayer; the slider sets how loud they are.
    /// </summary>
    public class TapSoundPicker : MonoBehaviour
    {
        [Tooltip("One tab per sound, in the order of UiSoundPlayer's list")]
        [SerializeField] private ChoiceTabs tabs;
        [Tooltip("The tabs' labels: they are filled with the names from UiSoundPlayer")]
        [SerializeField] private TextMeshProUGUI[] tabLabels;
        [Tooltip("How loud the tap sounds are. Optional.")]
        [SerializeField] private Slider volumeSlider;
        [Tooltip("On = the names on the tabs come from UiSoundPlayer (off = keep what is typed on the tabs)")]
        [SerializeField] private bool namesFromSoundPlayer = true;

        void Awake()
        {
            if (tabs != null) tabs.Changed += Choose;
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f; volumeSlider.maxValue = 1f;
                volumeSlider.SetValueWithoutNotify(UiPreferences.TapSoundVolume);
                volumeSlider.onValueChanged.AddListener(v => UiPreferences.TapSoundVolume = v);
            }
        }

        void Start()
        {
            UiSoundPlayer player = UiSoundPlayer.Instance;
            if (namesFromSoundPlayer && player != null && tabLabels != null)
                for (int i = 0; i < tabLabels.Length; i++)
                    if (tabLabels[i] != null && i < player.SoundCount) tabLabels[i].text = player.NameOf(i);
            if (tabs != null) tabs.Select(UiPreferences.TapSoundIndex);
        }

        void OnDestroy() { if (tabs != null) tabs.Changed -= Choose; }

        void Choose(int index)
        {
            UiPreferences.TapSoundIndex = index;
            if (UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(index);       // let the user hear it, even when tap sounds are off
        }
    }
}
