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
    ///  - While they shake, drag a tile to the LEFT. Past the distance below it is removed when let go;
    ///    let go earlier and it springs back.
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

        [Header("Drag to remove")]
        [Tooltip("Dragging this far to the left (canvas units) removes the tile when it is let go")]
        [SerializeField] private float removeDistance = 230f;
        [Tooltip("How much the tile follows the finger up and down (0 = only sideways)")]
        [Range(0f, 1f)] [SerializeField] private float followUpDown = 0.35f;
        [Tooltip("Seconds the tile takes to spring back")]
        [SerializeField] private float returnSeconds = 0.18f;
        [Tooltip("The tile shrinks to this size at the remove distance")]
        [Range(0.5f, 1f)] [SerializeField] private float scaleAtRemove = 0.86f;

        /// <summary>The finger has rested long enough: the tiles should start to shake.</summary>
        public event Action<HoldToRemoveTile> HoldStarted;
        /// <summary>The tile is being dragged; the number is 0 to 1 (1 = far enough to be removed).</summary>
        public event Action<HoldToRemoveTile, float> Dragging;
        /// <summary>The drag ended; true = the tile was let go past the remove distance.</summary>
        public event Action<HoldToRemoveTile, bool> DragEnded;

        public bool Shaking { get; private set; }

        ScrollRect scroll;
        Canvas rootCanvas;
        bool pressed, holdFired, dragging, forwarding, returning;
        float pressedFor, returnT, phase;
        Vector2 pressAt, offset, returnFrom;

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
            pressed = dragging = forwarding = returning = false; Shaking = false;
            ResetVisual();
        }

        float Scale => rootCanvas != null && rootCanvas.scaleFactor > 0.001f ? rootCanvas.scaleFactor : 1f;

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
            dragging = true; returning = false; offset = Vector2.zero;
            SwipeSceneNavigator.Suppressed = true;
            if (visualCanvas != null) { visualCanvas.overrideSorting = true; visualCanvas.sortingOrder = 40; }
        }

        public void OnDrag(PointerEventData e)
        {
            if (forwarding) { if (scroll != null) scroll.OnDrag(e); return; }
            if (!dragging || visual == null) return;
            offset += e.delta / Scale;
            float k = Progress();
            visual.anchoredPosition = new Vector2(Mathf.Min(offset.x, 40f), offset.y * followUpDown);
            visual.localScale = Vector3.one * Mathf.Lerp(1f, scaleAtRemove, k);
            Dragging?.Invoke(this, k);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (forwarding) { forwarding = false; if (scroll != null) scroll.OnEndDrag(e); return; }
            if (!dragging) return;
            dragging = false; pressed = false;
            SwipeSceneNavigator.Suppressed = false;
            bool remove = Progress() >= 1f;
            if (!remove) { returning = true; returnT = 0f; returnFrom = visual != null ? visual.anchoredPosition : Vector2.zero; }
            DragEnded?.Invoke(this, remove);
        }

        float Progress() => Mathf.Clamp01(-offset.x / Mathf.Max(1f, removeDistance));

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
            if (returning)
            {
                returnT = returnSeconds <= 0.01f ? 1f : Mathf.Min(1f, returnT + Time.unscaledDeltaTime / returnSeconds);
                float k = 1f - Mathf.Pow(1f - returnT, 3f);
                visual.anchoredPosition = Vector2.Lerp(returnFrom, Vector2.zero, k);
                visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one, k);
                if (returnT >= 1f) { returning = false; if (visualCanvas != null) visualCanvas.overrideSorting = false; }
            }
            if (Shaking && !dragging)
                visual.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(Time.unscaledTime * shakeSpeed * Mathf.PI * 2f + phase) * shakeDegrees);
            else if (!Shaking && visual.localEulerAngles.z != 0f)
                visual.localEulerAngles = Vector3.zero;
        }
    }
}
