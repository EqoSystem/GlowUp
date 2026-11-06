using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// ONE STATUS MESSAGE FOR THE WHOLE APP (7 November 2026). A short notice in a soft tinted box, with an icon on the left:
    ///   Info      - neutral tint, (i) icon
    ///   Progress  - accent tint, a turning spinner
    ///   Error     - warning tint, triangle icon (also used for warnings)
    ///   Success   - green tint, check icon
    /// The text is at least 40 units (about 13 sp at GlowUp's 1080-wide layout) in the theme's main text colour on a nearly white
    /// (or nearly black) tint, so it passes WCAG AA in both themes. An optional link at the end ("Type it in") is a real tap target.
    ///
    /// It is laid around an EXISTING text of a screen (<see cref="Wrap"/>), so screens built earlier get it without a rebuild: the
    /// text moves into a new box that takes its place. Show / Hide only; the words are always set as plain text.
    /// </summary>
    public class StatusMessage : MonoBehaviour, IPointerClickHandler
    {
        public enum Kind { Info, Progress, Error, Success }

        /// <summary>Smallest text size: 40 units = about 13 sp.</summary>
        public const float MinimumFontSize = 40f;

        TMP_Text text;
        Image tint, icon;
        RectTransform spinner;
        Kind kind = Kind.Info;
        Action linkAction;
        string message = "", linkWords = "";

        public TMP_Text Text => text;
        public bool Visible => gameObject.activeSelf;

        /// <summary>The status box around `existing` (made once; later calls return the same box).</summary>
        public static StatusMessage Wrap(TMP_Text existing)
        {
            if (existing == null) return null;
            var already = existing.transform.parent != null ? existing.transform.parent.GetComponent<StatusMessage>() : null;
            if (already != null && already.text == existing) return already;

            var original = (RectTransform)existing.transform;
            Transform parent = original.parent;
            RectTransform box = RuntimeUiKit.Rect("StatusMessage", parent);
            box.SetSiblingIndex(original.GetSiblingIndex());
            bool inLayout = parent != null && parent.GetComponent<LayoutGroup>() != null;
            if (inLayout)
            {
                var element = box.gameObject.AddComponent<LayoutElement>();
                var old = existing.GetComponent<LayoutElement>();
                if (old != null) { element.flexibleWidth = old.flexibleWidth; element.ignoreLayout = old.ignoreLayout; }
            }
            else
            {
                // the same place as the text, growing downwards from its top edge as the message needs
                box.anchorMin = original.anchorMin; box.anchorMax = original.anchorMax;
                box.pivot = new Vector2(original.pivot.x, 1f);
                box.sizeDelta = original.sizeDelta;
                float topShift = (1f - original.pivot.y) * original.rect.height;
                box.anchoredPosition = original.anchoredPosition + new Vector2(0f, topShift);
                var fitter = box.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(28, 28, 20, 20); row.spacing = 18; row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;

            var status = box.gameObject.AddComponent<StatusMessage>();
            status.tint = box.gameObject.AddComponent<Image>();
            status.tint.sprite = RuntimeIcons.RoundedBox; status.tint.type = Image.Type.Sliced; status.tint.raycastTarget = false;

            // the icon (or the spinner) on the left, the same size whatever the message
            RectTransform slot = RuntimeUiKit.Rect("Icon", box);
            var slotElement = slot.gameObject.AddComponent<LayoutElement>();
            slotElement.minWidth = slotElement.preferredWidth = 48; slotElement.minHeight = slotElement.preferredHeight = 48;
            status.icon = RuntimeUiKit.Icon(slot, "Glyph", RuntimeIcons.Info, Color.white, 48);
            status.spinner = RuntimeUiKit.Icon(slot, "Spinner", RuntimeIcons.SpinnerArc, Color.white, 48).rectTransform;
            status.spinner.gameObject.AddComponent<Spinner>();

            original.SetParent(box, false);
            var textElement = existing.GetComponent<LayoutElement>();
            if (textElement == null) textElement = existing.gameObject.AddComponent<LayoutElement>();
            textElement.flexibleWidth = 1; textElement.ignoreLayout = false;
            existing.enableAutoSizing = false;
            existing.fontSize = Mathf.Max(existing.fontSize, MinimumFontSize);
            existing.enableWordWrapping = true;
            existing.overflowMode = TextOverflowModes.Overflow;
            existing.alignment = TextAlignmentOptions.MidlineLeft;
            existing.fontStyle = FontStyles.Normal;
            existing.margin = Vector4.zero;
            existing.raycastTarget = true;                                   // for the link
            // a shrink-to-fit helper of the old layout would fight the box
            foreach (var behaviour in existing.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().Name.Contains("Shrink")) behaviour.enabled = false;
            status.text = existing;
            status.Repaint();
            box.gameObject.SetActive(existing.text.Length > 0);
            return status;
        }

        void OnEnable() { ThemeManager.ThemeChanged += OnTheme; }
        void OnDisable() { ThemeManager.ThemeChanged -= OnTheme; }
        void OnTheme(bool dark) => Repaint();

        /// <summary>Shows a message. `link` (optional) is added after it as a tappable link that runs `onLink`.</summary>
        public void Show(Kind what, string words, string link = null, Action onLink = null)
        {
            kind = what; message = words ?? ""; linkWords = link ?? ""; linkAction = onLink;
            if (message.Length == 0 && linkWords.Length == 0) { Hide(); return; }
            gameObject.SetActive(true);
            Repaint();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

        public void Hide()
        {
            message = ""; linkWords = ""; linkAction = null;
            if (text != null) text.text = "";
            gameObject.SetActive(false);
        }

        void Repaint()
        {
            if (text == null) return;
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Color accent = kind == Kind.Error ? p.warning : kind == Kind.Success ? p.success : kind == Kind.Progress ? p.accent : p.secondaryText;
            // a light tint of the kind's colour on the card: the text keeps full contrast on it
            tint.color = RuntimeTheme.Mix(p.surface, accent, RuntimeTheme.IsDark ? 0.18f : 0.10f);
            Color iconColor = kind == Kind.Error ? p.warning : kind == Kind.Success ? p.success : kind == Kind.Progress ? p.accentText : p.secondaryText;
            icon.gameObject.SetActive(kind != Kind.Progress);
            spinner.gameObject.SetActive(kind == Kind.Progress);
            icon.sprite = kind == Kind.Error ? RuntimeIcons.Warning : kind == Kind.Success ? RuntimeIcons.Check : RuntimeIcons.Info;
            icon.color = iconColor;
            spinner.GetComponent<Image>().color = iconColor;
            text.color = p.mainText;
            string shown = message;
            if (linkWords.Length > 0)
            {
                string hex = ColorUtility.ToHtmlStringRGB(p.accentText);
                shown = (shown.Length > 0 ? shown + " " : "") + "<link=\"act\"><color=#" + hex + "><u><b>" + Plain(linkWords) + "</b></u></color></link>";
            }
            text.richText = true;
            text.text = shown;
        }

        /// <summary>The words given as plain text: markup characters cannot change how the message looks.</summary>
        public static string Plain(string words) => (words ?? "").Replace("<", "‹").Replace(">", "›");

        public void OnPointerClick(PointerEventData eventData)
        {
            if (linkAction == null || text == null) return;
            int at = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, eventData.pressEventCamera);
            // a tap anywhere on a message that only offers its link counts too (a larger target than the words)
            if (at >= 0 || message.Length == 0 || kind == Kind.Error) linkAction.Invoke();
        }
    }
}
