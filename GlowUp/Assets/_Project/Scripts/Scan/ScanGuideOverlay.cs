using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The guide drawn over the camera while scanning: a framed area with corner brackets, a faint
    /// guiding mesh (grid) inside it, and a line that sweeps up and down. Whatever is inside the
    /// frame is what the app reads, so the user can see exactly which part of the product they are
    /// pointing at. It changes colour while reading and when the ingredients have been recognised.
    /// </summary>
    public class ScanGuideOverlay : ThemeAwareBehaviour
    {
        public enum State { Hidden, Searching, Reading, Detected }

        [Header("References")]
        [Tooltip("The framed area. Its rectangle is the part of the camera picture that gets read.")]
        [SerializeField] private RectTransform frame;
        [SerializeField] private Image frameBrackets;
        [SerializeField] private Image mesh;
        [SerializeField] private RectTransform scanLine;
        [SerializeField] private Image scanLineImage;
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private CanvasGroup group;

        [Header("Hints")]
        [SerializeField] private string searchingHint = "Fit the ingredient list inside the frame and hold steady";
        [SerializeField] private string readingHint = "Reading the label...";
        [SerializeField] private string detectedHint = "Ingredients recognised";

        [Header("Colours (the accent and success colours come from the theme)")]
        [SerializeField] private Color searchingColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color readingColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color detectedColor = new Color32(111, 143, 91, 255);
        [Range(0f, 1f)] [SerializeField] private float meshAlpha = 0.22f;

        [Header("Animation")]
        [Tooltip("Seconds for the line to travel from the bottom of the frame to the top")]
        [SerializeField] private float sweepSeconds = 1.6f;
        [SerializeField] private float colorFadeSeconds = 0.2f;
        [Tooltip("The frame gives a small pulse when the ingredients are recognised")]
        [SerializeField] private float detectedPulseScale = 1.04f;

        public RectTransform Frame => frame;
        public State Current { get; private set; } = State.Hidden;

        Color shown, target;
        float pulse;

        void Awake() { shown = target = searchingColor; Apply(State.Hidden, true); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            readingColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            detectedColor = new Color(p.success.r, p.success.g, p.success.b, 1f);
        }

        public void SetState(State state) { Apply(state, false); }
        public void SetHint(string text) { if (hintText != null) hintText.text = text; }

        void Apply(State state, bool instant)
        {
            Current = state;
            if (group != null) group.alpha = state == State.Hidden ? 0f : 1f;
            target = state == State.Detected ? detectedColor : state == State.Reading ? readingColor : searchingColor;
            if (state == State.Detected) pulse = 1f;
            if (hintText != null && state != State.Hidden)
                hintText.text = state == State.Detected ? detectedHint : state == State.Reading ? readingHint : searchingHint;
            if (instant) { shown = target; Paint(); }
        }

        void Update()
        {
            if (Current == State.Hidden) return;

            if (shown != target)
            {
                shown = Color.Lerp(shown, target, Mathf.Clamp01(Time.unscaledDeltaTime / Mathf.Max(0.01f, colorFadeSeconds) * 3f));
                Paint();
            }

            if (scanLine != null && frame != null)
            {
                bool sweeping = Current != State.Detected;
                if (scanLineImage != null) scanLineImage.enabled = sweeping;
                if (sweeping)
                {
                    float k = Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.1f, sweepSeconds), 1f);
                    scanLine.anchorMin = new Vector2(0f, k); scanLine.anchorMax = new Vector2(1f, k);
                    scanLine.anchoredPosition = Vector2.zero;
                }
            }

            if (pulse > 0f && frame != null)
            {
                pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime / 0.35f);
                frame.localScale = Vector3.one * (1f + (detectedPulseScale - 1f) * Mathf.Sin(pulse * Mathf.PI));
            }
        }

        void Paint()
        {
            if (frameBrackets != null) frameBrackets.color = shown;
            if (scanLineImage != null) scanLineImage.color = shown;
            if (mesh != null) mesh.color = new Color(shown.r, shown.g, shown.b, meshAlpha);
        }
    }
}
