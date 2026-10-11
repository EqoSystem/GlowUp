using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Tactile press feedback for ordinary buttons: the button shrinks slightly while pressed and
    /// springs back with a small overshoot (scale punch) on release. Tune it in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ButtonPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Scale while the finger is down")]
        [SerializeField] private float pressedScale = 0.95f;
        [Tooltip("Peak scale of the spring-back after release")]
        [SerializeField] private float releaseOvershoot = 1.05f;
        [SerializeField] private float pressSeconds = 0.07f;
        [SerializeField] private float releaseSeconds = 0.24f;

        Button button;
        Vector3 baseScale = Vector3.one;
        bool initialised, pressed;
        float pressK;
        float releaseT = 1f;

        void Awake() { button = GetComponent<Button>(); baseScale = transform.localScale; initialised = true; }

        void OnDisable()
        {
            if (initialised) transform.localScale = baseScale;
            pressed = false; pressK = 0f; releaseT = 1f;
        }

        public void OnPointerDown(PointerEventData eventData) { if (button != null && button.interactable) pressed = true; }
        public void OnPointerUp(PointerEventData eventData) { Release(); }
        public void OnPointerExit(PointerEventData eventData) { Release(); }

        void Release()
        {
            if (!pressed) return;
            pressed = false;
            releaseT = 0f;
        }

        void Update()
        {
            float target = pressed ? 1f : 0f;
            if (Mathf.Approximately(pressK, target) && releaseT >= 1f) return;

            float seconds = pressed ? pressSeconds : releaseSeconds * 0.4f;
            pressK = Mathf.MoveTowards(pressK, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
            float punch = 0f;
            if (releaseT < 1f)
            {
                releaseT = Mathf.Min(1f, releaseT + Time.unscaledDeltaTime / Mathf.Max(0.01f, releaseSeconds));
                punch = Mathf.Sin(releaseT * Mathf.PI) * (releaseOvershoot - 1f);
            }
            transform.localScale = baseScale * (Mathf.Lerp(1f, pressedScale, pressK) + punch);
        }
    }
}
