using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The "pixel sweep": small squares light up from the bottom-left corner to the top-right corner and
    /// fade again. It is the same picture as the tile animation on Onboarding Steps 2 and 3, drawn by one
    /// object instead of a hundred, so it can sit on every button. It draws nothing while it rests.
    /// The squares stay inside this object's rounded outline (set Corner Radius to the button's).
    /// PressSweepEffect starts it; its colour is this object's colour.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class PixelSweepGraphic : MaskableGraphic
    {
        [Header("Grid")]
        [Tooltip("Size of one square in canvas units (the grid adapts to the object's size)")]
        [SerializeField] private float cellSize = 26f;
        [Tooltip("Gap between squares, as a share of a square")]
        [Range(0f, 0.5f)] [SerializeField] private float gap = 0.08f;
        [Tooltip("Corner radius of the shape the squares must stay inside. Negative = fully round ends (half the height).")]
        [SerializeField] private float cornerRadius = -1f;
        [Tooltip("Strongest opacity of a square")]
        [Range(0f, 1f)] [SerializeField] private float peakAlpha = 0.8f;

        float time = -1f, sweepSeconds = 0.5f, lifetime = 0.25f;

        public bool IsPlaying => time >= 0f;

        protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }

        /// <summary>Starts one sweep.</summary>
        public void Play(float sweep, float pixelLifetime)
        {
            sweepSeconds = Mathf.Max(0.05f, sweep); lifetime = Mathf.Max(0.05f, pixelLifetime);
            time = 0f;
            SetVerticesDirty();
        }

        public void Stop() { if (time < 0f) return; time = -1f; SetVerticesDirty(); }

        void Update()
        {
            if (time < 0f) return;
            time += Time.unscaledDeltaTime;
            if (time > sweepSeconds + lifetime) time = -1f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (time < 0f) return;
            Rect r = rectTransform.rect;
            if (r.width < 2f || r.height < 2f) return;
            int columns = Mathf.Clamp(Mathf.RoundToInt(r.width / Mathf.Max(4f, cellSize)), 2, 40);
            int rows = Mathf.Clamp(Mathf.RoundToInt(r.height / Mathf.Max(4f, cellSize)), 2, 40);
            float cw = r.width / columns, ch = r.height / rows;
            float radius = cornerRadius < 0f ? Mathf.Min(r.width, r.height) * 0.5f : Mathf.Min(cornerRadius, Mathf.Min(r.width, r.height) * 0.5f);
            Vector2 half = new Vector2(r.width, r.height) * 0.5f, middle = r.center;
            float diagonal = Mathf.Max(1, (columns - 1) + (rows - 1));
            Color32 c = color;

            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float start = (column + row) / diagonal * sweepSeconds;         // bottom-left first, top-right last
                    float local = (time - start) / lifetime;
                    if (local < 0f || local > 1f) continue;

                    var centre = new Vector2(r.xMin + (column + 0.5f) * cw, r.yMin + (row + 0.5f) * ch);
                    // inside the rounded outline?
                    Vector2 q = new Vector2(Mathf.Abs(centre.x - middle.x), Mathf.Abs(centre.y - middle.y)) - (half - new Vector2(radius, radius));
                    if (q.x > 0f && q.y > 0f && q.sqrMagnitude > radius * radius) continue;

                    byte alpha = (byte)Mathf.Clamp(c.a * peakAlpha * (1f - local), 0f, 255f);
                    var colour = new Color32(c.r, c.g, c.b, alpha);
                    float hw = cw * (0.5f - gap * 0.5f), hh = ch * (0.5f - gap * 0.5f);
                    int first = vh.currentVertCount;
                    vh.AddVert(new Vector3(centre.x - hw, centre.y - hh), colour, Vector2.zero);
                    vh.AddVert(new Vector3(centre.x - hw, centre.y + hh), colour, Vector2.zero);
                    vh.AddVert(new Vector3(centre.x + hw, centre.y + hh), colour, Vector2.zero);
                    vh.AddVert(new Vector3(centre.x + hw, centre.y - hh), colour, Vector2.zero);
                    vh.AddTriangle(first, first + 1, first + 2);
                    vh.AddTriangle(first, first + 2, first + 3);
                }
        }
    }
}
