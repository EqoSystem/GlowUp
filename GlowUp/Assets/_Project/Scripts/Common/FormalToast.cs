using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The app's one formal message for errors and short notices (10 November 2026): a rounded box with an icon on the left and the
    /// message centred beside it, wrapping normally. It sits INSIDE a card, directly above a chosen element (on the log-in page: the
    /// footer line), as wide as the form, and grows upwards, so it never covers a box or a button. It goes away by itself after a few
    /// seconds, or when tapped. An optional link ("Try again") runs an action.
    ///
    /// Made at run time (no scene has to be rebuilt): FormalToast.Above(card, anchor, widthFrom, fontFrom). Without a card
    /// (Toast.OnScreen) it sits at the top of the screen, over everything, for messages that outlive a closed pop-up.
    /// </summary>
    public class FormalToast : MonoBehaviour, IPointerClickHandler
    {
        public enum Kind { Error, Info, Success }

        [SerializeField] private float showSeconds = 4f;

        Image box, icon;
        TextMeshProUGUI words, link;
        Action onLink;
        float hideAt;
        CanvasGroup fade;

        /// <summary>
        /// A toast inside `card`, its bottom edge `gap` units above the top of `anchor`, its left and right edges those of `widthFrom`
        /// (the form's boxes) or, without it, the card's with 48 units of margin.
        /// </summary>
        public static FormalToast Above(RectTransform card, RectTransform anchor, RectTransform widthFrom, TMP_Text fontFrom, float gap = 16f)
        {
            if (card == null) return null;
            FormalToast toast = Build(card, fontFrom);
            var rt = (RectTransform)toast.transform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            float left = 48f, right = 48f;
            if (widthFrom != null)
            {
                Vector3[] corners = new Vector3[4];
                widthFrom.GetWorldCorners(corners);
                Vector3 a = card.InverseTransformPoint(corners[0]), b = card.InverseTransformPoint(corners[2]);
                left = Mathf.Max(0f, a.x - card.rect.xMin); right = Mathf.Max(0f, card.rect.xMax - b.x);
            }
            float bottom = 160f;
            if (anchor != null)
            {
                Vector3[] corners = new Vector3[4];
                anchor.GetWorldCorners(corners);
                bottom = card.InverseTransformPoint(corners[1]).y - card.rect.yMin + gap;
            }
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, bottom);
            return toast;
        }

        /// <summary>A toast at the top of the screen, over everything (for a message that stays after a pop-up closed).</summary>
        public static FormalToast OnScreen(Transform anywhere, TMP_Text fontFrom)
        {
            Canvas canvas = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            RectTransform layer = RuntimeUiKit.Rect("ToastLayer", top);
            RuntimeUiKit.Stretch(layer);
            var sort = layer.gameObject.AddComponent<Canvas>();
            sort.overrideSorting = true; sort.sortingOrder = 6000;
            layer.gameObject.AddComponent<GraphicRaycaster>();
            FormalToast toast = Build(layer, fontFrom);
            var rt = (RectTransform)toast.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(56f, -260f); rt.offsetMax = new Vector2(-56f, -260f);
            return toast;
        }

        static FormalToast Build(RectTransform parent, TMP_Text fontFrom)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            RectTransform rt = RuntimeUiKit.Rect("FormalToast", parent);
            rt.SetAsLastSibling();
            var toast = rt.gameObject.AddComponent<FormalToast>();
            toast.box = rt.gameObject.AddComponent<Image>();
            toast.box.sprite = RuntimeIcons.RoundedBox; toast.box.type = Image.Type.Sliced; toast.box.raycastTarget = true;
            toast.fade = rt.gameObject.AddComponent<CanvasGroup>();
            var row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(32, 32, 22, 22); row.spacing = 20; row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            toast.icon = RuntimeUiKit.Icon(rt, "Icon", RuntimeIcons.Warning, p.warning, 44);
            var iconElement = toast.icon.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = 44; iconElement.minHeight = iconElement.preferredHeight = 44;

            RectTransform column = RuntimeUiKit.Rect("Text", rt);
            var columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            columnLayout.childControlWidth = true; columnLayout.childControlHeight = true; columnLayout.childForceExpandWidth = true; columnLayout.childForceExpandHeight = false;
            columnLayout.spacing = 6; columnLayout.childAlignment = TextAnchor.MiddleCenter;
            column.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            toast.words = RuntimeUiKit.Label(column, "Message", "", 34, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Center);
            toast.words.enableWordWrapping = true; toast.words.overflowMode = TextOverflowModes.Overflow;
            toast.link = RuntimeUiKit.Label(column, "Link", "", 34, p.accentText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Center);
            toast.link.fontStyle = FontStyles.Bold | FontStyles.Underline;
            toast.link.raycastTarget = true;
            toast.link.gameObject.AddComponent<Button>().onClick.AddListener(toast.RunLink);
            toast.link.gameObject.SetActive(false);

            rt.gameObject.SetActive(false);
            return toast;
        }

        /// <summary>Shows `message`. `kind` sets the icon and colour; `linkWords` + `action` add a link under the message.</summary>
        public void Show(Kind kind, string message, string linkWords = null, Action action = null)
        {
            if (string.IsNullOrWhiteSpace(message)) { Hide(); return; }
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Color tone = kind == Kind.Error ? p.warning : kind == Kind.Success ? p.success : p.accentText;
            box.color = RuntimeTheme.Mix(p.surface, tone, RuntimeTheme.IsDark ? 0.22f : 0.12f);
            icon.sprite = kind == Kind.Error ? RuntimeIcons.Warning : kind == Kind.Success ? RuntimeIcons.Check : RuntimeIcons.Info;
            icon.color = tone;
            words.color = p.mainText;
            words.text = message;
            onLink = action;
            bool hasLink = !string.IsNullOrEmpty(linkWords) && action != null;
            link.gameObject.SetActive(hasLink);
            if (hasLink) link.text = linkWords;
            // with a link it stays until used or tapped away (a link that vanishes after 4 seconds cannot be reached)
            hideAt = hasLink ? float.MaxValue : Time.unscaledTime + Mathf.Max(1f, showSeconds);
            fade.alpha = 1f;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        public bool IsShowing => gameObject.activeSelf;

        void Update()
        {
            float left = hideAt - Time.unscaledTime;
            if (left <= 0f) { Hide(); return; }
            fade.alpha = left < 0.25f ? left / 0.25f : 1f;        // a quick fade at the very end, no movement
        }

        void RunLink() { Action a = onLink; Hide(); a?.Invoke(); }

        public void OnPointerClick(PointerEventData eventData) { if (eventData.pointerPressRaycast.gameObject != link.gameObject) Hide(); }
    }
}
