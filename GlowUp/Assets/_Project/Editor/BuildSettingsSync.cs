using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Keeps File > Build Settings > Scenes In Build in step with the app's scenes.
    ///
    /// Menu: GlowUp > Add Missing Scenes To Build Settings.
    ///   Appends every scene in the flow below that exists on disk but is not yet in the list.
    ///   It never removes or reorders entries of scenes that exist.
    ///
    /// Menu: GlowUp > Remove Deleted Scenes From Build Settings (also runs by itself when Unity loads the scripts).
    ///   Takes out entries whose scene file no longer exists (Unity shows those greyed out as "Deleted").
    /// </summary>
    [InitializeOnLoad]
    public static class BuildSettingsSync
    {
        static readonly string[] FlowScenes =
        {
            "LogInScene", "OnboardingStep1Scene", "OnboardingStep2Scene", "OnboardingStep3Scene",
            "OnboardingStep4Scene", "OnboardingStep5Scene", "TermsAndConditionScene", "UserDashboardScene",
            "RecentScanActivitiesScene", "AiSmartAnalysisScene", "LoadingScene", "AiSmartAnalysisResultScene", "ARTryOnScene",
            "SettingsScene"
        };

        static BuildSettingsSync()
        {
            // once the Editor has finished loading: drop entries of scenes that were deleted
            EditorApplication.delayCall += RemoveDeletedScenes;
        }

        [MenuItem("GlowUp/Add Missing Scenes To Build Settings")]
        public static void Run()
        {
            var list = EditorBuildSettings.scenes.ToList();
            int added = 0;
            foreach (var name in FlowScenes)
            {
                string path = SceneBuilderCore.ScenesFolder + name + ".unity";
                if (!File.Exists(path)) continue;
                if (list.Any(s => s.path == path)) continue;
                list.Add(new EditorBuildSettingsScene(path, true));
                added++;
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[GlowUp] Added {added} scene(s) to Build Settings.");
        }

        [MenuItem("GlowUp/Remove Deleted Scenes From Build Settings")]
        public static void RemoveDeletedScenes()
        {
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            var kept = new List<EditorBuildSettingsScene>();
            var dropped = new List<string>();
            foreach (EditorBuildSettingsScene scene in all)
            {
                if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path)) kept.Add(scene);
                else dropped.Add(string.IsNullOrEmpty(scene.path) ? "(no path)" : Path.GetFileNameWithoutExtension(scene.path));
            }
            if (dropped.Count == 0) return;
            EditorBuildSettings.scenes = kept.ToArray();
            Debug.Log($"[GlowUp] Build Settings: removed {dropped.Count} entr{(dropped.Count == 1 ? "y" : "ies")} of deleted scene(s): {string.Join(", ", dropped)}.");
        }
    }
}
