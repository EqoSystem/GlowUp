using GlowUp.Common;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The Sensitivity slider of "Wave to change screen" on the Settings page. Left = only a big, clear wave
    /// of the hand counts; right = a small wave is enough. The choice is kept in UiPreferences.AirSensitivity
    /// and read by AirGestureNavigator (on UiServices), which turns it into its thresholds.
    /// </summary>
    public class AirSensitivitySlider : MonoBehaviour
    {
        [SerializeField] private Slider slider;

        bool writing;

        void Awake()
        {
            if (slider == null) return;
            slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(OnMoved);
        }

        void OnEnable() { UiPreferences.Changed += Show; Show(); }
        void OnDisable() { UiPreferences.Changed -= Show; }

        void Show()
        {
            if (slider == null || Mathf.Approximately(slider.value, UiPreferences.AirSensitivity)) return;
            writing = true; slider.SetValueWithoutNotify(UiPreferences.AirSensitivity); writing = false;
        }

        void OnMoved(float value)
        {
            if (writing) return;
            UiPreferences.AirSensitivity = value;
        }
    }
}
