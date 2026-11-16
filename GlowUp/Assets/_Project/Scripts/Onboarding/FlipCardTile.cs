using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// A skin type card that FLIPS when it is chosen (10 November 2026, replaces the square sweep): it turns about its vertical axis in
    /// 0.28 s, once, and shows on its back how to tell that skin type (SkinTypeHints). The choice stays visible on both faces: the
    /// outline and a tick in the corner. Tapping the chosen card again flips it back; choosing another card flips this one back.
    /// With Performance Mode on (reduced effects) the two faces cross-fade instead of turning.
    ///
    /// Added at run time by the Step 2 page (no scene has to be rebuilt). The back is made from code and laid over the card's
    /// own content, inside its frame, so nothing moves around it.
    /// </summary>
    public class FlipCardTile : MonoBehaviour
    {
        const float Seconds = 0.36f;
        // New17: the turn PIXELATES - a mosaic of squares fills the card towards the edge and clears again on the new face.
        // Only on these skin type cards (never on Continue or another button); with reduced effects there is no mosaic.
        const int Columns = 8, Rows = 6;

        readonly List<GameObject> front = new List<GameObject>();
        readonly List<Image> cells = new List<Image>();
        readonly List<float> cellOrder = new List<float>();
        RectTransform mosaic;
        CanvasGroup back;
        CheckFillIndicator tick;
        bool showingBack, wantBack;
        float t = 1f;                       // 0..1 progress of the running turn; 1 = still
        bool swapped;

        public static FlipCardTile AddTo(SelectableTile tile, TMP_Text fontFrom)
        {
            if (tile == null) return null;
            FlipCardTile flip = tile.GetComponent<FlipCardTile>();
            if (flip == null) { flip = tile.gameObject.AddComponent<FlipCardTile>(); flip.Build(tile.Value, fontFrom); }
            return flip;
        }

        void Build(string value, TMP_Text fontFrom)
        {
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            var rt = (RectTransform)transform;
            foreach (Transform child in transform)
                if (child.name != "NeonOutline") front.Add(child.gameObject);

            // the back: a soft panel inside the frame with two short lines, fitted to the card
            Image panel = RuntimeUiKit.Box(rt, "FlipBack", RuntimeTheme.Mix(p.surface, p.accent, RuntimeTheme.IsDark ? 0.16f : 0.08f));
            panel.raycastTarget = false;
            RuntimeUiKit.Stretch(panel.rectTransform, 10, 10, 10, 10);
            back = panel.gameObject.AddComponent<CanvasGroup>();
            back.blocksRaycasts = false; back.interactable = false;
            TextMeshProUGUI words = RuntimeUiKit.Label(panel.rectTransform, "HowToTell", SkinTypeHints.For(value), 30, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Center);
            RuntimeUiKit.Stretch(words.rectTransform, 20, 20, 16, 16);
            words.enableAutoSizing = true; words.fontSizeMin = 18; words.fontSizeMax = 30;      // two lines always fit, never spill
            words.overflowMode = TextOverflowModes.Ellipsis;
            words.lineSpacing = -4;
            panel.gameObject.SetActive(false);

            // the check in the corner: the card is chosen, whichever face shows. It FILLS when chosen (New13 item 7): the circle grows
            // and fills with the brand colour, then the tick is drawn; it empties again when another card is chosen.
            tick = CheckFillIndicator.AddTo(rt, 44f, 14f, p.buttonFill, p.buttonText);
            BuildMosaic(rt, p);
        }

        /// <summary>The mosaic: Columns x Rows squares over the card (inside its frame), each with its own moment to appear.</summary>
        void BuildMosaic(RectTransform rt, ThemeProfile.Palette p)
        {
            mosaic = RuntimeUiKit.Rect("PixelMosaic", rt);
            RuntimeUiKit.Stretch(mosaic, 10, 10, 10, 10);
            var grid = mosaic.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = Columns;
            var random = new System.Random(GetInstanceID());
            for (int i = 0; i < Columns * Rows; i++)
            {
                RectTransform cell = RuntimeUiKit.Rect("Cell", mosaic);
                Image img = cell.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                float shade = (float)random.NextDouble();
                img.color = RuntimeTheme.Mix(p.surface, p.accent, 0.10f + 0.30f * shade);
                cells.Add(img);
                cellOrder.Add((float)random.NextDouble() * 0.7f);              // when this square appears (0 = first)
            }
            mosaic.gameObject.SetActive(false);
        }

        void SizeMosaic()
        {
            if (mosaic == null) return;
            var grid = mosaic.GetComponent<GridLayoutGroup>();
            Rect r = mosaic.rect;
            grid.cellSize = new Vector2(Mathf.Ceil(r.width / Columns), Mathf.Ceil(r.height / Rows));
        }

        /// <summary>k = 0 (no squares) .. 1 (the card is all squares). Each square fades in over a short part of k, at its own moment.</summary>
        void PaintMosaic(float k)
        {
            if (mosaic == null) return;
            bool show = k > 0.001f;
            if (mosaic.gameObject.activeSelf != show) { mosaic.gameObject.SetActive(show); if (show) SizeMosaic(); }
            if (!show) return;
            mosaic.SetAsLastSibling();
            if (tick != null) tick.transform.SetAsLastSibling();
            for (int i = 0; i < cells.Count; i++)
            {
                Color c = cells[i].color;
                c.a = Mathf.Clamp01((k - cellOrder[i]) / 0.3f);
                cells[i].color = c;
            }
        }

        /// <summary>The card was chosen (true) or another one was (false). `animate` = turn; false = jump (a page restoring a saved answer).</summary>
        public void SetSelected(bool on, bool wasSelected, bool animate)
        {
            if (tick != null) tick.SetOn(on, animate);
            bool target;
            if (!on) target = false;                                   // another card was chosen: this one shows its front again
            else if (wasSelected) target = animate ? !wantBack : wantBack;   // the chosen card tapped again: it turns over
            else target = animate;                                     // newly chosen: its back; a restored answer stays on its front
            Turn(target, animate);
        }

        void Turn(bool toBack, bool animate)
        {
            if (wantBack == toBack && t >= 1f && showingBack == toBack) return;
            wantBack = toBack;
            if (!animate || !isActiveAndEnabled) { Face(toBack); transform.localRotation = Quaternion.identity; t = 1f; return; }
            t = 0f; swapped = false;
        }

        void Face(bool showBack)
        {
            showingBack = showBack;
            foreach (GameObject g in front) if (g != null) g.SetActive(!showBack);
            if (back != null) { back.gameObject.SetActive(showBack); back.alpha = 1f; back.transform.SetAsLastSibling(); }
            if (tick != null) tick.transform.SetAsLastSibling();
        }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Seconds);
            float eased = 1f - (1f - t) * (1f - t);                                 // ease-out
            if (GlowUp.Performance.PerformanceSettings.ReduceEffects)
            {
                // reduced motion: a cross-fade, no turning
                if (!swapped) { Face(wantBack); swapped = true; }
                if (back != null && wantBack) back.alpha = eased;
                return;
            }
            // 0 -> 90 degrees on the old face, then 90 -> 0 on the new one (the faces swap at the edge, where neither can be seen)
            float angle = eased < 0.5f ? Mathf.Lerp(0f, 90f, eased * 2f) : Mathf.Lerp(90f, 0f, (eased - 0.5f) * 2f);
            if (!swapped && eased >= 0.5f) { Face(wantBack); swapped = true; }
            transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            // the mosaic: builds up towards the edge (all squares at 90 degrees), clears on the new face
            PaintMosaic(eased < 0.5f ? eased * 2f : (1f - eased) * 2f);
            if (t >= 1f) { transform.localRotation = Quaternion.identity; PaintMosaic(0f); }
        }

        void OnDisable() { if (t < 1f) { Face(wantBack); t = 1f; transform.localRotation = Quaternion.identity; } PaintMosaic(0f); }
    }
}
