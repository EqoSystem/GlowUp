using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// "IS THIS ...?" A small question on the camera picture, asked when the pack in front of the camera looks like a
    /// product this phone has scanned before. The scan goes on behind it. "Yes" names the product at once (no AI is
    /// asked); "No" puts the question away and that product is not suggested again in this scan.
    ///
    /// The app never takes the look of a pack for the product without asking: two products of one line can look alike.
    /// </summary>
    public class ScanProductQuestion : MonoBehaviour
    {
        [Tooltip("The banner with the question and its two buttons (hidden until there is something to ask)")]
        [SerializeField] private GameObject banner;
        [SerializeField] private TextMeshProUGUI questionText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        [Header("Wording")]
        [Tooltip("{0} = the product's name")]
        [SerializeField] private string question = "Is this {0}?";
        [Tooltip("{0} = the product's name, {1} = its brand")]
        [SerializeField] private string questionWithBrand = "Is this {0} by {1}?";

        Action yes, no;

        /// <summary>True while a question is on screen.</summary>
        public bool IsOpen => banner != null && banner.activeSelf;

        void Awake()
        {
            if (yesButton != null) yesButton.onClick.AddListener(() => Answer(true));
            if (noButton != null) noButton.onClick.AddListener(() => Answer(false));
            if (banner != null) banner.SetActive(false);
        }

        /// <summary>Asks whether the product in front of the camera is this one. One question at a time: a new one takes the place of the old one.</summary>
        public void Ask(string productName, string brandName, Action onYes, Action onNo)
        {
            if (banner == null || string.IsNullOrWhiteSpace(productName)) return;
            yes = onYes; no = onNo;
            string name = productName.Trim(), brand = (brandName ?? "").Trim();
            // a name that already starts with its brand is not given the brand a second time
            bool brandInName = brand.Length > 0 && name.StartsWith(brand, StringComparison.OrdinalIgnoreCase);
            if (questionText != null) questionText.text = brand.Length > 0 && !brandInName ? string.Format(questionWithBrand, name, brand) : string.Format(question, name);
            banner.SetActive(true);
        }

        /// <summary>Puts the question away without an answer (the scan was started over, or the product was named another way).</summary>
        public void Hide()
        {
            yes = no = null;
            if (banner != null) banner.SetActive(false);
        }

        void Answer(bool saidYes)
        {
            Action chosen = saidYes ? yes : no;
            Hide();
            chosen?.Invoke();
        }
    }
}
