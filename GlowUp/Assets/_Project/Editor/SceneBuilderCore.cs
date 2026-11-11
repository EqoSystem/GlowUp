using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Helpers shared by every scene builder. Rule: NEVER delete or rename anything –
    /// every helper finds an existing object by name first and only creates what is missing.
    /// </summary>
    public static class SceneBuilderCore
    {
        public const string ScenesFolder = "Assets/_Project/Scenes/";

        public static Scene Open(string sceneName)
        {
            string path = ScenesFolder + sceneName + ".unity";
            if (!System.IO.File.Exists(path))
            {
                Scene open = SceneManager.GetActiveScene();
                if (open.IsValid() && open.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new System.OperationCanceledException("Scene build cancelled: the open scene has unsaved changes.");
                var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, path);
                return s;
            }
            // already open: build on what is there, so unsaved edits are not thrown away by a reload
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.path == path) { GlowUp.SafeAreaFix.SafeAreaStructure.Unwrap(active); return active; }
            // another scene with unsaved edits is open: ask before it is replaced
            if (active.IsValid() && active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new System.OperationCanceledException("Scene build cancelled: the open scene has unsaved changes.");
            Scene opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // the builders find a screen's card directly under its canvas: the safe-area root is taken away first (ThemeKit.FinishScene puts it back)
            GlowUp.SafeAreaFix.SafeAreaStructure.Unwrap(opened);
            return opened;
        }

        public static void Save(Scene s)
        {
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
        }

        // ---------------- find / create ----------------
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        public static GameObject FindInScene(string name)
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>(true)) if (go.name == name && go.scene.IsValid()) return go;
            return null;
        }

        /// <summary>
        /// True for the scene's own top canvas: not the screen-change sheet under UiServices, and not a canvas that sits inside
        /// another canvas. Unity reports a switched-off inner canvas (a help bubble, a dragged tile) as a "root canvas" as well,
        /// which is why the parents are checked here: a whole copy of a screen was once built inside a help bubble.
        /// </summary>
        public static bool IsSceneCanvas(Canvas candidate)
        {
            if (candidate == null || UiEffectsKit.IsServiceCanvas(candidate)) return false;
            for (Transform parent = candidate.transform.parent; parent != null; parent = parent.parent)
                if (parent.GetComponent<Canvas>() != null) return false;
            return true;
        }

        public static Canvas EnsureCanvas()
        {
            // the scene's own canvas (the screen-change sheet under UiServices has a canvas too: that one is not it)
            Canvas c = null;
            foreach (var candidate in Object.FindObjectsOfType<Canvas>(true))
                if (IsSceneCanvas(candidate)) { c = candidate; break; }
            if (c == null)
            {
                var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                c = go.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                var sc = go.GetComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1080, 1920);
                sc.matchWidthOrHeight = 0f;         // fixed 1080 width, the height follows the phone
            }
            if (Object.FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return c;
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var ex = FindDeep(parent, name);
            if (ex != null) return (RectTransform)ex;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Positions a new element by anchored centre + size (only used for objects we create).</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static Image ImageBox(Transform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var ex = FindDeep(parent, name);
            if (ex != null) return ex.GetComponent<Image>();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = color;
            Place((RectTransform)go.transform, anchor, pos, size);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Vector2 anchor, Vector2 pos, Vector2 sizeDelta,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var ex = FindDeep(parent, name);
            if (ex != null) return ex.GetComponent<TextMeshProUGUI>();
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align; t.enableWordWrapping = true;
            Place((RectTransform)go.transform, anchor, pos, sizeDelta);
            return t;
        }

        public static Button MakeButton(Transform parent, string name, string label, float fontSize, Vector2 anchor, Vector2 pos, Vector2 size, Color fill)
        {
            var ex = FindDeep(parent, name);
            if (ex != null) return Ensure<Button>(ex.gameObject);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = fill;
            go.GetComponent<Button>().targetGraphic = img;
            Place((RectTransform)go.transform, anchor, pos, size);
            var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lbl.transform.SetParent(go.transform, false);
            var rt = (RectTransform)lbl.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = lbl.GetComponent<TextMeshProUGUI>(); t.text = label; t.fontSize = fontSize; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            return go.GetComponent<Button>();
        }

        public static TMP_InputField MakeInput(Transform parent, string name, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var ex = FindDeep(parent, name);
            if (ex != null) return ex.GetComponent<TMP_InputField>();
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, anchor, pos, size);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform; art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = new Vector2(20, 6); art.offsetMax = new Vector2(-20, -6);
            var ph = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            ph.transform.SetParent(area.transform, false);
            Stretch(ph.transform); var pht = ph.GetComponent<TextMeshProUGUI>(); pht.text = placeholder; pht.fontSize = 34; pht.fontStyle = FontStyles.Italic; pht.alignment = TextAlignmentOptions.MidlineLeft;
            var tx = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            tx.transform.SetParent(area.transform, false);
            Stretch(tx.transform); var tt = tx.GetComponent<TextMeshProUGUI>(); tt.fontSize = 34; tt.alignment = TextAlignmentOptions.MidlineLeft;
            var f = go.GetComponent<TMP_InputField>();
            f.textViewport = art; f.textComponent = tt; f.placeholder = pht; f.targetGraphic = go.GetComponent<Image>();
            return f;
        }

        public static void Stretch(Transform t)
        {
            var rt = (RectTransform)t; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ---------------- components ----------------
        /// <summary>Returns the component, adding it when missing. (TryGetComponent: safe against Unity's Editor-only "fake null".)</summary>
        public static T Ensure<T>(GameObject go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var c)) c = go.AddComponent<T>();
            return c;
        }

        public static GameObject EnsureRoot(string name)
        {
            return FindInScene(name) ?? new GameObject(name);
        }

        /// <summary>Adds a persistent OnClick listener only if that exact one is not already wired.</summary>
        public static void WireClick(Button b, UnityAction action)
        {
            if (b == null) return;
            int n = b.onClick.GetPersistentEventCount();
            for (int i = 0; i < n; i++)
                if (b.onClick.GetPersistentTarget(i) == action.Target as Object && b.onClick.GetPersistentMethodName(i) == action.Method.Name) return;
            UnityEventTools.AddPersistentListener(b.onClick, action);
        }

        public static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no field '{field}'"); return; }
            switch (value)
            {
                case Object o: p.objectReferenceValue = o; break;
                case string s: p.stringValue = s; break;
                case int i: if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
                case Color c: p.colorValue = c; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A text on a controller that survives rebuilds keeps whatever the scene has saved, so a new default wording in
        /// the code would never reach an existing scene. This writes the new wording, but ONLY when the scene still holds
        /// one of the earlier defaults (or nothing): wording the user typed in the Inspector themselves is left alone.
        /// </summary>
        public static void ReplaceText(Object target, string field, string newText, params string[] earlierDefaults)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.String) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no text field '{field}'"); return; }
            string current = p.stringValue ?? "";
            bool replace = current.Length == 0;
            foreach (string earlier in earlierDefaults) if (current == earlier) replace = true;
            if (!replace || current == newText) return;
            p.stringValue = newText;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The same for a whole number: the new value is written only while the scene still holds one of the earlier defaults.</summary>
        public static void ReplaceNumber(Object target, string field, int newValue, params int[] earlierDefaults)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.Integer) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no number field '{field}'"); return; }
            if (p.intValue == newValue || System.Array.IndexOf(earlierDefaults, p.intValue) < 0) return;
            p.intValue = newValue;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The same for a number with a fraction (seconds, shares).</summary>
        public static void ReplaceNumber(Object target, string field, float newValue, params float[] earlierDefaults)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.Float) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no number field '{field}'"); return; }
            bool held = false;
            foreach (float earlier in earlierDefaults) if (Mathf.Approximately(p.floatValue, earlier)) held = true;
            if (!held || Mathf.Approximately(p.floatValue, newValue)) return;
            p.floatValue = newValue;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        /// <summary>ThemeProfile asset (created once).</summary>
        public static GlowUp.Theme.ThemeProfile EnsureThemeProfile()
        {
            const string path = "Assets/_Project/ThemeProfile.asset";
            var p = AssetDatabase.LoadAssetAtPath<GlowUp.Theme.ThemeProfile>(path);
            bool dirty = false;
            if (p == null) { p = ScriptableObject.CreateInstance<GlowUp.Theme.ThemeProfile>(); AssetDatabase.CreateAsset(p, path); dirty = true; }
            // when the default palette changes (now: white, pink for actions, gold for details) it is applied once; later Inspector edits are kept
            if (p.paletteVersion < GlowUp.Theme.ThemeProfile.CurrentPaletteVersion) { p.ResetPalettes(); dirty = true; }
            if (p.titleFont == null || p.bodyFont == null || p.inputFont == null) { FontFactory.FillProfile(p); dirty = true; }
            if (dirty) { EditorUtility.SetDirty(p); AssetDatabase.SaveAssets(); }
            return p;
        }

        /// <summary>Adds the shared singletons every scene needs.</summary>
        public static void EnsureSceneServices()
        {
            var theme = EnsureRoot("ThemeManager"); var tm = Ensure<GlowUp.Theme.ThemeManager>(theme);
            SetField(tm, "profile", EnsureThemeProfile());
            var net = EnsureRoot("NetworkServices");
            Ensure<GlowUp.Net.ConnectivityMonitor>(net); Ensure<GlowUp.Data.SyncQueue>(net);
            Ensure<GlowUp.Common.ContextualHintController>(EnsureRoot("HintController"));
        }

        public static void Themed(GameObject go, GlowUp.Theme.ThemeRole role)
        {
            var t = Ensure<GlowUp.Theme.ThemedGraphic>(go);
            SetField(t, "role", (int)role);
        }
    }
}
