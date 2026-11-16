using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// "BACK TO TOP" for long documents (7 November 2026): a round button in the theme's accent colour with an up arrow, at the
    /// bottom right of a scroll area. Hidden at the top; it fades in once the reader has scrolled down about one screen and fades
    /// out again near the top. A tap scrolls the text smoothly back to the start.
    ///
    /// One component for every Privacy Policy / Terms screen: LegalTextSource adds it to the scroll area its text sits in, so
    /// every copy of the documents gets it, also in scenes built before it existed. 132 x 132 units = 44 dp (the smallest touch
    /// target). It sits inside the scroll area, left of its scroll bar; the end of the text gets room below it, so the last lines
    /// can always be scrolled clear of the button.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class BackToTopButton : MonoBehaviour
    {
        public const string AccessibleName = "Back to top";
        const float Size = 132f, Margin = 40f, FadeSeconds = 0.2f, ScrollSeconds = 0.45f;

        ScrollRect scroll;
        CanvasGroup group;
        Image disc, arrow;
        Coroutine scrolling;
        bool shown;

        /// <summary>Adds the button to `target` (once).</summary>
        public static BackToTopButton Attach(ScrollRect target)
        {
            if (target == null || !target.vertical) return null;
            BackToTopButton existing = target.GetComponentInChildren<BackToTopButton>(true);
            if (existing != null) return existing;

            RectTransform rt = RuntimeUiKit.Rect(AccessibleName, target.transform);       // the object's name is what the button is
            rt.SetAsLastSibling();                                                          // drawn over the text
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            float barWidth = target.verticalScrollbar != null ? ((RectTransform)target.verticalScrollbar.transform).rect.width : 0f;
            rt.sizeDelta = new Vector2(Size, Size);
            rt.anchoredPosition = new Vector2(-(Margin + barWidth), Margin);

            var button = rt.gameObject.AddComponent<BackToTopButton>();
            button.scroll = target;
            button.group = rt.GetComponent<CanvasGroup>();
            button.group.alpha = 0f; button.group.interactable = false; button.group.blocksRaycasts = false;
            button.disc = rt.gameObject.AddComponent<Image>();
            button.disc.sprite = RuntimeIcons.Circle;
            var press = rt.gameObject.AddComponent<Button>();
            press.targetGraphic = button.disc;
            press.onClick.AddListener(button.ToTop);
            button.arrow = RuntimeUiKit.Icon(rt, "Arrow", RuntimeIcons.ArrowUp, Color.white, 60);
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.18f); shadow.effectDistance = new Vector2(0f, -4f);
            button.Paint();
            button.MakeRoomAtTheEnd();
            return button;
        }

        void OnEnable() { GlowUp.Theme.ThemeManager.ThemeChanged += OnTheme; }
        void OnDisable() { GlowUp.Theme.ThemeManager.ThemeChanged -= OnTheme; }
        void OnTheme(bool dark) => Paint();

        void Paint()
        {
            var p = RuntimeTheme.Palette;
            if (disc != null) disc.color = p.buttonFill;
            if (arrow != null) arrow.color = p.buttonText;
        }

        /// <summary>The last lines must never stay under the button: the content gets a free strip of the button's height at its end.</summary>
        void MakeRoomAtTheEnd()
        {
            if (scroll == null || scroll.content == null) return;
            var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.padding.bottom = Mathf.Max(layout.padding.bottom, (int)(Size + Margin * 2f)); return; }
            var text = scroll.content.GetComponentInChildren<TMPro.TMP_Text>();
            if (text != null && scroll.content.childCount == 1 && text.transform.parent == scroll.content)
                text.margin = new Vector4(text.margin.x, text.margin.y, text.margin.z, Mathf.Max(text.margin.w, Size + Margin * 2f));
        }

        void Update()
        {
            if (scroll == null || scroll.content == null) return;
            RectTransform view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float scrolled = scroll.content.anchoredPosition.y;                   // how far the text has moved up
            bool want = scrolled > view.rect.height * 0.9f;
            if (want != shown) { shown = want; group.interactable = want; group.blocksRaycasts = want; }
            float target = shown ? 1f : 0f;
            if (!Mathf.Approximately(group.alpha, target))
                group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / FadeSeconds);
        }

        void ToTop()
        {
            if (scrolling != null) StopCoroutine(scrolling);
            scrolling = StartCoroutine(Glide());
        }

        IEnumerator Glide()
        {
            scroll.StopMovement();
            float from = scroll.verticalNormalizedPosition, t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / ScrollSeconds;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);           // fast first, gentle at the end
                scroll.verticalNormalizedPosition = Mathf.Lerp(from, 1f, eased);
                yield return null;
            }
            scroll.verticalNormalizedPosition = 1f;
            scrolling = null;
        }
    }
}
