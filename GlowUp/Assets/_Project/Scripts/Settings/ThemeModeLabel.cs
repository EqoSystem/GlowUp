using GlowUp.Theme;
using TMPro;
using UnityEngine;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The small line next to the dark mode button on the Settings page: it says which mode is on and
    /// follows the button. (The button itself is the DarkModeToggleButton.)
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class ThemeModeLabel : MonoBehaviour
    {
        [SerializeField] private string lightModeText = "Light mode is on. Tap the moon for dark mode.";
        [SerializeField] private string darkModeText = "Dark mode is on. Tap the sun for light mode.";

        void OnEnable()
        {
            ThemeManager.ThemeChanged += Show;
            if (ThemeManager.Instance != null) Show(ThemeManager.Instance.IsDark);
        }

        void OnDisable() { ThemeManager.ThemeChanged -= Show; }

        void Show(bool dark)
        {
            var text = GetComponent<TMP_Text>();
            if (text != null) text.text = dark ? darkModeText : lightModeText;
        }
    }
}
