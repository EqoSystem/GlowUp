using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Inline helper text under an input field plus an optional badge (e.g. "Required",
    /// "Optional", "0/20"). Text, badge label and counter are all Inspector-editable.
    /// Visibility follows ContextualHintController through a HintVisibilityTarget.
    /// </summary>
    public class InlineHelperText : MonoBehaviour
    {
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TextMeshProUGUI helperLabel;
        [SerializeField] private TextMeshProUGUI badgeLabel;
        [TextArea] [SerializeField] private string helperMessage = "Helpful hint about this field";
        [SerializeField] private string badgeMessage = "Required";
        [Tooltip("Show 'n/max' in the badge instead of badgeMessage")]
        [SerializeField] private bool showCharacterCounter = false;

        void Awake()
        {
            if (helperLabel != null) helperLabel.text = helperMessage;
            if (badgeLabel != null) badgeLabel.text = badgeMessage;
            if (inputField != null && showCharacterCounter)
            {
                inputField.onValueChanged.AddListener(UpdateCounter);
                UpdateCounter(inputField.text);
            }
        }

        void UpdateCounter(string v)
        {
            if (badgeLabel != null && inputField != null)
                badgeLabel.text = v.Length + "/" + inputField.characterLimit;
        }

        public void SetHelper(string message, Color? color = null)
        {
            if (helperLabel == null) return;
            helperLabel.text = message;
            if (color.HasValue) helperLabel.color = color.Value;
        }
    }
}
