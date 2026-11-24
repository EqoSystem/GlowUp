using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>Menus: GlowUp > Build Scene > 3 Onboarding Step 2 (skin type) and 4 Onboarding Step 3 (skin tone).</summary>
    public static class OnboardingTileBuilders
    {
        const float TileRadius = 80;          // corner radius of every tile (matches PillCorners' default maximum)
        const float Frame = 16;               // thickness of the frame (outer panel) around the inner panel

        // =====================================================================================
        //  Step 2 - skin type
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/3 Onboarding Step 2", false, 113)]
        public static void BuildStep2()
        {
            var scene = SceneBuilderCore.Open("OnboardingStep2Scene");
            var c = BuildChrome(2, "Skin Type Selection");

            const float cellH = 210, gap = 16, wideH = 170;
            var grid = TileGrid(c.card, "SkinTypeTiles", gap);
            RemoveBrokenScripts(grid.gameObject);
            Top(grid, c.card, HeaderBottom, 2 * cellH + gap);
            grid.GetComponent<GridLayoutGroup>().cellSize = new Vector2((InnerWidth - gap) / 2f, cellH);

            // each tile: OuterPanel (frame, subtitle at the bottom) > InnerPanel (skin texture, skin type name inside)
            // the official skin types (23 Nov 2026), in this order: Normal, Dry, Oily, Combination, Sensitive. A tile's VALUE is what is saved;
            // "Mix" stays the saved word of Combination, so old profiles and ingredient rules keep working.
            var normal = SkinTile(grid, "NormalCard", "NormalText", "Normal", "Balanced Skin", SkinTextureFactory.Normal, "Comfortable, few concerns");
            var dry = SkinTile(grid, "DryCard", "DryText", "Dry", "Needs Hydration", SkinTextureFactory.Dry, "Tight, rough or flaky");
            var oily = SkinTile(grid, "OilyCard", "OilyText", "Oily", "Oil Control Needed", SkinTextureFactory.Oily, "Shiny T-zone, visible pores");
            var mix = SkinTile(grid, "MixCard", "MixText", "Mix", "Combination Zones", SkinTextureFactory.Mix, "Oily T-zone, dry cheeks");
            SetTileTitle(mix, "MixText", "Combination");
            // the fifth tile spans the row under the grid
            var sensitive = SkinTile(grid, "SensitiveCard", "SensitiveText", "Sensitive", "Reacts Easily", SkinTextureFactory.Dry, "Stings, itches or flushes easily");
            var sensitiveRoot = sensitive.transform;
            sensitiveRoot.SetParent(c.card, false);
            Top(sensitiveRoot, c.card, HeaderBottom + 2 * cellH + gap + gap, wideH);
            // the five names share one size that fits "Combination" and the short window of a tile
            foreach (var pair in new (SelectableTile tile, string name)[] { (normal, "NormalText"), (dry, "DryText"), (oily, "OilyText"), (mix, "MixText"), (sensitive, "SensitiveText") })
                foreach (TMP_Text t in pair.tile.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == pair.name) { t.fontSize = 46; t.enableAutoSizing = true; t.fontSizeMin = 30; t.fontSizeMax = 46; t.alignment = TextAlignmentOptions.Bottom; }   // below the hint pill a new user sees
            Transform sensitiveBadge = sensitiveRoot.Find("InfoBadge"); if (sensitiveBadge != null) Object.DestroyImmediate(sensitiveBadge.gameObject);   // its window is too short for a hint pill
            var group = SceneBuilderCore.Ensure<TileSelectionGroup>(grid.gameObject);
            SetArray(group, "tiles", new Object[] { normal, dry, oily, mix, sensitive });

            float y = HeaderBottom + 2 * cellH + gap + gap + wideH + 14;
            // panel revision (22 Nov 2026): help is on the page, the user never has to look it up elsewhere
            var finder = HelpLink(c.card, "SkinTypeFinderButton", "Find my skin type", y, true);
            { var fr = (RectTransform)finder.transform; fr.anchorMax = new Vector2(1f, 1f); fr.offsetMax = new Vector2(-fr.offsetMin.x, fr.offsetMax.y); }   // the whole row; the controller halves it while "How it reacts" shows
            // "Where lists come from" was removed (24 Nov 2026): that belongs in the paper, not in the app. Its half of the row now holds "How it reacts"
            // (Sensitive skin only); alone, "Find my skin type" spans the whole row.
            Transform oldSources = Loose(c.card, "SkinListSourcesButton"); if (oldSources != null) Object.DestroyImmediate(oldSources.gameObject);
            var reactionsOpen = HelpLink(c.card, "SensitiveReactionsButton", "How it reacts", y, false);
            reactionsOpen.gameObject.SetActive(false);
            y += 132 + 28;
            var head = TextOn(c.card, "SkinConditionHeaderText", "Chronic Skin Conditions (Optional)", HeaderSize, TextAlignmentOptions.MidlineLeft);
            SetText(head, "Chronic Skin Conditions (Optional)", HeaderSize, TextAlignmentOptions.MidlineLeft);
            Top(head.transform, c.card, y, 56); Role(head.transform, ThemeRole.Heading); y += 58;
            var desc = TextOn(c.card, "SkinConditionHintText", "", HelperSize, TextAlignmentOptions.MidlineLeft);
            SetText(desc, "Tick any that apply, or add your own below.", HelperSize, TextAlignmentOptions.MidlineLeft);
            Top(desc.transform, c.card, y, 44); Role(desc.transform, ThemeRole.SecondaryText); y += 54;

            var panel = ChecklistBuilder.Build(c.card, "ConditionsPanel", y,
                new[] { "Atopic Dermatitis", "Psoriasis", "Rosacea", "Seborrheic Dermatitis", "Allergic Contact Dermatitis", "Irritant Contact Dermatitis", "Hidradenitis Suppurativa", "Lichen Planus", "Chronic Urticaria", "Alopecia Areata", "Vitiligo", "Chronic Prurigo", "Cutaneous Lupus Erythematosus", "Lichen Sclerosus", "Ichthyosis" },
                "Not listed? Add it here", null, null, null, c.feedback);

            // ---- "How does your skin react?": optional, opens when Sensitive is chosen (or from "How it reacts"); one tick row per reaction ----
            Transform oldReact = Loose(c.canvas, "ReactionsPanel");
            if (oldReact != null) Object.DestroyImmediate(oldReact.gameObject);
            AppKit.AppPopup react = AppKit.Overlay(new AppKit.AppScreen { canvas = c.canvas }, "ReactionsPanel", "How does your skin react?");
            AppKit.At(react.box, AppKit.MC, new Vector2(0, 30), new Vector2(900, 1260));
            TextMeshProUGUI reactHint = AppKit.Label(react.box, "HintText", "Optional. Tick any that apply, or skip. It helps GlowUp flag ingredients that may set your skin off.",
                30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            AppKit.TopStrip(reactHint.transform, 130, 100, 48, 48);
            var reactRows = new Object[SensitiveReactions.All.Length]; var reactTicks = new Object[SensitiveReactions.All.Length];
            for (int i = 0; i < SensitiveReactions.All.Length; i++)
            {
                Button row = AppKit.PillButton(react.box, "ReactionRow" + i, SensitiveReactions.All[i].label, 32, ThemeRole.SurfaceAlt, ThemeRole.MainText);
                AppKit.TopStrip(row.transform, 250 + i * 132, 118, 48, 48);
                Transform content = row.transform.Find("Content");
                AppKit.Fill(content, 40, 0, 130, 0);                                           // room on the right for the tick: the words never run under it
                content.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                Image tick = AppKit.Icon(row.transform, "Tick", IconFactory.Check, 56, ThemeRole.Accent);
                AppKit.At(tick.transform, AppKit.TR, new Vector2(-40, -31), new Vector2(56, 56));
                tick.gameObject.SetActive(false);
                reactRows[i] = row; reactTicks[i] = tick.gameObject;
            }
            Button reactSkip = AppKit.PillButton(react.box, "SkipButton", "Skip", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            AppKit.BottomStrip(reactSkip.transform, 44, 108, 48, 462);
            Button reactDone = AppKit.PillButton(react.box, "DoneButton", "Done", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            AppKit.BottomStrip(reactDone.transform, 44, 108, 462, 48);

            var go = SceneBuilderCore.EnsureRoot("OnboardingStep2FlowController");
            var ctl = SceneBuilderCore.Ensure<OnboardingStep2FlowController>(go);
            SceneBuilderCore.SetField(ctl, "skinTypeGroup", group); SceneBuilderCore.SetField(ctl, "conditions", panel);
            SceneBuilderCore.SetField(ctl, "continueButton", c.cont); SceneBuilderCore.SetField(ctl, "continueAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);
            SceneBuilderCore.SetField(ctl, "finderButton", finder);
            SceneBuilderCore.SetField(ctl, "reactionsButton", reactionsOpen); SceneBuilderCore.SetField(ctl, "reactionsPanel", react.panel);
            SetArray(ctl, "reactionRows", reactRows); SetArray(ctl, "reactionTicks", reactTicks);
            SceneBuilderCore.SetField(ctl, "reactionsDone", reactDone); SceneBuilderCore.SetField(ctl, "reactionsSkip", reactSkip);

            FinishScene(); SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] OnboardingStep2Scene built. Tile = OuterPanel (frame) > InnerPanel (FaceTexture + skin type name); the subtitle sits in the frame; InfoBadge is a hint for new users.");
        }

        /// <summary>A quiet outlined button on one half of the row at `y` (132 units tall = 44 dp), for help that opens a sheet.</summary>
        static Button HelpLink(Transform card, string name, string label, float y, bool left)
        {
            Button b = SceneBuilderCore.MakeButton(card, name, label, HelperSize, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(100, 132), Color.white);
            var rt = (RectTransform)b.transform;
            if (rt.parent != card) rt.SetParent(card, false);
            rt.anchorMin = new Vector2(left ? 0f : 0.5f, 1f); rt.anchorMax = new Vector2(left ? 0.5f : 1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left ? PadX : 8f, -y - 132f); rt.offsetMax = new Vector2(left ? -8f : -PadX, -y);
            var img = b.GetComponent<Image>(); StyleRounded(img); SetRadius(img, 66f);
            SceneBuilderCore.Themed(b.gameObject, ThemeRole.InputBackground);
            EnsureOutline(b.transform, "Outline", ThemeRole.Accent, 66f);
            var text = b.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label; text.fontSize = HelperSize; text.fontStyle = FontStyles.Bold; text.enableWordWrapping = true; text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.offsetMin = new Vector2(20, 6); text.rectTransform.offsetMax = new Vector2(-20, -6);
            Role(text.transform, ThemeRole.AccentText);
            return b;
        }

        // =====================================================================================
        //  Step 3 - skin tone
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/4 Onboarding Step 3", false, 114)]
        public static void BuildStep3()
        {
            var scene = SceneBuilderCore.Open("OnboardingStep3Scene");
            var c = BuildChrome(3, "Skin Tone Selection");

            const float gap = 20;
            var grid = TileGrid(c.card, "SkinToneTiles", gap);
            RemoveBrokenScripts(grid.gameObject);
            TopBottom(grid, c.card, HeaderBottom, FooterTop + 150);                // the six tiles share the height that is left (the help row sits under them)
            grid.GetComponent<GridLayoutGroup>().cellSize = new Vector2((InnerWidth - gap) / 2f, 388);
            var fit = SceneBuilderCore.Ensure<GridFitCells>(grid.gameObject);
            SceneBuilderCore.SetField(fit, "columns", 2); SceneBuilderCore.SetField(fit, "rows", 3);
            SceneBuilderCore.SetField(fit, "minCellHeight", 240f); SceneBuilderCore.SetField(fit, "maxCellHeight", 470f);

            // each tile: OuterPanel (frame, description at the bottom) > InnerPanel (solid skin tone, tone name inside)
            var fair = ToneTile(grid, "FairCard", "FairText", "Fair", "Very light, burns easily", Hex("#F7F4E2"), "Panel");
            var light = ToneTile(grid, "LightCard", "LightText", "Light", "Light, tans slowly", Hex("#F2DEC9"), "Panel (3)");
            var medium = ToneTile(grid, "MediumCard", "MediumText", "Medium", "Golden, tans easily", Hex("#D4A86F"), "Panel (1)");
            var tan = ToneTile(grid, "TanCard", "TanText", "Tan", "Warm, rarely burns", Hex("#B69772"), "Panel (4)");
            var deep = ToneTile(grid, "DeepCard", "DeepText", "Deep", "Rich, almost never burns", Hex("#9D855D"), "Panel (2)");
            var dark = ToneTile(grid, "DarkCard", "DarkText", "Dark", "Deep, very rarely burns", Hex("#5F453B"), "Panel (5)");
            var group = SceneBuilderCore.Ensure<TileSelectionGroup>(grid.gameObject);
            SetArray(group, "tiles", new Object[] { fair, light, medium, tan, deep, dark });

            // ---- 22 Nov 2026 (panel): the help row under the tiles: a selfie suggests the tone, a sheet says where the tones come from ----
            Button selfieOpen = HelpLink(c.card, "SelfieToneButton", "Find my tone with a selfie", 0, true);
            // 24 Nov 2026: "Where tones come from" is gone (it belongs in the paper's literature review, docs/SOURCES_REGISTER.md); the selfie button takes the whole row
            Transform oldToneSources = Loose(c.card, "SkinToneSourcesButton");
            if (oldToneSources != null) Object.DestroyImmediate(oldToneSources.gameObject);
            {
                var hr = (RectTransform)selfieOpen.transform;
                hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(0.5f, 0f);
                hr.offsetMin = new Vector2(PadX, FooterTop + 8f); hr.offsetMax = new Vector2(-PadX, FooterTop + 8f + 132f);
                Transform oldIcon = selfieOpen.transform.Find("SelfieIcon"); if (oldIcon != null) Object.DestroyImmediate(oldIcon.gameObject);
                Image cam = Icon(selfieOpen.transform, "SelfieIcon", IconFactory.CameraIcon, 54);
                var cr = cam.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f); cr.pivot = new Vector2(0.5f, 0.5f); cr.anchoredPosition = new Vector2(80f, 0f);
                SceneBuilderCore.Themed(cam.gameObject, ThemeRole.AccentText);
                var camText = selfieOpen.GetComponentInChildren<TextMeshProUGUI>(); camText.rectTransform.offsetMin = new Vector2(130, 6); camText.rectTransform.offsetMax = new Vector2(-60, -6);
            }

            Transform oldSelfie = Loose(c.canvas, "SelfiePanel");
            if (oldSelfie != null) Object.DestroyImmediate(oldSelfie.gameObject);
            AppKit.AppPopup selfie = AppKit.Overlay(new AppKit.AppScreen { canvas = c.canvas }, "SelfiePanel", "Find my tone");
            AppKit.At(selfie.box, AppKit.MC, new Vector2(0, 20), new Vector2(920, 1500));
            // (24 Nov 2026) the live view is the AI Smart Scan screen's camera component on the FRONT camera, without the scan grid or the moving line.
            // Bands from the top: viewport 130-890 | status 900-990 | result row 1000-1130 | delete link 1150-1230 | the two buttons at the bottom.
            Image viewport = AppKit.RawPanel(selfie.box, "CameraViewport", new Color32(30, 26, 25, 255), 48f);
            AppKit.TopStrip(viewport.transform, 130, 760, 48, 48);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            GameObject mirror = AppKit.Node(viewport.transform, "MirrorRoot");
            AppKit.Fill(mirror.transform);
            GameObject displayObject = AppKit.Node(mirror.transform, "CameraDisplay", typeof(RawImage));
            AppKit.Fill(displayObject.transform);
            var cameraDisplay = displayObject.GetComponent<RawImage>();
            cameraDisplay.color = new Color(0, 0, 0, 0); cameraDisplay.raycastTarget = false;          // invisible until the camera runs
            GameObject resultObject = AppKit.Node(viewport.transform, "ResultImage", typeof(RawImage));
            AppKit.Fill(resultObject.transform);
            var resultImage = resultObject.GetComponent<RawImage>(); resultImage.raycastTarget = false;
            resultObject.SetActive(false);
            // the box on the frozen photo: the part of the face that was read (placed by SkinToneSelfie)
            Image sampleBox = AppKit.Panel(viewport.transform, "SampleBox", ThemeRole.Gold, 0.12f, 16f);
            sampleBox.raycastTarget = false;
            AppKit.Outline(sampleBox.transform, ThemeRole.Gold, 16f);
            sampleBox.gameObject.SetActive(false);
            TextMeshProUGUI selfieStatus = AppKit.Label(selfie.box, "StatusText", "", 30, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            AppKit.TopStrip(selfieStatus.transform, 902, 90, 56, 56);
            AppKit.ShrinkWrapped(selfieStatus, 22);
            // how the tone was set: the colour found in the box, then the nearest of the six tones
            GameObject resultRow = AppKit.Node(selfie.box, "ResultRow");
            AppKit.TopStrip(resultRow.transform, 1004, 128, 48, 48);
            Image foundSwatch = AppKit.Panel(resultRow.transform, "FoundSwatch", ThemeRole.SurfaceAlt, 1f, 56f);
            AppKit.At(foundSwatch.transform, AppKit.TL, new Vector2(0, -10), new Vector2(108, 108));
            AppKit.Outline(foundSwatch.transform, ThemeRole.Accent, 56f);
            TextMeshProUGUI foundLabel = AppKit.Label(resultRow.transform, "FoundLabel", "Skin colour found", 28, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.MidlineLeft, true);
            AppKit.Fill(foundLabel.transform, 124, 0, 430, 0);
            Image toneSwatch = AppKit.Panel(resultRow.transform, "ToneSwatch", ThemeRole.SurfaceAlt, 1f, 56f);
            AppKit.At(toneSwatch.transform, AppKit.TL, new Vector2(424, -10), new Vector2(108, 108));
            AppKit.Outline(toneSwatch.transform, ThemeRole.Accent, 56f);
            TextMeshProUGUI toneLabel = AppKit.Label(resultRow.transform, "ToneLabel", "Closest tone", 28, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.MidlineLeft, true);
            AppKit.Fill(toneLabel.transform, 548, 0, 0, 0);
            foreach (Image swatch in new[] { foundSwatch, toneSwatch }) { var themed = swatch.GetComponent<ThemedGraphic>(); if (themed != null) Object.DestroyImmediate(themed); }   // their colour is set from the photo
            resultRow.SetActive(false);
            Button deleteSelfie = AppKit.LinkButton(selfie.box, "DeleteSelfieButton", "Delete my selfie", 30, ThemeRole.Warning);
            AppKit.TopStrip(deleteSelfie.transform, 1150, 76, 48, 48);
            Button selfieClose = AppKit.PillButton(selfie.box, "CloseButton", "Close", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            AppKit.BottomStrip(selfieClose.transform, 44, 108, 48, 462);
            Button selfieTake = AppKit.PillButton(selfie.box, "TakePhotoButton", "Take photo", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.CameraIcon);
            AppKit.BottomStrip(selfieTake.transform, 44, 108, 462, 48);

            Transform oldConsent = Loose(c.canvas, "SelfieConsentPanel");
            if (oldConsent != null) Object.DestroyImmediate(oldConsent.gameObject);
            AppKit.AppPopup consent = AppKit.Overlay(new AppKit.AppScreen { canvas = c.canvas }, "SelfieConsentPanel", "Use a selfie?");
            AppKit.At(consent.box, AppKit.MC, new Vector2(0, 60), new Vector2(900, 760));
            TextMeshProUGUI consentText = AppKit.Label(consent.box, "MessageText", "Agree first.", 32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            AppKit.Fill(consentText.transform, 48, 190, 48, 130);
            AppKit.ShrinkWrapped(consentText, 26);
            Button consentCancel = AppKit.PillButton(consent.box, "CancelButton", "Cancel", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            AppKit.BottomStrip(consentCancel.transform, 44, 108, 48, 462);
            Button consentAgree = AppKit.PillButton(consent.box, "AgreeButton", "I agree", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            AppKit.BottomStrip(consentAgree.transform, 44, 108, 462, 48);
            Object.DestroyImmediate(consentAgree.GetComponent<ThemedGraphic>());                 // ConfirmDialog paints this button itself
            Object.DestroyImmediate(AppKit.LabelOf(consentAgree).GetComponent<ThemedGraphic>());
            var consentDialog = SceneBuilderCore.Ensure<GlowUp.AppSettings.ConfirmDialog>(SceneBuilderCore.EnsureRoot("SelfieConsentDialog"));
            SceneBuilderCore.SetField(consentDialog, "panel", consent.panel); SceneBuilderCore.SetField(consentDialog, "titleText", consent.title);
            SceneBuilderCore.SetField(consentDialog, "messageText", consentText);
            SceneBuilderCore.SetField(consentDialog, "confirmButton", consentAgree); SceneBuilderCore.SetField(consentDialog, "confirmLabel", AppKit.LabelOf(consentAgree));
            SceneBuilderCore.SetField(consentDialog, "cancelButton", consentCancel); SceneBuilderCore.SetField(consentDialog, "cancelLabel", AppKit.LabelOf(consentCancel));

            var selfieTone = SceneBuilderCore.Ensure<SkinToneSelfie>(SceneBuilderCore.EnsureRoot("SkinToneSelfie"));
            SceneBuilderCore.SetField(selfieTone, "openButton", selfieOpen); SceneBuilderCore.SetField(selfieTone, "toneGroup", group);
            SceneBuilderCore.SetField(selfieTone, "panel", selfie.panel);
            var selfieCamera = SceneBuilderCore.Ensure<GlowUp.Scan.CameraFeedController>(selfieTone.gameObject);
            SceneBuilderCore.SetField(selfieCamera, "display", cameraDisplay); SceneBuilderCore.SetField(selfieCamera, "viewport", viewport.rectTransform);
            SceneBuilderCore.SetField(selfieCamera, "mirrorRoot", mirror.transform); SceneBuilderCore.SetField(selfieCamera, "useFrontCamera", true);
            AdminScenesBuilder.FocusLockControl(viewport.transform, selfieCamera);          // the padlock of the AI Smart Scan camera, top centre (manual focus)
            SceneBuilderCore.SetField(selfieTone, "cameraFeed", selfieCamera); SceneBuilderCore.SetField(selfieTone, "resultImage", resultImage);
            SceneBuilderCore.SetField(selfieTone, "sampleBox", sampleBox.rectTransform); SceneBuilderCore.SetField(selfieTone, "resultRow", resultRow);
            SceneBuilderCore.SetField(selfieTone, "foundSwatch", foundSwatch); SceneBuilderCore.SetField(selfieTone, "toneSwatch", toneSwatch);
            SceneBuilderCore.SetField(selfieTone, "foundLabel", foundLabel); SceneBuilderCore.SetField(selfieTone, "toneLabel", toneLabel);
            SceneBuilderCore.SetField(selfieTone, "statusText", selfieStatus); SceneBuilderCore.SetField(selfieTone, "takeButton", selfieTake);
            SceneBuilderCore.SetField(selfieTone, "closeButton", selfieClose); SceneBuilderCore.SetField(selfieTone, "deleteButton", deleteSelfie);
            SceneBuilderCore.SetField(selfieTone, "consentDialog", consentDialog);

            var go = SceneBuilderCore.EnsureRoot("OnboardingStep3FlowController");
            var ctl = SceneBuilderCore.Ensure<OnboardingStep3FlowController>(go);
            SceneBuilderCore.SetField(ctl, "skinToneGroup", group);
            SceneBuilderCore.SetField(ctl, "continueButton", c.cont); SceneBuilderCore.SetField(ctl, "continueAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);

            FinishScene(); SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] OnboardingStep3Scene built. Tile = OuterPanel (frame) > InnerPanel (tone colour) > tone name; SubtitleText = description in the frame.");
        }

        // =====================================================================================
        //  Grid
        // =====================================================================================
        static RectTransform TileGrid(Transform card, string name, float gap)
        {
            Transform existing = card.Find(name);
            if (existing == null) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(card, false); existing = go.transform; }
            var g = SceneBuilderCore.Ensure<GridLayoutGroup>(existing.gameObject);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = 2;
            g.spacing = new Vector2(gap, gap); g.padding = new RectOffset(0, 0, 0, 0);
            g.startCorner = GridLayoutGroup.Corner.UpperLeft; g.startAxis = GridLayoutGroup.Axis.Horizontal;
            g.childAlignment = TextAnchor.UpperCenter;
            return (RectTransform)existing;
        }

        // =====================================================================================
        //  Shared tile base
        // =====================================================================================
        class TileParts { public GameObject root; public Image outline; public Image clip; public Transform cells; public SelectableTile tile; }

        /// <summary>root [Button, SelectableTile, PixelSweepTileAnimator] > NeonOutline, OuterPanel [Mask] > PixelSweepCells</summary>
        static TileParts BaseTile(Transform grid, string cardName, string value, string clipName)
        {
            var existing = SceneBuilderCore.FindInScene(cardName);
            GameObject root = existing != null ? existing : new GameObject(cardName, typeof(RectTransform));
            if (root.transform.parent != grid) root.transform.SetParent(grid, false);
            root.transform.SetAsLastSibling();                       // grid order = the order the tiles are built in
            root.transform.localScale = Vector3.one;

            var rootImg = SceneBuilderCore.Ensure<Image>(root);
            rootImg.sprite = Round; rootImg.type = Image.Type.Sliced; rootImg.color = new Color(0, 0, 0, 0.004f);   // invisible tap area
            var btn = SceneBuilderCore.Ensure<Button>(root);
            btn.targetGraphic = rootImg; btn.transition = Selectable.Transition.None;
            ClearClicks(btn);
            RetireLegacy(root);   // old SkinTypeCard / BaselineCard / CardHoverEffect scripts: disabled, not removed

            var p = new TileParts { root = root };

            // neon ring + glow, slightly larger than the tile; invisible until the tile is selected
            Transform outlineT = root.transform.Find("NeonOutline");
            if (outlineT == null) { var g = new GameObject("NeonOutline", typeof(RectTransform), typeof(Image)); g.transform.SetParent(root.transform, false); outlineT = g.transform; }
            outlineT.SetAsFirstSibling();
            p.outline = SceneBuilderCore.Ensure<Image>(outlineT.gameObject);
            p.outline.sprite = IconFactory.NeonOutline; p.outline.type = Image.Type.Sliced; p.outline.raycastTarget = false; p.outline.color = Color.clear;
            float halo = IconFactory.NeonOutlineMargin * TileRadius / PillCorners.SpriteRadius;
            Stretch(outlineT, -halo, -halo, -halo, -halo);
            var opc = SceneBuilderCore.Ensure<PillCorners>(outlineT.gameObject);
            SceneBuilderCore.SetField(opc, "spriteMargin", IconFactory.NeonOutlineMargin);
            SceneBuilderCore.SetField(opc, "maxRadius", TileRadius);

            // the tile body; it also masks its children to the rounded shape
            Transform clipT = root.transform.Find(clipName);
            if (clipT == null) clipT = root.transform.Find("Clip");                 // name used by earlier layouts
            if (clipT == null) { var g = new GameObject(clipName, typeof(RectTransform), typeof(Image)); g.transform.SetParent(root.transform, false); clipT = g.transform; }
            clipT.name = clipName;
            clipT.SetSiblingIndex(1);
            Stretch(clipT, 0, 0, 0, 0);
            p.clip = SceneBuilderCore.Ensure<Image>(clipT.gameObject);
            StyleRounded(p.clip); SetRadius(p.clip, TileRadius); p.clip.raycastTarget = false;
            var mask = SceneBuilderCore.Ensure<Mask>(clipT.gameObject); mask.showMaskGraphic = true;

            Transform cellsT = clipT.Find("PixelSweepCells");
            if (cellsT == null) { var g = new GameObject("PixelSweepCells", typeof(RectTransform)); g.transform.SetParent(clipT, false); cellsT = g.transform; }
            Stretch(cellsT, 0, 0, 0, 0);
            p.cells = cellsT;

            var anim = SceneBuilderCore.Ensure<PixelSweepTileAnimator>(root);
            SceneBuilderCore.SetField(anim, "cellContainer", (RectTransform)cellsT);
            SceneBuilderCore.SetField(anim, "neonOutline", p.outline);
            p.tile = SceneBuilderCore.Ensure<SelectableTile>(root);
            SceneBuilderCore.SetField(p.tile, "value", value);
            SceneBuilderCore.SetField(p.tile, "animator", anim);
            return p;
        }

        /// <summary>A text that belongs to a tile: reused by name anywhere inside the tile, placed under `parent`, with its own fixed colour.</summary>
        static TextMeshProUGUI TileText(Transform tileRoot, Transform parent, string name, string text, float size, Color color)
        {
            Transform ex = Loose(tileRoot, name);
            TextMeshProUGUI tmp = ex != null ? ex.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null) tmp = NewText(parent, name, text);
            if (tmp.transform.parent != parent) tmp.transform.SetParent(parent, false);
            tmp.transform.localScale = Vector3.one;
            SetText(tmp, text, size, TextAlignmentOptions.Center);
            // Overflow, not Ellipsis: with Ellipsis, TextMeshPro hides the whole line when the box is shorter than the font's line height
            tmp.color = color; tmp.raycastTarget = false; tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            var themed = tmp.GetComponent<ThemedGraphic>();              // these texts sit on an image, so the theme must not recolour them
            if (themed != null) Object.DestroyImmediate(themed);
            return tmp;
        }

        static Image ChildImage(Transform parent, string name)
        {
            Transform ex = parent.Find(name);
            if (ex == null) { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); ex = go.transform; }
            var img = SceneBuilderCore.Ensure<Image>(ex.gameObject);
            img.raycastTarget = false;
            return img;
        }

        // =====================================================================================
        //  Framed tile (the composition shared by the skin-type and skin-tone tiles):
        //    OUTER panel = the frame / outline, with the short description in its bottom strip
        //    INNER panel = the picture or the colour, with the title centred inside it
        // =====================================================================================
        class FramedParts { public TileParts tile; public Image inner; public TextMeshProUGUI title, description; }

        static FramedParts FramedTile(Transform grid, string cardName, string titleName, string title, string descriptionName, string description,
            string legacyPanelName, float descriptionZone)
        {
            var p = BaseTile(grid, cardName, title, "OuterPanel");
            var root = p.root.transform;
            var outer = p.clip.transform;

            // objects generated by earlier layouts of this tile that are no longer part of it
            foreach (var n in new[] { "Swatch", "LabelPlate" }) { var o = outer.Find(n); if (o != null) Object.DestroyImmediate(o.gameObject); }

            // OUTER panel: the frame
            SetAlpha(p.clip, 1f);
            SceneBuilderCore.Themed(p.clip.gameObject, ThemeRole.SurfaceAlt);

            // INNER panel (the scene's own swatch panel is reused when it is still there)
            Transform innerT = outer.Find("InnerPanel");
            if (innerT == null)
            {
                var legacy = legacyPanelName != null ? SceneBuilderCore.FindInScene(legacyPanelName) : null;
                if (legacy != null) { legacy.name = "InnerPanel"; legacy.transform.SetParent(outer, false); innerT = legacy.transform; }
                else { var g = new GameObject("InnerPanel", typeof(RectTransform), typeof(Image)); g.transform.SetParent(outer, false); innerT = g.transform; }
            }
            innerT.localScale = Vector3.one;
            var inner = SceneBuilderCore.Ensure<Image>(innerT.gameObject);
            var themedInner = inner.GetComponent<ThemedGraphic>(); if (themedInner != null) Object.DestroyImmediate(themedInner);   // a skin tone / photo is never re-themed
            StyleRounded(inner); SetRadius(inner, TileRadius - Frame);
            inner.raycastTarget = false;
            var irt = (RectTransform)innerT; irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.pivot = new Vector2(0.5f, 0.5f);
            irt.offsetMin = new Vector2(Frame, descriptionZone); irt.offsetMax = new Vector2(-Frame, -Frame);
            innerT.SetSiblingIndex(0);
            p.cells.SetAsLastSibling();

            // title: centred in the inner panel
            var t = TileText(root, innerT, titleName, title, 56, Color.white);
            Stretch(t.transform, 8, 8, 8, 8);

            // description: centred in the frame's bottom strip
            var s = TileText(root, root, descriptionName, description, 30, Color.white);
            s.enableAutoSizing = true; s.fontSizeMin = 22; s.fontSizeMax = 30;
            var srt = s.rectTransform; srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.anchoredPosition = new Vector2(0, 16); srt.sizeDelta = new Vector2(-2 * Frame - 8, descriptionZone - 28);
            SceneBuilderCore.Themed(s.gameObject, ThemeRole.MainText);
            s.transform.SetAsLastSibling();

            return new FramedParts { tile = p, inner = inner, title = t, description = s };
        }

        // =====================================================================================
        //  Skin type tile: the same composition as the skin-tone tile, with the close-up skin
        //  texture shown inside the INNER panel instead of a solid colour
        // =====================================================================================
        /// <summary>Changes only the words a tile shows (its saved value stays what the tile was made with).</summary>
        static void SetTileTitle(SelectableTile tile, string textObjectName, string words)
        {
            foreach (TMP_Text t in tile.GetComponentsInChildren<TMP_Text>(true)) if (t.name == textObjectName) t.text = words;
        }

        static SelectableTile SkinTile(Transform grid, string cardName, string titleName, string title, string subtitle, Sprite face, string badgeText)
        {
            GameObject existingRoot = SceneBuilderCore.FindInScene(cardName);
            string subtitleName = existingRoot != null && Loose(existingRoot.transform, "DescText") != null ? "DescText" : "SubtitleText";   // the scene's own subtitle object
            var f = FramedTile(grid, cardName, titleName, title, subtitleName, subtitle, null, 96);
            var root = f.tile.root.transform;
            var innerT = f.inner.transform;

            // INNER panel: a round window (mask) that shows the skin texture
            f.inner.color = Color.white;
            var mask = SceneBuilderCore.Ensure<Mask>(innerT.gameObject); mask.showMaskGraphic = true;

            Transform textureT = Loose(root, "FaceTexture");                 // made by an earlier layout: moved here, not duplicated
            Image texture;
            if (textureT != null) { textureT.SetParent(innerT, false); texture = SceneBuilderCore.Ensure<Image>(textureT.gameObject); texture.raycastTarget = false; }
            else texture = ChildImage(innerT, "FaceTexture");
            texture.transform.localScale = Vector3.one;
            texture.sprite = face; texture.type = Image.Type.Simple; texture.preserveAspect = false; texture.color = Color.white;
            Stretch(texture.transform, 0, 0, 0, 0); texture.transform.SetSiblingIndex(0);

            // a soft dark veil over the texture, so the white title stays readable on any photo
            var veil = ChildImage(innerT, "TextureVeil");
            veil.sprite = null; veil.color = new Color(0.16f, 0.11f, 0.10f, 0.30f);
            Stretch(veil.transform, 0, 0, 0, 0); veil.transform.SetSiblingIndex(1);

            f.title.color = Color.white;
            f.title.transform.SetAsLastSibling();

            // info badge: a small pill that hugs its text, at the top of the inner panel (only shown to new / struggling users)
            Transform badge = root.Find("InfoBadge");
            if (badge == null) { var b = new GameObject("InfoBadge", typeof(RectTransform), typeof(CanvasGroup)); b.transform.SetParent(root, false); badge = b.transform; }
            badge.localScale = Vector3.one;
            AutoPill(badge, badgeText, 20, new Color(0.14f, 0.11f, 0.11f, 0.72f), Color.white, 18, 4);
            var brt = (RectTransform)badge; brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.anchoredPosition = new Vector2(0, -(Frame + 6));
            SceneBuilderCore.Ensure<HintGroup>(badge.gameObject);
            badge.SetAsLastSibling();
            return f.tile.tile;
        }

        // =====================================================================================
        //  Skin tone tile: OUTER panel = frame / outline (description at the bottom),
        //                  INNER panel = the solid skin tone (tone name inside)
        // =====================================================================================
        static SelectableTile ToneTile(Transform grid, string cardName, string titleName, string title, string description, Color tone, string legacyPanelName)
        {
            var f = FramedTile(grid, cardName, titleName, title, "SubtitleText", description, legacyPanelName, 96);
            var root = f.tile.root.transform;

            // objects generated by earlier layouts of this tile that are no longer part of it
            var oldTexture = f.tile.clip.transform.Find("FaceTexture"); if (oldTexture != null) Object.DestroyImmediate(oldTexture.gameObject);
            var oldBadge = root.Find("InfoBadge"); if (oldBadge != null) Object.DestroyImmediate(oldBadge.gameObject);

            f.inner.color = tone;
            f.title.color = Luminance(tone) > 0.55f ? Hex("#3A2E2D") : Color.white;      // dark on light tones, white on dark tones
            return f.tile.tile;
        }

        /// <summary>
        /// Removes the empty "Missing Script" slots that earlier builds left on the tile group object. (The group's
        /// script used to share a file with SelectableTile, and Unity cannot keep such a script attached in a saved
        /// scene; each build left one more empty slot.) Only slots that have no script are removed.
        /// </summary>
        static void RemoveBrokenScripts(GameObject go)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            if (removed > 0) Debug.Log($"[GlowUp] {go.name}: removed {removed} empty script slot(s) left by earlier builds of this scene.");

            // the same earlier builds could also leave more than one Tile Selection Group: the first is kept (it is the one the scene uses)
            TileSelectionGroup[] groups = go.GetComponents<TileSelectionGroup>();
            for (int i = groups.Length - 1; i >= 1; i--) Object.DestroyImmediate(groups[i]);
            if (groups.Length > 1) Debug.Log($"[GlowUp] {go.name}: removed {groups.Length - 1} extra Tile Selection Group component(s).");
        }

        static Color Hex(string h) => SceneBuilderCore.Hex(h);
        static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
