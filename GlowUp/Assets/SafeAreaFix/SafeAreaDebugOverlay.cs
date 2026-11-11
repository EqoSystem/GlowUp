using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// ON-SCREEN SAFE-AREA CHECK for development builds (12 November 2026), instead of logcat. Its own full-screen overlay canvas (sort order
    /// 32000), never inside a SafeAreaRoot. A small "SA" button at the bottom left opens and closes the panel; while the panel is closed only
    /// the button, the red cutout boxes and the green safe-area outline are drawn.
    ///   - values: screen size, safe area, every cutout, orientation, dpi, Android API, the InsetProvider source, the insets applied per edge
    ///   - red box on each cutout, green outline on the safe area
    ///   - Top / Bottom / Left / Right toggles (SafeAreaConfig at run time) and "Reset to Top only"
    ///   - a draggable blue line + "Mark hole bottom": the line's Y in px and % from the top, next to the inset the app reports
    ///   - a warning when Android reports no cutout, naming the fallback in use
    ///   - "Copy values" (clipboard), and a PASS / FAIL per screen: the content's top (SafeAreaRoot's top edge) against the marked hole bottom
    /// Built in code; only made in development builds (or with the scripting define SAFEAREA_DEBUG) by SafeAreaBootstrap.
    /// </summary>
    public class SafeAreaDebugOverlay : MonoBehaviour
    {
        const string MarkKey = "SafeAreaFix.HoleBottomPx";

        RectTransform root, panel, drawLayer, safeOutline;
        readonly List<Image> cutoutBoxes = new List<Image>();
        TextMeshProUGUI info, warning, lineLabel, passText;
        readonly Dictionary<string, TextMeshProUGUI> toggleLabels = new Dictionary<string, TextMeshProUGUI>();
        SafeAreaCalibrationLine calibration;
        RectTransform lineRect;
        float markedHoleBottom = -1f;
        float k = 1f;                     // pixels per design unit (1080-wide design)
        float nextRefresh;
        readonly Dictionary<string, string> verdicts = new Dictionary<string, string>();

        public static SafeAreaDebugOverlay Create()
        {
            var go = new GameObject("SafeArea Debug Overlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(SafeAreaIgnore));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1f;     // everything here is in screen pixels
            return go.AddComponent<SafeAreaDebugOverlay>();
        }

        void Start()
        {
            k = Mathf.Max(0.5f, Screen.width / 1080f);
            markedHoleBottom = PlayerPrefs.GetFloat(MarkKey, -1f);
            root = (RectTransform)transform;
            drawLayer = Rect("Draw", root); Stretch(drawLayer);
            safeOutline = Rect("SafeAreaOutline", drawLayer);
            foreach (string side in new[] { "T", "B", "L", "R" }) Box(safeOutline, "Edge" + side, new Color(0.1f, 0.85f, 0.2f, 0.95f));

            Button toggle = MakeButton(root, "SA", new Vector2(0f, 0f), new Vector2(24 * k, 24 * k), new Vector2(96 * k, 96 * k), () => panel.gameObject.SetActive(!panel.gameObject.activeSelf));

            panel = Rect("Panel", root);
            panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(1f, 0f); panel.pivot = new Vector2(0.5f, 0f);
            panel.offsetMin = new Vector2(16 * k, 140 * k); panel.offsetMax = new Vector2(-16 * k, 140 * k + 980 * k);
            Image back = panel.gameObject.AddComponent<Image>(); back.color = new Color(0f, 0f, 0f, 0.82f);
            info = Label(panel, "Info", 26 * k, new Vector2(24 * k, -20 * k), new Vector2(-48 * k, 430 * k), Color.white);
            warning = Label(panel, "Warning", 26 * k, new Vector2(24 * k, -455 * k), new Vector2(-48 * k, 70 * k), new Color(1f, 0.75f, 0.2f));
            passText = Label(panel, "PassFail", 28 * k, new Vector2(24 * k, -530 * k), new Vector2(-48 * k, 120 * k), Color.white);

            float y = -660 * k, w = 230 * k, h = 84 * k, gap = 14 * k;
            string[] edges = { "Top", "Bottom", "Left", "Right" };
            for (int i = 0; i < 4; i++)
            {
                string edge = edges[i];
                Button b = MakeButton(panel, edge, new Vector2(0f, 1f), new Vector2(24 * k + i * (w + gap), y), new Vector2(w, h), () => ToggleEdge(edge));
                toggleLabels[edge] = b.GetComponentInChildren<TextMeshProUGUI>();
            }
            y -= h + gap;
            MakeButton(panel, "Reset to Top only", new Vector2(0f, 1f), new Vector2(24 * k, y), new Vector2(2 * w + gap, h), () => SafeAreaConfig.Current.TopOnly());
            MakeButton(panel, "Mark hole bottom", new Vector2(0f, 1f), new Vector2(24 * k + 2 * (w + gap), y), new Vector2(2 * w + gap, h), MarkHoleBottom);
            y -= h + gap;
            MakeButton(panel, "Copy values", new Vector2(0f, 1f), new Vector2(24 * k, y), new Vector2(2 * w + gap, h), () => GUIUtility.systemCopyBuffer = Report());
            MakeButton(panel, "Hide line", new Vector2(0f, 1f), new Vector2(24 * k + 2 * (w + gap), y), new Vector2(2 * w + gap, h), () => lineRect.gameObject.SetActive(!lineRect.gameObject.activeSelf));

            // the calibration line: drag it (or its handle) to the bottom of the camera hole as you see it
            lineRect = Rect("CalibrationLine", root);
            lineRect.anchorMin = new Vector2(0f, 1f); lineRect.anchorMax = new Vector2(1f, 1f); lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.sizeDelta = new Vector2(0f, 44 * k);
            Image hit = lineRect.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0.001f);
            RectTransform stroke = Rect("Stroke", lineRect); stroke.anchorMin = new Vector2(0f, 0.5f); stroke.anchorMax = new Vector2(1f, 0.5f);
            stroke.sizeDelta = new Vector2(0f, Mathf.Max(2f, 3 * k));
            stroke.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.55f, 1f, 1f);
            lineLabel = Label(lineRect, "Y", 24 * k, new Vector2(Screen.width * 0.45f, 40 * k), new Vector2(-Screen.width * 0.45f, 36 * k), new Color(0.4f, 0.75f, 1f));
            calibration = lineRect.gameObject.AddComponent<SafeAreaCalibrationLine>();
            calibration.Moved = Refresh;
            calibration.Init(lineRect, markedHoleBottom > 0f ? markedHoleBottom : Screen.height * 0.06f);

            SceneManager.sceneLoaded += (_, __) => nextRefresh = 0f;
            SafeAreaConfig.Changed += Refresh;
            panel.gameObject.SetActive(false);
            Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        void ToggleEdge(string edge)
        {
            SafeAreaConfig c = SafeAreaConfig.Current;
            if (edge == "Top") c.applyTop = !c.applyTop;
            if (edge == "Bottom") c.applyBottom = !c.applyBottom;
            if (edge == "Left") c.applyLeft = !c.applyLeft;
            if (edge == "Right") c.applyRight = !c.applyRight;
            SafeAreaConfig.RaiseChanged();
        }

        void MarkHoleBottom()
        {
            markedHoleBottom = calibration.YFromTopPx;
            PlayerPrefs.SetFloat(MarkKey, markedHoleBottom); PlayerPrefs.Save();
            Refresh();
        }

        /// <summary>The content's top on this screen: the lowest top edge of the SafeAreaRoots shown (pixels from the screen's top), or -1.</summary>
        static float ContentTopPx(out string screen)
        {
            screen = SceneManager.GetActiveScene().name;
            float best = -1f;
            var corners = new Vector3[4];
            foreach (SafeAreaRoot r in FindObjectsOfType<SafeAreaRoot>())
            {
                ((RectTransform)r.transform).GetWorldCorners(corners);
                Canvas c = r.GetComponentInParent<Canvas>();
                Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
                float topY = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y;
                best = Mathf.Max(best, Screen.height - topY);
            }
            return best;
        }

        void Refresh()
        {
            if (info == null) return;
            SafeAreaConfig c = SafeAreaConfig.Current;
            InsetProvider.Insets all = InsetProvider.Get();
            Rect safe = Screen.safeArea;
            // green outline of the safe area (screen pixels; this canvas is 1:1 with the screen)
            SetEdge("EdgeT", new Rect(safe.xMin, safe.yMax - 3, safe.width, 3));
            SetEdge("EdgeB", new Rect(safe.xMin, safe.yMin, safe.width, 3));
            SetEdge("EdgeL", new Rect(safe.xMin, safe.yMin, 3, safe.height));
            SetEdge("EdgeR", new Rect(safe.xMax - 3, safe.yMin, 3, safe.height));
            // red box on every cutout
            Rect[] cutouts = Screen.cutouts ?? new Rect[0];
            while (cutoutBoxes.Count < cutouts.Length) { Image b = Box(drawLayer, "Cutout" + cutoutBoxes.Count, new Color(1f, 0.1f, 0.1f, 0.55f)); cutoutBoxes.Add(b); }
            for (int i = 0; i < cutoutBoxes.Count; i++)
            {
                bool on = i < cutouts.Length;
                cutoutBoxes[i].gameObject.SetActive(on);
                if (on) Place(cutoutBoxes[i].rectTransform, cutouts[i]);
            }

            foreach (var pair in toggleLabels)
            {
                bool on = pair.Key == "Top" ? c.applyTop : pair.Key == "Bottom" ? c.applyBottom : pair.Key == "Left" ? c.applyLeft : c.applyRight;
                pair.Value.text = pair.Key + (on ? ": ON" : ": off");
            }

            float lineY = calibration != null ? calibration.YFromTopPx : 0f;
            if (lineLabel != null) lineLabel.text = $"line {lineY:0} px ({100f * lineY / Mathf.Max(1, Screen.height):0.0}% from top) | app top inset {all.top:0} px";

            float contentTop = ContentTopPx(out string screen);
            if (markedHoleBottom > 0f && contentTop >= 0f)
                verdicts[screen] = (contentTop + 0.5f >= markedHoleBottom ? "PASS" : "FAIL") + $" (content top {contentTop:0} px, hole bottom {markedHoleBottom:0} px)";
            var verdictText = new StringBuilder();
            foreach (var v in verdicts) verdictText.Append(v.Key).Append(": ").Append(v.Value).Append('\n');
            passText.text = markedHoleBottom > 0f ? verdictText.ToString() : "Drag the blue line to the bottom of the camera hole, then tap Mark hole bottom.";
            passText.color = verdictText.ToString().Contains("FAIL") ? new Color(1f, 0.45f, 0.45f) : new Color(0.5f, 1f, 0.55f);

            warning.text = InsetProvider.NoCutoutReported
                ? $"Android reports NO cutout. Fallback in use: {InsetProvider.Source}."
                : "";
            info.text = Values(all, contentTop, screen);
        }

        string Values(InsetProvider.Insets all, float contentTop, string screen)
        {
            SafeAreaConfig c = SafeAreaConfig.Current;
            var s = new StringBuilder();
            s.Append($"Screen {Screen.width} x {Screen.height}, {Screen.orientation}, dpi {Screen.dpi:0}, Android API {InsetProvider.AndroidApi}\n");
            Rect safe = Screen.safeArea;
            s.Append($"safeArea x {safe.x:0} y {safe.y:0} w {safe.width:0} h {safe.height:0}\n");
            Rect[] cutouts = Screen.cutouts ?? new Rect[0];
            s.Append($"cutouts: {cutouts.Length}");
            foreach (Rect r in cutouts) s.Append($"  [x {r.x:0} y {r.y:0} w {r.width:0} h {r.height:0}]");
            s.Append('\n');
            s.Append($"source: {InsetProvider.Source}\n");
            s.Append($"insets px: {all}\n");
            s.Append($"applied px: top {(c.applyTop ? all.top : 0):0}, bottom {(c.applyBottom ? all.bottom : 0):0}, left {(c.applyLeft ? all.left : 0):0}, right {(c.applyRight ? all.right : 0):0}\n");
            s.Append($"screen {screen}: content top {contentTop:0} px, marked hole bottom {(markedHoleBottom > 0 ? markedHoleBottom.ToString("0") : "-")} px");
            return s.ToString();
        }

        string Report()
        {
            var s = new StringBuilder(Values(InsetProvider.Get(), ContentTopPx(out string screen), screen));
            s.Append('\n').Append(warning.text).Append('\n');
            foreach (var v in verdicts) s.Append(v.Key).Append(": ").Append(v.Value).Append('\n');
            return s.ToString();
        }

        // ---- small UI helpers (screen pixels) ----
        void SetEdge(string name, Rect r) { Transform t = safeOutline.Find(name); if (t != null) Place((RectTransform)t, r); }

        static void Place(RectTransform rt, Rect r)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
            rt.anchoredPosition = r.position; rt.sizeDelta = r.size;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static Image Box(Transform parent, string name, Color color)
        {
            RectTransform rt = Rect(name, parent);
            Image i = rt.gameObject.AddComponent<Image>(); i.color = color; i.raycastTarget = false;
            return i;
        }

        static TextMeshProUGUI Label(RectTransform parent, string name, float size, Vector2 topLeft, Vector2 sizeDelta, Color color)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft; rt.sizeDelta = sizeDelta;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = size; t.color = color; t.raycastTarget = false; t.enableWordWrapping = true; t.alignment = TextAlignmentOptions.TopLeft;
            return t;
        }

        Button MakeButton(RectTransform parent, string words, Vector2 anchor, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rt = Rect(words, parent);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0f, anchor.y);
            rt.anchoredPosition = position; rt.sizeDelta = size;
            Image back = rt.gameObject.AddComponent<Image>(); back.color = new Color(0.85f, 0.42f, 0.54f, 0.95f);
            Button b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(onClick);
            TextMeshProUGUI t = Label(rt, "Label", 26 * k, Vector2.zero, Vector2.zero, Color.white);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Center; t.text = words;
            return b;
        }
    }
}
