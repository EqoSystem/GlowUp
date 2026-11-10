using GlowUp.Common;
using TMPro;
using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// How the sign-up pages say things (New13 item 5, 10 November 2026). Three kinds only:
    ///   - a field's hint or error: under that field (FieldHint), or in the short line under the Add row of Step 2 (kept to ONE line);
    ///   - a passing message (a short confirmation, an error after a tap): the one toast at the top (FormalToast);
    ///   - a message that must stay (the Google notice): a banner at the top of the page's content (StepNoticeBanner).
    /// The old free message line above Continue is no longer used for long sentences: it overflowed onto the boxes above it.
    /// </summary>
    public static class OnboardingNotices
    {
        /// <summary>The first free line under the onboarding header and the content's side margin (OnboardingKit.HeaderBottom / PadX).</summary>
        public const float ContentTop = 370f, ContentInset = 70f;

        /// <summary>The Google / Facebook notice as a banner at the top of the page (once per page; the X keeps it away).</summary>
        public static StepNoticeBanner SocialBanner(Component onCard, string pageKey, TMP_Text fontFrom)
        {
            if (!OnboardingSession.Social || onCard == null) return null;
            var card = onCard.transform.parent as RectTransform;
            return StepNoticeBanner.Show(card, ContentTop, ContentInset, OnboardingSession.SocialReminder, "social." + pageKey, fontFrom);
        }

        public static void Error(Component near, string message) => Toast(near, FormalToast.Kind.Error, message);
        public static void Info(Component near, string message) => Toast(near, FormalToast.Kind.Info, message);

        public static void Toast(Component near, FormalToast.Kind kind, string message)
        {
            if (near == null || string.IsNullOrWhiteSpace(message)) return;
            FormalToast toast = FormalToast.OnScreen(near.transform, near as TMP_Text);
            if (toast != null) toast.Show(kind, StatusMessage.Plain(message));
        }

        /// <summary>The page's old message line: ONE line that shrinks a little and then ends in "...", so it can never spill onto a box.</summary>
        public static void OneLine(TMP_Text line)
        {
            if (line == null) return;
            line.enableWordWrapping = false;
            line.overflowMode = TextOverflowModes.Ellipsis;
            if (!line.enableAutoSizing)
            {
                line.fontSizeMax = Mathf.Max(24f, line.fontSize);
                line.fontSizeMin = 22f;
                line.enableAutoSizing = true;
            }
        }
    }
}
