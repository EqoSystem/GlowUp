using GlowUp.Common;
using GlowUp.Result;
using GlowUp.Scan;
using GlowUp.Theme;
using GlowUp.Views;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 12 AI Scan Result (also run by GlowUp > Build Scene > All Scenes and All App Scenes).
    /// One analysed product, each fact in ONE place, most important first:
    ///   1. the product card: who it is, the verdict (pill + one line), "See safer alternatives" for a flagged result,
    ///      PENDING APPROVAL when it applies, the FDA Philippines badge (its details open under it), and one direct
    ///      verification-portal button;
    ///   2. how it matches the user (four lines);
    ///   3. key ingredients (an icon AND a colour per level), "See all ingredients", and "Where this list came from"
    ///      (closed: the source line, the second-source line, the pages as labelled buttons and what each label means);
    ///   4. "Why this result?": the long text, behind a button;
    ///   5. shades that suit the user; 6. the advice to see a dermatologist (pink only when something is flagged).
    /// The Alternatives button is at the top, Scan again / AR Try-On at the bottom.
    ///
    /// Made for easy reading (ADHD, OCD): every button and tappable row is at least Tap high (about 48 dp on a phone),
    /// no text is smaller than LeastSize, long text and details stay closed until asked for, buttons that leave the app
    /// carry the "opens a website" icon, and blocks that depend on the answer keep their place instead of jumping.
    /// </summary>
    public static class ScanResultSceneBuilder
    {
        // ---- sizes, in canvas units (the canvas is 1080 wide: on a phone one unit is about 0.33 to 0.38 dp) ----
        const float Tap = 132;              // every button and tappable row: about 44 to 50 dp (Android asks for at least 48 dp)
        const float ChipHeight = 120;       // an ingredient chip and a source button (two lines fit)
        const float HeadingSize = 46;       // a section's heading
        const float NameSize = 52;          // the product's name (wraps onto a second line instead of shrinking)
        const float BodySize = 38;          // sentences
        const float SmallSize = 32;         // second lines, hints, captions
        const float ButtonSize = 38;        // words on a button
        const float LeastSize = 28;         // no text ever gets smaller than this (auto-size floor)

        [MenuItem("GlowUp/Build Scene/12 AI Scan Result", false, 122)]
        public static void Build() { BuildScene("AiSmartAnalysisResultScene", "Your Result", null); }

        /// <summary>
        /// Builds a result screen. `extraCards` (may be null) adds cards right under the product card (the verdict), before
        /// "How it matches you": the Web Search Result screen puts its brand, product, sources and shades cards there.
        /// </summary>
        public static void BuildScene(string sceneName, string title, System.Action<A.AppScreen, Transform> extraCards)
        {
            A.AppScreen s = A.Begin(sceneName, title, true, "ResultCard", "AiSmartAnalysisResultCard");
            Transform card = s.card;

            // ---- header: the title moves left to make room for Alternatives ----
            s.title.alignment = TextAlignmentOptions.MidlineLeft;
            A.TopStrip(s.title.transform, 30, 96, 136, 344);
            A.Shrink(s.title, 40);                                              // a longer title gets smaller, never under the Alternatives button
            Button alternatives = A.PillButton(card, "AlternativesButton", "Alternatives", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Swap);
            A.At(alternatives.transform, A.TR, new Vector2(-28, -30), new Vector2(304, 100));
            A.Outline(alternatives.transform, ThemeRole.Accent, 100f);
            TouchArea(alternatives.transform, 304, Tap);

            // ---- the scrolling result ----
            GameObject area = A.Node(card, "ResultArea");
            A.Between(area.transform, A.HeaderBottom, 200);
            A.Scroll(area.transform, "ResultScrollView", out RectTransform content, 24, 0, 18, 4, 30);

            // 1. the product: who it is, the verdict (pill + one line of reason) and the FDA Philippines badge.
            //    The card grows with its content. (The one-line summary used to sit in a card of its own under this one.)
            GameObject product = A.Node(content, "ProductCard");
            A.VStack(product, 20, 28, 28, 28, 28);
            A.Backdrop(product.transform, ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);

            // the head: type icon | brand, name, type, date | rename. A row, so a long name wraps instead of shrinking.
            GameObject head = A.Node(product.transform, "ProductHead");
            A.HStack(head, 24, 0, 0, 0, 0, TextAnchor.UpperLeft);
            Image tile = A.Panel(head.transform, "IconTile", ThemeRole.InputBackground, 1f, 124f);
            A.Size(tile, 124, 124);
            Image typeIcon = A.Icon(tile.transform, "TypeIcon", IconFactory.Bottle, 70, ThemeRole.MainText);
            GameObject headTexts = A.Node(head.transform, "Texts");
            A.VStack(headTexts, 6);
            A.Size(headTexts.transform, -1, -1, 1);
            TextMeshProUGUI brand = A.Label(headTexts.transform, "BrandText", "Brand name", 34, ThemeRole.SecondaryText, FontRole.Body);
            A.Shrink(brand, LeastSize);
            TextMeshProUGUI name = A.Label(headTexts.transform, "NameText", "Product name", NameSize, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI type = A.Label(headTexts.transform, "TypeText", "Cosmetic type", SmallSize, ThemeRole.SecondaryText, FontRole.Body);
            A.Shrink(type, LeastSize);
            TextMeshProUGUI date = A.Label(headTexts.transform, "DateText", "Scanned 12 May 2026, 3:40 PM", 30, ThemeRole.SecondaryText, FontRole.Body);
            A.Shrink(date, LeastSize);
            Button rename = A.IconButton(head.transform, "RenameButton", IconFactory.Pencil, 104, 44, ThemeRole.MainText, ThemeRole.InputBackground);
            A.Size(rename, 104, 104);
            TouchArea(rename.transform, Tap, Tap);

            GameObject verdict = A.Node(product.transform, "VerdictRow");
            A.HStack(verdict, 0);
            A.Size(verdict.transform, -1, 92);
            StatusPillView status = A.StatusPill(verdict.transform, "StatusPill", 36);
            A.Size(status, 560, 92);
            TextMeshProUGUI summary = A.Label(product.transform, "SummaryText", "The one-line reason for this result shows here.", 40, ThemeRole.MainText, FontRole.Label,
                                              TextAlignmentOptions.TopLeft, true);
            // the next step for a flagged result, right under the verdict (inactive until a result needs it)
            Button saferAlternatives = A.PillButton(product.transform, "SaferAlternativesButton", "See safer alternatives", ButtonSize,
                                                    ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Swap);
            A.Size(saferAlternatives, -1, Tap);
            A.Outline(saferAlternatives.transform, ThemeRole.Accent, Tap);
            saferAlternatives.gameObject.SetActive(false);
            // PENDING APPROVAL: a product the AI read from a photo, not checked by an admin yet (inactive until a result needs it).
            // It stands above the FDA badge: it is about whether the entry itself can be trusted.
            GameObject pendingBadge = A.Node(product.transform, "PendingApprovalBadge");
            A.HStack(pendingBadge, 18, 24, 24, 18, 18, TextAnchor.MiddleLeft);
            A.Backdrop(pendingBadge.transform, ThemeRole.InputBackground, 1f, 44f);
            A.Outline(pendingBadge.transform, ThemeRole.Gold, 44f);
            Image pendingDisc = A.RawPanel(pendingBadge.transform, "IconDisc", new Color32(196, 134, 32, 255), 64f);
            A.Size(pendingDisc, 64, 64);
            A.RawIcon(pendingDisc.transform, "Icon", IconFactory.Warning, 36, Color.white);
            GameObject pendingTexts = A.Node(pendingBadge.transform, "Texts");
            A.VStack(pendingTexts, 4);
            A.Size(pendingTexts.transform, -1, -1, 1);
            TextMeshProUGUI pendingTitle = A.Label(pendingTexts.transform, "Title", "PENDING APPROVAL", 34, ThemeRole.Warning, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI pendingDetail = A.Label(pendingTexts.transform, "Detail", "Read by the AI from a photo. Not checked by an admin yet, so treat it as unverified.", 30,
                                                    ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            pendingBadge.SetActive(false);
            FdaBadgeView fdaBadge = FdaBadge(product.transform);
            // the ONE button to the FDA Philippines portal (the badge's details no longer have a second one)
            Button fdaVerification = A.PillButton(product.transform, "FdaVerificationButton", "Verify on FDA Philippines", ButtonSize,
                                                  ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.ExternalLink);
            A.Size(fdaVerification, -1, Tap);
            A.Outline(fdaVerification.transform, ThemeRole.Accent, Tap);

            extraCards?.Invoke(s, content);

            // 2. compatibility matrix
            GameObject matrix = Section(content, "CompatibilityCard", "How it matches you");
            CompatibilityIndicator skinType = A.Indicator(matrix.transform, "SkinTypeIndicator", "Skin type", true, BodySize, SmallSize, 64);
            CompatibilityIndicator allergens = A.Indicator(matrix.transform, "AllergensIndicator", "Allergens", true, BodySize, SmallSize, 64);
            CompatibilityIndicator concerns = A.Indicator(matrix.transform, "ConcernsIndicator", "Chronic skin condition", true, BodySize, SmallSize, 64);
            CompatibilityIndicator age = A.Indicator(matrix.transform, "AgeIndicator", "Age", true, BodySize, SmallSize, 64);

            // 3. key ingredients (tap a chip for its details)
            GameObject ingredients = Section(content, "IngredientsCard", "Key ingredients");
            TextMeshProUGUI chipHint = A.Label(ingredients.transform, "ChipHintText", "Tap an ingredient to see what it does and why it matters for you.", SmallSize,
                                               ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.HintOnly(chipHint);
            GameObject chipGrid = A.Node(ingredients.transform, "ChipGrid", typeof(GridLayoutGroup));
            var grid = chipGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(412, ChipHeight); grid.spacing = new Vector2(16, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2; grid.childAlignment = TextAnchor.UpperLeft;

            GameObject chip = A.Node(chipGrid.transform, "ChipTemplate", typeof(Image), typeof(Button), typeof(IngredientChipView));
            var chipBackground = chip.GetComponent<Image>();
            Kit.StyleRounded(chipBackground); chipBackground.color = Color.white;
            SceneBuilderCore.Themed(chip, ThemeRole.InputBackground);
            chip.GetComponent<Button>().targetGraphic = chipBackground;
            A.Outline(chip.transform, ThemeRole.SurfaceAlt, ChipHeight);
            // the level as a disc WITH an icon (check / warning / cross / i): never colour alone
            Image chipDot = A.RawPanel(chip.transform, "Dot", new Color32(94, 127, 75, 255), 56f);
            A.At(chipDot.transform, A.ML, new Vector2(22, 0), new Vector2(56, 56));
            Image chipIcon = A.RawIcon(chipDot.transform, "Icon", IconFactory.Check, 32, Color.white);
            TextMeshProUGUI chipLabel = A.Label(chip.transform, "Label", "Niacinamide", 34, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft, true);
            A.Fill(chipLabel.transform, 94, 8, 20, 8); A.ShrinkWrapped(chipLabel, LeastSize);
            var chipView = chip.GetComponent<IngredientChipView>();
            A.Set(chipView, "label", chipLabel); A.Set(chipView, "dot", chipDot); A.Set(chipView, "icon", chipIcon);
            A.Set(chipView, "safeIcon", IconFactory.Check); A.Set(chipView, "cautionIcon", IconFactory.Warning);
            A.Set(chipView, "harmfulIcon", IconFactory.Close); A.Set(chipView, "unknownIcon", IconFactory.Info);

            // a reminder why some chips say "Not checked": no reviewed information for those ingredients yet (shown only when that is so)
            TextMeshProUGUI noInfo = A.Label(ingredients.transform, "NoInfoText",
                "2 of the 12 ingredients show \"Not checked\": GlowUp has no reviewed information or trusted source for them yet. The list itself comes from the product. Their entries are still waiting for review, so they count neither for nor against this result.",
                SmallSize, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            noInfo.gameObject.SetActive(false);                                  // the controller shows it when a result needs it; tick it active to see and restyle it

            Button allIngredients = A.PillButton(ingredients.transform, "AllIngredientsButton", "See all ingredients", ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.ListIcon);
            A.Size(allIngredients, -1, Tap);
            A.Outline(allIngredients.transform, ThemeRole.Accent, Tap);

            // where the list came from: closed behind one button (a tap opens it underneath, another tap closes it).
            // Inside: one short line, the second-source line, the pages as labelled buttons and what each label means.
            GameObject sourcesBlock = A.Node(ingredients.transform, "SourcesBlock");
            A.VStack(sourcesBlock, 14);
            Button sourcesButton = A.PillButton(sourcesBlock.transform, "SourcesButton", "Where this list came from", ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Document);
            A.Size(sourcesButton, -1, Tap);
            A.Outline(sourcesButton.transform, ThemeRole.Accent, Tap);
            Image sourcesArrow = OpenArrow(sourcesButton.transform);
            GameObject sourcesBody = A.Node(sourcesBlock.transform, "SourcesBody");
            A.VStack(sourcesBody, 14, 6, 6, 4, 4);
            TextMeshProUGUI listSource = A.Label(sourcesBody.transform, "ListSourceText", "Ingredient list looked up online. Check it against your pack when you can.", SmallSize,
                                                 ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            // a second place that has the same product's list: "Matched 2 sources", or what it lists differently
            TextMeshProUGUI listCheck = A.Label(sourcesBody.transform, "ListCheckText", "Matched 2 sources: your pack and Open Beauty Facts. That entry was last edited 3 May 2026.", SmallSize,
                                                ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            GameObject links = A.Node(sourcesBody.transform, "SourceLinks", typeof(GridLayoutGroup));
            var linkGrid = links.GetComponent<GridLayoutGroup>();
            linkGrid.cellSize = new Vector2(412, ChipHeight); linkGrid.spacing = new Vector2(16, 16);
            linkGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; linkGrid.constraintCount = 2; linkGrid.childAlignment = TextAnchor.UpperLeft;
            SourceLinkButton linkTemplate = SourceLink(links.transform, "SourceLinkTemplate");
            TextMeshProUGUI sourceGuide = A.Label(sourcesBody.transform, "SourceGuideText",
                "<b>Retailer listing:</b> A shop's copy of the pack's list. It can be out of date: compare it with your pack.\n" + ListSources.LinkGuide,
                30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            sourceGuide.lineSpacing = 4;
            var sourcesExpand = SceneBuilderCore.Ensure<ExpandableSection>(sourcesBlock);
            A.Set(sourcesExpand, "toggleButton", sourcesButton); A.Set(sourcesExpand, "toggleLabel", A.LabelOf(sourcesButton));
            A.Set(sourcesExpand, "arrow", sourcesArrow.rectTransform); A.Set(sourcesExpand, "body", sourcesBody);
            A.Set(sourcesExpand, "closedLabel", "Where this list came from"); A.Set(sourcesExpand, "openLabel", "Hide where it came from");
            sourcesBody.SetActive(false);                                        // opens with the button; tick it active to see and restyle it

            // 4. why this result? The long text stays behind this button (the screen already shows the verdict, the four
            //    match lines and the ingredient chips); a tap opens it underneath, another tap closes it.
            GameObject why = Section(content, "WhyCard", null);
            Button whyButton = A.PillButton(why.transform, "WhyButton", "Why this result?", ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Info);
            A.Size(whyButton, -1, Tap);
            A.Outline(whyButton.transform, ThemeRole.Accent, Tap);
            Image whyArrow = OpenArrow(whyButton.transform);
            TextMeshProUGUI explanation = A.Label(why.transform, "ExplanationText",
                "The plain-language explanation shows here: which ingredients decided the result and how they relate to your skin type, concerns, allergens and age.",
                BodySize, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            explanation.lineSpacing = 6;
            var whyExpand = SceneBuilderCore.Ensure<ExpandableSection>(why);
            A.Set(whyExpand, "toggleButton", whyButton); A.Set(whyExpand, "toggleLabel", A.LabelOf(whyButton));
            A.Set(whyExpand, "arrow", whyArrow.rectTransform); A.Set(whyExpand, "body", explanation.gameObject);
            A.Set(whyExpand, "closedLabel", "Why this result?"); A.Set(whyExpand, "openLabel", "Hide the explanation");
            explanation.gameObject.SetActive(false);                             // opens with the button; tick it active to see and restyle it

            // 5. shades that suit the user (colour cosmetics only)
            GameObject shade = Section(content, "ShadeCard", "Shades that suit you");
            GameObject swatchRow = A.Node(shade.transform, "SwatchRow");
            A.HStack(swatchRow, 18);
            A.Size(swatchRow.transform, -1, 100);
            string[] samples = { "#C4696F", "#B5525C", "#D98880", "#A9454F", "#E0A18F", "#8E3B46" };
            var swatches = new Image[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                swatches[i] = A.RawPanel(swatchRow.transform, "Swatch" + (i + 1), SceneBuilderCore.Hex(samples[i]), 96f);
                A.Size(swatches[i], 96, 96);
                A.Outline(swatches[i].transform, ThemeRole.MainText, 96f, 0.28f);
            }
            TextMeshProUGUI shadeCaption = A.Label(shade.transform, "ShadeCaptionText", "Picked for your skin tone.", SmallSize, ThemeRole.SecondaryText, FontRole.Body,
                                                   TextAlignmentOptions.TopLeft, true);

            // 5b. questions about the product, answered on the phone from this result (ProductQuestions; panel revision 22 Nov 2026)
            GameObject askCard = Section(content, "AskCard", "Questions about this product?");
            A.Label(askCard.transform, "AskText", "Is it safe for me, which ingredients to watch, is it FDA-registered: tap to see the answers for your profile.",
                BodySize, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Button askButton = A.PillButton(askCard.transform, "AskButton", "Ask about this product", ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Sparkle);
            A.Size(askButton, -1, Tap);
            A.Outline(askButton.transform, ThemeRole.Accent, Tap);

            // 6. see a specialist. Two looks of one button: pink when something is flagged, an outline otherwise (so AR Try-On
            //    stays the one pink next step). The controller shows one of them; the pink one is inactive here.
            GameObject specialist = Section(content, "DermatologistCard", "Not sure about this result?");
            TextMeshProUGUI specialistText = A.Label(specialist.transform, "DermatologistText",
                ProductScope.DermatologistLine,
                BodySize, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Button specialistButton = A.PillButton(specialist.transform, "DermatologistButton", "Find a dermatologist", ButtonSize, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.ExternalLink);
            A.Size(specialistButton, -1, Tap);
            specialistButton.gameObject.SetActive(false);
            Button specialistQuiet = A.PillButton(specialist.transform, "DermatologistQuietButton", "Find a dermatologist", ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.ExternalLink);
            A.Size(specialistQuiet, -1, Tap);
            A.Outline(specialistQuiet.transform, ThemeRole.Accent, Tap);

            // ---- bottom buttons ----
            Button scanAnother = A.PillButton(card, "ScanAnotherButton", "Scan again", ButtonSize, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Scan);
            A.BottomStrip(scanAnother.transform, 44, Tap, A.Pad, 512);
            A.Outline(scanAnother.transform, ThemeRole.Accent, Tap);
            Button tryOn = A.PillButton(card, "TryOnButton", "AR Try-On", ButtonSize, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sparkle);
            A.BottomStrip(tryOn.transform, 44, Tap, 512, A.Pad);
            A.Glow(tryOn, Tap);

            // ---- pop-ups ----
            A.AppPopup alternativesPopup = A.Overlay(s, "AlternativesPanel", "Alternatives");
            A.Fill(alternativesPopup.box, 40, 40, 40, 150);
            TextMeshProUGUI alternativesHint = A.Label(alternativesPopup.box, "AlternativesHintText", "Make-up of the same kind with no ingredients flagged for your profile. This is not medical advice.", SmallSize,
                                                       ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(alternativesHint.transform, 114, 92, 48, 48);
            GameObject alternativesArea = A.Node(alternativesPopup.box, "ListArea");
            A.Fill(alternativesArea.transform, 32, 40, 32, 218);
            AppTiles.GridScroll(alternativesArea.transform, "AlternativesScrollView", out RectTransform alternativesContent);
            ProductTileView alternativeTemplate = AppTiles.ProductTile(alternativesContent, "AlternativeTileTemplate", AppTiles.TileKind.Dashboard);
            AppTiles.IconOnlySaveAndResult(alternativeTemplate);           // icons only for Save and Result: no cut-off labels (17 Nov 2026)
            TextMeshProUGUI alternativesEmpty = A.Label(alternativesArea.transform, "AlternativesEmptyText",
                "No make-up products matched your profile yet. Scan another product to check it.", 34, ThemeRole.SecondaryText, FontRole.Body,
                TextAlignmentOptions.Center, true);
            A.Fill(alternativesEmpty.transform, 50, 0, 50, 0);
            alternativesEmpty.gameObject.SetActive(false);

            OverlayPanel renamePanel = A.RenameDialog(s, out TMP_InputField renameInput, out Button renameSave);
            IngredientSheet sheet = A.IngredientSheetPopup(s);
            // "Scan again": the same product, or a different one?
            A.AppPopup scanAgain = SmartScanSceneBuilder.ScanAgainPopup(s, "ScanAgainPanel", "The new result takes the place of this one.",
                                                                        "The new product takes this one's place. This one can be restored in Recent Scans.", out Button sameProduct, out Button differentProduct);

            // ---- ingredient safety alert: opens by itself when the scan contains a trigger the user switched on in Settings ----
            A.AppPopup alertPopup = A.Overlay(s, "SafetyAlertPanel", "Safety alert");
            A.At(alertPopup.box, A.MC, new Vector2(0, 60), new Vector2(920, 900));
            A.Outline(alertPopup.box, ThemeRole.Warning, 64f, 1f, "WarningOutline");
            Image alertIcon = A.Icon(alertPopup.box, "WarningIcon", IconFactory.Warning, 66, ThemeRole.Warning);
            A.At(alertIcon.transform, A.TL, new Vector2(44, -40), new Vector2(66, 66));
            A.TopStrip(alertPopup.title.transform, 34, 80, 128, 128);
            TextMeshProUGUI alertMessage = A.Label(alertPopup.box, "MessageText",
                "This product contains something you asked to be warned about:\n\n<b>May aggravate eczema.</b> Fragrance, Alcohol Denat.\n\nOpen the ingredient list below for the reasons.",
                34, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(alertMessage.transform, 48, 200, 48, 140);
            A.ShrinkWrapped(alertMessage, LeastSize);
            Button alertOk = A.PillButton(alertPopup.box, "OkButton", "I understand", ButtonSize, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.BottomStrip(alertOk.transform, 44, Tap, 48, 48);
            var alertView = A.Controller<SafetyAlertPopup>("SafetyAlertPopup");
            A.Set(alertView, "panel", alertPopup.panel); A.Set(alertView, "messageText", alertMessage); A.Set(alertView, "okButton", alertOk);

            // ---- controller ----
            var controller = A.Controller<ScanResultFlowController>("ScanResultFlowController");
            A.Set(controller, "brandText", brand); A.Set(controller, "nameText", name); A.Set(controller, "typeText", type); A.Set(controller, "dateText", date);
            A.Set(controller, "typeIcon", typeIcon); A.Set(controller, "status", status); A.Set(controller, "summaryText", summary);
            A.Set(controller, "typeIcons", A.TypeIcons());
            A.Set(controller, "askButton", askButton);
            A.Set(controller, "skinType", skinType); A.Set(controller, "allergens", allergens); A.Set(controller, "concerns", concerns); A.Set(controller, "age", age);
            A.Set(controller, "chipContent", chipGrid.transform); A.Set(controller, "chipTemplate", chipView);
            A.Set(controller, "ingredientBreakdownText", chipHint);
            A.Set(controller, "allIngredientsButton", allIngredients); A.Set(controller, "allIngredientsLabel", A.LabelOf(allIngredients));
            A.Set(controller, "ingredientSheet", sheet);
            A.Set(controller, "explanationText", explanation);
            A.Set(controller, "fdaBadge", fdaBadge); A.Set(controller, "fdaVerificationButton", fdaVerification);
            A.Set(controller, "pendingBadge", pendingBadge); A.Set(controller, "pendingBadgeText", pendingTitle); A.Set(controller, "pendingDetailText", pendingDetail);
            A.Set(controller, "listSourceText", listSource); A.Set(controller, "listCheckText", listCheck);
            A.Set(controller, "noInfoText", noInfo);
            A.Set(controller, "sourceLinksContent", links.transform); A.Set(controller, "sourceLinkTemplate", linkTemplate);
            A.Set(controller, "sourceGuideText", sourceGuide);
            A.Set(controller, "shadeCard", shade); Kit.SetArray(controller, "shadeSwatches", swatches); A.Set(controller, "shadeCaption", shadeCaption);
            A.Set(controller, "dermatologistCard", specialist); A.Set(controller, "dermatologistText", specialistText);
            A.Set(controller, "dermatologistButton", specialistButton); A.Set(controller, "dermatologistQuietButton", specialistQuiet);
            A.Set(controller, "alternativesButton", alternatives); A.Set(controller, "alternativesPanel", alternativesPopup.panel);
            A.Set(controller, "saferAlternativesButton", saferAlternatives);
            A.Set(controller, "alternativesContent", alternativesContent); A.Set(controller, "alternativeTileTemplate", alternativeTemplate);
            A.Set(controller, "aiPanel", AppTiles.AiPanel(s));
            A.Set(controller, "alternativesEmptyText", alternativesEmpty);
            // reworded for "make-up only" and "nothing flagged" (wording changed in the Inspector is kept)
            SceneBuilderCore.ReplaceText(controller, "msgNoAlternatives", "No make-up products matched your profile yet. Scan another product to check it.",
                "No alternatives in the catalogue yet. They appear here as products are added.");
            SceneBuilderCore.ReplaceText(controller, "msgLoadingAlternatives", "Looking for make-up that matches your profile...", "Looking for safer alternatives...");
            A.Set(controller, "renameButton", rename); A.Set(controller, "renamePanel", renamePanel);
            A.Set(controller, "renameInput", renameInput); A.Set(controller, "renameSaveButton", renameSave);
            A.Set(controller, "backButton", s.back); A.Set(controller, "scanAnotherButton", scanAnother); A.Set(controller, "tryOnButton", tryOn);
            A.Set(controller, "scanAgainPanel", scanAgain.panel); A.Set(controller, "sameProductButton", sameProduct); A.Set(controller, "differentProductButton", differentProduct);

            // What is FETCHED on this screen: the product's own words and the result (bars only while nothing is on the phone
            // yet). The headings, the labels, the buttons and the panels are never bars.
            AppTiles.WordSkeleton(s, AppTiles.TextsOf(controller, "brandText", "nameText", "typeText", "dateText", "summaryText", "explanationText", "shadeCaption"));

            A.Finish(s);
            Debug.Log("[GlowUp] AiSmartAnalysisResultScene built. ChipTemplate, SourceLinkTemplate and AlternativeTileTemplate are pattern objects; ExplanationText, SourcesBody and FdaDetails open with their buttons; SaferAlternativesButton, PendingApprovalBadge and the pink DermatologistButton show only when a result needs them; the pop-ups (ScanAgainPanel among them) are inactive until opened.");
        }

        /// <summary>
        /// The FDA Philippines badge: a row (round icon, two short lines, a small arrow) that is a button, and under it a
        /// block that opens on a tap with the record's details, what a record does and does not prove, and (for a similar
        /// record) "is this your product?". The block is inactive in the Hierarchy until it is opened. The portal itself is
        /// opened by the one "Verify on FDA Philippines" button under the badge.
        /// </summary>
        static FdaBadgeView FdaBadge(Transform parent)
        {
            GameObject holder = A.Node(parent, "FdaBadge");
            A.VStack(holder, 12);

            GameObject row = A.Node(holder.transform, "BadgeRow", typeof(Button));
            A.HStack(row, 18, 24, 24, 18, 18, TextAnchor.MiddleLeft);
            SceneBuilderCore.Ensure<LayoutElement>(row).minHeight = Tap;         // at least Tap high; taller when the title wraps
            Image rowBackground = A.Backdrop(row.transform, ThemeRole.InputBackground, 1f, 44f);
            rowBackground.raycastTarget = true;                                  // the whole row is the button
            row.GetComponent<Button>().targetGraphic = rowBackground;
            A.Outline(row.transform, ThemeRole.SurfaceAlt, 44f);
            Image disc = A.RawPanel(row.transform, "IconDisc", new Color32(94, 127, 75, 255), 64f);
            A.Size(disc, 64, 64);
            Image icon = A.RawIcon(disc.transform, "Icon", IconFactory.Check, 36, Color.white);
            GameObject texts = A.Node(row.transform, "Texts");
            A.VStack(texts, 4);
            A.Size(texts.transform, -1, -1, 1);
            TextMeshProUGUI title = A.Label(texts.transform, "Title", "Registered with FDA Philippines", 34, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI detail = A.Label(texts.transform, "Detail", "Valid until 16 Apr 2028", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Image arrow = A.Icon(row.transform, "Arrow", IconFactory.ChevronDown, 34, ThemeRole.SecondaryText);
            A.Size(arrow, 34, 34);

            GameObject more = A.Node(holder.transform, "FdaDetails");
            A.VStack(more, 16, 14, 14, 2, 6);
            TextMeshProUGUI moreText = A.Label(more.transform, "DetailsText",
                "FDA record: Absidy Beauty Base Complexion Blur Translucent Perfecting Powder\nRegistered by: Ether International Inc.\nNotification no.: NN-1000014495482\n\n" +
                "The FDA Philippines list shows which cosmetics are registered (notified) for sale. It holds no ingredient lists, and a record does not prove that the item in your hand is genuine or safe.",
                SmallSize, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            moreText.lineSpacing = 4;
            // a SIMILAR record: the user says whether it is their product (shown for a similar record only)
            GameObject confirm = A.Node(more.transform, "ConfirmRow");
            A.VStack(confirm, 12);
            A.Label(confirm.transform, "QuestionText", "Is this record your product?", 34, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            GameObject confirmButtons = A.Node(confirm.transform, "Buttons");
            A.HStack(confirmButtons, 16, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            A.Size(confirmButtons.transform, -1, Tap);
            Button confirmNo = A.PillButton(confirmButtons.transform, "NotMineButton", "No, it is not", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.Size(confirmNo, -1, Tap, 1);
            A.Outline(confirmNo.transform, ThemeRole.Accent, Tap);
            Button confirmYes = A.PillButton(confirmButtons.transform, "IsMineButton", "Yes, it is", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.Size(confirmYes, -1, Tap, 1);

            var view = SceneBuilderCore.Ensure<FdaBadgeView>(holder);
            A.Set(view, "icon", icon); A.Set(view, "iconBackground", disc); A.Set(view, "titleLabel", title); A.Set(view, "detailLabel", detail);
            A.Set(view, "moreText", moreText);                                   // no portalButton: "Verify on FDA Philippines" is the one way to the portal
            A.Set(view, "confirmRow", confirm); A.Set(view, "confirmYesButton", confirmYes); A.Set(view, "confirmNoButton", confirmNo);
            A.Set(view, "registeredIcon", IconFactory.Check); A.Set(view, "similarIcon", IconFactory.Info); A.Set(view, "expiredIcon", IconFactory.Warning);
            A.Set(view, "noneIcon", IconFactory.Info); A.Set(view, "unavailableIcon", IconFactory.Info); A.Set(view, "checkingIcon", IconFactory.Sync);

            var expand = SceneBuilderCore.Ensure<ExpandableSection>(holder);
            A.Set(expand, "toggleButton", row.GetComponent<Button>()); A.Set(expand, "arrow", arrow.rectTransform); A.Set(expand, "body", more);
            more.SetActive(false);                                               // opens when the badge is tapped; tick it active to see and restyle it
            return view;
        }

        /// <summary>
        /// The pattern for a "page the list came from" button: the site's name, under it the KIND of page (Regulator /
        /// government, Retailer listing ...; the same words as on the admin website) and the "opens a website" icon.
        /// Copied once per page.
        /// </summary>
        internal static SourceLinkButton SourceLink(Transform parent, string name)
        {
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Button), typeof(SourceLinkButton));
            var background = go.GetComponent<Image>();
            Kit.StyleRounded(background); background.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.InputBackground);
            go.GetComponent<Button>().targetGraphic = background;
            A.Outline(go.transform, ThemeRole.Accent, ChipHeight);
            TextMeshProUGUI label = A.Label(go.transform, "Label", "watsons.com.ph", SmallSize, ThemeRole.AccentText, FontRole.Button, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(label.transform, 16, 48, 30, 76); A.Shrink(label, LeastSize);
            TextMeshProUGUI kind = A.Label(go.transform, "KindLabel", "Retailer listing", LeastSize, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(kind.transform, 64, 40, 30, 76); A.Shrink(kind, LeastSize);
            Image arrow = A.Icon(go.transform, "Arrow", IconFactory.ExternalLink, 34, ThemeRole.AccentText);
            A.At(arrow.transform, A.MR, new Vector2(-26, 0), new Vector2(34, 34));
            var view = go.GetComponent<SourceLinkButton>();
            A.Set(view, "button", go.GetComponent<Button>()); A.Set(view, "label", label); A.Set(view, "kindLabel", kind);
            return view;
        }

        /// <summary>The small arrow at the right end of a button that opens a block under it (it turns round its own middle).</summary>
        static Image OpenArrow(Transform button)
        {
            Image arrow = A.Icon(button, "Arrow", IconFactory.ChevronDown, 34, ThemeRole.SecondaryText);
            A.At(arrow.transform, A.MR, new Vector2(-34, 0), new Vector2(34, 34));
            arrow.rectTransform.pivot = A.MC; arrow.rectTransform.anchoredPosition = new Vector2(-51, 0);
            return arrow;
        }

        /// <summary>
        /// A see-through area round a small button that also takes taps, so a finger can hit `width` x `height` units
        /// while the button keeps its look. Only for buttons with free space round them (the header, the product head).
        /// </summary>
        static void TouchArea(Transform button, float width, float height)
        {
            GameObject pad = A.Node(button, "TouchArea", typeof(Image));
            var image = pad.GetComponent<Image>();
            image.color = new Color(1, 1, 1, 0.004f); image.raycastTarget = true;
            A.At(pad.transform, A.MC, Vector2.zero, new Vector2(width, height));
            SceneBuilderCore.Ensure<LayoutElement>(pad).ignoreLayout = true;
            pad.transform.SetAsFirstSibling();
        }

        /// <summary>A section of the result: a round panel that grows with its content, with an optional heading.</summary>
        internal static GameObject Section(Transform content, string name, string header)
        {
            GameObject go = A.Node(content, name);
            A.VStack(go, 18, 30, 30, 28, 30);
            A.Backdrop(go.transform, ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);
            if (header != null) A.Label(go.transform, "HeaderText", header, HeadingSize, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
            return go;
        }
    }
}
