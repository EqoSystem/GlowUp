using GlowUp.Common;
using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 10 AI Smart Scan.
    /// The camera view with the scan guide (corner brackets, guiding mesh, sweeping line, the outlines
    /// round the text that was read), the sun and flashlight buttons, the checklist of what was found with
    /// its gold check marks, the status line, the round HOLD-TO-SCAN button inside its ring of five
    /// segments, "Start over" and "Finish" beside it, and the pop-ups ("type it in", "the same product or a
    /// different one?").
    /// </summary>
    public static class SmartScanSceneBuilder
    {
        const float BottomBlock = 730;          // space under the camera view for the checklist, the status line and the hold button
        const float RingSize = 264, HoldSize = 190, RingBottom = 24;
        static readonly string[] ZoneNames = { "Front", "Ingredients", "Codes", "Shade", "OtherSides" };

        [MenuItem("GlowUp/Build Scene/10 AI Smart Scan", false, 120)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("AiSmartAnalysisScene", "AI Smart Scan", true, "SmartScanCard", "AiSmartAnalysisCard");
            Transform card = s.card;

            // ---- camera view (round corners; everything inside is clipped to it) ----
            Image viewport = A.RawPanel(card, "CameraViewport", new Color32(30, 26, 25, 255), 48f);
            A.Between(viewport.transform, A.HeaderBottom, BottomBlock);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            GameObject mirror = A.Node(viewport.transform, "MirrorRoot");
            A.Fill(mirror.transform);
            GameObject displayObject = A.Node(mirror.transform, "CameraDisplay", typeof(RawImage));
            A.Fill(displayObject.transform);
            var display = displayObject.GetComponent<RawImage>();
            display.color = new Color(0, 0, 0, 0); display.raycastTarget = false;     // invisible until the camera runs

            GameObject idle = A.Node(viewport.transform, "IdlePlaceholder");
            A.Fill(idle.transform);
            Image cameraIcon = A.RawIcon(idle.transform, "CameraIcon", IconFactory.CameraIcon, 150, new Color(1, 1, 1, 0.55f));
            cameraIcon.rectTransform.anchoredPosition = new Vector2(0, 60);
            TextMeshProUGUI idleText = A.RawLabel(idle.transform, "IdleText", "The camera turns on when you hold the button below", 34,
                                                  new Color(1, 1, 1, 0.82f), FontRole.Body, TextAlignmentOptions.Center, true);
            A.At(idleText.transform, A.MC, new Vector2(0, -90), new Vector2(640, 110));

            // ---- scan guide: what is inside the frame is what gets read ----
            GameObject guideObject = A.Node(viewport.transform, "ScanGuide", typeof(CanvasGroup), typeof(ScanGuideOverlay));
            A.Fill(guideObject.transform);
            var group = guideObject.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;

            GameObject frame = A.Node(guideObject.transform, "Frame");
            A.Fill(frame.transform, 56, 196, 56, 96);
            Image mesh = A.RawIcon(frame.transform, "GuideMesh", IconFactory.GuideMesh, 64, new Color(1, 1, 1, 0.22f));
            mesh.type = Image.Type.Tiled; mesh.preserveAspect = false;
            A.Fill(mesh.transform, 12, 12, 12, 12);
            Image brackets = A.RawIcon(frame.transform, "FrameBrackets", IconFactory.ScanFrame, 64, Color.white);
            brackets.type = Image.Type.Sliced; brackets.preserveAspect = false;
            A.Fill(brackets.transform);
            Image line = A.RawIcon(frame.transform, "ScanLine", IconFactory.ScanLine, 26, Color.white);
            line.preserveAspect = false;
            var lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(0, 0.78f); lineRect.anchorMax = new Vector2(1, 0.78f); lineRect.pivot = A.MC;
            lineRect.anchoredPosition = Vector2.zero; lineRect.sizeDelta = new Vector2(-40, 26);

            Image hintPill = A.RawPanel(guideObject.transform, "HintPill", new Color(0, 0, 0, 0.5f), 60f);
            A.BottomStrip(hintPill.transform, 46, 104, 56, 56);
            TextMeshProUGUI hint = A.RawLabel(hintPill.transform, "HintText", "Show the front: the brand and the product's name", 30,
                                              Color.white, FontRole.Label, TextAlignmentOptions.Center, true);
            A.Fill(hint.transform, 34, 6, 34, 6);

            var guide = guideObject.GetComponent<ScanGuideOverlay>();
            A.Set(guide, "frame", frame.transform); A.Set(guide, "frameBrackets", brackets); A.Set(guide, "mesh", mesh);
            A.Set(guide, "scanLine", lineRect); A.Set(guide, "scanLineImage", line);
            A.Set(guide, "hintText", hint); A.Set(guide, "group", group);

            // ---- the outlines round the text that was read (they cover the frame: that is the area that gets read) ----
            GameObject outlines = A.Node(frame.transform, "TextOutlines", typeof(TextOutlineOverlay));
            A.Fill(outlines.transform);
            Image outlineTemplate = A.RawIcon(outlines.transform, "OutlineTemplate", IconFactory.SquareOutline, 24, new Color32(226, 184, 96, 242));
            outlineTemplate.type = Image.Type.Sliced; outlineTemplate.preserveAspect = false;
            A.At(outlineTemplate.transform, A.MC, new Vector2(0, 120), new Vector2(520, 52));
            A.Set(outlines.GetComponent<TextOutlineOverlay>(), "outlineTemplate", outlineTemplate);

            // ---- the sun button: full screen brightness while scanning ----
            Button sun = CameraButton(viewport.transform, "BrightnessButton", IconFactory.Sun, out Image sunIcon);
            A.At(sun.transform, A.TR, new Vector2(-24, -24), new Vector2(92, 92));
            var boost = SceneBuilderCore.Ensure<ScreenBrightnessBoost>(sun.gameObject);
            A.Set(boost, "screen", (int)ScreenBrightnessBoost.CameraScreen.Scanner); A.Set(boost, "toggleButton", sun); A.Set(boost, "toggleIcon", sunIcon);

            // ---- the flashlight: its button, and the question that appears when it is dark ----
            GameObject lightHelper = A.Node(viewport.transform, "LightAssistant", typeof(ScanLightAssistant));
            A.Fill(lightHelper.transform);
            Button flash = CameraButton(lightHelper.transform, "FlashlightButton", IconFactory.Bolt, out Image flashIcon);
            A.At(flash.transform, A.TL, new Vector2(24, -24), new Vector2(92, 92));
            Image lightPrompt = A.RawPanel(lightHelper.transform, "LightPrompt", new Color(0, 0, 0, 0.72f), 44f);
            lightPrompt.raycastTarget = true;
            A.TopStrip(lightPrompt.transform, 136, 176, 40, 40);
            TextMeshProUGUI lightText = A.RawLabel(lightPrompt.transform, "PromptText", "It is dark here. Turn on the flashlight?", 30, Color.white, FontRole.Label,
                                                   TextAlignmentOptions.Center, true);
            A.TopStrip(lightText.transform, 14, 76, 28, 28);
            A.ShrinkWrapped(lightText, 22);
            Button lightYes = A.PillButton(lightPrompt.transform, "TurnOnButton", "Turn on", 28, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Bolt);
            A.At(lightYes.transform, A.BC, new Vector2(140, 16), new Vector2(250, 64));
            Button lightNo = A.PillButton(lightPrompt.transform, "NotNowButton", "Not now", 28, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.At(lightNo.transform, A.BC, new Vector2(-140, 16), new Vector2(250, 64));
            lightPrompt.gameObject.SetActive(false);         // appears when the camera picture stays dark
            var lightAssistant = lightHelper.GetComponent<ScanLightAssistant>();
            A.Set(lightAssistant, "prompt", lightPrompt.gameObject); A.Set(lightAssistant, "promptText", lightText);
            A.Set(lightAssistant, "yesButton", lightYes); A.Set(lightAssistant, "noButton", lightNo);
            A.Set(lightAssistant, "flashButton", flash); A.Set(lightAssistant, "flashIcon", flashIcon);
            A.Set(lightAssistant, "flashBackground", flash.targetGraphic);
            // reworded: the light is switched through the camera now, so a refusal has other reasons (wording changed in the Inspector is kept)
            SceneBuilderCore.ReplaceText(lightAssistant, "cannotMessage",
                "The flashlight is not available right now: the phone may be hot, or another app is using the camera. The screen was made brighter instead.",
                "This phone cannot use the flashlight while the camera is on. The screen was made brighter instead.");

            // ---- what was found: the product on the left, its ingredient list on the right ----
            Image list = A.Panel(card, "ScanChecklist", ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);
            A.BottomStrip(list.transform, 446, 276);

            GameObject foundProduct = A.Node(list.transform, "Found_Product");
            A.Share(foundProduct.transform, 0f, 0.55f);
            ScanCheckItem productCheck = CheckRow(foundProduct.transform, "ProductNameCheck", "Product name", 0);
            ScanCheckItem brandCheck = CheckRow(foundProduct.transform, "BrandCheck", "Brand", 1);
            ScanCheckItem typeCheck = CheckRow(foundProduct.transform, "CosmeticTypeCheck", "Cosmetic type", 2);
            ScanCheckItem shadeCheck = CheckRow(foundProduct.transform, "ShadeCheck", "Shade colour and texture", 3);
            Image swatch = A.RawPanel(shadeCheck.transform, "ShadeSwatch", new Color32(194, 75, 98, 255), 30f);
            A.At(swatch.transform, A.MR, new Vector2(-4, 0), new Vector2(30, 30));
            A.Outline(swatch.transform, ThemeRole.Gold, 30f);
            swatch.gameObject.SetActive(false);              // shown with the colour of the shade that was found

            Image split = A.Panel(list.transform, "Divider", ThemeRole.Gold, 0.45f, 2f);
            split.sprite = IconFactory.Square; split.type = Image.Type.Simple;
            var splitRect = split.rectTransform; splitRect.anchorMin = new Vector2(0.55f, 0); splitRect.anchorMax = new Vector2(0.55f, 1); splitRect.pivot = A.MC;
            splitRect.offsetMin = new Vector2(-1.5f, 24); splitRect.offsetMax = new Vector2(1.5f, -24);

            GameObject foundList = A.Node(list.transform, "Found_Ingredients");
            A.Share(foundList.transform, 0.55f, 1f);
            ScanCheckItem ingredientsCheck = CheckRow(foundList.transform, "IngredientListCheck", "Ingredient list", 0);
            TextMeshProUGUI lookUpHint = A.Label(foundList.transform, "LookUpHintText", "No list on the product, or no packaging?", 24, ThemeRole.SecondaryText, FontRole.Body,
                                                 TextAlignmentOptions.Center, true);
            A.BottomStrip(lookUpHint.transform, 104, 70, 22, 22);
            A.ShrinkWrapped(lookUpHint, 20);
            Button lookUp = A.PillButton(foundList.transform, "LookUpButton", "Look it up", 28, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Ai);
            A.BottomStrip(lookUp.transform, 24, 72, 26, 26);
            A.Outline(lookUp.transform, ThemeRole.Gold, 72f);

            // ---- the status line and the type-it-in link ----
            TextMeshProUGUI status = A.Label(card, "StatusText", "Hold the button and show the product. A quick tap keeps it scanning.", 31,
                                             ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.Center, true);
            A.BottomStrip(status.transform, 352, 86, 52, 52);
            A.ShrinkWrapped(status, 24);
            Button manualEntry = A.LinkButton(card, "ManualEntryButton", "Type the product in instead", 30, ThemeRole.SecondaryText);
            A.BottomStrip(manualEntry.transform, 292, 56, 180, 180);

            // ---- HOLD TO SCAN: the round button, and round it the ring of five segments that fills as the product is read ----
            GameObject hold = A.Node(card, "HoldToScan");
            A.At(hold.transform, A.BC, new Vector2(0, RingBottom), new Vector2(RingSize, RingSize));
            GameObject ringObject = A.Node(hold.transform, "ScanRing", typeof(ScanRingView));
            A.Fill(ringObject.transform);
            var tracks = new Image[ScanZones.Count]; var fills = new Image[ScanZones.Count];
            for (int i = 0; i < ScanZones.Count; i++) tracks[i] = RingSegment(ringObject.transform, "Track_" + ZoneNames[i], new Color(0.55f, 0.5f, 0.5f, 0.35f));
            for (int i = 0; i < ScanZones.Count; i++) fills[i] = RingSegment(ringObject.transform, "Fill_" + ZoneNames[i], new Color32(217, 142, 160, 255));
            var ring = ringObject.GetComponent<ScanRingView>();
            Kit.SetArray(ring, "tracks", tracks); Kit.SetArray(ring, "fills", fills);
            ring.SetUp();                                    // places the ten pictures round the ring
            foreach (Image fill in fills) { fill.fillAmount = 0f; EditorUtility.SetDirty(fill); }
            foreach (Image track in tracks) EditorUtility.SetDirty(track);

            Image disc = A.Panel(hold.transform, "HoldButton", ThemeRole.ButtonFill, 1f, HoldSize);
            disc.raycastTarget = true;                       // the button takes the touch
            A.At(disc.transform, A.MC, Vector2.zero, new Vector2(HoldSize, HoldSize));
            Image holdIcon = A.Icon(disc.transform, "Icon", IconFactory.Scan, 56, ThemeRole.ButtonText);
            holdIcon.rectTransform.anchoredPosition = new Vector2(0, 26);
            TextMeshProUGUI holdLabel = A.Label(disc.transform, "Label", "Hold to scan", 23, ThemeRole.ButtonText, FontRole.Button, TextAlignmentOptions.Center);
            A.At(holdLabel.transform, A.MC, new Vector2(0, -34), new Vector2(HoldSize - 36, 40));
            A.Shrink(holdLabel, 17);
            holdLabel.raycastTarget = false;
            var holdButton = SceneBuilderCore.Ensure<HoldToScanButton>(disc.gameObject);
            A.Set(holdButton, "scaleTarget", disc.rectTransform);

            // beside the ring: start over (left) and finish (right). Both appear when there is something to start over or to finish.
            float side = (Kit.CardWidth - RingSize) * 0.5f + 28f;       // from the card's edge to the far edge of a side button
            Button startOver = A.PillButton(card, "StartOverButton", "Start over", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(startOver.transform, RingBottom + RingSize * 0.5f - 46, 92, A.Pad, Kit.CardWidth - side + 56f);
            A.Outline(startOver.transform, ThemeRole.Accent, 92f);
            Button finish = A.PillButton(card, "FinishButton", "Finish", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(finish.transform, RingBottom + RingSize * 0.5f - 46, 92, Kit.CardWidth - side + 56f, A.Pad);
            A.Shrink(A.LabelOf(finish), 22);

            // ---- "Start over": the same product, or a different one? ----
            A.AppPopup startOverPopup = ScanAgainPopup(s, "StartOverPanel", "Keeps the product's name; the pictures are taken anew.",
                                                       "Forgets everything that was read.", out Button sameProduct, out Button differentProduct);

            // ---- type-it-in pop-up (for labels the camera cannot read) ----
            A.AppPopup manual = A.Overlay(s, "ManualEntryPanel", "Type it in");
            A.TopStrip(manual.box, 110, 1240, 40, 40);
            TextMeshProUGUI manualHint = A.Label(manual.box, "ManualHintText", "For a product the camera or the AI cannot read. Only the ingredient list is required.",
                                                 27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(manualHint.transform, 116, 76, 48, 48);

            TMP_InputField manualProduct = SceneBuilderCore.MakeInput(manual.box, "ManualProductField", "Product name", A.TC, Vector2.zero, new Vector2(800, 104));
            A.TopStrip(manualProduct.transform, 200, 104, 48, 48);
            Kit.StyleInput(manualProduct, "Product name", ThemeRole.InputBackground);
            A.Set(SceneBuilderCore.Ensure<InputFieldBehaviour>(manualProduct.gameObject), "characterLimit", 80);

            TMP_InputField manualBrand = SceneBuilderCore.MakeInput(manual.box, "ManualBrandField", "Brand", A.TC, Vector2.zero, new Vector2(390, 104));
            A.TopStrip(manualBrand.transform, 320, 104, 48, 512);
            Kit.StyleInput(manualBrand, "Brand", ThemeRole.InputBackground);
            A.Set(SceneBuilderCore.Ensure<InputFieldBehaviour>(manualBrand.gameObject), "characterLimit", 60);

            TMP_Dropdown manualType = Kit.MakeDropdown(manual.box, "ManualTypeDropdown", Vector2.zero, new Vector2(440, 104), 32);
            A.TopStrip(manualType.transform, 320, 104, 512, 48);
            manualType.ClearOptions();
            manualType.AddOptions(new System.Collections.Generic.List<string> { "Cosmetic type", "Lipstick", "Foundation", "Blush", "Eyeshadow", "Mascara", "Powder" });   // the full list is on the controller
            manualType.RefreshShownValue();

            TMP_InputField manualInput = SceneBuilderCore.MakeInput(manual.box, "ManualInputField", "Ingredient list, e.g. Aqua, Glycerin, Niacinamide, Parfum", A.TC, Vector2.zero, new Vector2(800, 430));
            A.TopStrip(manualInput.transform, 444, 440, 48, 48);
            Kit.StyleInput(manualInput, "Ingredient list, e.g. Aqua, Glycerin, Niacinamide, Parfum", ThemeRole.InputBackground);
            MakeMultiLine(manualInput, 32);
            var behaviour = SceneBuilderCore.Ensure<InputFieldBehaviour>(manualInput.gameObject);
            A.Set(behaviour, "characterLimit", 3000);

            TextMeshProUGUI manualFeedback = A.Label(manual.box, "ManualFeedbackText", "", 28, ThemeRole.Warning, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(manualFeedback.transform, 896, 84, 48, 48);
            Button manualCancel = A.PillButton(manual.box, "CancelButton", "Cancel", 38, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(manualCancel.transform, 44, 120, 48, 512);
            UnityEventTools.AddPersistentListener(manualCancel.onClick, manual.panel.Hide);
            Button manualAnalyse = A.PillButton(manual.box, "AnalyseButton", "Analyse", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sparkle);
            A.BottomStrip(manualAnalyse.transform, 44, 120, 512, 48);

            // ---- controller (kept across rebuilds: thresholds, messages and camera corrections live here) ----
            var controller = A.Controller<SmartScanFlowController>("SmartScanFlowController");
            var camera = SceneBuilderCore.Ensure<CameraFeedController>(controller.gameObject);
            A.Set(camera, "display", display); A.Set(camera, "viewport", viewport.rectTransform); A.Set(camera, "mirrorRoot", mirror.transform);
            A.Set(camera, "useFrontCamera", false);
            // the circle that shows where a tap made the camera focus (hidden until the picture is tapped)
            Image focusRing = A.RawIcon(viewport.transform, "FocusRing", IconFactory.RingThin, 170, new Color(1f, 0.86f, 0.5f, 1f));
            focusRing.raycastTarget = false;
            A.At(focusRing.transform, A.MC, Vector2.zero, new Vector2(170, 170));
            focusRing.gameObject.SetActive(false);
            A.Set(camera, "focusRing", focusRing.rectTransform);

            A.Set(controller, "cameraFeed", camera); A.Set(controller, "guide", guide); A.Set(controller, "idlePlaceholder", idle);
            A.Set(controller, "holdButton", holdButton); A.Set(controller, "holdLabel", holdLabel); A.Set(controller, "ring", ring);
            A.Set(controller, "finishButton", finish); A.Set(controller, "finishText", A.LabelOf(finish)); A.Set(controller, "startOverButton", startOver);
            A.Set(controller, "startOverPanel", startOverPopup.panel);
            A.Set(controller, "sameProductButton", sameProduct); A.Set(controller, "differentProductButton", differentProduct);
            A.Set(controller, "manualEntryButton", manualEntry); A.Set(controller, "backButton", s.back);
            A.Set(controller, "statusText", status);
            A.Set(controller, "manualPanel", manual.panel); A.Set(controller, "manualInput", manualInput);
            A.Set(controller, "manualProductInput", manualProduct); A.Set(controller, "manualBrandInput", manualBrand); A.Set(controller, "manualTypeDropdown", manualType);
            A.Set(controller, "manualAnalyseButton", manualAnalyse); A.Set(controller, "manualFeedbackText", manualFeedback);
            A.Set(controller, "textOutlines", outlines.GetComponent<TextOutlineOverlay>());
            A.Set(controller, "productCheck", productCheck); A.Set(controller, "brandCheck", brandCheck);
            A.Set(controller, "typeCheck", typeCheck); A.Set(controller, "shadeCheck", shadeCheck); A.Set(controller, "shadeSwatch", swatch);
            A.Set(controller, "ingredientsCheck", ingredientsCheck);
            A.Set(controller, "lookUpButton", lookUp); A.Set(controller, "lookUpLabel", A.LabelOf(lookUp));
            GlowUp.Scanning.GeminiApiConfig aiConfig = AppTiles.AiConfig();
            if (aiConfig != null) A.Set(controller, "aiConfig", aiConfig);
            A.Set(lightAssistant, "cameraFeed", camera);
            // texts of the earlier two-step scan that would name buttons which are gone (wording changed in the Inspector is kept)
            SceneBuilderCore.ReplaceText(controller, "hintIngredients", "Turn to the back: show the ingredient list", "Now fit the ingredient list inside the frame");
            SceneBuilderCore.ReplaceText(controller, "msgAiLimit", "The AI has been asked as often as it may be for now. Type the product's name, or tap Finish anyway.",
                "The AI could not name this product. Type its name instead, or tap Skip and scan the ingredient list.");

            AppTiles.SwipeNavigation(s, null);               // drag sideways: Dashboard on one side, AR Try-On on the other

            A.Finish(s);
            Debug.Log("[GlowUp] AiSmartAnalysisScene built. ScanGuide > Frame is the area that gets read; HoldToScan holds the round button (HoldToScanButton) and its ring of five segments " +
                      "(ScanRing: Fill_Front, Fill_Ingredients, Fill_Codes, Fill_Shade, Fill_OtherSides start empty and fill while scanning). ManualEntryPanel, StartOverPanel, " +
                      "LightAssistant > LightPrompt and ShadeSwatch are inactive until needed; StartOverButton and FinishButton are shown here and hidden by the controller until they can be used. " +
                      "The numbers of the ring and of the picture check are in GlowUp > Open Scan Rules.");
        }

        /// <summary>A round, see-through dark button for the camera view, with a white icon.</summary>
        internal static Button CameraButton(Transform parent, string name, Sprite icon, out Image iconImage)
        {
            Image fill = A.RawPanel(parent, name, new Color(0, 0, 0, 0.5f), 92f);
            fill.raycastTarget = true;
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            iconImage = A.RawIcon(fill.transform, "Icon", icon, 48, Color.white);
            SceneBuilderCore.Ensure<ButtonPressFeedback>(fill.gameObject);
            return button;
        }

        /// <summary>One segment of the scan ring: the ring picture, drawn as a part of a circle (ScanRingView.SetUp turns it into place).</summary>
        static Image RingSegment(Transform parent, string name, Color color)
        {
            Image image = A.RawIcon(parent, name, IconFactory.ScanRing, RingSize, color);
            image.preserveAspect = false;
            A.Fill(image.transform);
            return image;
        }

        /// <summary>
        /// The question "the same product, or a different one?" with its two buttons and a short line under each that says
        /// what the choice does. Used by the scan screen ("Start over") and by the result screen ("Scan again").
        /// </summary>
        internal static A.AppPopup ScanAgainPopup(A.AppScreen s, string name, string sameNote, string differentNote, out Button same, out Button different)
        {
            A.AppPopup p = A.Overlay(s, name, "Scan again");
            A.At(p.box, A.MC, new Vector2(0, 60), new Vector2(900, 640));
            TextMeshProUGUI question = A.Label(p.box, "QuestionText", "Is it the same product, or a different one?", 32, ThemeRole.MainText, FontRole.Body,
                                               TextAlignmentOptions.TopLeft, true);
            A.TopStrip(question.transform, 130, 56, 48, 48);
            A.Shrink(question, 24);

            same = A.PillButton(p.box, "SameProductButton", "Scan the same product again", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Scan);
            A.TopStrip(same.transform, 206, 116, 48, 48);
            TextMeshProUGUI sameText = A.Label(p.box, "SameProductNote", sameNote, 25, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(sameText.transform, 330, 44, 48, 48);
            A.Shrink(sameText, 19);

            different = A.PillButton(p.box, "DifferentProductButton", "Scan a different product", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Swap);
            A.TopStrip(different.transform, 408, 116, 48, 48);
            A.Outline(different.transform, ThemeRole.Accent, 116f);
            TextMeshProUGUI differentText = A.Label(p.box, "DifferentProductNote", differentNote, 25, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(differentText.transform, 532, 44, 48, 48);
            A.Shrink(differentText, 19);
            return p;
        }

        /// <summary>One line of the checklist: an empty ring that becomes a glowing gold check mark, and the text.</summary>
        static ScanCheckItem CheckRow(Transform parent, string name, string label, int index)
        {
            GameObject row = A.Node(parent, name, typeof(ScanCheckItem));
            A.TopStrip(row.transform, 24 + index * 58, 52, 26, 18);
            Image glow = A.RawIcon(row.transform, "Glow", IconFactory.SoftDisc, 84, new Color32(184, 138, 59, 0));
            A.At(glow.transform, A.ML, new Vector2(-22, 0), new Vector2(84, 84));
            Image ring = A.RawIcon(row.transform, "Ring", IconFactory.RingThin, 40, new Color32(139, 123, 122, 180));
            A.At(ring.transform, A.ML, Vector2.zero, new Vector2(40, 40));
            Image mark = A.RawPanel(row.transform, "CheckMark", new Color32(184, 138, 59, 255), 40f);
            A.At(mark.transform, A.ML, Vector2.zero, new Vector2(40, 40));
            A.RawIcon(mark.transform, "Check", IconFactory.Check, 24, Color.white);
            mark.gameObject.SetActive(false);                // pops in when the thing is found
            TextMeshProUGUI text = A.Label(row.transform, "Text", label, 29, ThemeRole.SecondaryText, FontRole.Label);
            A.Fill(text.transform, 54, 0, 40, 0);
            A.Shrink(text, 20);
            Object.DestroyImmediate(text.GetComponent<ThemedGraphic>());       // ScanCheckItem paints the text itself
            var item = row.GetComponent<ScanCheckItem>();
            A.Set(item, "ring", ring); A.Set(item, "mark", mark.rectTransform); A.Set(item, "markDisc", mark);
            A.Set(item, "glow", glow); A.Set(item, "text", text); A.Set(item, "label", label);
            return item;
        }

        /// <summary>Turns a single-line input into a text box: several lines, text starting at the top-left.</summary>
        public static void MakeMultiLine(TMP_InputField field, float fontSize)
        {
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            var image = field.GetComponent<Image>();
            if (image != null) Kit.SetRadius(image, 48f);
            RectTransform area = field.textViewport;
            if (area != null) { area.offsetMin = new Vector2(38, 28); area.offsetMax = new Vector2(-38, -28); }
            foreach (TMP_Text t in new[] { field.textComponent, field.placeholder as TMP_Text })
            {
                if (t == null) continue;
                t.enableAutoSizing = false; t.fontSize = fontSize; t.enableWordWrapping = true;
                t.alignment = TextAlignmentOptions.TopLeft; t.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(t);
            }
            var so = new SerializedObject(field);
            var size = so.FindProperty("m_GlobalPointSize");
            if (size != null) { size.floatValue = fontSize; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(field);
        }
    }
}
