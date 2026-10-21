using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// How much of the screen the phone's on-screen keyboard covers, as a SHARE of the screen's height (0 = no keyboard,
    /// 0.4 = the lower 40 percent). A share, not pixels, because the app may draw at a lower resolution than the screen
    /// (Settings > Graphics quality): a share means the same on the canvas whatever the resolution.
    ///
    /// Android only (iOS is untested and out of scope). Three ways to measure, the larger valid answer is used:
    ///   1. API 30+: the window insets, IME part (WindowInsets.Type.ime()). Works edge-to-edge (Android 15).
    ///   2. Any API: the part of the window the system still shows (getWindowVisibleDisplayFrame) against the whole window.
    ///   3. TouchScreenKeyboard.area: often an empty rect on Android, so only the last fallback.
    /// Every Java call is wrapped: a failure means "no keyboard" (no padding), never a crash. Java objects are made once.
    /// </summary>
    public static class KeyboardInsets
    {
        /// <summary>Set by KeyboardAvoider. Development builds and the Editor only; numbers are logged, never any typed text.</summary>
        public static bool LogNumbers;

        /// <summary>The last measurement, for the on-screen number label of development builds.</summary>
        public static string LastMethod = "none";
        public static float LastInsets, LastFrame, LastArea, LastUsed;

        static float lastLogged = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool javaTried, javaBroken;
        static int apiLevel, imeMask;
        static AndroidJavaObject decor, frame;       // the window's top view, and a Rect we refill each time
        static float baseline = -1f;                 // the share the system bars take while no keyboard is up (frame method)
        static bool insetsBroken;
#endif

        /// <summary>The covered share now. Cheap enough to call a few times a second, not every frame.</summary>
        public static float MeasureShare()
        {
            float insets = 0f, window = 0f;
            string method = "none";
#if UNITY_ANDROID && !UNITY_EDITOR
            if (EnsureJava())
            {
                if (apiLevel >= 30 && !insetsBroken) insets = FromInsets();
                window = FromFrame();
            }
#endif
            float area = FromUnity();
            float used = Mathf.Max(insets, Mathf.Max(window, area));
            if (used > 0f) method = used == insets ? "insets" : used == window ? "frame" : "area";

            LastMethod = method; LastInsets = insets; LastFrame = window; LastArea = area; LastUsed = used;
            Log(used);
            return used;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Makes the Java objects once. False = nothing could be made: no keyboard measuring, no padding.</summary>
        static bool EnsureJava()
        {
            if (javaBroken) return false;
            if (javaTried) return decor != null;
            javaTried = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) apiLevel = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                    decor = window.Call<AndroidJavaObject>("getDecorView");
                frame = new AndroidJavaObject("android.graphics.Rect");
                if (apiLevel >= 30)
                    using (var type = new AndroidJavaClass("android.view.WindowInsets$Type")) imeMask = type.CallStatic<int>("ime");
                return decor != null;
            }
            catch (System.Exception e)
            {
                javaBroken = true; decor = null; frame = null;
                Debug.LogWarning("Keyboard: Java objects could not be made, no keyboard padding: " + e.Message);
                return false;
            }
        }

        /// <summary>API 30+: the IME inset of the window, from the screen's bottom.</summary>
        static float FromInsets()
        {
            try
            {
                int height = decor.Call<int>("getHeight");
                if (height <= 0) return 0f;
                using (var all = decor.Call<AndroidJavaObject>("getRootWindowInsets"))
                {
                    if (all == null || !all.Call<bool>("isVisible", imeMask)) return 0f;
                    using (var ime = all.Call<AndroidJavaObject>("getInsets", imeMask))
                        return Mathf.Clamp01(ime.Get<int>("bottom") / (float)height);
                }
            }
            catch (System.Exception e)
            {
                insetsBroken = true;                                   // do not try again; the frame method still works
                Debug.LogWarning("Keyboard: window insets failed, using the window frame: " + e.Message);
                return 0f;
            }
        }

        /// <summary>Any API: the window's visible frame ends at the keyboard's top edge.</summary>
        static float FromFrame()
        {
            try
            {
                decor.Call("getWindowVisibleDisplayFrame", frame);
                int height = decor.Call<int>("getHeight");
                if (height <= 0) return 0f;
                float share = Mathf.Clamp01((height - frame.Get<int>("bottom")) / (float)height);
                // the smallest share ever seen is the system bars alone (a keyboard only ever adds to it)
                if (baseline < 0f || share < baseline) baseline = share;
                return share - baseline >= 0.05f ? share : 0f;         // a few pixels of difference is not a keyboard
            }
            catch (System.Exception e)
            {
                javaBroken = true;
                Debug.LogWarning("Keyboard: window frame failed, no keyboard padding: " + e.Message);
                return 0f;
            }
        }
#endif

        /// <summary>Unity's own number: often an empty rect on Android, so it is the last fallback. iOS is out of scope.</summary>
        static float FromUnity()
        {
            try
            {
                if (!TouchScreenKeyboard.visible) return 0f;
                Rect area = TouchScreenKeyboard.area;
                return Screen.height > 0 ? Mathf.Clamp01(area.height / Screen.height) : 0f;
            }
            catch (System.Exception) { return 0f; }
        }

        static void Log(float used)
        {
            if (!LogNumbers || !(Debug.isDebugBuild || Application.isEditor)) return;
            if (Mathf.Abs(used - lastLogged) < 0.005f) return;         // only when the answer changes
            lastLogged = used;
            Debug.Log("Keyboard: " + Describe());
        }

        /// <summary>One line for the log and the on-screen label: method and numbers only.</summary>
        public static string Describe()
        {
            return "method " + LastMethod + " | insets " + LastInsets.ToString("0.###") + " | frame " + LastFrame.ToString("0.###") +
                   " | area " + LastArea.ToString("0.###") + " | used " + LastUsed.ToString("0.###");
        }
    }
}
