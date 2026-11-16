using GlowUp.Common;
using GlowUp.Recent;
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
    /// Menu: GlowUp > Build Scene > 9 Recent Scans.
    /// The saved products as square tiles, two per row (ScanTileTemplate is the pattern tile), the Sort and
    /// Clear All buttons, the shadow that shows where to drag a tile to remove it, the bar that brings
    /// removed items back, the empty state, the rename dialog and the AI pop-up.
    /// </summary>
    public static class RecentScansSceneBuilder
    {
        [MenuItem("GlowUp/Build Scene/9 Recent Scans", false, 119)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("RecentScanActivitiesScene", "Recent Scans", true, "RecentScansCard", "LogInCard", "RecentScansCard");
            Transform card = s.card;

            // ---- count, Sort, Clear All (Done takes the place of Clear All while the tiles shake) ----
            TextMeshProUGUI count = A.Label(card, "CountText", "3 saved", 34, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(count.transform, 160, 68, 52, 660);
            Button clearAll = A.PillButton(card, "ClearAllButton", "Clear All", 28, ThemeRole.SurfaceAlt, ThemeRole.Warning, IconFactory.Trash);
            A.At(clearAll.transform, A.TR, new Vector2(-A.Pad, -160), new Vector2(226, 68));
            Button sort = A.PillButton(card, "SortButton", "Sort: Newest first", 28, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Swap);
            A.At(sort.transform, A.TR, new Vector2(-A.Pad - 240, -160), new Vector2(372, 68));
            A.Shrink(A.LabelOf(sort), 20);
            Button done = A.PillButton(card, "DoneButton", "Done", 30, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.At(done.transform, A.TR, new Vector2(-A.Pad, -160), new Vector2(226, 68));
            done.gameObject.SetActive(false);             // shown while the tiles shake

            // ---- several at once: the Delete button (top right, the mirror of Back) starts selection mode; while it is on, this
            //      strip takes the place of the count and Sort: how many are selected, Select all, Cancel ----
            Button select = A.IconButton(card, "DeleteButton", IconFactory.Trash, 92, 52, ThemeRole.MainText);
            A.At(select.transform, A.TR, new Vector2(-28, -28), new Vector2(92, 92));
            GameObject selectionBar = A.Node(card, "SelectionBar");
            A.TopStrip(selectionBar.transform, 160, 68, 52, A.Pad);
            TextMeshProUGUI selectionText = A.Label(selectionBar.transform, "SelectionText", "3 selected", 34, ThemeRole.MainText, FontRole.Label);
            A.Fill(selectionText.transform, 0, 0, 450, 0);
            A.Shrink(selectionText, 22);
            Button selectAll = A.PillButton(selectionBar.transform, "SelectAllButton", "Select all", 28, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.ListIcon);
            A.At(selectAll.transform, A.MR, new Vector2(-206, 0), new Vector2(236, 68));
            Button cancelSelection = A.PillButton(selectionBar.transform, "CancelSelectionButton", "Cancel", 28, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Close);
            A.At(cancelSelection.transform, A.MR, new Vector2(0, 0), new Vector2(194, 68));
            selectionBar.SetActive(false);                // shown while scans are being selected

            TextMeshProUGUI listHint = A.Label(card, "ListHintText", "Tap a scan to open it. Press and hold one, then drag it into the shadow to remove it.", 26,
                                               ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(listHint.transform, 234, 40);
            A.Shrink(listHint, 18); A.HintOnly(listHint);

            // ---- the tiles ----
            GameObject listArea = A.Node(card, "ScanListArea");
            A.Between(listArea.transform, 282, A.AboveBar);
            // long rows, one scan each: a tap makes the row longer and shows Result / Try On / AI
            AppTiles.ListScroll(listArea.transform, "ScanHistoryList", out RectTransform content);
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 18, 4, 140);   // room for the restore bar under the last row
            ProductTileView template = AppTiles.ExpandingRow(content, "ScanTileTemplate", AppTiles.TileKind.RecentScan);
            // placeholder rows (a row's panel with bars inside) and a way to keep the real rows out of sight meanwhile:
            // TextSkeleton uses both only while the saved scans are really being fetched
            GameObject listSkeleton = AppTiles.SkeletonRows(listArea.transform, "ScanListSkeleton", 5);
            listSkeleton.SetActive(false);
            CanvasGroup listGroup = SceneBuilderCore.Ensure<CanvasGroup>(content.gameObject);

            // ---- the shadow over the left half: "drag a tile here to remove it" (invisible until the tiles shake) ----
            GameObject zone = A.Node(listArea.transform, "RemoveZone", typeof(Image), typeof(CanvasGroup));
            var zoneRect = (RectTransform)zone.transform;
            zoneRect.anchorMin = new Vector2(0, 0); zoneRect.anchorMax = new Vector2(0.5f, 1); zoneRect.pivot = new Vector2(0, 0.5f);
            zoneRect.offsetMin = new Vector2(-A.Pad, 0); zoneRect.offsetMax = Vector2.zero;        // from the panel's left edge to the middle of the screen
            var shade = zone.GetComponent<Image>(); shade.sprite = IconFactory.ShadowFromLeft; shade.type = Image.Type.Simple; shade.raycastTarget = false;
            shade.color = new Color(0.16f, 0.11f, 0.13f, 1f);
            Image zoneDisc = A.Panel(zone.transform, "TrashDisc", ThemeRole.Background, 1f, 150f);
            A.At(zoneDisc.transform, A.ML, new Vector2(64, 60), new Vector2(150, 150));
            A.Outline(zoneDisc.transform, ThemeRole.Warning, 150f);
            A.Icon(zoneDisc.transform, "Icon", IconFactory.Trash, 78, ThemeRole.Warning);
            TextMeshProUGUI zoneText = A.RawLabel(zone.transform, "RemoveZoneText", "Drag here\nto remove", 34, Color.white, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            A.At(zoneText.transform, A.ML, new Vector2(44, -90), new Vector2(300, 110));
            var zoneGroup = zone.GetComponent<CanvasGroup>(); zoneGroup.alpha = 0f; zoneGroup.blocksRaycasts = false; zoneGroup.interactable = false;

            // ---- empty state ----
            GameObject empty = A.Node(listArea.transform, "EmptyState");
            A.At(empty.transform, A.MC, new Vector2(0, 40), new Vector2(760, 430));
            Image emptyIcon = A.Icon(empty.transform, "EmptyIcon", IconFactory.History, 130, ThemeRole.SecondaryText);
            A.At(emptyIcon.transform, A.TC, new Vector2(0, -10), new Vector2(130, 130));
            TextMeshProUGUI emptyTitle = A.Label(empty.transform, "EmptyTitleText", "No scans yet", 50, ThemeRole.Heading, FontRole.Title, TextAlignmentOptions.Center);
            A.TopStrip(emptyTitle.transform, 166, 78, 0, 0);
            TextMeshProUGUI emptyText = A.Label(empty.transform, "EmptyBodyText", "Products you scan are saved here, so you can check them again at any time.",
                                                32, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(emptyText.transform, 254, 130, 0, 0);
            CanvasGroup emptyGroup = SceneBuilderCore.Ensure<CanvasGroup>(empty);       // out of sight while the scans are still being fetched
            empty.SetActive(false);                       // the app shows it when the list is empty

            // ---- the bar that brings removed items back ----
            Image restoreBar = A.Panel(listArea.transform, "RestoreBar", ThemeRole.MainText, 0.96f, 96f);
            restoreBar.raycastTarget = true;
            A.BottomStrip(restoreBar.transform, 16, 96, 0, 18);
            TextMeshProUGUI restoreText = A.Label(restoreBar.transform, "RestoreText", "2 removed", 30, ThemeRole.Background, FontRole.Label);
            A.Fill(restoreText.transform, 36, 0, 470, 0);
            Button deleteForever = A.LinkButton(restoreBar.transform, "DeleteForeverButton", "Delete forever", 27, ThemeRole.Background);
            A.At(deleteForever.transform, A.MR, new Vector2(-226, 0), new Vector2(236, 68));
            Button restore = A.PillButton(restoreBar.transform, "RestoreButton", "Restore", 30, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.At(restore.transform, A.MR, new Vector2(-16, 0), new Vector2(196, 66));

            BottomActionBar bar = A.BottomBar(s, 1, 85f);      // the liquid drop is 85 x 85 on Recent Scans and the dashboard (owner, 11 November 2026)
            AppTiles.SwipeNavigation(s, bar);

            // ---- pop-ups ----
            OverlayPanel renamePanel = A.RenameDialog(s, out TMP_InputField renameInput, out Button renameSave);
            AiInsightPanel aiPanel = AppTiles.AiPanel(s);

            // ---- controller ----
            var controller = A.Controller<RecentScansFlowController>("RecentScansFlowController");
            A.Set(controller, "listContent", content); A.Set(controller, "tileTemplate", template);
            A.Set(controller, "countText", count); A.Set(controller, "emptyState", empty);
            A.Set(controller, "typeIcons", A.TypeIcons());
            A.Set(controller, "sortButton", sort); A.Set(controller, "sortLabel", A.LabelOf(sort));
            A.Set(controller, "doneButton", done); A.Set(controller, "removeZone", zoneGroup);
            A.Set(controller, "clearAllButton", clearAll);
            A.Set(controller, "restoreBar", restoreBar.gameObject); A.Set(controller, "restoreText", restoreText);
            A.Set(controller, "restoreButton", restore); A.Set(controller, "deleteForeverButton", deleteForever);
            A.Set(controller, "renamePanel", renamePanel); A.Set(controller, "renameInput", renameInput); A.Set(controller, "renameSaveButton", renameSave);
            A.Set(controller, "aiPanel", aiPanel);
            A.Set(controller, "backButton", s.back);

            // ---- the freezing sound of a removal: one player for this screen, on its own object so its settings survive rebuilds ----
            var deleteSound = A.Controller<DeleteSoundPlayer>("DeleteSoundPlayer");
            var deleteSource = SceneBuilderCore.Ensure<AudioSource>(deleteSound.gameObject);
            deleteSource.playOnAwake = false; deleteSource.loop = false; deleteSource.spatialBlend = 0f;
            var deleteClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Delete_FreezingCold.mp3");
            var deleteSoundObject = new SerializedObject(deleteSound);
            if (deleteSoundObject.FindProperty("clip").objectReferenceValue == null && deleteClip != null)      // a clip the user put there themselves is kept
            {
                deleteSoundObject.FindProperty("clip").objectReferenceValue = deleteClip;
                deleteSoundObject.ApplyModifiedPropertiesWithoutUndo();
            }
            // the app's own freezing sound sits beside it; "Use Own Sound" on DeleteSoundPlayer chooses it (the tick is kept across rebuilds)
            var ownDeleteClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Delete_Freeze_Made.wav");
            if (deleteSoundObject.FindProperty("ownClip").objectReferenceValue == null && ownDeleteClip != null)
            {
                deleteSoundObject.FindProperty("ownClip").objectReferenceValue = ownDeleteClip;
                deleteSoundObject.ApplyModifiedPropertiesWithoutUndo();
            }
            // the short sound of the drop (made by Audio/Source~/make_drop_sound.py); a clip set by hand is kept
            var dropClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Delete_Drop_Made.wav");
            if (deleteSoundObject.FindProperty("dropClip").objectReferenceValue == null && dropClip != null)
            {
                deleteSoundObject.FindProperty("dropClip").objectReferenceValue = dropClip;
                deleteSoundObject.ApplyModifiedPropertiesWithoutUndo();
            }
            if (deleteClip == null) Debug.LogWarning("[GlowUp] Assets/_Project/Audio/Delete_FreezingCold.mp3 was not found: removing a scan stays silent until a clip is set on DeleteSoundPlayer.");
            // New20: the piece is cut HERE, in the Editor, and saved as a small WAV that is always loaded; the phone plays it as it is
            // (its own run-time cut needed AudioClip.GetData and gave silence on Android). The ringer no longer silences it.
            deleteSoundObject.Update();
            AudioClip readyPiece = DeleteSoundPiece.Make(deleteSoundObject, deleteSoundObject.FindProperty("clip").objectReferenceValue as AudioClip);
            deleteSoundObject.FindProperty("readyPiece").objectReferenceValue = readyPiece;
            deleteSoundObject.FindProperty("quietWhenPhoneIsSilent").boolValue = false;
            deleteSoundObject.ApplyModifiedPropertiesWithoutUndo();
            DeleteSoundPiece.KeepLoaded("Assets/_Project/Audio/Delete_Drop_Made.wav");
            A.Set(controller, "deleteSound", deleteSound);
            A.Set(controller, "selectButton", select); A.Set(controller, "selectionBar", selectionBar); A.Set(controller, "selectionText", selectionText);
            A.Set(controller, "selectAllButton", selectAll); A.Set(controller, "cancelSelectionButton", cancelSelection);

            // What is FETCHED on this screen: the count ("42 saved") and the rows. While this phone has nothing to show yet the
            // count is a bar and the list is placeholder rows; otherwise the saved scans are simply there. The title, the
            // buttons and the bottom bar are never bars.
            AppTiles.WordSkeleton(s, AppTiles.TextsOf(controller, "countText"), new[] { listSkeleton }, new[] { listGroup, emptyGroup });

            A.Finish(s);
            Debug.Log("[GlowUp] RecentScanActivitiesScene built. ScanTileTemplate is the pattern row (left open in the Hierarchy so its buttons can be styled); RemoveZone is invisible until the tiles shake (set its CanvasGroup's Alpha to 1 to see it); " +
                      "EmptyState, DoneButton, RenamePanel and AiInsightPanel are inactive until needed.");
        }
    }
}
