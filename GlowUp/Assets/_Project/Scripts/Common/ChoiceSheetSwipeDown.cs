using UnityEngine;
using UnityEngine.EventSystems;

namespace GlowUp.Common
{
    /// <summary>Drag the sheet down by more than 200 units (or flick it) and it closes; less and it springs back. Sits on the sheet's Card.</summary>
    public class ChoiceSheetSwipeDown : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ChoiceSheet sheet;
        float startY, startTime;

        public void OnBeginDrag(PointerEventData e) { startY = e.position.y; startTime = Time.unscaledTime; }

        public void OnDrag(PointerEventData e)
        {
            if (sheet == null) return;
            sheet.SetCardOffset(Mathf.Max(0f, startY - e.position.y));
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (sheet == null) return;
            float down = startY - e.position.y, speed = down / Mathf.Max(0.01f, Time.unscaledTime - startTime);
            if (down > 200f || (down > 60f && speed > 1500f)) sheet.Hide();
            sheet.SetCardOffset(0f);
        }
    }
}
