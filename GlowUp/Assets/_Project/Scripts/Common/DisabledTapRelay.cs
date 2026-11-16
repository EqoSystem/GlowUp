using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A tap on a button that is greyed out (not interactable) does nothing in Unity. This relay runs `Tapped` for such a tap, so the
    /// page can say which field still needs correcting (New17: validation when the user tries to go on, not while typing).
    /// A tap on the button while it can be used is left to the button itself. Added at run time; no scene has to be rebuilt.
    /// </summary>
    public class DisabledTapRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action Tapped;
        Selectable target;

        public static DisabledTapRelay On(Selectable selectable, Action tapped)
        {
            if (selectable == null) return null;
            if (!selectable.TryGetComponent(out DisabledTapRelay relay)) relay = selectable.gameObject.AddComponent<DisabledTapRelay>();
            relay.target = selectable;
            relay.Tapped = tapped;
            return relay;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (target != null && !target.interactable && target.isActiveAndEnabled) Tapped?.Invoke();
        }
    }
}
