using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Responsive guideline §9 (16 November 2026): every tappable control gets at least a 48 dp touch area (144 units on the
    /// 1080-wide canvas), without changing how it looks or what it does. A control smaller than that receives an invisible
    /// "TouchArea" child, centred on it; taps on it reach the control's own Button / Toggle / input field as before.
    /// The area only grows as far as it can without covering another control, so neighbours in a row or grid keep their own
    /// taps (calendar days, chips and shade swatches that sit close together stay as they are). Runs on every scene save.
    /// </summary>
    public static class TouchTargets
    {
        public const float Minimum = 144f;     // 48 dp at 3 units per dp
        const string Name = "TouchArea";

        public static int Apply(Scene scene)
        {
            var controls = new List<Selectable>();
            foreach (GameObject root in scene.GetRootGameObjects()) controls.AddRange(root.GetComponentsInChildren<Selectable>(true));
            int added = 0;
            foreach (Selectable control in controls)
            {
                var rt = control.transform as RectTransform;
                if (rt == null || rt.Find(Name) != null) continue;
                if (control.GetComponentsInChildren<Selectable>(true).Length > 1) continue;   // has its own tappable children: leave them their taps
                Canvas canvas = control.GetComponentInParent<Canvas>(true);
                if (canvas == null) continue;
                Vector2 size = rt.rect.size;
                if (size.x <= 1f || size.y <= 1f) continue;                                   // sized by a layout at run time: nothing to measure
                float toCanvas = rt.lossyScale.x / Mathf.Max(1e-6f, canvas.transform.lossyScale.x);
                Vector2 inCanvas = size * toCanvas;
                if (inCanvas.x >= Minimum && inCanvas.y >= Minimum) continue;

                Vector2 want = new Vector2(Mathf.Max(inCanvas.x, Minimum), Mathf.Max(inCanvas.y, Minimum));
                Vector2 grow = Fit(rt, control, controls, inCanvas, want, canvas);
                if (grow.x <= inCanvas.x + 0.5f && grow.y <= inCanvas.y + 0.5f) continue;

                var pad = new GameObject(Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
                pad.layer = control.gameObject.layer;
                var prt = (RectTransform)pad.transform;
                prt.SetParent(rt, false);
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = rt.rect.center;
                prt.sizeDelta = grow / toCanvas;
                var image = pad.GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = true;
                pad.GetComponent<LayoutElement>().ignoreLayout = true;
                prt.SetAsLastSibling();
                added++;
            }
            return added;
        }

        /// <summary>The largest area up to `want` (canvas units) that covers no other control the original rect did not already touch.</summary>
        static Vector2 Fit(RectTransform rt, Selectable self, List<Selectable> all, Vector2 have, Vector2 want, Canvas canvas)
        {
            Rect own = CanvasRect(rt, canvas);
            var others = new List<Rect>();
            foreach (Selectable other in all)
            {
                if (other == self || !other.gameObject.activeInHierarchy && self.gameObject.activeInHierarchy) continue;
                if (other.transform.IsChildOf(self.transform) || self.transform.IsChildOf(other.transform)) continue;
                if (other.GetComponentInParent<Canvas>(true) != canvas) continue;
                var ort = other.transform as RectTransform;
                if (ort == null || ort.rect.width <= 1f || ort.rect.height <= 1f) continue;
                Rect r = CanvasRect(ort, canvas);
                if (!r.Overlaps(own)) others.Add(r);
            }
            for (float t = 1f; t > 0.05f; t -= 0.125f)
            {
                Vector2 size = Vector2.Lerp(have, want, t);
                var area = new Rect(own.center - size * 0.5f, size);
                bool clear = true;
                foreach (Rect r in others) if (r.Overlaps(area)) { clear = false; break; }
                if (clear) return size;
            }
            return have;
        }

        static Rect CanvasRect(RectTransform rt, Canvas canvas)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Transform c = canvas.transform;
            Vector3 a = c.InverseTransformPoint(corners[0]), b = c.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }
}
