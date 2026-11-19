using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Pictures of the app's screens for the paper (20 November 2026), taken from the scenes themselves instead of a phone.
    /// Each scene is opened (never saved), its canvases are drawn by a camera into a phone-sized picture, and the place of every
    /// button, input box and toggle is written next to it (JSON), so numbered boxes can be drawn on the picture afterwards.
    ///
    /// Batch mode (the Editor must be closed), with graphics (no -nographics):
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod GlowUp.EditorTools.SceneCapture.Run
    /// Environment: GLOWUP_CAPTURE_OUT = output folder; GLOWUP_CAPTURE_LIST = "Scene[:ObjectToShow[,Another]][:shotName];..."
    /// What the screens show is what the scene builders put in the scene; data that the app fetches at run time is not there.
    /// </summary>
    public static class SceneCapture
    {
        const int Width = 1080, Height = 2400;

        public static void Run()
        {
            string outDir = Environment.GetEnvironmentVariable("GLOWUP_CAPTURE_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Builds/SceneCaptures");
            Directory.CreateDirectory(outDir);
            string list = Environment.GetEnvironmentVariable("GLOWUP_CAPTURE_LIST");
            if (string.IsNullOrEmpty(list))
                list = "LogInScene;OnboardingStep1Scene;OnboardingStep2Scene;OnboardingStep3Scene;OnboardingStep4Scene;OnboardingStep5Scene;" +
                       "TermsAndConditionScene;UserDashboardScene;RecentScanActivitiesScene;AiSmartAnalysisScene;AiSmartAnalysisResultScene;ARTryOnScene;SettingsScene";
            foreach (string item in list.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                string[] parts = item.Split(':');
                string scene = parts[0].Trim();
                string[] show = parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(',') : new string[0];
                string shot = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : scene;
                try { Capture(scene, show, Path.Combine(outDir, shot)); }
                catch (Exception e) { Debug.LogError("[GlowUp] capture of " + scene + " failed: " + e); }
            }
            Debug.Log("[GlowUp] captures written to " + outDir);
        }

        static void Capture(string sceneName, string[] show, string outBase)
        {
            string path = "Assets/_Project/Scenes/" + sceneName + ".unity";
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            foreach (string raw in show)
            {
                string name = raw.Trim();
                bool hide = name.StartsWith("-");          // "-Name" hides an object (an undo bar the scene keeps ready, for example)
                if (hide) name = name.Substring(1);
                GameObject go = FindIncludingInactive(name);
                if (go == null) { Debug.LogWarning("[GlowUp] capture: no object named " + name + " in " + sceneName); continue; }
                if (hide) { go.SetActive(false); continue; }
                for (Transform t = go.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            }
            // the scene was built in the Editor: the version line names the platform the app is built for
            foreach (TMP_Text t in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
                if (t.text != null && t.text.Contains("Built for WindowsEditor")) t.text = t.text.Replace("Built for WindowsEditor", "Built for Android");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("CaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.orthographic = true;
            cam.targetTexture = rt;
            cam.cullingMask = ~0;

            var canvases = new List<Canvas>();
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                if (!c.isRootCanvas || !c.gameObject.activeInHierarchy) continue;
                if (c.renderMode == RenderMode.WorldSpace) continue;
                canvases.Add(c);
            }
            canvases.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
            float plane = 100f;
            foreach (Canvas c in canvases)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = plane;
                plane -= 1f;          // a higher sorting order is drawn nearer
            }
            cam.farClipPlane = 1000f;
            foreach (CanvasScaler s in UnityEngine.Object.FindObjectsOfType<CanvasScaler>(true)) { s.enabled = false; s.enabled = true; }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            Canvas.ForceUpdateCanvases();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(outBase + ".png", tex.EncodeToPNG());

            // where each control is, in picture pixels (top-left origin)
            var sb = new StringBuilder("[\n");
            bool first = true;
            var corners = new Vector3[4];
            foreach (Selectable sel in UnityEngine.Object.FindObjectsOfType<Selectable>(false))
            {
                if (!sel.gameObject.activeInHierarchy) continue;
                var r = sel.transform as RectTransform;
                if (r == null) continue;
                r.GetWorldCorners(corners);
                Vector2 a = cam.WorldToScreenPoint(corners[0]), b = cam.WorldToScreenPoint(corners[2]);
                float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Height - Mathf.Max(a.y, b.y), y1 = Height - Mathf.Min(a.y, b.y);
                if (x1 < 0 || y1 < 0 || x0 > Width || y0 > Height || x1 - x0 < 4 || y1 - y0 < 4) continue;
                string label = "";
                var tmp = sel.GetComponentInChildren<TMP_Text>(true);
                if (sel is TMP_InputField f && f.placeholder is TMP_Text ph) label = ph.text;
                else if (tmp != null) label = tmp.text;
                sb.Append(first ? "" : ",\n").Append("{\"name\":\"").Append(Esc(PathOf(sel.transform))).Append("\",\"kind\":\"").Append(sel.GetType().Name)
                  .Append("\",\"label\":\"").Append(Esc(label)).Append("\",\"box\":[").Append(Mathf.RoundToInt(x0)).Append(',').Append(Mathf.RoundToInt(y0)).Append(',')
                  .Append(Mathf.RoundToInt(x1)).Append(',').Append(Mathf.RoundToInt(y1)).Append("]}");
                first = false;
            }
            sb.Append("\n]\n");
            File.WriteAllText(outBase + ".json", sb.ToString());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log("[GlowUp] captured " + sceneName + " -> " + outBase + ".png (" + canvases.Count + " canvases)");
        }

        static GameObject FindIncludingInactive(string name)
        {
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            return null;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null && u.parent != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }
}
