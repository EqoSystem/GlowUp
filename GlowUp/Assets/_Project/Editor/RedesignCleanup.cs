using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Redesign plan (27 November 2026): the app's Admin Scanner is removed. Admins log in and review on the website only.
    /// Menu: GlowUp > Remove Admin Scanner. Rebuilds the Log-In scene first (its "Admin?" row goes), then deletes the six staff scenes,
    /// their builder and scripts through the AssetDatabase (so the .meta files go with them). GalleryPicker stays: Send for Review uses it.
    /// Safe to run twice: what is already gone is skipped.
    /// </summary>
    public static class RedesignCleanup
    {
        static readonly string[] Gone =
        {
            "Assets/_Project/Scenes/AdminLoginScene.unity",
            "Assets/_Project/Scenes/AdminDashboardScene.unity",
            "Assets/_Project/Scenes/AdminCaptureScene.unity",
            "Assets/_Project/Scenes/AdminSubmissionsScene.unity",
            "Assets/_Project/Scenes/AdminSettingsScene.unity",
            "Assets/_Project/Scenes/AdminScannerPrefsScene.unity",
            "Assets/_Project/Editor/AdminScenesBuilder.cs",
            "Assets/_Project/Editor/Tests/ProductFileParserTests.cs",
            "Assets/_Project/Scripts/Admin/AdminAccessAccordion.cs",
            "Assets/_Project/Scripts/Admin/AdminCaptureController.cs",
            "Assets/_Project/Scripts/Admin/AdminDashboardController.cs",
            "Assets/_Project/Scripts/Admin/AdminLoginController.cs",
            "Assets/_Project/Scripts/Admin/AdminScannerPrefsController.cs",
            "Assets/_Project/Scripts/Admin/AdminSettingsController.cs",
            "Assets/_Project/Scripts/Admin/AdminSubmissionsController.cs",
            "Assets/_Project/Scripts/Admin/BatchQueue.cs",
            "Assets/_Project/Scripts/Admin/CaptureDraft.cs",
            "Assets/_Project/Scripts/Admin/CaptureReader.cs",
            "Assets/_Project/Scripts/Admin/ProductFileImport.cs",
            "Assets/_Project/Scripts/Admin/ProductFileParser.cs",
            "Assets/_Project/Scripts/Admin/ScanLineSweep.cs",
            "Assets/_Project/Scripts/Admin/ScannerPrefs.cs",
            "Assets/_Project/Scripts/Admin/StaffAccess.cs",
            "Assets/_Project/Scripts/Admin/StaffSkin.cs",
            "Assets/_Project/Scripts/Admin/StaffToast.cs",
            "Assets/_Project/Scripts/Admin/SubmissionRows.cs",
            "Assets/_Project/Scripts/Admin/SubmissionStore.cs",
            "Assets/_Project/Scripts/Admin/WebsitePresence.cs",
        };

        /// <summary>
        /// 28 November 2026 (owner: finger signs are not needed anymore): the 7.8 MB hand model leaves the app (through the
        /// AssetDatabase, so its .meta goes too), then every scene is rebuilt so the finger-sign objects and Settings rows go.
        /// Batch: -executeMethod GlowUp.EditorTools.RedesignCleanup.Nov28
        /// </summary>
        [MenuItem("GlowUp/Rebuild for the 28 November 2026 changes")]
        public static void Nov28()
        {
            const string handModel = "Assets/StreamingAssets/hand_landmarker.task";
            if (AssetDatabase.LoadMainAssetAtPath(handModel) != null || System.IO.File.Exists(handModel))
                Debug.Log("[GlowUp] Hand model " + (AssetDatabase.DeleteAsset(handModel) ? "removed." : "could NOT be removed."));
            AllScenesBuilder.BuildEverything();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("GlowUp/Remove Admin Scanner")]
        public static void Run()
        {
            LoginSceneBuilder.Build();
            var failed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in Gone)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(path) == null && !System.IO.File.Exists(path)) continue;
                    if (!AssetDatabase.DeleteAsset(path)) failed.Add(path);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            Debug.Log("[GlowUp] Admin Scanner removed." + (failed.Count > 0 ? " Could not delete: " + string.Join(", ", failed) : ""));
        }
    }
}
