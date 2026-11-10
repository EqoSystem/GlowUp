using TMPro;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// When the password is sent, the row of asterisks in the box suddenly becomes a long fixed row, so someone looking over the
    /// shoulder cannot count the characters. Only a picture is laid over the text: the real value stays in the field.
    /// The long row goes away as soon as the box is used again (typing, tapping it, or the eye showing the password).
    /// It sits inside the text area of the field, which already ends before the eye button, so it never covers the eye or
    /// anything else, and it takes no touches. Added at run time by the sign-in screen: no scene has to be rebuilt.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class PasswordLengthMask : MonoBehaviour
    {
        const int RowLength = 60;

        TMP_InputField field;
        TextMeshProUGUI row;
        float shownAlpha = 1f;
        bool masked;

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.onValueChanged.AddListener(_ => Unmask());
            field.onSelect.AddListener(_ => Unmask());
        }

        void Update()
        {
            // the eye showed the password: the real text is what the person asked to see
            if (masked && field.inputType != TMP_InputField.InputType.Password) Unmask();
        }

        /// <summary>Lays the long row over the text. Call when the password is sent.</summary>
        public void Mask()
        {
            TMP_Text real = field != null ? field.textComponent : null;
            if (real == null || masked) return;
            if (row == null) Build(real);
            row.font = real.font;
            row.fontSize = real.fontSize;
            row.color = real.color;
            row.text = new string(field.asteriskChar, RowLength);
            shownAlpha = real.color.a;
            row.gameObject.SetActive(true);
            real.alpha = 0f;                                  // the real asterisks (their number is the password's length) are hidden
            field.DeactivateInputField();                     // and so is the caret, which stands at the real length
            masked = true;
        }

        void Unmask()
        {
            if (!masked) return;
            masked = false;
            if (row != null) row.gameObject.SetActive(false);
            if (field != null && field.textComponent != null) field.textComponent.alpha = shownAlpha;
        }

        void Build(TMP_Text real)
        {
            var go = new GameObject("LengthMask", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(real.transform.parent, false);
            var rect = (RectTransform)go.transform;
            var from = real.rectTransform;
            rect.anchorMin = from.anchorMin; rect.anchorMax = from.anchorMax; rect.pivot = from.pivot;
            rect.offsetMin = from.offsetMin; rect.offsetMax = from.offsetMax;       // exactly where the real text is: inside the field's text area
            row = go.GetComponent<TextMeshProUGUI>();
            row.alignment = real.alignment;
            row.fontStyle = real.fontStyle;
            row.enableWordWrapping = false;
            row.overflowMode = TextOverflowModes.Masking;     // cut at the text area's edge, never longer than the box
            row.raycastTarget = false;                        // takes no touches: the field, the eye and the buttons keep working
            go.SetActive(false);
        }
    }
}
