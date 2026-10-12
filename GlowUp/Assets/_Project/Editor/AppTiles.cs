using GlowUp.Common;
using GlowUp.Scanning;
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
    /// Building blocks for product lists: the long rows that open downwards (dashboard recommendations and Recent
    /// Scans), the square tiles in a two-column grid (the product picker of AR Try-On, the alternatives on a result),
    /// the grey placeholders shown while a list loads, the "AI" pop-up, and the sideways-drag navigation between
    /// the main screens.
    /// Product tiles are compact, with smooth round corners, and carry their own buttons; pink is kept for
    /// the main button. A tap plays the selection animation of the Onboarding tiles (Steps 2 and 3).
    /// </summary>
    public static class AppTiles
    {
        public const float Cell = 440f, CellHeight = 300f, Gap = 22f;      // two tiles side by side inside a 920-wide list
        public const float TileRadius = 40f;

        public enum TileKind { Dashboard, RecentScan, Picker }

        /// <summary>A vertical scroll list whose content is a grid of square cells, two per row.</summary>
        public static ScrollRect GridScroll(Transform parent, string name, out RectTransform content)
        {
            ScrollRect scroll = A.Scroll(parent, name, out content, 0, 0, 18, 4, 28);
            var vertical = content.GetComponent<VerticalLayoutGroup>();
            if (vertical != null) Object.DestroyImmediate(vertical);
            var grid = SceneBuilderCore.Ensure<GridLayoutGroup>(content.gameObject);
            grid.cellSize = new Vector2(Cell, CellHeight); grid.spacing = new Vector2(Gap, Gap);
            grid.padding = new RectOffset(0, 18, 4, 28);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft; grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            return scroll;
        }

        // =====================================================================================
        //  Rows that open downwards (dashboard "Recommended for you", Recent Scans)
        // =====================================================================================
        public const float RowHeight = 128f;            // a closed row
        public const float RowActionsHeight = 96f;      // what an open row is taller by: the strip with its buttons
        public const float RowGap = 14f;

        /// <summary>A vertical scroll list of rows, one product per row.</summary>
        public static ScrollRect ListScroll(Transform parent, string name, out RectTransform content)
        {
            return A.Scroll(parent, name, out content, RowGap, 0, 18, 4, 28);
        }

        /// <summary>
        /// The pattern ROW: long and low, one product. Closed it shows only the kind's icon, the product's name and its
        /// brand (and a small arrow). A tap makes it longer downwards and uncovers the strip "Actions" with its buttons:
        /// Save, Try On and AI on the dashboard; Result, Try On and AI in Recent Scans. A second tap closes it.
        ///
        /// Row (its place in the list; its Layout Element is what gets taller) > Visual (everything that is seen; in Recent
        /// Scans it shakes and is dragged) with the texts and Actions > Buttons. In the Hierarchy the pattern row is left
        /// OPEN, so the buttons can be seen and styled; the app closes every row when it makes the list.
        /// </summary>
        public static ProductTileView ExpandingRow(Transform content, string name, TileKind kind)
        {
            var root = A.Node(content, name);
            LayoutElement rowLayout = A.Size(root.transform, -1, RowHeight + RowActionsHeight);
            rowLayout.flexibleHeight = 0;

            var visual = A.Node(root.transform, "Visual", typeof(Image), typeof(Button), typeof(CanvasGroup));
            A.Fill(visual.transform);
            var background = visual.GetComponent<Image>();
            Kit.StyleRounded(background); Kit.SetRadius(background, TileRadius); background.raycastTarget = true; background.color = Color.white;
            SceneBuilderCore.Themed(visual, ThemeRole.InputBackground);
            var rowButton = visual.GetComponent<Button>(); rowButton.targetGraphic = background; rowButton.transition = Selectable.Transition.None;
            A.Outline(visual.transform, ThemeRole.SurfaceAlt, TileRadius);

            // ---- what a closed row shows: the kind's icon, the name, the brand ----
            Image chipImage = A.Panel(visual.transform, "TypeChip", ThemeRole.SurfaceAlt, 1f, 26f);
            A.At(chipImage.transform, A.TL, new Vector2(20, -20), new Vector2(88, 88));
            Image typeIcon = A.Icon(chipImage.transform, "TypeIcon", IconFactory.Bottle, 54, ThemeRole.MainText);
            Image statusDot = A.RawPanel(visual.transform, "StatusDot", new Color32(94, 127, 75, 255), 18f);
            A.At(statusDot.transform, A.TL, new Vector2(94, -14), new Vector2(18, 18));
            statusDot.gameObject.SetActive(false);                 // shown only when the controller's "Show Status Dot" is on

            TextMeshProUGUI nameText = A.Label(visual.transform, "NameText", "Product name", 33, ThemeRole.MainText, FontRole.Heading);
            A.TopStrip(nameText.transform, 22, 46, 130, 104); A.Shrink(nameText, 25);
            TextMeshProUGUI brandText = A.Label(visual.transform, "BrandText", "Brand name", 27, ThemeRole.SecondaryText, FontRole.Label);
            A.TopStrip(brandText.transform, 70, 38, 130, 104); A.Shrink(brandText, 21);

            // the small arrow at the right end: it says "there is more", and turns upside down while the row is open
            Image chevron = A.Icon(visual.transform, "Chevron", IconFactory.ChevronDown, 40, ThemeRole.SecondaryText);
            var chevronRect = chevron.rectTransform;
            chevronRect.anchorMin = chevronRect.anchorMax = new Vector2(1, 1); chevronRect.pivot = new Vector2(0.5f, 0.5f);
            chevronRect.anchoredPosition = new Vector2(-52, -RowHeight * 0.5f); chevronRect.sizeDelta = new Vector2(40, 40);

            var view = SceneBuilderCore.Ensure<ProductTileView>(root);
            A.Set(view, "nameText", nameText); A.Set(view, "brandText", brandText);
            A.Set(view, "typeIcon", typeIcon); A.Set(view, "statusDot", statusDot); A.Set(view, "tileButton", rowButton);
            A.Set(view, "popTarget", (RectTransform)visual.transform); A.Set(view, "popGroup", visual.GetComponent<CanvasGroup>());

            // ---- the strip that is uncovered when the row opens: it hangs under the closed row and is cut off at its own edge ----
            var actions = A.Node(visual.transform, "Actions", typeof(RectMask2D), typeof(CanvasGroup));
            var actionsRect = (RectTransform)actions.transform;
            actionsRect.anchorMin = new Vector2(0, 1); actionsRect.anchorMax = new Vector2(1, 1); actionsRect.pivot = new Vector2(0.5f, 1);
            actionsRect.anchoredPosition = new Vector2(0, -RowHeight); actionsRect.sizeDelta = new Vector2(0, RowActionsHeight);
            var buttons = A.Node(actions.transform, "Buttons");
            var buttonsRect = (RectTransform)buttons.transform;
            buttonsRect.anchorMin = new Vector2(0, 1); buttonsRect.anchorMax = new Vector2(1, 1); buttonsRect.pivot = new Vector2(0.5f, 1);
            buttonsRect.anchoredPosition = new Vector2(0, -4); buttonsRect.sizeDelta = new Vector2(0, 72);
            A.HStack(buttons, 14, 20, 20, 0, 0, TextAnchor.MiddleCenter);

            if (kind == TileKind.RecentScan)
            {
                Button result = RowButton(buttons.transform, "ResultButton", "Result", IconFactory.Document, true, 1.2f);
                Button tryOn = RowButton(buttons.transform, "TryOnButton", "Try On", IconFactory.Sparkle, false, 1.2f);
                Button ai = RowButton(buttons.transform, "AiButton", "AI", IconFactory.Ai, false, 0.8f);
                A.Set(view, "resultButton", result); A.Set(view, "tryOnButton", tryOn); A.Set(view, "aiButton", ai);

                // the pencil appears while the rows shake (press and hold): rename on this phone only. It takes the arrow's place.
                Button rename = A.IconButton(visual.transform, "RenameButton", IconFactory.Pencil, 62, 34, ThemeRole.MainText, ThemeRole.SurfaceAlt);
                var renameRect = (RectTransform)rename.transform;
                renameRect.anchorMin = renameRect.anchorMax = new Vector2(1, 1); renameRect.pivot = new Vector2(0.5f, 0.5f);
                renameRect.anchoredPosition = new Vector2(-52, -RowHeight * 0.5f); renameRect.sizeDelta = new Vector2(62, 62);
                rename.gameObject.SetActive(false);
                A.Set(view, "renameButton", rename);

                // press and hold, then drag to the shadow to remove; the Visual gets its own drawing layer so it can pass over everything
                var layer = SceneBuilderCore.Ensure<Canvas>(visual);
                SceneBuilderCore.Ensure<GraphicRaycaster>(visual);
                var hold = SceneBuilderCore.Ensure<HoldToRemoveTile>(root);
                A.Set(hold, "visual", (RectTransform)visual.transform); A.Set(hold, "visualCanvas", layer);
            }
            else
            {
                Button save = RowButton(buttons.transform, "SaveButton", "Save", IconFactory.Plus, false, 1f);
                Button tryOn = RowButton(buttons.transform, "TryOnButton", "Try On", IconFactory.Sparkle, true, 1.3f);
                Button ai = RowButton(buttons.transform, "AiButton", "AI", IconFactory.Ai, false, 0.8f);
                A.Set(view, "saveButton", save); A.Set(view, "saveLabel", A.LabelOf(save));
                A.Set(view, "tryOnButton", tryOn); A.Set(view, "aiButton", ai);
            }

            A.Set(view, "actions", actionsRect); A.Set(view, "actionsGroup", actions.GetComponent<CanvasGroup>());
            A.Set(view, "rowLayout", rowLayout); A.Set(view, "chevron", chevronRect);
            A.Set(view, "closedHeight", RowHeight); A.Set(view, "actionsHeight", RowActionsHeight);
            return view;
        }

        /// <summary>A button of an open row: they share the row's width (`share` = how much of it this one takes).</summary>
        static Button RowButton(Transform row, string name, string label, Sprite icon, bool primary, float share)
        {
            Button button = A.PillButton(row, name, label, 27, primary ? ThemeRole.ButtonFill : ThemeRole.SurfaceAlt, primary ? ThemeRole.ButtonText : ThemeRole.MainText, icon);
            A.Shrink(A.LabelOf(button), 19);
            A.Size(button, 120, 70, share);
            return button;
        }

        /// <summary>Grey placeholder rows shown in the place of a row list while it loads (skeleton loading).</summary>
        public static GameObject SkeletonRows(Transform parent, string name, int count)
        {
            var holder = A.Node(parent, name, typeof(SkeletonShimmer), typeof(CanvasGroup));
            A.Fill(holder.transform);
            A.VStack(holder, RowGap, 0, 18, 4, 0);
            for (int i = 0; i < count; i++)
            {
                var row = A.Node(holder.transform, "Placeholder" + (i + 1), typeof(Image));
                var img = row.GetComponent<Image>(); Kit.StyleRounded(img); Kit.SetRadius(img, TileRadius); img.raycastTarget = false; img.color = new Color(1, 1, 1, 0.7f);
                SceneBuilderCore.Themed(row, ThemeRole.SurfaceAlt);
                A.Size(img, -1, RowHeight);
            }
            holder.GetComponent<CanvasGroup>().blocksRaycasts = false;
            return holder;
        }

        // =====================================================================================
        //  Square tiles (the product picker of AR Try-On, the alternatives on a result)
        // =====================================================================================
        /// <summary>
        /// The pattern tile. Tile (its place in the grid) > Visual (everything that is seen; it shakes and is
        /// dragged) with NeonOutline (the glow of the selection animation), the texts, the buttons, and
        /// SweepClip > PixelSweepCells (the squares of the sweep). Returns the tile's ProductTileView.
        /// </summary>
        public static ProductTileView ProductTile(Transform content, string name, TileKind kind)
        {
            var root = A.Node(content, name);
            A.Size(root.transform, Cell, CellHeight);

            var visual = A.Node(root.transform, "Visual", typeof(Image), typeof(Button), typeof(CanvasGroup));
            A.Fill(visual.transform);
            var background = visual.GetComponent<Image>();
            Kit.StyleRounded(background); Kit.SetRadius(background, TileRadius); background.raycastTarget = true; background.color = Color.white;
            SceneBuilderCore.Themed(visual, ThemeRole.InputBackground);
            var tileButton = visual.GetComponent<Button>(); tileButton.targetGraphic = background; tileButton.transition = Selectable.Transition.None;

            // the glowing ring of the selection animation: a little larger than the tile, invisible until the tile is tapped
            var neon = A.Node(visual.transform, "NeonOutline", typeof(Image));
            var neonImage = neon.GetComponent<Image>();
            neonImage.sprite = IconFactory.NeonOutline; neonImage.type = Image.Type.Sliced; neonImage.raycastTarget = false; neonImage.color = Color.clear;
            float halo = IconFactory.NeonOutlineMargin * TileRadius / PillCorners.SpriteRadius;
            A.Fill(neon.transform, -halo, -halo, -halo, -halo);
            var neonCorners = SceneBuilderCore.Ensure<PillCorners>(neon);
            A.Set(neonCorners, "spriteMargin", IconFactory.NeonOutlineMargin); A.Set(neonCorners, "maxRadius", TileRadius);

            A.Outline(visual.transform, ThemeRole.SurfaceAlt, TileRadius);

            // ---- what the tile shows: icon, name, brand, kind ----
            Image chipImage = A.Panel(visual.transform, "TypeChip", ThemeRole.SurfaceAlt, 1f, 22f);
            A.At(chipImage.transform, A.TL, new Vector2(20, -20), new Vector2(70, 70));
            Image typeIcon = A.Icon(chipImage.transform, "TypeIcon", IconFactory.Bottle, 42, ThemeRole.MainText);

            Image statusDot = A.RawPanel(visual.transform, "StatusDot", new Color32(94, 127, 75, 255), 18f);
            A.At(statusDot.transform, A.TL, new Vector2(74, -16), new Vector2(18, 18));
            statusDot.gameObject.SetActive(false);                 // shown only when the controller's "Show Status Dot" is on

            TextMeshProUGUI nameText = A.Label(visual.transform, "NameText", "Product name", 32, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.Left, true);
            A.TopStrip(nameText.transform, 16, 78, 104, 22);
            A.ShrinkWrapped(nameText, 24); nameText.maxVisibleLines = 2;
            TextMeshProUGUI brandText = A.Label(visual.transform, "BrandText", "Brand name", 27, ThemeRole.MainText, FontRole.Label);
            A.TopStrip(brandText.transform, 104, 38, 24, 22); A.Shrink(brandText, 21);
            TextMeshProUGUI typeText = A.Label(visual.transform, "TypeText", "Cosmetic type", 25, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(typeText.transform, 142, 36, 24, 22); A.Shrink(typeText, 20);

            var view = SceneBuilderCore.Ensure<ProductTileView>(root);
            A.Set(view, "nameText", nameText); A.Set(view, "brandText", brandText); A.Set(view, "typeText", typeText);
            A.Set(view, "typeIcon", typeIcon); A.Set(view, "statusDot", statusDot); A.Set(view, "tileButton", tileButton);
            A.Set(view, "popTarget", (RectTransform)visual.transform); A.Set(view, "popGroup", visual.GetComponent<CanvasGroup>());

            // ---- the tile's own buttons, along its bottom edge ----
            const float buttonHeight = 66, edge = 18;
            if (kind == TileKind.RecentScan)
            {
                Button tryOn = TileButton(visual.transform, "TryOnButton", "Try On", IconFactory.Sparkle, true);
                A.At(tryOn.transform, A.BL, new Vector2(edge, edge), new Vector2(236, buttonHeight));
                Button ai = TileButton(visual.transform, "AiButton", "AI", IconFactory.Ai, false);
                A.At(ai.transform, A.BR, new Vector2(-edge, edge), new Vector2(156, buttonHeight));
                // the pencil appears while the tiles shake (press and hold): rename on this phone only
                Button rename = A.IconButton(visual.transform, "RenameButton", IconFactory.Pencil, 62, 34, ThemeRole.MainText, ThemeRole.SurfaceAlt);
                A.At(rename.transform, A.TR, new Vector2(-10, -10), new Vector2(62, 62));
                rename.gameObject.SetActive(false);
                A.Set(view, "tryOnButton", tryOn); A.Set(view, "aiButton", ai); A.Set(view, "renameButton", rename);

                // press and hold, then drag left to remove; the Visual gets its own drawing layer so it can pass over its neighbours
                var layer = SceneBuilderCore.Ensure<Canvas>(visual);
                SceneBuilderCore.Ensure<GraphicRaycaster>(visual);
                var hold = SceneBuilderCore.Ensure<HoldToRemoveTile>(root);
                A.Set(hold, "visual", (RectTransform)visual.transform); A.Set(hold, "visualCanvas", layer);
            }
            else if (kind == TileKind.Picker)
            {
                // one button across the tile: "take this one"
                Button pick = TileButton(visual.transform, "TryOnButton", "Try this", IconFactory.Sparkle, true);
                A.BottomStrip(pick.transform, edge, buttonHeight, edge, edge);
                A.Set(view, "tryOnButton", pick);
            }
            else
            {
                Button save = TileButton(visual.transform, "SaveButton", "Save", IconFactory.Plus, false);
                A.At(save.transform, A.BL, new Vector2(edge - 2, edge), new Vector2(128, buttonHeight));
                Button tryOn = TileButton(visual.transform, "TryOnButton", "Try On", IconFactory.Sparkle, true);
                A.At(tryOn.transform, A.BL, new Vector2(edge + 134, edge), new Vector2(158, buttonHeight));
                Button ai = TileButton(visual.transform, "AiButton", "AI", IconFactory.Ai, false);
                A.At(ai.transform, A.BR, new Vector2(-edge + 2, edge), new Vector2(104, buttonHeight));
                A.Set(view, "saveButton", save); A.Set(view, "saveLabel", A.LabelOf(save));
                A.Set(view, "tryOnButton", tryOn); A.Set(view, "aiButton", ai);
                A.Set(view, "staysSelected", true);              // the tapped recommendation keeps its glowing outline
            }

            // ---- the squares of the pixel sweep, cut to the tile's round shape (drawn over everything, never tappable) ----
            var clip = A.Node(visual.transform, "SweepClip", typeof(Image), typeof(Mask));
            A.Fill(clip.transform);
            var clipImage = clip.GetComponent<Image>();
            Kit.StyleRounded(clipImage); Kit.SetRadius(clipImage, TileRadius); clipImage.raycastTarget = false;
            clip.GetComponent<Mask>().showMaskGraphic = false;
            var cells = A.Node(clip.transform, "PixelSweepCells");
            A.Fill(cells.transform);

            var sweep = SceneBuilderCore.Ensure<PixelSweepTileAnimator>(root);
            A.Set(sweep, "cellContainer", (RectTransform)cells.transform); A.Set(sweep, "neonOutline", neonImage);
            A.Set(view, "selectAnimator", sweep);
            return view;
        }

        /// <summary>A small round-ended button that sits on a product tile.</summary>
        static Button TileButton(Transform tile, string name, string label, Sprite icon, bool primary)
        {
            Button button = A.PillButton(tile, name, label, 25, primary ? ThemeRole.ButtonFill : ThemeRole.SurfaceAlt, primary ? ThemeRole.ButtonText : ThemeRole.MainText, icon);
            A.Shrink(A.LabelOf(button), 18);
            return button;
        }

        /// <summary>Grey placeholder tiles shown in the place of a tile list while it loads (skeleton loading).</summary>
        public static GameObject SkeletonTiles(Transform parent, string name, int count)
        {
            var holder = A.Node(parent, name, typeof(GridLayoutGroup), typeof(SkeletonShimmer), typeof(CanvasGroup));
            A.Fill(holder.transform);
            var grid = holder.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Cell, CellHeight); grid.spacing = new Vector2(Gap, Gap); grid.padding = new RectOffset(0, 18, 4, 0);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            for (int i = 0; i < count; i++)
            {
                var tile = A.Node(holder.transform, "Placeholder" + (i + 1), typeof(Image));
                var img = tile.GetComponent<Image>(); Kit.StyleRounded(img); Kit.SetRadius(img, TileRadius); img.raycastTarget = false; img.color = new Color(1, 1, 1, 0.7f);
                SceneBuilderCore.Themed(tile, ThemeRole.SurfaceAlt);
            }
            holder.GetComponent<CanvasGroup>().blocksRaycasts = false;
            return holder;
        }

        // =====================================================================================
        //  The "AI" pop-up
        // =====================================================================================
        public static AiInsightPanel AiPanel(A.AppScreen s)
        {
            A.AppPopup p = A.Overlay(s, "AiInsightPanel", "Product name");
            A.Fill(p.box, 40, 230, 40, 230);

            TextMeshProUGUI subtitle = A.Label(p.box, "ProductLineText", "Brand  |  Cosmetic type", 30, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(subtitle.transform, 112, 46, 48, 48); A.Shrink(subtitle, 24);

            var area = A.Node(p.box, "AnswerArea");
            A.Fill(area.transform, 40, 190, 40, 180);
            ScrollRect scroll = A.Scroll(area.transform, "AnswerScrollView", out RectTransform content, 18, 4, 26, 6, 24);
            TextMeshProUGUI body = A.Label(content, "AnswerText",
                "What it is: the AI's short description of the product shows here.\nGood for: who it suits.\nTake care: what to watch out for.\nHow to use: a short tip.",
                32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI note = A.Label(content, "AnswerNoteText", "Checked with a live web search. General information, not medical advice.", 26,
                                           ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI sourcesHeader = A.Label(content, "SourcesHeaderText", "Sources", 32, ThemeRole.Heading, FontRole.Heading);
            A.Size(sourcesHeader.transform, -1, 50);
            Button source = A.LinkButton(content, "SourceTemplate", "source name", 28, ThemeRole.AccentText);
            A.Size(source.transform, -1, 60);
            A.LabelOf(source).alignment = TextAlignmentOptions.MidlineLeft;

            Image busy = A.Icon(p.box, "BusyIndicator", IconFactory.SpinnerArc, 76, ThemeRole.Accent);
            A.At(busy.transform, A.BC, new Vector2(0, 80), new Vector2(76, 76));
            busy.gameObject.SetActive(false);
            Button retry = A.PillButton(p.box, "RetryButton", "Ask again", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Sync);
            A.BottomStrip(retry.transform, 44, 100, 220, 220);

            var panel = SceneBuilderCore.Ensure<AiInsightPanel>(p.root);
            A.Set(panel, "panel", p.panel); A.Set(panel, "titleText", p.title); A.Set(panel, "subtitleText", subtitle);
            A.Set(panel, "bodyText", body); A.Set(panel, "noteText", note); A.Set(panel, "busyIndicator", busy.rectTransform);
            A.Set(panel, "retryButton", retry); A.Set(panel, "scroll", scroll);
            A.Set(panel, "sourcesHeader", sourcesHeader); A.Set(panel, "sourcesContent", content); A.Set(panel, "sourceTemplate", source);
            GeminiApiConfig config = AiConfig();
            if (config != null) A.Set(panel, "aiConfig", config);
            return panel;
        }

        /// <summary>The project's AI settings asset (models, timeout).</summary>
        public static GeminiApiConfig AiConfig()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GeminiApiConfig"))
            {
                var config = AssetDatabase.LoadAssetAtPath<GeminiApiConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) return config;
            }
            return null;
        }

        // =====================================================================================
        //  Sideways drag between the main screens
        // =====================================================================================
        /// <summary>Adds the object "SwipeNavigation": dragging the screen left or right opens the neighbouring main screen.</summary>
        public static SwipeSceneNavigator SwipeNavigation(A.AppScreen s, BottomActionBar bar)
        {
            var holder = A.Node(s.canvas, "SwipeNavigation", typeof(SwipeSceneNavigator));
            A.Fill(holder.transform);
            var swipe = holder.GetComponent<SwipeSceneNavigator>();
            A.Set(swipe, "page", (RectTransform)s.card);
            A.Set(swipe, "pageGroup", SceneBuilderCore.Ensure<CanvasGroup>(s.card.gameObject));
            if (bar != null) A.Set(swipe, "tabBar", bar.GetComponent<LiquidTabBar>());
            return swipe;
        }
    }
}
