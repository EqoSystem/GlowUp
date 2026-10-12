using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// One product in a list. It comes in two shapes, built by AppTiles:
    ///
    ///  - A ROW THAT OPENS DOWNWARDS (dashboard "Recommended for you", Recent Scans; the user's choice of 12 October
    ///    2026, so a long list does not overwhelm). Closed, the long row shows only the kind's icon, the product's name
    ///    and its brand. A tap makes it longer and shows its buttons under it (dashboard: Save, Try On, AI; Recent
    ///    Scans: Result, Try On, AI); a second tap closes it. The controller keeps one row open at a time. In
    ///    Performance Mode (reduced effects) it opens at once, without the short growing.
    ///  - A SQUARE TILE (the product picker of AR Try-On, the alternatives on a result): name, brand, kind and its
    ///    buttons on the tile; a tap plays the selection animation of the Onboarding tiles (pixel sweep and glowing
    ///    outline, set on this object's PixelSweepTileAnimator).
    ///
    /// The tile only shows things and reports taps; the scene's controller decides what a tap does.
    /// The pattern tile in the scene is copied once per product: restyle that one object.
    /// </summary>
    public class ProductTileView : MonoBehaviour
    {
        [Header("What the tile shows")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI brandText;
        [SerializeField] private TextMeshProUGUI typeText;
        [SerializeField] private Image typeIcon;
        [Tooltip("A small coloured dot for the scan's result (safe / caution / harmful). Hidden unless the controller shows it.")]
        [SerializeField] private Image statusDot;

        [Header("Taps")]
        [Tooltip("The whole tile")]
        [SerializeField] private Button tileButton;
        [SerializeField] private Button tryOnButton;
        [SerializeField] private Button aiButton;
        [Tooltip("Dashboard only: the Save button in the drawer")]
        [SerializeField] private Button saveButton;
        [SerializeField] private TextMeshProUGUI saveLabel;
        [Tooltip("Recent Scans only: the small pencil that shows while the tiles shake")]
        [SerializeField] private Button renameButton;

        [Header("Selection animation (the same as the Onboarding tiles)")]
        [SerializeField] private GlowUp.Common.PixelSweepTileAnimator selectAnimator;
        [Tooltip("On = the tapped tile keeps its glowing outline until another tile is tapped. Off = the outline fades after the sweep.")]
        [SerializeField] private bool staysSelected = false;
        [Tooltip("Seconds the outline stays when the tile does not stay selected")]
        [SerializeField] private float flashSeconds = 0.55f;
        [Tooltip("Seconds between the tap and what the tap does, so the animation is seen first (0 = at once)")]
        [SerializeField] private float tapDelaySeconds = 0.18f;

        [Header("Drawer (optional; leave empty for a tile whose buttons sit on the tile)")]
        [Tooltip("The arrow that opens and closes the drawer")]
        [SerializeField] private Button expandButton;
        [SerializeField] private RectTransform arrow;
        [Tooltip("The panel that slides down over the tile. Leave empty for a tile without a drawer.")]
        [SerializeField] private RectTransform drawer;
        [SerializeField] private CanvasGroup drawerGroup;
        [Tooltip("Seconds the drawer takes to slide")]
        [SerializeField] private float drawerSeconds = 0.26f;
        [SerializeField] private AnimationCurve drawerCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("A small overshoot at the end of the slide, like a pop-up settling (0 = none)")]
        [SerializeField] private float drawerBounce = 0.04f;
        [Tooltip("On = tapping the tile opens and closes the drawer too")]
        [SerializeField] private bool tileTapTogglesDrawer = true;

        [Header("A row that opens downwards (leave empty for a square tile)")]
        [Tooltip("The strip under the row that holds the buttons. It has no height until the row is tapped.")]
        [SerializeField] private RectTransform actions;
        [SerializeField] private CanvasGroup actionsGroup;
        [Tooltip("The row's own height setting: the row gets taller while it opens")]
        [SerializeField] private LayoutElement rowLayout;
        [Tooltip("The row's height while it is closed")]
        [SerializeField] private float closedHeight = 128f;
        [Tooltip("How much taller the row is when it is open (the height of the strip with the buttons)")]
        [SerializeField] private float actionsHeight = 96f;
        [Tooltip("Seconds the row takes to open or close. In Performance Mode (reduced effects) it opens at once.")]
        [SerializeField] private float openSeconds = 0.16f;
        [Tooltip("The small arrow at the right end of the row: it turns upside down while the row is open")]
        [SerializeField] private RectTransform chevron;
        [Tooltip("Recent Scans: the button in the opened part that opens the full result")]
        [SerializeField] private Button resultButton;

        [Header("Selecting several rows to remove them together (Recent Scans)")]
        [Tooltip("The check mark over the row's icon: this row is selected")]
        [SerializeField] private GameObject selectedMark;
        [Tooltip("The small number on the row that is dragged for a whole group: how many scans move together")]
        [SerializeField] private GameObject groupBadge;
        [SerializeField] private TextMeshProUGUI groupBadgeText;
        [Tooltip("How see-through the other rows of a group are while one of them is dragged for all")]
        [Range(0f, 1f)] [SerializeField] private float dimmedAlpha = 0.28f;

        [Header("Pop (when the tile is removed)")]
        [SerializeField] private RectTransform popTarget;
        [SerializeField] private CanvasGroup popGroup;
        [SerializeField] private float popSeconds = 0.24f;
        [Tooltip("The tile swells to this size, then bursts")]
        [SerializeField] private float popScale = 1.16f;

        public object Data { get; private set; }
        /// <summary>True when this is a row that opens downwards (and not a square tile).</summary>
        public bool IsRow => actions != null;
        /// <summary>True while the row is open (its buttons show).</summary>
        public bool DrawerOpen { get; private set; }
        bool expandLocked;
        public event Action<ProductTileView> Clicked, TryOnClicked, AiClicked, SaveClicked, RenameClicked;
        /// <summary>Raised when this tile's drawer starts to open (the controller closes the other tiles' drawers).</summary>
        public event Action<ProductTileView> DrawerOpening;
        /// <summary>A row was tapped while it cannot open (delete mode): in selection mode that tap selects it.</summary>
        public event Action<ProductTileView> LockedTap;
        /// <summary>True while the row is selected for removal.</summary>
        public bool Selected { get; private set; }

        float drawerK, popT = -1f;
        Action popDone;
        Vector3 popBase = Vector3.one;

        void Awake()
        {
            if (tileButton != null) tileButton.onClick.AddListener(OnTileTapped);
            if (expandButton != null) expandButton.onClick.AddListener(ToggleDrawer);
            if (tryOnButton != null) tryOnButton.onClick.AddListener(() => TryOnClicked?.Invoke(this));
            if (aiButton != null) aiButton.onClick.AddListener(() => AiClicked?.Invoke(this));
            if (saveButton != null) saveButton.onClick.AddListener(() => SaveClicked?.Invoke(this));
            if (resultButton != null) resultButton.onClick.AddListener(() => Clicked?.Invoke(this));       // "open this": what a tap on a square tile does
            if (renameButton != null) { renameButton.onClick.AddListener(() => RenameClicked?.Invoke(this)); renameButton.gameObject.SetActive(false); }
            if (statusDot != null) statusDot.gameObject.SetActive(false);
            if (selectedMark != null) selectedMark.SetActive(false);
            if (groupBadge != null) groupBadge.SetActive(false);
            if (popTarget != null) popBase = popTarget.localScale;
            ApplyDrawer(0f);
        }

        void OnTileTapped()
        {
            if (IsRow)
            {
                // a row: a tap opens or closes it and does nothing else. While it is locked (delete mode) it does not open:
                // the controller hears of the tap and, in selection mode, selects the row.
                if (expandLocked) LockedTap?.Invoke(this);
                else if (tileTapTogglesDrawer) ToggleDrawer();
                return;
            }
            PlaySelect();
            if (drawer != null && tileTapTogglesDrawer) { ToggleDrawer(); return; }
            if (tapDelaySeconds > 0f && isActiveAndEnabled) { CancelInvoke(nameof(RaiseClicked)); Invoke(nameof(RaiseClicked), tapDelaySeconds); }
            else RaiseClicked();
        }

        void RaiseClicked() { Clicked?.Invoke(this); }

        /// <summary>Plays the pixel sweep and lights the outline. A tile that stays selected switches its neighbours' outlines off.</summary>
        public void PlaySelect()
        {
            if (selectAnimator == null) return;
            if (staysSelected && transform.parent != null)
                foreach (Transform sibling in transform.parent)
                    if (sibling != transform && sibling.TryGetComponent(out ProductTileView other)) other.SetSelected(false);
            selectAnimator.SetSelected(true);
            CancelInvoke(nameof(Deselect));
            if (!staysSelected) Invoke(nameof(Deselect), flashSeconds);
        }

        void Deselect() { SetSelected(false); }
        /// <summary>Lights or clears the glowing outline without the sweep.</summary>
        public void SetSelected(bool selected) { if (selectAnimator != null && (!selected || isActiveAndEnabled)) { if (selected) PlaySelect(); else selectAnimator.SetSelected(false); } }

        /// <summary>Fills the tile. `data` is handed back with every event (the scan or product the tile stands for).</summary>
        public void Bind(object data, string productName, string brand, string type, Sprite icon)
        {
            Data = data;
            if (nameText != null) nameText.text = productName ?? "";
            if (brandText != null) { brandText.text = brand ?? ""; brandText.gameObject.SetActive(!string.IsNullOrWhiteSpace(brand)); }
            if (typeText != null) { typeText.text = type ?? ""; typeText.gameObject.SetActive(!string.IsNullOrWhiteSpace(type)); }
            if (typeIcon != null && icon != null) typeIcon.sprite = icon;
        }

        public void SetSaveLabel(string text, bool interactable = true)
        {
            if (saveLabel != null) saveLabel.text = text;
            if (saveButton != null) saveButton.interactable = interactable;
        }

        /// <summary>Shows the small result dot in this colour, or hides it.</summary>
        public void SetStatus(Color? colour)
        {
            if (statusDot == null) return;
            statusDot.gameObject.SetActive(colour.HasValue);
            if (colour.HasValue) statusDot.color = colour.Value;
        }

        /// <summary>Switches the tile's Try On button off for a product AR Try-On cannot show (skin care, hair, body ...).</summary>
        public void SetTryOnAvailable(bool available) { if (tryOnButton != null) tryOnButton.interactable = available; }

        /// <summary>The small pencil (shown while the tiles shake).</summary>
        public void ShowRename(bool show) { if (renameButton != null) renameButton.gameObject.SetActive(show); }

        public void ToggleDrawer() { SetDrawer(!DrawerOpen); }

        /// <summary>Opens or closes the row (or the old sliding drawer). A locked row does not open.</summary>
        public void SetDrawer(bool open)
        {
            if ((drawer == null && actions == null) || DrawerOpen == open) return;
            if (open && expandLocked) return;
            DrawerOpen = open;
            if (open) DrawerOpening?.Invoke(this);
        }

        /// <summary>
        /// Locked = the row cannot be opened and closes if it is open (Recent Scans while the tiles shake: delete mode
        /// comes first). Its small arrow is hidden meanwhile, because the pencil for renaming takes that place.
        /// </summary>
        public void SetExpandLocked(bool locked)
        {
            expandLocked = locked;
            if (locked) SetDrawer(false);
            if (chevron != null) chevron.gameObject.SetActive(!locked);
        }

        /// <summary>Marks the row as selected for removal (a check mark over its icon), or clears the mark.</summary>
        public void SetSelectedForRemoval(bool on)
        {
            Selected = on;
            if (selectedMark != null && selectedMark.activeSelf != on) selectedMark.SetActive(on);
        }

        /// <summary>The row that is dragged for a whole group shows how many scans move with it (1 or less: no number).</summary>
        public void ShowGroupCount(int count)
        {
            if (groupBadge != null) groupBadge.SetActive(count > 1);
            if (groupBadgeText != null) groupBadgeText.text = count.ToString();
        }

        /// <summary>The other rows of a dragged group fade while their group is under the finger, and come back when it is let go.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (popGroup != null && popT < 0f) popGroup.alpha = dimmed ? dimmedAlpha : 1f;
        }

        /// <summary>Shows the open or closed state at once, without the short growing (used when a list is built).</summary>
        public void SnapDrawer() { drawerK = DrawerOpen ? 1f : 0f; ApplyDrawer(drawerK); }

        /// <summary>The tile swells, fades and bursts; `done` is called when it is gone.</summary>
        public void PlayPop(Action done)
        {
            popDone = done;
            if (popTarget == null || !isActiveAndEnabled) { popDone?.Invoke(); popDone = null; return; }
            popT = 0f;
        }

        void Update()
        {
            if (drawer != null || actions != null)
            {
                float target = DrawerOpen ? 1f : 0f;
                if (!Mathf.Approximately(drawerK, target))
                {
                    // a row grows for a moment; with reduced effects (Performance Mode) it is simply open or closed
                    bool atOnce = IsRow && (openSeconds <= 0.01f || GlowUp.Performance.PerformanceSettings.ReduceEffects);
                    float seconds = IsRow ? openSeconds : drawerSeconds;
                    drawerK = atOnce ? target : Mathf.MoveTowards(drawerK, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
                    ApplyDrawer(drawerK);
                }
            }
            if (popT >= 0f)
            {
                popT += Time.unscaledDeltaTime / Mathf.Max(0.01f, popSeconds);
                float k = Mathf.Clamp01(popT);
                // swell during the first 70%, then collapse quickly
                float scale = k < 0.7f ? Mathf.Lerp(1f, popScale, k / 0.7f) : Mathf.Lerp(popScale, 0.2f, (k - 0.7f) / 0.3f);
                popTarget.localScale = popBase * scale;
                if (popGroup != null) popGroup.alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                if (popT >= 1f) { popT = -1f; Action call = popDone; popDone = null; call?.Invoke(); }
            }
        }

        void ApplyDrawer(float k)
        {
            if (actions != null)
            {
                // the row gets longer downwards and the strip with the buttons is uncovered with it
                float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
                float extra = Mathf.Max(0f, actionsHeight) * open;
                actions.sizeDelta = new Vector2(actions.sizeDelta.x, extra);
                if (rowLayout != null) { rowLayout.minHeight = closedHeight + extra; rowLayout.preferredHeight = closedHeight + extra; }
                if (actionsGroup != null) { actionsGroup.alpha = Mathf.Clamp01(open * 1.3f); actionsGroup.blocksRaycasts = k > 0.6f; actionsGroup.interactable = k > 0.6f; }
                if (chevron != null) chevron.localEulerAngles = new Vector3(0f, 0f, 180f * open);
            }
            if (drawer == null) return;
            float eased = drawerCurve != null ? drawerCurve.Evaluate(k) : k;
            float bounce = DrawerOpen ? Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * drawerBounce : 0f;
            float height = ((RectTransform)drawer.parent).rect.height;
            if (height < 1f) height = drawer.rect.height;
            // closed: parked just above the tile (the tile's mask hides it); open: covering the tile
            drawer.anchoredPosition = new Vector2(0f, height * (1f - eased - bounce));
            if (drawerGroup != null) { drawerGroup.alpha = Mathf.Clamp01(eased * 1.4f); drawerGroup.blocksRaycasts = k > 0.6f; drawerGroup.interactable = k > 0.6f; }
            if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, 180f * eased);
        }
    }
}
