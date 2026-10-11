using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A long text (or any block) kept behind a button. Closed, only the button shows; a tap opens the block under it
    /// and the words on the button change; another tap closes it again. The result screen uses it for "Why this
    /// result?" and for the details of the FDA Philippines check, so the screen stays short until the user asks for more.
    ///
    /// The block is switched OFF in the Hierarchy while it is closed: tick it active to see and restyle it.
    /// </summary>
    public class ExpandableSection : MonoBehaviour
    {
        [Tooltip("The button that opens and closes the block")]
        [SerializeField] private Button toggleButton;
        [Tooltip("The words on the button. Optional: leave empty for a button whose words do not change.")]
        [SerializeField] private TextMeshProUGUI toggleLabel;
        [Tooltip("A small arrow that turns when the block opens. Optional.")]
        [SerializeField] private RectTransform arrow;
        [Tooltip("What is shown and hidden")]
        [SerializeField] private GameObject body;

        [Header("Wording")]
        [SerializeField] private string closedLabel = "Read more";
        [SerializeField] private string openLabel = "Show less";

        [Header("Behaviour")]
        [Tooltip("On = the block is open when the screen opens")]
        [SerializeField] private bool startsOpen = false;
        [Tooltip("The arrow's turn in degrees while closed, and while open")]
        [SerializeField] private float arrowClosedAngle = 0f;
        [SerializeField] private float arrowOpenAngle = 180f;

        public bool IsOpen { get; private set; }
        /// <summary>Raised after the block was opened (true) or closed (false).</summary>
        public event Action<bool> Changed;

        void Awake()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(() => SetOpen(!IsOpen));
            IsOpen = startsOpen;
            Apply();
        }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            Apply();
            Changed?.Invoke(open);
        }

        void Apply()
        {
            if (body != null && body.activeSelf != IsOpen) body.SetActive(IsOpen);
            if (toggleLabel != null) toggleLabel.text = IsOpen ? openLabel : closedLabel;
            if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, IsOpen ? arrowOpenAngle : arrowClosedAngle);
            // the screen around it makes room (or closes the gap) at once
            if (transform is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }
}
