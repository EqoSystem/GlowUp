using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Shared building blocks for the scene builders (Login, Onboarding 1-5, Terms).
    /// Rules: reuse existing objects by name, never delete yours. Old script components that would
    /// fight with the new controllers are DISABLED (not removed) and their stale OnClick wiring cleared.
    ///
    /// All sizes are in canvas units. The canvas is always 1080 wide (about 3 units per dp on a phone),
    /// so readable body text is roughly 38-46 units.
    /// </summary>
    public static class OnboardingKit
    {
        // ---- layout ----
        public const float CardMargin = 40;                 // equal on all four sides
        public const float PadX = 70;                       // space between the panel edge and its content
        public const float CardWidth = 1000, InnerWidth = CardWidth - 2 * PadX;
        public const float HeaderBottom = 370;              // first free unit under the title / subtitle / progress bar
        public const float FooterTop = 262;                 // height the Continue button + message line take at the bottom
        public const float FieldHeight = 132, ButtonHeight = 140, LabelHeight = 54, HelperHeight = 48, RequirementHeight = 50;

        // ---- text sizes ----
        public const float TitleSize = 104, SubtitleSize = 46, HeaderSize = 40, LabelSize = 38, InputSize = 42,
                           HelperSize = 30, BadgeSize = 26, RequirementSize = 32, ButtonSize = 46, StepSize = 32, OptionSize = 38;

        // ---- translucency: the paper grain of the background shows through the panels ----
        public const float CardAlpha = 0.62f, PanelAlpha = 0.85f;

        public static Sprite Round => IconFactory.Pill;    // fully rounded; PillCorners adapts the radius to each object

        public class Chrome
        {
            public Transform canvas, card;
            public Button back, cont;
            public ContinueButtonGlowAnimator anim;
            public TextMeshProUGUI feedback, title, subtitle;
            /// <summary>"Leave sign-up?": the X at the top right, its pop-up and the script that asks.</summary>
            public OnboardingLeaveGuard leave;
        }

        // =====================================================================================
        //  Chrome: background, panel, title, subtitle, progress bar, back button, Continue
        // =====================================================================================
        public static Chrome BuildChrome(int step, string subtitleText, string continueLabel = "Continue", bool showBack = true, string subtitleExisting = null)
        {
            var canvas = SceneBuilderCore.EnsureCanvas();
            var ct = canvas.transform;
            int copies = RemoveCopiesInsideHelpBubbles(ct);
            if (copies > 0) Debug.Log("[GlowUp] " + copies + " leftover object(s) of an old builder fault were removed from inside help bubbles (hidden copies of the screen; nothing visible is lost).");
            SceneBuilderCore.EnsureSceneServices();
            var c = new Chrome { canvas = ct };

            // old controllers: disabled (never deleted)
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
                if ((go.name.StartsWith("OnboardingStep") && go.name.EndsWith("Manager")) || go.name == "TermsAndConditionsManager")
                    RetireLegacy(go);

            var bg = Loose(ct, "DarkBackground");
            if (bg != null) { bg.SetParent(ct, false); bg.SetAsFirstSibling(); Stretch(bg, 0, 0, 0, 0); }

            // ---- the panel: equal margins on all four sides ----
            Transform card = null;
            foreach (var n in new[] { "OnboardingCard", "OnboardingStep2Card", "OnboardingStep3Card", "PTCCard" }) { card = Loose(ct, n); if (card != null) break; }
            if (card == null) { var g = new GameObject("OnboardingCard", typeof(RectTransform), typeof(Image)); g.transform.SetParent(ct, false); card = g.transform; }
            var crt = (RectTransform)card;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = new Vector2(CardMargin, CardMargin); crt.offsetMax = new Vector2(-CardMargin, -CardMargin);
            foreach (var lg in card.GetComponents<LayoutGroup>()) lg.enabled = false;
            var cardImg = SceneBuilderCore.Ensure<Image>(card.gameObject);
            StyleRounded(cardImg); SetAlpha(cardImg, CardAlpha);
            SceneBuilderCore.Themed(card.gameObject, ThemeRole.Surface);
            EnsureOutline(card, "PanelOutline", ThemeRole.Gold);                // the thin gold frame of every card
            card.SetSiblingIndex(bg != null ? 1 : 0);
            c.card = card;

            // ---- title (the logo) ----
            Transform titleT = Loose(ct, "TitleText");
            if (titleT == null) titleT = NewText(card, "TitleText", "Glow Up").transform;
            c.title = titleT.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(c.title, "Glow Up", TitleSize, TextAlignmentOptions.Center);
            Top(titleT, card, 46, 136);
            SceneBuilderCore.Themed(c.title.gameObject, ThemeRole.Gold);        // the logo is the one large gold text

            // ---- subtitle (the page name) ----
            Transform sub = subtitleExisting != null ? Loose(ct, subtitleExisting) : null;
            if (sub == null) sub = Loose(ct, "SectionHeaderText");
            if (sub == null) sub = card.Find("SubtitleText");
            if (sub == null) sub = NewText(card, "SubtitleText", subtitleText).transform;
            c.subtitle = sub.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(c.subtitle, subtitleText, SubtitleSize, TextAlignmentOptions.Center);
            Top(sub, card, 186, 64);
            SceneBuilderCore.Themed(c.subtitle.gameObject, ThemeRole.MainText);

            // ---- progress bar ----
            Transform pbT = Loose(ct, "ProgressBar");
            if (pbT == null) { var g = new GameObject("ProgressBar", typeof(RectTransform)); g.transform.SetParent(card, false); pbT = g.transform; }
            Top(pbT, card, 274, 16);
            var hlg = SceneBuilderCore.Ensure<HorizontalLayoutGroup>(pbT.gameObject);
            hlg.enabled = true; hlg.spacing = 12; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = hlg.childControlHeight = true; hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
            var segs = new List<Image>();
            for (int i = 1; i <= 5; i++)
            {
                Transform bar = pbT.Find("Bar" + i);
                if (bar == null) { var g = new GameObject("Bar" + i, typeof(RectTransform), typeof(Image)); g.transform.SetParent(pbT, false); bar = g.transform; }
                bar.SetSiblingIndex(i - 1);                         // Bar1 .. Bar5 from left to right
                var img = SceneBuilderCore.Ensure<Image>(bar.gameObject); StyleRounded(img); segs.Add(img);
            }
            Transform stepT = Loose(ct, "StepText");
            if (stepT == null) stepT = NewText(card, "StepText", "").transform;
            var stepText = stepT.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(stepText, "Step " + step + " of 5", StepSize, TextAlignmentOptions.Center);
            Top(stepT, card, 300, 46);
            SceneBuilderCore.Themed(stepText.gameObject, ThemeRole.SecondaryText);
            var pbar = SceneBuilderCore.Ensure<OnboardingProgressBar>(pbT.gameObject);
            SceneBuilderCore.SetField(pbar, "stepLabel", stepText);
            SetArray(pbar, "segments", segs.ToArray());
            SceneBuilderCore.SetField(pbar, "currentStep", step);

            // ---- back button: inside the panel, top-left ----
            Transform backT = Loose(ct, "BackButton");
            if (backT == null && showBack)
            {
                var nb = SceneBuilderCore.MakeButton(card, "BackButton", "", 30, new Vector2(0, 1), Vector2.zero, new Vector2(92, 92), Color.white);
                var lbl = nb.transform.Find("Label"); if (lbl != null) Object.DestroyImmediate(lbl.gameObject);
                backT = nb.transform;
            }
            if (backT != null)
            {
                c.back = SceneBuilderCore.Ensure<Button>(backT.gameObject);
                ClearClicks(c.back);
                backT.SetParent(card, false);
                var brt = (RectTransform)backT; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
                brt.anchoredPosition = new Vector2(28, -28); brt.sizeDelta = new Vector2(92, 92);
                var bimg = SceneBuilderCore.Ensure<Image>(backT.gameObject);
                bimg.sprite = IconFactory.Back; bimg.preserveAspect = true; bimg.type = Image.Type.Simple;
                c.back.targetGraphic = bimg;
                SceneBuilderCore.Themed(bimg.gameObject, ThemeRole.MainText);
                foreach (var t in backT.GetComponentsInChildren<TextMeshProUGUI>(true)) t.text = "";   // the arrow is the icon
                backT.gameObject.SetActive(showBack);
                backT.SetAsLastSibling();
            }

            // ---- message line + Continue: anchored to the panel bottom ----
            Transform fbT = Loose(ct, "ErrorFeedbackText");
            if (fbT == null) fbT = NewText(card, "ErrorFeedbackText", "").transform;
            c.feedback = fbT.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(c.feedback, "", HelperSize, TextAlignmentOptions.Center);
            c.feedback.text = "";
            BottomRow(fbT, card, 200, 52);
            SceneBuilderCore.Themed(c.feedback.gameObject, ThemeRole.Warning);

            c.cont = BuildContinue(card, ct, continueLabel, out c.anim);

            var unused = card.Find("ContentArea");      // empty helper object from an earlier layout
            if (unused != null && unused.childCount == 0) Object.DestroyImmediate(unused.gameObject);

            c.leave = AddLeaveGuard(c);
            return c;
        }

        /// <summary>
        /// "Leave sign-up?" on every sign-up page (the user's rule of 12 October 2026): the X at the top right of the card
        /// (LeaveButton), the pop-up that asks (LeaveSignUpPanel: switched off in the Hierarchy, tick it active to see and
        /// style it) and the root object OnboardingLeaveGuard, whose Inspector holds the wording and survives rebuilds.
        /// The X and the pop-up are generated objects: they are rebuilt on every run.
        /// </summary>
        public static OnboardingLeaveGuard AddLeaveGuard(Chrome c)
        {
            foreach (string generated in new[] { "LeaveSignUpPanel", "LeaveButton" })
            {
                Transform old = Loose(c.canvas, generated);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // the X mirrors the Back arrow: same size, same distance from the corner
            Button leave = AppKit.IconButton(c.card, "LeaveButton", IconFactory.Close, 92, 48, ThemeRole.MainText);
            AppKit.At(leave.transform, AppKit.TR, new Vector2(-28, -28), new Vector2(92, 92));

            AppKit.AppPopup popup = AppKit.Overlay(new AppKit.AppScreen { canvas = c.canvas }, "LeaveSignUpPanel", "Leave sign-up?");
            AppKit.At(popup.box, AppKit.MC, new Vector2(0, 60), new Vector2(900, 520));
            TextMeshProUGUI message = AppKit.Label(popup.box, "MessageText",
                "You are about to leave the sign-up. What you typed and selected will be discarded, and you go back to the log-in page.",
                32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            AppKit.Fill(message.transform, 48, 190, 48, 130);
            AppKit.ShrinkWrapped(message, 26);
            Button stay = AppKit.PillButton(popup.box, "StayButton", "Stay", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            AppKit.BottomStrip(stay.transform, 44, 108, 48, 462);
            Button confirmLeave = AppKit.PillButton(popup.box, "LeaveConfirmButton", "Leave", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            AppKit.BottomStrip(confirmLeave.transform, 44, 108, 462, 48);
            // ConfirmDialog paints this button itself (the warning colour: leaving discards what was typed)
            Object.DestroyImmediate(confirmLeave.GetComponent<ThemedGraphic>());
            Object.DestroyImmediate(AppKit.LabelOf(confirmLeave).GetComponent<ThemedGraphic>());

            GameObject holder = SceneBuilderCore.EnsureRoot("OnboardingLeaveGuard");
            var dialog = SceneBuilderCore.Ensure<ConfirmDialog>(holder);
            SceneBuilderCore.SetField(dialog, "panel", popup.panel); SceneBuilderCore.SetField(dialog, "titleText", popup.title);
            SceneBuilderCore.SetField(dialog, "messageText", message);
            SceneBuilderCore.SetField(dialog, "confirmButton", confirmLeave); SceneBuilderCore.SetField(dialog, "confirmLabel", AppKit.LabelOf(confirmLeave));
            SceneBuilderCore.SetField(dialog, "cancelButton", stay); SceneBuilderCore.SetField(dialog, "cancelLabel", AppKit.LabelOf(stay));
            SceneBuilderCore.SetField(dialog, "defaultCancelText", "Stay");

            var guard = SceneBuilderCore.Ensure<OnboardingLeaveGuard>(holder);
            SceneBuilderCore.SetField(guard, "confirm", dialog);
            SceneBuilderCore.SetField(guard, "leaveButton", leave);
            SceneBuilderCore.SetField(guard, "backButton", c.back);
            return guard;
        }

        static Button BuildContinue(Transform card, Transform canvas, string label, out ContinueButtonGlowAnimator anim)
        {
            Transform t = Loose(canvas, "ContinueButton");
            Button b;
            if (t == null) b = SceneBuilderCore.MakeButton(card, "ContinueButton", label, ButtonSize, new Vector2(0.5f, 0), Vector2.zero, new Vector2(InnerWidth, ButtonHeight), Color.gray);
            else b = SceneBuilderCore.Ensure<Button>(t.gameObject);
            ClearClicks(b);
            BottomRow(b.transform, card, 50, ButtonHeight);
            b.transition = Selectable.Transition.None;
            var img = SceneBuilderCore.Ensure<Image>(b.gameObject); StyleRounded(img); b.targetGraphic = img;

            // soft halo around the button (hollow in the middle, so it never covers the button)
            Transform glowT = b.transform.Find("Glow");
            if (glowT == null) { var g = new GameObject("Glow", typeof(RectTransform), typeof(Image)); g.transform.SetParent(b.transform, false); glowT = g.transform; }
            glowT.SetAsFirstSibling();
            var glow = SceneBuilderCore.Ensure<Image>(glowT.gameObject);
            glow.sprite = IconFactory.PillGlow; glow.type = Image.Type.Sliced; glow.raycastTarget = false; glow.color = Color.clear;
            float halo = IconFactory.PillGlowMargin * (ButtonHeight * 0.5f) / PillCorners.SpriteRadius;
            Stretch(glowT, -halo, -halo, -halo, -halo);
            var gpc = SceneBuilderCore.Ensure<PillCorners>(glowT.gameObject);
            SceneBuilderCore.SetField(gpc, "spriteMargin", IconFactory.PillGlowMargin);
            SceneBuilderCore.SetField(gpc, "maxRadius", 200f);

            // label
            TextMeshProUGUI lbl = null;
            foreach (var l in b.GetComponentsInChildren<TextMeshProUGUI>(true)) { lbl = l; break; }
            if (lbl == null) lbl = NewText(b.transform, "Label", label);
            SetText(lbl, label, ButtonSize, TextAlignmentOptions.Center);
            lbl.enableWordWrapping = false;
            Stretch(lbl.transform, 0, 0, 0, 0);

            // arrow that slides forward
            Transform arrowT = b.transform.Find("Arrow");
            if (arrowT == null) { var a = new GameObject("Arrow", typeof(RectTransform), typeof(Image)); a.transform.SetParent(b.transform, false); arrowT = a.transform; }
            var arrow = SceneBuilderCore.Ensure<Image>(arrowT.gameObject);
            arrow.sprite = IconFactory.Arrow; arrow.type = Image.Type.Simple; arrow.preserveAspect = true; arrow.raycastTarget = false;
            var art = (RectTransform)arrowT; art.anchorMin = art.anchorMax = new Vector2(1, 0.5f); art.pivot = new Vector2(1, 0.5f);
            art.anchoredPosition = new Vector2(-64, 0); art.sizeDelta = new Vector2(56, 56);
            arrowT.SetAsLastSibling();

            anim = SceneBuilderCore.Ensure<ContinueButtonGlowAnimator>(b.gameObject);
            SceneBuilderCore.SetField(anim, "buttonBackground", img);
            SceneBuilderCore.SetField(anim, "glowImage", glow);
            SceneBuilderCore.SetField(anim, "arrow", art);
            SceneBuilderCore.SetField(anim, "label", lbl);
            return b;
        }

        // =====================================================================================
        //  Legacy handling
        // =====================================================================================
        /// <summary>Disables (does not delete) old global-namespace scripts on an object.</summary>
        public static void RetireLegacy(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && string.IsNullOrEmpty(mb.GetType().Namespace)) mb.enabled = false;
        }

        public static void ClearClicks(Button b)
        {
            if (b == null) return;
            for (int i = b.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(b.onClick, i);
        }

        // =====================================================================================
        //  Finders and layout
        // =====================================================================================
        /// <summary>
        /// A help bubble (the text that opens under an "i" badge) only ever holds its own text and outline. An old fault
        /// of the builders took a switched-off bubble for the scene's canvas and built a whole copy of the screen inside it,
        /// run after run. Those hidden copies are taken out here, BEFORE anything is looked up by name (a look-up by name
        /// would otherwise find an object of a copy first). Returns how many objects were removed.
        /// </summary>
        public static int RemoveCopiesInsideHelpBubbles(Transform canvas)
        {
            var doomed = new List<GameObject>();
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "HelpBubble") continue;
                foreach (Transform child in t)
                    if (child.name != "HelpText" && child.name != "Outline") doomed.Add(child.gameObject);
            }
            int removed = 0;
            foreach (var go in doomed) if (go != null) { removed += go.GetComponentsInChildren<Transform>(true).Length; Object.DestroyImmediate(go); }
            return removed;
        }

        /// <summary>Find by name anywhere under root, ignoring leading/trailing spaces (one existing object is named 'LastNameInputField ').</summary>
        public static Transform Loose(Transform root, string name)
        {
            if (root == null || name == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.Trim() == name) return t;
            return null;
        }

        public static void Stretch(Transform t, float l, float b, float r, float tp)
        {
            var rt = (RectTransform)t; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -tp);
        }

        /// <summary>Row anchored to the panel top, stretched across with PadX on both sides.</summary>
        public static void Top(Transform t, Transform parent, float y, float h)
        {
            if (t.parent != parent) t.SetParent(parent, false);
            var rt = (RectTransform)t; rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -y); rt.sizeDelta = new Vector2(-2 * PadX, h);
        }

        /// <summary>Fills the space between `top` units from the panel top and `bottom` units from the panel bottom (fits any screen height).</summary>
        public static void TopBottom(Transform t, Transform parent, float top, float bottom)
        {
            if (t.parent != parent) t.SetParent(parent, false);
            var rt = (RectTransform)t; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(PadX, bottom); rt.offsetMax = new Vector2(-PadX, -top);
        }

        /// <summary>Row anchored to the panel bottom, stretched across with PadX on both sides.</summary>
        public static void BottomRow(Transform t, Transform parent, float y, float h)
        {
            if (t.parent != parent) t.SetParent(parent, false);
            var rt = (RectTransform)t; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(-2 * PadX, h);
        }

        public static void SetAlpha(Graphic g, float a) { if (g == null) return; var c = g.color; c.a = a; g.color = c; }

        public static void StyleRounded(Image img)
        {
            if (img == null) return;
            img.sprite = Round; img.type = Image.Type.Sliced;
            SceneBuilderCore.Ensure<PillCorners>(img.gameObject);
        }

        /// <summary>A thin rounded outline on a panel's edge, so the paper panel separates crisply from the paper behind it.</summary>
        public static Image EnsureOutline(Transform target, string name, ThemeRole role, float maxRadius = 80f)
        {
            Transform t = target.Find(name);
            if (t == null) { var g = new GameObject(name, typeof(RectTransform), typeof(Image)); g.transform.SetParent(target, false); t = g.transform; }
            t.SetAsFirstSibling();
            Stretch(t, 0, 0, 0, 0);
            var img = SceneBuilderCore.Ensure<Image>(t.gameObject);
            img.sprite = IconFactory.PillOutline; img.type = Image.Type.Sliced; img.raycastTarget = false; SetAlpha(img, 1f);
            var pc = SceneBuilderCore.Ensure<PillCorners>(t.gameObject);
            SceneBuilderCore.SetField(pc, "maxRadius", maxRadius);
            SceneBuilderCore.Themed(t.gameObject, role);
            return img;
        }

        /// <summary>Limits how round a large shape gets (cards 80, inner panels ~56, small rows less).</summary>
        public static void SetRadius(Image img, float maxRadius)
        {
            if (img == null) return;
            var pc = SceneBuilderCore.Ensure<PillCorners>(img.gameObject);
            SceneBuilderCore.SetField(pc, "maxRadius", maxRadius);
        }

        // =====================================================================================
        //  Text
        // =====================================================================================
        public static TextMeshProUGUI NewText(Transform parent, string name, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.raycastTarget = false;
            return tmp;
        }

        public static void SetText(TextMeshProUGUI tmp, string text, float size, TextAlignmentOptions align)
        {
            if (tmp == null) return;
            if (text != null) tmp.text = text;
            tmp.enableAutoSizing = false; tmp.fontSize = size; tmp.alignment = align; tmp.enableWordWrapping = true;
            tmp.fontStyle = FontStyles.Normal;      // weight comes from the font role (see ThemeProfile)
        }

        /// <summary>
        /// A text with a scene-unique name: reuses `existing` (or the object with that name anywhere in the canvas),
        /// otherwise creates it under `parent`. The wording of an object that already exists is only replaced when `existing` is given.
        /// </summary>
        public static TextMeshProUGUI TextOn(Transform parent, string name, string text, float size, TextAlignmentOptions align, Transform existing = null)
        {
            Transform t = existing != null ? existing : Loose(parent.root, name);
            TextMeshProUGUI tmp = t != null ? t.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (tmp == null) tmp = NewText(parent, name, text);
            SetText(tmp, existing != null && !string.IsNullOrEmpty(text) ? text : null, size, align);
            return tmp;
        }

        /// <summary>A text that is a DIRECT child of `parent` (for generic names such as "Label"). Wording is only set when it is created.</summary>
        public static TextMeshProUGUI ChildText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
        {
            Transform t = parent.Find(name);
            TextMeshProUGUI tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null) tmp = NewText(parent, name, text);
            tmp.enableAutoSizing = false; tmp.fontSize = size; tmp.alignment = align; tmp.raycastTarget = false; tmp.fontStyle = FontStyles.Normal;
            return tmp;
        }

        public static void Role(Transform t, ThemeRole r) { if (t != null && t.GetComponent<Graphic>() != null) SceneBuilderCore.Themed(t.gameObject, r); }

        // =====================================================================================
        //  Serialized arrays
        // =====================================================================================
        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no field '{field}'"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetStrings(Object target, string field, string[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[SceneBuilder] {target.GetType().Name} has no field '{field}'"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).stringValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================================
        //  Panels and scroll views
        // =====================================================================================
        /// <summary>A rounded backdrop panel (reused when it exists). Its colour comes from the theme role.</summary>
        public static RectTransform EnsurePanel(Transform parent, string name, ThemeRole role, float alpha, Transform existing = null, float maxRadius = 56f)
        {
            Transform t = existing != null ? existing : Loose(parent, name);
            if (t == null) { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); t = go.transform; }
            var img = SceneBuilderCore.Ensure<Image>(t.gameObject);
            StyleRounded(img); SetRadius(img, maxRadius); SetAlpha(img, alpha);
            SceneBuilderCore.Themed(t.gameObject, role);
            foreach (var lg in t.GetComponents<LayoutGroup>()) lg.enabled = false;
            return (RectTransform)t;
        }

        /// <summary>
        /// A vertical scroll view named `name` under `parent` (created, or completed when it already exists):
        /// Viewport > Content with a vertical layout, plus a thin always-styled scrollbar on the right.
        /// </summary>
        public static RectTransform MakeScroll(Transform parent, string name, out RectTransform content)
        {
            Transform ex = Loose(parent, name);
            GameObject go;
            if (ex != null) go = ex.gameObject;
            else { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); Stretch(go.transform, 0, 0, 0, 0); }
            var hit = SceneBuilderCore.Ensure<Image>(go); hit.sprite = null; hit.color = new Color(0, 0, 0, 0.004f);   // lets a drag start anywhere in the list
            var scroll = SceneBuilderCore.Ensure<ScrollRect>(go);

            Transform vp = go.transform.Find("Viewport");
            if (vp == null) { var v = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); v.transform.SetParent(go.transform, false); vp = v.transform; }
            Stretch(vp, 0, 0, 0, 0);
            Transform ctn = vp.Find("Content");
            if (ctn == null) { var cg = new GameObject("Content", typeof(RectTransform)); cg.transform.SetParent(vp, false); ctn = cg.transform; }
            content = (RectTransform)ctn;
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, content.sizeDelta.y);
            var layout = SceneBuilderCore.Ensure<VerticalLayoutGroup>(content.gameObject);
            layout.spacing = 10; layout.padding = new RectOffset(16, 32, 12, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var fitter = SceneBuilderCore.Ensure<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)vp; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40; scroll.decelerationRate = 0.12f;

            EnsureScrollbar(scroll);
            return (RectTransform)go.transform;
        }

        /// <summary>A thin, round, dusty-pink vertical scrollbar on the right edge of a scroll view: it shows there is more to see.</summary>
        public static Scrollbar EnsureScrollbar(ScrollRect scroll)
        {
            if (scroll == null) return null;
            Transform sbT = scroll.transform.Find("VerticalScrollbar");
            if (sbT == null)
            {
                var sbGo = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
                sbGo.name = "VerticalScrollbar"; sbGo.transform.SetParent(scroll.transform, false); sbT = sbGo.transform;
            }
            sbT.SetAsLastSibling();
            var sb = sbT.GetComponent<Scrollbar>();
            if (sb == null) return null;
            sb.direction = Scrollbar.Direction.BottomToTop;
            var srt = (RectTransform)sbT; srt.anchorMin = new Vector2(1, 0); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(1, 0.5f);
            srt.anchoredPosition = new Vector2(-6, 0); srt.sizeDelta = new Vector2(12, -24);
            var track = sbT.GetComponent<Image>(); if (track != null) { StyleRounded(track); track.color = new Color(0, 0, 0, 0.08f); }
            if (sb.handleRect != null)
            {
                var handle = sb.handleRect.GetComponent<Image>();
                if (handle != null) { StyleRounded(handle); SetAlpha(handle, 1f); SceneBuilderCore.Themed(handle.gameObject, ThemeRole.Accent); }
            }
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 0;
            // out of sight while the list rests; it fades in while the user scrolls (ScrollbarAutoHide on the bar)
            SceneBuilderCore.Ensure<CanvasGroup>(sbT.gameObject);
            SceneBuilderCore.SetField(SceneBuilderCore.Ensure<GlowUp.Common.ScrollbarAutoHide>(sbT.gameObject), "scroll", scroll);
            return sb;
        }

        /// <summary>
        /// A small pill that hugs its text (badges, chips). The round background is a CHILD that the layout ignores:
        /// a round Image on the auto-sized object itself reports its sprite size as "preferred" and would inflate the pill.
        /// Returns the text; `pill` is the root object.
        /// </summary>
        public static TextMeshProUGUI AutoPill(Transform pill, string text, float fontSize, Color background, Color textColor, int padX = 22, int padY = 6)
        {
            // an older layout put the Image on the pill itself - take it off (generated object)
            var oldCorners = pill.GetComponent<PillCorners>(); if (oldCorners != null) Object.DestroyImmediate(oldCorners);
            var oldThemed = pill.GetComponent<ThemedGraphic>(); if (oldThemed != null) Object.DestroyImmediate(oldThemed);
            var oldImage = pill.GetComponent<Image>(); if (oldImage != null) Object.DestroyImmediate(oldImage);

            Transform bgT = pill.Find("Background");
            if (bgT == null) { var g = new GameObject("Background", typeof(RectTransform), typeof(Image)); g.transform.SetParent(pill, false); bgT = g.transform; }
            bgT.SetAsFirstSibling();
            Stretch(bgT, 0, 0, 0, 0);
            var bg = SceneBuilderCore.Ensure<Image>(bgT.gameObject); StyleRounded(bg); bg.color = background; bg.raycastTarget = false;
            SceneBuilderCore.Ensure<LayoutElement>(bgT.gameObject).ignoreLayout = true;

            var hl = SceneBuilderCore.Ensure<HorizontalLayoutGroup>(pill.gameObject);
            hl.padding = new RectOffset(padX, padX, padY, padY); hl.spacing = 8; hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            var fitter = SceneBuilderCore.Ensure<ContentSizeFitter>(pill.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var label = ChildText(pill, "BadgeText", text, fontSize, TextAlignmentOptions.Center);
            label.text = text; label.color = textColor; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
            label.transform.SetAsLastSibling();
            return label;
        }

        public static void Row(Transform t, Transform content, float preferredHeight)
        {
            if (t.parent != content) t.SetParent(content, false);
            var le = SceneBuilderCore.Ensure<LayoutElement>(t.gameObject);
            le.preferredHeight = preferredHeight; le.minHeight = preferredHeight; le.flexibleHeight = 0;
        }

        // =====================================================================================
        //  Labelled input rows
        // =====================================================================================
        public static TextMeshProUGUI LabelRow(Transform content, Transform existing, string name, string text)
        {
            Transform t = existing != null ? existing : content.Find(name);
            TextMeshProUGUI tmp = t != null ? t.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (tmp == null) tmp = NewText(content, name, text);
            SetText(tmp, text, LabelSize, TextAlignmentOptions.BottomLeft);
            tmp.margin = new Vector4(12, 0, 0, 4);
            Row(tmp.transform, content, LabelHeight);
            Role(tmp.transform, ThemeRole.MainText);
            return tmp;
        }

        public static TMP_InputField FieldRow(Transform content, Transform existing, string name, string placeholder, int limit = 64, bool alnum = false,
            TMP_InputField.ContentType type = TMP_InputField.ContentType.Standard, bool blockPaste = false)
        {
            TMP_InputField f = existing != null ? existing.GetComponent<TMP_InputField>() : null;
            if (f == null) f = SceneBuilderCore.MakeInput(content, name, placeholder, new Vector2(0.5f, 1), Vector2.zero, new Vector2(800, FieldHeight));
            Row(f.transform, content, FieldHeight);
            StyleInput(f, placeholder, ThemeRole.InputBackground);
            var b = SceneBuilderCore.Ensure<InputFieldBehaviour>(f.gameObject);
            SceneBuilderCore.SetField(b, "characterLimit", limit);
            SceneBuilderCore.SetField(b, "contentType", (int)type);
            SceneBuilderCore.SetField(b, "alphanumericOnly", alnum);
            SceneBuilderCore.SetField(b, "blockPaste", blockPaste);
            return f;
        }

        /// <summary>Round background, theme roles for text and placeholder, the placeholder wording, and centred text.</summary>
        public static void StyleInput(TMP_InputField f, string placeholder, ThemeRole background)
        {
            if (f == null) return;
            var img = SceneBuilderCore.Ensure<Image>(f.gameObject); StyleRounded(img); f.targetGraphic = img;
            SceneBuilderCore.Themed(f.gameObject, background);
            if (f.textComponent != null) Role(f.textComponent.transform, ThemeRole.InputText);
            if (f.placeholder != null)
            {
                Role(f.placeholder.transform, ThemeRole.InputPlaceholder);
                var pt = f.placeholder as TMP_Text;
                if (pt != null && placeholder != null) pt.text = placeholder;
            }
            NormalizeInput(f);
        }

        /// <summary>
        /// Puts the typed text and the placeholder exactly in the vertical middle of the field with even side
        /// padding (more on the right when there is an eye button), at one consistent size. Applied to EVERY
        /// input field of a scene by ThemeKit.FinishScene.
        /// </summary>
        public static void NormalizeInput(TMP_InputField f, float fontSize = InputSize)
        {
            if (f == null) return;
            bool hasEye = f.transform.Find("PasswordEyeButton") != null;
            RectTransform area = f.textViewport;
            if (area == null) { var a = Loose(f.transform, "Text Area"); if (a != null) area = (RectTransform)a; }
            if (area != null)
            {
                area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.pivot = new Vector2(0.5f, 0.5f);
                area.offsetMin = new Vector2(44, 6); area.offsetMax = new Vector2(hasEye ? -124 : -44, -6);
                f.textViewport = area;
            }
            FitText(f.textComponent, fontSize, FontStyles.Normal, false);
            FitText(f.placeholder as TMP_Text, fontSize, FontStyles.Italic, true);
            f.caretWidth = 3;

            // the field's own "Point Size" (it drives the text size in the Inspector)
            var so = new SerializedObject(f);
            var p = so.FindProperty("m_GlobalPointSize");
            if (p != null) { p.floatValue = fontSize; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(f);
        }

        static void FitText(TMP_Text t, float size, FontStyles style, bool shrinkToFit)
        {
            if (t == null) return;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            t.margin = Vector4.zero;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.enableAutoSizing = shrinkToFit;                     // long placeholders shrink instead of being cut
            if (shrinkToFit) { t.fontSizeMin = 26; t.fontSizeMax = size; t.overflowMode = TextOverflowModes.Ellipsis; }
            t.fontSize = size;
            EditorUtility.SetDirty(t);
        }

        /// <summary>The thin outline of an input / dropdown; it turns dusty pink while the field is active.</summary>
        public static void EnsureFocusBorder(GameObject fieldObject)
        {
            Transform bt = fieldObject.transform.Find("Border");
            if (bt == null) { var g = new GameObject("Border", typeof(RectTransform), typeof(Image)); g.transform.SetParent(fieldObject.transform, false); bt = g.transform; }
            bt.SetAsFirstSibling();
            Stretch(bt, 0, 0, 0, 0);
            var img = SceneBuilderCore.Ensure<Image>(bt.gameObject);
            img.sprite = IconFactory.PillOutline; img.type = Image.Type.Sliced; img.raycastTarget = false;
            SceneBuilderCore.Ensure<PillCorners>(bt.gameObject);
            var fb = SceneBuilderCore.Ensure<InputFocusBorder>(fieldObject);
            SceneBuilderCore.SetField(fb, "border", img);
        }

        /// <summary>Adds the eye toggle inside a password field (right end).</summary>
        public static void AddEye(TMP_InputField field)
        {
            if (field == null) return;
            var toggle = SceneBuilderCore.Ensure<PasswordFieldToggle>(field.gameObject);
            Transform et = field.transform.Find("PasswordEyeButton");
            Button eye = et != null ? SceneBuilderCore.Ensure<Button>(et.gameObject)
                                    : SceneBuilderCore.MakeButton(field.transform, "PasswordEyeButton", "", 30, new Vector2(1, 0.5f), Vector2.zero, new Vector2(96, 96), new Color(1, 1, 1, 0.004f));
            var old = eye.transform.Find("Label"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var rt = (RectTransform)eye.transform; rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-18, 0); rt.sizeDelta = new Vector2(96, 96);
            eye.transform.SetAsLastSibling();
            var icon = Icon(eye.transform, "EyeIcon", IconFactory.EyeClosed, 56);
            SceneBuilderCore.SetField(toggle, "eyeButton", eye);
            SceneBuilderCore.SetField(toggle, "eyeIconImage", icon);
            SceneBuilderCore.SetField(toggle, "eyeOpenIcon", IconFactory.EyeOpen);
            SceneBuilderCore.SetField(toggle, "eyeClosedIcon", IconFactory.EyeClosed);
            SceneBuilderCore.SetField(toggle, "maxLength", 64);
            SceneBuilderCore.Themed(icon.gameObject, ThemeRole.SecondaryText);
            NormalizeInput(field);
        }

        /// <summary>An icon Image that is a direct child of `parent`, centred, at `size`.</summary>
        public static Image Icon(Transform parent, string name, Sprite sprite, float size)
        {
            Transform ex = parent.Find(name);
            Image img;
            if (ex != null) img = SceneBuilderCore.Ensure<Image>(ex.gameObject);
            else { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); img = go.GetComponent<Image>(); }
            img.sprite = sprite; img.type = Image.Type.Simple; img.raycastTarget = false; img.preserveAspect = true;
            var rt = (RectTransform)img.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        /// <summary>Stops space characters and enforces the length limit (64 unless said otherwise) on an input; the message shows in `message`.</summary>
        public static void AddSpaceGuard(TMP_InputField f, TextMeshProUGUI message, string text, int maxLength = 64)
        {
            if (f == null) return;
            var g = SceneBuilderCore.Ensure<InputSpaceGuard>(f.gameObject);
            SceneBuilderCore.SetField(g, "messageText", message);
            SceneBuilderCore.SetField(g, "message", text);
            SceneBuilderCore.SetField(g, "maxLength", maxLength);
        }

        /// <summary>The longest email the sign-up pages take (a school or work address can be long).</summary>
        public const int EmailLimit = 100;

        /// <summary>
        /// Puts the email rule on an input (the EmailFieldVerifier script: well-known provider, or a domain checked online).
        /// With `content` given, the line the script writes in ("Checking that ... receives email") is placed right under
        /// the field as EmailCheckText. The line is switched off while it has nothing to say, so it leaves no gap:
        /// tick it active in the Hierarchy to see and restyle it. `host` = the object that carries the script when the field
        /// itself is switched off part of the time (the script waits and tries again in Update, so it needs an object that stays on).
        /// </summary>
        public static EmailFieldVerifier AddEmailCheck(TMP_InputField field, Transform content = null, GameObject host = null)
        {
            if (field == null) return null;
            var check = SceneBuilderCore.Ensure<EmailFieldVerifier>(host != null ? host : field.gameObject);
            if (content == null) return check;

            Transform existing = Loose(content, "EmailCheckText");
            TextMeshProUGUI line = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
            if (line == null) line = NewText(content, "EmailCheckText", "Checking that example.com receives email...");
            SetText(line, null, HelperSize, TextAlignmentOptions.TopLeft);
            line.margin = new Vector4(14, 0, 14, 0); line.overflowMode = TextOverflowModes.Overflow; line.raycastTarget = false;
            if (line.transform.parent != content) line.transform.SetParent(content, false);
            if (field.transform.parent == content)
            {
                line.transform.SetAsLastSibling();                                        // out of the way first, so the field's own place does not shift
                line.transform.SetSiblingIndex(field.transform.GetSiblingIndex() + 1);
            }
            var le = SceneBuilderCore.Ensure<LayoutElement>(line.gameObject);
            le.minHeight = HelperHeight - 8; le.preferredHeight = -1; le.flexibleHeight = 0;      // one line, or two when the message is long
            SceneBuilderCore.SetField(check, "statusText", line);
            line.gameObject.SetActive(false);
            return check;
        }

        /// <summary>
        /// Helper line under an input: a short hint on the left and a small pill badge on the right.
        /// Shown only to new users / users who seem to struggle (HintGroup); it takes no space while hidden.
        /// </summary>
        public static void HelperRow(Transform content, string name, string message, string badge, TMP_InputField field = null)
        {
            Transform holder = Loose(content, name);
            if (holder == null) { var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)); go.transform.SetParent(content, false); holder = go.transform; }
            var le = SceneBuilderCore.Ensure<LayoutElement>(holder.gameObject); le.preferredHeight = HelperHeight; le.minHeight = HelperHeight; le.flexibleHeight = 0;

            const float badgeWidth = 168;
            var label = ChildText(holder, name + "_Label", message, HelperSize, TextAlignmentOptions.MidlineLeft);
            label.text = message; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true; label.fontSizeMin = 22; label.fontSizeMax = HelperSize;
            var lrt = label.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(14, 0); lrt.offsetMax = new Vector2(-(badgeWidth + 18), 0);
            Role(label.transform, ThemeRole.SecondaryText);

            // badge = a small dusty-pink pill with dark text
            Transform pillT = holder.Find(name + "_BadgePill");
            if (pillT == null) { var g = new GameObject(name + "_BadgePill", typeof(RectTransform), typeof(Image)); g.transform.SetParent(holder, false); pillT = g.transform; }
            var pillImg = SceneBuilderCore.Ensure<Image>(pillT.gameObject); StyleRounded(pillImg); pillImg.raycastTarget = false; SetAlpha(pillImg, 1f);
            SceneBuilderCore.Themed(pillT.gameObject, ThemeRole.Accent);
            var prt = (RectTransform)pillT; prt.anchorMin = prt.anchorMax = new Vector2(1, 0.5f); prt.pivot = new Vector2(1, 0.5f);
            prt.anchoredPosition = new Vector2(-4, 0); prt.sizeDelta = new Vector2(badgeWidth, 40);

            Transform oldBadge = holder.Find(name + "_Badge");            // an earlier layout kept the badge text directly in the row
            if (oldBadge != null) oldBadge.SetParent(pillT, false);
            var pill = ChildText(pillT, name + "_Badge", badge, BadgeSize, TextAlignmentOptions.Center);
            pill.text = badge; pill.enableWordWrapping = false;
            Stretch(pill.transform, 0, 0, 0, 0);
            Role(pill.transform, ThemeRole.ButtonText);

            var ih = SceneBuilderCore.Ensure<InlineHelperText>(holder.gameObject);
            SceneBuilderCore.SetField(ih, "inputField", field);
            SceneBuilderCore.SetField(ih, "helperLabel", label); SceneBuilderCore.SetField(ih, "badgeLabel", pill);
            SceneBuilderCore.SetField(ih, "helperMessage", message); SceneBuilderCore.SetField(ih, "badgeMessage", badge);
            SceneBuilderCore.Ensure<HintGroup>(holder.gameObject);
        }

        /// <summary>
        /// The small round "i" at the right end of a field's label. Tapping it shows `message` in a bubble under it
        /// (HelpBadge script). The reminder line that used to sit under the field (`oldHelperRow`) is switched off, not deleted.
        /// </summary>
        public static HelpBadge InfoBadge(Transform content, TextMeshProUGUI label, string oldHelperRow, string message)
        {
            Transform old = string.IsNullOrEmpty(oldHelperRow) ? null : Loose(content, oldHelperRow);
            if (old != null) old.gameObject.SetActive(false);

            Transform badgeT = label.transform.Find("InfoBadge");
            if (badgeT == null) { var g = new GameObject("InfoBadge", typeof(RectTransform), typeof(Image), typeof(Button)); g.layer = label.gameObject.layer; g.transform.SetParent(label.transform, false); badgeT = g.transform; }
            var brt = (RectTransform)badgeT; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 0.5f);
            brt.anchoredPosition = new Vector2(-6, 0); brt.sizeDelta = new Vector2(54, 54);
            var disc = SceneBuilderCore.Ensure<Image>(badgeT.gameObject); StyleRounded(disc); disc.raycastTarget = true; SetAlpha(disc, 1f);
            SceneBuilderCore.Themed(badgeT.gameObject, ThemeRole.InputBackground);
            var button = SceneBuilderCore.Ensure<Button>(badgeT.gameObject); button.targetGraphic = disc;
            var icon = Icon(badgeT, "Icon", IconFactory.Info, 38);
            SceneBuilderCore.Themed(icon.gameObject, ThemeRole.AccentText);

            // the bubble: hangs under the badge, as wide as the form, and is drawn above the rows below it
            Transform bubbleT = badgeT.Find("HelpBubble");
            if (bubbleT != null) Object.DestroyImmediate(bubbleT.gameObject);
            var bubble = new GameObject("HelpBubble", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Canvas), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            bubble.layer = label.gameObject.layer; bubble.transform.SetParent(badgeT, false);
            var bubbleRt = (RectTransform)bubble.transform; bubbleRt.anchorMin = bubbleRt.anchorMax = new Vector2(1, 0); bubbleRt.pivot = new Vector2(1, 1);
            bubbleRt.anchoredPosition = new Vector2(6, -10); bubbleRt.sizeDelta = new Vector2(680, 96);
            var bubbleImage = bubble.GetComponent<Image>(); StyleRounded(bubbleImage); SetRadius(bubbleImage, 30f); bubbleImage.raycastTarget = false; SetAlpha(bubbleImage, 1f);
            SceneBuilderCore.Themed(bubble, ThemeRole.Background);
            var bubbleOutline = EnsureOutline(bubble.transform, "Outline", ThemeRole.Accent, 30f);
            if (bubbleOutline != null) SceneBuilderCore.Ensure<LayoutElement>(bubbleOutline.gameObject).ignoreLayout = true;
            var layout = bubble.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 18, 20); layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var fitter = bubble.GetComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = NewText(bubble.transform, "HelpText", message);
            SetText(text, message, HelperSize, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false;
            SceneBuilderCore.Themed(text.gameObject, ThemeRole.MainText);

            var badge = SceneBuilderCore.Ensure<HelpBadge>(badgeT.gameObject);
            SceneBuilderCore.SetField(badge, "badgeButton", button); SceneBuilderCore.SetField(badge, "bubble", bubble);
            SceneBuilderCore.SetField(badge, "bubbleText", text); SceneBuilderCore.SetField(badge, "bubbleGroup", bubble.GetComponent<CanvasGroup>());
            SceneBuilderCore.SetField(badge, "message", message);
            bubble.SetActive(false);                                   // tick it active in the Hierarchy to see and restyle it
            return badge;
        }

        /// <summary>A checklist of requirement rows ("1 uppercase letter" ...). Returns each row's RequirementIndicatorItem.</summary>
        public static RequirementIndicatorItem[] Checklist(Transform content, string namePrefix, string[] items)
        {
            var result = new RequirementIndicatorItem[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                string rowName = namePrefix + "_Req" + (i + 1);
                Transform row = Loose(content, rowName);
                bool created = row == null;
                if (created) { var go = new GameObject(rowName, typeof(RectTransform)); go.transform.SetParent(content, false); row = go.transform; }
                var le = SceneBuilderCore.Ensure<LayoutElement>(row.gameObject); le.preferredHeight = RequirementHeight; le.minHeight = RequirementHeight; le.flexibleHeight = 0;
                var item = SceneBuilderCore.Ensure<RequirementIndicatorItem>(row.gameObject);

                var icon = Icon(row, "CheckIcon", IconFactory.Check, 36);
                var irt = icon.rectTransform; irt.anchorMin = irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f); irt.anchoredPosition = new Vector2(36, 0);
                var lbl = ChildText(row, rowName + "_Label", items[i], RequirementSize, TextAlignmentOptions.MidlineLeft);
                lbl.enableWordWrapping = false;
                var lrt = lbl.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = new Vector2(70, 0); lrt.offsetMax = Vector2.zero;

                SceneBuilderCore.SetField(item, "checkIcon", icon);
                SceneBuilderCore.SetField(item, "label", lbl);
                if (created) SceneBuilderCore.SetField(item, "requirementText", items[i]);
                result[i] = item;
            }
            return result;
        }

        // =====================================================================================
        //  Dropdowns
        // =====================================================================================
        public static TMP_Dropdown MakeDropdown(Transform parent, string name, Vector2 pos, Vector2 size, float fontSize = InputSize)
        {
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            go.name = name; go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var d = go.GetComponent<TMP_Dropdown>();
            StyleDropdown(d, fontSize);
            return d;
        }

        /// <summary>Scales Unity's default dropdown (made for tiny desktop UI) up to phone size and themes it, including the list that opens.</summary>
        public static void StyleDropdown(TMP_Dropdown d, float font = InputSize)
        {
            if (d == null) return;
            var img = d.GetComponent<Image>();
            if (img != null) { StyleRounded(img); SetAlpha(img, 1f); SceneBuilderCore.Themed(img.gameObject, ThemeRole.InputBackground); }

            TMP_Text caption = d.captionText;
            if (caption != null)
            {
                var rt = caption.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(44, 0); rt.offsetMax = new Vector2(-100, 0);
                caption.enableAutoSizing = false; caption.fontSize = font; caption.alignment = TextAlignmentOptions.MidlineLeft; caption.enableWordWrapping = false;
                SceneBuilderCore.Themed(caption.gameObject, ThemeRole.InputText);
            }

            Transform arrow = d.transform.Find("Arrow");
            if (arrow != null)
            {
                var ai = arrow.GetComponent<Image>();
                if (ai != null) { ai.sprite = IconFactory.ChevronDown; ai.type = Image.Type.Simple; ai.preserveAspect = true; ai.raycastTarget = false; SceneBuilderCore.Themed(ai.gameObject, ThemeRole.SecondaryText); }
                var art = (RectTransform)arrow; art.anchorMin = art.anchorMax = new Vector2(1, 0.5f); art.pivot = new Vector2(0.5f, 0.5f);
                art.anchoredPosition = new Vector2(-56, 0); art.sizeDelta = new Vector2(44, 44);
            }

            RectTransform template = d.template;
            if (template == null) return;
            template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0); template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = new Vector2(0, -8); template.sizeDelta = new Vector2(0, 560);
            var ti = template.GetComponent<Image>();
            if (ti != null) { StyleRounded(ti); SetRadius(ti, 36f); SetAlpha(ti, 1f); SceneBuilderCore.Themed(ti.gameObject, ThemeRole.Surface); }
            var sr = template.GetComponent<ScrollRect>();
            if (sr != null) { sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide; sr.scrollSensitivity = 40; }

            Transform viewport = template.Find("Viewport");
            if (viewport == null) return;
            Stretch(viewport, 10, 14, 26, 14);
            Transform content = viewport.Find("Content");
            if (content == null) return;
            var crt = (RectTransform)content; crt.sizeDelta = new Vector2(0, 104);
            Transform item = content.Find("Item");
            if (item == null) return;
            ((RectTransform)item).sizeDelta = new Vector2(0, 96);

            Transform ib = item.Find("Item Background");
            if (ib != null)
            {
                var ibi = ib.GetComponent<Image>();
                if (ibi != null) { StyleRounded(ibi); SetAlpha(ibi, 1f); SceneBuilderCore.Themed(ibi.gameObject, ThemeRole.InputBackground); }
            }
            Transform ic = item.Find("Item Checkmark");
            if (ic != null)
            {
                var ici = ic.GetComponent<Image>();
                if (ici != null) { ici.sprite = IconFactory.Check; ici.type = Image.Type.Simple; ici.preserveAspect = true; SceneBuilderCore.Themed(ici.gameObject, ThemeRole.Accent); }
                var irt = (RectTransform)ic; irt.anchorMin = irt.anchorMax = new Vector2(0, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = new Vector2(46, 0); irt.sizeDelta = new Vector2(40, 40);
            }
            Transform il = item.Find("Item Label");
            if (il != null)
            {
                var lrt = (RectTransform)il; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = new Vector2(86, 0); lrt.offsetMax = new Vector2(-16, 0);
                var ilt = il.GetComponent<TMP_Text>();
                if (ilt != null)
                {
                    ilt.enableAutoSizing = false; ilt.fontSize = font; ilt.alignment = TextAlignmentOptions.MidlineLeft; ilt.enableWordWrapping = false;
                    SceneBuilderCore.Themed(ilt.gameObject, ThemeRole.MainText);
                }
            }
        }

        // =====================================================================================
        //  Final pass
        // =====================================================================================
        /// <summary>Run at the end of every builder: canvas scaler, paper background, input text, round shapes, fonts, colours.</summary>
        public static void FinishScene() { ThemeKit.FinishScene(); }
    }
}
