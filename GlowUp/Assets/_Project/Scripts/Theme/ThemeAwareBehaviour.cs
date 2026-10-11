using UnityEngine;

namespace GlowUp.Theme
{
    /// <summary>
    /// Base for animation components whose colours come from the theme (Continue button glow,
    /// checklist rows, tile outline, input borders ...). While "Use Theme Colors" is on, the colour
    /// fields on the component are filled in from the ThemeProfile and follow light / dark mode.
    /// Untick it to type your own colours on that one object.
    /// </summary>
    public abstract class ThemeAwareBehaviour : MonoBehaviour
    {
        [Header("Theme")]
        [Tooltip("On: colours come from the ThemeProfile and follow light / dark mode. Off: the colours typed on this component are used.")]
        [SerializeField] protected bool useThemeColors = true;

        protected virtual void OnEnable()
        {
            ThemeManager.ThemeChanged += HandleThemeChanged;
            if (ThemeManager.Instance != null) HandleThemeChanged(ThemeManager.Instance.IsDark);
        }

        protected virtual void OnDisable() { ThemeManager.ThemeChanged -= HandleThemeChanged; }

        void HandleThemeChanged(bool dark)
        {
            if (useThemeColors && ThemeManager.Instance != null) ApplyPalette(ThemeManager.Instance.Palette);
        }

        /// <summary>Used by the Editor (GlowUp > Preview Theme and the scene builders) so the scene looks right without Play mode.</summary>
        public void PreviewPalette(ThemeProfile.Palette palette)
        {
            if (useThemeColors && palette != null) ApplyPalette(palette);
        }

        /// <summary>Copy the palette into this component's colour fields and repaint its idle look.</summary>
        protected abstract void ApplyPalette(ThemeProfile.Palette palette);
    }
}
