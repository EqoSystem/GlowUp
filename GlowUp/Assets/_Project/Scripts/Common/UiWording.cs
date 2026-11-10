using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Words used on more than one screen, and a way to correct a label in a scene that was built before the wording changed
    /// (so the new words show without rebuilding the scene). "Terms and Conditions" is the one name used everywhere (10 November 2026).
    /// </summary>
    public static class UiWording
    {
        public const string LegalTitle = "Privacy Policy and Terms and Conditions";
        public const string PrivacyPolicy = "Privacy Policy";
        public const string TermsAndConditions = "Terms and Conditions";

        /// <summary>Every text in the open scenes that reads exactly `from` now reads `to`; a longer text shrinks to fit its box instead of spilling.</summary>
        public static void Retitle(string from, string to)
        {
            foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>(true))
            {
                if (text == null || text.text != from) continue;
                if (!text.enableAutoSizing && to.Length > from.Length)
                {
                    text.fontSizeMax = text.fontSize;
                    text.fontSizeMin = Mathf.Max(18f, text.fontSize * 0.7f);
                    text.enableAutoSizing = true;
                }
                text.text = to;
            }
        }

        /// <summary>Scrolls `scroll` so the first line of `text` that contains `phrase` is at the top. False when the phrase is not there.</summary>
        public static bool ScrollToPhrase(UnityEngine.UI.ScrollRect scroll, TMP_Text text, string phrase)
        {
            if (scroll == null || text == null || scroll.content == null || string.IsNullOrEmpty(phrase)) return false;
            text.ForceMeshUpdate();
            string parsed = text.GetParsedText();
            // a heading stands on a line of its own: the first place where the phrase starts a line (else anywhere)
            int at = parsed.StartsWith(phrase, System.StringComparison.Ordinal) ? 0 : parsed.IndexOf("\n" + phrase, System.StringComparison.Ordinal);
            if (at > 0) at += 1;
            if (at < 0) at = parsed.IndexOf(phrase, System.StringComparison.Ordinal);
            if (at < 0 || at >= text.textInfo.characterCount) return false;
            int line = text.textInfo.characterInfo[at].lineNumber;
            if (line < 0 || line >= text.textInfo.lineCount) return false;
            // the line's top, in the content's own space, measured down from the content's top edge
            Vector3 lineTop = text.rectTransform.TransformPoint(new Vector3(0f, text.textInfo.lineInfo[line].ascender, 0f));
            Vector3 local = scroll.content.InverseTransformPoint(lineTop);
            float fromTop = scroll.content.rect.yMax - local.y;
            RectTransform view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float room = scroll.content.rect.height - view.rect.height;
            if (room <= 0f) return true;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - (fromTop - 8f) / room);
            return true;
        }
    }
}
