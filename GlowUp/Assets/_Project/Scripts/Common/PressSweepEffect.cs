using GlowUp.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The tile animation of Onboarding Steps 2 and 3, for any button or tile: when it is tapped the whole
    /// object lifts a little (hover), small squares sweep across it from the bottom-left corner to the
    /// top-right corner, and a glowing outline swells and fades around it like an aura. Every number is
    /// in the Inspector. In Performance Mode only the lift and the glow play.
    ///
    /// The builder adds two children for it: "PressSweep" (the squares) and "PressGlow" (the outline).
    /// It takes the place of ButtonPressFeedback on the objects that have it.
    /// </summary>
    public class PressSweepEffect : ThemeAwareBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("References")]
        [Tooltip("The squares (a child with PixelSweepGraphic)")]
        [SerializeField] private PixelSweepGraphic sweep;
        [Tooltip("The glowing outline (a child Image)")]
        [SerializeField] private Image glow;
        [Tooltip("What lifts. Empty = this object.")]
        [SerializeField] private RectTransform lifted;

        [Header("Hover (the lift)")]
        [Tooltip("Size while the finger is down and during the sweep (1 = no change)")]
        [SerializeField] private float hoverScale = 1.035f;
        [Tooltip("Seconds to lift")]
        [SerializeField] private float liftSeconds = 0.09f;
        [Tooltip("Seconds to settle back")]
        [SerializeField] private float settleSeconds = 0.32f;
        [Tooltip("Small bounce past the resting size while settling (0 = none)")]
        [SerializeField] private float settleBounce = 0.012f;

        [Header("Pixel sweep")]
        [SerializeField] private float sweepSeconds = 0.42f;
        [Tooltip("How long each square stays lit")]
        [SerializeField] private float pixelLifetime = 0.2f;
        [Tooltip("How much the squares are lightened towards white (0 = the accent colour itself)")]
        [Range(0f, 1f)] [SerializeField] private float sweepWhiten = 0.55f;

        [Header("Glowing outline")]
        [Tooltip("Seconds the aura takes to swell")]
        [SerializeField] private float glowInSeconds = 0.18f;
        [Tooltip("Seconds it stays and fades")]
        [SerializeField] private float glowOutSeconds = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float glowPeak = 0.95f;
        [Tooltip("The aura is this much larger than the object at its peak (1 = same size)")]
        [SerializeField] private float glowSwell = 1.03f;

        [Header("Behaviour")]
        [Tooltip("Off = the object does not react while its Button is switched off")]
        [SerializeField] private bool playWhenNotInteractable = false;
        [Tooltip("Colours (set from the theme unless Use Theme Colors is off)")]
        [SerializeField] private Color glowColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color sweepColor = new Color(1f, 0.85f, 0.9f, 1f);

        Selectable selectable;
        Vector3 baseScale = Vector3.one;
        bool started, down;
        float lift, settleT = 1f, glowT = -1f;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (lifted == null) lifted = (RectTransform)transform;
            baseScale = lifted.localScale;
            started = true;
            SetGlow(0f, 1f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!started) return;
            lifted.localScale = baseScale;
            down = false; lift = 0f; settleT = 1f; glowT = -1f;
            SetGlow(0f, 1f);
            if (sweep != null) sweep.Stop();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            glowColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            sweepColor = Color.Lerp(p.accent, Color.white, sweepWhiten); sweepColor.a = 1f;
            if (sweep != null) sweep.color = sweepColor;
        }

        bool CanPlay => playWhenNotInteractable || selectable == null || selectable.IsInteractable();

        public void OnPointerDown(PointerEventData eventData) { if (CanPlay) down = true; }
        public void OnPointerUp(PointerEventData eventData) { Release(); }
        public void OnPointerExit(PointerEventData eventData) { Release(); }
        public void OnPointerClick(PointerEventData eventData) { if (CanPlay) Play(); }

        void Release()
        {
            if (!down) return;
            down = false; settleT = 0f;
        }

        /// <summary>Plays the sweep and the aura once (a script can call this to draw attention to the object).</summary>
        public void Play()
        {
            if (!isActiveAndEnabled) return;
            if (sweep != null && !GlowUp.Performance.PerformanceSettings.ReduceEffects) { sweep.color = sweepColor; sweep.Play(sweepSeconds, pixelLifetime); }
            glowT = 0f;
        }

        void Update()
        {
            // the lift
            bool up = down || (sweep != null && sweep.IsPlaying);
            float target = up ? 1f : 0f;
            if (!Mathf.Approximately(lift, target) || settleT < 1f)
            {
                lift = Mathf.MoveTowards(lift, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, up ? liftSeconds : settleSeconds * 0.5f));
                float bounce = 0f;
                if (!up && settleT < 1f)
                {
                    settleT = Mathf.Min(1f, settleT + Time.unscaledDeltaTime / Mathf.Max(0.01f, settleSeconds));
                    bounce = -Mathf.Sin(settleT * Mathf.PI) * settleBounce;
                }
                lifted.localScale = baseScale * (Mathf.Lerp(1f, hoverScale, lift) + bounce);
            }

            // the aura
            if (glowT < 0f) return;
            glowT += Time.unscaledDeltaTime;
            float total = glowInSeconds + glowOutSeconds;
            if (glowT >= total) { glowT = -1f; SetGlow(0f, 1f); return; }
            float k = glowT < glowInSeconds ? glowT / Mathf.Max(0.01f, glowInSeconds) : 1f - (glowT - glowInSeconds) / Mathf.Max(0.01f, glowOutSeconds);
            SetGlow(Mathf.SmoothStep(0f, 1f, k) * glowPeak, Mathf.Lerp(1f, glowSwell, Mathf.Clamp01(glowT / total)));
        }

        void SetGlow(float alpha, float scale)
        {
            if (glow == null) return;
            Color c = glowColor; c.a = alpha;
            glow.color = c;
            glow.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
