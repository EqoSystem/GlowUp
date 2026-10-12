using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// "Skeleton" loading for WORDS, the way the admin website fetches its data: while a screen is fetching, every title,
    /// label and subtitle in the list "Texts" is replaced by a soft bar of the same size in the same place, with a
    /// rose-to-gold shine travelling across. When the data is there the bars give way to the words.
    ///
    /// How a screen uses it:
    ///   - one object "TextSkeleton" lies over the screen's card (the scene builder makes it and fills "Texts");
    ///   - a script that fetches data says so: TextSkeleton.Waiting(this) before, TextSkeleton.Ready(this) after;
    ///   - the bars show when the screen opens and stay until nobody is fetching any more, at least "Shortest Seconds"
    ///     (so they do not flash) and at most "Longest Seconds" (the time limit: the words then show what is known).
    ///
    /// A bar is drawn for each LINE of a text, as long as that line and a little lower than its letters. A text that is
    /// empty, switched off or faded out gets no bar. Lists that are filled while the app runs have their own placeholder
    /// rows ("Show While Loading"), and the real list can be kept out of sight until then ("Hide While Loading").
    ///
    /// Nothing is drawn in the Editor while the app is not running, so the scene shows its real words; tick "Preview In
    /// Edit Mode" to see where the bars will be. The colours and the shine belong to the material (GlowUp/SkeletonShine)
    /// and follow the theme.
    /// </summary>
    [AddComponentMenu("GlowUp/Text Skeleton")]
    [RequireComponent(typeof(CanvasRenderer))]          // without it nothing is drawn (Unity does not add one for a Graphic of our own)
    public class TextSkeleton : MaskableGraphic
    {
        [Header("What it stands in for")]
        [Tooltip("The titles, labels and subtitles that become bars while the screen fetches its data")]
        [SerializeField] private TMP_Text[] texts;
        [Tooltip("Placeholder shapes for lists (rows, tiles): switched on while the screen is fetching, off afterwards. Optional.")]
        [SerializeField] private GameObject[] showWhileLoading;
        [Tooltip("The real lists: kept out of sight while the screen is fetching, then faded in. Optional.")]
        [SerializeField] private CanvasGroup[] hideWhileLoading;

        [Header("When")]
        [Tooltip("On = the bars show as soon as the screen opens. Off = only when a script says it is fetching (TextSkeleton.Waiting).")]
        [SerializeField] private bool showWhenScreenOpens = true;
        [Tooltip("The bars stay at least this long, so they do not flash when the data is on the phone already. 0 = they only show while something is really being fetched.")]
        [SerializeField] private float shortestSeconds = 0.35f;
        [Tooltip("The time limit: after this long the words are shown whatever happens (they then say what is known so far)")]
        [SerializeField] private float longestSeconds = 8f;
        [Tooltip("How long the bars take to give way to the words")]
        [SerializeField] private float revealSeconds = 0.2f;

        [Header("The bars")]
        [Tooltip("The round shape the bars are cut from (the app's pill sprite)")]
        [SerializeField] private Sprite roundSprite;
        [Tooltip("How high a bar is, as a share of the height of its line of text")]
        [Range(0.2f, 1f)] [SerializeField] private float barHeight = 0.56f;
        [Tooltip("No bar is higher than this (canvas units), however big the letters")]
        [SerializeField] private float tallestBar = 64f;
        [Tooltip("At most this many lines of one text get a bar")]
        [SerializeField] private int mostLines = 4;
        [Tooltip("Editor only: draw the bars over the words while the app is not running, to see where they will be")]
        [SerializeField] private bool previewInEditMode = false;

        // ---- who is fetching (any script of the screen), and the skeleton of the screen that is open
        static readonly HashSet<object> fetching = new HashSet<object>();
        static readonly List<object> gone = new List<object>();
        static TextSkeleton current;

        /// <summary>True while the open screen shows bars in the place of its words.</summary>
        public static bool IsShowing => current != null && current.showing;

        /// <summary>A script says: "I am fetching data for this screen". The words become bars until it calls Ready.</summary>
        public static void Waiting(object who)
        {
            if (who == null) return;
            fetching.Add(who);
            if (current != null && !current.showing && Application.isPlaying && current.isActiveAndEnabled) current.Show();
        }

        /// <summary>The script has its data (or gave up). Always call it, also when the fetch failed.</summary>
        public static void Ready(object who) { if (who != null) fetching.Remove(who); }

        readonly List<Rect> bars = new List<Rect>(), fresh = new List<Rect>();
        CanvasGroup[][] fadersOf;          // for each text: the CanvasGroups above it (a faded-out group hides its texts: no bar)
        RectTransform[] windowOf;          // for each text: the list window it scrolls in, if any (bars are cut at its edge)
        bool showing, revealing;
        float shownFor;
        int framesShown;
        static readonly Vector3[] corners = new Vector3[4];
        const float LongestFrame = 0.05f;          // seconds one frame may add to the clock

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            if (!Application.isPlaying) return;
            current = this;
            fetching.Clear();               // whoever was fetching belonged to the screen that was just left
        }

        protected override void OnDestroy()
        {
            if (current == this) current = null;
            base.OnDestroy();
        }

        protected override void Start()
        {
            base.Start();
            if (Application.isPlaying && showWhenScreenOpens) Show();
        }

        public override Texture mainTexture => roundSprite != null ? roundSprite.texture : s_WhiteTexture;

#if UNITY_EDITOR
        protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif

        // ---------------------------------------------------------------------------------------
        /// <summary>The words give way to bars (and the lists to their placeholder rows).</summary>
        public void Show()
        {
            if (showing || texts == null) return;
            showing = true; revealing = false; shownFor = 0f; framesShown = 0;
            Remember();
            foreach (TMP_Text text in texts)
            {
                if (text == null) continue;
                if (text.isActiveAndEnabled) text.ForceMeshUpdate();       // its lines are known at once, so the bars are there in the first picture
                text.CrossFadeAlpha(0f, 0f, true);
            }
            if (showWhileLoading != null) foreach (GameObject shape in showWhileLoading) if (shape != null) shape.SetActive(true);
            // the real lists: out of sight, and no touch lands on a row that cannot be seen
            if (hideWhileLoading != null) foreach (CanvasGroup list in hideWhileLoading) if (list != null) { list.alpha = 0f; list.blocksRaycasts = false; }
            CrossFadeAlpha(1f, 0f, true);
            Measure(bars);
            SetVerticesDirty();
        }

        void Reveal()
        {
            showing = false; revealing = true;
            float seconds = GlowUp.Performance.PerformanceSettings.ReduceEffects ? 0f : Mathf.Max(0f, revealSeconds);
            foreach (TMP_Text text in texts) if (text != null) text.CrossFadeAlpha(1f, seconds, true);
            if (showWhileLoading != null) foreach (GameObject shape in showWhileLoading) if (shape != null) shape.SetActive(false);
            CrossFadeAlpha(0f, seconds, true);
            shownFor = 0f;                  // now counts the reveal
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            SkeletonLook.Tick();
            if (showing)
            {
                // the time only counts while the screen can be seen (not under the sheet between two screens)
                // (one long frame, such as the first one of a screen, must not use up the bars' time: it counts as a short one)
                if (!ScreenTransition.IsBusy) { shownFor += Mathf.Min(Time.unscaledDeltaTime, LongestFrame); framesShown++; }
                Measure(fresh);
                if (!Same(fresh, bars)) { bars.Clear(); bars.AddRange(fresh); SetVerticesDirty(); }
                float shortest = GlowUp.Performance.PerformanceSettings.ReduceEffects ? 0f : shortestSeconds;
                if (SkeletonLook.MayEnd(shownFor, framesShown, StillFetching(), shortest, longestSeconds)) Reveal();
            }
            else if (revealing)
            {
                shownFor += Mathf.Min(Time.unscaledDeltaTime, LongestFrame);
                float k = revealSeconds <= 0.01f ? 1f : Mathf.Clamp01(shownFor / revealSeconds);
                if (hideWhileLoading != null) foreach (CanvasGroup list in hideWhileLoading) if (list != null) list.alpha = Mathf.Max(list.alpha, k);
                if (k < 1f) return;
                revealing = false;
                if (hideWhileLoading != null) foreach (CanvasGroup list in hideWhileLoading) if (list != null) list.blocksRaycasts = true;
                bars.Clear(); SetVerticesDirty();        // nothing is left to draw
            }
        }

        /// <summary>How many scripts are still fetching. A script that was destroyed without saying Ready no longer counts.</summary>
        static int StillFetching()
        {
            gone.Clear();
            foreach (object who in fetching) if (who is Object thing && thing == null) gone.Add(who);
            foreach (object who in gone) fetching.Remove(who);
            return fetching.Count;
        }

        // ---------------------------------------------------------------------------------------
        //  Where the bars are
        // ---------------------------------------------------------------------------------------
        void Remember()
        {
            fadersOf = new CanvasGroup[texts.Length][];
            windowOf = new RectTransform[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) continue;
                fadersOf[i] = texts[i].GetComponentsInParent<CanvasGroup>(true);
                RectMask2D mask = texts[i].GetComponentInParent<RectMask2D>();
                Mask oldMask = mask == null ? texts[i].GetComponentInParent<Mask>() : null;
                windowOf[i] = mask != null ? mask.rectTransform : oldMask != null ? oldMask.rectTransform : null;
            }
        }

        void Measure(List<Rect> into)
        {
            into.Clear();
            if (texts == null) return;
            if (fadersOf == null || fadersOf.Length != texts.Length) Remember();
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null || !text.isActiveAndEnabled) continue;
                TMP_TextInfo info = text.textInfo;
                if (info == null || info.characterCount == 0 || info.lineInfo == null) continue;       // nothing written: no bar
                if (FadedOut(fadersOf[i])) continue;

                bool cut = windowOf[i] != null;
                Rect window = cut ? RectOf(windowOf[i]) : default;
                int lines = Mathf.Min(info.lineCount, info.lineInfo.Length, Mathf.Max(1, mostLines));
                for (int n = 0; n < lines; n++)
                {
                    TMP_LineInfo line = info.lineInfo[n];
                    if (line.visibleCharacterCount <= 0) continue;
                    float height = Mathf.Min(tallestBar, (line.ascender - line.descender) * barHeight);
                    if (height < 2f) continue;
                    float middle = (line.ascender + line.descender) * 0.5f;
                    float left = line.lineExtents.min.x, right = Mathf.Max(line.lineExtents.max.x, left + height);
                    Vector3 a = Here(text.rectTransform, new Vector3(left, middle - height * 0.5f)), b = Here(text.rectTransform, new Vector3(right, middle + height * 0.5f));
                    Rect bar = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    if (cut) bar = SkeletonLook.Inside(bar, window);
                    if (bar.width >= 2f && bar.height >= 2f) into.Add(bar);
                }
            }
        }

        static bool FadedOut(CanvasGroup[] groups)
        {
            if (groups == null) return false;
            foreach (CanvasGroup group in groups) if (group != null && group.enabled && group.alpha < 0.05f) return true;
            return false;
        }

        Vector3 Here(Transform from, Vector3 point) => rectTransform.InverseTransformPoint(from.TransformPoint(point));

        Rect RectOf(RectTransform other)
        {
            other.GetWorldCorners(corners);
            Vector3 a = rectTransform.InverseTransformPoint(corners[0]), b = rectTransform.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static bool Same(List<Rect> one, List<Rect> other)
        {
            if (one.Count != other.Count) return false;
            for (int i = 0; i < one.Count; i++)
            {
                Rect a = one[i], b = other[i];
                if (Mathf.Abs(a.x - b.x) > 0.5f || Mathf.Abs(a.y - b.y) > 0.5f || Mathf.Abs(a.width - b.width) > 0.5f || Mathf.Abs(a.height - b.height) > 0.5f) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------------------------
        //  Drawing: every bar is a pill cut from the round sprite (round ends, a stretched middle)
        // ---------------------------------------------------------------------------------------
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            List<Rect> list = bars;
            if (!Application.isPlaying)
            {
                if (!previewInEditMode) return;
                Measure(fresh); list = fresh;
            }
            if (list.Count == 0) return;
            Vector4 outer = roundSprite != null ? DataUtility.GetOuterUV(roundSprite) : new Vector4(0f, 0f, 1f, 1f);
            Vector4 inner = roundSprite != null ? DataUtility.GetInnerUV(roundSprite) : outer;
            Color32 tint = color;
            foreach (Rect bar in list) AddPill(vh, bar, outer, inner, tint);
        }

        static readonly float[] xs = new float[4], ys = new float[4], us = new float[4], vs = new float[4];

        static void AddPill(VertexHelper vh, Rect bar, Vector4 outer, Vector4 inner, Color32 tint)
        {
            float round = Mathf.Min(bar.height, bar.width) * 0.5f;
            xs[0] = bar.xMin; xs[1] = bar.xMin + round; xs[2] = bar.xMax - round; xs[3] = bar.xMax;
            ys[0] = bar.yMin; ys[1] = bar.yMin + round; ys[2] = bar.yMax - round; ys[3] = bar.yMax;
            us[0] = outer.x; us[1] = inner.x; us[2] = inner.z; us[3] = outer.z;
            vs[0] = outer.y; vs[1] = inner.y; vs[2] = inner.w; vs[3] = outer.w;
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                {
                    if (xs[column + 1] - xs[column] < 0.01f || ys[row + 1] - ys[row] < 0.01f) continue;     // a part with no size (the middle of a round dot)
                    int first = vh.currentVertCount;
                    vh.AddVert(new Vector3(xs[column], ys[row]), tint, new Vector2(us[column], vs[row]));
                    vh.AddVert(new Vector3(xs[column], ys[row + 1]), tint, new Vector2(us[column], vs[row + 1]));
                    vh.AddVert(new Vector3(xs[column + 1], ys[row + 1]), tint, new Vector2(us[column + 1], vs[row + 1]));
                    vh.AddVert(new Vector3(xs[column + 1], ys[row]), tint, new Vector2(us[column + 1], vs[row]));
                    vh.AddTriangle(first, first + 1, first + 2);
                    vh.AddTriangle(first + 2, first + 3, first);
                }
        }
    }
}
