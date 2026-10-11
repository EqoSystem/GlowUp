using System;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A row of pill tabs where exactly one is chosen (for example Lips / Cheeks / Eyes / Face).
    /// The chosen tab is filled with the accent colour. Each tab is a Button with an Image and a label.
    /// </summary>
    public class ChoiceTabs : ThemeAwareBehaviour
    {
        [SerializeField] private Button[] tabs;
        [SerializeField] private TextMeshProUGUI[] labels;
        [Tooltip("The tab chosen when the scene opens")]
        [SerializeField] private int selectedIndex = 0;

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color selectedFill = new Color32(217, 142, 160, 255);
        [SerializeField] private Color selectedText = new Color32(58, 46, 45, 255);
        [SerializeField] private Color normalFill = new Color32(247, 243, 235, 255);
        [SerializeField] private Color normalText = new Color32(74, 65, 64, 255);
        [Tooltip("How faint a tab is while it cannot be chosen (see SetAvailable)")]
        [Range(0f, 1f)] [SerializeField] private float unavailableAlpha = 0.32f;

        public int SelectedIndex => selectedIndex;
        public event Action<int> Changed;
        bool[] unavailable;

        /// <summary>Switches one tab off (faint, cannot be tapped) or on again. The chosen tab stays chosen.</summary>
        public void SetAvailable(int index, bool available)
        {
            if (tabs == null || index < 0 || index >= tabs.Length) return;
            if (unavailable == null || unavailable.Length != tabs.Length) unavailable = new bool[tabs.Length];
            unavailable[index] = !available;
            if (tabs[index] != null) tabs[index].interactable = available;
            Paint();
        }

        void Awake()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                if (tabs[i] != null) { tabs[i].transition = Selectable.Transition.None; tabs[i].onClick.AddListener(() => Select(index, true)); }
            }
            Paint();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            selectedFill = p.buttonFill; selectedText = p.buttonText; normalFill = p.inputBackground; normalText = p.mainText;
            Paint();
        }

        public void Select(int index, bool notify = false)
        {
            if (tabs == null || tabs.Length == 0) return;
            index = Mathf.Clamp(index, 0, tabs.Length - 1);
            bool changed = index != selectedIndex;
            selectedIndex = index;
            Paint();
            if (notify && changed) Changed?.Invoke(index);
        }

        void Paint()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == selectedIndex;
                float alpha = unavailable != null && i < unavailable.Length && unavailable[i] ? unavailableAlpha : 1f;
                Color fill = on ? selectedFill : normalFill, text = on ? selectedText : normalText;
                fill.a *= alpha; text.a *= alpha;
                if (tabs[i] != null && tabs[i].targetGraphic != null) tabs[i].targetGraphic.color = fill;
                if (labels != null && i < labels.Length && labels[i] != null) labels[i].color = text;
            }
        }
    }
}
