using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Keeps a scroll bar out of sight while the list is at rest. The bar fades in as soon as the user
    /// scrolls or drags the list, and fades out again a moment after they stop.
    /// It sits on the scroll bar itself; the scene builders add it to every scroll view.
    /// In the Editor (scene not playing) the bar stays visible so it can be styled.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ScrollbarAutoHide : MonoBehaviour
    {
        [Tooltip("The list this bar belongs to")]
        [SerializeField] private ScrollRect scroll;
        [Tooltip("Seconds the bar stays after the list stops moving")]
        [SerializeField] private float stayVisibleSeconds = 0.9f;
        [Tooltip("How fast the bar fades in and out")]
        [SerializeField] private float fadeSpeed = 6f;
        [Tooltip("How visible the bar is while the list is at rest (0 = hidden)")]
        [Range(0f, 1f)] [SerializeField] private float restingAlpha = 0f;
        [Tooltip("The list counts as moving above this speed")]
        [SerializeField] private float movingSpeed = 8f;

        CanvasGroup group;
        float lastActive = -100f;
        Vector2 lastPosition;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = restingAlpha;
            if (scroll != null) scroll.onValueChanged.AddListener(OnScrolled);
        }

        void OnEnable() { if (group != null) group.alpha = restingAlpha; lastActive = -100f; }
        void OnDestroy() { if (scroll != null) scroll.onValueChanged.RemoveListener(OnScrolled); }

        void OnScrolled(Vector2 position)
        {
            // the list also "moves" when its content is filled or resized: only a real change of place counts
            if ((position - lastPosition).sqrMagnitude > 0.0000005f && scroll.velocity.sqrMagnitude + (Input.touchCount > 0 || Input.GetMouseButton(0) ? 1000f : 0f) > movingSpeed) lastActive = Time.unscaledTime;
            lastPosition = position;
        }

        void Update()
        {
            if (scroll != null && scroll.velocity.sqrMagnitude > movingSpeed * movingSpeed) lastActive = Time.unscaledTime;
            float target = Time.unscaledTime - lastActive <= stayVisibleSeconds ? 1f : restingAlpha;
            if (!Mathf.Approximately(group.alpha, target)) group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        }
    }
}
