using System.IO;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// The parts every scene gets, added by the shared final pass (ThemeKit.FinishScene):
    ///
    ///  - UiServices (top of the Hierarchy): tap sounds, shake-to-navigate and the screen change with its
    ///    skeleton loading sheet. Its child TransitionCanvas is the sheet; it is INACTIVE in the Hierarchy
    ///    and only shows during a change of screen (tick it active to see and style it).
    ///  - EdgeLight (inside the scene's panel): the light that runs round the panel's outline.
    ///  - On Onboarding Steps 1 to 5: the tile animation (lift, pixel sweep, glowing outline) on every button.
    ///
    /// AddPressSweep is also used by the scene builders for their tiles.
    /// </summary>
    public static class UiEffectsKit
    {
        public const string ServicesName = "UiServices";
        const string AudioFolder = "Assets/_Project/Audio/";
        static readonly string[] SoundFiles = { "UiTap_1_SoftTap", "UiTap_2_Pop", "UiTap_3_Click", "UiTap_4_Bubble", "UiTap_5_Chime" };

        /// <summary>True for the canvas of the screen-change sheet (scene builders must not mistake it for the scene's own canvas).</summary>
        public static bool IsServiceCanvas(Canvas canvas) => canvas != null && canvas.transform.root.name == ServicesName;

        public static void FinishScene()
        {
            RemoveLeftovers();
            EnsureUiServices();
            EnsureEdgeLight();
            string scene = SceneManager.GetActiveScene().name;
            if (scene.StartsWith("OnboardingStep")) AddPressSweepToEveryButton();
        }

        // =====================================================================================
        //  Leftovers of scripts that were removed from the project (October 2026 clean-up)
        // =====================================================================================
        /// <summary>
        /// Takes the "Missing Script" entries off the scene's objects (the old controllers that the new ones replaced).
        /// An object that is left with nothing on it and nothing under it (an old "...Manager" holder) is removed too.
        /// </summary>
        public static void RemoveLeftovers()
        {
            var all = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) all.Add(t.gameObject);
            int scripts = 0, objects = 0;
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                if (removed == 0) continue;
                scripts += removed;
                if (go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1) { Object.DestroyImmediate(go); objects++; }
            }
            if (scripts > 0) Debug.Log($"[GlowUp] {SceneManager.GetActiveScene().name}: {scripts} leftover script entr{(scripts == 1 ? "y" : "ies")} removed, {objects} empty old object(s) removed.");
        }

        // =====================================================================================
        //  UiServices: sounds, shake, screen change
        // =====================================================================================
        public static GameObject EnsureUiServices()
        {
            GameObject root = SceneBuilderCore.EnsureRoot(ServicesName);
            var audio = SceneBuilderCore.Ensure<AudioSource>(root);
            audio.playOnAwake = false; audio.loop = false;

            var sound = SceneBuilderCore.Ensure<UiSoundPlayer>(root);
            var serialized = new SerializedObject(sound);
            SerializedProperty clips = serialized.FindProperty("tapSounds");
            if (clips.arraySize < SoundFiles.Length) clips.arraySize = SoundFiles.Length;
            for (int i = 0; i < SoundFiles.Length; i++)                         // a slot the user filled with their own clip is kept
                if (clips.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    clips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + SoundFiles[i] + ".wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // finger signs (1 to 5 fingers held up to the selfie camera), brought back 19 November 2026. The navigator does nothing
            // until the user switches finger signs on in Settings > Sounds & Motion.
            var signs = SceneBuilderCore.Ensure<FingerSignNavigator>(root);
            SceneBuilderCore.SetField(signs, "indicator", BuildFingerSignIndicator(root));

            var transition = SceneBuilderCore.Ensure<ScreenTransition>(root);

            // ---- the sheet that covers the screen during a change (rebuilt each time) ----
            Transform old = root.transform.Find("TransitionCanvas");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var canvasObject = new GameObject("TransitionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;       // above every screen and pop-up
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0f;

            var sheet = Child(canvasObject.transform, "Sheet", typeof(Image), typeof(CanvasGroup));
            Stretch(sheet.transform, 0, 0, 0, 0);
            var sheetImage = sheet.GetComponent<Image>(); sheetImage.sprite = IconFactory.Square; sheetImage.raycastTarget = true;    // taps do not reach the screen underneath
            SceneBuilderCore.Themed(sheet, ThemeRole.Background);

            // a soft shadow just outside each side of the sheet, so its edge is never a hard line while it glides
            EdgeShade(sheet.transform, "EdgeShadeLeft", true);
            EdgeShade(sheet.transform, "EdgeShadeRight", false);

            // the sparkle in the middle of the sheet: it turns while the next screen loads
            var mark = Child(sheet.transform, "Mark", typeof(CanvasGroup));
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = markRect.anchorMax = markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.anchoredPosition = Vector2.zero; markRect.sizeDelta = new Vector2(150, 150);
            var markGroup = mark.GetComponent<CanvasGroup>(); markGroup.alpha = 0f; markGroup.blocksRaycasts = false; markGroup.interactable = false;
            var markHalo = Child(mark.transform, "Halo", typeof(Image));
            Stretch(markHalo.transform, -40, -40, -40, -40);
            var haloImage = markHalo.GetComponent<Image>(); haloImage.sprite = IconFactory.SoftDisc; haloImage.raycastTarget = false; haloImage.color = new Color(1, 1, 1, 0.28f);
            SceneBuilderCore.Themed(markHalo, ThemeRole.Accent);
            var markIcon = Child(mark.transform, "Sparkle", typeof(Image));
            Stretch(markIcon.transform, 0, 0, 0, 0);
            var iconImage = markIcon.GetComponent<Image>(); iconImage.sprite = IconFactory.Star4; iconImage.preserveAspect = true; iconImage.raycastTarget = false;
            SceneBuilderCore.Themed(markIcon, ThemeRole.Accent);

            // skeleton: the outline of a typical screen in soft shapes
            var skeleton = Child(sheet.transform, "Skeleton", typeof(CanvasGroup), typeof(SkeletonShimmer));
            Stretch(skeleton.transform, 40, 40, 40, 40);
            Block(skeleton.transform, "Panel", new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero, 80f, ThemeRole.Surface, 0.9f, false);
            Block(skeleton.transform, "Title", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-230, -118), new Vector2(230, -50), 40f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "Line1", new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -236), new Vector2(-420, -190), 30f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "TileLeft", new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(60, -640), new Vector2(-14, -270), 28f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "TileRight", new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(14, -640), new Vector2(-60, -270), 28f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "Line2", new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -730), new Vector2(-520, -684), 30f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "Row1", new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -930), new Vector2(-60, -770), 48f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "Row2", new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -1120), new Vector2(-60, -960), 48f, ThemeRole.SurfaceAlt);
            Block(skeleton.transform, "BottomBar", new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 28), new Vector2(-28, 204), 88f, ThemeRole.SurfaceAlt);
            // the panel itself does not shimmer: only the shapes on it do
            var shimmer = skeleton.GetComponent<SkeletonShimmer>();
            var shapes = new System.Collections.Generic.List<Object>();
            foreach (Transform t in skeleton.transform) if (t.name != "Panel") shapes.Add(t.GetComponent<Image>());
            OnboardingKit.SetArray(shimmer, "shapes", shapes.ToArray());
            // the shapes shine the way the website's do (and the way the bars of TextSkeleton do on the screen that follows)
            Material shine = SkeletonMaterial();
            SceneBuilderCore.SetField(shimmer, "shine", shine);
            if (shine != null) foreach (Object shape in shapes) ((Image)shape).material = shine;

            SceneBuilderCore.SetField(transition, "overlayCanvas", canvas);
            SceneBuilderCore.SetField(transition, "sheet", (RectTransform)sheet.transform);
            SceneBuilderCore.SetField(transition, "sheetGroup", sheet.GetComponent<CanvasGroup>());
            SceneBuilderCore.SetField(transition, "skeletonGroup", skeleton.GetComponent<CanvasGroup>());
            SceneBuilderCore.SetField(transition, "mark", markRect);
            SceneBuilderCore.SetField(transition, "markGroup", markGroup);
            canvasObject.SetActive(false);
            return root;
        }

        /// <summary>
        /// The small sign at the top of the screen that shows which finger sign the camera sees ("3  Hold for Recent Scans").
        /// It is rebuilt each time and left switched off: the app switches it on while a sign is seen.
        /// </summary>
        static FingerSignIndicator BuildFingerSignIndicator(GameObject root)
        {
            Transform old = root.transform.Find("FingerSignCanvas");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var canvasObject = new GameObject("FingerSignCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));     // no raycaster: it never takes a tap
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 450;       // above the screens and pop-ups, under the screen change
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0f;

            var sign = Child(canvasObject.transform, "Sign", typeof(Image), typeof(CanvasGroup));
            var signRect = (RectTransform)sign.transform;
            signRect.anchorMin = signRect.anchorMax = signRect.pivot = new Vector2(0.5f, 1f);
            signRect.anchoredPosition = new Vector2(0, -86); signRect.sizeDelta = new Vector2(640, 124);
            var signImage = sign.GetComponent<Image>(); signImage.raycastTarget = false;
            OnboardingKit.StyleRounded(signImage); OnboardingKit.SetRadius(signImage, 124f);
            SceneBuilderCore.Themed(sign, ThemeRole.Surface);
            var group = sign.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;

            var outline = Child(sign.transform, "Outline", typeof(Image));
            Stretch(outline.transform, 0, 0, 0, 0);
            var outlineImage = outline.GetComponent<Image>(); outlineImage.sprite = IconFactory.PillOutline; outlineImage.type = Image.Type.Sliced; outlineImage.raycastTarget = false;
            OnboardingKit.SetRadius(outlineImage, 124f);
            SceneBuilderCore.Themed(outline, ThemeRole.Gold);

            // the ring that fills while the sign is held, with the number of fingers in it
            var track = Child(sign.transform, "RingTrack", typeof(Image));
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = trackRect.anchorMax = trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.anchoredPosition = new Vector2(18, 0); trackRect.sizeDelta = new Vector2(92, 92);
            var trackImage = track.GetComponent<Image>(); trackImage.sprite = IconFactory.RingThin; trackImage.preserveAspect = true; trackImage.raycastTarget = false;
            SceneBuilderCore.Themed(track, ThemeRole.SurfaceAlt);
            var ring = Child(track.transform, "Ring", typeof(Image));
            Stretch(ring.transform, 0, 0, 0, 0);
            var ringImage = ring.GetComponent<Image>(); ringImage.sprite = IconFactory.RingThin; ringImage.preserveAspect = true; ringImage.raycastTarget = false;
            ringImage.type = Image.Type.Filled; ringImage.fillMethod = Image.FillMethod.Radial360; ringImage.fillOrigin = (int)Image.Origin360.Top; ringImage.fillClockwise = true;
            ringImage.fillAmount = 0.6f;
            SceneBuilderCore.Themed(ring, ThemeRole.ButtonFill);
            TextMeshProUGUI count = SignText(track.transform, "CountText", "3", 48, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.Center);
            Stretch(count.transform, 0, 0, 0, 0);

            TextMeshProUGUI screen = SignText(sign.transform, "ScreenText", "Hold for Recent Scans", 36, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft);
            Stretch(screen.transform, 132, 0, 30, 0);
            screen.enableWordWrapping = false; screen.enableAutoSizing = true; screen.fontSizeMax = 36; screen.fontSizeMin = 24; screen.overflowMode = TextOverflowModes.Ellipsis;

            var indicator = SceneBuilderCore.Ensure<FingerSignIndicator>(root);
            SceneBuilderCore.SetField(indicator, "signObject", canvasObject);
            SceneBuilderCore.SetField(indicator, "group", group);
            SceneBuilderCore.SetField(indicator, "countText", count);
            SceneBuilderCore.SetField(indicator, "screenText", screen);
            SceneBuilderCore.SetField(indicator, "ring", ringImage);
            canvasObject.SetActive(false);
            return indicator;
        }

        static TextMeshProUGUI SignText(Transform parent, string name, string text, float size, ThemeRole colour, FontRole font, TextAlignmentOptions align)
        {
            var go = Child(parent, name, typeof(TextMeshProUGUI));
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = align; t.enableWordWrapping = false; t.raycastTarget = false; t.richText = false;
            SceneBuilderCore.Themed(go, colour);
            var themedFont = SceneBuilderCore.Ensure<ThemedFont>(go); themedFont.Role = font; themedFont.KeepRole = true;
            return t;
        }

        /// <summary>A shadow that fades away from one side of the screen-change sheet (it sits just outside the sheet).</summary>
        const string SkeletonShaderPath = "Assets/_Project/Shaders/SkeletonShine.shader", SkeletonMaterialFolder = "Assets/_Project/Materials/";

        /// <summary>
        /// The one material of every "skeleton" shape of the app (shader GlowUp/SkeletonShine): a blush base with a rose-to-gold
        /// shine travelling across, the look of the admin website. Made once in Assets/_Project/Materials; its speed, width
        /// and tilt can be changed there. Null when the shader file is missing (the shapes then keep their plain look).
        /// </summary>
        public static Material SkeletonMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SkeletonShaderPath);
            if (shader == null) { Debug.LogWarning("[GlowUp] " + SkeletonShaderPath + " was not found: skeleton shapes are drawn without the travelling shine."); return null; }
            Directory.CreateDirectory(SkeletonMaterialFolder);
            string path = SkeletonMaterialFolder + "SkeletonShine.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            return material;
        }

        static void EdgeShade(Transform sheet, string name, bool left)
        {
            var go = Child(sheet, name, typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(left ? 0f : 1f, 0f); rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            rt.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(90f, 0f);
            rt.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);                 // the picture's dark side is its left: turn it so the dark side touches the sheet
            var img = go.GetComponent<Image>();
            img.sprite = IconFactory.ShadowFromLeft; img.type = Image.Type.Simple; img.preserveAspect = false; img.raycastTarget = false;
            img.color = new Color(0f, 0f, 0f, 0.5f);
        }

        static GameObject Child(Transform parent, string name, params System.Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            foreach (System.Type c in components) go.AddComponent(c);
            return go;
        }

        static void Stretch(Transform t, float left, float bottom, float right, float top)
        {
            var rt = (RectTransform)t; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        static Image Block(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float radius,
            ThemeRole role, float alpha = 0.7f, bool raycast = false)
        {
            var go = Child(parent, name, typeof(Image));
            var rt = (RectTransform)go.transform; rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var img = go.GetComponent<Image>();
            OnboardingKit.StyleRounded(img); OnboardingKit.SetRadius(img, radius);
            img.raycastTarget = raycast; img.color = new Color(1, 1, 1, alpha);
            SceneBuilderCore.Themed(go, role);
            return img;
        }

        // =====================================================================================
        //  Edge light: the light that runs round the scene's panel
        // =====================================================================================
        public static void EnsureEdgeLight()
        {
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>(true))
            {
                if (!SceneBuilderCore.IsSceneCanvas(canvas)) continue;
                foreach (Transform outline in canvas.GetComponentsInChildren<Transform>(true))
                {
                    if (outline.name != "PanelOutline" || outline.parent == null) continue;
                    Transform card = outline.parent;
                    Transform lightT = card.Find("EdgeLight");
                    if (lightT == null)
                    {
                        var go = new GameObject("EdgeLight", typeof(RectTransform), typeof(CanvasRenderer), typeof(PerimeterLightStrip));
                        go.layer = card.gameObject.layer; go.transform.SetParent(card, false); lightT = go.transform;
                    }
                    Stretch(lightT, 0, 0, 0, 0);
                    SceneBuilderCore.Ensure<LayoutElement>(lightT.gameObject).ignoreLayout = true;
                    var strip = SceneBuilderCore.Ensure<PerimeterLightStrip>(lightT.gameObject);
                    strip.raycastTarget = false;
                    SceneBuilderCore.SetField(strip, "cornerRadius", 80f);          // the panel outline's radius
                    SceneBuilderCore.Themed(lightT.gameObject, ThemeRole.Gold);     // the same colour as the outline it follows
                    lightT.SetAsLastSibling();
                }
            }
        }

        // =====================================================================================
        //  The tile animation on buttons
        // =====================================================================================
        static void AddPressSweepToEveryButton()
        {
            foreach (Button button in Object.FindObjectsOfType<Button>(true))
            {
                if (IsServiceCanvas(button.GetComponentInParent<Canvas>(true)?.rootCanvas)) continue;
                if (button.name == "Backdrop") continue;                                        // the dim layer behind a pop-up
                if (button.GetComponent<GlowUp.Onboarding.SelectableTile>() != null) continue;  // tiles already have the full animation
                AddPressSweep(button.gameObject, -1f, button.GetComponent<ContinueButtonGlowAnimator>() == null);
            }
        }

        /// <summary>
        /// Gives an object the tile animation: children "PressGlow" (glowing outline) and "PressSweep" (the squares)
        /// and the PressSweepEffect script. `cornerRadius` = the object's corner radius in canvas units, negative = fully round ends.
        /// `lift` off = the object keeps its size (for buttons that another script already scales).
        /// </summary>
        /// <param name="square">On = the object has straight corners (a product tile): the glow is a square ring.</param>
        public static PressSweepEffect AddPressSweep(GameObject target, float cornerRadius = -1f, bool lift = true, bool square = false)
        {
            var rt = (RectTransform)target.transform;
            float shortSide = Mathf.Min(rt.rect.width, rt.rect.height);
            float radius = cornerRadius >= 0f ? cornerRadius : (shortSide > 2f ? shortSide * 0.5f : 60f);

            Transform glowT = target.transform.Find("PressGlow");
            if (glowT == null) { var g = new GameObject("PressGlow", typeof(RectTransform), typeof(Image)); g.layer = target.layer; g.transform.SetParent(target.transform, false); glowT = g.transform; }
            glowT.SetAsFirstSibling();
            var glow = SceneBuilderCore.Ensure<Image>(glowT.gameObject);
            glow.raycastTarget = false; glow.color = Color.clear;
            if (square)
            {
                glow.sprite = IconFactory.SquareGlow; glow.type = Image.Type.Sliced;
                Stretch(glowT, -IconFactory.SquareGlowMargin, -IconFactory.SquareGlowMargin, -IconFactory.SquareGlowMargin, -IconFactory.SquareGlowMargin);
                if (glowT.TryGetComponent(out PillCorners rounding)) Object.DestroyImmediate(rounding);
            }
            else
            {
                glow.sprite = IconFactory.NeonOutline; glow.type = Image.Type.Sliced;
                float halo = IconFactory.NeonOutlineMargin * Mathf.Max(6f, radius) / PillCorners.SpriteRadius;
                Stretch(glowT, -halo, -halo, -halo, -halo);
                var corners = SceneBuilderCore.Ensure<PillCorners>(glowT.gameObject);
                SceneBuilderCore.SetField(corners, "spriteMargin", IconFactory.NeonOutlineMargin);
                SceneBuilderCore.SetField(corners, "maxRadius", cornerRadius >= 0f ? Mathf.Max(6f, cornerRadius) : 200f);
            }
            SceneBuilderCore.Ensure<LayoutElement>(glowT.gameObject).ignoreLayout = true;

            Transform sweepT = target.transform.Find("PressSweep");
            if (sweepT == null) { var g = new GameObject("PressSweep", typeof(RectTransform), typeof(CanvasRenderer), typeof(PixelSweepGraphic)); g.layer = target.layer; g.transform.SetParent(target.transform, false); sweepT = g.transform; }
            sweepT.SetAsLastSibling();
            Stretch(sweepT, 0, 0, 0, 0);
            var sweep = SceneBuilderCore.Ensure<PixelSweepGraphic>(sweepT.gameObject);
            sweep.raycastTarget = false;
            SceneBuilderCore.SetField(sweep, "cornerRadius", cornerRadius);
            SceneBuilderCore.Ensure<LayoutElement>(sweepT.gameObject).ignoreLayout = true;

            var effect = SceneBuilderCore.Ensure<PressSweepEffect>(target);
            SceneBuilderCore.SetField(effect, "sweep", sweep);
            SceneBuilderCore.SetField(effect, "glow", glow);
            if (!lift) SceneBuilderCore.SetField(effect, "hoverScale", 1f);
            if (target.TryGetComponent(out ButtonPressFeedback old)) Object.DestroyImmediate(old);     // the tile animation takes its place
            return effect;
        }
    }
}
