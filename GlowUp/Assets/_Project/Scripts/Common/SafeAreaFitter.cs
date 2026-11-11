using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// SAFE AREA (phase 1D, 11 November 2026): keeps a screen's card clear of the phone's notch, status bar and gesture bar.
    ///
    /// The card is stretched over the canvas with a margin (40 canvas units). On a phone whose notch or navigation bar is deeper
    /// than that margin, the edge that touches it moves in by the difference, so the bottom bar's pill and labels and the header
    /// never sit under the phone's own bars. On a phone without such bars nothing changes. Only the card's offsets are touched;
    /// its children follow through their anchors. Re-measured when the safe area or the screen size changes.
    /// Added at run time by BottomActionBar (every screen with the bottom bar), so no scene needs rebuilding.
    /// </summary>
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Room kept between the phone's bar and the card (canvas units)")]
        [SerializeField] private float extraGap = 8f;

        RectTransform rect;
        Vector2 baseMin, baseMax;
        Rect lastSafe;
        Vector2Int lastScreen;
        bool ready;

        /// <summary>Adds the fitter to `card` once.</summary>
        public static SafeAreaFitter On(RectTransform card)
        {
            if (card == null) return null;
            var fitter = card.GetComponent<SafeAreaFitter>();
            return fitter != null ? fitter : card.gameObject.AddComponent<SafeAreaFitter>();
        }

        void Awake()
        {
            rect = (RectTransform)transform;
            baseMin = rect.offsetMin; baseMax = rect.offsetMax;
            ready = true;
        }

        void OnEnable() { lastScreen = Vector2Int.zero; Fit(); }

        void Update()
        {
            if (Screen.safeArea != lastSafe || lastScreen.x != Screen.width || lastScreen.y != Screen.height) Fit();
        }

        void Fit()
        {
            if (!ready) return;
            Rect safe = Screen.safeArea;
            lastSafe = safe;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.rootCanvas.scaleFactor > 0f ? canvas.rootCanvas.scaleFactor : 1f;
            float bottomInset = Mathf.Max(0f, safe.yMin) / scale;
            float topInset = Mathf.Max(0f, Screen.height - safe.yMax) / scale;
            float bottom = bottomInset > 0f ? Mathf.Max(baseMin.y, bottomInset + extraGap) : baseMin.y;
            float top = topInset > 0f ? Mathf.Max(-baseMax.y, topInset + extraGap) : -baseMax.y;
            rect.offsetMin = new Vector2(baseMin.x, bottom);
            rect.offsetMax = new Vector2(baseMax.x, -top);
        }
    }
}
