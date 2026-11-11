using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Theme;
using GlowUp.Views;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Kit = GlowUp.EditorTools.OnboardingKit;
using Object = UnityEngine.Object;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Building blocks for the app scenes (Dashboard, Recent Scans, AI Smart Scan, Loading, Result, AR Try-On).
    ///
    /// How these scenes are built:
    ///  - The scene's existing panel is kept and reused as the card.
    ///  - The FIRST time a scene is built, everything that was inside that panel is moved into an inactive
    ///    object named "_Legacy (old objects - hidden, not deleted)". Nothing of yours is deleted.
    ///  - Old manager objects at the top of the Hierarchy (the ones with the old scripts) are switched off.
    ///  - Everything the builder creates carries the GeneratedByBuilder mark and is rebuilt on every run.
    ///
    /// Sizes are canvas units; the canvas is always 1080 wide, so the card is 1000 wide.
    /// </summary>
    public static class AppKit
    {
        public const string LegacyName = "_Legacy (old objects - hidden, not deleted)";
        public const float Pad = 40;                        // space between the card edge and its content
        public const float HeaderBottom = 160;              // first free unit under the back button / title row
        public const float BarBottom = 28, BarHeight = 176; // the bottom action bar
        // the line its icons stand on (above the bar's middle) and the size of the pink pill behind one of them. Phase 1D (11 November 2026):
        // the pill is 100 (was 108) and stands 8 units under the bar's top edge and 14 above the labels, at rest and while it moves.
        public const float BarIconY = 30, BarPill = 100;
        public const float AboveBar = BarBottom + BarHeight + 20;

        public static readonly Vector2 TL = new Vector2(0, 1), TC = new Vector2(0.5f, 1), TR = new Vector2(1, 1),
                                       ML = new Vector2(0, 0.5f), MC = new Vector2(0.5f, 0.5f), MR = new Vector2(1, 0.5f),
                                       BL = new Vector2(0, 0), BC = new Vector2(0.5f, 0), BR = new Vector2(1, 0);

        public class AppScreen
        {
            public Scene scene;
            public Transform canvas, card;
            public Button back;
            public TextMeshProUGUI title;
        }

        public class AppPopup
        {
            public GameObject root;
            public OverlayPanel panel;
            public RectTransform box;
            public Button close;
            public TextMeshProUGUI title;
        }

        static Transform topCard, topCanvas;                // objects created directly under these get the GeneratedByBuilder mark

        // =====================================================================================
        //  Begin / finish
        // =====================================================================================
        public static AppScreen Begin(string sceneName, string title, bool showBack, string newCardName, params string[] existingCardNames)
        {
            var s = new AppScreen { scene = SceneBuilderCore.Open(sceneName) };
            s.canvas = SceneBuilderCore.EnsureCanvas().rootCanvas.transform;
            SceneBuilderCore.EnsureSceneServices();

            // 1. whatever an earlier run of this builder created is rebuilt from scratch
            foreach (var mark in Object.FindObjectsOfType<GeneratedByBuilder>(true))
                if (mark != null) Object.DestroyImmediate(mark.gameObject);

            // 2. the card: the scene's own panel when it has one
            Transform card = null;
            foreach (string n in existingCardNames) { card = Kit.Loose(s.canvas, n); if (card != null) break; }
            if (card == null) card = Kit.Loose(s.canvas, newCardName);
            if (card == null)
            {
                var g = new GameObject(newCardName, typeof(RectTransform), typeof(Image));
                g.layer = s.canvas.gameObject.layer; g.transform.SetParent(s.canvas, false); card = g.transform;
            }
            if (card.parent != s.canvas) card.SetParent(s.canvas, false);
            s.card = card;

            // 3. first build only: the old content moves into a hidden holder (kept, not deleted)
            Transform legacy = null;
            foreach (Transform child in card) if (child.name == LegacyName) { legacy = child; break; }
            if (legacy == null)
            {
                var holder = new GameObject(LegacyName, typeof(RectTransform));
                holder.layer = card.gameObject.layer; holder.transform.SetParent(card, false);
                legacy = holder.transform; Kit.Stretch(legacy, 0, 0, 0, 0);
                var move = new List<Transform>();
                foreach (Transform child in card) if (child != legacy) move.Add(child);
                foreach (Transform child in s.canvas)
                    if (child != card && !IsBackground(child) && child.GetComponentInChildren<EventSystem>(true) == null) move.Add(child);
                foreach (Transform t in move) t.SetParent(legacy, true);
                holder.SetActive(false);
            }
            legacy.SetAsFirstSibling();

            // 4. old manager objects (old scripts without a namespace): switched off, never deleted
            foreach (GameObject root in s.scene.GetRootGameObjects())
            {
                if (root.transform == s.canvas || root.GetComponent<Camera>() != null || root.GetComponent<EventSystem>() != null) continue;
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && IsOldScript(mb)) { root.SetActive(false); break; }
            }
            Kit.RetireLegacy(s.canvas.gameObject);
            Kit.RetireLegacy(card.gameObject);
            if (Object.FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // 5. the card itself: equal margins, round, paper colour
            var crt = (RectTransform)card;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.pivot = MC;
            crt.localScale = Vector3.one; crt.localRotation = Quaternion.identity;
            crt.offsetMin = new Vector2(Kit.CardMargin, Kit.CardMargin); crt.offsetMax = new Vector2(-Kit.CardMargin, -Kit.CardMargin);
            foreach (var lg in card.GetComponents<LayoutGroup>()) lg.enabled = false;
            foreach (var f in card.GetComponents<ContentSizeFitter>()) f.enabled = false;
            foreach (var sr in card.GetComponents<ScrollRect>()) sr.enabled = false;
            var cardImage = SceneBuilderCore.Ensure<Image>(card.gameObject);
            cardImage.enabled = true; Kit.StyleRounded(cardImage); Kit.SetAlpha(cardImage, Kit.CardAlpha);
            SceneBuilderCore.Themed(card.gameObject, ThemeRole.Surface);
            Kit.EnsureOutline(card, "PanelOutline", ThemeRole.Gold);            // the thin gold frame of every card
            legacy.SetAsFirstSibling();

            topCard = card; topCanvas = s.canvas;

            // 6. header: back button and the screen title
            if (showBack)
            {
                s.back = IconButton(card, "BackButton", IconFactory.Back, 92, 60, ThemeRole.MainText);
                At(s.back.transform, TL, new Vector2(28, -28), new Vector2(92, 92));
            }
            if (!string.IsNullOrEmpty(title))
            {
                s.title = Label(card, "ScreenTitle", title, 60, ThemeRole.Heading, FontRole.Title, TextAlignmentOptions.Center);
                TopStrip(s.title.transform, 30, 96, 136, 136);
                Shrink(s.title, 40);
            }
            return s;
        }

        /// <summary>The shared final pass (round shapes, fonts, colours, paper background), then the scene is saved.</summary>
        public static void Finish(AppScreen s)
        {
            ThemeKit.FinishScene();
            SceneBuilderCore.Save(s.scene);
            BuildSettingsSync.Run();                // a scene that is not in Build Settings cannot be opened by the app
            topCard = topCanvas = null;
        }

        static bool IsBackground(Transform t) { string n = t.name.Trim(); return n == "DarkBackground" || n == "PaperBackground"; }

        static bool IsOldScript(MonoBehaviour mb)
        {
            Type type = mb.GetType();
            return string.IsNullOrEmpty(type.Namespace) && type.Assembly.GetName().Name == "Assembly-CSharp";
        }

        /// <summary>The scene's controller object (kept across rebuilds, so its Inspector settings survive).</summary>
        public static T Controller<T>(string objectName) where T : Component
        {
            return SceneBuilderCore.Ensure<T>(SceneBuilderCore.EnsureRoot(objectName));
        }

        public static void Set(Object target, string field, object value) { SceneBuilderCore.SetField(target, field, value); }

        // =====================================================================================
        //  Objects and placement
        // =====================================================================================
        public static GameObject Node(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (Type c in components) if (go.GetComponent(c) == null) go.AddComponent(c);
            if (parent == topCard || parent == topCanvas) go.AddComponent<GeneratedByBuilder>();
            return go;
        }

        /// <summary>Stretches over the whole parent, with optional insets.</summary>
        public static RectTransform Fill(Transform t, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            var rt = (RectTransform)t; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = MC;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>A row `y` units under the parent's top edge, `h` high, stretched across with side insets.</summary>
        public static RectTransform TopStrip(Transform t, float y, float h, float left = Pad, float right = Pad)
        {
            var rt = (RectTransform)t; rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -y - h); rt.offsetMax = new Vector2(-right, -y);
            return rt;
        }

        /// <summary>A row `y` units above the parent's bottom edge, `h` high, stretched across with side insets.</summary>
        public static RectTransform BottomStrip(Transform t, float y, float h, float left = Pad, float right = Pad)
        {
            var rt = (RectTransform)t; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(left, y); rt.offsetMax = new Vector2(-right, y + h);
            return rt;
        }

        /// <summary>Fills the space between `top` units from the top and `bottom` units from the bottom (fits any screen height).</summary>
        public static RectTransform Between(Transform t, float top, float bottom, float left = Pad, float right = Pad)
        {
            return Fill(t, left, bottom, right, top);
        }

        /// <summary>A fixed-size object: `anchor` is both the point of the parent it hangs on and its own pivot.</summary>
        public static RectTransform At(Transform t, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)t; rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        /// <summary>A share of the parent's width (from `from` to `to`, 0..1), full height, with a gap on each side.</summary>
        public static RectTransform Share(Transform t, float from, float to, float gap = 0, float bottom = 0, float top = 0)
        {
            var rt = (RectTransform)t; rt.anchorMin = new Vector2(from, 0); rt.anchorMax = new Vector2(to, 1); rt.pivot = MC;
            rt.offsetMin = new Vector2(gap, bottom); rt.offsetMax = new Vector2(-gap, -top);
            return rt;
        }

        // =====================================================================================
        //  Layout groups
        // =====================================================================================
        public static VerticalLayoutGroup VStack(GameObject go, float spacing, int left = 0, int right = 0, int top = 0, int bottom = 0,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = SceneBuilderCore.Ensure<VerticalLayoutGroup>(go);
            v.spacing = spacing; v.padding = new RectOffset(left, right, top, bottom); v.childAlignment = align;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(GameObject go, float spacing, int left = 0, int right = 0, int top = 0, int bottom = 0,
            TextAnchor align = TextAnchor.MiddleLeft, bool expandWidth = false)
        {
            var h = SceneBuilderCore.Ensure<HorizontalLayoutGroup>(go);
            h.spacing = spacing; h.padding = new RectOffset(left, right, top, bottom); h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = expandWidth; h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>Fixed and flexible sizes for an object inside a layout group. Negative = leave alone.</summary>
        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = SceneBuilderCore.Ensure<LayoutElement>(c.gameObject);
            if (width >= 0) { le.minWidth = width; le.preferredWidth = width; }
            if (height >= 0) { le.minHeight = height; le.preferredHeight = height; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return le;
        }

        // =====================================================================================
        //  Text
        // =====================================================================================
        /// <summary>A text whose colour follows the theme role and whose font follows the font role.</summary>
        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, ThemeRole color, FontRole font,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var t = RawLabel(parent, name, text, size, Color.white, font, align, wrap);
            SceneBuilderCore.Themed(t.gameObject, color);
            return t;
        }

        /// <summary>A text with a fixed colour (white on the camera picture, or a colour its view script sets).</summary>
        public static TextMeshProUGUI RawLabel(Transform parent, string name, string text, float size, Color color, FontRole font,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var go = Node(parent, name, typeof(TextMeshProUGUI));
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.enableAutoSizing = false; t.fontSize = size; t.alignment = align; t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow; t.raycastTarget = false; t.richText = true; t.fontStyle = FontStyles.Normal;
            t.color = color; t.margin = Vector4.zero;
            var tf = SceneBuilderCore.Ensure<ThemedFont>(go); tf.Role = font; tf.KeepRole = true;
            return t;
        }

        /// <summary>One line that gets smaller (down to `minSize`) instead of spilling out of its box, then ends with "...".</summary>
        public static TextMeshProUGUI Shrink(TextMeshProUGUI t, float minSize)
        {
            t.enableWordWrapping = false; t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = minSize;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>Up to `lines` lines; smaller text before anything is cut.</summary>
        public static TextMeshProUGUI ShrinkWrapped(TextMeshProUGUI t, float minSize)
        {
            t.enableWordWrapping = true; t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = minSize;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static TextMeshProUGUI LabelOf(Component c) { return c.GetComponentInChildren<TextMeshProUGUI>(true); }

        /// <summary>
        /// Makes a helper text a hint: it is shown only to a new user, or when the app senses the user is
        /// struggling (see the HintController object). Hidden hints take no space inside a layout.
        /// </summary>
        public static void HintOnly(Component c)
        {
            SceneBuilderCore.Ensure<CanvasGroup>(c.gameObject);
            SceneBuilderCore.Ensure<LayoutElement>(c.gameObject);
            SceneBuilderCore.Ensure<HintGroup>(c.gameObject);
        }

        // =====================================================================================
        //  Shapes and icons
        // =====================================================================================
        /// <summary>A round panel whose colour follows the theme.</summary>
        public static Image Panel(Transform parent, string name, ThemeRole role, float alpha = 1f, float maxRadius = 56f)
        {
            var img = RawPanel(parent, name, new Color(1, 1, 1, alpha), maxRadius);
            SceneBuilderCore.Themed(img.gameObject, role);
            return img;
        }

        /// <summary>A round panel with a fixed colour (or a colour its view script sets).</summary>
        public static Image RawPanel(Transform parent, string name, Color color, float maxRadius = 56f)
        {
            var go = Node(parent, name, typeof(Image));
            var img = go.GetComponent<Image>();
            Kit.StyleRounded(img); Kit.SetRadius(img, maxRadius);
            img.color = color; img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// The round background of an object whose size comes from a layout group. It is a child the layout
        /// ignores (a round Image on the object itself would push the object's size up).
        /// </summary>
        public static Image Backdrop(Transform parent, ThemeRole role, float alpha = 1f, float maxRadius = 48f, string name = "Background")
        {
            var img = Panel(parent, name, role, alpha, maxRadius);
            Fill(img.transform);
            SceneBuilderCore.Ensure<LayoutElement>(img.gameObject).ignoreLayout = true;
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>A thin round outline on the edge of `parent` (drawn above its background, below its content).</summary>
        public static Image Outline(Transform parent, ThemeRole role, float maxRadius = 48f, float alpha = 1f, string name = "Outline")
        {
            var go = Node(parent, name, typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = IconFactory.PillOutline; img.type = Image.Type.Sliced; img.raycastTarget = false; img.color = new Color(1, 1, 1, alpha);
            Kit.SetRadius(img, maxRadius);
            SceneBuilderCore.Themed(go, role);
            Fill(go.transform);
            SceneBuilderCore.Ensure<LayoutElement>(go).ignoreLayout = true;
            Transform background = parent.Find("Background");
            go.transform.SetSiblingIndex(background != null ? background.GetSiblingIndex() + 1 : 0);
            return img;
        }

        public static Image Icon(Transform parent, string name, Sprite sprite, float size, ThemeRole role)
        {
            var img = RawIcon(parent, name, sprite, size, Color.white);
            SceneBuilderCore.Themed(img.gameObject, role);
            return img;
        }

        public static Image RawIcon(Transform parent, string name, Sprite sprite, float size, Color color)
        {
            var go = Node(parent, name, typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Simple; img.preserveAspect = true; img.raycastTarget = false; img.color = color;
            At(go.transform, MC, Vector2.zero, new Vector2(size, size));
            return img;
        }

        /// <summary>A round tile with an icon in the middle (product type, avatar ...). Returns the icon.</summary>
        public static Image IconTile(Transform parent, string name, Sprite sprite, float size, float iconSize, ThemeRole fill, ThemeRole iconRole)
        {
            var tile = Panel(parent, name, fill, 1f, size);
            At(tile.transform, TL, Vector2.zero, new Vector2(size, size));
            return Icon(tile.transform, "Icon", sprite, iconSize, iconRole);
        }

        // =====================================================================================
        //  Buttons
        // =====================================================================================
        /// <summary>A round-ended button with a label and an optional icon in front of it.</summary>
        public static Button PillButton(Transform parent, string name, string label, float fontSize, ThemeRole fill, ThemeRole text,
            Sprite icon = null, float alpha = 1f)
        {
            var go = Node(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            Kit.StyleRounded(img); img.color = new Color(1, 1, 1, alpha);
            SceneBuilderCore.Themed(go, fill);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;

            var row = Node(go.transform, "Content");
            Fill(row.transform, 18, 0, 18, 0);
            HStack(row, 14, 0, 0, 0, 0, TextAnchor.MiddleCenter);
            if (icon != null)
            {
                var ic = Icon(row.transform, "Icon", icon, fontSize * 1.05f, text);
                Size(ic, fontSize * 1.05f, fontSize * 1.05f);
            }
            var lbl = Label(row.transform, "Label", label, fontSize, text, FontRole.Button, TextAlignmentOptions.Center);
            lbl.overflowMode = TextOverflowModes.Overflow;
            return b;
        }

        /// <summary>A round icon-only button. Without `fill` only the icon shows (the whole square is still tappable).</summary>
        public static Button IconButton(Transform parent, string name, Sprite icon, float size, float iconSize, ThemeRole iconRole, ThemeRole? fill = null)
        {
            var go = Node(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            Kit.StyleRounded(img); Kit.SetRadius(img, size);
            if (fill.HasValue) { img.color = Color.white; SceneBuilderCore.Themed(go, fill.Value); }
            else img.color = new Color(1, 1, 1, 0.004f);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var ic = Icon(go.transform, "Icon", icon, iconSize, iconRole);
            var b = go.GetComponent<Button>();
            b.targetGraphic = fill.HasValue ? (Graphic)img : ic;
            return b;
        }

        /// <summary>A text-only button (a link). The whole rectangle is tappable.</summary>
        public static Button LinkButton(Transform parent, string name, string label, float fontSize, ThemeRole text)
        {
            var go = Node(parent, name, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>(); Kit.StyleRounded(img); img.color = new Color(1, 1, 1, 0.004f);
            var lbl = Label(go.transform, "Label", "<u>" + label + "</u>", fontSize, text, FontRole.Button, TextAlignmentOptions.Center);
            Fill(lbl.transform);
            var b = go.GetComponent<Button>(); b.targetGraphic = lbl;
            return b;
        }

        /// <summary>A soft pink halo around a main button (hollow in the middle, so it never covers the button).</summary>
        public static Image Glow(Button button, float buttonHeight, float alpha = 0.5f)
        {
            var go = Node(button.transform, "Glow", typeof(Image));
            go.transform.SetAsFirstSibling();
            var glow = go.GetComponent<Image>();
            glow.sprite = IconFactory.PillGlow; glow.type = Image.Type.Sliced; glow.raycastTarget = false; glow.color = new Color(1, 1, 1, alpha);
            float halo = IconFactory.PillGlowMargin * (buttonHeight * 0.5f) / PillCorners.SpriteRadius;
            Fill(go.transform, -halo, -halo, -halo, -halo);
            var corners = SceneBuilderCore.Ensure<PillCorners>(go);
            Set(corners, "spriteMargin", IconFactory.PillGlowMargin);
            Set(corners, "maxRadius", 200f);
            SceneBuilderCore.Ensure<LayoutElement>(go).ignoreLayout = true;
            SceneBuilderCore.Themed(go, ThemeRole.Accent);
            return glow;
        }

        // =====================================================================================
        //  Scroll lists
        // =====================================================================================
        /// <summary>A vertical scroll list: Viewport > Content (vertical layout) and a thin round scrollbar.</summary>
        public static ScrollRect Scroll(Transform parent, string name, out RectTransform content, float spacing = 18,
            int left = 0, int right = 18, int top = 4, int bottom = 28)
        {
            var holder = Node(parent, name);
            Fill(holder.transform);
            Kit.MakeScroll(parent, name, out content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing; layout.padding = new RectOffset(left, right, top, bottom);
            return holder.GetComponent<ScrollRect>();
        }

        // =====================================================================================
        //  Pop-ups
        // =====================================================================================
        /// <summary>
        /// A pop-up layer over the whole screen: a dim backdrop (tap it to close), a round box with a title and a
        /// close button. It is INACTIVE in the Hierarchy; tick it active to see and style it.
        /// </summary>
        public static AppPopup Overlay(AppScreen s, string name, string title)
        {
            var p = new AppPopup { root = Node(s.canvas, name, typeof(CanvasGroup), typeof(OverlayPanel)) };
            Fill(p.root.transform);
            p.panel = p.root.GetComponent<OverlayPanel>();

            var dim = Node(p.root.transform, "Backdrop", typeof(Image), typeof(Button));
            Fill(dim.transform);
            var dimImage = dim.GetComponent<Image>(); dimImage.sprite = IconFactory.Square; dimImage.color = new Color(0.10f, 0.08f, 0.08f, 0.6f);
            var dimButton = dim.GetComponent<Button>(); dimButton.transition = Selectable.Transition.None; dimButton.targetGraphic = dimImage;

            var box = Panel(p.root.transform, "Box", ThemeRole.Background, 1f, 64f);
            box.raycastTarget = true;                           // taps on the box do not reach the backdrop
            p.box = (RectTransform)box.transform;
            Outline(p.box, ThemeRole.Accent, 64f);

            p.title = Label(p.box, "PopupTitle", title, 50, ThemeRole.Heading, FontRole.Title);
            TopStrip(p.title.transform, 34, 80, 48, 128);
            Shrink(p.title, 34);
            p.close = IconButton(p.box, "CloseButton", IconFactory.Close, 84, 44, ThemeRole.MainText);
            At(p.close.transform, TR, new Vector2(-24, -30), new Vector2(84, 84));

            Set(p.panel, "box", p.box); Set(p.panel, "closeButton", p.close); Set(p.panel, "backdropButton", dimButton);
            p.root.SetActive(false);
            return p;
        }

        // =====================================================================================
        //  Bottom action bar: Recent Scans | Start Scan | Try On
        // =====================================================================================
        /// <summary>
        /// The bar's buttons have NO tap animation (no bounce, no scaling, no colour fade): a tap changes the screen at once.
        /// Which screen the user is on is shown by the PINK PILL behind an icon: Recent Scans = the left icon, the
        /// dashboard = the middle one (Start Scan), AR Try-On = the right one. While the user drags the page sideways the
        /// pill slides along, stretches like liquid and snaps to the icon of the screen that opens (LiquidTabBar).
        /// </summary>
        /// <param name="current">0 = none (the dashboard), 1 = Recent, 2 = Scan, 3 = Try On (the screen this bar is on)</param>
        public static BottomActionBar BottomBar(AppScreen s, int current)
        {
            var bar = Panel(s.card, "BottomActionBar", ThemeRole.SurfaceAlt, 0.94f, BarHeight);
            bar.raycastTarget = true;
            BottomStrip(bar.transform, BarBottom, BarHeight, 28, 28);
            Outline(bar.transform, ThemeRole.Accent, BarHeight, 0.55f);

            // the button the pill rests behind on this screen: 0 = Recent, 1 = the middle (the dashboard), 2 = Try On
            int pillSlot = current == 1 ? 0 : current == 3 ? 2 : 1;

            // the pink pill: created before the buttons, so it is drawn BEHIND their icons. In Edit mode it stands where it
            // rests; LiquidTabBar moves and stretches it while the app runs.
            Image drop = Panel(bar.transform, "LiquidDrop", ThemeRole.ButtonFill, 1f, BarPill);
            float third = (Kit.CardWidth - 56f) / 3f;
            At(drop.transform, MC, new Vector2((pillSlot - 1) * third, BarIconY), new Vector2(BarPill, BarPill));

            Button recent = BarButton(bar.transform, "RecentScansButton", "Recent", IconFactory.History, 0f, 1f / 3f, current == 1, pillSlot == 0);
            Button scan = BarButton(bar.transform, "StartScanButton", "Start Scan", IconFactory.Scan, 1f / 3f, 2f / 3f, current == 2, pillSlot == 1);
            Button tryOn = BarButton(bar.transform, "TryOnButton", "Try On", IconFactory.Sparkle, 2f / 3f, 1f, current == 3, pillSlot == 2);

            var liquid = SceneBuilderCore.Ensure<LiquidTabBar>(bar.gameObject);
            Set(liquid, "drop", drop.rectTransform); Set(liquid, "dropImage", drop);
            Kit.SetArray(liquid, "slots", new Object[] { recent.transform, scan.transform, tryOn.transform });
            Kit.SetArray(liquid, "slotIcons", new Object[] { recent.targetGraphic, scan.targetGraphic, tryOn.targetGraphic });
            Set(liquid, "current", pillSlot);
            Set(liquid, "restWidth", BarPill); Set(liquid, "restHeight", BarPill);

            var view = SceneBuilderCore.Ensure<BottomActionBar>(bar.gameObject);
            Set(view, "recentButton", recent); Set(view, "scanButton", scan); Set(view, "tryOnButton", tryOn);
            Set(view, "current", current);
            return view;
        }

        /// <param name="isCurrent">this button belongs to the screen the bar is on (its label is darker)</param>
        /// <param name="underPill">the pink pill rests behind this button's icon (the icon takes the colour of text on pink)</param>
        static Button BarButton(Transform bar, string name, string label, Sprite icon, float from, float to, bool isCurrent, bool underPill)
        {
            var go = Node(bar, name, typeof(Image), typeof(Button));
            Share(go.transform, from, to);
            var hit = go.GetComponent<Image>(); Kit.StyleRounded(hit); hit.color = new Color(1, 1, 1, 0.004f);
            // all three icons stand on one line: the line the pill travels along
            var ic = Icon(go.transform, "Icon", icon, 60, underPill ? ThemeRole.ButtonText : ThemeRole.MainText);
            ic.rectTransform.anchoredPosition = new Vector2(0, BarIconY);
            var text = Label(go.transform, "Label", label, 26, isCurrent ? ThemeRole.Heading : ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.Center);
            BottomStrip(text.transform, 14, 40, 8, 8);
            Shrink(text, 20);                                    // a longer word gets smaller, never runs into the next slot
            var b = go.GetComponent<Button>(); b.targetGraphic = ic;
            b.transition = Selectable.Transition.None;          // no tap animation on the bar: the screen simply changes
            return b;
        }

        // =====================================================================================
        //  Product information widgets
        // =====================================================================================
        /// <summary>The shared asset that says which icon goes with which kind of product (created once; edit it in the Inspector).</summary>
        public static CosmeticTypeIcons TypeIcons()
        {
            const string path = "Assets/_Project/CosmeticTypeIcons.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CosmeticTypeIcons>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<CosmeticTypeIcons>(); AssetDatabase.CreateAsset(asset, path); }
            if (asset.entries == null || asset.entries.Length == 0)
            {
                asset.entries = new[]
                {
                    new CosmeticTypeIcons.Entry { keywords = "lipstick, lip, gloss, tint", icon = IconFactory.Lipstick },
                    new CosmeticTypeIcons.Entry { keywords = "mascara, eyeliner, liner, brow", icon = IconFactory.Wand },
                    new CosmeticTypeIcons.Entry { keywords = "eyeshadow, eye shadow, palette, shadow", icon = IconFactory.Palette },
                    new CosmeticTypeIcons.Entry { keywords = "blush, bronzer, highlighter, powder, compact, cushion, contour", icon = IconFactory.Compact },
                    new CosmeticTypeIcons.Entry { keywords = "cream, moistur, mask, balm, butter, gel, scrub", icon = IconFactory.Jar },
                    new CosmeticTypeIcons.Entry { keywords = "foundation, concealer, primer, serum, toner, cleanser, lotion, sunscreen, oil, wash, essence, shampoo, mist", icon = IconFactory.Bottle }
                };
                asset.fallback = IconFactory.Bottle;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            // the builder's older, filled icons are swapped for the thin-line ones; an icon you put there yourself is kept
            bool changed = false;
            foreach (CosmeticTypeIcons.Entry entry in asset.entries)
                if (entry != null) { Sprite newer = LineIcon(entry.icon); if (newer != null) { entry.icon = newer; changed = true; } }
            Sprite newFallback = LineIcon(asset.fallback);
            if (newFallback != null) { asset.fallback = newFallback; changed = true; }
            if (changed) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets(); }
            return asset;
        }

        /// <summary>The thin-line icon that replaces one of the builder's older filled type icons, or null when `old` is not one of those.</summary>
        static Sprite LineIcon(Sprite old)
        {
            if (old == null) return null;
            switch (old.name)
            {
                case "icon_lipstick": return IconFactory.Lipstick;
                case "icon_wand": return IconFactory.Wand;
                case "icon_palette": return IconFactory.Palette;
                case "icon_compact": return IconFactory.Compact;
                case "icon_jar": return IconFactory.Jar;
                case "icon_bottle": return IconFactory.Bottle;
                default: return null;
            }
        }

        /// <summary>The safe / use-with-care / harmful pill. The caller places and sizes the returned object.</summary>
        public static StatusPillView StatusPill(Transform parent, string name, float fontSize = 28)
        {
            var bg = RawPanel(parent, name, new Color32(94, 127, 75, 255), 60f);
            var row = Node(bg.transform, "Content");
            Fill(row.transform, 16, 0, 16, 0);
            HStack(row, 10, 0, 0, 0, 0, TextAnchor.MiddleCenter);
            var icon = RawIcon(row.transform, "Icon", IconFactory.Check, fontSize, Color.white);
            Size(icon, fontSize, fontSize);
            var label = RawLabel(row.transform, "Label", "Nothing flagged", fontSize, Color.white, FontRole.Button, TextAlignmentOptions.Center);

            var view = SceneBuilderCore.Ensure<StatusPillView>(bg.gameObject);
            Set(view, "background", bg); Set(view, "label", label); Set(view, "icon", icon);
            Set(view, "safeIcon", IconFactory.Check); Set(view, "cautionIcon", IconFactory.Warning); Set(view, "harmfulIcon", IconFactory.Close);
            return view;
        }

        /// <summary>
        /// One line of the compatibility matrix. `withDetail` adds the one-line reason under the title (result scene);
        /// without it the line is compact (scan cards).
        /// </summary>
        /// <param name="titleSize">text sizes of the title and the reason (with detail only; 0 = the usual 32 / 27)</param>
        public static CompatibilityIndicator Indicator(Transform parent, string name, string title, bool withDetail, float titleSize = 0, float detailSize = 0, float discSize = 0)
        {
            var go = Node(parent, name);
            HStack(go, withDetail ? 20 : 10, 0, 0, 0, 0, withDetail ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter);
            float disc = discSize > 0 ? discSize : withDetail ? 56 : 40;
            var discImage = RawPanel(go.transform, "IconDisc", new Color32(94, 127, 75, 255), disc);
            Size(discImage, disc, disc);
            var icon = RawIcon(discImage.transform, "Icon", IconFactory.Check, disc * 0.56f, Color.white);

            TextMeshProUGUI titleLabel, detail = null;
            if (withDetail)
            {
                var column = Node(go.transform, "Texts");
                VStack(column, 2);
                Size(column.transform, -1, -1, 1);
                titleLabel = Label(column.transform, "Title", title, titleSize > 0 ? titleSize : 32, ThemeRole.MainText, FontRole.Label);
                detail = Label(column.transform, "Detail", "The reason shows here once a product is analysed.", detailSize > 0 ? detailSize : 27, ThemeRole.SecondaryText, FontRole.Body,
                               TextAlignmentOptions.TopLeft, true);
            }
            else
            {
                titleLabel = Label(go.transform, "Title", title, 26, ThemeRole.MainText, FontRole.Label);
            }

            var view = SceneBuilderCore.Ensure<CompatibilityIndicator>(go);
            Set(view, "icon", icon); Set(view, "iconBackground", discImage); Set(view, "titleLabel", titleLabel);
            if (detail != null) Set(view, "detailLabel", detail);
            Set(view, "title", title);
            Set(view, "goodIcon", IconFactory.Check); Set(view, "cautionIcon", IconFactory.Warning);
            Set(view, "badIcon", IconFactory.Close); Set(view, "unknownIcon", IconFactory.Info);
            return view;
        }

        public const float ProductRowHeight = 268, ProductRowCompactHeight = 176;

        /// <summary>
        /// The pattern row for product lists (recommendations, alternatives, the try-on picker):
        /// icon tile, name, second line, note and one or two buttons. With no `secondary` the row is compact
        /// and the whole row is tappable.
        /// </summary>
        public static ProductRowView ProductRow(Transform content, string name, string title, string subtitle, string note, string primary, string secondary)
        {
            bool compact = secondary == null;
            var go = Node(content, name, typeof(Image));
            Size(go.transform, -1, compact ? ProductRowCompactHeight : ProductRowHeight);
            var bg = go.GetComponent<Image>(); Kit.StyleRounded(bg); Kit.SetRadius(bg, 48f); bg.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.InputBackground);
            Outline(go.transform, ThemeRole.SurfaceAlt, 48f);

            var tile = Panel(go.transform, "IconTile", ThemeRole.SurfaceAlt, 1f, 120f);
            At(tile.transform, TL, new Vector2(22, -22), new Vector2(120, 120));
            var icon = Icon(tile.transform, "TypeIcon", IconFactory.Bottle, 66, ThemeRole.MainText);

            float textRight = compact ? 212 : 26;
            var titleText = Label(go.transform, "TitleText", title, 40, ThemeRole.MainText, FontRole.Heading);
            TopStrip(titleText.transform, 16, 62, 166, textRight); Shrink(titleText, 30);
            var subtitleText = Label(go.transform, "SubtitleText", subtitle, 29, ThemeRole.SecondaryText, FontRole.Body);
            TopStrip(subtitleText.transform, 78, 46, 166, textRight); Shrink(subtitleText, 24);
            var noteText = Label(go.transform, "NoteText", note, 28, compact ? ThemeRole.SecondaryText : ThemeRole.Success, FontRole.Label);
            TopStrip(noteText.transform, 122, 44, 166, textRight); Shrink(noteText, 22);

            var view = SceneBuilderCore.Ensure<ProductRowView>(go);
            Set(view, "icon", icon); Set(view, "titleText", titleText); Set(view, "subtitleText", subtitleText); Set(view, "noteText", noteText);

            Button primaryButton;
            if (compact)
            {
                bg.raycastTarget = true;
                var rowButton = SceneBuilderCore.Ensure<Button>(go); rowButton.targetGraphic = bg;
                Set(view, "rowButton", rowButton);
                primaryButton = PillButton(go.transform, "PrimaryButton", primary, 30, ThemeRole.ButtonFill, ThemeRole.ButtonText);
                At(primaryButton.transform, MR, new Vector2(-22, 0), new Vector2(170, 76));
            }
            else
            {
                primaryButton = PillButton(go.transform, "PrimaryButton", primary, 32, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sparkle);
                At(primaryButton.transform, BR, new Vector2(-22, 20), new Vector2(250, 76));
                var secondaryButton = PillButton(go.transform, "SecondaryButton", secondary, 32, ThemeRole.SurfaceAlt, ThemeRole.MainText);
                At(secondaryButton.transform, BR, new Vector2(-288, 20), new Vector2(190, 76));
                Set(view, "secondaryButton", secondaryButton); Set(view, "secondaryLabel", LabelOf(secondaryButton));
            }
            Set(view, "primaryButton", primaryButton); Set(view, "primaryLabel", LabelOf(primaryButton));
            return view;
        }

        // =====================================================================================
        //  Shared pop-ups
        // =====================================================================================
        /// <summary>The "rename this product" dialog (the new name is kept on this device only).</summary>
        public static OverlayPanel RenameDialog(AppScreen s, out TMP_InputField input, out Button save)
        {
            AppPopup p = Overlay(s, "RenamePanel", "Rename");
            At(p.box, MC, new Vector2(0, 140), new Vector2(900, 560));

            var hint = Label(p.box, "RenameHintText", "The new name is saved on this phone only. The product keeps its real name everywhere else.",
                             29, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            TopStrip(hint.transform, 124, 92, 48, 48);

            input = SceneBuilderCore.MakeInput(p.box, "RenameInputField", "Product name", TC, Vector2.zero, new Vector2(800, Kit.FieldHeight));
            TopStrip(input.transform, 232, Kit.FieldHeight, 48, 48);
            Kit.StyleInput(input, "Product name", ThemeRole.InputBackground);
            var behaviour = SceneBuilderCore.Ensure<InputFieldBehaviour>(input.gameObject);
            Set(behaviour, "characterLimit", 64);

            var cancel = PillButton(p.box, "CancelButton", "Cancel", 38, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            BottomStrip(cancel.transform, 44, 112, 48, 462);
            save = PillButton(p.box, "SaveButton", "Save", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            BottomStrip(save.transform, 44, 112, 462, 48);

            // Cancel closes the dialog just like the X does
            UnityEditor.Events.UnityEventTools.AddPersistentListener(cancel.onClick, p.panel.Hide);
            return p.panel;
        }

        /// <summary>
        /// The ingredient list pop-up: every ingredient with its level, a quick description, where it is used and
        /// why it matters for this user, and the advice to see a dermatologist.
        /// </summary>
        public static IngredientSheet IngredientSheetPopup(AppScreen s)
        {
            AppPopup p = Overlay(s, "IngredientSheet", "Ingredients");
            Fill(p.box, 40, 40, 40, 150);

            var subtitle = Label(p.box, "SheetSubtitleText", "12 ingredients, 2 to look at", 30, ThemeRole.SecondaryText, FontRole.Body);
            TopStrip(subtitle.transform, 112, 46, 48, 48);

            // (the legend of coloured dots was taken out on 11 October 2026: every row says its level in words next to its own dot,
            //  so the legend said the same thing a second time)
            var listArea = Node(p.box, "ListArea");
            Fill(listArea.transform, 32, 300, 32, 176);
            ScrollRect scroll = Scroll(listArea.transform, "IngredientScrollView", out RectTransform content, 24, 0, 22, 4, 24);   // 24: closed cards read as separate items

            // ---- the pattern row ----
            var row = Node(content, "IngredientRowTemplate");
            VStack(row, 6, 26, 26, 18, 20);
            Backdrop(row.transform, ThemeRole.InputBackground, 1f, 40f);
            Outline(row.transform, ThemeRole.SurfaceAlt, 40f);
            // the top line: the INCI name (with the first alias in brackets) and, on the right, the classification in ONE neutral style
            var topLine = Node(row.transform, "TopLine");
            HStack(topLine, 12, 0, 0, 0, 0, TextAnchor.UpperLeft);
            var nameText = Label(topLine.transform, "NameText", "Niacinamide  <size=80%>(Vitamin B3)</size>", 36, ThemeRole.MainText, FontRole.Heading,
                                 TextAlignmentOptions.TopLeft, true);
            Size(nameText, 0, -1, 1);                                            // the name takes what the badge leaves over, and wraps
            var badge = Node(topLine.transform, "ClassificationBadge");
            HStack(badge, 0, 16, 16, 6, 6, TextAnchor.MiddleCenter);
            Backdrop(badge.transform, ThemeRole.SurfaceAlt, 1f, 24f);
            var badgeText = Label(badge.transform, "ClassificationText", "Humectant", 23, ThemeRole.SecondaryText, FontRole.Label, TextAlignmentOptions.Center);
            var levelLine = Node(row.transform, "LevelLine");
            HStack(levelLine, 12);
            var dot = RawPanel(levelLine.transform, "LevelDot", new Color32(94, 127, 75, 255), 20f);
            Size(dot, 22, 22);
            var levelText = RawLabel(levelLine.transform, "LevelText", "Not flagged", 28, new Color32(94, 127, 75, 255), FontRole.Label);
            var purpose = Label(row.transform, "PurposeText", "Purpose: Helps even out skin tone and supports the skin barrier.",
                                28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);   // same size as the status, never the loudest line
            var reason = RawLabel(row.transform, "ReasonText", "For you: this is where the reason shows when an ingredient is flagged for your skin.",
                                  28, new Color32(169, 116, 28, 255), FontRole.Label, TextAlignmentOptions.TopLeft, true);

            // the fold at the bottom of the card: closed by default; each row has its own
            var fold = Node(row.transform, "Fold");
            VStack(fold, 6);
            var foldToggle = Node(fold.transform, "FoldToggle", typeof(Image), typeof(Button));
            var foldTouch = foldToggle.GetComponent<Image>();
            foldTouch.color = new Color(1, 1, 1, 0);                             // invisible, but the whole line can be tapped
            foldToggle.GetComponent<Button>().targetGraphic = foldTouch;
            HStack(foldToggle, 10);
            Size(foldToggle.transform, -1, 56);
            Label(foldToggle.transform, "FoldLabel", "More about this ingredient", 27, ThemeRole.SecondaryText, FontRole.Label);
            var foldArrow = Icon(foldToggle.transform, "FoldArrow", IconFactory.ChevronDown, 28, ThemeRole.SecondaryText);
            Size(foldArrow, 28, 28);
            var foldBody = Node(fold.transform, "FoldBody", typeof(RectMask2D));
            VStack(foldBody, 8, 0, 0, 2, 4);
            TextMeshProUGUI Fact(string objectName, string sample) => Label(foldBody.transform, objectName, sample, 27, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            var description = Fact("DescriptionText", "<b>What it is:</b> A form of vitamin B3 that helps even out skin tone and supports the skin barrier.");
            var aliasesText = Fact("AliasesText", "<b>Also known as:</b> Nicotinamide, Vitamin B3");
            var chemical = Fact("ChemicalText", "<b>What the chemical is:</b> The amide form of vitamin B3.");
            var uses = Fact("UsesText", "<b>Commonly used in:</b> serums, moisturisers, toners");
            var highlights = Fact("HighlightsText", "<mark=#8C7F7F33> Key active </mark>  <mark=#8C7F7F33> Umbrella term </mark>");
            var allergen = Fact("AllergenText", "<b>Allergen family:</b> Fragrance / Parfum");
            var caution = Fact("CautionText", "<b>Caution:</b> It can clog pores.");
            var sourceText = Label(foldBody.transform, "SourceText", "Source: GlowUp ingredient list (checked by the GlowUp team)", 25, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            var sourceLink = Node(foldBody.transform, "SourceLink", typeof(Image), typeof(Button));
            var linkTouch = sourceLink.GetComponent<Image>();
            linkTouch.color = new Color(1, 1, 1, 0);
            sourceLink.GetComponent<Button>().targetGraphic = linkTouch;
            HStack(sourceLink, 0);
            Size(sourceLink.transform, -1, 52);
            Label(sourceLink.transform, "SourceLinkLabel", "Open the source", 26, ThemeRole.ButtonFill, FontRole.Label);
            foldBody.SetActive(false);                                           // opens when "More about this ingredient" is tapped; tick it active to see and restyle it
            var expand = SceneBuilderCore.Ensure<ExpandableSection>(fold);
            Set(expand, "toggleButton", foldToggle.GetComponent<Button>()); Set(expand, "arrow", foldArrow.rectTransform); Set(expand, "body", foldBody);
            SceneBuilderCore.SetField(expand, "animateSeconds", 0.2f);

            var rowView = SceneBuilderCore.Ensure<IngredientRowView>(row);
            Set(rowView, "nameText", nameText); Set(rowView, "levelDot", dot); Set(rowView, "levelText", levelText);
            Set(rowView, "classificationBadge", badge); Set(rowView, "classificationText", badgeText); Set(rowView, "purposeText", purpose);
            Set(rowView, "descriptionText", description); Set(rowView, "usesText", uses); Set(rowView, "reasonText", reason);
            Set(rowView, "fold", fold); Set(rowView, "aliasesText", aliasesText); Set(rowView, "chemicalText", chemical); Set(rowView, "highlightsText", highlights);
            Set(rowView, "allergenText", allergen); Set(rowView, "cautionText", caution); Set(rowView, "sourceText", sourceText); Set(rowView, "sourceLinkButton", sourceLink.GetComponent<Button>());

            // ---- footer: see a specialist ----
            var advice = Label(p.box, "DermatologistAdviceText",
                "Not sure about an ingredient, or noticed a reaction? A dermatologist or skin specialist can give you a proper assessment.",
                28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            BottomStrip(advice.transform, 170, 112, 48, 48);
            var derm = PillButton(p.box, "DermatologistButton", "Find a dermatologist", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.User);
            BottomStrip(derm.transform, 44, 112, 48, 48);

            var sheet = SceneBuilderCore.Ensure<IngredientSheet>(p.root);
            Set(sheet, "panel", p.panel); Set(sheet, "titleText", p.title); Set(sheet, "subtitleText", subtitle);
            Set(sheet, "scroll", scroll); Set(sheet, "listContent", content); Set(sheet, "rowTemplate", rowView);
            Set(sheet, "dermatologistButton", derm);
            return sheet;
        }

    }
}
