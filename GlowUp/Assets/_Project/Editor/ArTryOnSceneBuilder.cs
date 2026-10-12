using GlowUp.AR;
using GlowUp.Common;
using GlowUp.Scan;
using GlowUp.Theme;
using GlowUp.Views;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 13 AR Try-On.
    /// The front camera with the face guide and the colour shapes, the buttons on the camera view
    /// (before/after split, face light, bright screen), and under it a list that scrolls: the product
    /// line, the area tabs (Lips / Cheeks / Eyes / Face), the finish tabs (Matte / Satin / Glossy /
    /// Shimmer / Metallic), the product's shades, the recommended shades and the strength slider.
    /// Then the bottom action bar and the saved-product picker.
    /// </summary>
    public static class ArTryOnSceneBuilder
    {
        const float BelowCamera = 700;          // space under the camera view for the controls and the bottom bar
        const int SwatchCount = 6;

        [MenuItem("GlowUp/Build Scene/13 AR Try-On", false, 123)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("ARTryOnScene", "AR Try-On", true, "ArTryOnCard");
            Transform card = s.card;

            Button change = A.IconButton(card, "ChangeProductButton", IconFactory.Swap, 92, 52, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(change.transform, A.TR, new Vector2(-28, -28), new Vector2(92, 92));
            A.Outline(change.transform, ThemeRole.Accent, 92f);

            // ---- camera view ----
            Image viewport = A.RawPanel(card, "CameraViewport", new Color32(30, 26, 25, 255), 48f);
            A.Between(viewport.transform, A.HeaderBottom, BelowCamera);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            GameObject mirror = A.Node(viewport.transform, "MirrorRoot");
            A.Fill(mirror.transform);
            GameObject displayObject = A.Node(mirror.transform, "CameraDisplay", typeof(RawImage));
            A.Fill(displayObject.transform);
            var display = displayObject.GetComponent<RawImage>();
            display.color = new Color(0, 0, 0, 0); display.raycastTarget = false;     // invisible until the camera runs

            // ---- the plain picture for the "before" side of the split (live tracking fills it; inactive until the split is on) ----
            GameObject beforeClip = A.Node(viewport.transform, "BeforeClip", typeof(RectMask2D));
            A.Fill(beforeClip.transform);
            GameObject beforeObject = A.Node(beforeClip.transform, "BeforeDisplay", typeof(RawImage));
            A.Fill(beforeObject.transform);
            var beforeDisplay = beforeObject.GetComponent<RawImage>();
            beforeDisplay.raycastTarget = false;
            beforeObject.SetActive(false);
            beforeClip.SetActive(false);

            // ---- guided preview: the colour shapes (inside AfterClip, so the split can cut them) and the face outline ----
            GameObject guided = A.Node(viewport.transform, "GuidedPreview");
            A.Fill(guided.transform);
            GameObject afterClip = A.Node(guided.transform, "AfterClip", typeof(RectMask2D));
            A.Fill(afterClip.transform);
            // move or resize a shape to fine-tune where the colour lands
            GameObject face = A.Node(afterClip.transform, "FaceGuide");
            A.At(face.transform, A.MC, new Vector2(0, 36), new Vector2(620, 775));
            Image faceTint = Shape(face.transform, "FaceTint", IconFactory.SoftDisc, 0.5f, 0.5f, 700, 860, new Color(0, 0, 0, 0));
            Image cheekLeft = Shape(face.transform, "CheekLeft", IconFactory.SoftDisc, 0.25f, 0.40f, 160, 130, new Color(0.90f, 0.50f, 0.52f, 0.40f));
            Image cheekRight = Shape(face.transform, "CheekRight", IconFactory.SoftDisc, 0.75f, 0.40f, 160, 130, new Color(0.90f, 0.50f, 0.52f, 0.40f));
            Image eyeLeft = Shape(face.transform, "EyeLeft", IconFactory.EyeShadow, 0.31f, 0.585f, 152, 88, new Color(0.62f, 0.42f, 0.50f, 0.45f));
            Image eyeRight = Shape(face.transform, "EyeRight", IconFactory.EyeShadow, 0.69f, 0.585f, 152, 88, new Color(0.62f, 0.42f, 0.50f, 0.45f));
            Image lips = Shape(face.transform, "Lips", IconFactory.Lips, 0.5f, 0.245f, 212, 106, new Color(0.78f, 0.26f, 0.36f, 0.70f));
            // the highlight and the sparkle of the finish, on each shape (invisible for a matte finish)
            Image[] finishShapes = { lips, cheekLeft, cheekRight, eyeLeft, eyeRight, faceTint };
            var shines = new Object[finishShapes.Length]; var glitters = new Object[finishShapes.Length];
            for (int i = 0; i < finishShapes.Length; i++)
            {
                Image shine = A.RawIcon(finishShapes[i].transform, "Shine", IconFactory.SoftDisc, 40, new Color(1, 1, 1, 0));
                shine.preserveAspect = false;
                var shineRect = shine.rectTransform; shineRect.anchorMin = new Vector2(0.24f, 0.46f); shineRect.anchorMax = new Vector2(0.76f, 0.8f);
                shineRect.offsetMin = shineRect.offsetMax = Vector2.zero;
                Image glitter = A.RawIcon(finishShapes[i].transform, "Glitter", IconFactory.Glitter, 40, new Color(1, 1, 1, 0));
                glitter.preserveAspect = false; glitter.type = Image.Type.Tiled;
                var glitterRect = glitter.rectTransform; glitterRect.anchorMin = new Vector2(0.2f, 0.26f); glitterRect.anchorMax = new Vector2(0.8f, 0.76f);
                glitterRect.offsetMin = glitterRect.offsetMax = Vector2.zero;
                shines[i] = shine; glitters[i] = glitter;
            }
            Image outline = A.RawIcon(guided.transform, "GuideOutline", IconFactory.FaceGuide, 620, new Color(1, 1, 1, 0.85f));
            outline.preserveAspect = false;
            A.At(outline.transform, A.MC, new Vector2(0, 36), new Vector2(620, 775));

            // ---- before / after: the line that can be dragged (hidden until the compare button is tapped) ----
            GameObject compare = A.Node(viewport.transform, "CompareSplit", typeof(BeforeAfterSplit));
            A.Fill(compare.transform);
            GameObject splitVisuals = A.Node(compare.transform, "SplitVisuals");
            A.Fill(splitVisuals.transform);
            Image beforeTag = A.RawPanel(splitVisuals.transform, "BeforeTag", new Color(0, 0, 0, 0.55f), 56f);
            A.At(beforeTag.transform, A.BL, new Vector2(24, 124), new Vector2(170, 56));
            A.Fill(A.RawLabel(beforeTag.transform, "Text", "Before", 27, Color.white, FontRole.Label, TextAlignmentOptions.Center).transform);
            Image afterTag = A.RawPanel(splitVisuals.transform, "AfterTag", new Color(0, 0, 0, 0.55f), 56f);
            A.At(afterTag.transform, A.BR, new Vector2(-24, 124), new Vector2(150, 56));
            A.Fill(A.RawLabel(afterTag.transform, "Text", "After", 27, Color.white, FontRole.Label, TextAlignmentOptions.Center).transform);
            GameObject divider = A.Node(splitVisuals.transform, "Divider", typeof(Image));
            var dividerRect = (RectTransform)divider.transform;
            dividerRect.anchorMin = new Vector2(0, 0); dividerRect.anchorMax = new Vector2(0, 1); dividerRect.pivot = A.MC;
            dividerRect.anchoredPosition = new Vector2(460, 0); dividerRect.sizeDelta = new Vector2(130, 0);       // wide, so it is easy to grab
            var dividerHit = divider.GetComponent<Image>(); dividerHit.sprite = IconFactory.Square; dividerHit.color = new Color(1, 1, 1, 0f);
            Image dividerLine = A.RawIcon(divider.transform, "Line", IconFactory.Square, 6, Color.white);
            dividerLine.preserveAspect = false;
            var lineRect = dividerLine.rectTransform; lineRect.anchorMin = new Vector2(0.5f, 0); lineRect.anchorMax = new Vector2(0.5f, 1); lineRect.pivot = A.MC;
            lineRect.anchoredPosition = Vector2.zero; lineRect.sizeDelta = new Vector2(5, 0);
            Image dividerHandle = A.RawPanel(divider.transform, "Handle", Color.white, 76f);
            A.At(dividerHandle.transform, A.MC, Vector2.zero, new Vector2(76, 76));
            A.RawIcon(dividerHandle.transform, "Icon", IconFactory.Swap, 40, new Color32(74, 65, 64, 255));
            splitVisuals.SetActive(false);

            Image statusPill = A.RawPanel(viewport.transform, "StatusPill", new Color(0, 0, 0, 0.5f), 60f);
            A.BottomStrip(statusPill.transform, 28, 80, 90, 90);
            TextMeshProUGUI status = A.RawLabel(statusPill.transform, "StatusText", "Line your face up with the guide", 29, Color.white, FontRole.Label,
                                                TextAlignmentOptions.Center);
            A.Fill(status.transform, 26, 0, 26, 0);
            A.Shrink(status, 22);

            // ---- the buttons on the camera view ----
            Button compareButton = SmartScanSceneBuilder.CameraButton(viewport.transform, "CompareButton", IconFactory.Compare, out Image compareIcon);
            A.At(compareButton.transform, A.TL, new Vector2(24, -24), new Vector2(92, 92));
            Button lightButton = SmartScanSceneBuilder.CameraButton(viewport.transform, "FaceLightButton", IconFactory.Bulb, out Image lightIcon);
            A.At(lightButton.transform, A.TR, new Vector2(-132, -24), new Vector2(92, 92));
            Button sun = SmartScanSceneBuilder.CameraButton(viewport.transform, "BrightnessButton", IconFactory.Sun, out Image sunIcon);
            A.At(sun.transform, A.TR, new Vector2(-24, -24), new Vector2(92, 92));
            var boost = SceneBuilderCore.Ensure<ScreenBrightnessBoost>(sun.gameObject);
            A.Set(boost, "screen", (int)ScreenBrightnessBoost.CameraScreen.TryOn); A.Set(boost, "toggleButton", sun); A.Set(boost, "toggleIcon", sunIcon);

            var splitView = compare.GetComponent<BeforeAfterSplit>();
            A.Set(splitView, "area", viewport.rectTransform); A.Set(splitView, "afterClip", afterClip.GetComponent<RectMask2D>());
            A.Set(splitView, "beforeClip", beforeClip.GetComponent<RectMask2D>()); A.Set(splitView, "visuals", splitVisuals);
            A.Set(splitView, "divider", dividerRect); A.Set(splitView, "toggleButton", compareButton); A.Set(splitView, "toggleIcon", compareIcon);

            // ---- the face light: a white frame round the whole screen (invisible until it is dark or the lamp button is tapped) ----
            GameObject screenLight = A.Node(s.canvas, "ScreenLight", typeof(CanvasGroup), typeof(ScreenLightFrame));
            A.Fill(screenLight.transform);
            var lightGroup = screenLight.GetComponent<CanvasGroup>(); lightGroup.alpha = 0f; lightGroup.blocksRaycasts = false; lightGroup.interactable = false;
            LightBar(screenLight.transform, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -150), Vector2.zero);
            LightBar(screenLight.transform, "Bottom", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 150));
            LightBar(screenLight.transform, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 150), new Vector2(64, -150));
            LightBar(screenLight.transform, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-64, 150), new Vector2(0, -150));
            var faceLight = screenLight.GetComponent<ScreenLightFrame>();
            A.Set(faceLight, "frame", lightGroup); A.Set(faceLight, "toggleButton", lightButton); A.Set(faceLight, "toggleIcon", lightIcon);

            // ---- the controls under the camera: one list that scrolls ----
            GameObject controls = A.Node(card, "ControlsArea");
            A.BottomStrip(controls.transform, A.AboveBar, BelowCamera - A.AboveBar - 16);
            A.Scroll(controls.transform, "ControlsScrollView", out RectTransform list, 14, 0, 20, 4, 24);

            // the product being tried
            TextMeshProUGUI productText = A.Label(list, "ProductText", "Choose a product to try on", 31, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.Center);
            A.Size(productText, -1, 50);
            A.Shrink(productText, 23);

            // where the colour goes (only the part the product is made for can be chosen)
            ChoiceTabs tabs = TabRow(list, "AreaTabs", ShadeAdvisor.Areas, 30);
            // the texture of the colour
            ChoiceTabs finishTabs = TabRow(list, "FinishTabs", new[] { "Matte", "Satin", "Glossy", "Shimmer", "Metallic" }, 26);

            // ---- shades ----
            ShadeButton[] productShades = ShadeGroup(list, "ProductShades", "This product's shades", out GameObject productGroup, out _);
            ShadeButton[] recommendedShades = ShadeGroup(list, "RecommendedShades", "Recommended shades", out _, out TextMeshProUGUI recommendedLabel);

            // ---- strength ----
            GameObject strength = A.Node(list, "StrengthRow");
            A.Size(strength.transform, -1, 84);
            TextMeshProUGUI strengthLabel = A.Label(strength.transform, "StrengthLabel", "Strength", 29, ThemeRole.SecondaryText, FontRole.Body);
            A.At(strengthLabel.transform, A.ML, Vector2.zero, new Vector2(160, 60));
            Button less = A.IconButton(strength.transform, "LessButton", IconFactory.Minus, 72, 34, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(less.transform, A.ML, new Vector2(166, 0), new Vector2(72, 72));
            Slider slider = BuildSlider(strength.transform, "StrengthSlider");
            A.Fill(slider.transform, 252, 0, 214, 0);
            Button more = A.IconButton(strength.transform, "MoreButton", IconFactory.Plus, 72, 34, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(more.transform, A.MR, new Vector2(-128, 0), new Vector2(72, 72));
            TextMeshProUGUI strengthValue = A.Label(strength.transform, "StrengthValueText", "60%", 33, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineRight);
            A.At(strengthValue.transform, A.MR, Vector2.zero, new Vector2(116, 60));

            BottomActionBar bar = A.BottomBar(s, 3);
            AppTiles.SwipeNavigation(s, bar);                // drag sideways: AI Smart Scan is the screen before this one


            // ---- saved-product picker ----
            A.AppPopup picker = A.Overlay(s, "ProductPickerPanel", "Choose a product");
            // the picker opens by itself when Try On is opened without a product: a sideways drag must still lead back to the dashboard
            A.Set(picker.panel, "letsSidewaysSwipesThrough", true);
            A.Fill(picker.box, 40, 40, 40, 190);
            TextMeshProUGUI pickerHint = A.Label(picker.box, "PickerHintText", "Pick a saved product, or search the product list.", 29, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(pickerHint.transform, 114, 50, 48, 48);
            // not scanned yet? the product can be searched for by hand
            TMP_InputField pickerSearch = SceneBuilderCore.MakeInput(picker.box, "PickerSearchField", "Search a product or brand", A.TC, Vector2.zero, new Vector2(800, 92));
            A.TopStrip(pickerSearch.transform, 176, 92, 48, 48);
            Kit.StyleInput(pickerSearch, "Search a product or brand", ThemeRole.InputBackground);
            GameObject pickerArea = A.Node(picker.box, "ListArea");
            A.Fill(pickerArea.transform, 32, 190, 32, 284);
            AppTiles.GridScroll(pickerArea.transform, "PickerScrollView", out RectTransform pickerContent);
            ProductTileView pickerTemplate = AppTiles.ProductTile(pickerContent, "PickerTileTemplate", AppTiles.TileKind.Picker);
            TextMeshProUGUI pickerEmpty = A.Label(pickerArea.transform, "PickerEmptyText",
                "You have no saved products yet. Scan one first, then come back to try it on.", 32, ThemeRole.SecondaryText, FontRole.Body,
                TextAlignmentOptions.Center, true);
            A.Fill(pickerEmpty.transform, 50, 0, 50, 0);
            pickerEmpty.gameObject.SetActive(false);
            Button pickerScan = A.PillButton(picker.box, "ScanProductButton", "Scan a product", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Scan);
            A.BottomStrip(pickerScan.transform, 44, 116, 48, 48);
            A.Outline(pickerScan.transform, ThemeRole.Accent, 116f);

            // ---- controller (kept across rebuilds: camera corrections, shape opacities and texts live here) ----
            var controller = A.Controller<ArTryOnFlowController>("ArTryOnFlowController");
            var camera = SceneBuilderCore.Ensure<CameraFeedController>(controller.gameObject);
            A.Set(camera, "display", display); A.Set(camera, "viewport", viewport.rectTransform); A.Set(camera, "mirrorRoot", mirror.transform);
            A.Set(camera, "useFrontCamera", true);
            var makeup = SceneBuilderCore.Ensure<FallbackMakeupOverlay>(controller.gameObject);
            A.Set(makeup, "lips", lips); A.Set(makeup, "cheekLeft", cheekLeft); A.Set(makeup, "cheekRight", cheekRight);
            A.Set(makeup, "eyeLeft", eyeLeft); A.Set(makeup, "eyeRight", eyeRight); A.Set(makeup, "faceTint", faceTint);
            Kit.SetArray(makeup, "shines", shines); Kit.SetArray(makeup, "glitters", glitters);
            A.Set(faceLight, "cameraFeed", camera);

            // ---- live face tracking (Google ARCore). Its objects stay INACTIVE in the Hierarchy: ArFaceTrackingController
            //      switches them on when the phone supports face tracking and uses the guided preview above when it does not. ----
            ArFaceTrackingSetup.Apply(new List<string>());                       // project settings, plug-in, camera picture feature (says what it did in the Console)
            ArFaceTrackingSetup.Layers layers = ArFaceTrackingSetup.EnsureLayers();

            GameObject arSession = ArRoot("AR Session", typeof(ARSession), typeof(ARInputManager));
            var session = arSession.GetComponent<ARSession>();
            session.requestedTrackingMode = TrackingMode.DontCare;               // the front camera has no room tracking; asking for it would pick the back camera
            session.attemptUpdate = false;                                       // installing AR services is offered by ArFaceTrackingController instead
            A.Set(session, "m_MatchFrameRate", false);                           // the app's own frame-rate cap (Settings page) stays in charge

            GameObject xrOrigin = ArRoot("XR Origin", typeof(XROrigin), typeof(ARFaceManager));
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(xrOrigin.transform, false);
            var arCameraObject = new GameObject("AR Camera", typeof(Camera), typeof(ARCameraManager), typeof(ARCameraBackground));
            arCameraObject.transform.SetParent(offset.transform, false);
            var arCamera = arCameraObject.GetComponent<Camera>();
            arCamera.clearFlags = CameraClearFlags.SolidColor; arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.05f; arCamera.farClipPlane = 20f; arCamera.depth = -5;
            var arCameraManager = arCameraObject.GetComponent<ARCameraManager>();
            arCameraManager.requestedFacingDirection = CameraFacingDirection.User;   // the selfie camera
            arCameraManager.autoFocusRequested = true;                               // keeps the face sharp where the selfie camera can focus (ArFaceTrackingController > Auto Focus sets it again at run time)
            // the component keeps an older focus setting too and reads it instead when the scene loads: mark it as already converted
            var cameraSettings = new SerializedObject(arCameraManager);
            cameraSettings.FindProperty("m_FocusMode").intValue = -1;
            cameraSettings.FindProperty("m_AutoFocus").boolValue = true;
            cameraSettings.ApplyModifiedPropertiesWithoutUndo();
            var origin = xrOrigin.GetComponent<XROrigin>();
            A.Set(origin, "m_Camera", arCamera); A.Set(origin, "m_CameraFloorOffsetObject", offset); A.Set(origin, "m_OriginBaseGameObject", xrOrigin);
            A.Set(origin, "m_CameraYOffset", 0f);                                    // a phone is held in the hand: no standing-height offset
            var faces = xrOrigin.GetComponent<ARFaceManager>();
            faces.requestedMaximumFaceCount = 1;
            arSession.SetActive(false);
            xrOrigin.SetActive(false);

            var liveMakeup = SceneBuilderCore.Ensure<ArFaceMakeup>(controller.gameObject);
            A.Set(liveMakeup, "choices", makeup);
            if (layers.face != null) A.Set(liveMakeup, "faceMaterial", layers.face);
            if (layers.cheeks != null) A.Set(liveMakeup, "cheeksMaterial", layers.cheeks);
            if (layers.eyes != null) A.Set(liveMakeup, "eyesMaterial", layers.eyes);
            if (layers.lips != null) A.Set(liveMakeup, "lipsMaterial", layers.lips);
            if (layers.shine != null) A.Set(liveMakeup, "shineMaterial", layers.shine);
            Kit.SetArray(liveMakeup, "shineMasks", layers.shineMasks);

            var live = SceneBuilderCore.Ensure<ArFaceTrackingController>(controller.gameObject);
            A.Set(live, "sessionObject", arSession); A.Set(live, "originObject", xrOrigin); A.Set(live, "arCamera", arCamera);
            A.Set(live, "cameraManager", arCameraManager); A.Set(live, "faceManager", faces); A.Set(live, "makeup", liveMakeup);
            A.Set(live, "display", display); A.Set(live, "viewport", viewport.rectTransform); A.Set(live, "mirrorRoot", mirror.transform);
            A.Set(live, "faceGuide", guided); A.Set(live, "beforeDisplay", beforeDisplay); A.Set(live, "statusText", status); A.Set(live, "guidedCamera", camera);
            A.Set(controller, "liveTracking", live);

            A.Set(controller, "cameraFeed", camera); A.Set(controller, "makeup", makeup); A.Set(controller, "statusText", status);
            A.Set(controller, "productText", productText); A.Set(controller, "changeProductButton", change);
            A.Set(controller, "areaTabs", tabs); A.Set(controller, "finishTabs", finishTabs); A.Set(controller, "split", splitView);
            A.Set(controller, "productShadesGroup", productGroup);
            Kit.SetArray(controller, "productShadeButtons", productShades); Kit.SetArray(controller, "recommendedShadeButtons", recommendedShades);
            A.Set(controller, "recommendedLabel", recommendedLabel);
            A.Set(controller, "intensitySlider", slider); A.Set(controller, "intensityValueText", strengthValue);
            A.Set(controller, "lessButton", less); A.Set(controller, "moreButton", more);
            A.Set(controller, "pickerPanel", picker.panel); A.Set(controller, "pickerContent", pickerContent); A.Set(controller, "pickerTileTemplate", pickerTemplate);
            A.Set(controller, "pickerEmptyText", pickerEmpty); A.Set(controller, "pickerSearchInput", pickerSearch); A.Set(controller, "pickerScanButton", pickerScan);
            A.Set(controller, "typeIcons", A.TypeIcons());
            A.Set(controller, "backButton", s.back);

            // while the profile and the product are fetched: the screen's titles and labels are bars
            AppTiles.WordSkeleton(s);

            A.Finish(s);
            Debug.Log("[GlowUp] ARTryOnScene built. GuidedPreview > AfterClip > FaceGuide holds the colour shapes of the guided preview (the tints you see in the Editor are samples); " +
                      "CompareSplit > SplitVisuals (the before/after line) and ScreenLight (the white face light; set its CanvasGroup's Alpha to 1 to see it) are hidden until used; " +
                      "AR Session and XR Origin (live face tracking) are inactive until the app has checked the phone; ProductPickerPanel is inactive until needed.");
        }

        /// <summary>An object at the top of the Hierarchy for live tracking. It is rebuilt with the scene, like everything the builder makes.</summary>
        static GameObject ArRoot(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.AddComponent<GeneratedByBuilder>();
            return go;
        }

        /// <summary>One white bar of the face light.</summary>
        static void LightBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image bar = A.RawIcon(parent, name, IconFactory.Square, 10, Color.white);
            bar.preserveAspect = false;
            var rt = bar.rectTransform; rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        /// <summary>A row of pill tabs where one is chosen (a line of the controls list).</summary>
        static ChoiceTabs TabRow(Transform list, string name, string[] captions, float fontSize)
        {
            GameObject tabsObject = A.Node(list, name, typeof(ChoiceTabs));
            A.Size(tabsObject.transform, -1, 76);
            A.HStack(tabsObject, 10, 0, 0, 0, 0, TextAnchor.MiddleCenter, true);
            var buttons = new Button[captions.Length];
            var labels = new TextMeshProUGUI[captions.Length];
            for (int i = 0; i < captions.Length; i++)
            {
                Button tab = A.PillButton(tabsObject.transform, captions[i] + "Tab", captions[i], fontSize, ThemeRole.InputBackground, ThemeRole.MainText);
                A.Size(tab, 100, 76, 1);
                buttons[i] = tab; labels[i] = A.LabelOf(tab);
                A.Shrink(labels[i], fontSize * 0.72f);
                // ChoiceTabs paints the chosen / not chosen colours itself
                Object.DestroyImmediate(tab.GetComponent<ThemedGraphic>());
                Object.DestroyImmediate(labels[i].GetComponent<ThemedGraphic>());
            }
            var tabs = tabsObject.GetComponent<ChoiceTabs>();
            Kit.SetArray(tabs, "tabs", buttons); Kit.SetArray(tabs, "labels", labels);
            return tabs;
        }

        /// <summary>One colour shape on the face guide, placed by its position inside the guide (0..1 across, 0..1 up).</summary>
        static Image Shape(Transform face, string name, Sprite sprite, float across, float up, float width, float height, Color sample)
        {
            Image img = A.RawIcon(face, name, sprite, width, sample);
            img.preserveAspect = false;
            // the colour tints the camera picture under it and keeps the skin's texture (looks worn, not painted on)
            Material worn = ArFaceTrackingSetup.PreviewShapeMaterial();
            if (worn != null) img.material = worn;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(across, up); rt.pivot = A.MC;
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(width, height);
            return img;
        }

        /// <summary>A caption on the left and a row of round shade swatches on the right.</summary>
        static ShadeButton[] ShadeGroup(Transform list, string name, string caption, out GameObject group, out TextMeshProUGUI label)
        {
            group = A.Node(list, name);
            A.Size(group.transform, -1, 96);
            label = A.Label(group.transform, name + "Label", caption, 27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft, true);
            A.Fill(label.transform, 0, 0, 640, 0);
            A.ShrinkWrapped(label, 21);

            GameObject row = A.Node(group.transform, "Swatches");
            A.Fill(row.transform, 296, 0, 0, 0);
            A.HStack(row, 16, 8, 0, 0, 0, TextAnchor.MiddleLeft);
            string[] samples = { "#C4696F", "#B5525C", "#D98880", "#A9454F", "#E0A18F", "#8E3B46" };
            var buttons = new ShadeButton[SwatchCount];
            for (int i = 0; i < SwatchCount; i++)
            {
                GameObject go = A.Node(row.transform, "Shade" + (i + 1), typeof(Image), typeof(Button), typeof(ShadeButton));
                A.Size(go.transform, 82, 82);
                var swatch = go.GetComponent<Image>();
                Kit.StyleRounded(swatch); Kit.SetRadius(swatch, 82f);
                swatch.color = SceneBuilderCore.Hex(samples[i % samples.Length]);
                var button = go.GetComponent<Button>(); button.targetGraphic = swatch; button.transition = Selectable.Transition.None;
                A.Outline(go.transform, ThemeRole.MainText, 82f, 0.28f, "Edge");

                // the glowing ring of the chosen shade (invisible until the shade is chosen)
                GameObject ringObject = A.Node(go.transform, "SelectedRing", typeof(Image));
                var ring = ringObject.GetComponent<Image>();
                ring.sprite = IconFactory.NeonOutline; ring.type = Image.Type.Sliced; ring.raycastTarget = false; ring.color = new Color(1, 1, 1, 0);
                const float gap = 5;
                float halo = IconFactory.NeonOutlineMargin * ((82 + 2 * gap) * 0.5f) / PillCorners.SpriteRadius;
                A.Fill(ringObject.transform, -(gap + halo), -(gap + halo), -(gap + halo), -(gap + halo));
                var corners = SceneBuilderCore.Ensure<PillCorners>(ringObject);
                A.Set(corners, "spriteMargin", IconFactory.NeonOutlineMargin);
                A.Set(corners, "maxRadius", 200f);
                SceneBuilderCore.Ensure<LayoutElement>(ringObject).ignoreLayout = true;

                buttons[i] = go.GetComponent<ShadeButton>();
                A.Set(buttons[i], "swatch", swatch); A.Set(buttons[i], "selectedRing", ring);
            }
            return buttons;
        }

        /// <summary>A round slider: a thin track, a dusty-pink fill and a round handle.</summary>
        internal static Slider BuildSlider(Transform parent, string name)
        {
            GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = name; go.transform.SetParent(parent, false);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            var slider = go.GetComponent<Slider>();
            const float handle = 60, track = 16;

            Transform background = go.transform.Find("Background");
            if (background != null)
            {
                var rt = (RectTransform)background; rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = A.MC;
                rt.offsetMin = new Vector2(handle * 0.5f, -track * 0.5f); rt.offsetMax = new Vector2(-handle * 0.5f, track * 0.5f);
                var img = background.GetComponent<Image>();
                if (img != null) { Kit.StyleRounded(img); img.color = Color.white; SceneBuilderCore.Themed(img.gameObject, ThemeRole.SurfaceAlt); }
            }
            Transform fillArea = go.transform.Find("Fill Area");
            if (fillArea != null)
            {
                var rt = (RectTransform)fillArea; rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = A.MC;
                rt.offsetMin = new Vector2(handle * 0.5f, -track * 0.5f); rt.offsetMax = new Vector2(-handle * 0.5f, track * 0.5f);
                if (slider.fillRect != null)
                {
                    slider.fillRect.offsetMin = Vector2.zero; slider.fillRect.offsetMax = Vector2.zero;
                    var img = slider.fillRect.GetComponent<Image>();
                    if (img != null) { Kit.StyleRounded(img); img.color = Color.white; SceneBuilderCore.Themed(img.gameObject, ThemeRole.Accent); }
                }
            }
            Transform handleArea = go.transform.Find("Handle Slide Area");
            if (handleArea != null)
            {
                var rt = (RectTransform)handleArea; rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = A.MC;
                rt.offsetMin = new Vector2(handle * 0.5f, -handle * 0.5f); rt.offsetMax = new Vector2(-handle * 0.5f, handle * 0.5f);
                if (slider.handleRect != null)
                {
                    slider.handleRect.sizeDelta = new Vector2(handle, 0);
                    var img = slider.handleRect.GetComponent<Image>();
                    if (img != null)
                    {
                        Kit.StyleRounded(img); Kit.SetRadius(img, handle); img.color = Color.white;
                        SceneBuilderCore.Themed(img.gameObject, ThemeRole.ButtonFill);
                        slider.targetGraphic = img;
                    }
                }
            }
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false; slider.value = 0.6f;
            return slider;
        }
    }
}
