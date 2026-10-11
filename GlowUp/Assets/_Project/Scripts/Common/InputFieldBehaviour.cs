using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Standard behaviour for every TMP input field: hides the placeholder as soon as the
    /// field is tapped, enforces a character limit and (optionally) blocks copy/paste.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class InputFieldBehaviour : MonoBehaviour
    {
        [SerializeField] private int characterLimit = 64;
        [Tooltip("Placeholder is cleared the moment the field is tapped")]
        [SerializeField] private bool hidePlaceholderOnFocus = true;
        [SerializeField] private bool blockPaste = false;
        [SerializeField] private TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard;
        [Tooltip("Only for Standard/Alphanumeric style fields: strip characters that are not letters/digits")]
        [SerializeField] private bool alphanumericOnly = false;

        TMP_InputField field;

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.characterLimit = characterLimit;
            field.contentType = contentType;
            field.onSelect.AddListener(_ => SetPlaceholder(false));
            field.onDeselect.AddListener(_ => SetPlaceholder(string.IsNullOrEmpty(field.text)));
            field.onValueChanged.AddListener(OnValueChanged);
        }

        /// <summary>Lets a screen change at run time whether only letters and digits may be typed.</summary>
        public void SetAlphanumericOnly(bool value) { alphanumericOnly = value; }

        void SetPlaceholder(bool visible)
        {
            if (!hidePlaceholderOnFocus || field.placeholder == null) return;
            field.placeholder.gameObject.SetActive(visible);
        }

        string lastValid = "";
        float lastLen;

        void OnValueChanged(string value)
        {
            if (alphanumericOnly)
            {
                var sb = new System.Text.StringBuilder();
                foreach (char c in value) if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                if (sb.ToString() != value) { field.SetTextWithoutNotify(sb.ToString()); value = sb.ToString(); }
            }
            // Block paste: a single event that grows the text by more than 1 char is a paste.
            if (blockPaste && value.Length - lastValid.Length > 1)
            {
                field.SetTextWithoutNotify(lastValid);
                return;
            }
            lastValid = value;
        }
    }
}
