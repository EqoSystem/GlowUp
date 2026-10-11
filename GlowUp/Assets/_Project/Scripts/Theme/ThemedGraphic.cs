using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Theme
{
    public enum ThemeRole
    {
        Background, Surface, MainText, SecondaryText,
        ButtonFill, ButtonText, InputBackground, InputText, InputPlaceholder,
        Accent, Success, Warning,
        Heading, SurfaceAlt, Caution, // appended last on purpose: objects store their role by position in this list
        Gold, AccentText             // gold = logo, borders, small highlights; accent text = pink that is readable as small text
    }

    /// <summary>
    /// Put on any Image or TMP text. Pick which palette role it uses; it recolours itself
    /// whenever the theme changes. Change the role in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThemedGraphic : MonoBehaviour
    {
        [SerializeField] private ThemeRole role = ThemeRole.MainText;
        [Tooltip("Keep the alpha already set on the graphic (so translucent panels stay translucent)")]
        [SerializeField] private bool preserveAlpha = true;

        void OnEnable()
        {
            ThemeManager.ThemeChanged += Apply;
            if (ThemeManager.Instance != null) Apply(ThemeManager.Instance.IsDark);
        }

        void OnDisable() => ThemeManager.ThemeChanged -= Apply;

        void Apply(bool dark)
        {
            if (ThemeManager.Instance == null) return;
            ApplyPalette(ThemeManager.Instance.Palette);
        }

        /// <summary>Also used by the Editor menu GlowUp > Preview Theme to preview without Play mode.</summary>
        public void ApplyPalette(ThemeProfile.Palette p)
        {
            var g = GetComponent<Graphic>();
            if (g == null) return;
            Color c = Pick(p);
            if (preserveAlpha) c.a = g.color.a;
            g.color = c;
        }

        Color Pick(ThemeProfile.Palette p)
        {
            switch (role)
            {
                case ThemeRole.Background: return p.background;
                case ThemeRole.Surface: return p.surface;
                case ThemeRole.SurfaceAlt: return p.surfaceAlt;
                case ThemeRole.Heading: return p.heading;
                case ThemeRole.MainText: return p.mainText;
                case ThemeRole.SecondaryText: return p.secondaryText;
                case ThemeRole.ButtonFill: return p.buttonFill;
                case ThemeRole.ButtonText: return p.buttonText;
                case ThemeRole.InputBackground: return p.inputBackground;
                case ThemeRole.InputText: return p.inputText;
                case ThemeRole.InputPlaceholder: return p.inputPlaceholder;
                case ThemeRole.Accent: return p.accent;
                case ThemeRole.Success: return p.success;
                case ThemeRole.Caution: return p.caution;
                case ThemeRole.Gold: return p.gold;
                case ThemeRole.AccentText: return p.accentText;
                default: return p.warning;
            }
        }
    }
}
