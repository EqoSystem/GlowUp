using System.Collections.Generic;
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
                // Phase 1D (11 November 2026): no "AI" button on a scan card; Result and Try On share the row's width evenly
                Button result = RowButton(buttons.transform, "ResultButton", "Result", IconFactory.Document, true, 1f);
                Button tryOn = RowButton(buttons.transform, "TryOnButton", "Try On", IconFactory.Sparkle, false, 1f);
                buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
                A.Set(view, "resultButton", result); A.Set(view, "tryOnButton", tryOn); A.Set(view, "aiButton", (Button)null);

                // the pencil appears while the rows shake (press and hold): rename on this phone only. It takes the arrow's place.
                Button rename = A.IconButton(visual.transform, "RenameButton", IconFactory.Pencil, 62, 34, ThemeRole.MainText, ThemeRole.SurfaceAlt);
                var renameRect = (RectTransform)rename.transform;
                renameRect.anchorMin = renameRect.anchorMax = new Vector2(1, 1); renameRect.pivot = new Vector2(0.5f, 0.5f);
                renameRect.anchoredPosition = new Vector2(-52, -RowHeight * 0.5f); renameRect.sizeDelta = new Vector2(62, 62);
                rename.gameObject.SetActive(false);
                A.Set(view, "renameButton", rename);

                // selecting several rows (the Delete button of the screen): a check mark takes the icon's place on a selected row,
                // and the row that is dragged for the whole group carries a small number. Both are switched off until needed.
                Image mark = A.Panel(visual.transform, "SelectedMark", ThemeRole.ButtonFill, 1f, 26f);
                A.At(mark.transform, A.TL, new Vector2(20, -20), new Vector2(88, 88));
                A.Icon(mark.transform, "Icon", IconFactory.Check, 50, ThemeRole.ButtonText);
                mark.gameObject.SetActive(false);
                Image badge = A.Panel(visual.transform, "GroupBadge", ThemeRole.ButtonFill, 1f, 30f);
                A.At(badge.transform, A.TR, new Vector2(10, 14), new Vector2(76, 60));
                TextMeshProUGUI badgeText = A.Label(badge.transform, "CountText", "3", 32, ThemeRole.ButtonText, FontRole.Label, TextAlignmentOptions.Center);
                A.Fill(badgeText.transform);
                badge.gameObject.SetActive(false);
                A.Set(view, "selectedMark", mark.gameObject); A.Set(view, "groupBadge", badge.gameObject); A.Set(view, "groupBadgeText", badgeText);

                // press and hold, then drag to the shadow to remove; the Visual gets its own drawing layer so it can pass over everything
                var layer = SceneBuilderCore.Ensure<Canvas>(visual);
                SceneBuilderCore.Ensure<GraphicRaycaster>(visual);
                var hold = SceneBuilderCore.Ensure<HoldToRemoveTile>(root);
                A.Set(hold, "visual", (RectTransform)visual.transform); A.Set(hold, "visualCanvas", layer);
                A.Set(hold, "shakeDegrees", 0.55f);                 // a long row tilts far less than a square tile, or its ends would swing
            }
            else
            {
                // 18 Nov 2026 (owner): the same buttons as the Alternatives pop-up: Save (icon only) | Try On | Result (icon only).
                // Result opens the product's AI Smart Scan Result, as it does on an alternative.
                Button save = RowButton(buttons.transform, "SaveButton", "Save", IconFactory.Plus, false, 0f);
                Button tryOn = RowButton(buttons.transform, "TryOnButton", "Try On", IconFactory.Sparkle, true, 1f);
                Button result = RowButton(buttons.transform, "ResultButton", "Result", IconFactory.Document, false, 0f);
                IconOnly(save); IconOnly(result);
                buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                A.Set(view, "saveButton", save); A.Set(view, "saveLabel", A.LabelOf(save));
                A.Set(view, "tryOnButton", tryOn); A.Set(view, "resultButton", result); A.Set(view, "aiButton", (Button)null);
            }

            A.Set(view, "actions", actionsRect); A.Set(view, "actionsGroup", actions.GetComponent<CanvasGroup>());
            A.Set(view, "rowLayout", rowLayout); A.Set(view, "chevron", chevronRect);
            A.Set(view, "closedHeight", RowHeight); A.Set(view, "actionsHeight", RowActionsHeight);
            return view;
        }

        /// <summary>A row button that shows only its icon, in a round 70-unit button. The label object stays (hidden): "Saved" can still be written into it.</summary>
        static void IconOnly(Button b)
        {
            TextMeshProUGUI label = A.LabelOf(b);
            if (label != null) label.gameObject.SetActive(false);
            Transform content = b.transform.Find("Content");
            if (content != null) A.Fill(content);
            Transform icon = content != null ? content.Find("Icon") : null;
            if (icon != null) A.Size(icon, 36, 36);
            A.Size(b, 70, 70, 0f);
        }

        /// <summary>A button of an open row: they share the row's width (`share` = how much of it this one takes).</summary>
        static Button RowButton(Transform row, string name, string label, Sprite icon, bool primary, float share)
        {
            Button button = A.PillButton(row, name, label, 27, primary ? ThemeRole.ButtonFill : ThemeRole.SurfaceAlt, primary ? ThemeRole.ButtonText : ThemeRole.MainText, icon);
            A.Shrink(A.LabelOf(button), 19);
            A.Size(button, 120, 70, share);
            return button;
        }

        /// <summary>
        /// Placeholder rows shown in the place of a row list while its products are fetched. A row keeps the look of a real
        /// product row (its panel is part of the screen, not of what is fetched); what IS fetched stands in it as bars with
        /// the travelling shine: the kind's icon, the name and the brand.
        /// </summary>
        public static GameObject SkeletonRows(Transform parent, string name, int count)
        {
            var holder = A.Node(parent, name, typeof(SkeletonShimmer), typeof(CanvasGroup));
            A.Fill(holder.transform);
            A.VStack(holder, RowGap, 0, 18, 4, 0);
            Material shine = UiEffectsKit.SkeletonMaterial();
            var bars = new List<Object>();
            for (int i = 0; i < count; i++)
            {
                var row = A.Node(holder.transform, "Placeholder" + (i + 1), typeof(Image));
                var frame = row.GetComponent<Image>(); Kit.StyleRounded(frame); Kit.SetRadius(frame, TileRadius); frame.raycastTarget = false; frame.color = Color.white;
                SceneBuilderCore.Themed(row, ThemeRole.InputBackground);
                A.Outline(row.transform, ThemeRole.SurfaceAlt, TileRadius);
                A.Size(frame, -1, RowHeight);
                // the same places as in a real row (ExpandingRow): icon chip, name, brand. The lengths differ a little from row
                // to row, so the list does not look ruled.
                bars.Add(Bar(row.transform, "IconBar", shine, 26f, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -108), new Vector2(108, -20)));
                bars.Add(Bar(row.transform, "NameBar", shine, 15f, new Vector2(0, 1), new Vector2(0.66f - (i % 3) * 0.09f, 1), new Vector2(130, -62), new Vector2(0, -32)));
                bars.Add(Bar(row.transform, "BrandBar", shine, 12f, new Vector2(0, 1), new Vector2(0.42f - (i % 2) * 0.07f, 1), new Vector2(130, -100), new Vector2(0, -78)));
            }
            Kit.SetArray(holder.GetComponent<SkeletonShimmer>(), "shapes", bars.ToArray());        // the bars shine; the rows' panels do not
            A.Set(holder.GetComponent<SkeletonShimmer>(), "shine", shine);
            holder.GetComponent<CanvasGroup>().blocksRaycasts = false;
            return holder;
        }

        /// <summary>One bar of a placeholder row or tile: a round shape drawn with the travelling shine.</summary>
        static Image Bar(Transform parent, string name, Material shine, float radius, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image bar = A.RawPanel(parent, name, Color.white, radius);
            var rect = bar.rectTransform;
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = new Vector2(0, 1);
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            if (shine != null) bar.material = shine;
            else SceneBuilderCore.Themed(bar.gameObject, ThemeRole.SurfaceAlt);              // no shine material: a plain soft shape
            return bar;
        }

        /// <summary>The texts a component fills with fetched data, read from its Inspector fields (for WordSkeleton).</summary>
        public static Object[] TextsOf(Object component, params string[] fields)
        {
            var found = new List<Object>();
            if (component == null) return found.ToArray();
            var so = new SerializedObject(component);
            foreach (string field in fields)
            {
                SerializedProperty p = so.FindProperty(field);
                if (p == null) { Debug.LogWarning($"[SceneBuilder] {component.GetType().Name} has no field '{field}' (asked for by the text skeleton)"); continue; }
                if (p.objectReferenceValue is TMP_Text) found.Add(p.objectReferenceValue);
            }
            return found.ToArray();
        }

        /// <summary>
        /// Adds the object "TextSkeleton" over the screen's card. It shows the texts that hold FETCHED data as bars with a
        /// travelling shine while that data is being fetched, the way the admin website loads: the frame first, the data
        /// after it. Call it LAST, when the screen's controllers are wired.
        ///
        /// `fetchedTexts` = ONLY texts that are filled from the database (use TextsOf). The project owner's rule of
        /// 13 October 2026: titles, labels, buttons, the bottom bar and the panel of a product row are never bars. A list has
        /// placeholder rows instead (`showWhileLoading`), and the real list can be kept out of sight meanwhile
        /// (`hideWhileLoading`). When the data is on the phone already nothing of this is ever seen.
        /// </summary>
        public static TextSkeleton WordSkeleton(A.AppScreen s, Object[] fetchedTexts, GameObject[] showWhileLoading = null, CanvasGroup[] hideWhileLoading = null)
        {
            var holder = A.Node(s.card, "TextSkeleton", typeof(CanvasRenderer), typeof(TextSkeleton));
            A.Fill(holder.transform);
            holder.transform.SetAsLastSibling();                                                // drawn over everything on the card
            var skeleton = holder.GetComponent<TextSkeleton>();
            skeleton.raycastTarget = false;
            Material shine = UiEffectsKit.SkeletonMaterial();
            if (shine != null) skeleton.material = shine;
            A.Set(skeleton, "roundSprite", IconFactory.Pill);
            Kit.SetArray(skeleton, "texts", fetchedTexts ?? new Object[0]);
            Kit.SetArray(skeleton, "showWhileLoading", showWhileLoading ?? new Object[0]);
            Kit.SetArray(skeleton, "hideWhileLoading", hideWhileLoading ?? new Object[0]);
            A.Set(skeleton, "whenScreenOpens", (int)TextSkeleton.OnOpen.BarsWhileFetching);
            A.Set(skeleton, "slowAfterSeconds", 0f);
            A.Set(skeleton, "shortestSeconds", 0.2f);
            EditorUtility.SetDirty(skeleton);
            return skeleton;
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
                // New20: "Result" in place of "AI". The product is already checked against the user's skin profile, so the tap
                // opens its AI Smart Scan Result (the same screen a scan ends on) instead of asking the AI again.
                Button result = TileButton(visual.transform, "ResultButton", "Result", IconFactory.Document, false);
                A.At(result.transform, A.BR, new Vector2(-edge + 2, edge), new Vector2(104, buttonHeight));
                A.Set(view, "saveButton", save); A.Set(view, "saveLabel", A.LabelOf(save));
                A.Set(view, "tryOnButton", tryOn); A.Set(view, "resultButton", result);
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

        /// <summary>
        /// 17 November 2026 (Alternatives pop-up): "Save" and "Result" show only their icons (plus and document), in round 66-unit
        /// buttons at the two bottom corners, and "Try On" takes the room between them, so no label is cut off on a narrow tile.
        /// The label objects stay (hidden): the controller may still write "Saved" into Save's label, and that state shows as the
        /// greyed, untappable button.
        /// </summary>
        public static void IconOnlySaveAndResult(ProductTileView tile)
        {
            const float size = 66, edge = 18, gap = 10;
            foreach (Button b in tile.GetComponentsInChildren<Button>(true))
            {
                if (b.name == "SaveButton" || b.name == "ResultButton")
                {
                    bool left = b.name == "SaveButton";
                    A.At(b.transform, left ? A.BL : A.BR, new Vector2(left ? edge : -edge, edge), new Vector2(size, size));
                    TextMeshProUGUI label = A.LabelOf(b);
                    if (label != null) label.gameObject.SetActive(false);
                    Transform content = b.transform.Find("Content");
                    if (content != null) A.Fill(content);                                  // the icon sits in the middle of the round button
                    Transform icon = content != null ? content.Find("Icon") : null;
                    if (icon != null) A.Size(icon, 34, 34);
                }
                else if (b.name == "TryOnButton")
                {
                    var rt = (RectTransform)b.transform;
                    rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
                    rt.offsetMin = new Vector2(edge + size + gap, edge); rt.offsetMax = new Vector2(-(edge + size + gap), edge + size);
                }
            }
        }

        /// <summary>A small round-ended button that sits on a product tile.</summary>
        static Button TileButton(Transform tile, string name, string label, Sprite icon, bool primary)
        {
            Button button = A.PillButton(tile, name, label, 25, primary ? ThemeRole.ButtonFill : ThemeRole.SurfaceAlt, primary ? ThemeRole.ButtonText : ThemeRole.MainText, icon);
            A.Shrink(A.LabelOf(button), 18);
            return button;
        }

        /// <summary>
        /// Placeholder tiles shown in the place of a tile list while its products are fetched. As with the rows: the tile
        /// keeps its panel, and the fetched things in it (icon, name, brand) are bars with the travelling shine.
        /// </summary>
        public static GameObject SkeletonTiles(Transform parent, string name, int count)
        {
            var holder = A.Node(parent, name, typeof(GridLayoutGroup), typeof(SkeletonShimmer), typeof(CanvasGroup));
            A.Fill(holder.transform);
            var grid = holder.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Cell, CellHeight); grid.spacing = new Vector2(Gap, Gap); grid.padding = new RectOffset(0, 18, 4, 0);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            Material shine = UiEffectsKit.SkeletonMaterial();
            var bars = new List<Object>();
            for (int i = 0; i < count; i++)
            {
                var tile = A.Node(holder.transform, "Placeholder" + (i + 1), typeof(Image));
                var frame = tile.GetComponent<Image>(); Kit.StyleRounded(frame); Kit.SetRadius(frame, TileRadius); frame.raycastTarget = false; frame.color = Color.white;
                SceneBuilderCore.Themed(tile, ThemeRole.InputBackground);
                A.Outline(tile.transform, ThemeRole.SurfaceAlt, TileRadius);
                bars.Add(Bar(tile.transform, "IconBar", shine, 22f, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -90), new Vector2(90, -20)));
                bars.Add(Bar(tile.transform, "NameBar", shine, 14f, new Vector2(0, 1), new Vector2(0.9f - (i % 3) * 0.1f, 1), new Vector2(106, -66), new Vector2(0, -38)));
                bars.Add(Bar(tile.transform, "BrandBar", shine, 12f, new Vector2(0, 1), new Vector2(0.62f - (i % 2) * 0.1f, 1), new Vector2(24, -134), new Vector2(0, -112)));
            }
            Kit.SetArray(holder.GetComponent<SkeletonShimmer>(), "shapes", bars.ToArray());
            A.Set(holder.GetComponent<SkeletonShimmer>(), "shine", shine);
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
