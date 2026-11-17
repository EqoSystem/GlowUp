using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Android APK. One click instead of File > Build Settings > Build: it checks the
    /// settings live face tracking needs, builds the scenes ticked in Build Settings and saves the app as
    /// Builds/GlowUp.apk inside the project folder (next to Assets). Copy that file to the phone and open
    /// it there to install, or use File > Build Settings > Build And Run with the phone on a USB cable.
    ///
    /// This build is ALWAYS a release build (New13 item 6, 10 November 2026): Unity prints "Development Build" in the corner of every
    /// screen of a development build, and that label must never reach users. The tick in File > Build Settings is ignored here; the
    /// development build (the dev flag) is the separate menu GlowUp > Build Test APK (development).
    /// </summary>
    public static class AndroidApkBuilder
    {
        public const string OutputFolder = "Builds";
        public const string FileName = "GlowUp.54.apk";       // the owner's build number, 17 November 2026 (version 0.47.0, code 47; next build: GlowUp.48.apk ...)

        [MenuItem("GlowUp/Build Android APK", false, 300)]
        static void BuildFromMenu()
        {
            bool built = Build(out string message);
            EditorUtility.DisplayDialog("GlowUp: Android build", message, "OK");
            if (built) EditorUtility.RevealInFinder(OutputFolder + "/" + FileName);
        }

        /// <summary>For Unity's command line (-executeMethod GlowUp.EditorTools.AndroidApkBuilder.BuildFromCommandLine). Exit code 0 = built.</summary>
        public static void BuildFromCommandLine()
        {
            bool built = Build(out string message);
            Debug.Log("[GlowUp] Android build: " + message);
            EditorApplication.Exit(built ? 0 : 1);
        }

        public const string TestFolder = "Builds/Android";
        public const string TestFileName = "GlowUp.35-dev.apk";      // development build: the safe-area overlay and the AR debug overlay (12 November 2026)

        public const string PanelistFolder = "Builds/Android";
        public const string PanelistFileName = "GlowUpForPanelist.apk";

        /// <summary>
        /// Menu: GlowUp > Build Panelist APK. Standard release build named GlowUpForPanelist.apk.
        /// </summary>
        [MenuItem("GlowUp/Build Panelist APK", false, 302)]
        public static void BuildPanelistFromMenu()
        {
            bool built = Build(out string message, PanelistFolder, PanelistFileName, false);
            EditorUtility.DisplayDialog("GlowUp: Panelist Build", message, "OK");
            if (built) EditorUtility.RevealInFinder(PanelistFolder + "/" + PanelistFileName);
        }

        /// <summary>Command-line build method for GlowUpForPanelist.apk</summary>
        public static void BuildPanelistFromCommandLine()
        {
            bool built = Build(out string message, PanelistFolder, PanelistFileName, false);
            Debug.Log("[GlowUp] Android panelist build: " + message);
            EditorApplication.Exit(built ? 0 : 1);
        }

        /// <summary>
        /// Menu: GlowUp > Build Test APK (development). A DEVELOPMENT build for testing on a phone: the AR debug overlay can be
        /// shown (three fingers held on the screen for a second), and the app's messages reach `adb logcat -s Unity`.
        /// Saved as Builds/Android/GlowUp-AR-debug.apk, signed with Unity's debug key (a release needs the team's own keystore).
        /// </summary>
        [MenuItem("GlowUp/Build Test APK (development)", false, 301)]
        static void BuildTestFromMenu()
        {
            bool built = Build(out string message, TestFolder, TestFileName, true);
            EditorUtility.DisplayDialog("GlowUp: test build", message, "OK");
            if (built) EditorUtility.RevealInFinder(TestFolder + "/" + TestFileName);
        }

        /// <summary>
        /// For Unity's command line, with the Editor closed:
        /// Unity.exe -batchmode -nographics -projectPath &lt;GlowUp&gt; -buildTarget Android -executeMethod GlowUp.EditorTools.AndroidApkBuilder.BuildAndroidDebug -logFile build.log
        /// (no -quit: it closes Unity itself). Exit code 0 = built.
        /// </summary>
        public static void BuildAndroidDebug()
        {
            bool built = Build(out string message, TestFolder, TestFileName, true);
            Debug.Log("[GlowUp] Android test build: " + message);
            EditorApplication.Exit(built ? 0 : 1);
        }

        /// <summary>Builds the APK. Returns true when it was built; `message` says where it is, or what went wrong.</summary>
        public static bool Build(out string message)
        {
            if (EditorUserBuildSettings.development)
                Debug.Log("[GlowUp] \"Development Build\" is ticked in Build Settings: ignored for GlowUp > Build Android APK (a release build has no \"Development Build\" label). Use GlowUp > Build Test APK (development) for a test build.");
            return Build(out message, OutputFolder, FileName, false);
        }

        /// <summary>Builds an APK into `folder`/`file`; `development` = a development build.</summary>
        public static bool Build(out string message, string folder, string file, bool development)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                message = "The project is not set to Android. Open File > Build Settings, choose Android, press Switch Platform, then try again.";
                return false;
            }

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && File.Exists(scene.path)) scenes.Add(scene.path);
            if (scenes.Count == 0)
            {
                message = "No scene is ticked in File > Build Settings. Run GlowUp > Build Scene > All Scenes first.";
                return false;
            }

            // graphics, the ARCore plug-in and the camera picture feature: put right here too, so a build never fails on them
            var notes = new List<string>();
            ArFaceTrackingSetup.Apply(notes);
            BrandingSetup.Apply(notes);

            Directory.CreateDirectory(folder);
            string path = folder + "/" + file;
            EditorUserBuildSettings.buildAppBundle = false;         // an .apk can be copied to a phone and installed; an .aab cannot

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                bool devLabel = (summary.options & BuildOptions.Development) != 0;
                if (!development && devLabel)
                {
                    message = "The build was made as a DEVELOPMENT build although a release was asked for: it would show \"Development Build\". Not used.";
                    return false;
                }
                Debug.Log("[GlowUp] " + (devLabel ? "Development build: Unity shows \"Development Build\" on screen." : "Release build: no \"Development Build\" label."));
                message = $"Built in {summary.totalTime.TotalMinutes:0.0} minutes ({summary.totalSize / (1024f * 1024f):0} MB, {scenes.Count} scenes):\n" +
                          Path.GetFullPath(path) + "\n\nCopy this file to the phone and open it there to install the app.";
                return true;
            }

            var errors = new List<string>();
            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage line in step.messages)
                    if (line.type == LogType.Error && errors.Count < 6) errors.Add("- " + line.content);
            message = "The build did not finish (" + summary.result + ", " + summary.totalErrors + " error(s)).\n" + string.Join("\n", errors) +
                      "\n\nThe Console (and Unity's Editor.log) has the full text.";
            return false;
        }
    }
}
