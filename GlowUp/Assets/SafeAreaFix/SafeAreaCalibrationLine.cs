using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// The draggable horizontal line of the safe-area debug overlay: the tester drags it to the bottom edge of the camera hole they SEE on the
    /// phone (the hole is not in screenshots). Y is in screen pixels from the top.
    /// </summary>
    public class SafeAreaCalibrationLine : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        RectTransform line;
        public float YFromTopPx { get; private set; } = 120f;
        public System.Action Moved;

        public void Init(RectTransform lineRect, float startYFromTop)
        {
            line = lineRect;
            SetY(startYFromTop);
        }

        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData) => SetY(Screen.height - eventData.position.y);

        public void SetY(float yFromTop)
        {
            YFromTopPx = Mathf.Clamp(yFromTop, 0f, Screen.height);
            if (line != null) line.anchoredPosition = new Vector2(0f, -YFromTopPx);
            Moved?.Invoke();
        }
    }
}
