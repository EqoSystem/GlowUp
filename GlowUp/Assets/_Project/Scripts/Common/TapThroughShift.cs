using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A TAP THAT THE SCREEN MOVED UNDER (17 Nov 2026). On a page with a text box, the first tap on a button closes the phone's keyboard,
    /// and the page slides back down (KeyboardAvoider) while the finger is still on the glass. The finger then lifts over another spot, so
    /// the button never gets its click: the tap sound of the press is heard, nothing happens, and only a second tap works
    /// (Sign Up and Log-In on the log-in page, reported on a phone).
    ///
    /// This is put on such a button. When a press on it ends within <see cref="WindowSeconds"/> without the finger having moved
    /// (the page moved, not the finger) and without a click, the button's click is made. A drag or a long press is not a tap
    /// and does nothing. Buttons that guard against a second click (a busy flag, a disabled state) are unaffected: the click is the
    /// button's own onClick, run at most once per press.
    /// </summary>
    public class TapThroughShift : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public const float WindowSeconds = 0.6f;
        public const float MoveLimitPixels = 36f;

        Button button;
        bool armed, lifted;
        float downAt, moved;
        Vector2 downPosition;

        /// <summary>The same rule as plain code (covered by a test): is a press that ended like this a tap that the page moved under?</summary>
        public static bool IsShiftedTap(float seconds, float fingerMovedPixels, bool clicked) =>
            !clicked && seconds >= 0f && seconds <= WindowSeconds && fingerMovedPixels <= MoveLimitPixels;

        /// <summary>Puts the helper on `button` (once).</summary>
        public static void On(Button button)
        {
            if (button != null && !button.TryGetComponent(out TapThroughShift _)) button.gameObject.AddComponent<TapThroughShift>();
        }

        void Awake() { button = GetComponent<Button>(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            armed = true; lifted = false; downAt = Time.unscaledTime; downPosition = eventData.position; moved = 0f;
        }

        public void OnPointerClick(PointerEventData eventData) { armed = false; }       // a normal click: nothing to make up

        void Update()
        {
            if (!armed) return;
            if (Time.unscaledTime - downAt > WindowSeconds) { armed = false; return; }
            bool down = Input.touchCount > 0 || Input.GetMouseButton(0);
            if (down)
            {
                // the finger is still on the glass: remember how far it has gone (a drag is not a tap)
                Vector2 now = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
                moved = Mathf.Max(moved, Vector2.Distance(now, downPosition));
                return;
            }
            // the lift was seen: wait ONE more frame, so a normal click (which disarms this helper) is always handled first
            if (!lifted) { lifted = true; return; }
            armed = false;
            if (IsShiftedTap(Time.unscaledTime - downAt, moved, false) && button != null && button.isActiveAndEnabled && button.interactable)
                button.onClick.Invoke();
        }
    }
}
