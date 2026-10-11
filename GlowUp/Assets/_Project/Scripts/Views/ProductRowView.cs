using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Views
{
    /// <summary>
    /// A product card / row with an icon, a name, a second line, an optional note and up to two
    /// buttons. Used for the dashboard recommendations, the alternatives list of a result, and the
    /// product picker of AR Try-On. The one you see in the Editor is the pattern: the app copies it.
    /// </summary>
    public class ProductRowView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI noteText;
        [Tooltip("Optional: the row itself is tappable")]
        [SerializeField] private Button rowButton;
        [SerializeField] private Button primaryButton;
        [SerializeField] private TextMeshProUGUI primaryLabel;
        [SerializeField] private Button secondaryButton;
        [SerializeField] private TextMeshProUGUI secondaryLabel;

        /// <summary>Whatever this row stands for (a product, a saved scan).</summary>
        public object Data { get; private set; }
        public event Action<ProductRowView> RowClicked, PrimaryClicked, SecondaryClicked;

        bool hooked;

        public void Bind(object data, string title, string subtitle, string note, Sprite sprite)
        {
            if (!hooked)
            {
                hooked = true;
                if (rowButton != null) rowButton.onClick.AddListener(() => RowClicked?.Invoke(this));
                if (primaryButton != null) primaryButton.onClick.AddListener(() => PrimaryClicked?.Invoke(this));
                if (secondaryButton != null) secondaryButton.onClick.AddListener(() => SecondaryClicked?.Invoke(this));
            }
            Data = data;
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle;
            if (noteText != null) { noteText.text = note ?? ""; noteText.gameObject.SetActive(!string.IsNullOrWhiteSpace(note)); }
            if (icon != null && sprite != null) icon.sprite = sprite;
        }

        public void SetPrimary(string text, bool visible = true, bool interactable = true)
        {
            if (primaryButton != null) { primaryButton.gameObject.SetActive(visible); primaryButton.interactable = interactable; }
            if (primaryLabel != null && text != null) primaryLabel.text = text;
        }

        public void SetSecondary(string text, bool visible = true, bool interactable = true)
        {
            if (secondaryButton != null) { secondaryButton.gameObject.SetActive(visible); secondaryButton.interactable = interactable; }
            if (secondaryLabel != null && text != null) secondaryLabel.text = text;
        }
    }
}
