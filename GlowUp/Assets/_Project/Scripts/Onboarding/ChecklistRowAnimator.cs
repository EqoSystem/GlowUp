using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Selection animation for one checklist row (skin conditions, allergens, the Terms checkbox):
    /// 1) a tactile scale punch of the whole row,
    /// 2) a smooth background colour transition (unselected -> selected),
    /// 3) a checkmark that snaps in with an overshoot (and shrinks away when unticked),
    /// 4) a soft neon outline that glows while the row is selected.
    /// Every colour, curve and duration is an Inspector field on the row.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ChecklistRowAnimator : ThemeAwareBehaviour
    {
        [Header("References")]
        [SerializeField] private Image rowBackground;
        [SerializeField] private Image checkBoxBackground;
        [SerializeField] private RectTransform checkmark;
        [SerializeField] private Image glowOutline;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color unselectedRow = new Color32(247, 243, 235, 255);
        [SerializeField] private Color selectedRow = new Color32(236, 205, 207, 255);
        [SerializeField] private Color unselectedBox = new Color32(220, 211, 194, 255);
        [SerializeField] private Color selectedBox = new Color32(217, 142, 160, 255);
        [SerializeField] private Color checkColor = new Color32(58, 46, 45, 255);
        [SerializeField] private Color glowColor = new Color32(217, 142, 160, 255);

        [Header("Scale punch")]
        [SerializeField] private float punchScale = 1.04f;
        [SerializeField] private float punchSeconds = 0.28f;
        [Tooltip("0..1 progress -> punch strength (up, slightly under, then settle)")]
        [SerializeField] private AnimationCurve punchCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(0.7f, -0.25f), new Keyframe(1f, 0f));

        [Header("Colour transition")]
        [SerializeField] private float colorSeconds = 0.22f;

        [Header("Checkmark snap")]
        [SerializeField] private float checkSeconds = 0.30f;
        [SerializeField] private AnimationCurve checkCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.6f, 1.25f), new Keyframe(0.8f, 0.92f), new Keyframe(1f, 1f));
        [SerializeField] private float checkSpinDegrees = -12f;

        [Header("Glow outline")]
        [Range(0f, 1f)] [SerializeField] private float glowMaxAlpha = 0.85f;
        [SerializeField] private float glowPulseSpeed = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float glowPulseDepth = 0.3f;

        Toggle toggle;
        bool shown;
        float punchT = 1f, colorK, colorTarget, checkT = 1f;
        Vector3 baseScale = Vector3.one;

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            baseScale = transform.localScale;
            shown = toggle.isOn;
            colorK = colorTarget = shown ? 1f : 0f;
            Paint();
            ApplyCheck(shown ? 1f : 0f, 0f);
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            unselectedRow = p.inputBackground;
            selectedRow = Color.Lerp(p.inputBackground, p.accent, 0.38f);
            unselectedBox = p.surfaceAlt;
            selectedBox = p.accent;
            checkColor = p.buttonText;
            glowColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            Paint();
        }

        void Update()
        {
            if (toggle.isOn != shown)                 // user tap, "Apply All" or a value restored by code
            {
                shown = toggle.isOn;
                colorTarget = shown ? 1f : 0f;
                punchT = 0f; checkT = 0f;
            }

            if (punchT < 1f)
            {
                punchT = Mathf.Min(1f, punchT + Time.unscaledDeltaTime / Mathf.Max(0.01f, punchSeconds));
                transform.localScale = baseScale * (1f + punchCurve.Evaluate(punchT) * (punchScale - 1f));
            }

            if (!Mathf.Approximately(colorK, colorTarget))
            {
                colorK = Mathf.MoveTowards(colorK, colorTarget, Time.unscaledDeltaTime / Mathf.Max(0.01f, colorSeconds));
                Paint();
            }

            if (checkT < 1f)
            {
                checkT = Mathf.Min(1f, checkT + Time.unscaledDeltaTime / Mathf.Max(0.01f, checkSeconds));
                if (shown) ApplyCheck(checkCurve.Evaluate(checkT), 1f - checkT);
                else ApplyCheck(1f - checkT, checkT);
            }

            if (glowOutline != null && colorK > 0f)
            {
                float pulse = 1f - glowPulseDepth + glowPulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * glowPulseSpeed));
                var c = glowColor; c.a = glowMaxAlpha * colorK * pulse;
                glowOutline.color = c;
            }
        }

        /// <summary>Colours only (also used for the Editor preview).</summary>
        void Paint()
        {
            if (rowBackground != null) rowBackground.color = Color.Lerp(unselectedRow, selectedRow, colorK);
            if (checkBoxBackground != null) checkBoxBackground.color = Color.Lerp(unselectedBox, selectedBox, colorK);
            if (checkmark != null) { var g = checkmark.GetComponent<Graphic>(); if (g != null) g.color = checkColor; }
            if (glowOutline != null) { var c = glowColor; c.a = glowMaxAlpha * colorK; glowOutline.color = c; }
        }

        void ApplyCheck(float scale, float spinProgress)
        {
            if (checkmark == null) return;
            checkmark.localScale = Vector3.one * Mathf.Max(0f, scale);
            checkmark.localRotation = Quaternion.Euler(0, 0, checkSpinDegrees * spinProgress);
        }
    }
}
