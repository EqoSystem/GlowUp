using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Scrollable checklist used for skin conditions (Step 2) and allergens / sensitivities (Step 4).
    /// - The option texts are an Inspector list (edit them without touching code).
    /// - The last row is "Apply All".
    /// - An optional "none of these" row (None Row) can be ticked instead of the options: ticking it
    ///   unticks everything else, and ticking anything else unticks it.
    /// - The user can add an item that is not listed; it is selected and reported through
    ///   <see cref="CustomAdded"/> so it can be sent to the database for future research.
    /// - Rows slide/fade in with a stagger (all timings are Inspector fields).
    /// </summary>
    public class ChecklistPanel : MonoBehaviour
    {
        [Header("Options (editable)")]
        [Tooltip("Only used when the list has no option rows in the Hierarchy. Normally edit the label text of each Option_* row instead.")]
        [SerializeField] private string[] options = { "Option 1", "Option 2", "Option 3", "Option 4", "Option 5", "Option 6", "Option 7" };
        [SerializeField] private string applyAllLabel = "Apply All";

        [Header("References")]
        [SerializeField] private RectTransform listContent;
        [Tooltip("Inactive template: a Toggle with a TextMeshProUGUI label. Cloned for every option.")]
        [SerializeField] private Toggle rowTemplate;
        [Tooltip("Optional: the row that means 'none of these' (for example \"No known allergens\"). It counts as an answer, " +
                 "but it is not one of the options: Selected never contains it. Leave empty for a list without such a row.")]
        [SerializeField] private Toggle noneRow;
        [SerializeField] private TMP_InputField customInput;
        [SerializeField] private Button addButton;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Behaviour")]
        [SerializeField] private int customMaxLength = 40;
        [SerializeField] private string msgAdded = "Added. We'll review it so our AI can learn about it.";
        [SerializeField] private string msgDuplicate = "That one is already in the list.";
        [SerializeField] private string msgEmpty = "Type a name first.";

        [Header("Animation")]
        [SerializeField] private float rowStaggerSeconds = 0.05f;
        [SerializeField] private float rowFadeSeconds = 0.25f;

        readonly List<Toggle> rows = new List<Toggle>();
        readonly Dictionary<Toggle, string> labels = new Dictionary<Toggle, string>();
        Toggle applyAll;
        bool suppress;
        bool built;

        public event Action Changed;
        public event Action<string> CustomAdded;

        /// <summary>The ticked options (never "Apply All", never the None row).</summary>
        public List<string> Selected => labels.Where(kv => IsOption(kv.Key) && kv.Key.isOn).Select(kv => kv.Value).ToList();

        /// <summary>True while the "none of these" row is ticked.</summary>
        public bool NoneSelected => noneRow != null && noneRow.isOn;

        /// <summary>True when the user has given an answer: at least one option, or the None row.</summary>
        public bool HasAnswer => NoneSelected || Selected.Count > 0;

        /// <summary>A row that stands for a real option (not "Apply All", not the None row).</summary>
        bool IsOption(Toggle t) => t != applyAll && t != noneRow;

        void Awake()
        {
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (customInput != null) customInput.characterLimit = customMaxLength;
            if (addButton != null) addButton.onClick.AddListener(AddCustom);
            if (feedbackText != null) feedbackText.text = "";
        }

        void Start() { Build(); }

        void Build()
        {
            if (built) return;
            built = true;

            // Rows that already exist in the scene (created by the scene builder) are adopted, so every
            // option is a real object you can select and edit in the Hierarchy. Only when there are none
            // are rows generated from the Options list.
            var existing = new List<Toggle>();
            foreach (Transform child in listContent)
            {
                var t = child.GetComponent<Toggle>();
                if (t != null && t != rowTemplate && child.gameObject.activeSelf) existing.Add(t);
            }
            if (existing.Count > 0)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    var t = existing[i];
                    var label = t.GetComponentInChildren<TextMeshProUGUI>(true);
                    string text = label != null ? label.text : t.gameObject.name;
                    t.SetIsOnWithoutNotify(false);
                    t.onValueChanged.RemoveAllListeners();
                    t.onValueChanged.AddListener(on => OnRowChanged(t, on));
                    rows.Add(t); labels[t] = text;
                    if (t != noneRow && string.Equals(text, applyAllLabel, StringComparison.OrdinalIgnoreCase)) applyAll = t;
                    StartCoroutine(FadeIn(t.gameObject, i * rowStaggerSeconds));
                }
                if (applyAll == null)
                    for (int i = existing.Count - 1; i >= 0; i--) if (existing[i] != noneRow) { applyAll = existing[i]; break; }
            }
            else
            {
                for (int i = 0; i < options.Length; i++) AddRow(options[i], false, i);
                applyAll = AddRow(applyAllLabel, false, options.Length);
            }
            if (applyAll != null)
            {
                applyAll.onValueChanged.RemoveAllListeners();
                applyAll.onValueChanged.AddListener(OnApplyAll);
            }
        }

        /// <summary>Ticks the given options (adding a row for any that is not listed) and unticks the rest, the None row included.</summary>
        public void SetSelected(IEnumerable<string> values)
        {
            Build();
            var set = new HashSet<string>(values ?? new string[0], StringComparer.OrdinalIgnoreCase);
            suppress = true;
            foreach (var v in set) if (!labels.Values.Contains(v, StringComparer.OrdinalIgnoreCase)) AddRow(v, true, rows.Count, animate: false);
            foreach (var kv in labels) if (IsOption(kv.Key)) kv.Key.SetIsOnWithoutNotify(set.Contains(kv.Value));
            if (noneRow != null) noneRow.SetIsOnWithoutNotify(false);
            suppress = false;
            SyncApplyAll();
        }

        /// <summary>Ticks the "none of these" row and unticks every option.</summary>
        public void SelectNone()
        {
            Build();
            if (noneRow == null) return;
            suppress = true;
            foreach (var kv in labels) if (kv.Key != noneRow) kv.Key.SetIsOnWithoutNotify(false);
            noneRow.SetIsOnWithoutNotify(true);
            suppress = false;
        }

        Toggle AddRow(string text, bool isOn, int index, bool animate = true)
        {
            var t = Instantiate(rowTemplate, listContent);
            t.gameObject.name = "Option_" + text;
            t.gameObject.SetActive(true);
            var label = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
            t.SetIsOnWithoutNotify(isOn);
            t.onValueChanged.AddListener(on => OnRowChanged(t, on));
            rows.Add(t); labels[t] = text;
            if (applyAll != null) t.transform.SetSiblingIndex(applyAll.transform.GetSiblingIndex());   // keep Apply All last
            if (animate) StartCoroutine(FadeIn(t.gameObject, index * rowStaggerSeconds));
            return t;
        }

        IEnumerator FadeIn(GameObject row, float delay)
        {
            if (!row.TryGetComponent<CanvasGroup>(out var cg)) cg = row.AddComponent<CanvasGroup>();
            cg.alpha = 0;
            yield return new WaitForSecondsRealtime(delay);
            float e = 0;
            while (e < rowFadeSeconds)
            {
                e += Time.unscaledDeltaTime;
                cg.alpha = Mathf.SmoothStep(0, 1, e / Mathf.Max(0.01f, rowFadeSeconds));
                yield return null;
            }
            cg.alpha = 1;
        }

        void OnRowChanged(Toggle row, bool on)
        {
            if (suppress) return;
            if (on && noneRow != null)
            {
                suppress = true;
                if (row == noneRow) { foreach (var kv in labels) if (kv.Key != noneRow) kv.Key.SetIsOnWithoutNotify(false); }   // "none" replaces every option
                else noneRow.SetIsOnWithoutNotify(false);                                                                       // an option replaces "none"
                suppress = false;
            }
            SyncApplyAll();
            Changed?.Invoke();
        }

        void OnApplyAll(bool on)
        {
            if (suppress) return;
            suppress = true;
            foreach (var kv in labels) if (IsOption(kv.Key)) kv.Key.SetIsOnWithoutNotify(on);
            if (on && noneRow != null) noneRow.SetIsOnWithoutNotify(false);
            suppress = false;
            Changed?.Invoke();
        }

        void SyncApplyAll()
        {
            if (applyAll == null) return;
            var optionRows = labels.Keys.Where(IsOption).ToList();
            applyAll.SetIsOnWithoutNotify(optionRows.Count > 0 && optionRows.All(t => t.isOn));
        }

        void AddCustom()
        {
            string text = customInput != null ? customInput.text.Trim() : "";
            if (text.Length == 0) { Say(msgEmpty); return; }
            if (labels.Values.Contains(text, StringComparer.OrdinalIgnoreCase)) { Say(msgDuplicate); return; }
            suppress = true;
            var t = AddRow(text, true, rows.Count);
            if (noneRow != null) noneRow.SetIsOnWithoutNotify(false);          // an added item replaces "none"
            suppress = false;
            customInput.text = "";
            Say(msgAdded);
            SyncApplyAll();
            CustomAdded?.Invoke(text);
            Changed?.Invoke();
        }

        void Say(string m) { if (feedbackText != null) feedbackText.text = m; }
    }
}
