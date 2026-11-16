using System;
using System.Collections.Generic;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// THE APP'S ONE TOAST (reworked 10 November 2026, New13 item 5B). Short confirmations and passing errors only; a hint that belongs
    /// to a field is a <see cref="FieldHint"/> under that field, and a message that must stay is a <see cref="StepNoticeBanner"/>.
    ///
    ///   - ONE at a time on the whole screen: a new toast replaces the one that shows (no stacking).
    ///   - Top centre, in the header row BETWEEN the corner buttons (Back / X): it never lies over a button or a text box, and never
    ///     near the bottom action bar. At most 90 % of the screen wide, at most two lines (the words shrink a little, then end in "...").
    ///   - An icon on the left: success, info, warning, error. Not colour alone.
    ///   - Goes away by itself (3 s; 5 s for errors and warnings), or when tapped, or when swiped up or sideways.
    ///   - Fades in (150 ms); with reduced effects it simply appears. Only the toast itself takes touches.
    ///
    /// Made at run time (no scene rebuild): FormalToast.OnScreen(anything on the canvas, a text for its font). The older Above() gives the
    /// same top toast, so every caller uses one system.
    /// </summary>
    public class FormalToast : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public enum Kind { Error, Info, Success, Warning }

        [SerializeField] private float showSeconds = 2.5f;
        [SerializeField] private float errorSeconds = 4f;

        Image box, icon;
        TextMeshProUGUI words, link;
        LayoutElement wordsElement;
        Action onLink;
        float hideAt, shownAt;
        CanvasGroup fade;
        Vector2 dragStart, restPosition;

        /// <summary>Every toast that exists, so a new one can hide the one that shows (one at a time).</summary>
        static readonly List<FormalToast> all = new List<FormalToast>();
        /// <summary>The toast on screen now (null = none). For tests.</summary>
        public static FormalToast Current { get; private set; }

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDestroy() { all.Remove(this); if (Current == this) Current = null; }

        /// <summary>
        /// A toast for the NEXT screen (New17), for a message that belongs to a change of screen, for example "Email confirmed" when
        /// Step 1 goes on to Step 2 by itself. It shows once the next scene has loaded and its first frame is drawn.
        /// </summary>
        public static void ShowAfterNextScene(Kind kind, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            void Loaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
            {
                if (scene.name == "LoadingScene") return;                          // the loading screen in between is not the target
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Loaded;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Canvas canvas = root.GetComponentInChildren<Canvas>();
                    if (canvas == null) continue;
                    TMP_Text font = canvas.GetComponentInChildren<TMP_Text>(true);
                    OnScreen(canvas.transform, font)?.Show(kind, message);
                    return;
                }
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += Loaded;
        }

        /// <summary>Kept for older callers: gives the ONE top toast of the screen (the card and anchor are no longer used).</summary>
        public static FormalToast Above(RectTransform card, RectTransform anchor, RectTransform widthFrom, TMP_Text fontFrom, float gap = 16f)
            => OnScreen(card, fontFrom);

        /// <summary>The top toast of the screen `anywhere` is on (made once per screen, then reused).</summary>
        public static FormalToast OnScreen(Transform anywhere, TMP_Text fontFrom)
        {
            Canvas canvas = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            Transform existing = top.Find("ToastLayer");
            if (existing != null)
            {
                FormalToast found = existing.GetComponentInChildren<FormalToast>(true);
                if (found != null) return found;
                Destroy(existing.gameObject);
            }
            RectTransform layer = RuntimeUiKit.Rect("ToastLayer", top);
            RuntimeUiKit.Stretch(layer);
            var sort = layer.gameObject.AddComponent<Canvas>();
            sort.overrideSorting = true; sort.sortingOrder = 6000;
            layer.gameObject.AddComponent<GraphicRaycaster>();          // only the toast's own Image takes touches; the layer has none
            FormalToast toast = Build(layer, fontFrom);
            Place((RectTransform)toast.transform, layer);
            return toast;
        }

        // ---- where it sits (New17): near the BOTTOM, inside the safe area, just above the lowest buttons of the screen (the bottom bar,
        //      Continue). When that band would lie over any button or text box, or the keyboard is open, it uses the header row between
        //      the corner buttons instead (the place that is free on every screen). ToastPlacement holds the numbers (tested). ----
        static void Place(RectTransform rt, RectTransform layer)
        {
            Rect safe = Screen.safeArea;
            float scale = layer.rect.height > 0f ? Screen.height / layer.rect.height : 1f;
            float safeTop = scale > 0f ? (Screen.height - safe.yMax) / scale : 0f;
            float safeBottom = scale > 0f ? safe.yMin / scale : 0f;
            ToastPlacement.Band band = ToastPlacement.For(layer.rect.width, safeTop);
            rt.sizeDelta = new Vector2(band.width, rt.sizeDelta.y);

            if (!KeyboardOpen && BottomBand(rt, layer, band.width, safeBottom, out float fromBottom))
            {
                rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, fromBottom);
                return;
            }
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -band.top);
        }

        static bool KeyboardOpen { get { try { return TouchScreenKeyboard.visible; } catch (Exception) { return false; } } }

        /// <summary>
        /// The bottom band: its lower edge stands a gap above the highest top edge of the buttons in the lowest part of the screen
        /// (ToastPlacement.LowZone), and never under the safe area. False when that band would cover any visible button or text box.
        /// </summary>
        static bool BottomBand(RectTransform rt, RectTransform layer, float width, float safeBottom, out float fromBottom)
        {
            fromBottom = 0f;
            Transform root = layer.parent;
            if (root == null) return false;
            Rect area = layer.rect;
            var controls = new List<Rect>();
            var corners = new Vector3[4];
            foreach (Selectable control in root.GetComponentsInChildren<Selectable>(false))
            {
                if (control == null || !control.isActiveAndEnabled || control.transform.IsChildOf(layer)) continue;
                if (control.GetComponentInParent<CanvasGroup>() is CanvasGroup g && g.alpha < 0.01f) continue;
                ((RectTransform)control.transform).GetWorldCorners(corners);
                Vector3 a = layer.InverseTransformPoint(corners[0]), b = layer.InverseTransformPoint(corners[2]);
                Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                if (r.width >= 1f && r.height >= 1f && r.Overlaps(area)) controls.Add(r);
            }
            float floor = area.yMin + safeBottom + ToastPlacement.BottomGap;
            float lowZone = area.yMin + area.height * ToastPlacement.LowZone;
            foreach (Rect r in controls) if (r.yMin < lowZone && r.center.y < lowZone) floor = Mathf.Max(floor, r.yMax + ToastPlacement.BottomGap);
            Rect band = new Rect(area.center.x - width / 2f, floor, width, ToastPlacement.MaxHeight);
            if (band.yMax > area.yMin + area.height * 0.5f) return false;                      // the bottom is full of controls: use the header row
            foreach (Rect r in controls) if (r.Overlaps(band)) return false;
            fromBottom = floor - area.yMin;
            return true;
        }

        static FormalToast Build(RectTransform parent, TMP_Text fontFrom)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            RectTransform rt = RuntimeUiKit.Rect("FormalToast", parent);
            rt.SetAsLastSibling();
            var toast = rt.gameObject.AddComponent<FormalToast>();
            toast.box = rt.gameObject.AddComponent<Image>();
            toast.box.sprite = RuntimeIcons.RoundedBox; toast.box.type = Image.Type.Sliced; toast.box.raycastTarget = true;
            var shadow = rt.gameObject.AddComponent<Shadow>();                 // lifts it off whatever lies under it
            shadow.effectColor = new Color(0f, 0f, 0f, 0.28f); shadow.effectDistance = new Vector2(0f, -4f);
            toast.fade = rt.gameObject.AddComponent<CanvasGroup>();
            var row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(ToastPlacement.PadX, ToastPlacement.PadX, ToastPlacement.PadY, ToastPlacement.PadY);
            row.spacing = 18; row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            toast.icon = RuntimeUiKit.Icon(rt, "Icon", RuntimeIcons.Info, p.accentText, ToastPlacement.IconSize);
            var iconElement = toast.icon.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = ToastPlacement.IconSize; iconElement.minHeight = iconElement.preferredHeight = ToastPlacement.IconSize;

            RectTransform column = RuntimeUiKit.Rect("Text", rt);
            var columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            columnLayout.childControlWidth = true; columnLayout.childControlHeight = true; columnLayout.childForceExpandWidth = true; columnLayout.childForceExpandHeight = false;
            columnLayout.spacing = 4; columnLayout.childAlignment = TextAnchor.MiddleLeft;
            column.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            toast.words = RuntimeUiKit.Label(column, "Message", "", ToastPlacement.FontSize, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.MidlineLeft);
            toast.words.enableWordWrapping = true; toast.words.overflowMode = TextOverflowModes.Ellipsis;
            toast.wordsElement = toast.words.gameObject.AddComponent<LayoutElement>();
            // the link ("Try again") stands at the right of the same row, so a toast is never more than two lines high
            toast.link = RuntimeUiKit.Label(rt, "Link", "", ToastPlacement.FontSize, p.accentText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Center);
            toast.link.fontStyle = FontStyles.Bold | FontStyles.Underline;
            toast.link.enableWordWrapping = false;
            toast.link.raycastTarget = true;
            var linkElement = toast.link.gameObject.AddComponent<LayoutElement>();
            linkElement.minHeight = 80f; linkElement.minWidth = 96f;           // a reachable tap target inside the toast
            toast.link.gameObject.AddComponent<Button>().onClick.AddListener(toast.RunLink);
            toast.link.gameObject.SetActive(false);
            rt.gameObject.SetActive(false);
            return toast;
        }

        /// <summary>Shows `message`, replacing any toast that shows. `linkWords` + `action` add a link (the toast then stays until used or dismissed).</summary>
        public void Show(Kind kind, string message, string linkWords = null, Action action = null)
        {
            if (string.IsNullOrWhiteSpace(message)) { Hide(); return; }
            // no duplicates: the same words that already show only stay a little longer
            if (IsShowing && shownKind == kind && words.text == message && onLink == null && action == null)
            {
                hideAt = Mathf.Max(hideAt, Time.unscaledTime + SecondsFor(kind));
                return;
            }
            // no flood: a new toast waits while the one on screen has shown for less than a moment (an error never waits)
            if (IsShowing && kind != Kind.Error && Time.unscaledTime - shownAt < ToastPlacement.MinShowSeconds)
            {
                foreach (Pending p in queue) if (p.message == message && p.kind == kind) return;
                if (queue.Count >= ToastPlacement.MaxWaiting) queue.RemoveAt(0);
                queue.Add(new Pending { kind = kind, message = message, linkWords = linkWords, action = action });
                return;
            }
            ShowNow(kind, message, linkWords, action);
        }

        struct Pending { public Kind kind; public string message, linkWords; public Action action; }
        readonly List<Pending> queue = new List<Pending>();
        Kind shownKind;

        void ShowNow(Kind kind, string message, string linkWords, Action action)
        {
            shownKind = kind;
            foreach (FormalToast other in all.ToArray()) if (other != null && other != this) other.Hide();
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Color tone = ToneOf(kind, p);
            box.color = RuntimeTheme.Mix(p.surface, tone, RuntimeTheme.IsDark ? 0.22f : 0.10f);
            icon.sprite = kind == Kind.Error || kind == Kind.Warning ? RuntimeIcons.Warning : kind == Kind.Success ? RuntimeIcons.Check : RuntimeIcons.Info;
            icon.color = tone;
            words.color = p.mainText;
            words.text = message;
            onLink = action;
            bool hasLink = !string.IsNullOrEmpty(linkWords) && action != null;
            link.gameObject.SetActive(hasLink);
            if (hasLink) link.text = linkWords;
            hideAt = hasLink ? float.MaxValue : Time.unscaledTime + SecondsFor(kind);
            shownAt = Time.unscaledTime;
            transform.parent.SetAsLastSibling();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            var rt = (RectTransform)transform;
            Place(rt, (RectTransform)rt.parent);
            restPosition = rt.anchoredPosition;
            FitTwoLines();
            fade.alpha = Reduced ? 1f : 0f;
            Current = this;
        }

        /// <summary>How long a toast of this kind stays (seconds): errors and warnings longer, they need reading.</summary>
        public float SecondsFor(Kind kind) => Mathf.Max(1f, kind == Kind.Error || kind == Kind.Warning ? errorSeconds : showSeconds);

        static Color ToneOf(Kind kind, ThemeProfile.Palette p) =>
            kind == Kind.Error ? p.warning : kind == Kind.Warning ? p.caution : kind == Kind.Success ? p.success : p.info;   // Information: the blue info token (colour guideline §5)

        static bool Reduced => GlowUp.Performance.PerformanceSettings.ReduceEffects;

        /// <summary>At most two lines: the words shrink to 26, then the rest ends in "...".</summary>
        void FitTwoLines()
        {
            var rt = (RectTransform)transform;
            float linkWidth = link.gameObject.activeSelf ? Mathf.Max(96f, link.GetPreferredValues(link.text).x) + 18f : 0f;
            float textWidth = rt.sizeDelta.x - 2 * ToastPlacement.PadX - ToastPlacement.IconSize - 18f - linkWidth;
            words.fontSize = ToastPlacement.FontSize;
            wordsElement.preferredHeight = -1f;
            for (float size = ToastPlacement.FontSize; size >= ToastPlacement.SmallestFont; size -= 2f)
            {
                words.fontSize = size;
                Vector2 need = words.GetPreferredValues(words.text, textWidth, 0f);
                if (need.y <= ToastPlacement.TwoLines(size)) break;
            }
            Vector2 last = words.GetPreferredValues(words.text, textWidth, 0f);
            if (last.y > ToastPlacement.TwoLines(words.fontSize)) wordsElement.preferredHeight = ToastPlacement.TwoLines(words.fontSize);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            if (Current == this) Current = null;
            // the next waiting toast, if any (one at a time, in the order they came)
            if (queue.Count > 0)
            {
                Pending next = queue[0];
                queue.RemoveAt(0);
                ShowNow(next.kind, next.message, next.linkWords, next.action);
            }
        }

        public bool IsShowing => gameObject.activeSelf;

        void Update()
        {
            float now = Time.unscaledTime;
            float left = hideAt - now;
            if (left <= 0f) { Hide(); return; }
            float inK = Reduced ? 1f : Mathf.Clamp01((now - shownAt) / 0.15f);           // 150 ms fade in
            float outK = left < 0.2f ? left / 0.2f : 1f;                                   // and a short fade at the end
            fade.alpha = Mathf.Min(inK, outK);
        }

        void RunLink() { Action a = onLink; Hide(); a?.Invoke(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            if (eventData.pointerPressRaycast.gameObject != link.gameObject) Hide();
        }

        // swipe up or sideways to dismiss
        public void OnBeginDrag(PointerEventData e) { dragStart = e.position; }
        public void OnDrag(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            float scale = rt.lossyScale.x > 0f ? 1f / rt.lossyScale.x : 1f;
            Vector2 d = (e.position - dragStart) * scale;
            rt.anchoredPosition = restPosition + new Vector2(d.x, Mathf.Max(0f, d.y));
            fade.alpha = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(d.x), d.y) / 300f);
        }
        public void OnEndDrag(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            Vector2 moved = rt.anchoredPosition - restPosition;
            rt.anchoredPosition = restPosition;
            if (Mathf.Abs(moved.x) > 80f || moved.y > 50f) Hide(); else fade.alpha = 1f;
        }
    }

    /// <summary>
    /// The numbers of the toast's place (pure, tested in ToastPlacementTests): the header row of a 1080-wide canvas, between the
    /// 92-unit corner buttons that every screen has 28 units inside its 40-unit card margin.
    /// </summary>
    public static class ToastPlacement
    {
        public const float CornerButtonsInset = 40f + 28f + 92f + 16f;   // card margin + button margin + button + a gap
        public const float TopGap = 40f + 20f;                            // card margin + a little air
        public const float MaxWidthDp = 420f, UnitsPerDp = 3f;           // 1080 units ~ 360 dp
        public const int PadX = 28, PadY = 16;
        /// <summary>The bottom band: the gap above the lowest buttons (and above the safe area), and how high "the lowest part" reaches.</summary>
        public const float BottomGap = 24f, LowZone = 0.3f;
        /// <summary>A new toast waits until the shown one has been up this long; at most this many wait.</summary>
        public const float MinShowSeconds = 1.2f;
        public const int MaxWaiting = 3;
        public const float IconSize = 40f, FontSize = 30f, SmallestFont = 26f;

        public struct Band { public float top, width; }

        /// <summary>Two lines of text at `size` (TMP line height ~1.32 of the size).</summary>
        public static float TwoLines(float size) => Mathf.Ceil(size * 1.32f * 2f + 2f);

        /// <summary>The tallest the toast can be: two lines plus padding (it must stay above the subtitle line of the screens).</summary>
        public static float MaxHeight => TwoLines(FontSize) + 2 * PadY;

        public static Band For(float canvasWidth, float safeTop)
        {
            float between = canvasWidth - 2f * CornerButtonsInset;
            float width = Mathf.Min(canvasWidth * 0.9f, MaxWidthDp * UnitsPerDp, between);
            return new Band { top = safeTop + TopGap, width = Mathf.Max(320f, width) };
        }
    }
}
