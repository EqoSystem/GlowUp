using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Theme
{
    /// <summary>Attach to the Dark/Light mode button. Swaps its icon to match the mode.</summary>
    [RequireComponent(typeof(Button))]
    public class DarkModeToggleButton : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [Tooltip("Shown while in light mode (tap to go dark), e.g. a moon")]
        [SerializeField] private Sprite moonIcon;
        [Tooltip("Shown while in dark mode (tap to go light), e.g. a sun")]
        [SerializeField] private Sprite sunIcon;

        void Awake() => GetComponent<Button>().onClick.AddListener(OnClick);
        void OnEnable() { ThemeManager.ThemeChanged += Refresh; if (ThemeManager.Instance) Refresh(ThemeManager.Instance.IsDark); }
        void OnDisable() => ThemeManager.ThemeChanged -= Refresh;

        void OnClick() { if (ThemeManager.Instance) ThemeManager.Instance.Toggle(); }

        void Refresh(bool dark)
        {
            if (iconImage == null) return;
            var s = dark ? sunIcon : moonIcon;
            if (s != null) iconImage.sprite = s;
        }
    }
}
