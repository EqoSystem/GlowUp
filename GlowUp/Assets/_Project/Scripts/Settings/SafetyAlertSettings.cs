using System;
using GlowUp.Data;
using GlowUp.Scan;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Ingredient Safety Alerts on the Settings page: the main switch, "vibrate", and one switch per
    /// trigger (my allergens, banned or harmful ingredients, and each skin condition such as eczema or
    /// psoriasis). A scan that contains a switched-on trigger opens an alert on the result screen.
    /// The first time, the conditions saved in the user's profile are switched on for them.
    /// </summary>
    public class SafetyAlertSettings : MonoBehaviour
    {
        [SerializeField] private Toggle masterToggle;
        [SerializeField] private Toggle vibrateToggle;
        [Tooltip("The words of the triggers, in the same order as Trigger Toggles: allergens, harmful, acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact")]
        [SerializeField] private string[] triggerKeys;
        [SerializeField] private Toggle[] triggerToggles;
        [Tooltip("Everything under the main switch. It is dimmed and locked while alerts are off.")]
        [SerializeField] private CanvasGroup triggerGroup;
        [Range(0.2f, 1f)] [SerializeField] private float alphaWhenOff = 0.45f;

        async void Start()
        {
            UserProfileData profile = null;
            GlowUp.Common.TextSkeleton.Waiting(this);       // the page's words are bars until the profile is here
            try { profile = await UserProfileRepository.Load(); }
            catch (Exception e) { Debug.LogWarning("Settings: profile not loaded: " + e.Message); }
            GlowUp.Common.TextSkeleton.Ready(this);
            if (this == null) return;
            SafetyAlertPreferences.SeedFromProfile(profile);

            if (masterToggle != null)
            {
                masterToggle.SetIsOnWithoutNotify(SafetyAlertPreferences.Enabled);
                masterToggle.onValueChanged.AddListener(on => { SafetyAlertPreferences.Enabled = on; Paint(); });
            }
            if (vibrateToggle != null)
            {
                vibrateToggle.SetIsOnWithoutNotify(SafetyAlertPreferences.Vibrate);
                vibrateToggle.onValueChanged.AddListener(on => SafetyAlertPreferences.Vibrate = on);
            }
            int count = Mathf.Min(triggerKeys != null ? triggerKeys.Length : 0, triggerToggles != null ? triggerToggles.Length : 0);
            for (int i = 0; i < count; i++)
            {
                Toggle toggle = triggerToggles[i];
                string trigger = triggerKeys[i];
                if (toggle == null || string.IsNullOrWhiteSpace(trigger)) continue;
                toggle.SetIsOnWithoutNotify(SafetyAlertPreferences.IsOn(trigger));
                toggle.onValueChanged.AddListener(on => SafetyAlertPreferences.Set(trigger, on));
            }
            Paint();
        }

        void Paint()
        {
            if (triggerGroup == null) return;
            bool on = SafetyAlertPreferences.Enabled;
            triggerGroup.alpha = on ? 1f : alphaWhenOff;
            triggerGroup.interactable = on;
            triggerGroup.blocksRaycasts = on;
        }
    }
}
