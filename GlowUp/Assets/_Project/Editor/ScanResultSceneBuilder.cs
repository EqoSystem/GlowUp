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
    /// Menu: GlowUp > Build Scene > 12 AI Scan Result.
    /// One analysed product, each fact in ONE place:
    ///   1. the product card: who it is, the verdict (status pill and one line of reason) and the FDA Philippines badge
    ///      (tap it for the record's details and a button to the FDA's site);
    ///   2. how it matches the user (four lines);
    ///   3. key ingredients, "See all ingredients", and where the list came from (one line, the pages as buttons);
    ///   4. "Why this result?": the long text, behind a button;
    ///   5. shades that suit the user; 6. the advice to see a dermatologist.
    /// The Alternatives button is at the top, Scan Another / AR Try-On at the bottom.
    /// </summary>
    public static class ScanResultSceneBuilder
    {
        [MenuItem("GlowUp/Build Scene/12 AI Scan Result", false, 122)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("AiSmartAnalysisResultScene", "Your Result", true, "ResultCard", "AiSmartAnalysisResultCard");
            Transform card = s.card;

            // ---- header: the title moves left to make room for Alternatives ----
            s.title.alignment = TextAlignmentOptions.MidlineLeft;
            A.TopStrip(s.title.transform, 30, 96, 136, 312);
            Button alternatives = A.PillButton(card, "AlternativesButton", "Alternatives", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Swap);
            A.At(alternatives.transform, A.TR, new Vector2(-28, -40), new Vector2(272, 76));
            A.Outline(alternatives.transform, ThemeRole.Accent, 76f);

            // ---- the scrolling result ----
            GameObject area = A.Node(card, "ResultArea");
            A.Between(area.transform, A.HeaderBottom, 200);
            A.Scroll(area.transform, "ResultScrollView", out RectTransform content, 20, 0, 18, 4, 30);

            // 1. the product: who it is, the verdict (pill + one line of reason) and the FDA Philippines badge.
            //    The card grows with its content. (The one-line summary used to sit in a card of its own under this one.)
            GameObject product = A.Node(content, "ProductCard");
            A.VStack(product, 16, 24, 24, 0, 24);
            A.Backdrop(product.transform, ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);

            GameObject head = A.Node(product.transform, "ProductHead");
            A.Size(head.transform, -1, 214);
            Image tile = A.Panel(head.transform, "IconTile", ThemeRole.InputBackground, 1f, 124f);
            A.At(tile.transform, A.TL, new Vector2(0, -24), new Vector2(124, 124));
            Image typeIcon = A.Icon(tile.transform, "TypeIcon", IconFactory.Bottle, 70, ThemeRole.MainText);
            TextMeshProUGUI brand = A.Label(head.transform, "BrandText", "Brand name", 30, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(brand.transform, 18, 48, 148, 92); A.Shrink(brand, 22);
            TextMeshProUGUI name = A.Label(head.transform, "NameText", "Product name", 44, ThemeRole.MainText, FontRole.Heading);
            A.TopStrip(name.transform, 62, 68, 148, 92); A.Shrink(name, 30);
            TextMeshProUGUI type = A.Label(head.transform, "TypeText", "Cosmetic type", 29, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(type.transform, 128, 44, 148, 0); A.Shrink(type, 22);
            TextMeshProUGUI date = A.Label(head.transform, "DateText", "Scanned 12 May 2026, 3:40 PM", 26, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(date.transform, 168, 42, 148, 0); A.Shrink(date, 20);
            Button rename = A.IconButton(head.transform, "RenameButton", IconFactory.Pencil, 76, 38, ThemeRole.MainText, ThemeRole.InputBackground);
            A.At(rename.transform, A.TR, new Vector2(2, -22), new Vector2(76, 76));

            GameObject verdict = A.Node(product.transform, "VerdictRow");
            A.HStack(verdict, 0);
            A.Size(verdict.transform, -1, 68);
            StatusPillView status = A.StatusPill(verdict.transform, "StatusPill", 30);
            A.Size(status, 440, 68);
            TextMeshProUGUI summary = A.Label(product.transform, "SummaryText", "The one-line reason for this result shows here.", 32, ThemeRole.MainText, FontRole.Label,
                                              TextAlignmentOptions.TopLeft, true);
            FdaBadgeView fdaBadge = FdaBadge(product.transform);

            // 3. compatibility matrix
            GameObject matrix = Section(content, "CompatibilityCard", "How it matches you");
            CompatibilityIndicator skinType = A.Indicator(matrix.transform, "SkinTypeIndicator", "Skin type", true);
            CompatibilityIndicator allergens = A.Indicator(matrix.transform, "AllergensIndicator", "Allergens", true);
            CompatibilityIndicator concerns = A.Indicator(matrix.transform, "ConcernsIndicator", "Skin concerns", true);
            CompatibilityIndicator age = A.Indicator(matrix.transform, "AgeIndicator", "Age", true);

            // 4. key ingredients (tap a chip for its details)
            GameObject ingredients = Section(content, "IngredientsCard", "Key ingredients");
            TextMeshProUGUI chipHint = A.Label(ingredients.transform, "ChipHintText", "Tap an ingredient to see what it does and why it matters for you.", 27,
                                               ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.HintOnly(chipHint);
            GameObject chipGrid = A.Node(ingredients.transform, "ChipGrid", typeof(GridLayoutGroup));
            var grid = chipGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(412, 70); grid.spacing = new Vector2(16, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2; grid.childAlignment = TextAnchor.UpperLeft;

            GameObject chip = A.Node(chipGrid.transform, "ChipTemplate", typeof(Image), typeof(Button), typeof(IngredientChipView));
            var chipBackground = chip.GetComponent<Image>();
            Kit.StyleRounded(chipBackground); chipBackground.color = Color.white;
            SceneBuilderCore.Themed(chip, ThemeRole.InputBackground);
            chip.GetComponent<Button>().targetGraphic = chipBackground;
            A.Outline(chip.transform, ThemeRole.SurfaceAlt, 70f);
            Image chipDot = A.RawPanel(chip.transform, "Dot", new Color32(94, 127, 75, 255), 22f);
            A.At(chipDot.transform, A.ML, new Vector2(24, 0), new Vector2(22, 22));
            TextMeshProUGUI chipLabel = A.Label(chip.transform, "Label", "Niacinamide", 28, ThemeRole.MainText, FontRole.Label);
            A.Fill(chipLabel.transform, 58, 0, 20, 0); A.Shrink(chipLabel, 20);
            var chipView = chip.GetComponent<IngredientChipView>();
            A.Set(chipView, "label", chipLabel); A.Set(chipView, "dot", chipDot);

            Button allIngredients = A.PillButton(ingredients.transform, "AllIngredientsButton", "See all ingredients", 32, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.ListIcon);
            A.Size(allIngredients, -1, 88);
            A.Outline(allIngredients.transform, ThemeRole.Accent, 88f);

            // where the list came from: one short line, and the pages as buttons (not a paragraph with web addresses in it)
            TextMeshProUGUI listSource = A.Label(ingredients.transform, "ListSourceText", "Ingredient list looked up online. Check it against your pack when you can.", 27,
                                                 ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            // a second place that has the same product's list: "Matched 2 sources", or what it lists differently
            TextMeshProUGUI listCheck = A.Label(ingredients.transform, "ListCheckText", "Matched 2 sources: your pack and Open Beauty Facts. That entry was last edited 3 May 2026.", 27,
                                                ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            GameObject links = A.Node(ingredients.transform, "SourceLinks", typeof(GridLayoutGroup));
            var linkGrid = links.GetComponent<GridLayoutGroup>();
            linkGrid.cellSize = new Vector2(412, 68); linkGrid.spacing = new Vector2(16, 12);
            linkGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; linkGrid.constraintCount = 2; linkGrid.childAlignment = TextAnchor.UpperLeft;
            SourceLinkButton linkTemplate = SourceLink(links.transform, "SourceLinkTemplate");

            // 5. why this result? The long text stays behind this button (the screen already shows the verdict, the four
            //    match lines and the ingredient chips); a tap opens it underneath, another tap closes it.
            GameObject why = Section(content, "WhyCard", null);
            Button whyButton = A.PillButton(why.transform, "WhyButton", "Why this result?", 34, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Info);
            A.Size(whyButton, -1, 88);
            A.Outline(whyButton.transform, ThemeRole.Accent, 88f);
            Image whyArrow = A.Icon(whyButton.transform, "Arrow", IconFactory.ChevronDown, 30, ThemeRole.SecondaryText);
            A.At(whyArrow.transform, A.MR, new Vector2(-30, 0), new Vector2(30, 30));
            whyArrow.rectTransform.pivot = A.MC; whyArrow.rectTransform.anchoredPosition = new Vector2(-45, 0);      // it turns round its own middle when the text opens
            TextMeshProUGUI explanation = A.Label(why.transform, "ExplanationText",
                "The plain-language explanation shows here: which ingredients decided the result and how they relate to your skin type, concerns, allergens and age.",
                30, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            explanation.lineSpacing = 6;
            var whyExpand = SceneBuilderCore.Ensure<ExpandableSection>(why);
            A.Set(whyExpand, "toggleButton", whyButton); A.Set(whyExpand, "toggleLabel", A.LabelOf(whyButton));
            A.Set(whyExpand, "arrow", whyArrow.rectTransform); A.Set(whyExpand, "body", explanation.gameObject);
            A.Set(whyExpand, "closedLabel", "Why this result?"); A.Set(whyExpand, "openLabel", "Hide the explanation");
            explanation.gameObject.SetActive(false);                             // opens with the button; tick it active to see and restyle it

            // 6. shades that suit the user (colour cosmetics only)
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
            TextMeshProUGUI shadeCaption = A.Label(shade.transform, "ShadeCaptionText", "Picked for your skin tone.", 28, ThemeRole.SecondaryText, FontRole.Body,
                                                   TextAlignmentOptions.TopLeft, true);

            // 7. see a specialist
            GameObject specialist = Section(content, "DermatologistCard", "Not sure about this result?");
            TextMeshProUGUI specialistText = A.Label(specialist.transform, "DermatologistText",
                ProductScope.DermatologistLine,
                30, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Button specialistButton = A.PillButton(specialist.transform, "DermatologistButton", "Find a dermatologist", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.User);
            A.Size(specialistButton, -1, 96);

            // ---- bottom buttons ----
            Button scanAnother = A.PillButton(card, "ScanAnotherButton", "Scan again", 38, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Scan);
            A.BottomStrip(scanAnother.transform, 44, 132, A.Pad, 512);
            A.Outline(scanAnother.transform, ThemeRole.Accent, 132f);
            Button tryOn = A.PillButton(card, "TryOnButton", "AR Try-On", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sparkle);
            A.BottomStrip(tryOn.transform, 44, 132, 512, A.Pad);
            A.Glow(tryOn, 132);

            // ---- pop-ups ----
            A.AppPopup alternativesPopup = A.Overlay(s, "AlternativesPanel", "Alternatives");
            A.Fill(alternativesPopup.box, 40, 40, 40, 150);
            TextMeshProUGUI alternativesHint = A.Label(alternativesPopup.box, "AlternativesHintText", "Make-up of the same kind with no ingredients flagged for your profile. This is not medical advice.", 29,
                                                       ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(alternativesHint.transform, 114, 84, 48, 48);
            GameObject alternativesArea = A.Node(alternativesPopup.box, "ListArea");
            A.Fill(alternativesArea.transform, 32, 40, 32, 210);
            AppTiles.GridScroll(alternativesArea.transform, "AlternativesScrollView", out RectTransform alternativesContent);
            ProductTileView alternativeTemplate = AppTiles.ProductTile(alternativesContent, "AlternativeTileTemplate", AppTiles.TileKind.Dashboard);
            TextMeshProUGUI alternativesEmpty = A.Label(alternativesArea.transform, "AlternativesEmptyText",
                "No make-up products matched your profile yet. Scan another product to check it.", 32, ThemeRole.SecondaryText, FontRole.Body,
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
                31, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(alertMessage.transform, 48, 190, 48, 140);
            A.ShrinkWrapped(alertMessage, 24);
            Button alertOk = A.PillButton(alertPopup.box, "OkButton", "I understand", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.BottomStrip(alertOk.transform, 44, 112, 48, 48);
            var alertView = A.Controller<SafetyAlertPopup>("SafetyAlertPopup");
            A.Set(alertView, "panel", alertPopup.panel); A.Set(alertView, "messageText", alertMessage); A.Set(alertView, "okButton", alertOk);

            // ---- controller ----
            var controller = A.Controller<ScanResultFlowController>("ScanResultFlowController");
            A.Set(controller, "brandText", brand); A.Set(controller, "nameText", name); A.Set(controller, "typeText", type); A.Set(controller, "dateText", date);
            A.Set(controller, "typeIcon", typeIcon); A.Set(controller, "status", status); A.Set(controller, "summaryText", summary);
            A.Set(controller, "typeIcons", A.TypeIcons());
            A.Set(controller, "skinType", skinType); A.Set(controller, "allergens", allergens); A.Set(controller, "concerns", concerns); A.Set(controller, "age", age);
            A.Set(controller, "chipContent", chipGrid.transform); A.Set(controller, "chipTemplate", chipView);
            A.Set(controller, "allIngredientsButton", allIngredients); A.Set(controller, "allIngredientsLabel", A.LabelOf(allIngredients));
            A.Set(controller, "ingredientSheet", sheet);
            A.Set(controller, "explanationText", explanation);
            A.Set(controller, "fdaBadge", fdaBadge); A.Set(controller, "listSourceText", listSource); A.Set(controller, "listCheckText", listCheck);
            A.Set(controller, "sourceLinksContent", links.transform); A.Set(controller, "sourceLinkTemplate", linkTemplate);
            A.Set(controller, "shadeCard", shade); Kit.SetArray(controller, "shadeSwatches", swatches); A.Set(controller, "shadeCaption", shadeCaption);
            A.Set(controller, "dermatologistCard", specialist); A.Set(controller, "dermatologistText", specialistText); A.Set(controller, "dermatologistButton", specialistButton);
            A.Set(controller, "alternativesButton", alternatives); A.Set(controller, "alternativesPanel", alternativesPopup.panel);
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

            A.Finish(s);
            Debug.Log("[GlowUp] AiSmartAnalysisResultScene built. ChipTemplate, SourceLinkTemplate and AlternativeTileTemplate are pattern objects; ExplanationText and FdaDetails open with their buttons; the pop-ups (ScanAgainPanel among them) are inactive until opened.");
        }

        /// <summary>
        /// The FDA Philippines badge: a row (round icon, two short lines, a small arrow) that is a button, and under it a
        /// block that opens on a tap with the record's details, what a record does and does not prove, and a button to the
        /// FDA's own site. The block is inactive in the Hierarchy until it is opened.
        /// </summary>
        static FdaBadgeView FdaBadge(Transform parent)
        {
            GameObject holder = A.Node(parent, "FdaBadge");
            A.VStack(holder, 10);

            GameObject row = A.Node(holder.transform, "BadgeRow", typeof(Button));
            A.HStack(row, 18, 20, 20, 14, 14, TextAnchor.MiddleLeft);
            Image rowBackground = A.Backdrop(row.transform, ThemeRole.InputBackground, 1f, 44f);
            rowBackground.raycastTarget = true;                                  // the whole row is the button
            row.GetComponent<Button>().targetGraphic = rowBackground;
            A.Outline(row.transform, ThemeRole.SurfaceAlt, 44f);
            Image disc = A.RawPanel(row.transform, "IconDisc", new Color32(94, 127, 75, 255), 56f);
            A.Size(disc, 56, 56);
            Image icon = A.RawIcon(disc.transform, "Icon", IconFactory.Check, 32, Color.white);
            GameObject texts = A.Node(row.transform, "Texts");
            A.VStack(texts, 2);
            A.Size(texts.transform, -1, -1, 1);
            TextMeshProUGUI title = A.Label(texts.transform, "Title", "Registered with FDA Philippines", 30, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI detail = A.Label(texts.transform, "Detail", "Valid until 16 Apr 2028", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Image arrow = A.Icon(row.transform, "Arrow", IconFactory.ChevronDown, 30, ThemeRole.SecondaryText);
            A.Size(arrow, 30, 30);

            GameObject more = A.Node(holder.transform, "FdaDetails");
            A.VStack(more, 14, 14, 14, 2, 6);
            TextMeshProUGUI moreText = A.Label(more.transform, "DetailsText",
                "FDA record: Absidy Beauty Base Complexion Blur Translucent Perfecting Powder\nRegistered by: Ether International Inc.\nNotification no.: NN-1000014495482\n\n" +
                "The FDA Philippines list shows which cosmetics are registered (notified) for sale. It holds no ingredient lists, and a record does not prove that the item in your hand is genuine or safe.",
                26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            moreText.lineSpacing = 4;
            // a SIMILAR record: the user says whether it is their product (shown for a similar record only)
            GameObject confirm = A.Node(more.transform, "ConfirmRow");
            A.VStack(confirm, 10);
            A.Label(confirm.transform, "QuestionText", "Is this record your product?", 28, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            GameObject confirmButtons = A.Node(confirm.transform, "Buttons");
            A.HStack(confirmButtons, 16, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            A.Size(confirmButtons.transform, -1, 76);
            Button confirmNo = A.PillButton(confirmButtons.transform, "NotMineButton", "No, it is not", 27, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.Size(confirmNo, -1, 76, 1);
            A.Outline(confirmNo.transform, ThemeRole.Accent, 76f);
            Button confirmYes = A.PillButton(confirmButtons.transform, "IsMineButton", "Yes, it is", 27, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.Size(confirmYes, -1, 76, 1);
            Button site = A.PillButton(more.transform, "FdaSiteButton", "Open the FDA Philippines site", 28, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.Arrow);
            A.Size(site, -1, 76);
            A.Outline(site.transform, ThemeRole.Accent, 76f);

            var view = SceneBuilderCore.Ensure<FdaBadgeView>(holder);
            A.Set(view, "icon", icon); A.Set(view, "iconBackground", disc); A.Set(view, "titleLabel", title); A.Set(view, "detailLabel", detail);
            A.Set(view, "moreText", moreText); A.Set(view, "portalButton", site);
            A.Set(view, "confirmRow", confirm); A.Set(view, "confirmYesButton", confirmYes); A.Set(view, "confirmNoButton", confirmNo);
            A.Set(view, "registeredIcon", IconFactory.Check); A.Set(view, "similarIcon", IconFactory.Info); A.Set(view, "expiredIcon", IconFactory.Warning);
            A.Set(view, "noneIcon", IconFactory.Info); A.Set(view, "unavailableIcon", IconFactory.Info); A.Set(view, "checkingIcon", IconFactory.Sync);

            var expand = SceneBuilderCore.Ensure<ExpandableSection>(holder);
            A.Set(expand, "toggleButton", row.GetComponent<Button>()); A.Set(expand, "arrow", arrow.rectTransform); A.Set(expand, "body", more);
            more.SetActive(false);                                               // opens when the badge is tapped; tick it active to see and restyle it
            return view;
        }

        /// <summary>The pattern for a "page the list came from" button: the site's name and a small arrow. Copied once per page.</summary>
        static SourceLinkButton SourceLink(Transform parent, string name)
        {
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Button), typeof(SourceLinkButton));
            var background = go.GetComponent<Image>();
            Kit.StyleRounded(background); background.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.InputBackground);
            go.GetComponent<Button>().targetGraphic = background;
            A.Outline(go.transform, ThemeRole.Accent, 68f);
            TextMeshProUGUI label = A.Label(go.transform, "Label", "incidecoder.com", 27, ThemeRole.AccentText, FontRole.Button, TextAlignmentOptions.Center);
            A.Fill(label.transform, 26, 0, 58, 0); A.Shrink(label, 20);
            Image arrow = A.Icon(go.transform, "Arrow", IconFactory.Arrow, 28, ThemeRole.AccentText);
            A.At(arrow.transform, A.MR, new Vector2(-22, 0), new Vector2(28, 28));
            var view = go.GetComponent<SourceLinkButton>();
            A.Set(view, "button", go.GetComponent<Button>()); A.Set(view, "label", label);
            return view;
        }

        /// <summary>A section of the result: a round panel that grows with its content, with an optional heading.</summary>
        static GameObject Section(Transform content, string name, string header)
        {
            GameObject go = A.Node(content, name);
            A.VStack(go, 14, 30, 30, 26, 28);
            A.Backdrop(go.transform, ThemeRole.SurfaceAlt, Kit.PanelAlpha, 48f);
            if (header != null) A.Label(go.transform, "HeaderText", header, 38, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
            return go;
        }
    }
}
