using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// One line of the scanner's checklist ("Product name", "Brand", "Ingredient list" ...).
    /// Until the scanner has found the thing, the line shows an empty ring and the faint label.
    /// When it is found, a gold check mark pops in with a glow and the line shows what was found.
    /// While the app is still looking (the AI is asked), the ring breathes.
    /// </summary>
    public class ScanCheckItem : ThemeAwareBehaviour
    {
        [Header("References")]
        [Tooltip("The empty ring shown until the thing is found")]
        [SerializeField] private Image ring;
        [Tooltip("The gold disc with the check mark")]
        [SerializeField] private RectTransform mark;
        [SerializeField] private Image markDisc;
        [Tooltip("The soft gold light behind the check mark")]
        [SerializeField] private Image glow;
        [SerializeField] private TextMeshProUGUI text;

        [Header("Wording")]
        [Tooltip("What the line says until the thing is found")]
        [SerializeField] private string label = "Product name";

        [Header("Colours (gold and the text colours come from the theme)")]
        [SerializeField] private Color gold = new Color32(184, 138, 59, 255);
        [SerializeField] private Color pendingText = new Color32(139, 123, 122, 255);
        [SerializeField] private Color foundText = new Color32(74, 65, 64, 255);

        [Header("Animation")]
        [Tooltip("Seconds the check mark takes to pop in")]
        [SerializeField] private float popSeconds = 0.32f;
        [Tooltip("How far it overshoots its size (0 = no bounce)")]
        [Range(0f, 3f)] [SerializeField] private float bounce = 1.9f;
        [Tooltip("How strong the glow is at its peak, and how much stays afterwards")]
        [Range(0f, 1f)] [SerializeField] private float glowPeak = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float glowRest = 0.3f;
        [SerializeField] private float glowSeconds = 0.9f;

        public bool Found { get; private set; }
        bool working;
        float t = -1f;

        void Awake() { Paint(); }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            gold = p.gold; pendingText = p.secondaryText; foundText = p.mainText;
            Paint();
        }

        /// <summary>Back to "not found yet".</summary>
        public void SetPending()
        {
            Found = false; working = false; t = -1f;
            if (text != null) text.text = label;
            Paint();
        }

        /// <summary>Found: the gold check pops in and the line shows `value`.</summary>
        public void SetFound(string value)
        {
            if (text != null) text.text = string.IsNullOrWhiteSpace(value) ? label : value.Trim();
            if (Found) { Paint(); return; }
            Found = true; working = false;
            t = isActiveAndEnabled ? 0f : -1f;
            Paint();
        }

        /// <summary>The app is still looking for this (the ring breathes).</summary>
        public void SetWorking(bool on) { working = on && !Found; if (!working && ring != null) ring.rectTransform.localScale = Vector3.one; }

        void Update()
        {
            if (working && ring != null)
            {
                float s = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
                ring.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            if (t < 0f) return;
            t += Time.unscaledDeltaTime;
            float pop = Mathf.Clamp01(t / Mathf.Max(0.01f, popSeconds));
            float c = bounce, c3 = c + 1f;
            float scale = 1f + c3 * Mathf.Pow(pop - 1f, 3f) + c * Mathf.Pow(pop - 1f, 2f);
            if (mark != null) mark.localScale = new Vector3(scale, scale, 1f);
            float g = Mathf.Clamp01(t / Mathf.Max(0.01f, glowSeconds));
            SetGlow(Mathf.Lerp(glowPeak, glowRest, g) * Mathf.Clamp01(pop * 3f));
            if (t >= Mathf.Max(popSeconds, glowSeconds)) { t = -1f; Paint(); }
        }

        void Paint()
        {
            if (ring != null) { ring.enabled = !Found; Color c = pendingText; c.a = 0.7f; ring.color = c; }
            if (mark != null) { mark.gameObject.SetActive(Found); if (t < 0f) mark.localScale = Vector3.one; }
            if (markDisc != null) markDisc.color = gold;
            if (t < 0f) SetGlow(Found ? glowRest : 0f);
            if (text != null) text.color = Found ? foundText : pendingText;
        }

        void SetGlow(float alpha)
        {
            if (glow == null) return;
            Color c = gold; c.a = alpha; glow.color = c;
        }
    }
}
