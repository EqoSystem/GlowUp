using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The adjustable box over the photo (10 November 2026, like Google Lens): the user drags its corners or the box itself so it covers
    /// the product's text and leaves out what surrounds it. Only the part inside the box is read. While it is read, the lines of text
    /// that were found are outlined inside the box, so the user sees the reader at work.
    ///
    /// Area is in the photo's own 0..1 space, origin at the bottom-left (the same space as OcrRead.Boxes and Texture2D pixels).
    /// </summary>
    public class PhotoCropBox : MonoBehaviour
    {
        const float MinSide = 0.15f;
        const float Handle = 96f;               // touch size of a corner, canvas units (about 32 dp)

        RectTransform photo, box;
        readonly List<Image> shapes = new List<Image>();
        // New13 item 8: everything OUTSIDE the box is veiled (four pieces round the box = an even-odd cut-out), the box edge glows.
        // Phase 1C (11 November 2026): the app's own colours instead of blue: a cream veil outside, a gold edge on a deep rose rim
        // (3:1 or more on light and dark photos), a soft rose glow and gold corner handles.
        readonly RectTransform[] outside = new RectTransform[4];
        public static Color OutsideTint { get { Color c = RuntimeTheme.Palette.background; c.a = 0.62f; return c; } }
        public static Color EdgeLight { get { Color c = RuntimeTheme.Palette.gold; c.a = 1f; return c; } }
        public static readonly Color EdgeRim = new Color32(74, 30, 46, 220);               // deep rose: the gold line reads on a light photo too
        static Color EdgeGlowColor { get { Color c = RuntimeTheme.Palette.accent; c.a = 0.40f; return c; } }
        Rect area = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
        Color line;

        public Rect Area => area;

        /// <summary>Lays the box over `photoRect` (the photo as it is shown).</summary>
        public static PhotoCropBox Over(RectTransform photoRect)
        {
            var crop = photoRect.gameObject.AddComponent<PhotoCropBox>();
            crop.photo = photoRect;
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            crop.line = p.buttonFill;

            // the cream veil outside the box: under the box, over the photo; it takes no touches
            Color veil = OutsideTint;
            for (int i = 0; i < 4; i++)
            {
                Image tint = RuntimeUiKit.Box(photoRect, "OutsideTint" + i, veil, false);
                tint.raycastTarget = false;
                crop.outside[i] = tint.rectTransform;
            }
            crop.box = RuntimeUiKit.Rect("CropBox", photoRect);
            // a soft rose glow just outside the edge, then a deep rose rim and a gold line (readable on light and dark photos)
            Color edge = EdgeLight;
            Image glow = RuntimeUiKit.Box(crop.box, "EdgeGlow", EdgeGlowColor);
            glow.raycastTarget = false; glow.fillCenter = false;
            RuntimeUiKit.Stretch(glow.rectTransform, -10, -10, -10, -10);
            foreach (var (name, min, max, offMin, offMax) in new[]
            {
                ("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(0, 2)),
                ("Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -2), new Vector2(0, 2)),
                ("Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-2, 0), new Vector2(2, 0)),
                ("Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(2, 0))
            })
            {
                Edge(crop.box, name + "Rim", min, max, offMin * 2.5f, offMax * 2.5f, EdgeRim);
                Edge(crop.box, name, min, max, offMin, offMax, edge);
            }
            // the whole box can be moved
            var body = crop.box.gameObject.AddComponent<Image>();
            body.color = new Color(1f, 1f, 1f, 0.001f);
            crop.box.gameObject.AddComponent<CropDrag>().Init(crop, -1);
            // the four corners
            for (int i = 0; i < 4; i++)
            {
                Vector2 at = new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0);      // 0 = bottom-left, 1 = bottom-right, 2 = top-right, 3 = top-left
                RectTransform corner = RuntimeUiKit.Rect("Corner" + i, crop.box);
                corner.anchorMin = corner.anchorMax = at; corner.pivot = new Vector2(0.5f, 0.5f);
                corner.sizeDelta = new Vector2(Handle, Handle);
                var hit = corner.gameObject.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0.001f);
                Image rim = RuntimeUiKit.Icon(corner, "DotRim", RuntimeIcons.Circle, EdgeRim, 42);
                rim.rectTransform.anchoredPosition = Vector2.zero;
                Image dot = RuntimeUiKit.Icon(corner, "Dot", RuntimeIcons.Circle, edge, 34);            // gold handles
                dot.rectTransform.anchoredPosition = Vector2.zero;
                corner.gameObject.AddComponent<CropDrag>().Init(crop, i);
            }
            crop.Apply();
            return crop;
        }

        static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax, Color color)
        {
            Image e = RuntimeUiKit.Box(parent, name, color, false);
            e.raycastTarget = false;
            e.rectTransform.anchorMin = min; e.rectTransform.anchorMax = max;
            e.rectTransform.offsetMin = offMin; e.rectTransform.offsetMax = offMax;
        }

        /// <summary>Back to the default: the middle 80 % of the photo.</summary>
        public void Reset() { area = new Rect(0.1f, 0.1f, 0.8f, 0.8f); ClearShapes(); Apply(); }

        public void SetEditable(bool on)
        {
            foreach (CropDrag d in box.GetComponentsInChildren<CropDrag>(true)) d.enabled = on;
            foreach (Image i in box.GetComponentsInChildren<Image>(true)) if (i.color.a < 0.01f) i.raycastTarget = on;
        }

        void Apply()
        {
            box.anchorMin = new Vector2(area.xMin, area.yMin);
            box.anchorMax = new Vector2(area.xMax, area.yMax);
            box.offsetMin = box.offsetMax = Vector2.zero;
            // the tint: below, above, left of and right of the box (the inside stays clear)
            Rect[] parts = OutsideParts(area);
            for (int i = 0; i < 4; i++)
            {
                if (outside[i] == null) continue;
                outside[i].anchorMin = parts[i].min; outside[i].anchorMax = parts[i].max;
                outside[i].offsetMin = outside[i].offsetMax = Vector2.zero;
            }
        }

        /// <summary>The four pieces of the photo outside `inside` (0..1 space): below, above, left, right. Together with `inside` they cover the photo once.</summary>
        public static Rect[] OutsideParts(Rect inside) => new[]
        {
            Rect.MinMaxRect(0f, 0f, 1f, inside.yMin),
            Rect.MinMaxRect(0f, inside.yMax, 1f, 1f),
            Rect.MinMaxRect(0f, inside.yMin, inside.xMin, inside.yMax),
            Rect.MinMaxRect(inside.xMax, inside.yMin, 1f, inside.yMax)
        };

        /// <summary>Outlines the text lines found: `found` is in the space of the CUT photo (0..1 of the box).</summary>
        public void ShowShapes(Rect[] found)
        {
            ClearShapes();
            if (found == null) return;
            Color c = line; c.a = 0.9f;
            foreach (Rect r in found)
            {
                RectTransform shape = RuntimeUiKit.Rect("TextShape", box);
                shape.anchorMin = new Vector2(Mathf.Clamp01(r.xMin), Mathf.Clamp01(r.yMin));
                shape.anchorMax = new Vector2(Mathf.Clamp01(r.xMax), Mathf.Clamp01(r.yMax));
                shape.offsetMin = shape.offsetMax = Vector2.zero;
                var outline = shape.gameObject.AddComponent<Image>();
                outline.sprite = RuntimeIcons.RoundedBox; outline.type = Image.Type.Sliced; outline.fillCenter = false;
                outline.color = c; outline.raycastTarget = false;
                shapes.Add(outline);
            }
        }

        public void ClearShapes()
        {
            foreach (Image s in shapes) if (s != null) Destroy(s.gameObject);
            shapes.Clear();
        }

        /// <summary>A drag on a corner (0..3) or on the box (-1), as a point in the photo's 0..1 space.</summary>
        internal void Drag(int corner, PointerEventData e, ref Vector2 lastPoint)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(photo, e.position, e.pressEventCamera, out Vector2 local)) return;
            Rect pr = photo.rect;
            Vector2 p = new Vector2(Mathf.Clamp01((local.x - pr.xMin) / Mathf.Max(1f, pr.width)), Mathf.Clamp01((local.y - pr.yMin) / Mathf.Max(1f, pr.height)));
            if (corner < 0)
            {
                Vector2 move = p - lastPoint;
                lastPoint = p;
                float x = Mathf.Clamp(area.x + move.x, 0f, 1f - area.width), y = Mathf.Clamp(area.y + move.y, 0f, 1f - area.height);
                area = new Rect(x, y, area.width, area.height);
            }
            else
            {
                float x0 = area.xMin, y0 = area.yMin, x1 = area.xMax, y1 = area.yMax;
                if (corner == 0 || corner == 3) x0 = Mathf.Min(p.x, x1 - MinSide); else x1 = Mathf.Max(p.x, x0 + MinSide);
                if (corner == 0 || corner == 1) y0 = Mathf.Min(p.y, y1 - MinSide); else y1 = Mathf.Max(p.y, y0 + MinSide);
                area = Rect.MinMaxRect(Mathf.Clamp01(x0), Mathf.Clamp01(y0), Mathf.Clamp01(x1), Mathf.Clamp01(y1));
            }
            ClearShapes();
            Apply();
        }

        internal Vector2 PointOf(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(photo, e.position, e.pressEventCamera, out Vector2 local)) return area.center;
            Rect pr = photo.rect;
            return new Vector2(Mathf.Clamp01((local.x - pr.xMin) / Mathf.Max(1f, pr.width)), Mathf.Clamp01((local.y - pr.yMin) / Mathf.Max(1f, pr.height)));
        }
    }

    /// <summary>Hands the drags of one corner (or of the whole box) to its PhotoCropBox.</summary>
    public class CropDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        PhotoCropBox owner;
        int corner;
        Vector2 last;

        public void Init(PhotoCropBox box, int which) { owner = box; corner = which; }
        public void OnBeginDrag(PointerEventData e) { if (enabled && owner != null) last = owner.PointOf(e); }
        public void OnDrag(PointerEventData e) { if (enabled && owner != null) owner.Drag(corner, e, ref last); }
    }
}
