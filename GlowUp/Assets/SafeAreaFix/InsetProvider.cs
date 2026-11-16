using System;
using UnityEngine;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// THE ONE PLACE that knows how much of each screen edge the phone covers (12 November 2026). No screen works this out itself.
    /// Pixels, measured from each edge of the screen. Sources, in this order (the one used is in <see cref="Source"/>):
    ///   1. Screen.safeArea and Screen.cutouts (Unity; needs "Render outside safe area", which the project has on)
    ///   2. Android itself: the decor view's root window insets, DisplayCutout safe insets (Android 9+), when 1 shows no cutout
    ///   3. Android's "status_bar_height" resource, when there is still nothing at the top
    ///   4. SafeAreaConfig.manualTopInsetPx, when set
    /// Measured once per screen size / orientation (Android is asked through JNI, so not every frame).
    /// </summary>
    public static class InsetProvider
    {
        public struct Insets
        {
            public float top, bottom, left, right;
            public override string ToString() => $"top {top:0}, bottom {bottom:0}, left {left:0}, right {right:0}";
        }

        /// <summary>Where the insets came from: "Screen.safeArea", "Android DisplayCutout", "Android status_bar_height", "manual", "none".</summary>
        public static string Source { get; private set; } = "none";
        /// <summary>True when neither Unity nor Android reported a cutout (the overlay warns about it).</summary>
        public static bool NoCutoutReported { get; private set; }
        /// <summary>Android's API level (0 elsewhere).</summary>
        public static int AndroidApi { get; private set; }

        static Insets cached;
        static Vector2Int cachedFor;
        static ScreenOrientation cachedOrientation;
        static Rect cachedSafe;
        static int cachedManual = -1;

        /// <summary>The insets now (all edges, whatever the config applies).</summary>
        public static Insets Get()
        {
            SafeAreaConfig config = SafeAreaConfig.Current;
            if (cachedFor.x == Screen.width && cachedFor.y == Screen.height && cachedOrientation == Screen.orientation
                && cachedSafe == Screen.safeArea && cachedManual == config.manualTopInsetPx) return cached;
            cachedFor = new Vector2Int(Screen.width, Screen.height); cachedOrientation = Screen.orientation;
            cachedSafe = Screen.safeArea; cachedManual = config.manualTopInsetPx;
            cached = Measure(config);
            return cached;
        }

        /// <summary>Forget the measurement (the next Get asks again).</summary>
        public static void Invalidate() { cachedFor = Vector2Int.zero; }

        static Insets Measure(SafeAreaConfig config)
        {
            Rect safe = Screen.safeArea;
            var fromUnity = new Insets
            {
                left = Mathf.Max(0f, safe.xMin), bottom = Mathf.Max(0f, safe.yMin),
                right = Mathf.Max(0f, Screen.width - safe.xMax), top = Mathf.Max(0f, Screen.height - safe.yMax)
            };
            bool unityHasCutout = Screen.cutouts != null && Screen.cutouts.Length > 0;
            bool unityHasAny = fromUnity.top > 0f || fromUnity.bottom > 0f || fromUnity.left > 0f || fromUnity.right > 0f;
            NoCutoutReported = !unityHasCutout;
            if (unityHasCutout || unityHasAny) { Source = "Screen.safeArea" + (unityHasCutout ? " + Screen.cutouts" : ""); return fromUnity; }

#if UNITY_ANDROID && !UNITY_EDITOR
            Insets android;
            if (AndroidCutout(out android)) { Source = "Android DisplayCutout"; NoCutoutReported = false; return android; }
            float status = AndroidStatusBar();
            if (status > 0f) { Source = "Android status_bar_height"; return new Insets { top = status }; }
#endif
            if (config.manualTopInsetPx > 0) { Source = "manual (SafeAreaConfig.manualTopInsetPx)"; return new Insets { top = config.manualTopInsetPx }; }
            Source = "none";
            return fromUnity;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject Activity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        static bool AndroidCutout(out Insets insets)
        {
            insets = default;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) AndroidApi = version.GetStatic<int>("SDK_INT");
                if (AndroidApi < 28) return false;
                using (AndroidJavaObject activity = Activity())
                using (AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow"))
                using (AndroidJavaObject decor = window.Call<AndroidJavaObject>("getDecorView"))
                using (AndroidJavaObject rootInsets = decor.Call<AndroidJavaObject>("getRootWindowInsets"))
                {
                    if (rootInsets == null) return false;
                    using (AndroidJavaObject cutout = rootInsets.Call<AndroidJavaObject>("getDisplayCutout"))
                    {
                        if (cutout == null) return false;
                        insets = new Insets
                        {
                            top = cutout.Call<int>("getSafeInsetTop"), bottom = cutout.Call<int>("getSafeInsetBottom"),
                            left = cutout.Call<int>("getSafeInsetLeft"), right = cutout.Call<int>("getSafeInsetRight")
                        };
                        return insets.top > 0f || insets.bottom > 0f || insets.left > 0f || insets.right > 0f;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[SafeArea] Android cutout query failed: " + e.Message); return false; }
        }

        static float AndroidStatusBar()
        {
            try
            {
                using (AndroidJavaObject activity = Activity())
                using (AndroidJavaObject resources = activity.Call<AndroidJavaObject>("getResources"))
                {
                    int id = resources.Call<int>("getIdentifier", "status_bar_height", "dimen", "android");
                    return id > 0 ? resources.Call<int>("getDimensionPixelSize", id) : 0f;
                }
            }
            catch (Exception e) { Debug.LogWarning("[SafeArea] status_bar_height query failed: " + e.Message); return 0f; }
        }
#endif
    }
}
