using System;
using System.Collections.Generic;
using System.Linq;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Recent
{
    /// <summary>
    /// Recent Scans: the saved products as long rows, one under the other. A row shows only the kind's icon, the
    /// product's name and its brand. Tapping a row makes it longer and shows "Result" (the full result), "Try On"
    /// and "AI"; tapping it again closes it, and only one row is open at a time.
    ///
    /// Removing works like app icons on a phone's home screen: press and hold a tile and all tiles start
    /// to shake; a shadow over the left half of the screen shows where to drag. A tile can be dragged anywhere
    /// and stays exactly under the finger; while the finger is inside the shadow the shadow turns red, and letting
    /// go there pops the tile and removes it (it can still be brought back from the bar at the bottom).
    /// While the tiles shake each one shows a small pencil for renaming it (on this phone only), and
    /// "Done" stops the shaking. The Sort button changes the order of the tiles.
    ///
    /// SEVERAL AT ONCE: the Delete button at the top right starts selection mode. The rows shake, a tap on a row
    /// selects it or takes it out again (a check mark and "3 selected" show it), with "Select all" and "Cancel".
    /// Dragging ANY selected row moves the whole group: that row carries a small number, the others fade, and the same
    /// drag, shadow and finger-following are used as for one row. Let go inside the shadow: every selected scan is
    /// removed in one save, with one sound. Let go outside: they return to their places, still selected. A row that is
    /// not selected is dragged alone. Press-and-hold works as before; it simply has no selection.
    /// </summary>
    public class RecentScansFlowController : MonoBehaviour
    {
        public enum SortOrder { Newest, Oldest, Name, Brand, Result }

        [Header("Tiles")]
        [SerializeField] private RectTransform listContent;
        [Tooltip("The pattern tile. It is hidden when the app runs and copied once per saved scan.")]
        [SerializeField] private ProductTileView tileTemplate;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private CosmeticTypeIcons typeIcons;
        [Tooltip("On = each tile shows a small coloured dot for its result (green safe, amber caution, red harmful)")]
        [SerializeField] private bool showStatusDot = false;
        [SerializeField] private Color safeColor = new Color32(79, 114, 64, 255);
        [SerializeField] private Color cautionColor = new Color32(148, 99, 18, 255);
        [SerializeField] private Color harmfulColor = new Color32(181, 71, 47, 255);

        [Header("Sort")]
        [SerializeField] private Button sortButton;
        [SerializeField] private TextMeshProUGUI sortLabel;
        [Tooltip("The words on the Sort button, in this order: Newest, Oldest, Name, Brand, Result")]
        [SerializeField] private string[] sortNames = { "Newest first", "Oldest first", "Name A to Z", "By brand", "By result" };
        [Tooltip("{0} = the current order")]
        [SerializeField] private string sortFormat = "Sort: {0}";

        [Header("Press, hold and drag to remove")]
        [Tooltip("The button that stops the tiles shaking")]
        [SerializeField] private Button doneButton;
        [Tooltip("The shadow over the left half of the screen that shows where to drag a tile. Letting go with the finger inside it removes the tile.")]
        [SerializeField] private CanvasGroup removeZone;
        [Tooltip("How visible the shadow is while the tiles shake, and while the finger is inside it with a tile")]
        [Range(0f, 1f)] [SerializeField] private float zoneAlphaWaiting = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float zoneAlphaAtRemove = 1f;
        [SerializeField] private float zoneFadeSpeed = 7f;
        [Tooltip("The shadow's colour while it waits, and while a dragged tile's finger is inside it (it turns red: letting go removes)")]
        [SerializeField] private Color zoneColorWaiting = new Color(0.16f, 0.11f, 0.13f, 1f);
        [SerializeField] private Color zoneColorInside = new Color32(181, 47, 47, 255);
        [Tooltip("The freezing sound of a removal: once per removal, at the moment the tile starts to pop (the user can switch it off in Settings)")]
        [SerializeField] private DeleteSoundPlayer deleteSound;

        [Header("Selecting several to remove them together")]
        [Tooltip("The Delete button at the top right: starts and ends selection mode")]
        [SerializeField] private Button selectButton;
        [Tooltip("The strip that takes the place of the count and Sort while rows are being selected")]
        [SerializeField] private GameObject selectionBar;
        [SerializeField] private TextMeshProUGUI selectionText;
        [SerializeField] private Button selectAllButton;
        [SerializeField] private Button cancelSelectionButton;
        [Tooltip("{0} = how many are selected")]
        [SerializeField] private string selectedFormat = "{0} selected";
        [SerializeField] private string selectedNone = "Tap scans to select them";

        [Header("Clear / restore")]
        [SerializeField] private Button clearAllButton;
        [Tooltip("The bar that appears while there are removed items to bring back")]
        [SerializeField] private GameObject restoreBar;
        [SerializeField] private TextMeshProUGUI restoreText;
        [SerializeField] private Button restoreButton;
        [SerializeField] private Button deleteForeverButton;

        [Header("Pop-ups")]
        [SerializeField] private OverlayPanel renamePanel;
        [SerializeField] private TMP_InputField renameInput;
        [SerializeField] private Button renameSaveButton;
        [Tooltip("The pop-up the tiles' AI button opens")]
        [SerializeField] private AiInsightPanel aiPanel;

        [Header("Buttons / scenes")]
        [SerializeField] private Button backButton;
        [SerializeField] private string backScene = SceneNames.Dashboard;
        [SerializeField] private string resultScene = SceneNames.AiResult;
        [SerializeField] private string tryOnScene = SceneNames.ArTryOn;

        [Header("Wording")]
        [Tooltip("{0} = number of saved scans")]
        [SerializeField] private string countFormat = "{0} saved";
        [SerializeField] private string countOne = "1 saved";
        [Tooltip("{0} = number of removed items")]
        [SerializeField] private string restoreFormat = "{0} removed";

        const string SortKey = "GlowUp.Recent.Sort";
        readonly List<ProductTileView> tiles = new List<ProductTileView>();
        ScanRecord renaming;
        SortOrder order;
        bool shaking, popping, fingerInZone, selecting, groupShown;
        readonly HashSet<string> selected = new HashSet<string>();      // the ids of the selected scans
        float zoneTarget;
        Image zoneImage;

        void Awake()
        {
            order = (SortOrder)Mathf.Clamp(PlayerPrefs.GetInt(SortKey, 0), 0, 4);
            if (tileTemplate != null) tileTemplate.gameObject.SetActive(false);
            if (clearAllButton != null) clearAllButton.onClick.AddListener(() => { ScanHistoryRepository.ClearAll(); Refresh(); });
            if (restoreButton != null) restoreButton.onClick.AddListener(() => { ScanHistoryRepository.RestoreAll(); Refresh(); });
            if (deleteForeverButton != null) deleteForeverButton.onClick.AddListener(() => { ScanHistoryRepository.PurgeTrash(); Refresh(); });
            if (renameSaveButton != null) renameSaveButton.onClick.AddListener(SaveRename);
            if (sortButton != null) sortButton.onClick.AddListener(NextSort);
            if (doneButton != null) { doneButton.onClick.AddListener(() => SetShaking(false)); doneButton.gameObject.SetActive(false); }
            if (backButton != null) backButton.onClick.AddListener(() => { if (shaking) SetShaking(false); else SceneNavigator.Go(backScene); });
            if (selectButton != null) selectButton.onClick.AddListener(() => SetSelecting(!selecting));
            if (selectAllButton != null) selectAllButton.onClick.AddListener(SelectAll);
            if (cancelSelectionButton != null) cancelSelectionButton.onClick.AddListener(() => SetSelecting(false));
            if (selectionBar != null) selectionBar.SetActive(false);
            if (removeZone != null)
            {
                removeZone.alpha = 0f; removeZone.blocksRaycasts = false; removeZone.interactable = false;
                zoneImage = removeZone.GetComponent<Image>();
                if (zoneImage != null) zoneImage.color = zoneColorWaiting;
            }
        }

        async void Start()
        {
            Refresh();
            // make sure the profile is known (the history belongs to the signed-in account), then show it again
            try { await UserProfileRepository.Load(); } catch (Exception e) { Debug.LogWarning("Recent scans: " + e.Message); }
            if (this != null) Refresh();
        }

        void OnDisable() { SwipeSceneNavigator.Suppressed = false; }

        void Update()
        {
            if (removeZone == null) return;
            removeZone.alpha = Mathf.MoveTowards(removeZone.alpha, zoneTarget, zoneFadeSpeed * Time.unscaledDeltaTime);
            // dark while it waits, red while the finger is inside it with a tile, dark again when the finger leaves
            if (zoneImage != null) zoneImage.color = Color.Lerp(zoneImage.color, fingerInZone ? zoneColorInside : zoneColorWaiting, Mathf.Clamp01(zoneFadeSpeed * 2f * Time.unscaledDeltaTime));
        }

        // ---------------------------------------------------------------------------------------
        //  The list
        // ---------------------------------------------------------------------------------------
        /// <summary>Rebuilds the tiles from what is saved.</summary>
        public void Refresh()
        {
            foreach (ProductTileView t in tiles) if (t != null) Destroy(t.gameObject);
            tiles.Clear();
            popping = false;

            List<ScanRecord> saved = Sorted(ScanHistoryRepository.Active());
            if (tileTemplate != null && listContent != null)
                foreach (ScanRecord record in saved)
                {
                    ProductTileView tile = Instantiate(tileTemplate, listContent);
                    tile.gameObject.name = "ScanTile_" + record.Title;
                    tile.gameObject.SetActive(true);
                    tile.Bind(record, record.Title, record.brandName, record.cosmeticType, typeIcons != null ? typeIcons.For(record.cosmeticType) : null);
                    tile.SetStatus(showStatusDot ? StatusColour(record.status) : (Color?)null);
                    tile.SetTryOnAvailable(ProductScope.CanTryOn(record));
                    tile.Clicked += t => { if (!shaking) Open(t.Data as ScanRecord); };
                    tile.TryOnClicked += t => { if (!shaking) { ScanSession.TryOn = t.Data as ScanRecord; SceneNavigator.Go(tryOnScene); } };
                    tile.AiClicked += t => { if (!shaking && aiPanel != null) aiPanel.Show(t.Data as ScanRecord); };
                    tile.RenameClicked += t => BeginRename(t.Data as ScanRecord);
                    tile.DrawerOpening += CloseOtherRows;
                    tile.LockedTap += OnRowTappedWhileShaking;
                    tile.SetSelectedForRemoval(selected.Contains(record.id));
                    if (tile.TryGetComponent(out HoldToRemoveTile hold))
                    {
                        if (removeZone != null) hold.SetRemoveArea((RectTransform)removeZone.transform);
                        hold.HoldStarted += _ => SetShaking(true);
                        hold.Dragging += (h, inZone) =>
                        {
                            fingerInZone = inZone >= 0.5f; zoneTarget = fingerInZone ? zoneAlphaAtRemove : zoneAlphaWaiting;
                            // a selected row is dragged: the whole selection moves as one group under the finger
                            if (!groupShown && h.TryGetComponent(out ProductTileView draggedTile) && DragsGroup(draggedTile)) ShowGroup(draggedTile, true);
                        };
                        hold.DragEnded += OnDragEnded;
                    }
                    tiles.Add(tile);
                }

            if (countText != null) countText.text = saved.Count == 1 ? countOne : string.Format(countFormat, saved.Count);
            if (emptyState != null) emptyState.SetActive(saved.Count == 0);
            if (sortLabel != null) sortLabel.text = string.Format(sortFormat, SortName(order));
            // a selected scan that is no longer in the list (removed, or gone) is no longer selected
            selected.RemoveWhere(id => !saved.Exists(r => r.id == id));
            groupShown = false;
            if (saved.Count == 0) { shaking = false; selecting = false; selected.Clear(); }
            ApplyShaking();

            int removed = ScanHistoryRepository.Trash().Count;
            if (restoreBar != null) restoreBar.SetActive(removed > 0);
            if (restoreText != null) restoreText.text = string.Format(restoreFormat, removed);
        }

        List<ScanRecord> Sorted(List<ScanRecord> items)
        {
            switch (order)
            {
                case SortOrder.Oldest: return items.OrderBy(r => r.scannedAtUnix).ToList();
                case SortOrder.Name: return items.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
                case SortOrder.Brand: return items.OrderBy(r => string.IsNullOrWhiteSpace(r.brandName) ? "￿" : r.brandName, StringComparer.OrdinalIgnoreCase)
                                                 .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
                case SortOrder.Result: return items.OrderBy(r => r.status == "harmful" ? 0 : r.status == "caution" ? 1 : 2).ThenByDescending(r => r.scannedAtUnix).ToList();
                default: return items.OrderByDescending(r => r.scannedAtUnix).ToList();
            }
        }

        string SortName(SortOrder o) => sortNames != null && (int)o < sortNames.Length ? sortNames[(int)o] : o.ToString();

        void NextSort()
        {
            order = (SortOrder)(((int)order + 1) % 5);
            PlayerPrefs.SetInt(SortKey, (int)order); PlayerPrefs.Save();
            Refresh();
        }

        Color StatusColour(string status) => status == "harmful" ? harmfulColor : status == "caution" ? cautionColor : safeColor;

        // ---------------------------------------------------------------------------------------
        //  Shake, drag, pop
        // ---------------------------------------------------------------------------------------
        void SetShaking(bool on)
        {
            if (!on && selecting) { selecting = false; selected.Clear(); }      // leaving delete mode also leaves selection mode
            if (shaking == on) { ApplyShaking(); return; }
            shaking = on;
            ApplyShaking();
        }

        /// <summary>
        /// Selection mode on or off (the Delete button; "Cancel" and Back end it). It is delete mode with a selection: the
        /// rows shake and can be dragged as after a press-and-hold, and a tap on a row selects it.
        /// </summary>
        void SetSelecting(bool on)
        {
            if (on && tiles.Count == 0) return;
            selecting = on;
            if (!on) selected.Clear();
            if (on && !shaking) { shaking = true; }
            if (!on) shaking = false;
            ApplyShaking();
        }

        void SelectAll()
        {
            if (!selecting) return;
            foreach (ProductTileView tile in tiles) if (tile != null && tile.Data is ScanRecord record) selected.Add(record.id);
            ApplyShaking();
        }

        /// <summary>A tap on a shaking row: in selection mode it selects the row or takes it out again; otherwise nothing.</summary>
        void OnRowTappedWhileShaking(ProductTileView tile)
        {
            if (!selecting || popping || !(tile.Data is ScanRecord record)) return;
            if (!selected.Remove(record.id)) selected.Add(record.id);
            tile.SetSelectedForRemoval(selected.Contains(record.id));
            ShowSelection();
        }

        void ShowSelection()
        {
            if (selectionBar != null) selectionBar.SetActive(selecting);
            if (selectionText != null) selectionText.text = selected.Count == 0 ? selectedNone : string.Format(selectedFormat, selected.Count);
            if (countText != null) countText.gameObject.SetActive(!selecting);
        }

        /// <summary>One selected row is under the finger for all of them: it carries the number, the others fade.</summary>
        void ShowGroup(ProductTileView dragged, bool show)
        {
            groupShown = show;
            foreach (ProductTileView tile in tiles)
            {
                if (tile == null) continue;
                if (tile == dragged) tile.ShowGroupCount(show ? selected.Count : 0);
                else if (tile.Selected) tile.SetDimmed(show);
            }
            if (!show && dragged != null) dragged.ShowGroupCount(0);
        }

        /// <summary>True when this row is dragged for a whole group (selection mode, the row is selected, and so is at least one more).</summary>
        bool DragsGroup(ProductTileView tile) => selecting && tile != null && tile.Selected && selected.Count > 1;

        void ApplyShaking()
        {
            foreach (ProductTileView tile in tiles)
            {
                if (tile == null) continue;
                if (tile.TryGetComponent(out HoldToRemoveTile hold)) hold.SetShaking(shaking);
                tile.ShowRename(shaking);
                tile.SetExpandLocked(shaking);          // delete mode comes first: a shaking row does not open, and an open one closes
                tile.SetSelectedForRemoval(selecting && tile.Data is ScanRecord record && selected.Contains(record.id));
                if (!selecting) { tile.ShowRename(shaking); } else tile.ShowRename(false);     // while selecting, the check marks take the pencils' place
            }
            ShowSelection();
            if (doneButton != null) doneButton.gameObject.SetActive(shaking && !selecting);     // selection mode ends with its own Cancel
            if (sortButton != null) sortButton.gameObject.SetActive(!shaking);
            if (clearAllButton != null) clearAllButton.gameObject.SetActive(!shaking && tiles.Count > 0);
            zoneTarget = shaking ? zoneAlphaWaiting : 0f;
        }

        /// <summary>One row open at a time, so the list stays calm.</summary>
        void CloseOtherRows(ProductTileView opened)
        {
            foreach (ProductTileView tile in tiles) if (tile != null && tile != opened) tile.SetDrawer(false);
        }

        void OnDragEnded(HoldToRemoveTile hold, bool remove)
        {
            fingerInZone = false;
            zoneTarget = shaking ? zoneAlphaWaiting : 0f;
            ProductTileView tile = hold != null && hold.TryGetComponent(out ProductTileView found) ? found : null;
            bool group = DragsGroup(tile);
            if (groupShown) ShowGroup(tile, false);             // let go: the others are fully there again (outside the shadow they stay selected)
            if (!remove || popping || tile == null || !(tile.Data is ScanRecord record)) return;
            popping = true;
            if (deleteSound != null) deleteSound.Play();        // ONCE per removal, for one scan or a whole group; not when it is dropped outside the shadow
            // a group: every selected scan; otherwise the one that was dragged (a row that is not selected is always dragged alone)
            var ids = group ? new List<string>(selected) : new List<string> { record.id };
            tile.PlayPop(() =>
            {
                // into the removed scans, all in one save: the Restore bar can bring them back. A group is never left half removed.
                int gone = ids.Count == 1 ? RemoveOne(ids[0]) : ScanHistoryRepository.RemoveMany(ids);
                if (gone < ids.Count) Debug.LogWarning("Recent scans: " + (ids.Count - gone) + " of " + ids.Count + " scans were no longer there to remove.");
                foreach (string id in ids) selected.Remove(id);
                if (this != null) Refresh();
            });
        }

        static int RemoveOne(string id)
        {
            if (ScanHistoryRepository.Find(id) == null) return 0;
            ScanHistoryRepository.Remove(id);
            return 1;
        }

        // ---------------------------------------------------------------------------------------
        void Open(ScanRecord record)
        {
            if (record == null) return;
            ScanSession.Current = record;
            ScanSession.ResultBackScene = SceneNames.RecentScans;
            SceneNavigator.Go(resultScene);
        }

        void BeginRename(ScanRecord record)
        {
            if (renamePanel == null || record == null) return;
            renaming = record;
            renamePanel.Show();
            if (renameInput != null) renameInput.text = record.Title;
        }

        void SaveRename()
        {
            if (renaming != null && renameInput != null)
                ScanHistoryRepository.Rename(renaming.id, renameInput.text);      // on this device only
            renaming = null;
            if (renamePanel != null) renamePanel.Hide();
            Refresh();
        }
    }
}
