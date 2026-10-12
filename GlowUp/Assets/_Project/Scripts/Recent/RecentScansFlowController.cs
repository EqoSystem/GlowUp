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
    /// Recent Scans: the saved products as square tiles, two per row. A tile shows only the product's
    /// name, brand and kind, with "Try On" and "AI". Tapping a tile opens its full result.
    ///
    /// Removing works like app icons on a phone's home screen: press and hold a tile and all tiles start
    /// to shake; a shadow over the left half of the screen shows where to drag; drag a tile to the left and
    /// let go, and it pops and is removed (it can still be brought back from the bar at the bottom).
    /// While the tiles shake each one shows a small pencil for renaming it (on this phone only), and
    /// "Done" stops the shaking. The Sort button changes the order of the tiles.
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
        [Tooltip("The shadow over the left half of the screen that shows where to drag a tile")]
        [SerializeField] private CanvasGroup removeZone;
        [Tooltip("How visible the shadow is while the tiles shake, and when a tile is dragged all the way")]
        [Range(0f, 1f)] [SerializeField] private float zoneAlphaWaiting = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float zoneAlphaAtRemove = 1f;
        [SerializeField] private float zoneFadeSpeed = 7f;
        [Tooltip("The freezing sound of a removal: once per removal, at the moment the tile starts to pop (the user can switch it off in Settings)")]
        [SerializeField] private DeleteSoundPlayer deleteSound;

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
        bool shaking, popping;
        float zoneTarget;

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
            if (removeZone != null) { removeZone.alpha = 0f; removeZone.blocksRaycasts = false; removeZone.interactable = false; }
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
                    if (tile.TryGetComponent(out HoldToRemoveTile hold))
                    {
                        hold.HoldStarted += _ => SetShaking(true);
                        hold.Dragging += (_, progress) => zoneTarget = Mathf.Lerp(zoneAlphaWaiting, zoneAlphaAtRemove, progress);
                        hold.DragEnded += OnDragEnded;
                    }
                    tiles.Add(tile);
                }

            if (countText != null) countText.text = saved.Count == 1 ? countOne : string.Format(countFormat, saved.Count);
            if (emptyState != null) emptyState.SetActive(saved.Count == 0);
            if (sortLabel != null) sortLabel.text = string.Format(sortFormat, SortName(order));
            if (saved.Count == 0) shaking = false;
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
            if (shaking == on) return;
            shaking = on;
            ApplyShaking();
        }

        void ApplyShaking()
        {
            foreach (ProductTileView tile in tiles)
            {
                if (tile == null) continue;
                if (tile.TryGetComponent(out HoldToRemoveTile hold)) hold.SetShaking(shaking);
                tile.ShowRename(shaking);
            }
            if (doneButton != null) doneButton.gameObject.SetActive(shaking);
            if (sortButton != null) sortButton.gameObject.SetActive(!shaking);
            if (clearAllButton != null) clearAllButton.gameObject.SetActive(!shaking && tiles.Count > 0);
            zoneTarget = shaking ? zoneAlphaWaiting : 0f;
        }

        void OnDragEnded(HoldToRemoveTile hold, bool remove)
        {
            zoneTarget = shaking ? zoneAlphaWaiting : 0f;
            if (!remove || popping || hold == null) return;
            if (!hold.TryGetComponent(out ProductTileView tile) || !(tile.Data is ScanRecord record)) return;
            popping = true;
            if (deleteSound != null) deleteSound.Play();        // once per removal; not when the tile is dropped outside the shadow (that never gets here)
            tile.PlayPop(() =>
            {
                ScanHistoryRepository.Remove(record.id);       // into the trash: the Restore bar can bring it back
                if (this != null) Refresh();
            });
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
