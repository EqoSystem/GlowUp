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
    /// A bottom sheet built at run time (no scene change): a title, a text and a list of choices. A choice can open the next
    /// page of the same sheet (Show again), so one sheet serves a short question flow (the skin type finder) or a list of
    /// questions with their answers (Ask about this product).
    /// Ways out (docs/NAVIGATION_RULES.md, rule 2): the X at the top right, Android Back, a tap on the dimmed backdrop and a
    /// swipe down on the sheet. None of them changes anything: closing never confirms.
    /// </summary>
    public class ChoiceSheet : MonoBehaviour
    {
        public struct Choice
        {
            public string label;
            public Action run;
            public bool primary;
            public Choice(string label, Action run, bool primary = false) { this.label = label; this.run = run; this.primary = primary; }
        }

        // Every part is a serialized field: the sheet is made once in the scene by the scene builders (object "ChoiceSheetLayer" under the
        // canvas, switched off), so it can be seen and restyled in the Unity Editor. Run time only fills it in; a scene that has none still gets
        // one built on first use (the old way), so nothing breaks in a scene that was not rebuilt yet.
        [SerializeField] RectTransform layer, card, list;
        [SerializeField] Image dim, cardBack, grip;
        [SerializeField] TextMeshProUGUI title, body, foot;
        [SerializeField] Button close, backdrop;
        [SerializeField] CanvasGroup group;
        [SerializeField] ScrollRect scroll;
        bool open, hooked;
        float shownAt;
        Action closed;
        readonly List<GameObject> rows = new List<GameObject>();

        public bool IsOpen => open;

        /// <summary>The sheet of the canvas `anywhere` belongs to (made once, then reused).</summary>
        public static ChoiceSheet Get(Transform anywhere)
        {
            Canvas canvas = anywhere != null ? anywhere.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            Transform known = top.Find("ChoiceSheetLayer");
            if (known != null && known.TryGetComponent(out ChoiceSheet found)) return found;
            return Build(top);
        }

        /// <summary>The sheet under this canvas root: the one that is already there, or a new one (used by the scene builders and by Get).</summary>
        public static ChoiceSheet BuildInto(Transform canvasRoot)
        {
            Transform known = canvasRoot.Find("ChoiceSheetLayer");
            if (known != null && known.TryGetComponent(out ChoiceSheet found)) return found;
            return Build(canvasRoot);
        }

        void EnsureHooked()
        {
            if (hooked) return;
            hooked = true;
            if (backdrop != null) backdrop.onClick.AddListener(() => { if (Time.unscaledTime - shownAt > 0.35f) Hide(); });
            if (close != null) close.onClick.AddListener(Hide);
        }

        /// <summary>Called by the swipe-down handler: the card follows the finger.</summary>
        public void SetCardOffset(float down) { if (card != null) card.anchoredPosition = new Vector2(0f, -down); }

        static ChoiceSheet Build(Transform canvasRoot)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            TMP_Text fontFrom = canvasRoot.GetComponentInChildren<TMP_Text>(true);

            RectTransform layer = RuntimeUiKit.Rect("ChoiceSheetLayer", canvasRoot);
            RuntimeUiKit.Stretch(layer);
            var sort = layer.gameObject.AddComponent<Canvas>();
            sort.overrideSorting = true; sort.sortingOrder = 7050;
            layer.gameObject.AddComponent<GraphicRaycaster>();
            var sheet = layer.gameObject.AddComponent<ChoiceSheet>();
            sheet.layer = layer;
            sheet.group = layer.gameObject.AddComponent<CanvasGroup>();

            sheet.dim = layer.gameObject.AddComponent<Image>();
            sheet.dim.color = new Color(0f, 0f, 0f, 0.5f);
            var backdrop = layer.gameObject.AddComponent<Button>();
            backdrop.transition = Selectable.Transition.None;
            sheet.backdrop = backdrop;

            // the card: bottom of the screen, at most 82 % of its height; the text and the choices scroll inside it
            sheet.cardBack = RuntimeUiKit.Box(layer, "Card", p.surface);
            sheet.cardBack.raycastTarget = true;
            sheet.card = sheet.cardBack.rectTransform;
            sheet.card.anchorMin = new Vector2(0f, 0f); sheet.card.anchorMax = new Vector2(1f, 0f); sheet.card.pivot = new Vector2(0.5f, 0f);
            sheet.card.offsetMin = new Vector2(0f, -40f); sheet.card.offsetMax = new Vector2(0f, 0f);
            sheet.card.gameObject.AddComponent<ChoiceSheetSwipeDown>().sheet = sheet;

            sheet.grip = RuntimeUiKit.Box(sheet.card, "Grip", p.border);
            RectTransform gripRt = sheet.grip.rectTransform;
            gripRt.anchorMin = gripRt.anchorMax = gripRt.pivot = new Vector2(0.5f, 1f);
            gripRt.sizeDelta = new Vector2(120f, 12f); gripRt.anchoredPosition = new Vector2(0f, -22f);

            sheet.title = RuntimeUiKit.Label(sheet.card, "Title", "", 50, p.heading, RuntimeTheme.Font(FontRole.Heading, fontFrom), TextAlignmentOptions.TopLeft);
            sheet.title.fontStyle = FontStyles.Bold;
            RectTransform titleRt = sheet.title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f); titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(56f, -190f); titleRt.offsetMax = new Vector2(-190f, -56f);
            sheet.title.enableAutoSizing = true; sheet.title.fontSizeMin = 36; sheet.title.fontSizeMax = 50;

            // X: top right, 132 x 132 units (44 dp)
            Image closeBack = RuntimeUiKit.Box(sheet.card, "Close", p.surfaceAlt);
            closeBack.sprite = RuntimeIcons.Circle; closeBack.type = Image.Type.Simple; closeBack.raycastTarget = true;
            RectTransform closeRt = closeBack.rectTransform;
            closeRt.anchorMin = closeRt.anchorMax = closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(132f, 132f); closeRt.anchoredPosition = new Vector2(-36f, -40f);
            sheet.close = closeBack.gameObject.AddComponent<Button>();
            sheet.close.targetGraphic = closeBack;
            TextMeshProUGUI cross = RuntimeUiKit.Label(closeRt, "X", "×", 76, p.mainText, RuntimeTheme.Font(FontRole.Button, fontFrom));
            RuntimeUiKit.Stretch(cross.rectTransform, 0, 0, 0, 8);

            // scrolling area
            RectTransform view = RuntimeUiKit.Rect("View", sheet.card);
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(0f, 40f); view.offsetMax = new Vector2(0f, -200f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            sheet.scroll = view.gameObject.AddComponent<ScrollRect>();
            sheet.scroll.horizontal = false; sheet.scroll.movementType = ScrollRect.MovementType.Clamped; sheet.scroll.scrollSensitivity = 40f;
            RectTransform content = RuntimeUiKit.Rect("Content", view);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var column = content.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(56, 56, 0, 56); column.spacing = 22;
            column.childControlWidth = column.childControlHeight = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sheet.scroll.viewport = view; sheet.scroll.content = content;

            sheet.body = RuntimeUiKit.Label(content, "Body", "", 38, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.TopLeft);
            sheet.body.richText = true;
            sheet.list = RuntimeUiKit.Rect("Choices", content);
            var listColumn = sheet.list.gameObject.AddComponent<VerticalLayoutGroup>();
            listColumn.spacing = 18; listColumn.childControlWidth = listColumn.childControlHeight = true;
            listColumn.childForceExpandWidth = true; listColumn.childForceExpandHeight = false;
            sheet.foot = RuntimeUiKit.Label(content, "Foot", "", 30, p.secondaryText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.TopLeft);

            layer.gameObject.SetActive(false);
            return sheet;
        }

        /// <summary>Shows a page: `text` may use TMP rich text; `note` is the small line under the choices ("" = none).</summary>
        public void Show(string heading, string text, IList<Choice> choices, string note = "", Action whenClosed = null)
        {
            EnsureHooked();
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            TMP_Text fontFrom = title;
            cardBack.color = p.surface; title.color = p.heading; body.color = p.mainText; foot.color = p.secondaryText; grip.color = p.border;
            title.text = heading ?? "";
            body.text = text ?? "";
            body.gameObject.SetActive(!string.IsNullOrEmpty(text));
            foot.text = note ?? "";
            foot.gameObject.SetActive(!string.IsNullOrEmpty(note));

            foreach (GameObject row in rows) if (row != null) Destroy(row);
            rows.Clear();
            if (choices != null)
                foreach (Choice choice in choices)
                {
                    Choice c = choice;
                    Color fill = p.buttonFill;
                    Button button = RuntimeUiKit.Pill(list, "Choice", c.label, fill, c.primary ? ContrastInk.On(fill) : p.accentText, RuntimeTheme.Font(FontRole.Button, fontFrom), !c.primary);
                    TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
                    label.enableWordWrapping = true; label.fontSize = 36; label.fontStyle = c.primary ? FontStyles.Bold : FontStyles.Normal;
                    label.alignment = c.primary ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
                    RuntimeUiKit.Stretch(label.rectTransform, 40, 40, 16, 16);
                    LayoutElement element = button.GetComponent<LayoutElement>();
                    element.preferredHeight = Mathf.Max(132f, label.GetPreferredValues(c.label, 880f, 0f).y + 48f);
                    button.onClick.AddListener(() => c.run?.Invoke());
                    rows.Add(button.gameObject);
                }

            if (whenClosed != null || !open) closed = whenClosed;
            float h = layer.rect.height > 0 ? layer.rect.height : 2340f;
            card.sizeDelta = new Vector2(card.sizeDelta.x, Mathf.Round(h * 0.82f));
            if (!open) shownAt = Time.unscaledTime;
            open = true;
            layer.gameObject.SetActive(true);
            layer.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            float needed = ((RectTransform)scroll.content).rect.height + 260f;
            card.sizeDelta = new Vector2(card.sizeDelta.x, Mathf.Clamp(needed, 520f, Mathf.Round(h * 0.82f)));
            scroll.verticalNormalizedPosition = 1f;
            card.anchoredPosition = Vector2.zero;
            group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true;
        }

        public void Hide()
        {
            if (!open) return;
            open = false;
            layer.gameObject.SetActive(false);
            Action then = closed; closed = null;
            then?.Invoke();
        }

        void Update()
        {
            if (open && Input.GetKeyDown(KeyCode.Escape)) Hide();          // Android Back closes the sheet, never more
        }
    }
}
