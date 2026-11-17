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
        class State { public float fontSize; public bool autoSize; public Vector4 margin; public RectTransform spinner; public ColorBlock colors; public bool colorsKept; public List<GameObject> hidden = new List<GameObject>(); }
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
            // A pill button keeps its icon and label side by side in one centred row (the "Take photo" button of the scanner). The spinner
            // then takes the ICON's place in that row, as a row member of its own: nothing is worked out by hand, so it can never lie on top
            // of the words (17 Nov 2026: "Identifying..." had the spinner over its first letters, the camera icon still showing).
            HorizontalLayoutGroup row = label.transform.parent != null ? label.transform.parent.GetComponent<HorizontalLayoutGroup>() : null;
            if (row != null)
            {
                foreach (GameObject kept in state.hidden) if (kept != null) kept.SetActive(true);
                state.hidden.Clear();
                if (!busy)
                {
                    state.spinner.gameObject.SetActive(false);
                    if (words != null) label.text = words;
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)row.transform);
                    return;
                }
                foreach (Transform child in row.transform)
                    if (child != label.transform && child != state.spinner && child.gameObject.activeSelf) { state.hidden.Add(child.gameObject); }
                foreach (GameObject icon in state.hidden) icon.SetActive(false);
                float spin = Mathf.Round(Mathf.Max(label.fontSize, 28f) * 1.05f);
                if (state.spinner.parent != row.transform) state.spinner.SetParent(row.transform, false);
                state.spinner.SetSiblingIndex(label.transform.GetSiblingIndex());           // left of the words
                state.spinner.anchorMin = state.spinner.anchorMax = new Vector2(0.5f, 0.5f);
                state.spinner.sizeDelta = new Vector2(spin, spin);
                LayoutElement spinnerSize = state.spinner.GetComponent<LayoutElement>();
                if (spinnerSize == null) spinnerSize = state.spinner.gameObject.AddComponent<LayoutElement>();
                spinnerSize.minWidth = spinnerSize.preferredWidth = spin; spinnerSize.minHeight = spinnerSize.preferredHeight = spin;
                state.spinner.GetComponent<Image>().color = label.color;
                state.spinner.gameObject.SetActive(true);
                string text = words ?? "";
                float widest = row.padding.horizontal + spin + row.spacing + label.GetPreferredValues(text).x;
                RectTransform rowRect = (RectTransform)row.transform;
                if (!string.IsNullOrEmpty(shortWords) && widest > rowRect.rect.width && rowRect.rect.width > 0f) text = shortWords;
                label.text = text;
                label.overflowMode = TextOverflowModes.Overflow;
                label.enableWordWrapping = false;
                LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
                return;
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
