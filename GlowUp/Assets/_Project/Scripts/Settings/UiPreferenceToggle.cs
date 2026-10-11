using GlowUp.Common;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// One on/off switch of the Settings page that belongs to a choice kept in UiPreferences: tap
    /// sounds, the air wave that changes screen, the light round the screen edge, the bright screen in
    /// the scanner and in AR Try-On, the flashlight, the screen light. Pick the choice under "Preference".
    /// The same choice can also be changed inside a screen (the sun button in the scanner, for
    /// example); the switch follows it.
    /// </summary>
    public class UiPreferenceToggle : MonoBehaviour
    {
        // new choices go at the END of this list: the scenes remember a choice by its place in it
        public enum Preference { TapSounds, ShakeToNavigate, EdgeLight, ScanBrightScreen, TryOnBrightScreen, AutoFlashlight, TryOnScreenLight, AirGestures }

        [SerializeField] private Preference preference = Preference.TapSounds;
        [SerializeField] private Toggle toggle;
        [Tooltip("Things that only matter while the switch is on (they fade and cannot be tapped while it is off). Optional.")]
        [SerializeField] private CanvasGroup onlyWhenOn;
        [Range(0f, 1f)] [SerializeField] private float alphaWhenOff = 0.4f;
        [Tooltip("On = a tap sound is played when the switch is turned on (so the user hears what it does)")]
        [SerializeField] private bool playSoundWhenTurnedOn = false;

        bool writing;

        void Awake() { if (toggle != null) toggle.onValueChanged.AddListener(OnToggled); }
        void OnEnable() { UiPreferences.Changed += Show; Show(); }
        void OnDisable() { UiPreferences.Changed -= Show; }

        bool Value
        {
            get
            {
                switch (preference)
                {
                    case Preference.TapSounds: return UiPreferences.TapSounds;
                    case Preference.ShakeToNavigate: return UiPreferences.ShakeToNavigate;
                    case Preference.EdgeLight: return UiPreferences.BorderLight;
                    case Preference.ScanBrightScreen: return UiPreferences.ScanBrightScreen;
                    case Preference.TryOnBrightScreen: return UiPreferences.TryOnBrightScreen;
                    case Preference.AutoFlashlight: return UiPreferences.AutoFlashlight;
                    case Preference.AirGestures: return UiPreferences.AirGestures;
                    default: return UiPreferences.TryOnScreenLight;
                }
            }
            set
            {
                switch (preference)
                {
                    case Preference.TapSounds: UiPreferences.TapSounds = value; break;
                    case Preference.ShakeToNavigate: UiPreferences.ShakeToNavigate = value; break;
                    case Preference.EdgeLight: UiPreferences.BorderLight = value; break;
                    case Preference.ScanBrightScreen: UiPreferences.ScanBrightScreen = value; break;
                    case Preference.TryOnBrightScreen: UiPreferences.TryOnBrightScreen = value; break;
                    case Preference.AutoFlashlight: UiPreferences.AutoFlashlight = value; break;
                    case Preference.AirGestures: UiPreferences.AirGestures = value; break;
                    default: UiPreferences.TryOnScreenLight = value; break;
                }
            }
        }

        void Show()
        {
            bool on = Value;
            if (toggle != null && toggle.isOn != on) { writing = true; toggle.isOn = on; writing = false; }
            if (onlyWhenOn != null) { onlyWhenOn.alpha = on ? 1f : alphaWhenOff; onlyWhenOn.interactable = on; onlyWhenOn.blocksRaycasts = on; }
        }

        void OnToggled(bool on)
        {
            if (writing) return;
            Value = on;
            if (on && playSoundWhenTurnedOn && UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(UiPreferences.TapSoundIndex);
        }
    }
}
