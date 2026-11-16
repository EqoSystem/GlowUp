using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// THE LOADING STATE OF A BUTTON, the same everywhere (7 November 2026): while the button waits for something, a spinner turns
    /// INSIDE it, left of its label and centred with it, in the label's own colour; the button cannot be pressed meanwhile.
    /// The label is never cut: when the long words do not fit next to the spinner, the short words are shown ("Analyzing…" for
    /// "Analyzing Packaging..."). Other buttons on the screen (for example "Not now") are not touched.
    /// </summary>
    public static class ButtonBusy
    {
        class State { public float fontSize; public bool autoSize; public Vector4 margin; public RectTransform spinner; public ColorBlock colors; public bool colorsKept; }
        static readonly Dictionary<TMP_Text, State> states = new Dictionary<TMP_Text, State>();
        const float Gap = 16f;

        /// <summary>
        /// busy = true: spinner on, button off, `words` (or `shortWords` when they do not fit). busy = false: spinner off, button
        /// `interactable`, label `words`.
        /// </summary>
        public static void Set(Button button, TMP_Text label, bool busy, string words, string shortWords = null, bool interactable = true)
        {
            if (label == null) { if (button != null) button.interactable = !busy && interactable; return; }
            if (!states.TryGetValue(label, out State state) || state.spinner == null)
            {
                state = new State { fontSize = label.enableAutoSizing ? label.fontSizeMax : label.fontSize, autoSize = label.enableAutoSizing, margin = label.margin };
                Image arc = RuntimeUiKit.Icon(label.transform, "BusySpinner", RuntimeIcons.SpinnerArc, label.color, 40);
                arc.gameObject.AddComponent<Spinner>();
                state.spinner = arc.rectTransform;
                state.spinner.anchorMin = state.spinner.anchorMax = new Vector2(0.5f, 0.5f);
                state.spinner.gameObject.SetActive(false);
                states[label] = state;
            }
            // a busy button keeps its own colour (only slightly softer) instead of the washed-out "disabled" look, so its words stay readable
            if (button != null)
            {
                if (!state.colorsKept) { state.colors = button.colors; state.colorsKept = true; }
                ColorBlock look = state.colors;
                if (busy) { look.disabledColor = new Color(look.normalColor.r, look.normalColor.g, look.normalColor.b, look.normalColor.a * 0.85f); look.fadeDuration = 0f; }
                button.colors = look;
                button.interactable = !busy && interactable;
            }
            if (!busy)
            {
                state.spinner.gameObject.SetActive(false);
                label.margin = state.margin;
                label.enableAutoSizing = state.autoSize;
                if (!state.autoSize) label.fontSize = state.fontSize;
                if (words != null) label.text = words;
                return;
            }

            // busy: fixed size (no shrinking to fit), then the words that fit beside the spinner
            label.enableAutoSizing = false;
            label.fontSize = Mathf.Max(state.autoSize ? Mathf.Max(label.fontSizeMin, state.fontSize * 0.85f) : state.fontSize, 28f);
            float size = Mathf.Round(label.fontSize * 1.05f);
            float room = Mathf.Max(0f, label.rectTransform.rect.width - state.margin.x - state.margin.z);
            string chosen = words ?? "";
            float width = label.GetPreferredValues(chosen).x;
            if (!string.IsNullOrEmpty(shortWords) && width + size + Gap > room) { chosen = shortWords; width = label.GetPreferredValues(chosen).x; }
            label.text = chosen;
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableWordWrapping = false;
            // the words move right by half the spinner's room, the spinner sits just left of them: the pair is centred in the button
            label.margin = new Vector4(state.margin.x + size + Gap, state.margin.y, state.margin.z, state.margin.w);
            float middle = (state.margin.x - state.margin.z) * 0.5f;                  // the centre of the label's own area
            state.spinner.sizeDelta = new Vector2(size, size);
            state.spinner.anchoredPosition = new Vector2(middle - width * 0.5f - Gap * 0.5f, 0f);
            state.spinner.GetComponent<Image>().color = label.color;
            state.spinner.gameObject.SetActive(true);
        }
    }
}
