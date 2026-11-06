using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A row that scrolls sideways (the shade swatches in AR Try-On) inside a list that scrolls up and down. A drag that is mostly
    /// sideways moves the row; a drag that is mostly up or down is handed to the list around it, so the page still scrolls when the
    /// finger starts on a swatch. A mouse wheel also goes to the list. Taps still reach the swatches.
    /// </summary>
    public class SideScrollRect : ScrollRect
    {
        ScrollRect outer;
        bool handOver;

        ScrollRect Outer()
        {
            if (outer == null && transform.parent != null) outer = transform.parent.GetComponentInParent<ScrollRect>();
            return outer;
        }

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (Outer() != null) outer.OnInitializePotentialDrag(eventData);
            base.OnInitializePotentialDrag(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            handOver = Mathf.Abs(eventData.delta.y) > Mathf.Abs(eventData.delta.x);
            if (handOver && Outer() != null) outer.OnBeginDrag(eventData);
            else base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (handOver && Outer() != null) outer.OnDrag(eventData);
            else base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (handOver && Outer() != null) outer.OnEndDrag(eventData);
            else base.OnEndDrag(eventData);
            handOver = false;
        }

        public override void OnScroll(PointerEventData data)
        {
            if (Outer() != null) outer.OnScroll(data);
            else base.OnScroll(data);
        }
    }
}
