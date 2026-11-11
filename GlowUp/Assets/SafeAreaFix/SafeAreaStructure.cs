using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// THE TARGET STRUCTURE of every screen canvas (12 November 2026), used both at run time (SafeAreaBootstrap) and in the Editor
    /// (Tools > SafeAreaFix > Migrate All), so a screen looks the same either way:
    ///   Canvas
    ///    - background layer: the canvas's full-stretch background pictures stay where they are, first, and reach every edge
    ///    - SafeAreaRoot: everything else (the screen's card with its header, buttons and lists) moves in here, unchanged
    ///    - left on the canvas as they are: pop-ups (OverlayPanel: their dim layer must cover the whole screen and their box is centred),
    ///      objects with their own Canvas (toasts, overlays), objects that draw nothing (logic holders)
    /// Only the parent changes: anchors, pivots, sizes and fonts of the moved objects are kept (the root starts full-stretch, so nothing moves
    /// until the safe area is applied).
    /// </summary>
    public static class SafeAreaStructure
    {
        public enum Kind { Background, Content, KeepOnCanvas, Uncertain }

        /// <summary>A canvas the structure is for: a root canvas in screen space, not one of the app's service or debug layers.</summary>
        public static bool Wanted(Canvas canvas)
        {
            if (canvas == null || !canvas.isActiveAndEnabled && Application.isPlaying) return false;
            if (canvas.transform.parent != null) return false;                                  // a nested canvas (pop-up layer, toast layer)
            if (canvas.renderMode == RenderMode.WorldSpace) return false;
            if (canvas.GetComponent<SafeAreaIgnore>() != null) return false;
            string n = canvas.gameObject.name;
            return n != "UiServices" && !n.Contains("Transition") && !n.Contains("Debug") && !n.Contains("Keyboard Numbers");
        }

        /// <summary>What a direct child of the canvas is, and why.</summary>
        public static Kind Classify(RectTransform child, out string why)
        {
            if (child.GetComponent<SafeAreaRoot>() != null) { why = "the root itself"; return Kind.KeepOnCanvas; }
            if (child.GetComponent<SafeAreaIgnore>() != null) { why = "marked SafeAreaIgnore"; return Kind.KeepOnCanvas; }
            if (child.GetComponent<Canvas>() != null) { why = "has its own Canvas (overlay layer)"; return Kind.KeepOnCanvas; }
            if (HasComponentNamed(child, "OverlayPanel")) { why = "pop-up (OverlayPanel): its dim layer covers the whole screen, its box is centred"; return Kind.KeepOnCanvas; }
            bool full = child.anchorMin == Vector2.zero && child.anchorMax == Vector2.one;
            bool edgeToEdge = full && child.offsetMin == Vector2.zero && child.offsetMax == Vector2.zero;
            Graphic graphic = child.GetComponent<Graphic>();
            string n = child.name;
            if (edgeToEdge && graphic != null && (n.Contains("Background") || child.childCount == 0)) { why = "full-stretch picture that touches every edge"; return Kind.Background; }
            if (graphic == null && child.GetComponentInChildren<Graphic>(true) == null) { why = "draws nothing (logic holder)"; return Kind.KeepOnCanvas; }
            if (edgeToEdge && graphic != null && child.GetComponentInChildren<Selectable>(true) != null)
            { why = "full-stretch picture that also holds buttons: background or content?"; return Kind.Uncertain; }
            if (edgeToEdge && (n.Contains("Camera") || child.GetComponent<RawImage>() != null))
            { why = "full-bleed camera picture: stays in the background layer"; return Kind.Background; }
            // a full-screen light or camera layer without buttons (AR Try-On's face light frames the whole screen): full-bleed
            if (full && graphic != null && child.GetComponentInChildren<Selectable>(true) == null && (n.Contains("Light") || n.Contains("Camera") || n.Contains("Preview")))
            { why = "full-bleed light / camera layer (no buttons): stays in the background layer"; return Kind.Background; }
            why = full ? "content container (inset card)" : "content";
            return Kind.Content;
        }

        /// <summary>
        /// Gives `canvas` the target structure. `dryRun` = only report. Messages go to `log`, objects the rules were not sure about to `review`
        /// (they are left where they are). True = something was (or would be) moved.
        /// </summary>
        public static bool Convert(Canvas canvas, bool dryRun, List<string> log, List<string> review)
        {
            if (!Wanted(canvas)) return false;
            var canvasRect = (RectTransform)canvas.transform;
            SafeAreaRoot root = null;
            foreach (Transform t in canvasRect) if (t.TryGetComponent(out SafeAreaRoot r)) { root = r; break; }

            var moves = new List<RectTransform>();
            foreach (Transform t in canvasRect)
            {
                if (!(t is RectTransform child) || (root != null && child == root.transform)) continue;
                Kind kind = Classify(child, out string why);
                if (kind == Kind.Content) moves.Add(child);
                else if (kind == Kind.Uncertain) review?.Add($"{canvas.gameObject.scene.name} > {canvas.name} > {child.name}: {why}. Left on the canvas (not moved).");
                log?.Add($"  {child.name}: {kind} ({why})");
            }
            if (moves.Count == 0) return false;
            if (dryRun) { foreach (RectTransform m in moves) log?.Add($"  would move {m.name} into SafeAreaRoot"); return true; }

            if (root == null)
            {
                var go = new GameObject(SafeAreaRoot.ObjectName, typeof(RectTransform));
                go.layer = canvas.gameObject.layer;
                var rt = (RectTransform)go.transform;
                rt.SetParent(canvasRect, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                root = go.AddComponent<SafeAreaRoot>();
            }
            // the root takes the place of the first moved object, so the drawing order (backgrounds below, pop-ups above) is kept
            int index = int.MaxValue;
            foreach (RectTransform m in moves) index = Mathf.Min(index, m.GetSiblingIndex());
            root.transform.SetSiblingIndex(Mathf.Min(index, canvasRect.childCount - 1));
            foreach (RectTransform m in moves)
            {
                m.SetParent(root.transform, false);                 // anchors, pivot, size and position are kept: the root starts full-stretch
                log?.Add($"  moved {m.name} into SafeAreaRoot");
            }
            return true;
        }

        /// <summary>Takes the safe-area roots out of a scene again: their children go back onto the canvas in the same place (for the scene builders).</summary>
        public static void Unwrap(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid()) return;
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (SafeAreaRoot root in go.GetComponentsInChildren<SafeAreaRoot>(true))
                {
                    Transform canvas = root.transform.parent;
                    int index = root.transform.GetSiblingIndex();
                    var kids = new List<Transform>();
                    foreach (Transform t in root.transform) kids.Add(t);
                    foreach (Transform t in kids) { t.SetParent(canvas, false); t.SetSiblingIndex(index++); }
                    Object.DestroyImmediate(root.gameObject);
                }
        }

        static bool HasComponentNamed(Component c, string typeName)
        {
            foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name == typeName) return true;
            return false;
        }
    }
}
