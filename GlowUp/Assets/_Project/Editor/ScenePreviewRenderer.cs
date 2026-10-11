using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Save Scene Previews (PNG). Saves a picture of every scene in Build Settings exactly as
    /// it looks in Edit mode (1080 x 1920), into the folder ScenePreviews next to Assets. Handy for checking
    /// all screens after "Build Scene > All Scenes" without opening them one by one. Nothing in the scenes
    /// is changed or saved.
    ///
    /// It also runs from Unity's command line (used to check the builders automatically):
    ///   -executeMethod GlowUp.EditorTools.ScenePreviewRenderer.RenderFromCommandLine
    ///   -previewOut "folder"  [-previewScenes "LogInScene,SettingsScene"]  [-previewSize 1080x2400]
    ///   [-previewShow "ObjectA;ObjectB"]   (switches these objects on first, for pop-ups that start hidden)
    /// </summary>
    public static class ScenePreviewRenderer
    {
        const string DefaultFolder = "ScenePreviews";

        [MenuItem("GlowUp/Save Scene Previews (PNG)", false, 220)]
        static void RenderFromMenu()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("GlowUp", "Stop Play mode first.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string start = SceneManager.GetActiveScene().path;
            int saved = RenderAll(DefaultFolder, null, 1080, 1920, null);
            if (!string.IsNullOrEmpty(start) && File.Exists(start)) EditorSceneManager.OpenScene(start, OpenSceneMode.Single);
            EditorUtility.RevealInFinder(DefaultFolder);
            EditorUtility.DisplayDialog("GlowUp", saved + " picture(s) saved in the folder " + DefaultFolder + " (next to Assets).", "OK");
        }

        public static void RenderFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string folder = Arg("-previewOut") ?? DefaultFolder;
            string only = Arg("-previewScenes"), size = Arg("-previewSize"), show = Arg("-previewShow");
            int width = 1080, height = 1920;
            if (!string.IsNullOrEmpty(size) && size.Contains("x"))
            {
                string[] parts = size.Split('x');
                int.TryParse(parts[0], out width); int.TryParse(parts[1], out height);
            }
            int saved = 0;
            try { saved = RenderAll(folder, only, Mathf.Max(200, width), Mathf.Max(200, height), show); }
            catch (Exception e) { Debug.LogError("[GlowUp] Scene previews failed: " + e); }
            Debug.Log("[GlowUp] Scene previews saved: " + saved);
            EditorApplication.Exit(saved > 0 ? 0 : 1);
        }

        static int RenderAll(string folder, string onlyTheseScenes, int width, int height, string showObjects)
        {
            Directory.CreateDirectory(folder);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(onlyTheseScenes))
                foreach (string name in onlyTheseScenes.Split(',')) if (name.Trim().Length > 0) wanted.Add(name.Trim());
            var show = new List<string>();
            if (!string.IsNullOrEmpty(showObjects))
                foreach (string name in showObjects.Split(';')) if (name.Trim().Length > 0) show.Add(name.Trim());

            int saved = 0;
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled || !File.Exists(entry.path)) continue;
                string sceneName = Path.GetFileNameWithoutExtension(entry.path);
                if (wanted.Count > 0 && !wanted.Contains(sceneName)) continue;
                try
                {
                    string suffix = show.Count > 0 ? "_" + string.Join("+", show) : "";
                    if (RenderScene(entry.path, Path.Combine(folder, sceneName + suffix + ".png"), width, height, show)) saved++;
                }
                catch (Exception e) { Debug.LogError("[GlowUp] Preview of " + sceneName + " failed: " + e); }
            }
            return saved;
        }

        static bool RenderScene(string scenePath, string file, int width, int height, List<string> show)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Canvas canvas = null;
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
                if (c.isRootCanvas && c.gameObject.activeInHierarchy) { canvas = c; break; }
            if (canvas == null) { Debug.LogWarning("[GlowUp] " + scene.name + " has no canvas to draw."); return false; }

            var opened = new List<GlowUp.AppSettings.SettingsSectionTile>();
            foreach (string name in show)
                foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                    {
                        t.gameObject.SetActive(true);
                        if (t.TryGetComponent(out CanvasGroup faded)) faded.alpha = 1f;        // things that are only invisible, not switched off
                        if (t.TryGetComponent(out GlowUp.AppSettings.SettingsSectionTile group)) opened.Add(group);        // a Settings group that is shut
                    }

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("PreviewCamera (temporary)");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; camera.nearClipPlane = 0.1f; camera.farClipPlane = 100f;
            camera.targetTexture = target; camera.transform.position = new Vector3(5000f, 5000f, -50f);

            try
            {
                // the canvas normally draws straight onto the screen; for the picture it is drawn by this camera instead
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                for (int pass = 0; pass < 4; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var group in opened) group.ShowOpenNow();
                    foreach (LayoutGroup group in canvas.GetComponentsInChildren<LayoutGroup>(false))
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture before = RenderTexture.active;
                RenderTexture.active = target;
                var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                picture.Apply();
                RenderTexture.active = before;
                File.WriteAllBytes(file, picture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(picture);
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            return true;       // the scene is NOT saved: the next OpenScene discards these temporary changes
        }
    }
}
