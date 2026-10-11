using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Put on a message text (for example the error line on the login page): whatever is written into
    /// it stays for a few seconds, fades out and is cleared. The script that writes the message does not
    /// need to know about this.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class AutoHideText : MonoBehaviour
    {
        [Tooltip("How long a message stays fully visible (seconds)")]
        [SerializeField] private float visibleSeconds = 3f;
        [Tooltip("How long the fade-out takes (seconds)")]
        [SerializeField] private float fadeSeconds = 0.35f;

        TMP_Text label;
        string shown = "";
        float timer, lastAlpha = 1f;

        void Awake() { label = GetComponent<TMP_Text>(); }
        void OnEnable() { shown = label != null ? label.text : ""; timer = 0f; }

        void LateUpdate()
        {
            if (label == null) return;
            string now = label.text ?? "";
            // a new message, or the same message written again while it was fading (its colour was reset): start over
            if (now != shown || (timer > visibleSeconds && label.alpha > lastAlpha + 0.02f))
            {
                shown = now; timer = 0f;
                if (label.alpha < 0.99f) label.alpha = 1f;
            }
            if (shown.Length == 0) return;

            timer += Time.unscaledDeltaTime;
            if (timer <= visibleSeconds) return;
            float k = fadeSeconds <= 0.01f ? 1f : Mathf.Clamp01((timer - visibleSeconds) / fadeSeconds);
            if (k >= 1f)
            {
                label.text = ""; shown = "";
                label.alpha = 1f; lastAlpha = 1f;
                return;
            }
            label.alpha = lastAlpha = 1f - k;
        }
    }
}
