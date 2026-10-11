using System.Collections;
using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Rejects spaces in an input field (email, password) and tells the user straight away.
    /// Also enforces the maximum length (default 64). The message appears in the assigned text
    /// and fades after a few seconds. Everything is editable in the Inspector.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class InputSpaceGuard : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private string message = "Spaces are not allowed here.";
        [SerializeField] private int maxLength = 64;
        [SerializeField] private float messageSeconds = 3f;
        [SerializeField] private Color messageColor = new Color32(181, 71, 47, 255);

        TMP_InputField field;
        Coroutine hide;

        void Awake()
        {
            field = GetComponent<TMP_InputField>();
            field.characterLimit = maxLength;
            field.onValueChanged.AddListener(OnChanged);
        }

        void OnChanged(string value)
        {
            if (value.IndexOf(' ') < 0 && value.IndexOf(' ') < 0) return;
            string clean = value.Replace(" ", "").Replace(" ", "");
            int caret = field.caretPosition;
            field.SetTextWithoutNotify(clean);
            field.caretPosition = Mathf.Min(caret, clean.Length);
            field.onValueChanged.Invoke(clean);        // let other listeners validate the cleaned text
            Show();
        }

        void Show()
        {
            if (messageText == null) return;
            messageText.text = message;
            messageText.color = messageColor;
            if (hide != null) StopCoroutine(hide);
            hide = StartCoroutine(Hide());
        }

        IEnumerator Hide()
        {
            yield return new WaitForSecondsRealtime(messageSeconds);
            if (messageText != null && messageText.text == message) messageText.text = "";
        }
    }
}
