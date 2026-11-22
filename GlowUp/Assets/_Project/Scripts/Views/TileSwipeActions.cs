using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// Drag a row sideways to act on it (panel revision, 22 Nov 2026): the row follows the finger and shows a hint behind it.
    ///   drag LEFT  = the left action  (Recommended for you: save it; Recent Scans: remove it, the Restore bar can bring it back)
    ///   drag RIGHT = the right action (try the product on in AR)
    /// Far enough (Commit Fraction of the row's width) and the action runs; less and the row glides back. An up-or-down drag is
    /// handed to the list around it, so the page still scrolls. While a row is open, shaking or the action is off nothing happens.
    /// Everything here is a field: the hint objects (LeftHint, RightHint) are in the row in the Hierarchy; reword or recolour them there.
    /// </summary>
    public class TileSwipeActions : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IOwnsSidewaysDrag
    {
        [Tooltip("The part of the row that follows the finger (the row's 'Visual')")]
        [SerializeField] private RectTransform visual;
        [Tooltip("Behind the row, uncovered when it is dragged to the LEFT (the left action's words and colour)")]
        [SerializeField] private CanvasGroup leftHint;
        [Tooltip("Behind the row, uncovered when it is dragged to the RIGHT (the right action's words and colour)")]
        [SerializeField] private CanvasGroup rightHint;
        [Range(0.1f, 0.8f)] [SerializeField] private float commitFraction = 0.32f;
        [Range(0.2f, 0.9f)] [SerializeField] private float maxFraction = 0.5f;
        [SerializeField] private float returnSeconds = 0.18f;
        [Tooltip("A drag counts as sideways when it is at least this many times longer across than up or down")]
        [SerializeField] private float sidewaysRatio = 1.2f;
        [SerializeField] private bool leftActionOn = true;
        [SerializeField] private bool rightActionOn = true;

        /// <summary>Dragged left / right far enough. The row has already glided back; the owner does what the action means.</summary>
        public event Action<TileSwipeActions> SwipedLeft, SwipedRight;

        ScrollRect scroll;
        HoldToRemoveTile hold;
        ProductTileView tile;
        bool sideways, forwarding, returning;
        float offset, returnFrom, returnT;
        Vector2 home;

        public void SetActions(bool left, bool right) { leftActionOn = left; rightActionOn = right; }

        void Awake()
        {
            scroll = GetComponentInParent<ScrollRect>();
            hold = GetComponent<HoldToRemoveTile>();
            tile = GetComponent<ProductTileView>();
            if (visual != null) home = visual.anchoredPosition;
            ShowHints(0f);
        }

        void OnDisable()
        {
            if (sideways) SwipeSceneNavigator.Suppressed = false;
            sideways = forwarding = returning = false;
            Reset();
        }

        bool Blocked => (hold != null && hold.Shaking) || (tile != null && tile.DrawerOpen) || visual == null;

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (hold == null && scroll != null) scroll.OnInitializePotentialDrag(e);        // a row of Recent Scans hands this on itself (HoldToRemoveTile)
        }

        public void OnBeginDrag(PointerEventData e)
        {
            sideways = !Blocked && Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) * sidewaysRatio;
            if (sideways) { SwipeSceneNavigator.Suppressed = true; returning = false; offset = visual.anchoredPosition.x - home.x; return; }
            if (hold == null && scroll != null) { forwarding = true; scroll.OnBeginDrag(e); }       // mostly up or down: the list scrolls
        }

        public void OnDrag(PointerEventData e)
        {
            if (forwarding) { scroll.OnDrag(e); return; }
            if (!sideways) return;
            float scale = visual.lossyScale.x > 0.0001f ? visual.lossyScale.x : 1f;
            float width = ((RectTransform)visual.parent).rect.width;
            offset += e.delta.x / scale;
            float max = width * maxFraction;
            float low = leftActionOn ? -max : 0f, high = rightActionOn ? max : 0f;
            offset = Mathf.Clamp(offset, low, high);
            visual.anchoredPosition = new Vector2(home.x + offset, home.y);
            ShowHints(width > 1f ? offset / (width * commitFraction) : 0f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (forwarding) { forwarding = false; scroll.OnEndDrag(e); return; }
            if (!sideways) return;
            sideways = false;
            SwipeSceneNavigator.Suppressed = false;
            float width = ((RectTransform)visual.parent).rect.width;
            float commit = width * commitFraction;
            bool left = offset <= -commit && leftActionOn, right = offset >= commit && rightActionOn;
            returning = true; returnT = 0f; returnFrom = offset;
            if (left) SwipedLeft?.Invoke(this);
            else if (right) SwipedRight?.Invoke(this);
        }

        void Update()
        {
            if (!returning || visual == null) return;
            returnT += Time.unscaledDeltaTime / Mathf.Max(0.05f, returnSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnT));
            offset = Mathf.Lerp(returnFrom, 0f, k);
            visual.anchoredPosition = new Vector2(home.x + offset, home.y);
            ShowHints(offset / Mathf.Max(1f, ((RectTransform)visual.parent).rect.width * commitFraction));
            if (returnT >= 1f) { returning = false; Reset(); }
        }

        void Reset()
        {
            offset = 0f;
            if (visual != null) visual.anchoredPosition = home;
            ShowHints(0f);
        }

        /// <summary>amount: -1 = the left action is just reached, +1 = the right action is just reached.</summary>
        void ShowHints(float amount)
        {
            if (leftHint != null) { leftHint.alpha = Mathf.Clamp01(-amount); leftHint.gameObject.SetActive(amount < -0.02f); }
            if (rightHint != null) { rightHint.alpha = Mathf.Clamp01(amount); rightHint.gameObject.SetActive(amount > 0.02f); }
        }
    }
}
