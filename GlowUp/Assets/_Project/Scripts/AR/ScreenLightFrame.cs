using GlowUp.Common;
using GlowUp.Scan;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// The "front flash" of AR Try-On: a bright white frame round the screen that lights the user's
    /// face in a dark room (phones have no flash next to the selfie camera, so the screen is the light).
    ///
    /// It comes on by itself when the camera picture is dark, if "Face light in the dark" is on on the
    /// Settings page. The lamp button on the camera view switches it on and off by hand. In live face
    /// tracking the app cannot measure how dark it is, so there the button is the way to switch it on.
    /// </summary>
    public class ScreenLightFrame : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The white frame (a CanvasGroup over the whole screen; invisible while the light is off)")]
        [SerializeField] private CanvasGroup frame;
        [SerializeField] private Button toggleButton;
        [Tooltip("The button's icon: clear when the light is on, faint when it is off")]
        [SerializeField] private Graphic toggleIcon;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenOff = 0.45f;
        [Tooltip("The guided preview's camera: its picture tells how dark the room is. Optional.")]
        [SerializeField] private CameraFeedController cameraFeed;

        [Header("When does it come on by itself?")]
        [Tooltip("The picture counts as dark below this brightness (0 = black, 1 = white)")]
        [Range(0.02f, 0.6f)] [SerializeField] private float darkBelow = 0.2f;
        [Tooltip("Seconds it must stay dark first")]
        [SerializeField] private float darkSeconds = 1.2f;
        [Tooltip("How often the picture is measured (seconds)")]
        [SerializeField] private float checkEvery = 0.5f;

        [Header("Look")]
        [Tooltip("How bright the frame is when on (1 = pure white)")]
        [Range(0.3f, 1f)] [SerializeField] private float frameAlpha = 1f;
        [SerializeField] private float fadeSpeed = 5f;

        public bool IsOn { get; private set; }
        bool switchedOffByUser;
        float timer, darkFor;

        void Awake()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(() => { switchedOffByUser = IsOn; IsOn = !IsOn; Paint(); });
            if (frame != null) { frame.alpha = 0f; frame.blocksRaycasts = false; frame.interactable = false; }
            Paint();
        }

        void Update()
        {
            if (frame != null) frame.alpha = Mathf.MoveTowards(frame.alpha, IsOn ? frameAlpha : 0f, fadeSpeed * Time.unscaledDeltaTime);

            // once it is on it stays on: its own light makes the picture brighter, so measuring again would switch it off and on
            if (IsOn || switchedOffByUser || !UiPreferences.TryOnScreenLight || cameraFeed == null || !cameraFeed.IsPlaying) { darkFor = 0f; return; }
            timer += Time.unscaledDeltaTime;
            if (timer < checkEvery) return;
            cameraFeed.MeasureMotion();                         // takes a fresh look at the picture
            darkFor = cameraFeed.MeasureBrightness() < darkBelow ? darkFor + timer : 0f;
            timer = 0f;
            if (darkFor >= darkSeconds) { IsOn = true; Paint(); }
        }

        void Paint()
        {
            if (toggleIcon == null) return;
            Color c = toggleIcon.color; c.a = IsOn ? 1f : iconAlphaWhenOff; toggleIcon.color = c;
        }
    }
}
