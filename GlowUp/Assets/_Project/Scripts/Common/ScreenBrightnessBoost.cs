using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Turns the phone's screen up while a camera screen is open (the scanner, AR Try-On) and gives the
    /// phone's own brightness back when the screen is left. The user can switch it off with the sun button
    /// on the screen itself or on the Settings page; both change the same setting.
    ///
    ///  - Only the app's own window is changed: the phone's brightness setting is not touched, and its
    ///    automatic brightness takes over again the moment the boost ends.
    ///  - The brightness GLIDES up and down. Without that, a phone with automatic brightness in a dim room
    ///    drops from full to its own low level in one jump when the boost ends, which looks as if the screen
    ///    went dark.
    ///  - While the boost is on, the brightness is set again every moment, so nothing else can leave the
    ///    screen dark (some phones reset it when a permission question or the camera comes up).
    ///  - The screen stays awake while the camera screen is open: nobody touches the screen while they hold
    ///    a product to the camera or look at their face, and the phone would dim and switch off otherwise.
    /// </summary>
    public class ScreenBrightnessBoost : MonoBehaviour
    {
        public enum CameraScreen { Scanner, TryOn }

        [Tooltip("Which screen this is (each has its own on/off setting)")]
        [SerializeField] private CameraScreen screen = CameraScreen.Scanner;
        [Tooltip("Brightness while the boost is on (1 = full)")]
        [Range(0.3f, 1f)] [SerializeField] private float brightness = 1f;
        [Tooltip("Seconds the brightness takes to glide up or back down (0 = at once)")]
        [SerializeField] private float glideSeconds = 0.5f;
        [Tooltip("While the boost is on, the brightness is set again this often, in seconds (in case the phone changed it)")]
        [SerializeField] private float holdEverySeconds = 1.5f;
        [Tooltip("On = the screen does not dim or switch off while this camera screen is open")]
        [SerializeField] private bool keepScreenAwake = true;

        [Header("The button on the screen (optional)")]
        [SerializeField] private Button toggleButton;
        [Tooltip("The sun icon: clear when the boost is on, faint when it is off")]
        [SerializeField] private Graphic toggleIcon;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenOff = 0.4f;

        static int awakeHolders;            // how many camera screens are asking for the screen to stay awake

        float shown = -1f;                  // what the window is set to now (-1 = the phone's own brightness)
        float phoneLevel = 0.5f;            // the phone's own brightness when the boost began: the glide starts and ends there
        bool releasing;                     // gliding back down before the phone takes over again
        bool holdsAwake;
        float holdTimer;

        public bool IsOn
        {
            get => screen == CameraScreen.Scanner ? UiPreferences.ScanBrightScreen : UiPreferences.TryOnBrightScreen;
            set { if (screen == CameraScreen.Scanner) UiPreferences.ScanBrightScreen = value; else UiPreferences.TryOnBrightScreen = value; }
        }

        void Awake() { if (toggleButton != null) toggleButton.onClick.AddListener(() => IsOn = !IsOn); }

        void OnEnable()
        {
            UiPreferences.Changed += Apply;
            if (keepScreenAwake && !holdsAwake) { holdsAwake = true; awakeHolders++; Screen.sleepTimeout = SleepTimeout.NeverSleep; }
            Apply();
        }

        void OnDisable()
        {
            UiPreferences.Changed -= Apply;
            if (holdsAwake) { holdsAwake = false; awakeHolders = Mathf.Max(0, awakeHolders - 1); if (awakeHolders == 0) Screen.sleepTimeout = SleepTimeout.SystemSetting; }
            // the screen is being left: the phone's own brightness is back at once
            if (shown >= 0f)
            {
                SetWindowBrightness(-1f);
#if UNITY_IOS && !UNITY_EDITOR
                Screen.brightness = phoneLevel;             // an iPhone has no "follow the phone again": put back what it was
#endif
            }
            shown = -1f; releasing = false;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) return;
            // back from another app or from a permission question: the window may have lost its brightness
            if (holdsAwake) Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (shown >= 0f && !releasing) SetWindowBrightness(shown);
        }

        void Apply()
        {
            bool on = IsOn;
            if (toggleIcon != null) { Color c = toggleIcon.color; c.a = on ? 1f : iconAlphaWhenOff; toggleIcon.color = c; }
            if (on)
            {
                if (shown < 0f) { phoneLevel = ReadPhoneBrightness(); shown = phoneLevel; }      // the glide starts where the screen is now
                releasing = false;
            }
            else if (shown >= 0f) releasing = true;
        }

        void Update()
        {
            if (shown < 0f) return;
            float target = releasing ? phoneLevel : Mathf.Max(brightness, 0.3f);
            float step = glideSeconds <= 0.01f ? 1f : Mathf.Min(Time.unscaledDeltaTime, 0.05f) / glideSeconds;
            float next = Mathf.MoveTowards(shown, target, step);
            holdTimer += Time.unscaledDeltaTime;
            if (!Mathf.Approximately(next, shown) || holdTimer >= Mathf.Max(0.25f, holdEverySeconds))
            {
                shown = next; holdTimer = 0f;
                SetWindowBrightness(shown);
            }
            if (releasing && Mathf.Approximately(shown, target))
            {
                // down at the phone's own level: from here the phone (and its automatic brightness) is in charge again
                releasing = false; shown = -1f;
                SetWindowBrightness(-1f);
            }
        }

        /// <summary>How bright the screen is now, 0 to 1 (the phone's own setting, also when it is automatic).</summary>
        static float ReadPhoneBrightness()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            try { float now = Screen.brightness; if (now > 0.02f && now <= 1f) return now; }
            catch (System.Exception e) { Debug.LogWarning("Screen brightness could not be read: " + e.Message); }
#endif
            return 0.5f;
        }

        /// <summary>Brightness of this app's window: 0 to 1, or a negative number to follow the phone's own setting again.</summary>
        static void SetWindowBrightness(float value)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var helper = new AndroidJavaClass("com.glowup.screen.GlowUpScreen"))
                    helper.CallStatic("setBrightness", activity, value);        // Assets/Plugins/Android/GlowUpScreen.java
            }
            catch (System.Exception e) { Debug.LogWarning("Screen brightness: " + e.Message); }
#elif UNITY_IOS && !UNITY_EDITOR
            if (value >= 0f) Screen.brightness = Mathf.Clamp(value, 0.05f, 1f);
#endif
        }
    }
}
