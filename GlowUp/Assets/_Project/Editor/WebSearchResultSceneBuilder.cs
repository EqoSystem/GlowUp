using GlowUp.Common;
using GlowUp.Result;
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
    /// Menu: GlowUp > Build Scene > 12b Web Search Result (also run by All Scenes and All App Scenes). 11 November 2026.
    /// The result of a product the web search of a photo found: the AI Scan Result screen (built by ScanResultSceneBuilder, so the
    /// verdict, how it matches the user, the ingredients, Scan again, AR Try-On and Alternatives are the same) with four cards under
    /// the verdict:
    ///   Brand          - logo, name, market, status, official website and brand picture buttons;
    ///   Product details- photo, name, cosmetic type, description, minimum age of use, FDA registration number, photo and page buttons;
    ///   Sources        - primary (maker's site or an unverified site), secondary (e-commerce retailer), third;
    ///   Shade on the pack - its colour and code.
    /// Every field is a label over its value inside a vertical stack, so a long value wraps and pushes the next row down: nothing
    /// overlaps. Buttons are at least Tap high, with the icon and the words centred together on one line.
    /// </summary>
    public static class WebSearchResultSceneBuilder
    {
        const float Tap = 132;
        const float ChipHeight = 120;
        const float NameSize = 46;
        const float BodySize = 38;
        const float SmallSize = 32;
        const float ButtonSize = 36;
        const float LeastSize = 28;

        [MenuItem("GlowUp/Build Scene/12b Web Search Result", false, 123)]
        public static void Build()
        {
            WebSearchResultView view = null;
            GameObject overviewCard = null, brandCard = null, productCard = null, sourcesCard = null, shadesCard = null;
            ScanResultSceneBuilder.BuildScene("WebSearchResultScene", "Web Search Result", (s, content) =>
            {
                view = A.Controller<WebSearchResultView>("WebSearchResultView");

                // ---- 0. what the pages say (an AI summary of the web search): a row per kind, each hidden when nothing was found
                overviewCard = ScanResultSceneBuilder.Section(content, "WebOverviewCard", "From the web");
                var notes = new[]
                {
                    Field(overviewCard.transform, "FinishField", "Finish and coverage", "Buildable medium to full coverage with a luminous matte finish that wears up to 30 hours."),
                    Field(overviewCard.transform, "ShadeField", "Shade", "Shade 119 is made for light skin with warm or neutral undertones."),
                    Field(overviewCard.transform, "PriceField", "Price and availability", "About PHP 699 for 35 ml in the Philippines (approximate), sold by Maybelline Philippines, Watsons and Shopee."),
                    Field(overviewCard.transform, "ApplyField", "How to apply", "Shake well, then blend one or two pumps with a damp sponge, a brush or your fingertips.")
                };
                A.Label(overviewCard.transform, "OverviewNote", "Summarised by AI from the pages under Sources. It can contain mistakes: check the brand's own page.", SmallSize,
                        ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);

                // ---- 1. the brand
                brandCard =ScanResultSceneBuilder.Section(content, "WebBrandCard", "Brand");
                GameObject head = A.Node(brandCard.transform, "BrandHead");
                A.HStack(head, 24, 0, 0, 0, 0, TextAnchor.MiddleLeft);
                RawImage logo = PictureFrame(head.transform, "LogoFrame", 168, 168, IconFactory.Shield, out GameObject logoPlaceholder);
                GameObject headTexts = A.Node(head.transform, "Texts");
                A.VStack(headTexts, 6);
                A.Size(headTexts.transform, -1, -1, 1);
                TextMeshProUGUI brandName = A.Label(headTexts.transform, "BrandNameText", "Maybelline New York", NameSize, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
                TextMeshProUGUI market = Field(brandCard.transform, "MarketField", "Market", "Global");
                TextMeshProUGUI status = Field(brandCard.transform, "StatusField", "Status", "Pending review: GlowUp checks new products before they join the catalogue.");
                Button website = LinkButton(brandCard.transform, "BrandWebsiteButton", "Official website");
                TextMeshProUGUI websiteNote = A.Label(brandCard.transform, "BrandWebsiteNote", "The brand's own site.", SmallSize, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
                Button brandPicture = LinkButton(brandCard.transform, "BrandPictureButton", "Brand picture");

                // ---- 2. the product
                productCard = ScanResultSceneBuilder.Section(content, "WebProductCard", "Product details");
                RawImage photo = PictureFrame(productCard.transform, "PhotoFrame", -1, 440, IconFactory.Bottle, out GameObject photoPlaceholder);
                TextMeshProUGUI productName = Field(productCard.transform, "ProductNameField", "Product name", "Super Stay Lumi-Matte Foundation");
                TextMeshProUGUI cosmeticType = Field(productCard.transform, "CosmeticTypeField", "Cosmetic type", "Foundation");
                TextMeshProUGUI description = Field(productCard.transform, "DescriptionField", "Description", "A long-wear liquid foundation with a luminous matte finish and medium to full coverage.");
                TextMeshProUGUI minimumAge = Field(productCard.transform, "MinimumAgeField", "Minimum age of use", "Not stated by the maker.");
                TextMeshProUGUI fda = Field(productCard.transform, "FdaNumberField", "FDA registration number", "Not checked yet: GlowUp looks it up on the FDA Philippines portal during review.");
                Button photoLink = LinkButton(productCard.transform, "ProductPhotoButton", "Product photo");
                Button pageLink = LinkButton(productCard.transform, "ProductPageButton", "Official product page");

                // ---- 3. the sources: one full-width button each (site on the first line, which source and its kind on the second)
                sourcesCard = ScanResultSceneBuilder.Section(content, "WebSourcesCard", "Sources");
                SourceLinkButton primary = SourceButton(sourcesCard.transform, "PrimarySourceButton", "maybelline.com", "Primary · Official manufacturer site");
                SourceLinkButton secondary = SourceButton(sourcesCard.transform, "SecondarySourceButton", "watsons.com.ph", "Secondary · E-commerce retailer");
                SourceLinkButton third = SourceButton(sourcesCard.transform, "ThirdSourceButton", "lazada.com.ph", "Third · E-commerce retailer");
                TextMeshProUGUI noSources = A.Label(sourcesCard.transform, "NoSourcesText", "No web page was found for this product: the details come from the photo alone.", SmallSize,
                                                    ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
                noSources.gameObject.SetActive(false);                           // shown only when no page was found

                // ---- 4. the shade printed on the pack: a colour disc and its code, one chip per shade
                shadesCard = ScanResultSceneBuilder.Section(content, "WebShadesCard", "Shade on the pack");
                var swatches = new Image[3];
                var labels = new TextMeshProUGUI[3];
                for (int i = 0; i < 3; i++)
                {
                    GameObject chip = A.Node(shadesCard.transform, "ShadeChip" + (i + 1));
                    A.HStack(chip, 20, 24, 24, 14, 14, TextAnchor.MiddleLeft);
                    A.Size(chip.transform, -1, Tap);
                    A.Backdrop(chip.transform, ThemeRole.InputBackground, 1f, Tap);
                    A.Outline(chip.transform, ThemeRole.SurfaceAlt, Tap);
                    swatches[i] = A.RawPanel(chip.transform, "Swatch", SceneBuilderCore.Hex(i == 0 ? "#D9A57E" : "#C98F6A"), 80f);
                    A.Size(swatches[i], 80, 80);
                    A.Outline(swatches[i].transform, ThemeRole.MainText, 80f, 0.28f);
                    labels[i] = A.Label(chip.transform, "Label", i == 0 ? "119  ·  #D9A57E" : "128  ·  #C98F6A", BodySize, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft);
                    A.Shrink(labels[i], LeastSize);
                    A.Size(labels[i], -1, -1, 1);
                    if (i > 0) chip.SetActive(false);                            // the pack shows one shade; more chips only when the search found more
                }

                A.Set(view, "brandLogo", logo); A.Set(view, "brandLogoPlaceholder", logoPlaceholder); A.Set(view, "brandNameText", brandName);
                A.Set(view, "marketText", market); A.Set(view, "statusText", status);
                A.Set(view, "brandWebsiteButton", website); A.Set(view, "brandWebsiteNote", websiteNote); A.Set(view, "brandPictureButton", brandPicture);
                A.Set(view, "productPhoto", photo); A.Set(view, "productPhotoPlaceholder", photoPlaceholder);
                A.Set(view, "productNameText", productName); A.Set(view, "cosmeticTypeText", cosmeticType); A.Set(view, "descriptionText", description);
                A.Set(view, "minimumAgeText", minimumAge); A.Set(view, "fdaNumberText", fda);
                A.Set(view, "productPhotoButton", photoLink); A.Set(view, "productPageButton", pageLink);
                A.Set(view, "primarySource", primary); A.Set(view, "secondarySource", secondary); A.Set(view, "thirdSource", third); A.Set(view, "noSourcesText", noSources);
                A.Set(view, "shadesCard", shadesCard);
                Kit.SetArray(view, "shadeSwatches", swatches);
                Kit.SetArray(view, "shadeLabels", labels);
                A.Set(view, "overviewCard", overviewCard);
                Kit.SetArray(view, "overviewTexts", notes);
                Kit.SetArray(view, "webCards", new[] { overviewCard, brandCard, productCard, sourcesCard, shadesCard });
            });
            Debug.Log("[GlowUp] WebSearchResultScene built: the AI Scan Result screen with the Brand, Product details, Sources and Shade on the pack cards under the verdict (WebSearchResultView fills them).");
        }

        /// <summary>A label over its value: the value wraps and pushes what follows down (no fixed height, so nothing overlaps). Returns the value.</summary>
        static TextMeshProUGUI Field(Transform parent, string name, string label, string sample)
        {
            GameObject row = A.Node(parent, name);
            A.VStack(row, 4);
            A.Label(row.transform, "Label", label, SmallSize, ThemeRole.SecondaryText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            return A.Label(row.transform, "Value", sample, BodySize, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
        }

        /// <summary>A full-width outlined button that opens a web page: the "opens a website" icon and the words, centred together.</summary>
        static Button LinkButton(Transform parent, string name, string words)
        {
            Button b = A.PillButton(parent, name, words, ButtonSize, ThemeRole.InputBackground, ThemeRole.MainText, IconFactory.ExternalLink);
            A.Size(b, -1, Tap);
            A.Outline(b.transform, ThemeRole.Accent, Tap);
            return b;
        }

        /// <summary>The usual "page" button of the result screen (site name, kind of page, arrow), full width.</summary>
        static SourceLinkButton SourceButton(Transform parent, string name, string site, string kind)
        {
            SourceLinkButton button = ScanResultSceneBuilder.SourceLink(parent, name);
            A.Size(button, -1, ChipHeight);
            TextMeshProUGUI[] texts = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length > 0) texts[0].text = site;
            if (texts.Length > 1) texts[1].text = kind;
            return button;
        }

        /// <summary>
        /// A round frame for a downloaded picture: a placeholder icon until the picture arrives, then the whole picture fitted inside
        /// (never stretched). `width` -1 = the full width of the card.
        /// </summary>
        static RawImage PictureFrame(Transform parent, string name, float width, float height, Sprite placeholderIcon, out GameObject placeholder)
        {
            Image frame = A.Panel(parent, name, ThemeRole.InputBackground, 1f, 40f);
            A.Size(frame, width, height);
            A.Outline(frame.transform, ThemeRole.SurfaceAlt, 40f);
            Image icon = A.Icon(frame.transform, "Placeholder", placeholderIcon, Mathf.Min(96f, height * 0.5f), ThemeRole.SecondaryText);
            A.At(icon.transform, A.MC, Vector2.zero, Vector2.one * Mathf.Min(96f, height * 0.5f));
            placeholder = icon.gameObject;
            GameObject holder = A.Node(frame.transform, "PictureArea");
            A.Fill(holder.transform, 14, 14, 14, 14);
            GameObject picture = A.Node(holder.transform, "Picture", typeof(RawImage), typeof(AspectRatioFitter));
            var raw = picture.GetComponent<RawImage>();
            raw.raycastTarget = false; raw.enabled = false;                       // shown when the picture has arrived
            var fit = picture.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 1f;
            return raw;
        }
    }
}
