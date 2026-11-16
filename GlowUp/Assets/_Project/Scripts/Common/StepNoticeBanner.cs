using System.Collections.Generic;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// A MESSAGE THAT MUST STAY (New13 item 5C, 10 November 2026), e.g. "You signed in with Google ...": a banner at the TOP of a step's
    /// content, in the flow of the page. It pushes the content below it down (top-anchored parts move down, parts that stretch between the
    /// top and the bottom get shorter), so it never lies over anything, and the footer (Continue) does not move. An X dismisses it; the
    /// content moves back up. Shown once per step and key; a dismissed banner stays away for the rest of the app's run.
    ///
    /// The onboarding cards are laid out by position (not by layout groups), hence the push. Made at run time: no scene rebuild.
    /// </summary>
    public class StepNoticeBanner : MonoBehaviour
    {
        static readonly HashSet<string> dismissed = new HashSet<string>();

        class Moved { public RectTransform rt; public bool top; public Vector2 anchoredPosition, offsetMax; }
        readonly List<Moved> moved = new List<Moved>();
        RectTransform card;
        string key;
        float pushedBy;

        /// <summary>True when the banner with this key was dismissed in this run of the app.</summary>
        public static bool WasDismissed(string key) => dismissed.Contains(key ?? "");

        /// <summary>
        /// Shows `message` at `topY` units below the top of `card` (the first free line under the page's header), unless it was dismissed.
        /// `inset` = the content's own left/right margin. Returns null when nothing was shown.
        /// </summary>
        public static StepNoticeBanner Show(RectTransform card, float topY, float inset, string message, string key, TMP_Text fontFrom)
        {
            if (card == null || string.IsNullOrWhiteSpace(message) || WasDismissed(key)) return null;
            Transform old = card.Find("StepNoticeBanner");
            if (old != null) { var b = old.GetComponent<StepNoticeBanner>(); if (b != null) b.Remove(false); else Destroy(old.gameObject); }

            ThemeProfile.Palette p = RuntimeTheme.Palette;
            Image back = RuntimeUiKit.Box(card, "StepNoticeBanner", RuntimeTheme.Mix(p.surface, p.accentText, RuntimeTheme.IsDark ? 0.20f : 0.09f));
            back.raycastTarget = true;
            var banner = back.gameObject.AddComponent<StepNoticeBanner>();
            banner.card = card; banner.key = key ?? "";
            RectTransform rt = back.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);

            var outline = back.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(p.accentText.r, p.accentText.g, p.accentText.b, 0.55f); outline.effectDistance = new Vector2(2f, -2f);

            var row = back.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 8, 14, 14); row.spacing = 14; row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;

            Image icon = RuntimeUiKit.Icon(rt, "Icon", RuntimeIcons.Info, p.accentText, 36);
            var iconElement = icon.gameObject.AddComponent<LayoutElement>();
            iconElement.minWidth = iconElement.preferredWidth = 36; iconElement.minHeight = iconElement.preferredHeight = 36;
            TextMeshProUGUI words = RuntimeUiKit.Label(rt, "Words", message, 28, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.MidlineLeft);
            words.enableWordWrapping = true;
            words.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // the X: 132 units (44 dp) to tap, a small cross drawn as two bars
            RectTransform close = RuntimeUiKit.Rect("DismissButton", rt);
            var closeHit = close.gameObject.AddComponent<Image>(); closeHit.color = new Color(1f, 1f, 1f, 0.001f);
            var closeElement = close.gameObject.AddComponent<LayoutElement>();
            closeElement.minWidth = closeElement.preferredWidth = 112; closeElement.minHeight = closeElement.preferredHeight = 112;
            for (int i = 0; i < 2; i++)
            {
                Image bar = RuntimeUiKit.Box(close, "Bar" + i, p.mainText, false);
                bar.raycastTarget = false;
                bar.rectTransform.sizeDelta = new Vector2(34f, 4f);
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            }
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = closeHit;
            button.onClick.AddListener(() => banner.Remove(true));

            // its width first, then its height from its words, then the push
            rt.offsetMin = new Vector2(inset, 0f); rt.offsetMax = new Vector2(-inset, 0f);
            float width = card.rect.width - 2f * inset;
            float textWidth = Mathf.Max(100f, width - 24 - 8 - 36 - 112 - 28);
            float textHeight = words.GetPreferredValues(message, textWidth, 0f).y;
            float height = Mathf.Max(112f, textHeight) + 28f;
            rt.sizeDelta = new Vector2(-2f * inset, height);
            rt.anchoredPosition = new Vector2(0f, -topY);
            banner.Push(topY, height + 16f);
            return banner;
        }

        /// <summary>Moves every part of the card that starts at or below `topY` down by `by` (stretching parts lose that much height).</summary>
        void Push(float topY, float by)
        {
            pushedBy = by;
            Vector3[] corners = new Vector3[4];
            float cardTop = card.rect.yMax;
            foreach (Transform child in card)
            {
                if (child == transform || !(child is RectTransform rt)) continue;
                rt.GetWorldCorners(corners);
                float childTop = cardTop - card.InverseTransformPoint(corners[1]).y;     // units from the card's top to the child's top edge
                if (childTop < topY - 1f) continue;
                bool topAnchored = Mathf.Approximately(rt.anchorMin.y, 1f) && Mathf.Approximately(rt.anchorMax.y, 1f);
                bool stretched = rt.anchorMin.y < rt.anchorMax.y - 0.001f;
                if (!topAnchored && !stretched) continue;                                 // bottom-anchored (the footer) stays
                moved.Add(new Moved { rt = rt, top = topAnchored, anchoredPosition = rt.anchoredPosition, offsetMax = rt.offsetMax });
                if (topAnchored) rt.anchoredPosition -= new Vector2(0f, by);
                else rt.offsetMax -= new Vector2(0f, by);
            }
        }

        /// <summary>Takes the banner away and puts the content back. `byUser` = the X was tapped (it stays away).</summary>
        public void Remove(bool byUser)
        {
            if (byUser) dismissed.Add(key);
            foreach (Moved m in moved)
            {
                if (m.rt == null) continue;
                if (m.top) m.rt.anchoredPosition = m.anchoredPosition;
                else m.rt.offsetMax = m.offsetMax;
            }
            moved.Clear();
            Destroy(gameObject);
        }

        public float PushedBy => pushedBy;
    }
}
