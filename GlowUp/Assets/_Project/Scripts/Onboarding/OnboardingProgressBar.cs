using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// 5-segment progress bar + "Step X of 5" text. Set Current Step per scene in the Inspector.
    /// Colours and animation speed are fields; segments are the Bar1..Bar5 images.
    /// </summary>
    public class OnboardingProgressBar : ThemeAwareBehaviour
    {
        [SerializeField] private Image[] segments;
        [SerializeField] private TextMeshProUGUI stepLabel;
        [Range(1, 5)] [SerializeField] private int currentStep = 1;
        [SerializeField] private string labelFormat = "Step {0} of {1}";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color doneColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color currentColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color pendingColor = new Color32(206, 197, 183, 255);

        [Header("Animation")]
        [Tooltip("Seconds the current step's segment takes to fill in when the scene opens")]
        [SerializeField] private float fillSeconds = 0.4f;

        float[] k;

        void Start()
        {
            if (segments == null) return;
            k = new float[segments.Length];
            if (stepLabel != null) stepLabel.text = string.Format(labelFormat, currentStep, segments.Length);
            for (int i = 0; i < segments.Length; i++) { k[i] = i < currentStep - 1 ? 1f : 0f; Paint(i, k[i]); }
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            doneColor = p.accent;
            currentColor = p.accent;
            pendingColor = Color.Lerp(p.surfaceAlt, p.inputPlaceholder, 0.25f); pendingColor.a = 1f;
            if (segments == null) return;
            for (int i = 0; i < segments.Length; i++) Paint(i, k != null ? k[i] : (i < currentStep ? 1f : 0f));
        }

        void Update()
        {
            if (k == null) return;
            for (int i = 0; i < segments.Length; i++)
            {
                float target = i < currentStep ? 1f : 0f;
                if (Mathf.Approximately(k[i], target)) continue;
                k[i] = Mathf.MoveTowards(k[i], target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fillSeconds));
                Paint(i, k[i]);
            }
        }

        void Paint(int i, float amount)
        {
            if (segments[i] == null) return;
            var full = i == currentStep - 1 ? currentColor : doneColor;
            segments[i].color = Color.Lerp(pendingColor, full, amount);
        }
    }
}
