using TMPro;
using UnityEngine;

namespace GlowUp.Theme
{
    /// <summary>
    /// Put on any TMP text. Pick its typographic role (Title, Heading, Label, Body, Input, Button);
    /// the font comes from the ThemeProfile asset, so you can swap a font for a whole role in one place.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class ThemedFont : MonoBehaviour
    {
        [SerializeField] private FontRole role = FontRole.Body;
        [Tooltip("On: the scene builders keep this role instead of guessing one from the object's name")]
        [SerializeField] private bool keepRole = false;

        public FontRole Role { get => role; set => role = value; }
        public bool KeepRole { get => keepRole; set => keepRole = value; }

        void OnEnable()
        {
            if (ThemeManager.Instance != null && ThemeManager.Instance.Profile != null) Apply(ThemeManager.Instance.Profile);
        }

        public void Apply(ThemeProfile profile)
        {
            if (profile == null) return;
            var f = profile.GetFont(role);
            if (f == null) return;
            var t = GetComponent<TMP_Text>();
            if (t != null && t.font != f) t.font = f;
        }
    }
}
