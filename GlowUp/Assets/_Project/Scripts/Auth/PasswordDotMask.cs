using TMPro;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// Every password box in the app shows dots (•), not asterisks, and the character just typed stays readable for about
    /// one second before it turns into a dot too (as on Android's own keyboards and on Facebook). Only a picture is laid over
    /// the box for that second: the field's real value, caret and selection are never touched, so nothing typed is lost.
    /// The reveal is skipped when the text is longer than the box (the field scrolls then) and while the eye shows the password.
    /// Added at run time by <see cref="PasswordFieldToggle"/> and the Settings delete form: no scene has to be rebuilt.
    /// The password is never logged, stored or sent anywhere by this component.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class PasswordDotMask : MonoBehaviour
    {
        public const char Dot = '•';

        [Tooltip("How long the last typed character stays readable (seconds). 0 = never shown.")]
        [SerializeField] private float revealSeconds = 1f;

        TMP_InputField field;
        TextMeshProUGUI overlay;
        int lastLength;
        float hideAt = -1f;
        float shownAlpha = 1f;

        /// <summary>Makes a password field show dots. Safe to call more than once.</summary>
        public static void ApplyTo(TMP_InputField field)
        {
            if (field == null) return;
            field.asteriskChar = Dot;
            if (!field.TryGetComponent(out PasswordDotMask _)) field.gameObject.AddComponent<PasswordDotMask>();
        }

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.asteriskChar = Dot;
            lastLength = (field.text ?? "").Length;
            field.onValueChanged.AddListener(OnChanged);
            field.onDeselect.AddListener(_ => Hide());
        }

        void OnDisable() { Hide(); }

        void Update()
        {
            if (hideAt > 0f && Time.unscaledTime >= hideAt) Hide();
        }

        bool Masked => field != null && field.inputType == TMP_InputField.InputType.Password;

        void OnChanged(string value)
        {
            value = value ?? "";
            bool oneMoreAtEnd = value.Length == lastLength + 1 && field.stringPosition >= value.Length;
            lastLength = value.Length;
            if (!oneMoreAtEnd || !Masked || revealSeconds <= 0f) { Hide(); return; }
            TMP_Text real = field.textComponent;
            if (real == null) return;
            if (overlay == null) Build(real);
            overlay.font = real.font; overlay.fontSize = real.fontSize; overlay.color = real.color;
            overlay.alignment = real.alignment; overlay.fontStyle = real.fontStyle;
            overlay.text = new string(Dot, value.Length - 1) + value[value.Length - 1];
            overlay.ForceMeshUpdate();
            // a text longer than the box scrolls inside the field: the picture could stand in the wrong place, so no reveal then
            if (overlay.preferredWidth > real.rectTransform.rect.width) { Hide(); return; }
            // never remember 0: PasswordLengthMask may have hidden the text already, and restoring 0 would leave the box blank for good
            if (hideAt < 0f) shownAlpha = real.alpha > 0.01f ? real.alpha : 1f;
            real.alpha = 0f;
            overlay.gameObject.SetActive(true);
            hideAt = Time.unscaledTime + revealSeconds;
        }

        void Hide()
        {
            if (hideAt < 0f) return;
            hideAt = -1f;
            if (overlay != null) overlay.gameObject.SetActive(false);
            if (field != null && field.textComponent != null) field.textComponent.alpha = shownAlpha;
        }

        void Build(TMP_Text real)
        {
            var go = new GameObject("LastCharacter", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(real.transform.parent, false);
            var rect = (RectTransform)go.transform;
            var from = real.rectTransform;
            rect.anchorMin = from.anchorMin; rect.anchorMax = from.anchorMax; rect.pivot = from.pivot;
            rect.offsetMin = from.offsetMin; rect.offsetMax = from.offsetMax;       // exactly over the real text, inside the field's text area
            overlay = go.GetComponent<TextMeshProUGUI>();
            overlay.enableWordWrapping = false;
            overlay.overflowMode = TextOverflowModes.Masking;
            overlay.raycastTarget = false;                                            // takes no touches: the field and the eye keep working
            overlay.margin = real.margin;
            go.SetActive(false);
        }
    }
}
