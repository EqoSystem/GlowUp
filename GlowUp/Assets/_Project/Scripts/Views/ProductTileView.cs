using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// One compact product tile with round corners: the product's name, its brand and its kind, a small
    /// icon, and its own buttons along the bottom edge.
    ///
    ///  - Recent Scans: "Try On" and "AI". Tapping the tile opens the result.
    ///  - Dashboard: "Save", "Try On" and "AI". Tapping the tile marks it as the chosen one.
    ///
    /// A tap plays the selection animation of the Onboarding tiles (Steps 2 and 3): the diagonal pixel
    /// sweep and the glowing outline. Their settings are on this object's PixelSweepTileAnimator.
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

        [Header("Pop (when the tile is removed)")]
        [SerializeField] private RectTransform popTarget;
        [SerializeField] private CanvasGroup popGroup;
        [SerializeField] private float popSeconds = 0.24f;
        [Tooltip("The tile swells to this size, then bursts")]
        [SerializeField] private float popScale = 1.16f;

        public object Data { get; private set; }
        public bool DrawerOpen { get; private set; }
        public event Action<ProductTileView> Clicked, TryOnClicked, AiClicked, SaveClicked, RenameClicked;
        /// <summary>Raised when this tile's drawer starts to open (the controller closes the other tiles' drawers).</summary>
        public event Action<ProductTileView> DrawerOpening;

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
            if (renameButton != null) { renameButton.onClick.AddListener(() => RenameClicked?.Invoke(this)); renameButton.gameObject.SetActive(false); }
            if (statusDot != null) statusDot.gameObject.SetActive(false);
            if (popTarget != null) popBase = popTarget.localScale;
            ApplyDrawer(0f);
        }

        void OnTileTapped()
        {
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
        public void SetDrawer(bool open)
        {
            if (drawer == null || DrawerOpen == open) return;
            DrawerOpen = open;
            if (open) DrawerOpening?.Invoke(this);
        }

        /// <summary>The tile swells, fades and bursts; `done` is called when it is gone.</summary>
        public void PlayPop(Action done)
        {
            popDone = done;
            if (popTarget == null || !isActiveAndEnabled) { popDone?.Invoke(); popDone = null; return; }
            popT = 0f;
        }

        void Update()
        {
            if (drawer != null)
            {
                float target = DrawerOpen ? 1f : 0f;
                if (!Mathf.Approximately(drawerK, target))
                {
                    drawerK = Mathf.MoveTowards(drawerK, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, drawerSeconds));
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
