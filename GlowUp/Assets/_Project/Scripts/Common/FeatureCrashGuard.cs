using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Remembers when a feature closed the app. A feature that can take the whole app down on some
    /// phones (live face tracking is the one that uses it) leaves a small mark on the phone just
    /// before it starts and removes the mark once it is running. If the app opens again and the mark
    /// is still there, the app died while that feature was starting: the feature is then switched off
    /// on this phone and the screen uses its simpler version instead, so the same crash cannot happen
    /// twice. The user stays signed in through all of this.
    ///
    /// The switch-off lasts until the next version of the app is installed (a new build may have
    /// fixed it), or until <see cref="Reset"/> is called.
    /// </summary>
    public static class FeatureCrashGuard
    {
        const string Prefix = "glowup.guard.";
        static readonly HashSet<string> startedInThisRun = new HashSet<string>();

        // every build of the app has its own id, so an update gives the feature a fresh try
        static string Build => string.IsNullOrEmpty(Application.buildGUID) ? Application.version : Application.buildGUID;
        static string Key(string feature, string what) => Prefix + feature + "." + what;

        /// <summary>True when this feature closed the app the last time it was started (in this build of the app).</summary>
        public static bool IsBlocked(string feature)
        {
            Adopt(feature);
            return PlayerPrefs.GetInt(Key(feature, "blocked"), 0) == 1 && PlayerPrefs.GetString(Key(feature, "build"), "") == Build;
        }

        /// <summary>Call just before the feature starts. The mark is written to the phone at once.</summary>
        public static void Begin(string feature)
        {
            Adopt(feature);
            startedInThisRun.Add(feature);
            PlayerPrefs.SetInt(Key(feature, "starting"), 1);
            PlayerPrefs.SetString(Key(feature, "build"), Build);
            PlayerPrefs.Save();
        }

        /// <summary>The feature is running fine: the mark is removed and any earlier switch-off is lifted.</summary>
        public static void Passed(string feature)
        {
            startedInThisRun.Remove(feature);
            PlayerPrefs.SetInt(Key(feature, "starting"), 0);
            PlayerPrefs.SetInt(Key(feature, "blocked"), 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The feature stopped for an ordinary reason (the user left the screen, the app went to the
        /// background, the feature is not available): the mark is removed, nothing else changes.
        /// </summary>
        public static void Suspend(string feature)
        {
            startedInThisRun.Remove(feature);
            if (PlayerPrefs.GetInt(Key(feature, "starting"), 0) == 0) return;
            PlayerPrefs.SetInt(Key(feature, "starting"), 0);
            PlayerPrefs.Save();
        }

        /// <summary>Gives the feature another try on this phone.</summary>
        public static void Reset(string feature)
        {
            startedInThisRun.Remove(feature);
            PlayerPrefs.DeleteKey(Key(feature, "starting"));
            PlayerPrefs.DeleteKey(Key(feature, "blocked"));
            PlayerPrefs.Save();
        }

        // A mark that this run of the app did not write was left by a run that died.
        static void Adopt(string feature)
        {
            if (startedInThisRun.Contains(feature) || PlayerPrefs.GetInt(Key(feature, "starting"), 0) != 1) return;
            PlayerPrefs.SetInt(Key(feature, "starting"), 0);
            if (PlayerPrefs.GetString(Key(feature, "build"), "") == Build)
            {
                PlayerPrefs.SetInt(Key(feature, "blocked"), 1);
                { }
            }
            PlayerPrefs.Save();
        }
    }
}
