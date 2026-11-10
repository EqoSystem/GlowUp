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
    /// its gold check marks, the status line, the round tap-to-scan button inside its ring of five
    /// segments, "Start over" and "Finish" beside it, the question "Is this ...?" on the camera picture, and
    /// the pop-ups ("type it in", "the same product or a different one?", "Is it one of these?").
    /// </summary>
    public static class SmartScanSceneBuilder
    {
        // New13 items 1 and 2 (10 November 2026): under the camera, from the bottom up: the control bar (Type it in | scan | photo/live),
        // the row of Start over / Finish, the status line, and ONE row of four chips (product, brand, type, shade). 130 units more camera.
        const float BottomBlock = 600;          // space under the camera view
        const float BarCentre = RingBottom + RingSize * 0.5f, SideButton = 132;          // 132 units = 44 dp
        const float ActionRowY = 300, ActionRowH = 96, StatusY = 406, StatusH = 76, ChipRowY = 494, ChipRowH = 92;
        const float RingSize = 264, HoldSize = 190, RingBottom = 24;
        static readonly string[] ZoneNames = { "Brand", "ProductName", "CosmeticType", "Barcode", "Ingredients" };

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
            TextMeshProUGUI idleText = A.RawLabel(idle.transform, "IdleText", "Tap the button below to start scanning", 34,
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

            // ---- the text tracer: corner marks round the block of text it follows. Small print gets close looks (the marks
            //      turn gold and the label says so). The box lies here at a sample place; the app hides it and moves it. ----
            GameObject trace = A.Node(frame.transform, "TextTrace", typeof(TextTraceView));
            A.Fill(trace.transform);
            Image traceBox = A.RawIcon(trace.transform, "TraceBox", IconFactory.ScanFrame, 64, new Color32(226, 184, 96, 255));
            traceBox.type = Image.Type.Sliced; traceBox.preserveAspect = false; traceBox.raycastTarget = false;
            traceBox.pixelsPerUnitMultiplier = 2f;           // smaller corners than the scan frame's own
            var traceRect = traceBox.rectTransform;
            traceRect.anchorMin = new Vector2(0.07f, 0.3f); traceRect.anchorMax = new Vector2(0.93f, 0.6f); traceRect.pivot = A.MC;
            traceRect.offsetMin = traceRect.offsetMax = Vector2.zero;
            Image traceLabelBack = A.RawPanel(traceBox.transform, "TraceLabel", new Color(0, 0, 0, 0.6f), 30f);
            traceLabelBack.raycastTarget = false;
            var traceLabelRect = traceLabelBack.rectTransform;       // sits on the top edge of the box, at its left
            traceLabelRect.anchorMin = traceLabelRect.anchorMax = new Vector2(0f, 1f); traceLabelRect.pivot = new Vector2(0f, 0f);
            traceLabelRect.anchoredPosition = new Vector2(6, 8); traceLabelRect.sizeDelta = new Vector2(440, 46);
            TextMeshProUGUI traceLabel = A.RawLabel(traceLabelBack.transform, "LabelText", "Small print: reading it enlarged", 24, Color.white, FontRole.Label,
                                                    TextAlignmentOptions.Center, false);
            A.Fill(traceLabel.transform, 14, 2, 14, 2);
            A.Shrink(traceLabel, 18);
            var traceView = trace.GetComponent<TextTraceView>();
            A.Set(traceView, "box", traceRect); A.Set(traceView, "boxGraphic", traceBox);
            A.Set(traceView, "label", traceLabel); A.Set(traceView, "labelBackground", traceLabelBack);

            // ---- the sun button: full screen brightness while scanning ----
            Button sun = CameraButton(viewport.transform, "BrightnessButton", IconFactory.Sun, out Image sunIcon);
            A.At(sun.transform, A.TR, new Vector2(-24, -24), new Vector2(92, 92));
            var boost = SceneBuilderCore.Ensure<ScreenBrightnessBoost>(sun.gameObject);
            A.Set(boost, "screen", (int)ScreenBrightnessBoost.CameraScreen.Scanner); A.Set(boost, "toggleButton", sun); A.Set(boost, "toggleIcon", sunIcon);

            // (the small camera button that sat in the camera view under the sun is gone: New13 item 1 moved it to the bottom bar)

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

            // ---- the focus lock: its padlock button beside the flashlight, and the line that says what happened ----
            GameObject focusHelper = A.Node(viewport.transform, "FocusAssistant", typeof(ScanFocusAssistant));
            A.Fill(focusHelper.transform);
            Button focusLock = CameraButton(focusHelper.transform, "FocusLockButton", IconFactory.LockOpen, out Image focusLockIcon);
            A.At(focusLock.transform, A.TL, new Vector2(132, -24), new Vector2(92, 92));
            Image focusNote = A.RawPanel(focusHelper.transform, "FocusNote", new Color(0, 0, 0, 0.72f), 40f);
            focusNote.raycastTarget = false;
            A.At(focusNote.transform, A.TL, new Vector2(240, -34), new Vector2(520, 72));
            TextMeshProUGUI focusNoteText = A.RawLabel(focusNote.transform, "NoteText", "Focus locked. Tap the padlock to release it.", 26, Color.white, FontRole.Label,
                                                       TextAlignmentOptions.Center, true);
            A.Fill(focusNoteText.transform, 22, 6, 22, 6);
            A.ShrinkWrapped(focusNoteText, 20);
            CanvasGroup focusNoteGroup = SceneBuilderCore.Ensure<CanvasGroup>(focusNote.gameObject);
            focusNoteGroup.blocksRaycasts = false; focusNoteGroup.interactable = false;
            focusNote.gameObject.SetActive(false);           // appears for a moment when the lock goes on or off
            var focusAssistant = focusHelper.GetComponent<ScanFocusAssistant>();
            A.Set(focusAssistant, "lockButton", focusLock); A.Set(focusAssistant, "lockIcon", focusLockIcon);
            A.Set(focusAssistant, "lockedSprite", IconFactory.LockClosed); A.Set(focusAssistant, "freeSprite", IconFactory.LockOpen);
            A.Set(focusAssistant, "lockBackground", focusLock.targetGraphic);
            A.Set(focusAssistant, "note", focusNoteGroup); A.Set(focusAssistant, "noteText", focusNoteText);

            // ---- the scan readout: a line of numbers for testing on a phone (inactive; "Show Scan Readout" on the controller switches it on) ----
            Image readoutBack = A.RawPanel(viewport.transform, "ScanReadout", new Color(0, 0, 0, 0.6f), 24f);
            readoutBack.raycastTarget = false;
            A.TopStrip(readoutBack.transform, 128, 84, 24, 24);
            TextMeshProUGUI readout = A.RawLabel(readoutBack.transform, "ReadoutText",
                                                 "Camera 1920 x 1080   frame 957 x 733 px   picture ok   sharp 0.62\nLetters 14 px (close look x2.3)   lines 12   sure 0.71   close looks 4   focus locked",
                                                 21, Color.white, FontRole.Body, TextAlignmentOptions.Left, true);
            A.Fill(readout.transform, 18, 6, 18, 6);
            A.ShrinkWrapped(readout, 15);
            readoutBack.gameObject.SetActive(false);

            // ---- "Is this ...?": asked on the camera picture when the pack looks like one this phone has scanned before ----
            GameObject questionHolder = A.Node(viewport.transform, "ProductQuestion", typeof(ScanProductQuestion));
            A.Fill(questionHolder.transform);
            Image questionBanner = A.RawPanel(questionHolder.transform, "QuestionBanner", new Color(0, 0, 0, 0.78f), 44f);
            questionBanner.raycastTarget = true;
            A.BottomStrip(questionBanner.transform, 168, 196, 40, 40);
            TextMeshProUGUI questionText = A.RawLabel(questionBanner.transform, "QuestionText", "Is this Fit Me Matte + Poreless Foundation by Maybelline?", 30, Color.white, FontRole.Label,
                                                      TextAlignmentOptions.Center, true);
            A.TopStrip(questionText.transform, 14, 92, 28, 28);
            A.ShrinkWrapped(questionText, 22);
            Button questionYes = A.PillButton(questionBanner.transform, "YesButton", "Yes, it is", 28, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.At(questionYes.transform, A.BC, new Vector2(150, 16), new Vector2(270, 68));
            Button questionNo = A.PillButton(questionBanner.transform, "NoButton", "No", 28, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.At(questionNo.transform, A.BC, new Vector2(-150, 16), new Vector2(270, 68));
            questionBanner.gameObject.SetActive(false);      // appears when there is something to ask
            var productQuestion = questionHolder.GetComponent<ScanProductQuestion>();
            A.Set(productQuestion, "banner", questionBanner.gameObject); A.Set(productQuestion, "questionText", questionText);
            A.Set(productQuestion, "yesButton", questionYes); A.Set(productQuestion, "noButton", questionNo);

            // ---- what was found: ONE row of four chips (New13 item 2). A chip shows an empty ring until its thing is found, then a filled
            //      check and what was found. The row has a fixed height, so nothing under it moves. ----
            GameObject chips = A.Node(card, "FoundChips");
            A.BottomStrip(chips.transform, ChipRowY, ChipRowH);
            ScanCheckItem productCheck = Chip(chips.transform, "ProductNameCheck", "Product name", 0);
            ScanCheckItem brandCheck = Chip(chips.transform, "BrandCheck", "Brand", 1);
            ScanCheckItem typeCheck = Chip(chips.transform, "CosmeticTypeCheck", "Cosmetic type", 2);
            ScanCheckItem shadeCheck = Chip(chips.transform, "ShadeCheck", "Shade & texture", 3);
            Image swatch = A.RawPanel(shadeCheck.transform, "ShadeSwatch", new Color32(194, 75, 98, 255), 26f);
            A.At(swatch.transform, A.TR, new Vector2(-8, -8), new Vector2(26, 26));
            A.Outline(swatch.transform, ThemeRole.Gold, 26f);
            swatch.gameObject.SetActive(false);              // shown with the colour of the shade that was found

            // ---- the ingredient list block: HIDDEN until it helps (New13 item 2): after a scan or a photo that found no list. It sits at the
            //      bottom of the camera view; the hint on the picture moves up above it while it shows. The type-it-in sheet has its own look-up. ----
            Image listHelp = A.RawPanel(viewport.transform, "IngredientListHelp", new Color(0, 0, 0, 0.78f), 40f);
            listHelp.raycastTarget = true;
            A.BottomStrip(listHelp.transform, 24, 132, 24, 24);
            GameObject foundList = A.Node(listHelp.transform, "Found_Ingredients");
            A.Share(foundList.transform, 0f, 0.56f, 0, 0, 0);
            ScanCheckItem ingredientsCheck = CheckRow(foundList.transform, "IngredientListCheck", "Ingredient list", 0);
            A.TopStrip(ingredientsCheck.transform, 16, 48, 26, 8);
            TextMeshProUGUI lookUpHint = A.RawLabel(foundList.transform, "LookUpHintText", "No list on the product, or no packaging?", 24, new Color(1, 1, 1, 0.82f), FontRole.Body,
                                                    TextAlignmentOptions.TopLeft, true);
            A.TopStrip(lookUpHint.transform, 68, 56, 26, 8);
            A.ShrinkWrapped(lookUpHint, 18);
            Button lookUp = A.PillButton(listHelp.transform, "LookUpButton", "Look it up", 30, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Ai);
            var lookUpRect = (RectTransform)lookUp.transform;
            lookUpRect.anchorMin = new Vector2(0.58f, 0.5f); lookUpRect.anchorMax = new Vector2(1f, 0.5f); lookUpRect.pivot = A.MC;
            lookUpRect.offsetMin = new Vector2(0, -50); lookUpRect.offsetMax = new Vector2(-20, 50);
            A.Shrink(A.LabelOf(lookUp), 22);
            listHelp.gameObject.SetActive(false);

            // ---- the status line: its own row (two lines at most), above the buttons ----
            TextMeshProUGUI status = A.Label(card, "StatusText", "Tap once to scan. Show one clear product view.", 30,
                                             ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.Center, true);
            A.BottomStrip(status.transform, StatusY, StatusH, 52, 52);
            A.ShrinkWrapped(status, 22);

            // ---- TAP TO SCAN: the round button, and round it the ring of five segments that fills as the product is read ----
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
            TextMeshProUGUI holdLabel = A.Label(disc.transform, "Label", "Tap to scan", 23, ThemeRole.ButtonText, FontRole.Button, TextAlignmentOptions.Center);
            A.At(holdLabel.transform, A.MC, new Vector2(0, -34), new Vector2(HoldSize - 36, 40));
            A.Shrink(holdLabel, 17);
            holdLabel.raycastTarget = false;
            var holdButton = SceneBuilderCore.Ensure<HoldToScanButton>(disc.gameObject);
            A.Set(holdButton, "scaleTarget", disc.rectTransform);
            // a Canvas Group on the button, so the dimming while it is locked is an object you can see in the Hierarchy and Inspector
            A.Set(holdButton, "dimGroup", SceneBuilderCore.Ensure<CanvasGroup>(disc.gameObject));

            // ---- the control bar (New13 item 1): three slots in one row, centred on the scan button.
            //      LEFT: "Type it in" (a real button). CENTRE: the scan button (the capture button in photo mode). RIGHT: one round icon button
            //      that switches mode: the camera (take a photo) in live mode, the live-scan frame in photo mode. Fixed sizes: nothing shifts. ----
            float slot = (Kit.CardWidth - RingSize) * 0.5f - A.Pad - 24f;          // width of a side slot (from the card's padding to the ring's gap)
            Button manualEntry = A.PillButton(card, "ManualEntryButton", "Type it in", 32, ThemeRole.Surface, ThemeRole.MainText, IconFactory.Pencil);
            A.At(manualEntry.transform, A.BL, new Vector2(A.Pad, BarCentre - SideButton * 0.5f), new Vector2(slot, SideButton));
            A.Outline(manualEntry.transform, ThemeRole.Accent, SideButton);
            A.Shrink(A.LabelOf(manualEntry), 24);
            Button photoFallback = A.IconButton(card, "ModeSwitchButton", IconFactory.CameraIcon, SideButton, 60, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(photoFallback.transform, A.BR, new Vector2(-(A.Pad + (slot - SideButton) * 0.5f), BarCentre - SideButton * 0.5f), new Vector2(SideButton, SideButton));
            A.Outline(photoFallback.transform, ThemeRole.Accent, SideButton);
            Image cameraModeIcon = photoFallback.transform.Find("Icon").GetComponent<Image>();
            cameraModeIcon.name = "CameraIcon";
            Image liveModeIcon = A.Icon(photoFallback.transform, "LiveScanIcon", IconFactory.ScanFrame, 60, ThemeRole.MainText);
            liveModeIcon.gameObject.SetActive(false);        // shown in photo mode instead of the camera
            SceneBuilderCore.Ensure<ButtonPressFeedback>(photoFallback.gameObject);

            // ---- Start over (left) and Finish (right): their own row above the bar; each appears when it can be used ----
            Button startOver = A.PillButton(card, "StartOverButton", "Start over", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(startOver.transform, ActionRowY, ActionRowH, A.Pad, Kit.CardWidth * 0.5f + 10f);
            A.Outline(startOver.transform, ThemeRole.Accent, ActionRowH);
            Button finish = A.PillButton(card, "FinishButton", "Finish", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(finish.transform, ActionRowY, ActionRowH, Kit.CardWidth * 0.5f + 10f, A.Pad);
            A.Shrink(A.LabelOf(finish), 22);

            // ---- "Start over": the same product, or a different one? ----
            A.AppPopup startOverPopup = ScanAgainPopup(s, "StartOverPanel", "Keeps the product's name; the pictures are taken anew.",
                                                       "Forgets everything that was read.", out Button sameProduct, out Button differentProduct);

            // ---- "Is it one of these?": up to three products the scan may be about, and a box to type the name ----
            A.AppPopup pick = A.Overlay(s, "ShortlistPanel", "Is it one of these?");
            A.At(pick.box, A.MC, new Vector2(0, 170), new Vector2(940, 1240));
            TextMeshProUGUI pickMessage = A.Label(pick.box, "MessageText", "We are not sure which product this is. Tap it, or type its name.", 28, ThemeRole.SecondaryText, FontRole.Body,
                                                  TextAlignmentOptions.TopLeft, true);
            A.TopStrip(pickMessage.transform, 124, 84, 48, 48);
            A.ShrinkWrapped(pickMessage, 22);
            var pickRows = new Button[3]; var pickNames = new TextMeshProUGUI[3]; var pickDetails = new TextMeshProUGUI[3];
            string[] sampleNames = { "Fit Me Matte + Poreless Foundation", "Fit Me Concealer", "Superstay Lumi-Matte Foundation" };
            string[] sampleDetails = { "Maybelline, Foundation (scanned on this phone before)", "Maybelline, Concealer", "Maybelline, Foundation" };
            for (int i = 0; i < 3; i++)
            {
                Image row = A.Panel(pick.box, "Candidate_" + (i + 1), ThemeRole.SurfaceAlt, 1f, 40f);
                row.raycastTarget = true;
                A.TopStrip(row.transform, 222 + i * 130, 118, 48, 48);
                pickRows[i] = row.gameObject.AddComponent<Button>();
                pickRows[i].targetGraphic = row;
                SceneBuilderCore.Ensure<ButtonPressFeedback>(row.gameObject);
                pickNames[i] = A.Label(row.transform, "NameText", sampleNames[i], 31, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.BottomLeft);
                A.TopStrip(pickNames[i].transform, 12, 50, 34, 84);
                A.Shrink(pickNames[i], 22);
                pickDetails[i] = A.Label(row.transform, "DetailText", sampleDetails[i], 24, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft);
                A.TopStrip(pickDetails[i].transform, 64, 40, 34, 84);
                A.Shrink(pickDetails[i], 18);
                Image arrow = A.Icon(row.transform, "Arrow", IconFactory.ChevronRight, 34, ThemeRole.AccentText);
                A.At(arrow.transform, A.MR, new Vector2(-28, 0), new Vector2(34, 34));
            }
            TextMeshProUGUI pickEmpty = A.Label(pick.box, "EmptyText", "No product of our list fits what the camera read. Type its name below.", 29, ThemeRole.SecondaryText, FontRole.Body,
                                                TextAlignmentOptions.Center, true);
            A.TopStrip(pickEmpty.transform, 222, 378, 70, 70);
            pickEmpty.gameObject.SetActive(false);           // shown in the place of the rows when there is nothing to offer
            TextMeshProUGUI pickTypeTitle = A.Label(pick.box, "TypeItTitle", "Not in the list? Type it:", 28, ThemeRole.Heading, FontRole.Label);
            A.TopStrip(pickTypeTitle.transform, 624, 44, 52, 48);
            TMP_InputField pickName = SceneBuilderCore.MakeInput(pick.box, "NameField", "Product name", A.TC, Vector2.zero, new Vector2(800, 100));
            A.TopStrip(pickName.transform, 676, 100, 48, 48);
            Kit.StyleInput(pickName, "Product name", ThemeRole.InputBackground);
            A.Set(SceneBuilderCore.Ensure<InputFieldBehaviour>(pickName.gameObject), "characterLimit", 80);
            TMP_InputField pickBrand = SceneBuilderCore.MakeInput(pick.box, "BrandField", "Brand (if you know it)", A.TC, Vector2.zero, new Vector2(800, 100));
            A.TopStrip(pickBrand.transform, 788, 100, 48, 48);
            Kit.StyleInput(pickBrand, "Brand (if you know it)", ThemeRole.InputBackground);
            A.Set(SceneBuilderCore.Ensure<InputFieldBehaviour>(pickBrand.gameObject), "characterLimit", 60);
            Button pickUse = A.PillButton(pick.box, "UseTypedButton", "Type the name above", 29, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Check);
            A.TopStrip(pickUse.transform, 904, 92, 48, 48);
            A.Outline(pickUse.transform, ThemeRole.Gold, 92f);
            A.Shrink(A.LabelOf(pickUse), 20);
            Button pickAgain = A.PillButton(pick.box, "ScanAgainButton", "Scan again", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Scan);
            A.BottomStrip(pickAgain.transform, 44, 112, 48, 486);
            A.Outline(pickAgain.transform, ThemeRole.Accent, 112f);
            Button pickNone = A.PillButton(pick.box, "NoneButton", "None of these", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(pickNone.transform, 44, 112, 486, 48);
            A.Shrink(A.LabelOf(pickNone), 22);
            var shortlistView = SceneBuilderCore.Ensure<ScanShortlistView>(pick.root);
            A.Set(shortlistView, "panel", pick.panel); A.Set(shortlistView, "titleText", pick.title); A.Set(shortlistView, "messageText", pickMessage);
            Kit.SetArray(shortlistView, "rows", pickRows); Kit.SetArray(shortlistView, "rowNames", pickNames); Kit.SetArray(shortlistView, "rowDetails", pickDetails);
            A.Set(shortlistView, "emptyText", pickEmpty);
            A.Set(shortlistView, "nameInput", pickName); A.Set(shortlistView, "brandInput", pickBrand);
            A.Set(shortlistView, "useTypedButton", pickUse); A.Set(shortlistView, "useTypedLabel", A.LabelOf(pickUse));
            A.Set(shortlistView, "noneButton", pickNone); A.Set(shortlistView, "noneLabel", A.LabelOf(pickNone)); A.Set(shortlistView, "scanAgainButton", pickAgain);

            // ---- type-it-in pop-up (for labels the camera cannot read) ----
            A.AppPopup manual = A.Overlay(s, "ManualEntryPanel", "Type it in");
            A.TopStrip(manual.box, 110, 1240, 40, 40);
            TextMeshProUGUI manualHint = A.Label(manual.box, "ManualHintText", "No pack at hand? Type the product's name: GlowUp looks its ingredient list up. Or type the list yourself.",
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

            TMP_InputField manualInput = SceneBuilderCore.MakeInput(manual.box, "ManualInputField", "Ingredient list (leave empty to look it up), e.g. Aqua, Glycerin, Niacinamide, Parfum", A.TC, Vector2.zero, new Vector2(800, 430));
            A.TopStrip(manualInput.transform, 444, 440, 48, 48);
            Kit.StyleInput(manualInput, "Ingredient list (leave empty to look it up), e.g. Aqua, Glycerin, Niacinamide, Parfum", ThemeRole.InputBackground);
            MakeMultiLine(manualInput, 32);
            var behaviour = SceneBuilderCore.Ensure<InputFieldBehaviour>(manualInput.gameObject);
            A.Set(behaviour, "characterLimit", 3000);

            TextMeshProUGUI manualFeedback = A.Label(manual.box, "ManualFeedbackText", "", 28, ThemeRole.Warning, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(manualFeedback.transform, 892, 176, 48, 48);       // room for a sentence that says what to do next
            A.ShrinkWrapped(manualFeedback, 22);
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
            // focus lock: a tap on the picture focuses on that spot and holds the focus there (the padlock releases it)
            A.Set(camera, "tapLocksFocus", true);
            A.Set(focusAssistant, "cameraFeed", camera);
            // The text reader needs the small print of an ingredient list sharp. With the first size (1280 x 720) the scan
            // frame held only about 630 x 515 of the camera's pixels (the picture is 720 wide when the phone is upright);
            // Full HD gives it about 950 x 770. A size that was changed in the Inspector is kept.
            SceneBuilderCore.ReplaceNumber(camera, "requestedWidth", 1920, 1280);
            SceneBuilderCore.ReplaceNumber(camera, "requestedHeight", 1080, 720);

            A.Set(controller, "cameraFeed", camera); A.Set(controller, "guide", guide); A.Set(controller, "idlePlaceholder", idle);
            A.Set(controller, "holdButton", holdButton); A.Set(controller, "scanButtonLabel", holdLabel); A.Set(controller, "ring", ring);
            A.Set(controller, "finishButton", finish); A.Set(controller, "finishText", A.LabelOf(finish)); A.Set(controller, "startOverButton", startOver);
            A.Set(controller, "startOverPanel", startOverPopup.panel);
            A.Set(controller, "productQuestion", productQuestion); A.Set(controller, "shortlist", shortlistView);
            A.Set(controller, "sameProductButton", sameProduct); A.Set(controller, "differentProductButton", differentProduct);
            A.Set(controller, "manualEntryButton", manualEntry); A.Set(controller, "photoButton", photoFallback); A.Set(controller, "backButton", s.back);
            A.Set(controller, "statusText", status);
            A.Set(controller, "manualPanel", manual.panel); A.Set(controller, "manualInput", manualInput);
            A.Set(controller, "manualProductInput", manualProduct); A.Set(controller, "manualBrandInput", manualBrand); A.Set(controller, "manualTypeDropdown", manualType);
            A.Set(controller, "manualAnalyseButton", manualAnalyse); A.Set(controller, "manualFeedbackText", manualFeedback);

            // ---- AI photo fallback: "Product not in catalog yet!" ----
            // The pop-up sits low on the screen so the camera picture above it stays visible while the user aims.
            // Its logic is the "Scan Snapshot" object (ScanSnapshotController): every word and number is in its Inspector.
            A.AppPopup snap = A.Overlay(s, "SnapshotPanel", "Not in our catalog yet");
            A.At(snap.box, A.BC, new Vector2(0, 90), new Vector2(940, 660));
            TextMeshProUGUI snapPrompt = A.Label(snap.box, "PromptText", "Product not in catalog yet! Tap to take a quick photo for live AI analysis.", 30,
                                                 ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(snapPrompt.transform, 124, 96, 48, 48);
            A.ShrinkWrapped(snapPrompt, 22);
            TextMeshProUGUI snapStatus = A.Label(snap.box, "StatusText", "Show the front of the pack, or the side with the ingredient list, inside the frame. Good light helps.", 26,
                                                 ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(snapStatus.transform, 232, 150, 48, 48);
            A.ShrinkWrapped(snapStatus, 20);
            Button snapCancel = A.PillButton(snap.box, "CancelButton", "Not now", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(snapCancel.transform, 44, 112, 48, 642);
            A.Outline(snapCancel.transform, ThemeRole.Accent, 112f);
            // "Take a photo of the product" needs the wider button; the label gets smaller instead of running over the icon
            Button snapTake = A.PillButton(snap.box, "TakePhotoButton", "Take a photo of the product", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.CameraIcon);
            A.Shrink(A.LabelOf(snapTake), 22);
            A.BottomStrip(snapTake.transform, 44, 112, 322, 48);
            // the spinner turns at the right end of the button while the AI reads the photo (inactive until then)
            Image snapSpinner = A.Icon(snapTake.transform, "Spinner", IconFactory.Sync, 40, ThemeRole.ButtonText);
            A.At(snapSpinner.transform, A.MR, new Vector2(-34, 0), new Vector2(40, 40));
            snapSpinner.raycastTarget = false;
            snapSpinner.gameObject.SetActive(false);
            var snapController = SceneBuilderCore.Ensure<ScanSnapshotController>(SceneBuilderCore.EnsureRoot("Scan Snapshot"));
            A.Set(snapController, "panel", snap.panel); A.Set(snapController, "promptText", snapPrompt); A.Set(snapController, "statusText", snapStatus);
            A.Set(snapController, "takePhotoButton", snapTake); A.Set(snapController, "takePhotoLabel", A.LabelOf(snapTake));
            A.Set(snapController, "spinner", snapSpinner.rectTransform); A.Set(snapController, "cancelButton", snapCancel);
            A.Set(snapController, "cameraFeed", camera); A.Set(snapController, "frame", (RectTransform)frame.transform);
            A.Set(controller, "snapshot", snapController);
            A.Set(controller, "textOutlines", outlines.GetComponent<TextOutlineOverlay>());
            A.Set(controller, "focusAssistant", focusAssistant); A.Set(controller, "textTrace", traceView);
            A.Set(controller, "scanReadout", readoutBack.gameObject); A.Set(controller, "scanReadoutText", readout);
            // "Got it" stays half a second, not 1.1: the project owner wants a scan of about five seconds (a time set by hand is kept)
            SceneBuilderCore.ReplaceNumber(controller, "recognisedHoldSeconds", 0.5f, 1.1f);
            SceneBuilderCore.ReplaceNumber(controller, "readPictureSize", 900, 1200);
            SceneBuilderCore.ReplaceNumber(controller, "blurrySkipsBeforeReadingAnyway", 2, 6);
            A.Set(controller, "productCheck", productCheck); A.Set(controller, "brandCheck", brandCheck);
            A.Set(controller, "typeCheck", typeCheck); A.Set(controller, "shadeCheck", shadeCheck); A.Set(controller, "shadeSwatch", swatch);
            A.Set(controller, "ingredientsCheck", ingredientsCheck);
            A.Set(controller, "lookUpButton", lookUp); A.Set(controller, "lookUpLabel", A.LabelOf(lookUp));
            A.Set(controller, "listHelp", listHelp.gameObject); A.Set(controller, "guideHint", (RectTransform)hintPill.transform);
            A.Set(controller, "questionBanner", (RectTransform)questionBanner.transform);
            A.Set(controller, "cameraModeIcon", cameraModeIcon.gameObject); A.Set(controller, "liveModeIcon", liveModeIcon.gameObject);
            SceneBuilderCore.ReplaceText(controller, "msgStalled", "Not getting further? Tap Start over, or tap Type it in.", "Not getting further? Tap Start over, or type the product in instead.");
            GlowUp.Scanning.GeminiApiConfig aiConfig = AppTiles.AiConfig();
            if (aiConfig != null) A.Set(controller, "aiConfig", aiConfig);
            A.Set(controller, "aiRecognisesProduct", true);
            A.Set(controller, "aiHelpsEarly", true); A.Set(controller, "aiHelpAfterSeconds", 0.7f);
            A.Set(controller, "aiChecksTheWeb", true);
            A.Set(controller, "closeLooksAtSmallPrint", true);
            A.Set(controller, "smallPrintBelowPixels", 32f); A.Set(controller, "closeLookLetterPixels", 38f);
            A.Set(controller, "aiPictureSize", 1200); A.Set(controller, "looksPerSecond", 10f);
            A.Set(lightAssistant, "cameraFeed", camera);
            // texts of the earlier two-step scan that would name buttons which are gone (wording changed in the Inspector is kept)
            SceneBuilderCore.ReplaceText(controller, "msgAiLooking", "This product is new to GlowUp. Recognising it takes a little longer the first time...", "Recognising the product...");
            SceneBuilderCore.ReplaceText(controller, "hintIngredients", "Turn to the back: show the ingredient list", "Now fit the ingredient list inside the frame");
            SceneBuilderCore.ReplaceText(controller, "hintName", "Now the product's name: hold the front steady, with the name inside the frame", "Keep the product name inside the frame");
            SceneBuilderCore.ReplaceText(controller, "msgAiLimit", "The AI has been asked as often as it may be for now. Type the product's name, or tap Finish anyway.",
                "The AI could not name this product. Type its name instead, or tap Skip and scan the ingredient list.");

            // The scanner is NOT in the row of screens a sideways drag moves through: it is left with its back button
            // (to the dashboard). The SwipeNavigation object is kept in the Hierarchy and does nothing on this screen.
            AppTiles.SwipeNavigation(s, null);

            A.Finish(s);
            Debug.Log("[GlowUp] AiSmartAnalysisScene built. ScanGuide > Frame is the area that gets read; HoldToScan holds the round button (HoldToScanButton) and its ring of five segments " +
                      "(ScanRing: Fill_Front, Fill_Ingredients, Fill_Codes, Fill_Shade, Fill_OtherSides start empty and fill while scanning). ManualEntryPanel, StartOverPanel, ShortlistPanel, CameraViewport > ProductQuestion > QuestionBanner, " +
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
        /// <summary>One chip of the found row (index 0..3 of four equal chips): a soft rounded box, the ring / filled check on the left, the words beside it (two lines at most).</summary>
        static ScanCheckItem Chip(Transform row, string name, string label, int index)
        {
            const float gap = 12f;
            GameObject chip = A.Node(row, name, typeof(ScanCheckItem));
            var rt = (RectTransform)chip.transform;
            rt.anchorMin = new Vector2(index / 4f, 0f); rt.anchorMax = new Vector2((index + 1) / 4f, 1f); rt.pivot = A.MC;
            rt.offsetMin = new Vector2(index == 0 ? 0f : gap * 0.5f, 0f); rt.offsetMax = new Vector2(index == 3 ? 0f : -gap * 0.5f, 0f);
            Image back = A.Panel(chip.transform, "ChipBack", ThemeRole.SurfaceAlt, Kit.PanelAlpha, ChipRowH);
            A.Fill(back.transform);
            back.raycastTarget = false;
            Image glow = A.RawIcon(chip.transform, "Glow", IconFactory.SoftDisc, 70, new Color32(184, 138, 59, 0));
            A.At(glow.transform, A.ML, new Vector2(-4, 0), new Vector2(70, 70));
            Image ring = A.RawIcon(chip.transform, "Ring", IconFactory.RingThin, 34, new Color32(139, 123, 122, 180));
            A.At(ring.transform, A.ML, new Vector2(14, 0), new Vector2(34, 34));
            Image mark = A.RawPanel(chip.transform, "CheckMark", new Color32(184, 138, 59, 255), 34f);
            A.At(mark.transform, A.ML, new Vector2(14, 0), new Vector2(34, 34));
            A.RawIcon(mark.transform, "Check", IconFactory.Check, 20, Color.white);
            mark.gameObject.SetActive(false);                // fills in when the thing is found
            TextMeshProUGUI text = A.Label(chip.transform, "Text", label, 24, ThemeRole.SecondaryText, FontRole.Label, TextAlignmentOptions.MidlineLeft, true);
            A.Fill(text.transform, 56, 4, 12, 4);
            A.ShrinkWrapped(text, 18);
            text.overflowMode = TextOverflowModes.Ellipsis;
            Object.DestroyImmediate(text.GetComponent<ThemedGraphic>());       // ScanCheckItem paints the text itself
            var item = chip.GetComponent<ScanCheckItem>();
            A.Set(item, "ring", ring); A.Set(item, "mark", mark.rectTransform); A.Set(item, "markDisc", mark);
            A.Set(item, "glow", glow); A.Set(item, "text", text); A.Set(item, "label", label);
            return item;
        }

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
