using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Auth
{
    /// <summary>
    /// Password field helper: dots by default (see <see cref="PasswordDotMask"/>), an eye / closed-eye button at the end of the
    /// field to reveal the text, and paste blocking. Put on the password TMP_InputField object;
    /// assign the eye button and the two icons in the Inspector.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class PasswordFieldToggle : MonoBehaviour
    {
        [SerializeField] private Button eyeButton;
        [SerializeField] private Image eyeIconImage;
        [SerializeField] private Sprite eyeOpenIcon;
        [SerializeField] private Sprite eyeClosedIcon;
        [SerializeField] private bool blockCopyPaste = true;
        [SerializeField] private int maxLength = 64;
        [Tooltip("Re-hide the password this many seconds after it is revealed (0 = never)")]
        [SerializeField] private float autoHideSeconds = 0f;

        TMP_InputField field;
        bool visible;
        string lastValid = "";
        float revealedAt;

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.characterLimit = maxLength;
            field.inputType = TMP_InputField.InputType.Password;
            PasswordDotMask.ApplyTo(field);                   // dots, not asterisks; the last typed character shows for a second
            field.contentType = TMP_InputField.ContentType.Password;
            field.onValueChanged.AddListener(OnChanged);
            if (eyeButton != null) eyeButton.onClick.AddListener(Toggle);
            Apply();
        }

        void Update()
        {
            if (visible && autoHideSeconds > 0 && Time.unscaledTime - revealedAt > autoHideSeconds) Toggle();
            // Block ctrl+V / ctrl+C in the editor and on hardware keyboards.
            if (blockCopyPaste && field.isFocused &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand)) &&
                (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.X)))
            {
                GUIUtility.systemCopyBuffer = "";
            }
        }

        void OnChanged(string v)
        {
            if (blockCopyPaste && v.Length - lastValid.Length > 1) { field.SetTextWithoutNotify(lastValid); return; }
            lastValid = v;
        }

        public void Toggle()
        {
            visible = !visible;
            revealedAt = Time.unscaledTime;
            Apply();
        }

        void Apply()
        {
            field.contentType = visible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
            field.inputType = visible ? TMP_InputField.InputType.Standard : TMP_InputField.InputType.Password;
            field.ForceLabelUpdate();
            if (eyeIconImage != null)
            {
                var s = visible ? eyeOpenIcon : eyeClosedIcon;
                if (s != null) eyeIconImage.sprite = s;
            }
        }
    }
}
