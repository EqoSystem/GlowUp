using GlowUp.Theme;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// What all "skeleton" shapes of the app share (TextSkeleton, SkeletonShimmer, the sheet between two screens), so they
    /// look and behave the same as on the admin website: a blush base, a rose-to-gold shine travelling across, and one
    /// rule for when the shapes may give way to the real content.
    ///
    /// The shine itself is drawn by the shader GlowUp/SkeletonShine. This class hands it the theme's colours and the time.
    /// </summary>
    public static class SkeletonLook
    {
        /// <summary>
        /// How much of the pink goes into the base colour and how much of the gold into the shine. The website uses 0.11 and
        /// 0.26 on a white page; the app's page is paper-coloured and its panels are rose, so the base is a little stronger
        /// here and takes a touch of the text colour (`InkInBase`), or a bar could not be told from what it lies on.
        /// </summary>
        public static float PinkInBase = 0.2f, InkInBase = 0.05f, GoldInShine = 0.3f;

        static readonly int On = Shader.PropertyToID("_GlowUpSkOn"), Clock = Shader.PropertyToID("_GlowUpSkTime"), Still = Shader.PropertyToID("_GlowUpSkStill"),
                            BaseColour = Shader.PropertyToID("_GlowUpSkBase"), ShineColour = Shader.PropertyToID("_GlowUpSkShine");
        static int tickedOnFrame = -1;

        /// <summary>The colour of a shape at rest: the soft section colour with a little pink.</summary>
        public static Color Base(ThemeProfile.Palette p) { Color c = Color.Lerp(Color.Lerp(p.surfaceAlt, p.accent, PinkInBase), p.mainText, InkInBase); c.a = 1f; return c; }

        /// <summary>The colour of the shine: the soft section colour with gold.</summary>
        public static Color Shine(ThemeProfile.Palette p) { Color c = Color.Lerp(p.surfaceAlt, p.gold, GoldInShine); c.a = 1f; return c; }

        /// <summary>
        /// Called every frame by whatever skeleton is on the screen (more than one may call: only the first counts). Gives
        /// the shader the time, the theme's colours and whether the shine must stand still (Performance Mode).
        /// </summary>
        public static void Tick()
        {
            if (!Application.isPlaying || tickedOnFrame == Time.frameCount) return;
            tickedOnFrame = Time.frameCount;
            Shader.SetGlobalFloat(Clock, Time.unscaledTime);
            Shader.SetGlobalFloat(Still, GlowUp.Performance.PerformanceSettings.ReduceEffects ? 1f : 0f);
            ThemeManager theme = ThemeManager.Instance;
            if (theme == null || theme.Palette == null) { Shader.SetGlobalFloat(On, 0f); return; }
            // Handed over as plain numbers, already in the colour space the project draws in: a colour that is not listed in
            // a shader's Properties is not converted by Unity, and in a Linear project it would come out too pale.
            Shader.SetGlobalVector(BaseColour, ForShader(Base(theme.Palette)));
            Shader.SetGlobalVector(ShineColour, ForShader(Shine(theme.Palette)));
            Shader.SetGlobalFloat(On, 1f);
        }

        static Vector4 ForShader(Color colour) => QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;

        /// <summary>
        /// How strongly the shine shows at one place at one moment (0 = the base colour, 1 = the full shine). The same
        /// sum as in the shader, kept here so it can be checked. `across` = 0 at the left edge of the screen, 1 at the right;
        /// `up` = 0 at the bottom, 1 at the top; `width` = the width of the shine, 1 = as wide as the screen.
        /// </summary>
        public static float ShineAt(float across, float up, float time, float seconds = 1.5f, float width = 0.6f, float tilt = 0.18f)
        {
            seconds = Mathf.Max(seconds, 0.05f); width = Mathf.Max(width, 0.05f);
            float phase = Mathf.Repeat(time / seconds, 1f);
            float eased = phase * phase * (3f - 2f * phase);
            float middle = Mathf.Lerp(-width, 1f + width, eased);
            float away = Mathf.Clamp01(Mathf.Abs(across + (up - 0.5f) * tilt - middle) / width);
            return 1f - away * away * (3f - 2f * away);
        }

        /// <summary>
        /// True when the shapes may give way to the real content.
        ///   - never in the first two frames: every script of the screen has had its Start by then, and has said if it is fetching;
        ///   - at the latest after `longestSeconds` (the time limit: a screen never shows shapes for ever);
        ///   - otherwise when nobody is fetching any more and the shapes were seen for `shortestSeconds` (so they do not flash).
        /// </summary>
        public static bool MayEnd(float shownSeconds, int framesShown, int stillFetching, float shortestSeconds, float longestSeconds)
        {
            if (framesShown < 2) return false;
            if (longestSeconds > 0f && shownSeconds >= longestSeconds) return true;
            return stillFetching <= 0 && shownSeconds >= shortestSeconds;
        }

        /// <summary>
        /// For a screen set to "Bars Only When Slow": true when its words should become bars, because a fetch has been
        /// running for `slowAfterSeconds`. A screen whose data is there at once never gets bars.
        /// </summary>
        public static bool SlowEnough(int stillFetching, float fetchingSeconds, float slowAfterSeconds)
        {
            return stillFetching > 0 && fetchingSeconds >= Mathf.Max(0f, slowAfterSeconds);
        }

        /// <summary>The part of `bar` that lies inside `window` (a list's visible area). Empty when it is outside.</summary>
        public static Rect Inside(Rect bar, Rect window)
        {
            float xMin = Mathf.Max(bar.xMin, window.xMin), xMax = Mathf.Min(bar.xMax, window.xMax);
            float yMin = Mathf.Max(bar.yMin, window.yMin), yMax = Mathf.Min(bar.yMax, window.yMax);
            return xMax <= xMin || yMax <= yMin ? new Rect(xMin, yMin, 0f, 0f) : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
