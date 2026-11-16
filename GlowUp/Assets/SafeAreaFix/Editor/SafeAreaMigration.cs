using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.SafeAreaFix.EditorTools
{
    /// <summary>
    /// Tools > SafeAreaFix (12 November 2026).
    ///   Dry Run      : prints what Migrate All would change; nothing is saved.
    ///   Migrate All  : Android player settings, the config asset, a backup of every scene / prefab it touches (Assets/SafeAreaFix/Backup), then
    ///                  every screen canvas gets the target structure (SafeAreaStructure). Writes MigrationLog.md and ManualReview.md.
    ///   Player Settings only : the Android settings the system needs, with a log of what changed.
    /// Batch: Unity.exe -batchmode -quit -projectPath ... -executeMethod GlowUp.SafeAreaFix.EditorTools.SafeAreaMigration.MigrateFromCommandLine [-dryRun]
    /// The scene builders (GlowUp > Build Scene) apply the same structure at their end, so a rebuilt scene keeps it; SafeAreaBootstrap applies it at
    /// run time to anything that still lacks it.
    /// </summary>
    public static class SafeAreaMigration
    {
        const string Folder = "Assets/SafeAreaFix";
        const string BackupFolder = Folder + "/Backup";
        const string ConfigPath = Folder + "/Resources/SafeAreaConfig.asset";

        [MenuItem("Tools/SafeAreaFix/Dry Run")]
        public static void DryRunMenu() => Run(true);

        [MenuItem("Tools/SafeAreaFix/Migrate All")]
        public static void MigrateMenu() => Run(false);

        [MenuItem("Tools/SafeAreaFix/Player Settings only")]
        public static void PlayerSettingsMenu() { var log = new List<string>(); ApplyPlayerSettings(log, false); Debug.Log(string.Join("\n", log)); }

        public static void MigrateFromCommandLine()
        {
            bool dry = Array.IndexOf(Environment.GetCommandLineArgs(), "-dryRun") >= 0;
            Run(dry);
        }

        /// <summary>The Android settings the safe-area system relies on (Unity 2022.3). Only what is needed is changed; each change is logged.</summary>
        public static void ApplyPlayerSettings(List<string> log, bool dryRun)
        {
            log.Add("## Player settings (Android, Unity " + Application.unityVersion + ")");
            // render outside safe area: Unity then draws under the cutout and reports it in Screen.safeArea / Screen.cutouts
            // (Unity writes layoutInDisplayCutoutMode = shortEdges into the app's theme when this is on)
            if (!PlayerSettings.Android.renderOutsideSafeArea)
            {
                if (!dryRun) PlayerSettings.Android.renderOutsideSafeArea = true;
                log.Add("- Render outside safe area: off -> ON (display cutout mode = short edges)");
            }
            else log.Add("- Render outside safe area: already ON (display cutout mode = short edges) - unchanged");
            log.Add("- Start in fullscreen (status bar hidden): " + (PlayerSettings.Android.startInFullscreen ? "ON" : "off") + " - unchanged (the app is drawn under the status-bar area either way; the top inset covers it)");
            log.Add("- Target API level: " + PlayerSettings.Android.targetSdkVersion + " (Automatic = the highest installed SDK, android-36 here). Android 15+ (API 35+) forces edge-to-edge for apps that target it: the app draws under the system bars, which this system handles. Unity 2022.3 itself does not opt out.");
            log.Add("- Default orientation: " + PlayerSettings.defaultInterfaceOrientation + " - unchanged (SafeAreaRoot also follows landscape and every rotation)");
        }

        static void Run(bool dryRun)
        {
            var log = new List<string> { "# SafeAreaFix migration " + (dryRun ? "(DRY RUN: nothing changed)" : "") + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), "" };
            var review = new List<string>();
            ApplyPlayerSettings(log, dryRun);
            log.Add("");

            if (!dryRun && AssetDatabase.LoadAssetAtPath<SafeAreaConfig>(ConfigPath) == null)
            {
                Directory.CreateDirectory(Folder + "/Resources");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SafeAreaConfig>(), ConfigPath);
                log.Add("Created " + ConfigPath + " (top only, debug overlay on in development builds).");
            }

            string current = SceneManager.GetActiveScene().path;
            if (!dryRun && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Debug.Log("[SafeAreaFix] cancelled"); return; }

            var scenes = new List<string>(Directory.GetFiles("Assets/_Project/Scenes", "*.unity"));
            log.Add("## Scenes");
            foreach (string path in scenes)
            {
                string p = path.Replace('\\', '/');
                Scene scene = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                var sceneLog = new List<string>();
                bool changed = false;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    Canvas canvas = go.GetComponent<Canvas>();
                    if (canvas == null) continue;
                    sceneLog.Add($"- canvas {go.name} ({canvas.renderMode}):");
                    if (!SafeAreaStructure.Wanted(canvas)) { sceneLog.Add("  skipped (service, overlay or world canvas)"); continue; }
                    TopOffsets(canvas, sceneLog);
                    if (dryRun) changed |= SafeAreaStructure.Convert(canvas, true, sceneLog, review);
                    else
                    {
                        if (!changed) Backup(p, log);
                        changed |= SafeAreaStructure.Convert(canvas, false, sceneLog, review);
                    }
                }
                log.Add($"### {Path.GetFileNameWithoutExtension(p)}: " + (changed ? (dryRun ? "would change" : "changed") : "no change"));
                log.AddRange(sceneLog);
                if (changed && !dryRun) EditorSceneManager.SaveScene(scene);
            }

            log.Add(""); log.Add("## Prefabs");
            foreach (string path in Directory.GetFiles("Assets/_Project/Prefabs", "*.prefab"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                bool hasCanvas = prefab != null && prefab.GetComponentInChildren<Canvas>(true) != null;
                log.Add($"- {Path.GetFileName(path)}: " + (hasCanvas ? "has a Canvas: check by hand (listed in ManualReview.md)" : "no Canvas (a piece placed inside a screen): nothing to change"));
                if (hasCanvas) review.Add($"Prefab {Path.GetFileName(path)} contains a Canvas: give it the structure by hand if it is a full screen.");
            }

            if (!string.IsNullOrEmpty(current) && File.Exists(current)) EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
            Write(Folder + (dryRun ? "/DryRunLog.md" : "/MigrationLog.md"), log);
            var reviewDoc = new List<string> { "# SafeAreaFix: manual review", "", "Objects the rules were not sure about. They were LEFT WHERE THEY WERE; nothing was guessed.", "" };
            if (review.Count == 0) reviewDoc.Add("- none: every canvas child matched a rule.");
            foreach (string r in review) reviewDoc.Add("- " + r);
            reviewDoc.Add("");
            reviewDoc.Add("Always worth a look on the phone (by design, not moved):");
            reviewDoc.Add("- Pop-ups (OverlayPanel) stay on the canvas: their dim layer must cover the whole screen and their box is centred. A very tall pop-up box could reach the camera hole on a short phone.");
            reviewDoc.Add("- The top toast (FormalToast, its own ToastLayer canvas) places itself from Screen.safeArea already.");
            reviewDoc.Add("- The screen-change sheet (UiServices > TransitionCanvas) is a full-screen loading sheet: left full-bleed.");
            if (!dryRun) Write(Folder + "/ManualReview.md", reviewDoc);
            AssetDatabase.Refresh();
            Debug.Log("[SafeAreaFix] " + (dryRun ? "dry run" : "migration") + " done: see " + Folder + (dryRun ? "/DryRunLog.md" : "/MigrationLog.md"));
        }

        /// <summary>Lists any canvas child whose top is pushed down by a fixed amount (a compensation for the old behaviour would be removed here).</summary>
        static void TopOffsets(Canvas canvas, List<string> log)
        {
            foreach (Transform t in canvas.transform)
            {
                if (!(t is RectTransform rt) || rt.anchorMax.y < 0.999f) continue;
                float top = -rt.offsetMax.y, bottom = rt.offsetMin.y;
                if (top <= 0.5f) continue;
                // a margin equal at top and bottom is the card's design margin, not a cutout compensation
                if (Mathf.Abs(top - bottom) <= 1f) log.Add($"  {rt.name}: top margin {top:0} = bottom margin {bottom:0} (design margin, kept)");
                else log.Add($"  {rt.name}: top {top:0} vs bottom {bottom:0}: looks like a top compensation; NOT removed automatically, listed for review");
            }
        }

        static void Backup(string assetPath, List<string> log)
        {
            Directory.CreateDirectory(BackupFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            // ".bak" so Unity does not import the copies as extra scenes
            string target = $"{BackupFolder}/{Path.GetFileName(assetPath)}.{stamp}.bak";
            File.Copy(assetPath, target, true);
            log.Add($"Backed up {assetPath} -> {target}");
        }

        static void Write(string path, List<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }
    }
}
