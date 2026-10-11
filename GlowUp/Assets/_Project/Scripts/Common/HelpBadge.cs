using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The small round "i" next to an input field. The reminder for that field ("Up to 50 characters",
    /// "Tap the eye to check it" ...) is no longer printed under the field: tapping the badge shows it in
    /// a small bubble, which goes away by itself after a few seconds or when the user taps anywhere else.
    /// Write the reminder in Message below; the bubble is the inactive child "HelpBubble".
    /// </summary>
    public class HelpBadge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button badgeButton;
        [Tooltip("The bubble (inactive until the badge is tapped)")]
        [SerializeField] private GameObject bubble;
        [SerializeField] private TextMeshProUGUI bubbleText;
        [SerializeField] private CanvasGroup bubbleGroup;

        [Header("Content")]
        [TextArea(2, 4)]
        [SerializeField] private string message = "";

        [Header("Behaviour")]
        [Tooltip("Seconds the bubble stays open")]
        [SerializeField] private float showSeconds = 4f;
        [SerializeField] private float fadeSeconds = 0.14f;
        [Tooltip("Drawing order of the bubble: high enough to sit above the fields under it")]
        [SerializeField] private int bubbleSortingOrder = 30;

        static HelpBadge openBadge;
        float openFor = -1f, alpha;

        /// <summary>The reminder shown in the bubble.</summary>
        public string Message
        {
            get => message;
            set { message = value ?? ""; if (bubbleText != null) bubbleText.text = message; }
        }

        void Awake()
        {
            if (badgeButton != null) badgeButton.onClick.AddListener(Toggle);
            if (bubbleText != null && !string.IsNullOrEmpty(message)) bubbleText.text = message;
            if (bubble != null) bubble.SetActive(false);
        }

        void OnDisable() { Close(true); }

        void Toggle()
        {
            if (openBadge == this) { Close(false); return; }
            if (openBadge != null) openBadge.Close(true);
            if (bubble == null) return;
            openBadge = this; openFor = 0f; alpha = 0f;
            bubble.SetActive(true);
            if (bubble.TryGetComponent(out Canvas layer)) { layer.overrideSorting = true; layer.sortingOrder = bubbleSortingOrder; }
            if (bubbleGroup != null) bubbleGroup.alpha = 0f;
        }

        void Close(bool atOnce)
        {
            if (openBadge == this) openBadge = null;
            openFor = -1f;
            if (bubble != null && (atOnce || bubbleGroup == null)) bubble.SetActive(false);
        }

        void Update()
        {
            if (bubble == null || !bubble.activeSelf) return;
            if (openFor < 0f)                                   // closing: fade out, then hide
            {
                alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                if (bubbleGroup != null) bubbleGroup.alpha = alpha;
                if (alpha <= 0f) bubble.SetActive(false);
                return;
            }
            openFor += Time.unscaledDeltaTime;
            alpha = Mathf.MoveTowards(alpha, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (bubbleGroup != null) bubbleGroup.alpha = alpha;
            if (openFor > showSeconds) { Close(false); return; }
            // a tap anywhere else closes it (a tap on the badge itself is handled by Toggle)
            if (openFor > 0.1f && Input.GetMouseButtonDown(0) && !PointerIsOnBadge()) Close(false);
        }

        bool PointerIsOnBadge()
        {
            if (badgeButton == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)badgeButton.transform, Input.mousePosition, null);
        }
    }
}
