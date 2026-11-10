using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A HINT OR ERROR THAT BELONGS TO ONE FIELD (New13 item 5A, 10 November 2026): an icon and the words, directly under the field, in
    /// the layout flow of the field's own column (a VerticalLayoutGroup), so it pushes what follows down and never lies over anything.
    /// Not colour alone: an error has the warning triangle, a hint the "i". It wraps.
    ///
    /// Made at run time inside an existing column: FieldHint.In(column, fontFrom). Shown with Show(...), removed from the flow with Hide().
    /// </summary>
    public class FieldHint : MonoBehaviour
    {
        public enum Kind { Hint, Error }

        Image icon;
        TextMeshProUGUI words;
        Transform after;                // null = last child of its column; else it stands right after this sibling

        /// <summary>
        /// The hint of one field in a form whose fields are siblings of one column (Step 1): it stands right after `field` (or after
        /// `field`'s own line, when the field is wrapped). Made once per field.
        /// </summary>
        public static FieldHint After(Transform field, TMP_Text fontFrom, float fontSize = 28f)
        {
            if (field == null || field.parent == null) return null;
            Transform column = field.parent;
            string name = "FieldHint_" + field.name;
            Transform found = column.Find(name);
            if (found != null && found.TryGetComponent(out FieldHint known)) return known;
            FieldHint hint = Make(column, fontFrom, fontSize);
            hint.name = name;
            hint.after = field;
            return hint;
        }

        /// <summary>The hint of `column` (made once; it is the column's last child). `column` should lay out its children (VerticalLayoutGroup).</summary>
        public static FieldHint In(Transform column, TMP_Text fontFrom, float fontSize = 28f)
        {
            if (column == null) return null;
            FieldHint existing = null;
            foreach (Transform child in column) if (child.name == "FieldHint") { existing = child.GetComponent<FieldHint>(); break; }
            if (existing != null) return existing;
            return Make(column, fontFrom, fontSize);
        }

        static FieldHint Make(Transform column, TMP_Text fontFrom, float fontSize)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            RectTransform rt = RuntimeUiKit.Rect("FieldHint", column);
            var hint = rt.gameObject.AddComponent<FieldHint>();
            var row = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 2, 6); row.spacing = 10; row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            hint.icon = RuntimeUiKit.Icon(rt, "Icon", RuntimeIcons.Warning, p.warning, fontSize);
            var iconElement = hint.icon.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = fontSize; iconElement.minHeight = iconElement.preferredHeight = fontSize;
            hint.words = RuntimeUiKit.Label(rt, "Words", "", fontSize, p.warning, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.TopLeft);
            hint.words.enableWordWrapping = true;
            hint.words.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            rt.gameObject.SetActive(false);
            return hint;
        }

        public void Show(Kind kind, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) { Hide(); return; }
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Color tone = kind == Kind.Error ? p.warning : p.secondaryText;
            icon.sprite = kind == Kind.Error ? RuntimeIcons.Warning : RuntimeIcons.Info;
            icon.color = tone; words.color = tone;
            words.text = message;
            if (after != null) transform.SetSiblingIndex(Mathf.Min(transform.parent.childCount - 1, after.GetSiblingIndex() + (transform.GetSiblingIndex() > after.GetSiblingIndex() ? 1 : 0)));
            else transform.SetAsLastSibling();
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            if (transform.parent != null) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
        }

        public bool IsShowing => gameObject.activeSelf;
        public string Text => words != null ? words.text : "";
    }
}
