using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// "Press and hold, then drag away" for a tile, the way app icons are removed on a phone's home screen.
    ///
    ///  - Press and hold a tile: all tiles start to shake (the controller switches the others on).
    ///  - While they shake, a tile can be dragged ANYWHERE on the screen. It stays exactly under the finger: the point
    ///    that was touched stays under the finger, nothing is smoothed or delayed, and the tile is drawn above the list
    ///    and everything else (it is not cut off at the list's edge).
    ///  - Let go while the FINGER is inside the remove area (the shadow the controller shows): the tile is removed.
    ///    Let go anywhere else: it glides back from the exact spot where it was let go.
    ///  - A drag that starts before the hold time is an ordinary scroll and is passed on to the list.
    ///
    /// The tile itself stays in its place in the grid; only its "Visual" child shakes and moves.
    /// The controller listens to the events and decides what "removed" means.
    /// </summary>
    public class HoldToRemoveTile : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("References")]
        [Tooltip("The part of the tile that shakes and follows the finger (a child holding everything visible)")]
        [SerializeField] private RectTransform visual;
        [Tooltip("On the Visual: lets the dragged tile be drawn above its neighbours")]
        [SerializeField] private Canvas visualCanvas;

        [Header("Hold")]
        [Tooltip("Seconds the finger must rest on the tile before the tiles start to shake")]
        [SerializeField] private float holdSeconds = 0.45f;
        [Tooltip("The finger may wander this far during the hold (screen pixels)")]
        [SerializeField] private float holdTolerance = 22f;

        [Header("Shake")]
        [Tooltip("How far the tile tilts each way, in degrees")]
        [SerializeField] private float shakeDegrees = 1.8f;
        [Tooltip("Tilts per second")]
        [SerializeField] private float shakeSpeed = 9f;

        [Tooltip("On = the tiles do not tilt in Performance Mode (reduced effects); the pencils and the Done button still show the mode")]
        [SerializeField] private bool stillInPerformanceMode = true;

        [Header("Drag to remove")]
        [Tooltip("Only used when the controller gives no remove area: dragging this far to the left (canvas units) removes the tile")]
        [SerializeField] private float removeDistance = 230f;
        [Tooltip("Seconds the tile takes to glide back")]
        [SerializeField] private float returnSeconds = 0.18f;
        [Tooltip("The tile shrinks to this size while the finger is inside the remove area (it shrinks around the finger)")]
        [Range(0.5f, 1f)] [SerializeField] private float scaleAtRemove = 0.86f;
        [Tooltip("How fast it shrinks and grows back (per second)")]
        [SerializeField] private float scaleSpeed = 9f;

        [Header("Gathered into a group")]
        [Tooltip("Seconds a selected row takes to fly to the finger when its group is picked up")]
        [SerializeField] private float gatherSeconds = 0.22f;
        [Tooltip("How much a gathered row is turned, in degrees, so the pile looks like a pile")]
        [SerializeField] private float gatherTilt = 2.5f;

        /// <summary>The finger has rested long enough: the tiles should start to shake.</summary>
        public event Action<HoldToRemoveTile> HoldStarted;
        /// <summary>The tile is being dragged; the number is 1 while the finger is inside the remove area and 0 while it is not.</summary>
        public event Action<HoldToRemoveTile, float> Dragging;
        /// <summary>The drag ended; true = the tile was let go with the finger inside the remove area.</summary>
        public event Action<HoldToRemoveTile, bool> DragEnded;
        /// <summary>22 Nov 2026: while dragged, true = the finger is inside the Try On area (the right side of the screen).</summary>
        public event Action<HoldToRemoveTile, bool> OverTryOn;
        /// <summary>22 Nov 2026: let go with the finger inside the Try On area: the controller opens AR Try-On (this tile, or its whole group).</summary>
        public event Action<HoldToRemoveTile> DroppedOnTryOn;

        public bool Shaking { get; private set; }

        ScrollRect scroll;
        Canvas rootCanvas;
        RectTransform removeArea, tryOnArea;
        bool removeEnabled = true, insideTryOn;
        bool pressed, holdFired, dragging, forwarding, returning, inside;
        float pressedFor, returnT, phase, shrink = 1f;
        Vector2 pressAt, fingerNow, grab, returnFrom;
        Vector3 returnScale = Vector3.one;

        RectTransform leader;           // the Visual of the row under the finger, while this row is gathered behind it
        Vector2 gatherOffset;
        float gatherT, gatherTurn;

        /// <summary>True while this row lies in the pile behind the row that is carried.</summary>
        public bool Gathered => leader != null;

        /// <summary>
        /// A group is picked up: this row flies to the row under the finger (`lead`) and then stays in the pile behind
        /// it, `offset` away (in the leader's own units), wherever on the screen the finger goes. `order` = its layer.
        /// </summary>
        public void GatherBehind(HoldToRemoveTile lead, Vector2 offset, int order, float turn)
        {
            if (visual == null || lead == null || lead == this || lead.visual == null) return;
            leader = lead.visual; gatherOffset = offset; gatherT = 0f; gatherTurn = turn; returning = false;
            if (visualCanvas != null)
            {
                visualCanvas.overrideSorting = true; visualCanvas.sortingOrder = order;
                foreach (MaskableGraphic graphic in visual.GetComponentsInChildren<MaskableGraphic>(true)) graphic.RecalculateClipping();
            }
        }

        /// <summary>The group was let go outside the remove area: this row glides back to its place in the list.</summary>
        public void LeavePile()
        {
            if (leader == null) return;
            leader = null;
            if (visual == null) return;
            returning = true; returnT = 0f; returnFrom = visual.anchoredPosition; returnScale = visual.localScale;
        }

        /// <summary>The area in which letting go removes the tile (the controller's shadow). "Inside" is decided by the finger.</summary>
        public void SetRemoveArea(RectTransform area) { removeArea = area; }
        /// <summary>The area in which letting go opens AR Try-On (the controller's right-side zone). Null = no try-on drop.</summary>
        public void SetTryOnArea(RectTransform area) { tryOnArea = area; }
        /// <summary>Off = a drag never removes (the dashboard's recommendations: there is nothing to remove there).</summary>
        public void SetRemoveEnabled(bool on) { removeEnabled = on; }

        void Awake()
        {
            scroll = GetComponentInParent<ScrollRect>();
            Canvas c = GetComponentInParent<Canvas>();
            rootCanvas = c != null ? c.rootCanvas : null;
            phase = UnityEngine.Random.value * 6.28f;           // tiles do not all tilt in step
        }

        void OnDisable()
        {
            if (dragging) SwipeSceneNavigator.Suppressed = false;
            pressed = dragging = forwarding = returning = false; Shaking = false; leader = null;
            ResetVisual();
        }

        Camera Eye => rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;

        /// <summary>A place on the screen, as a place in the tile's own space (the Visual's parent). The tile does not move while it is dragged.</summary>
        Vector2 InTile(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)visual.parent, screenPoint, Eye, out Vector2 local);
            return local;
        }

        /// <summary>The controller switches the shaking of every tile on and off.</summary>
        public void SetShaking(bool on)
        {
            Shaking = on;
            if (!on && !dragging && !returning) ResetVisual();
        }

        void ResetVisual()
        {
            if (visual == null) return;
            visual.localEulerAngles = Vector3.zero; visual.anchoredPosition = Vector2.zero; visual.localScale = Vector3.one;
            if (visualCanvas != null) visualCanvas.overrideSorting = false;
        }

        // ---------------------------------------------------------------------------------------
        // Called the moment a finger lands on the tile or on anything inside it (the tile's own buttons included),
        // which is why the hold is timed from here and not from a pointer-down event (a button would swallow that).
        public void OnInitializePotentialDrag(PointerEventData e)
        {
            pressed = true; holdFired = false; pressedFor = 0f; pressAt = e.position;
            if (scroll != null) scroll.OnInitializePotentialDrag(e);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!Shaking)
            {
                // an ordinary scroll: the list handles it
                forwarding = true; pressed = false;
                if (scroll != null) scroll.OnBeginDrag(e);
                return;
            }
            if (visual == null) return;
            dragging = true; returning = false; inside = false;
            SwipeSceneNavigator.Suppressed = true;
            // where the finger holds the tile: kept for the whole drag, so the tile never snaps its middle or a corner to the finger
            fingerNow = e.pressPosition;
            grab = (Vector2)visual.localPosition - InTile(e.pressPosition);
            shrink = visual.localScale.x;
            // a top layer of its own: above the list, the bars and the shadow, and not cut off at the list's edge
            if (visualCanvas != null)
            {
                visualCanvas.overrideSorting = true; visualCanvas.sortingOrder = 40;
                foreach (MaskableGraphic graphic in visual.GetComponentsInChildren<MaskableGraphic>(true)) graphic.RecalculateClipping();
            }
            Follow(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (forwarding) { if (scroll != null) scroll.OnDrag(e); return; }
            if (!dragging || visual == null) return;
            Follow(e.position);
        }

        /// <summary>Puts the tile under the finger (in screen coordinates, on every move) and says whether the finger is in the remove area.</summary>
        void Follow(Vector2 finger)
        {
            fingerNow = finger;
            bool overTryOn = tryOnArea != null && tryOnArea.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(tryOnArea, finger, Eye);
            inside = !overTryOn && removeEnabled && InRemoveArea(finger);
            if (overTryOn != insideTryOn) { insideTryOn = overTryOn; OverTryOn?.Invoke(this, overTryOn); }
            Place();
            Dragging?.Invoke(this, inside ? 1f : 0f);
        }

        /// <summary>The touched point of the tile stays under the finger, also while the tile shrinks or grows.</summary>
        void Place()
        {
            Vector2 at = InTile(fingerNow) + grab * shrink;
            visual.localPosition = new Vector3(at.x, at.y, visual.localPosition.z);
            visual.localScale = Vector3.one * shrink;
        }

        bool InRemoveArea(Vector2 finger)
        {
            if (removeArea != null) return removeArea.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(removeArea, finger, Eye);
            // no area given (an older scene): as before, far enough to the left
            return InTile(finger).x - InTile(pressAt).x <= -Mathf.Max(1f, removeDistance);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (forwarding) { forwarding = false; if (scroll != null) scroll.OnEndDrag(e); return; }
            if (!dragging) return;
            if (visual != null) Follow(e.position);
            dragging = false; pressed = false;
            SwipeSceneNavigator.Suppressed = false;
            bool remove = inside;
            bool tryOn = insideTryOn && !remove;
            if (insideTryOn) { insideTryOn = false; OverTryOn?.Invoke(this, false); }
            // not removed: it glides back from exactly where it was let go (also when it opens AR Try-On: the list is as it was on return)
            if (!remove && visual != null) { returning = true; returnT = 0f; returnFrom = visual.anchoredPosition; returnScale = visual.localScale; }
            DragEnded?.Invoke(this, remove);
            if (tryOn) DroppedOnTryOn?.Invoke(this);
        }

        void Update()
        {
            // the hold: the finger rests on the tile without moving away
            if (pressed && !Input.GetMouseButton(0) && !dragging) pressed = false;       // the finger was lifted
            if (pressed && !holdFired && !Shaking && !forwarding)
            {
                pressedFor += Time.unscaledDeltaTime;
                if (((Vector2)Input.mousePosition - pressAt).sqrMagnitude > holdTolerance * holdTolerance) pressed = false;
                else if (pressedFor >= holdSeconds) { holdFired = true; HoldStarted?.Invoke(this); }
            }

            if (visual == null) return;
            if (leader != null)
            {
                // in the pile: fly to the carried row, then stay with it (same size, a little turned)
                gatherT = gatherSeconds <= 0.01f ? 1f : Mathf.Min(1f, gatherT + Time.unscaledDeltaTime / gatherSeconds);
                Vector3 there = visual.parent.InverseTransformPoint(leader.TransformPoint(gatherOffset));
                there.z = visual.localPosition.z;
                float k = 1f - Mathf.Pow(1f - gatherT, 3f);
                visual.localPosition = gatherT >= 1f ? there : Vector3.Lerp(visual.localPosition, there, k);
                visual.localScale = Vector3.Lerp(visual.localScale, leader.localScale, k);
                visual.localEulerAngles = new Vector3(0f, 0f, gatherTurn * gatherTilt * k);
                return;
            }
            if (dragging)
            {
                // smaller while the finger is in the remove area, back to full size outside it; the finger keeps its place on the tile
                float wanted = inside || insideTryOn ? scaleAtRemove : 1f;
                if (!Mathf.Approximately(shrink, wanted)) { shrink = Mathf.MoveTowards(shrink, wanted, Mathf.Max(0.1f, scaleSpeed) * Time.unscaledDeltaTime); Place(); }
                return;                 // the tilt it had when it was picked up is kept, so nothing jumps
            }
            if (returning)
            {
                returnT = returnSeconds <= 0.01f ? 1f : Mathf.Min(1f, returnT + Time.unscaledDeltaTime / returnSeconds);
                float k = 1f - Mathf.Pow(1f - returnT, 3f);
                visual.anchoredPosition = Vector2.Lerp(returnFrom, Vector2.zero, k);
                visual.localScale = Vector3.Lerp(returnScale, Vector3.one, k);
                if (returnT >= 1f)
                {
                    returning = false;
                    if (visualCanvas != null)
                    {
                        visualCanvas.overrideSorting = false;
                        foreach (MaskableGraphic graphic in visual.GetComponentsInChildren<MaskableGraphic>(true)) graphic.RecalculateClipping();
                    }
                }
            }
            bool tilt = Shaking && !(stillInPerformanceMode && GlowUp.Performance.PerformanceSettings.ReduceEffects);
            if (tilt)
                visual.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(Time.unscaledTime * shakeSpeed * Mathf.PI * 2f + phase) * shakeDegrees);
            else if (visual.localEulerAngles.z != 0f)
                visual.localEulerAngles = Vector3.zero;
        }
    }
}
