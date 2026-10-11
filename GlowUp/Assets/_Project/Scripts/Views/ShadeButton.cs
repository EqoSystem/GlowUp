using System;
using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A round shade swatch. Tapping it selects that shade; the selected one grows slightly and
    /// gets a ring. Used for the product's shades and the recommended shades in AR Try-On.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ShadeButton : ThemeAwareBehaviour
    {
        [SerializeField] private Image swatch;
        [SerializeField] private Image selectedRing;

        [Header("Selection")]
        [SerializeField] private float selectedScale = 1.12f;
        [SerializeField] private float animationSeconds = 0.15f;
        [Tooltip("Ring colour (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color ringColor = new Color32(217, 142, 160, 255);

        public Color Shade { get; private set; } = Color.white;
        public string Hex { get; private set; } = "";
        public bool HasShade { get; private set; }
        public event Action<ShadeButton> Clicked;

        bool selected, hooked;
        float k;
        Vector3 baseScale = Vector3.one;

        void Awake() { baseScale = transform.localScale; Hook(); Paint(); }

        void Hook()
        {
            if (hooked) return;
            hooked = true;
            GetComponent<Button>().onClick.AddListener(() => { if (HasShade) Clicked?.Invoke(this); });
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            ringColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            Paint();
        }

        /// <summary>Shows a shade. Pass null / "" to hide this swatch.</summary>
        public void Set(string hex)
        {
            Hook();
            HasShade = !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c0);
            if (HasShade) { ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c); Shade = c; Hex = hex; }
            gameObject.SetActive(HasShade);
            if (swatch != null && HasShade) swatch.color = Shade;
        }

        public void SetSelected(bool value) { selected = value; if (!isActiveAndEnabled) { k = value ? 1f : 0f; Paint(); } }

        void Update()
        {
            float target = selected ? 1f : 0f;
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, animationSeconds));
            transform.localScale = baseScale * Mathf.Lerp(1f, selectedScale, k);
            Paint();
        }

        void Paint()
        {
            if (selectedRing != null) { var c = ringColor; c.a = k; selectedRing.color = c; }
        }
    }
}
