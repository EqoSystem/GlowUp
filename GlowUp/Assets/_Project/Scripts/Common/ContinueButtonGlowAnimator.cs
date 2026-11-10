using System.Collections;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Continue-button animation: when set ready, the button smoothly expands, glows with the
    /// accent colour and an inner arrow slides forward. Everything is tunable in the Inspector.
    /// Call <see cref="SetReady"/> from the scene's validation logic.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ContinueButtonGlowAnimator : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private Image buttonBackground;
        [Tooltip("Soft halo image behind/around the button")]
        [SerializeField] private Image glowImage;
        [SerializeField] private RectTransform arrow;
        [SerializeField] private TMP_Text label;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color idleColor = new Color32(201, 191, 176, 255);
        [SerializeField] private Color readyColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color glowColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color labelIdleColor = new Color(0.29f, 0.25f, 0.25f, 0.6f);
        [SerializeField] private Color labelReadyColor = new Color32(58, 46, 45, 255);

        [Header("Motion")]
        [Tooltip("How much the button grows when it becomes ready")]
        [SerializeField] private float expandScale = 1.04f;
        [SerializeField] private float transitionSeconds = 0.35f;
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("How far the arrow slides forward while ready")]
        [SerializeField] private float arrowSlideDistance = 16f;
        [SerializeField] private float arrowLoopSeconds = 0.9f;
        [SerializeField] private float glowPulseSpeed = 2.2f;
        [Range(0f, 1f)] [SerializeField] private float glowMinAlpha = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float glowMaxAlpha = 0.9f;

        Button button;
        bool ready;
        float t;                       // 0 idle -> 1 ready
        Vector2 arrowStart;
        Vector3 baseScale = Vector3.one;

        void Awake()
        {
            button = GetComponent<Button>();
            button.transition = Selectable.Transition.None;      // colours are driven here, not by the Button's tint
            baseScale = transform.localScale;
            if (arrow != null) arrowStart = arrow.anchoredPosition;
            PaintColors(0f);
            button.interactable = false;
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            idleColor = Color.Lerp(p.surfaceAlt, p.inputPlaceholder, 0.30f); idleColor.a = 1f;
            readyColor = p.buttonFill;
            glowColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            labelReadyColor = p.buttonText;
            labelIdleColor = new Color(p.mainText.r, p.mainText.g, p.mainText.b, 0.6f);
            PaintColors(t);
        }

        public void SetReady(bool value)
        {
            if (button == null) button = GetComponent<Button>();
            if (ready == value) return;
            ready = value;
            button.interactable = value;
            StopAllCoroutines();
            if (isActiveAndEnabled) StartCoroutine(Transition());
            else { t = value ? 1f : 0f; PaintColors(t); ApplyMotion(t); }
        }

        IEnumerator Transition()
        {
            float from = t, to = ready ? 1f : 0f;
            float dur = Mathf.Max(0.01f, transitionSeconds), e = 0;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                t = Mathf.Lerp(from, to, easing.Evaluate(Mathf.Clamp01(e / dur)));
                PaintColors(t); ApplyMotion(t);
                yield return null;
            }
            t = to; PaintColors(t); ApplyMotion(t);
        }

        bool calm;
        /// <summary>Calm: the ready state shows as a steady glow; nothing loops (no pulse, no sliding arrow).</summary>
        public void Calm() { calm = true; }

        void Update()
        {
            if (!ready) return;
            bool still = calm || GlowUp.Performance.PerformanceSettings.ReduceEffects;      // Performance Mode: no looping arrow, a steady glow
            float loop = still ? 0f : Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.05f, arrowLoopSeconds), 1f);
            if (arrow != null) arrow.anchoredPosition = arrowStart + Vector2.right * (arrowSlideDistance * loop * t);
            if (glowImage != null)
            {
                float pulse = still ? 1f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * glowPulseSpeed);
                var c = glowColor; c.a = Mathf.Lerp(glowMinAlpha, glowMaxAlpha, pulse) * t;
                glowImage.color = c;
            }
        }

        /// <summary>Colours only (safe in the Editor: it never touches the scale).</summary>
        void PaintColors(float k)
        {
            if (buttonBackground != null) buttonBackground.color = Color.Lerp(idleColor, readyColor, k);
            Color text = Color.Lerp(labelIdleColor, labelReadyColor, k);
            if (label != null) label.color = text;
            if (arrow != null) { var g = arrow.GetComponent<Graphic>(); if (g != null) g.color = text; }
            if (glowImage != null) { var c = glowColor; c.a = glowMaxAlpha * k; glowImage.color = c; }
        }

        void ApplyMotion(float k)
        {
            transform.localScale = Vector3.Lerp(baseScale, baseScale * expandScale, k);
            if (arrow != null && k < 0.01f) arrow.anchoredPosition = arrowStart;
        }
    }
}
