using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Common
{
    /// <summary>
    /// The name of an icon-only button, said when the button is held for half a second (New13 item 1): a toast at the top with the
    /// button's name ("Take a photo", "Back to live scan"). A hold does not press the button. Made at run time.
    /// </summary>
    public class LongPressHint : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public string words = "";
        public TMP_Text fontFrom;
        const float HoldSeconds = 0.5f;
        float downAt = -1f;
        bool said;

        /// <summary>True for the tap that ended a hold: the button's click should be ignored once.</summary>
        public bool ConsumeHold() { bool was = said; said = false; return was; }

        public static LongPressHint On(GameObject button, string words, TMP_Text fontFrom)
        {
            if (button == null) return null;
            if (!button.TryGetComponent(out LongPressHint hint)) hint = button.AddComponent<LongPressHint>();
            hint.words = words; hint.fontFrom = fontFrom;
            return hint;
        }

        public void OnPointerDown(PointerEventData e) { downAt = Time.unscaledTime; said = false; }
        public void OnPointerUp(PointerEventData e) { downAt = -1f; }
        public void OnPointerExit(PointerEventData e) { downAt = -1f; }

        void Update()
        {
            if (downAt < 0f || said || Time.unscaledTime - downAt < HoldSeconds) return;
            said = true;
            FormalToast toast = FormalToast.OnScreen(transform, fontFrom);
            if (toast != null) toast.Show(FormalToast.Kind.Info, words);
        }
    }
}
