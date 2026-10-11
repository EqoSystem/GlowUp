using System;
using GlowUp.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// The "before and after" view of AR Try-On. The compare button splits the camera picture in two:
    /// the left side shows the face as it is (before), the right side shows it with the colour (after).
    /// The line between them can be dragged left and right.
    ///
    /// How it works: the colour is drawn inside "After Clip" and the plain picture of live tracking
    /// inside "Before Clip"; this script only moves the edge of those two clipping areas.
    /// </summary>
    public class BeforeAfterSplit : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("References")]
        [Tooltip("The camera view (the split is measured across it)")]
        [SerializeField] private RectTransform area;
        [Tooltip("Clips everything that shows the colour to the right of the line")]
        [SerializeField] private RectMask2D afterClip;
        [Tooltip("Clips the plain picture (live tracking) to the left of the line")]
        [SerializeField] private RectMask2D beforeClip;
        [Tooltip("The line, its handle and the words Before / After (hidden while the split is off)")]
        [SerializeField] private GameObject visuals;
        [Tooltip("The line with its handle: it is moved to where the split is")]
        [SerializeField] private RectTransform divider;
        [SerializeField] private Button toggleButton;
        [Tooltip("The button's icon: clear when the split is on, faint when it is off")]
        [SerializeField] private Graphic toggleIcon;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenOff = 0.45f;

        [Header("Behaviour")]
        [Tooltip("On = the picture is split as soon as the screen opens")]
        [SerializeField] private bool startOn = false;
        [Tooltip("Where the line starts (0 = left edge, 1 = right edge)")]
        [Range(0.1f, 0.9f)] [SerializeField] private float startAt = 0.5f;
        [Tooltip("The line cannot be dragged closer to an edge than this (canvas units)")]
        [SerializeField] private float edgeMargin = 70f;

        public bool IsOn { get; private set; }
        /// <summary>Raised when the split is switched on or off.</summary>
        public event Action<bool> Changed;

        float split;

        void Awake()
        {
            split = startAt;
            if (toggleButton != null) toggleButton.onClick.AddListener(() => SetOn(!IsOn));
            IsOn = startOn;
            Apply();
        }

        void OnDisable() { SwipeSceneNavigator.Suppressed = false; }
        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Apply(); }

        public void SetOn(bool on)
        {
            if (IsOn == on) return;
            IsOn = on;
            Apply();
            Changed?.Invoke(on);
        }

        public void OnBeginDrag(PointerEventData e) { if (IsOn) SwipeSceneNavigator.Suppressed = true; }
        public void OnEndDrag(PointerEventData e) { SwipeSceneNavigator.Suppressed = false; }

        public void OnDrag(PointerEventData e)
        {
            if (!IsOn || area == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out Vector2 local)) return;
            Rect r = area.rect;
            float x = Mathf.Clamp(local.x, r.xMin + edgeMargin, r.xMax - edgeMargin);
            split = Mathf.InverseLerp(r.xMin, r.xMax, x);
            Apply();
        }

        void Apply()
        {
            float width = area != null ? area.rect.width : 0f;
            float x = width * split;
            // RectMask2D padding: left, bottom, right, top
            if (afterClip != null) afterClip.padding = new Vector4(IsOn ? x : 0f, 0f, 0f, 0f);
            if (beforeClip != null) { beforeClip.padding = new Vector4(0f, 0f, width - x, 0f); beforeClip.gameObject.SetActive(IsOn); }
            if (visuals != null) visuals.SetActive(IsOn);
            if (divider != null) divider.anchoredPosition = new Vector2(x, divider.anchoredPosition.y);
            if (toggleIcon != null) { Color c = toggleIcon.color; c.a = IsOn ? 1f : iconAlphaWhenOff; toggleIcon.color = c; }
        }
    }
}
