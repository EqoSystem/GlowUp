using System;
using GlowUp.Common;
using GlowUp.Views;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE BARCODE CARD of the AI Smart Scan screen (11 November 2026). OPTIONAL: it opens only when the brand and the product could
    /// not be told from the pack (live scan or photo) but a barcode is there, or when the user chooses "Use a barcode" in Type it in.
    /// The user focuses on the number: it is shown big, can be typed or corrected, and "Find product" looks it up (the catalogue's
    /// Barcode field, then what this phone learned). A barcode nobody has registered yet: the user names the product and this phone
    /// remembers the number for it. "Photograph the barcode" takes a photo of the bars (the photo step reads them first).
    /// The card only collects what the user did; SmartScanFlowController decides what happens next.
    /// </summary>
    public class ScanBarcodeCardView : MonoBehaviour
    {
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TMP_InputField numberInput;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [Tooltip("The product's name, asked only when no product has the barcode yet")]
        [SerializeField] private GameObject nameRow;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button findButton;
        [SerializeField] private Button photoButton;
        [SerializeField] private Button cancelButton;
        [Header("Words")]
        [SerializeField] private string hintWords = "Type the digits under the barcode, or photograph it. GlowUp finds the product by that number.";
        [SerializeField] private string notABarcodeWords = "That is not a barcode yet: it has 6 to 14 digits.";
        [SerializeField] private string unknownWords = "No product has this barcode yet. Type the product's name below: this phone then remembers the number for it.";
        [SerializeField] private string nameNotFoundWords = "No product in the GlowUp catalogue is called that. Try another name, or choose Type it in.";

        /// <summary>The user asked for this number: (barcode key, the typed product name or "").</summary>
        public event Action<string, string> FindRequested;
        public event Action PhotoRequested;

        public bool IsOpen => panel != null && panel.IsOpen;

        /// <summary>The digits as typed, reduced to the app's barcode key ("" = not a barcode).</summary>
        public string Key => MlKitBarcodeReader.Key(numberInput != null ? numberInput.text : "");

        bool wired;

        void Wire()
        {
            if (wired) return;
            wired = true;
            if (findButton != null) findButton.onClick.AddListener(Find);
            if (photoButton != null) photoButton.onClick.AddListener(() => PhotoRequested?.Invoke());
            if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
            if (numberInput != null) numberInput.onValueChanged.AddListener(_ => { if (nameRow != null) nameRow.SetActive(false); Say(hintWords); });
        }

        /// <summary>Opens the card. `barcode` is filled in when the camera read one ("" = the user types it).</summary>
        public void Show(string barcode, string message = null)
        {
            if (panel == null) return;
            Wire();
            if (numberInput != null) numberInput.SetTextWithoutNotify(barcode ?? "");
            if (nameInput != null) nameInput.SetTextWithoutNotify("");
            if (nameRow != null) nameRow.SetActive(false);
            Say(string.IsNullOrEmpty(message) ? hintWords : message);
            panel.Show();
        }

        public void Hide() { if (panel != null) panel.Hide(); }

        /// <summary>The number is right but no product has it: the name box opens (and the words say why).</summary>
        public void AskForName() { if (nameRow != null) nameRow.SetActive(true); Say(unknownWords); }

        public void NameNotFound() { Say(nameNotFoundWords); }

        public void Say(string words) { if (feedbackText != null) feedbackText.text = words ?? ""; }

        void Find()
        {
            string key = Key;
            if (key.Length == 0) { Say(notABarcodeWords); return; }
            string name = nameInput != null && nameRow != null && nameRow.activeSelf ? nameInput.text.Trim() : "";
            FindRequested?.Invoke(key, name);
        }
    }
}
