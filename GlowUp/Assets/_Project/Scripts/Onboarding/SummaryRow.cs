using TMPro;
using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// One line of the profile summary (Step 5): "Label ........ Value".
    /// The value is plain text until the user taps Edit; then the editor object (an input field or
    /// a dropdown that sits inactive in the Hierarchy) appears under the label and the text hides.
    /// </summary>
    public class SummaryRow : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private TextMeshProUGUI valueText;
        [Tooltip("The input field / dropdown object for this row. Hidden until the user taps Edit.")]
        [SerializeField] private GameObject editor;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private TMP_Dropdown dropdown;

        [Header("Behaviour")]
        [Tooltip("Untick for values the user cannot change (e.g. Age, which is calculated)")]
        [SerializeField] private bool editable = true;
        [Tooltip("Shown when the value is empty")]
        [SerializeField] private string emptyText = "Not set";

        public bool IsEditing { get; private set; }

        public void SetEditing(bool editing)
        {
            bool show = editing && editable && editor != null;
            IsEditing = show;
            if (editor != null) editor.SetActive(show);
            if (valueText != null) valueText.gameObject.SetActive(!show);
            if (!show) RefreshValue();
        }

        /// <summary>Copies the editor's current value into the plain text.</summary>
        public void RefreshValue()
        {
            if (valueText == null) return;
            string v;
            if (input != null) v = input.text;
            else if (dropdown != null && dropdown.options.Count > 0) v = dropdown.options[Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1)].text;
            else return;                              // no editor: the value is set with SetValue
            valueText.text = string.IsNullOrWhiteSpace(v) ? emptyText : v;
        }

        public void SetValue(string value)
        {
            if (valueText != null) valueText.text = string.IsNullOrWhiteSpace(value) ? emptyText : value;
        }
    }
}
