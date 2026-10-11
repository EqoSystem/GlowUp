using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The thin outline around an input field (or dropdown). It is a soft neutral while idle and
    /// turns the accent colour (dusty pink) while the field is being typed in - the "active state".
    /// Colours and fade time are Inspector fields.
    /// </summary>
    public class InputFocusBorder : ThemeAwareBehaviour
    {
        [SerializeField] private Image border;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color idleColor = new Color32(190, 180, 170, 255);
        [SerializeField] private Color focusedColor = new Color32(217, 142, 160, 255);

        [Header("Animation")]
        [SerializeField] private float fadeSeconds = 0.15f;

        float k, target;

        void Awake()
        {
            var field = GetComponent<TMP_InputField>();
            if (field != null)
            {
                field.onSelect.AddListener(_ => target = 1f);
                field.onDeselect.AddListener(_ => target = 0f);
            }
            Paint();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            idleColor = Color.Lerp(p.inputPlaceholder, p.background, 0.40f); idleColor.a = 1f;
            focusedColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            Paint();
        }

        void Update()
        {
            if (Mathf.Approximately(k, target)) return;
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            Paint();
        }

        void Paint() { if (border != null) border.color = Color.Lerp(idleColor, focusedColor, k); }
    }
}
