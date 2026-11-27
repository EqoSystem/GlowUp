using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menus: GlowUp > Build Scene > All Scenes / All App Scenes.
    /// Runs the scene builders one after another. A scene that fails does not stop the others; the
    /// dialog at the end lists what was built and what was not. The scene you had open is reopened.
    /// </summary>
    public static class AllScenesBuilder
    {
        struct Step
        {
            public string name; public Action build;
            public Step(string name, Action build) { this.name = name; this.build = build; }
        }

        static readonly Step[] AccountScenes =
        {
            new Step("Start-Up Animation", StartupSceneBuilder.Build),
            new Step("Login", LoginSceneBuilder.Build),
            new Step("Onboarding Step 1", OnboardingStep1Builder.Build),
            new Step("Onboarding Step 2", OnboardingTileBuilders.BuildStep2),
            new Step("Onboarding Step 3", OnboardingTileBuilders.BuildStep3),
            new Step("Onboarding Step 4", OnboardingFinalBuilders.BuildStep4),
            new Step("Onboarding Step 5", OnboardingFinalBuilders.BuildStep5),
            new Step("Terms And Conditions", OnboardingFinalBuilders.BuildTerms)
        };

        static readonly Step[] AppScenes =
        {
            new Step("User Dashboard", DashboardSceneBuilder.Build),
            new Step("Recent Scans", RecentScansSceneBuilder.Build),
            new Step("AI Smart Scan", SmartScanSceneBuilder.Build),
            new Step("Loading", LoadingSceneBuilder.Build),
            new Step("AI Scan Result", ScanResultSceneBuilder.Build),
            new Step("AR Try-On", ArTryOnSceneBuilder.Build),
            new Step("Settings", SettingsSceneBuilder.Build),
            new Step("Notifications", NotificationsSceneBuilder.Build),
            new Step("Send for Review", SendForReviewSceneBuilder.Build)
        };

        [MenuItem("GlowUp/Build Scene/All Scenes", false, 90)]
        public static void BuildEverything()
        {
            var all = new List<Step>(AccountScenes);
            all.AddRange(AppScenes);
            Run(all);
        }

        [MenuItem("GlowUp/Build Scene/All App Scenes (Dashboard to Settings)", false, 92)]
        public static void BuildAppScenes() { Run(new List<Step>(AppScenes)); }

        static void Run(List<Step> steps)
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("GlowUp", "Stop Play mode first, then build the scenes.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string startScene = SceneManager.GetActiveScene().path;

            var built = new List<string>();
            var failed = new List<string>();
            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("GlowUp: building scenes", steps[i].name, i / (float)steps.Count);
                    try { steps[i].build(); built.Add(steps[i].name); }
                    catch (Exception e)
                    {
                        Debug.LogError("[GlowUp] " + steps[i].name + " was not built: " + e);
                        failed.Add(steps[i].name + ": " + e.Message);
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            BuildSettingsSync.Run();
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(startScene) && File.Exists(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);

            string message = built.Count + " scene(s) built: " + string.Join(", ", built) + ".";
            if (failed.Count > 0)
                message += "\n\n" + failed.Count + " scene(s) could NOT be built:\n- " + string.Join("\n- ", failed) + "\n\nThe Console has the details.";
            EditorUtility.DisplayDialog("GlowUp", message, "OK");
            Debug.Log("[GlowUp] " + message);
        }
    }
}
