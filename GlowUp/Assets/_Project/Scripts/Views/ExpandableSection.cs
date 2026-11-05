using System;
using System.Collections;
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
        [Tooltip("How long opening and closing takes, in seconds. 0 = at once. The block needs a RectMask2D so its text is cut while it moves.")]
        [SerializeField] private float animateSeconds = 0f;

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
            if (animateSeconds > 0f && body != null && gameObject.activeInHierarchy)
            {
                if (running != null) StopCoroutine(running);
                running = StartCoroutine(Slide(open));
            }
            else Apply();
            Changed?.Invoke(open);
        }

        Coroutine running;

        /// <summary>The block's height moves between its height now and its full height (or 0); the screen around it follows on every frame.</summary>
        IEnumerator Slide(bool open)
        {
            if (toggleLabel != null) toggleLabel.text = open ? openLabel : closedLabel;
            var rect = (RectTransform)body.transform;
            var parent = transform as RectTransform;
            if (!body.TryGetComponent(out LayoutElement size)) size = body.AddComponent<LayoutElement>();
            float from = body.activeSelf ? rect.rect.height : 0f;
            // its full height: measured with the block free, in the width it really has
            size.enabled = false;
            body.SetActive(true);
            if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            float full = rect.rect.height;
            size.enabled = true;
            float to = open ? full : 0f;
            float fromAngle = arrow != null ? arrow.localEulerAngles.z : 0f, toAngle = open ? arrowOpenAngle : arrowClosedAngle;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / animateSeconds)
            {
                float eased = Mathf.SmoothStep(0f, 1f, t);
                size.minHeight = size.preferredHeight = Mathf.Lerp(from, to, eased);
                if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(fromAngle, toAngle, eased));
                if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
                yield return null;
            }
            size.minHeight = size.preferredHeight = -1f;
            size.enabled = false;
            running = null;
            Apply();
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
