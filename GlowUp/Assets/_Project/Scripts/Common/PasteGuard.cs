using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// ONE RULE FOR "THIS CHANGE WAS A PASTE" (New20, 17 November 2026), used by every field that blocks pasting (the passwords
    /// of Log in and Onboarding Step 1).
    ///
    /// Before, any change that added MORE THAN ONE character at once was undone as a paste. On Android the field reads the phone
    /// keyboard's hidden text box once per frame, so fast typing often brings two or three letters in one frame: they were undone,
    /// and the next frame the hidden box (which still had them) put them back. The user saw letters vanish and come back.
    ///
    /// Now a change counts as a paste only when it adds more characters than a person can type in the time that frame took
    /// (at most <see cref="FastestTypingPerSecond"/> a second, and never fewer than <see cref="AlwaysAllowed"/>). After a stall of
    /// the app (a long frame) more letters are allowed, because they were typed during the stall. When a paste IS undone, the
    /// keyboard's hidden box is given the same text, so the paste does not come back on the next frame.
    /// Nothing typed is logged or kept here.
    /// </summary>
    public static class PasteGuard
    {
        /// <summary>Letters one change may always add (fast typing read in a single frame).</summary>
        public const int AlwaysAllowed = 3;
        /// <summary>Faster than any thumb typing: about 900 characters a minute.</summary>
        public const float FastestTypingPerSecond = 15f;

        static TMP_InputField allowOnce;

        /// <summary>The next change of `field` is made by the app itself (text put back), not typed or pasted: let it through.</summary>
        public static void AllowNextChange(TMP_InputField field) { allowOnce = field; }

        /// <summary>The most characters one change may add when its frame took `frameSeconds`.</summary>
        public static int MostTypedIn(float frameSeconds) =>
            Mathf.Max(AlwaysAllowed, Mathf.CeilToInt(Mathf.Max(0f, frameSeconds) * FastestTypingPerSecond) + 1);

        /// <summary>Plain rule, covered by a test: does going from `before` to `now` in a frame of `frameSeconds` look like a paste?</summary>
        public static bool LooksPasted(string before, string now, float frameSeconds) =>
            (now ?? "").Length - (before ?? "").Length > MostTypedIn(frameSeconds);

        /// <summary>True when this change of `field` should be undone as a paste.</summary>
        public static bool IsPaste(TMP_InputField field, string before, string now)
        {
            if (field != null && allowOnce == field) { allowOnce = null; return false; }
            return LooksPasted(before, now, Time.unscaledDeltaTime);
        }

        /// <summary>Puts `before` back in the field AND in the phone keyboard's hidden box, with the caret at its end.</summary>
        public static void Undo(TMP_InputField field, string before)
        {
            if (field == null) return;
            before = before ?? "";
            field.SetTextWithoutNotify(before);
            field.caretPosition = before.Length;
            InputCaretTouch.KeepCaret(field, before.Length);
        }
    }
}
