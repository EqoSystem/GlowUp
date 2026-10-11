using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A light that runs round the edge of a panel, like a glowing light strip: one bright stretch with a
    /// fading tail travels along the outline in a loop. It is drawn in this object's colour (the builder
    /// gives it the same colour as the panel's outline), so no picture is needed. The user can switch it
    /// off on the Settings page ("Edge light"); it also rests in Performance Mode unless allowed below.
    ///
    /// Stretch this object over the panel whose outline it follows and set Corner Radius to that panel's.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class PerimeterLightStrip : MaskableGraphic
    {
        [Header("Path")]
        [Tooltip("Corner radius of the outline it follows, in canvas units")]
        [SerializeField] private float cornerRadius = 64f;
        [Tooltip("Distance from this object's edge to the middle of the light (half the outline's thickness sits well)")]
        [SerializeField] private float inset = 2.5f;

        [Header("Light")]
        [Tooltip("Thickness of the bright line")]
        [SerializeField] private float thickness = 7f;
        [Tooltip("How much the middle of the light is lightened towards white, so it stands out on an outline of the same colour (0 = not at all)")]
        [Range(0f, 1f)] [SerializeField] private float coreWhiten = 0.6f;
        [Tooltip("Width of the soft glow around the line")]
        [SerializeField] private float glowWidth = 30f;
        [Range(0f, 1f)] [SerializeField] private float glowStrength = 0.5f;
        [Tooltip("Length of the lit stretch as a share of the whole outline (0.2 = one fifth)")]
        [Range(0.02f, 1f)] [SerializeField] private float length = 0.22f;
        [Tooltip("Rounds per second (0.1 = once round every ten seconds)")]
        [SerializeField] private float roundsPerSecond = 0.11f;
        [Tooltip("On = clockwise")]
        [SerializeField] private bool clockwise = true;
        [Tooltip("Where the light sits while the scene is not playing (0 to 1 round the outline), so it can be seen in the Editor")]
        [Range(0f, 1f)] [SerializeField] private float editorPosition = 0.08f;

        [Header("Behaviour")]
        [Tooltip("Also run in Performance Mode (off = the light rests there to save the phone work)")]
        [SerializeField] private bool runInPerformanceMode = false;

        float phase;
        bool lit = true;

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            UiPreferences.Changed += Refresh;
            Refresh();
        }

        protected override void OnDisable()
        {
            UiPreferences.Changed -= Refresh;
            base.OnDisable();
        }

        void Refresh()
        {
            lit = !Application.isPlaying || UiPreferences.BorderLight;
            SetVerticesDirty();
        }

        void Update()
        {
            if (!Application.isPlaying || !lit) return;
            if (!runInPerformanceMode && GlowUp.Performance.PerformanceSettings.ReduceEffects) { if (phase != 0f) { phase = 0f; SetVerticesDirty(); } return; }
            phase = Mathf.Repeat(phase + roundsPerSecond * Time.unscaledDeltaTime, 1f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!lit) return;
            if (Application.isPlaying && !runInPerformanceMode && GlowUp.Performance.PerformanceSettings.ReduceEffects) return;

            Rect r = rectTransform.rect;
            float w = r.width - inset * 2f, h = r.height - inset * 2f;
            if (w <= 4f || h <= 4f) return;
            float radius = Mathf.Clamp(cornerRadius - inset, 0f, Mathf.Min(w, h) * 0.5f);
            float straightW = w - 2f * radius, straightH = h - 2f * radius, arc = Mathf.PI * 0.5f * radius;
            float total = 2f * (straightW + straightH) + 4f * arc;
            if (total <= 1f) return;

            float head = (Application.isPlaying ? phase : editorPosition) * total;
            float lit_ = Mathf.Clamp01(length) * total;
            int steps = Mathf.Clamp(Mathf.CeilToInt(lit_ / 14f), 8, 160);

            var centre = new List<Vector2>(steps + 1); var normal = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float s = head - lit_ * (1f - i / (float)steps);       // i = 0 is the end of the tail, i = steps is the head
                Sample(r, radius, straightW, straightH, arc, total, clockwise ? s : -s, out Vector2 p, out Vector2 n);
                centre.Add(p); normal.Add(n);
            }

            Color32 baseColour = color;
            Color32 core = Color.Lerp(color, new Color(1f, 1f, 1f, color.a), coreWhiten);
            if (glowWidth > 0.5f && glowStrength > 0.01f) Strip(vh, centre, normal, glowWidth * 0.5f, baseColour, glowStrength, true);
            Strip(vh, centre, normal, thickness * 0.5f, core, 1f, false);
        }

        // A point `s` units along the outline, starting at the middle of the top edge and going clockwise, with the outward direction there.
        void Sample(Rect r, float radius, float straightW, float straightH, float arc, float total, float s, out Vector2 point, out Vector2 outward)
        {
            s = Mathf.Repeat(s + straightW * 0.5f, total);
            float left = r.xMin + inset, right = r.xMax - inset, bottom = r.yMin + inset, top = r.yMax - inset;
            if (s < straightW) { point = new Vector2(left + radius + s, top); outward = Vector2.up; return; }                     // top edge, left to right
            s -= straightW;
            if (s < arc) { Corner(new Vector2(right - radius, top - radius), radius, 90f - 90f * s / Mathf.Max(0.001f, arc), out point, out outward); return; }
            s -= arc;
            if (s < straightH) { point = new Vector2(right, top - radius - s); outward = Vector2.right; return; }                   // right edge, down
            s -= straightH;
            if (s < arc) { Corner(new Vector2(right - radius, bottom + radius), radius, 0f - 90f * s / Mathf.Max(0.001f, arc), out point, out outward); return; }
            s -= arc;
            if (s < straightW) { point = new Vector2(right - radius - s, bottom); outward = Vector2.down; return; }                 // bottom edge, right to left
            s -= straightW;
            if (s < arc) { Corner(new Vector2(left + radius, bottom + radius), radius, -90f - 90f * s / Mathf.Max(0.001f, arc), out point, out outward); return; }
            s -= arc;
            if (s < straightH) { point = new Vector2(left, bottom + radius + s); outward = Vector2.left; return; }                  // left edge, up
            s -= straightH;
            Corner(new Vector2(left + radius, top - radius), radius, 180f - 90f * s / Mathf.Max(0.001f, arc), out point, out outward);
        }

        static void Corner(Vector2 centre, float radius, float degrees, out Vector2 point, out Vector2 outward)
        {
            float a = degrees * Mathf.Deg2Rad;
            outward = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            point = centre + outward * radius;
        }

        // A band along the path. `soft` = brightest in the middle and fading to nothing at both edges (the glow).
        static void Strip(VertexHelper vh, List<Vector2> centre, List<Vector2> normal, float half, Color32 colour, float strength, bool soft)
        {
            int first = vh.currentVertCount, across = soft ? 3 : 2;
            for (int i = 0; i < centre.Count; i++)
            {
                float along = i / (float)(centre.Count - 1);
                float alpha = along * along * strength;                 // the tail fades out
                if (i == centre.Count - 1) alpha *= 0.55f;              // a slightly softer tip
                byte a = (byte)Mathf.Clamp(colour.a * alpha, 0f, 255f);
                var edge = new Color32(colour.r, colour.g, colour.b, soft ? (byte)0 : a);
                var middle = new Color32(colour.r, colour.g, colour.b, a);
                vh.AddVert(centre[i] + normal[i] * half, edge, Vector2.zero);
                if (soft) vh.AddVert(centre[i], middle, Vector2.zero);
                vh.AddVert(centre[i] - normal[i] * half, edge, Vector2.zero);
            }
            for (int i = 0; i < centre.Count - 1; i++)
                for (int k = 0; k < across - 1; k++)
                {
                    int a0 = first + i * across + k, b0 = a0 + across;
                    vh.AddTriangle(a0, b0, b0 + 1);
                    vh.AddTriangle(a0, b0 + 1, a0 + 1);
                }
        }
    }
}
