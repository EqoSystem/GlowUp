using GlowUp.Performance;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>The pink scan line that sweeps up and down inside the camera frame. Off when "Scan Line" is off in Scanner Preferences,
    /// and still (centred) when the phone is set to reduced effects.</summary>
    public class ScanLineSweep : MonoBehaviour
    {
        [SerializeField] private RectTransform line;
        [SerializeField] private RectTransform area;
        [SerializeField] private float margin = 40f;
        [SerializeField] private float cycleSeconds = 2.4f;

        bool allowed = true;

        /// <summary>The capture screen shows the line only for the barcode mode.</summary>
        public bool Allowed { get => allowed; set { allowed = value; Apply(); } }

        void OnEnable() { ScannerPrefs.Changed += Apply; Apply(); }
        void OnDisable() { ScannerPrefs.Changed -= Apply; }

        void Apply() { if (line != null) line.gameObject.SetActive(allowed && ScannerPrefs.ScanLine); }

        void Update()
        {
            if (line == null || area == null || !allowed || !ScannerPrefs.ScanLine) return;
            float h = Mathf.Max(0f, area.rect.height - 2 * margin);
            float k = PerformanceSettings.ReduceEffects ? 0.5f : Mathf.PingPong(Time.unscaledTime / cycleSeconds, 1f);
            line.anchoredPosition = new Vector2(0f, h * (0.5f - k));
        }
    }
}
