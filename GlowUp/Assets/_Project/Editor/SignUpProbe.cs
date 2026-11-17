using System;
using System.IO;
using System.Reflection;
using System.Text;
using GlowUp.Auth;
using GlowUp.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Developer check (batch mode): opens the log-in scene in Play mode, presses "Sign Up" through its button, and writes to a text file
    /// what happened: was the click taken, was the screen change refused (busy), how many frames and seconds until the sign-up screen was
    /// the active scene, and every error that was logged on the way. Run:
    ///   Unity -batchmode -nographics -projectPath ... -executeMethod GlowUp.EditorTools.SignUpProbe.Run -logFile x.log
    /// The result file path is the environment variable GLOWUP_PROBE_OUT (default: SignUpProbe.txt in the project's Temp folder).
    /// Editor timings are only a guide: a phone is slower. A refusal or an error is what this finds.
    /// </summary>
    public static class SignUpProbe
    {
        static readonly StringBuilder log = new StringBuilder();
        static double startedAt, pressedAt;
        static int stage, pressFrame;
        static string output;
        static bool fromStartup;
        static double loginSeenAt, lastBusyLog;

        static void Say(string line) { log.AppendLine(string.Format("[{0:F2}s] {1}", EditorApplication.timeSinceStartup - startedAt, line)); }

        public static void Run()
        {
            output = Environment.GetEnvironmentVariable("GLOWUP_PROBE_OUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.dataPath, "..", "Temp", "SignUpProbe.txt");
            startedAt = EditorApplication.timeSinceStartup;
            EditorSettings.unityRemoteDevice = "None";           // an Android phone as the Unity Remote device needs adb and fails in batch mode
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            // GLOWUP_PROBE_START=StartupScene: begin where the app begins, so the log-in page is reached the way a user reaches it
            string first = Environment.GetEnvironmentVariable("GLOWUP_PROBE_START");
            fromStartup = first == "StartupScene";
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/" + (fromStartup ? "StartupScene" : "LogInScene") + ".unity");
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>True when `a` is drawn (and hit) before `b` would be: a later canvas or a later place in the hierarchy.</summary>
        static bool DrawnAfter(Transform a, Transform b)
        {
            Canvas ca = a.GetComponentInParent<Canvas>(), cb = b.GetComponentInParent<Canvas>();
            if (ca != cb)
            {
                if (ca.rootCanvas != cb.rootCanvas) return ca.rootCanvas.sortingOrder > cb.rootCanvas.sortingOrder;
                if (ca.overrideSorting || cb.overrideSorting) return ca.sortingOrder > cb.sortingOrder;
            }
            var pa = Chain(a); var pb = Chain(b);
            for (int i = 0; i < Mathf.Min(pa.Count, pb.Count); i++)
                if (pa[i] != pb[i]) return pa[i] > pb[i];
            return pa.Count > pb.Count;
        }

        static System.Collections.Generic.List<int> Chain(Transform t)
        {
            var list = new System.Collections.Generic.List<int>();
            for (var p = t; p != null; p = p.parent) list.Insert(0, p.GetSiblingIndex());
            return list;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Say(type + ": " + message);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(output, log.ToString());
            EditorApplication.Exit(code);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - startedAt > 120) { Say("TIMEOUT: nothing finished in 120 s"); Finish(2); return; }

            if (stage == 0 && fromStartup)
            {
                // wait for the start-up screen to hand over to the log-in page; log how long the transition stays busy there
                if (SceneManager.GetActiveScene().name != "LogInScene") { loginSeenAt = 0; return; }
                if (loginSeenAt == 0) { loginSeenAt = now; Say("log-in page reached; transition busy: " + ScreenTransition.IsBusy); }
                if (now - lastBusyLog > 0.5 && now - loginSeenAt < 6) { lastBusyLog = now; Say(string.Format("  +{0:F1}s after arrival, transition busy: {1}", now - loginSeenAt, ScreenTransition.IsBusy)); }
                if (now - loginSeenAt < 6) return;
            }
            if (stage == 0)
            {
                if (!fromStartup && now - startedAt < 4) return;               // let the log-in page settle
                var flow = UnityEngine.Object.FindObjectOfType<LoginFlowController>();
                if (flow == null) { Say("no LoginFlowController in the scene"); Finish(3); return; }
                var button = typeof(LoginFlowController).GetField("signUpButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(flow) as Button;
                if (button == null) { Say("signUpButton is not wired"); Finish(3); return; }
                Say("scene: " + SceneManager.GetActiveScene().name + ", transition busy: " + ScreenTransition.IsBusy + ", button interactable: " + button.interactable);
                // what a finger on the link would hit: the objects under its centre, top first
                var rect = (RectTransform)button.transform;
                var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
                // the batch screen is 640x480, the page is a tall phone page: shrink the canvas so the link is on the screen and a real
                // pointer test (raycast + press + release + click) is possible
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 0.18f; }
                Canvas.ForceUpdateCanvases();
                Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), high = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                Say(string.Format("sign-up link on screen: {0:F0}x{1:F0} px at ({2:F0},{3:F0}); screen {4}x{5}", high.x - low.x, high.y - low.y, low.x, low.y, Screen.width, Screen.height));
                // the batch-mode screen is 640x480, so a screen-point hit test is useless here: compare the rectangles instead.
                // Everything that can take a touch (raycast target on, alive, not hidden by a group) and overlaps the link's rectangle
                // while it is drawn AFTER the link (so it is hit first) is listed.
                Rect linkRect = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
                Say(string.Format("link rect in canvas units: x {0:F0}..{1:F0}, y {2:F0}..{3:F0}", linkRect.xMin, linkRect.xMax, linkRect.yMin, linkRect.yMax));
                foreach (var g in UnityEngine.Object.FindObjectsOfType<Graphic>())
                {
                    if (g == null || !g.raycastTarget || !g.isActiveAndEnabled || g.canvasRenderer.cull) continue;
                    if (g.transform == button.transform || g.transform.IsChildOf(button.transform)) continue;
                    var cg = g.GetComponentInParent<CanvasGroup>();
                    if (cg != null && (!cg.blocksRaycasts || cg.alpha <= 0f) && cg.gameObject.activeInHierarchy) { }
                    var gc = new Vector3[4]; g.rectTransform.GetWorldCorners(gc);
                    Rect gr = new Rect(gc[0].x, gc[0].y, gc[2].x - gc[0].x, gc[2].y - gc[0].y);
                    if (!gr.Overlaps(linkRect)) continue;
                    // drawn after the link? compare the order of the two in the hierarchy (and the canvas sorting)
                    bool after = DrawnAfter(g.transform, button.transform);
                    Say(string.Format("  overlaps the link: {0} ({1}), size {2:F0}x{3:F0}, {4}, blocksRaycasts group: {5}", PathOf(g.transform), g.GetType().Name, gr.width, gr.height, after ? "ABOVE the link" : "below", cg == null ? "none" : cg.blocksRaycasts.ToString()));
                }
                var es = UnityEngine.EventSystems.EventSystem.current;
                Say("EventSystem present: " + (es != null));
                if (es != null)
                    foreach (var spot in new[] { 0.5f, 0.15f, 0.85f })
                    {
                        var point = new Vector2(Mathf.Lerp(low.x, high.x, spot), (low.y + high.y) * 0.5f);
                        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                        es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = point }, hits);
                        var names = new StringBuilder();
                        for (int i = 0; i < Mathf.Min(4, hits.Count); i++) names.Append(hits[i].gameObject.name).Append(i < 3 ? " > " : "");
                        Say(string.Format("  hit at {0:P0} across the link: {1}", spot, names.Length == 0 ? "NOTHING" : names.ToString()));
                    }
                pressedAt = now; pressFrame = Time.frameCount;
                bool real = false;
                if (es != null)
                {
                    // a REAL tap at the middle of the link: what the event system hands to the press, release and click
                    var mid = new Vector2((low.x + high.x) * 0.5f, (low.y + high.y) * 0.5f);
                    var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = mid, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                    var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                    es.RaycastAll(ped, hits);
                    if (hits.Count > 0)
                    {
                        var go = hits[0].gameObject;
                        ped.pointerCurrentRaycast = ped.pointerPressRaycast = hits[0];
                        ped.pointerEnter = go;
                        var target = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(go, ped, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                        ped.pointerPress = target != null ? target : UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(go);
                        ped.rawPointerPress = go; ped.pressPosition = mid;
                        UnityEngine.EventSystems.ExecuteEvents.Execute(ped.pointerPress, ped, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                        var clicked = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(go);
                        bool same = clicked == ped.pointerPress;
                        if (same) UnityEngine.EventSystems.ExecuteEvents.Execute(clicked, ped, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                        Say("REAL tap: top hit " + PathOf(go.transform) + "; press target " + (ped.pointerPress != null ? ped.pointerPress.name : "none") + "; click handler " + (clicked != null ? clicked.name : "none") + "; same object: " + same);
                        real = true;
                    }
                    else Say("REAL tap: the pointer hit NOTHING at the middle of the link");
                }
                if (!real) button.onClick.Invoke();
                Say("pressed Sign Up; transition busy now: " + ScreenTransition.IsBusy + ", button interactable now: " + button.interactable);
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene == "OnboardingStep1Scene")
                {
                    Say(string.Format("sign-up screen is active after {0:F2} s and {1} frames; transition busy: {2}", now - pressedAt, Time.frameCount - pressFrame, ScreenTransition.IsBusy));
                    stage = 2; pressedAt = now;
                    return;
                }
                if (now - pressedAt > 40) { Say("the sign-up screen did NOT open within 40 s (active scene: " + scene + ", busy: " + ScreenTransition.IsBusy + ")"); Finish(4); }
                return;
            }
            if (now - pressedAt > 3) { Say("done"); Finish(0); }
        }
    }
}
