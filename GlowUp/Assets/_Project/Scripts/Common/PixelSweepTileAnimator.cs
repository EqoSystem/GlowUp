using System.Collections;
using System.Collections.Generic;
using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Tile selection animation: a diagonal pixelation sweep from bottom-left to top-right,
    /// plus a glowing neon outline. The sweep is a grid of small cells created when the scene
    /// starts (their count comes from Columns x Rows below), so it needs no shader and every
    /// value is editable in the Inspector.
    /// </summary>
    public class PixelSweepTileAnimator : ThemeAwareBehaviour
    {
        [Header("References")]
        [Tooltip("Empty stretched object inside the tile; the pixel cells are created under it")]
        [SerializeField] private RectTransform cellContainer;
        [SerializeField] private Image neonOutline;

        [Header("Pixel Grid")]
        [SerializeField] private int columns = 10;
        [SerializeField] private int rows = 10;
        [SerializeField] private Color pixelColor = new Color(1f, 0.85f, 0.9f, 0.8f);

        [Header("Timing")]
        [SerializeField] private float sweepSeconds = 0.55f;
        [Tooltip("How long each pixel stays visible after the sweep reaches it")]
        [SerializeField] private float pixelLifetime = 0.25f;

        [Header("Neon outline")]
        [SerializeField] private Color neonColor = new Color32(217, 142, 160, 255);
        [SerializeField] private float pulseSpeed = 3f;
        [Range(0f, 1f)] [SerializeField] private float pulseDepth = 0.25f;
        [SerializeField] private float outlineFadeSeconds = 0.2f;

        readonly List<Image> cells = new List<Image>();
        bool selected;
        float outlineK;

        void Awake()
        {
            BuildGrid();
            if (neonOutline != null) { var c = neonColor; c.a = 0; neonOutline.color = c; }
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            neonColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            var px = Color.Lerp(p.accent, Color.white, 0.55f); px.a = 0.8f;
            pixelColor = px;
        }

        void BuildGrid()
        {
            if (cellContainer == null) return;
            foreach (Transform t in cellContainer) Destroy(t.gameObject);
            cells.Clear();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    var go = new GameObject($"Pixel_{c}_{r}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(cellContainer, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = new Vector2((float)c / columns, (float)r / rows);   // row 0 = bottom
                    rt.anchorMax = new Vector2((float)(c + 1) / columns, (float)(r + 1) / rows);
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    var img = go.GetComponent<Image>();
                    img.raycastTarget = false;
                    var col = pixelColor; col.a = 0; img.color = col;
                    cells.Add(img);
                }
        }

        bool calm;
        /// <summary>Calm: no square sweep, and a steady (not pulsing) outline while selected.</summary>
        public void Calm() { calm = true; pulseDepth = 0f; outlineFadeSeconds = Mathf.Min(outlineFadeSeconds, 0.15f); if (cellContainer != null) cellContainer.gameObject.SetActive(false); }

        public void SetSelected(bool value)
        {
            selected = value;
            if (calm) return;
            // Performance Mode: the pixel sweep is skipped (the outline still shows the choice)
            if (value && isActiveAndEnabled && !GlowUp.Performance.PerformanceSettings.ReduceEffects) { StopAllCoroutines(); StartCoroutine(Sweep()); }
        }

        IEnumerator Sweep()
        {
            float t = 0, total = sweepSeconds + pixelLifetime;
            float diagMax = Mathf.Max(1, (columns - 1) + (rows - 1));
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < cells.Count; i++)
                {
                    int c = i % columns, r = i / columns;
                    float start = ((c + r) / diagMax) * sweepSeconds;   // bottom-left first, top-right last
                    float local = (t - start) / Mathf.Max(0.01f, pixelLifetime);
                    var col = pixelColor;
                    col.a = (local < 0 || local > 1) ? 0 : pixelColor.a * (1 - local);
                    cells[i].color = col;
                }
                yield return null;
            }
            foreach (var cell in cells) { var col = pixelColor; col.a = 0; cell.color = col; }
        }

        void Update()
        {
            if (neonOutline == null) return;
            outlineK = Mathf.MoveTowards(outlineK, selected ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, outlineFadeSeconds));
            var c = neonColor;
            float pulse = selected && !GlowUp.Performance.PerformanceSettings.ReduceEffects ? 1f - pulseDepth + pulseDepth * Mathf.Sin(Time.unscaledTime * pulseSpeed) : 1f;
            c.a = neonColor.a * outlineK * pulse;
            neonOutline.color = c;
        }
    }
}
