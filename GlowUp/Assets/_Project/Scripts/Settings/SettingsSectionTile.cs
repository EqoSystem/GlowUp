using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// One group on the Settings page, shown as a long rounded tile (icon, title, one short line).
    /// Tapping the tile slides its settings open underneath; tapping again closes them. Opening one
    /// group closes the others, so the page stays short.
    ///
    /// Opening is one smooth change of state: the tile grows a little, a glowing outline in the accent
    /// colour fades in round it, its icon and title slide to the right, and the icon plays its own
    /// small animation (IconDisc > SettingsIconAnimator). Closing runs the same way back.
    ///
    /// To see and style a group's settings in the Editor, tick "Start Open" here: the group opens in
    /// the Scene view straight away (untick it again when done).
    /// </summary>
    [ExecuteAlways]
    public class SettingsSectionTile : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The long tile the user taps")]
        [SerializeField] private Button tileButton;
        [Tooltip("The arrow at the tile's right end (it turns when the group opens)")]
        [SerializeField] private RectTransform arrow;
        [Tooltip("The object under the tile whose height grows and shrinks (it has the LayoutElement)")]
        [SerializeField] private LayoutElement body;
        [SerializeField] private CanvasGroup bodyGroup;
        [Tooltip("The panel with the settings inside the body (its own height is the body's full height)")]
        [SerializeField] private RectTransform panel;
        [Tooltip("The icon's animation, played when the group opens")]
        [SerializeField] private SettingsIconAnimator iconAnimator;
        [Tooltip("The page's scroll list: it follows the group while it opens. Optional.")]
        [SerializeField] private ScrollRect scroll;

        [Header("The open tile")]
        [Tooltip("The tile itself: it grows a little while its group is open")]
        [SerializeField] private RectTransform tileVisual;
        [Tooltip("The glowing outline round the tile (a CanvasGroup; invisible while the group is shut)")]
        [SerializeField] private CanvasGroup openGlow;
        [Tooltip("The row inside the tile (icon, texts, arrow): its left padding grows so the icon and title slide right")]
        [SerializeField] private HorizontalLayoutGroup line;
        [Tooltip("Size of the tile while open (1 = no change)")]
        [SerializeField] private float openScale = 1.02f;
        [Tooltip("How far the icon and title slide to the right (canvas units)")]
        [SerializeField] private int slideDistance = 18;
        [Tooltip("How strong the glow is while open, and how deep it breathes")]
        [Range(0f, 1f)] [SerializeField] private float glowAlpha = 0.9f;
        [Range(0f, 0.6f)] [SerializeField] private float glowBreath = 0.18f;

        [Header("Behaviour")]
        [Tooltip("On = this group is open when the page appears (also opens it in the Editor's Scene view)")]
        [SerializeField] private bool startOpen = false;
        [Tooltip("On = opening this group closes the other groups")]
        [SerializeField] private bool closeOthers = true;
        [Tooltip("On = the page scrolls so the opened tile sits near the top")]
        [SerializeField] private bool scrollToTile = true;

        [Header("Animation")]
        [Tooltip("Seconds the settings take to slide open or shut")]
        [SerializeField] private float slideSeconds = 0.28f;
        [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("Space between the tile and its settings")]
        [SerializeField] private float gap = 14f;
        [Tooltip("Degrees the arrow turns when the group is open")]
        [SerializeField] private float arrowTurn = 90f;
        [Tooltip("Space kept above the tile when the page scrolls to it")]
        [SerializeField] private float scrollMargin = 12f;

        static readonly List<SettingsSectionTile> all = new List<SettingsSectionTile>();
        float k;                 // 0 = shut, 1 = open
        int basePadding = -1;
        bool open, following;
        float scrollFrom;

        public bool IsOpen => open;

        void Awake()
        {
            if (!Application.isPlaying) return;
            if (tileButton != null) tileButton.onClick.AddListener(Toggle);
            open = startOpen; k = open ? 1f : 0f;
        }

        void OnEnable() { if (Application.isPlaying) all.Add(this); Apply(); }
        void OnDisable() { all.Remove(this); }
        void Start() { Apply(); }

        public void Toggle() { SetOpen(!open); }

        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (!open) { following = false; return; }
            if (closeOthers)
                foreach (SettingsSectionTile other in all) if (other != null && other != this) other.SetOpen(false);
            if (iconAnimator != null) iconAnimator.Play();
            if (scroll != null && scrollToTile && scroll.content != null) { following = true; scrollFrom = scroll.content.anchoredPosition.y; }
        }

        void Update()
        {
            if (!Application.isPlaying)
            {
                // in the Editor the group simply shows as "Start Open" says
                k = startOpen ? 1f : 0f;
                Apply();
                return;
            }
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(k, target)) { if (open) Apply(); return; }      // an open group keeps fitting its settings and its glow keeps breathing
            k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, slideSeconds));
            Apply();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !following || scroll == null || scroll.content == null || scroll.viewport == null) return;
            RectTransform content = scroll.content;
            var self = (RectTransform)transform;
            float tileTop = -(self.localPosition.y + self.rect.yMax) - scrollMargin;                 // distance of the tile from the top of the list
            float furthest = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
            float eased = slideCurve != null ? slideCurve.Evaluate(k) : k;
            Vector2 p = content.anchoredPosition;
            p.y = Mathf.Lerp(scrollFrom, Mathf.Clamp(tileTop, 0f, furthest), eased);
            content.anchoredPosition = p;
            scroll.velocity = Vector2.zero;
            if (k >= 1f) following = false;
        }

        void Apply()
        {
            if (body == null) return;
            float eased = slideCurve != null ? slideCurve.Evaluate(k) : k;
            float full = panel != null ? panel.rect.height + gap : 0f;
            float height = full * eased;
            if (!Mathf.Approximately(body.preferredHeight, height)) { body.minHeight = height; body.preferredHeight = height; }
            if (bodyGroup != null)
            {
                bodyGroup.alpha = Mathf.Clamp01(eased * 1.3f);
                bodyGroup.blocksRaycasts = k > 0.6f; bodyGroup.interactable = k > 0.6f;
            }
            if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, -arrowTurn * eased);

            // the tile's own change of state
            if (tileVisual != null) { float s = Mathf.Lerp(1f, openScale, eased); tileVisual.localScale = new Vector3(s, s, 1f); }
            if (openGlow != null)
            {
                float breath = Application.isPlaying ? 1f - glowBreath * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f)) : 1f;
                openGlow.alpha = glowAlpha * eased * breath;
            }
            if (line != null)
            {
                if (basePadding < 0) basePadding = line.padding.left;
                int wanted = basePadding + Mathf.RoundToInt(slideDistance * eased);
                if (line.padding.left != wanted) { line.padding.left = wanted; LayoutRebuilder.MarkLayoutForRebuild((RectTransform)line.transform); }
            }
        }

        /// <summary>Opens the group at once, without animation (used when the Editor draws a picture of the page).</summary>
        public void ShowOpenNow()
        {
            startOpen = true; open = true; k = 1f;
            if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Apply();
        }

#if UNITY_EDITOR
        void OnValidate() { k = startOpen ? 1f : 0f; }
#endif
    }
}
