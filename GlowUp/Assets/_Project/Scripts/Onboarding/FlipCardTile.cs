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
        const float Seconds = 0.28f;

        readonly List<GameObject> front = new List<GameObject>();
        CanvasGroup back;
        Image tick;
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

            // the tick in the corner: the card is chosen, whichever face shows
            tick = RuntimeUiKit.Icon(rt, "ChosenTick", RuntimeIcons.Check, p.buttonFill, 40);
            var tr = tick.rectTransform;
            tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(1f, 1f);
            tr.anchoredPosition = new Vector2(-16f, -16f);
            tick.gameObject.SetActive(false);
        }

        /// <summary>The card was chosen (true) or another one was (false). `animate` = turn; false = jump (a page restoring a saved answer).</summary>
        public void SetSelected(bool on, bool wasSelected, bool animate)
        {
            if (tick != null) { tick.gameObject.SetActive(on); tick.transform.SetAsLastSibling(); }
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
            if (t >= 1f) transform.localRotation = Quaternion.identity;
        }

        void OnDisable() { if (t < 1f) { Face(wantBack); t = 1f; transform.localRotation = Quaternion.identity; } }
    }
}
