using GlowUp.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The ring round the scan button: five segments, one per zone (front, ingredients, barcode, shade, other sides).
    /// A segment fills as its zone is read and turns gold with a small pulse when it is complete. What each segment
    /// stands for is said by the hint on the camera picture; this object only draws.
    ///
    /// Each segment is two Images that use a ring picture and Unity's "Filled, Radial 360" drawing: a faint Track (the
    /// empty segment) and a Fill on top of it. SetUp() places them round the ring; the scene builder calls it, and so
    /// does this script when the app runs (so a segment moved by hand in the Inspector snaps back into place).
    /// </summary>
    public class ScanRingView : ThemeAwareBehaviour
    {
        [Tooltip("The five faint segments (the empty ring), in the order front, ingredients, barcode, shade, other sides")]
        [SerializeField] private Image[] tracks = new Image[ScanZones.Count];
        [Tooltip("The five segments that fill, in the same order")]
        [SerializeField] private Image[] fills = new Image[ScanZones.Count];

        [Header("Shape")]
        [Tooltip("The gap between two segments, in degrees")]
        [Range(0f, 30f)] [SerializeField] private float gapDegrees = 9f;

        [Header("Colours (the accent and gold come from the theme)")]
        [SerializeField] private Color trackColor = new Color(0.55f, 0.5f, 0.5f, 0.35f);
        [SerializeField] private Color fillingColor = new Color32(217, 142, 160, 255);
        [SerializeField] private Color fullColor = new Color32(184, 138, 59, 255);

        [Header("Movement")]
        [Tooltip("How fast a segment grows towards its new value (shares of a segment per second)")]
        [SerializeField] private float fillSpeed = 2.5f;
        [Tooltip("A segment swells to this size for a moment when it becomes full")]
        [SerializeField] private float pulseScale = 1.12f;
        [SerializeField] private float pulseSeconds = 0.35f;

        readonly float[] target = new float[ScanZones.Count];
        readonly float[] shown = new float[ScanZones.Count];
        readonly float[] pulse = new float[ScanZones.Count];

        float SegmentShare => Mathf.Max(0.01f, (360f / ScanZones.Count - gapDegrees) / 360f);

        void Awake() { SetUp(); Paint(true); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            fillingColor = new Color(p.accent.r, p.accent.g, p.accent.b, 1f);
            fullColor = new Color(p.gold.r, p.gold.g, p.gold.b, 1f);
            Paint(true);
        }

        /// <summary>Places the ten Images round the ring and sets how they are drawn.</summary>
        public void SetUp()
        {
            for (int i = 0; i < ScanZones.Count; i++)
            {
                float start = 360f / ScanZones.Count * i + gapDegrees * 0.5f;       // degrees clockwise from the top
                foreach (Image image in new[] { i < tracks.Length ? tracks[i] : null, i < fills.Length ? fills[i] : null })
                {
                    if (image == null) continue;
                    image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Radial360;
                    image.fillOrigin = (int)Image.Origin360.Top; image.fillClockwise = true;
                    image.raycastTarget = false;
                    image.rectTransform.localEulerAngles = new Vector3(0f, 0f, -start);
                }
                if (i < tracks.Length && tracks[i] != null) { tracks[i].fillAmount = SegmentShare; tracks[i].color = trackColor; }
            }
        }

        /// <summary>How full a zone's segment should be, 0 to 1. It grows there smoothly.</summary>
        public void Set(ScanZone zone, float progress)
        {
            int i = (int)zone;
            bool wasFull = target[i] >= 1f;
            target[i] = Mathf.Clamp01(progress);
            if (!wasFull && target[i] >= 1f) pulse[i] = 1f;
        }

        /// <summary>Shows what a ring holds.</summary>
        public void Show(ScanEvidence evidence)
        {
            for (int i = 0; i < ScanZones.Count; i++) Set((ScanZone)i, evidence != null ? evidence.Progress((ScanZone)i) : 0f);
        }

        /// <summary>Empties the ring at once (a new scan).</summary>
        public void Clear()
        {
            for (int i = 0; i < ScanZones.Count; i++) { target[i] = shown[i] = pulse[i] = 0f; }
            Paint(true);
        }

        void Update()
        {
            bool changed = false;
            for (int i = 0; i < ScanZones.Count; i++)
            {
                if (!Mathf.Approximately(shown[i], target[i]))
                {
                    shown[i] = GlowUp.Performance.PerformanceSettings.ReduceEffects ? target[i] : Mathf.MoveTowards(shown[i], target[i], fillSpeed * Time.unscaledDeltaTime);
                    changed = true;
                }
                if (pulse[i] > 0f) { pulse[i] = Mathf.Max(0f, pulse[i] - Time.unscaledDeltaTime / Mathf.Max(0.05f, pulseSeconds)); changed = true; }
            }
            if (changed) Paint(false);
        }

        void Paint(bool everything)
        {
            for (int i = 0; i < ScanZones.Count && i < fills.Length; i++)
            {
                Image fill = fills[i];
                if (fill == null) continue;
                fill.fillAmount = SegmentShare * shown[i];
                fill.color = shown[i] >= 1f ? fullColor : fillingColor;
                float swell = GlowUp.Performance.PerformanceSettings.ReduceEffects ? 1f : 1f + (pulseScale - 1f) * Mathf.Sin(pulse[i] * Mathf.PI);
                fill.rectTransform.localScale = Vector3.one * swell;
                if (everything && i < tracks.Length && tracks[i] != null) tracks[i].color = trackColor;
            }
        }
    }
}
