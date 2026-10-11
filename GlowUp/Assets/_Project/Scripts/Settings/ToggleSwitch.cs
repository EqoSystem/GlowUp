using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The look and animation of an on/off switch: the knob slides across the track and the track
    /// changes colour. It sits on the same object as the Toggle. Speed, distance, the slide curve and
    /// the colours are Inspector fields; while Use Theme Colors is on, the colours follow the theme.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ToggleSwitch : ThemeAwareBehaviour
    {
        [Header("Parts")]
        [SerializeField] private Image track;
        [SerializeField] private RectTransform knob;
        [SerializeField] private Image knobImage;

        [Header("Animation")]
        [Tooltip("Seconds the knob takes to slide from one side to the other")]
        [SerializeField] private float slideSeconds = 0.16f;
        [Tooltip("How far the knob sits from the middle of the track, in canvas units")]
        [SerializeField] private float knobTravel = 30f;
        [Tooltip("The knob swells to this size in the middle of its slide (1 = no swell)")]
        [SerializeField] private float knobStretch = 1.12f;
        [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color offTrack = new Color32(196, 180, 181, 255);
        [SerializeField] private Color onTrack = new Color32(216, 108, 138, 255);
        [SerializeField] private Color knobColor = new Color32(250, 247, 242, 255);

        Toggle toggle;
        float k;                    // 0 = off, 1 = on

        Toggle Target => toggle != null ? toggle : (toggle = GetComponent<Toggle>());

        void Awake()
        {
            Target.transition = Selectable.Transition.None;
            k = Target.isOn ? 1f : 0f;
            Paint();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            k = Target.isOn ? 1f : 0f;      // a switch that was set while hidden shows its state at once
            Paint();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            bool darkTheme = p.background.grayscale < 0.5f;
            offTrack = Color.Lerp(p.surfaceAlt, p.inputPlaceholder, 0.45f); offTrack.a = 1f;
            onTrack = p.buttonFill;
            knobColor = darkTheme ? p.mainText : p.inputBackground;
            if (!Application.isPlaying) k = Target.isOn ? 1f : 0f;
            Paint();
        }

        void Update()
        {
            float target = Target.isOn ? 1f : 0f;
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, slideSeconds));
            Paint();
        }

        void Paint()
        {
            float eased = slideCurve != null ? slideCurve.Evaluate(k) : k;
            if (track != null) track.color = Color.Lerp(offTrack, onTrack, eased);
            if (knobImage != null) knobImage.color = knobColor;
            if (knob != null)
            {
                knob.anchoredPosition = new Vector2(Mathf.Lerp(-knobTravel, knobTravel, eased), 0f);
                float swell = 1f + (knobStretch - 1f) * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                knob.localScale = new Vector3(swell, 1f, 1f);
            }
        }
    }
}
