using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// Shows what the TEXT TRACER of hold-to-scan is following: corner marks round the block of text, moving with it.
    /// While the block is small print (the text reader gets close looks at it, enlarged) the marks change colour and a
    /// short label says so. The marks fade when the tracer has let go of the block.
    ///
    /// This object lies over the scan frame. In the Hierarchy the box is shown at a sample place so it can be styled;
    /// when the app runs it is hidden until text is followed. SmartScanFlowController tells it where the block is.
    /// </summary>
    public class TextTraceView : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The corner marks (a child of this object; this script moves and sizes it)")]
        [SerializeField] private RectTransform box;
        [SerializeField] private Graphic boxGraphic;
        [Tooltip("The small label on the box. Optional.")]
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("The dark pill behind the label. Optional.")]
        [SerializeField] private Graphic labelBackground;

        [Header("Look")]
        [SerializeField] private Color followingColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("The colour while the text reader gets close looks at this block")]
        [SerializeField] private Color closeLookColor = new Color32(226, 184, 96, 255);
        [SerializeField] private string followingLabel = "Text";
        [SerializeField] private string closeLookLabel = "Small print: reading it enlarged";
        [Tooltip("How quickly the marks move to where the text is (higher = quicker)")]
        [SerializeField] private float followSpeed = 14f;
        [Tooltip("Seconds the marks stay after the last word from the tracer")]
        [SerializeField] private float holdSeconds = 1.0f;
        [SerializeField] private float fadeSeconds = 0.3f;

        /// <summary>The box round the followed text (the camera can be told to focus on it).</summary>
        public RectTransform Box => box;
        /// <summary>True while a block is shown.</summary>
        public bool IsShowing => shownFor >= 0f && shownFor < holdSeconds;

        Vector2 wantedMin, wantedMax;
        float shownFor = -1f, alpha;
        float labelAlpha = 1f, labelBackAlpha = 0.6f;
        bool placed;

        void Awake()
        {
            if (label != null) labelAlpha = label.color.a;
            if (labelBackground != null) labelBackAlpha = labelBackground.color.a;
            Clear();
        }

        /// <summary>
        /// `following` = a block of text is followed; `block` = where it is, 0 to 1 of the scan frame from its top-left
        /// corner; `closeLooks` = it is small print that is read enlarged.
        /// </summary>
        public void Show(bool following, Rect block, bool closeLooks)
        {
            if (box == null) return;
            if (!following) { if (shownFor >= 0f) shownFor = Mathf.Max(shownFor, holdSeconds); return; }       // it fades from now
            wantedMin = new Vector2(Mathf.Clamp01(block.xMin), Mathf.Clamp01(1f - block.yMax));
            wantedMax = new Vector2(Mathf.Clamp01(block.xMax), Mathf.Clamp01(1f - block.yMin));
            if (!placed) { box.anchorMin = wantedMin; box.anchorMax = wantedMax; placed = true; }
            box.offsetMin = box.offsetMax = Vector2.zero;
            if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
            shownFor = 0f;

            Color c = closeLooks ? closeLookColor : followingColor;
            tint = new Color(c.r, c.g, c.b, 1f); fullAlpha = c.a;
            if (label != null) label.text = closeLooks ? closeLookLabel : followingLabel;
        }
        Color tint = Color.white; float fullAlpha = 1f;

        /// <summary>Takes the marks away at once (a new scan, the camera went off).</summary>
        public void Clear()
        {
            shownFor = -1f; alpha = 0f; placed = false;
            if (box != null) box.gameObject.SetActive(false);
        }

        void Update()
        {
            if (shownFor < 0f || box == null) return;
            shownFor += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-Mathf.Max(1f, followSpeed) * Time.unscaledDeltaTime);
            box.anchorMin = Vector2.Lerp(box.anchorMin, wantedMin, k);
            box.anchorMax = Vector2.Lerp(box.anchorMax, wantedMax, k);

            float wanted = shownFor < holdSeconds ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, wanted, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (boxGraphic != null) boxGraphic.color = new Color(tint.r, tint.g, tint.b, fullAlpha * alpha);
            if (label != null) { Color c = label.color; c.a = labelAlpha * alpha; label.color = c; }
            if (labelBackground != null) { Color c = labelBackground.color; c.a = labelBackAlpha * alpha; labelBackground.color = c; }
            if (alpha <= 0.001f && wanted <= 0f) Clear();
        }
    }
}
