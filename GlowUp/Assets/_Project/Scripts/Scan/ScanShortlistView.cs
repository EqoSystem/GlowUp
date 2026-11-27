using System;
using System.Collections.Generic;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The pop-up "IS IT ONE OF THESE?" of the scan screen: up to three products the scan may be about, a search box to
    /// type or correct the name and the brand, and the ways on when none of them is right. It is shown when the app is
    /// not sure which product it is, and when the AI could not name it either, so a scan never ends in a dead end.
    ///
    ///  - Tapping a row chooses that product.
    ///  - Typing into the boxes looks the name up in the app's product list while it is typed; "Use ..." takes what was
    ///    typed as the product's name when the list does not have it.
    ///  - The two buttons at the bottom: scan again, and "None of these" (its words are set by the scan screen: after
    ///    the AI was asked it reads "Finish without a name").
    ///
    /// What is offered comes from ScanShortlist; this object only shows it and reports the user's choice.
    /// </summary>
    public class ScanShortlistView : MonoBehaviour
    {
        [Header("The pop-up")]
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;

        [Header("The products offered (three rows)")]
        [SerializeField] private Button[] rows = new Button[3];
        [SerializeField] private TextMeshProUGUI[] rowNames = new TextMeshProUGUI[3];
        [SerializeField] private TextMeshProUGUI[] rowDetails = new TextMeshProUGUI[3];
        [Tooltip("Shown in the place of the rows when there is nothing to offer")]
        [SerializeField] private TextMeshProUGUI emptyText;

        [Header("Not in the list: type it")]
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private TMP_InputField brandInput;
        [Tooltip("Takes what was typed as the product's name")]
        [SerializeField] private Button useTypedButton;
        [SerializeField] private TextMeshProUGUI useTypedLabel;

        [Header("Buttons")]
        [SerializeField] private Button noneButton;
        [SerializeField] private TextMeshProUGUI noneLabel;
        [SerializeField] private Button scanAgainButton;
        [Tooltip("Not one of these and not in GlowUp: opens Send for Review")]
        [SerializeField] private Button reviewButton;

        [Header("Wording")]
        [Tooltip("The line under the name of a product this phone has scanned before. {0} = brand and kind")]
        [SerializeField] private string seenBeforeFormat = "{0} (scanned on this phone before)";
        [SerializeField] private string seenBeforeOnly = "Scanned on this phone before";
        [Tooltip("{0} = what was typed")]
        [SerializeField] private string useTypedFormat = "Use \"{0}\" as the name";
        [SerializeField] private string useTypedIdle = "Type the name above";
        [SerializeField] private string msgNothingToOffer = "No product of our list fits what the camera read. Type its name below.";
        [SerializeField] private string msgNothingTyped = "Nothing in our list fits what you typed. You can still use it as the name.";
        [Tooltip("A typed name needs at least this many letters to be used")]
        [SerializeField] private int shortestTypedName = 2;

        /// <summary>Looks a typed name and brand up (set by the scan screen).</summary>
        public Func<string, string, List<ScanShortlist.Entry>> Search;
        /// <summary>"Send for review" was tapped: the typed name and brand (set by the scan screen).</summary>
        public Action<string, string> ReviewRequested;

        readonly List<ScanShortlist.Entry> shown = new List<ScanShortlist.Entry>();
        List<ScanShortlist.Entry> offered = new List<ScanShortlist.Entry>();
        Action<ScanShortlist.Entry, string> picked;
        Action<string, string> typed;
        Action none, scanAgain;
        bool wired;

        public bool IsOpen => panel != null && panel.IsOpen;

        void Awake() { Wire(); }

        void Wire()
        {
            if (wired) return;
            wired = true;
            for (int i = 0; i < rows.Length; i++)
            {
                int row = i;
                if (rows[i] != null) rows[i].onClick.AddListener(() => Pick(row));
            }
            if (nameInput != null) nameInput.onValueChanged.AddListener(_ => Refresh());
            if (brandInput != null) brandInput.onValueChanged.AddListener(_ => Refresh());
            if (useTypedButton != null) useTypedButton.onClick.AddListener(UseTyped);
            if (noneButton != null) noneButton.onClick.AddListener(() => Leave(none));
            if (scanAgainButton != null) scanAgainButton.onClick.AddListener(() => Leave(scanAgain));
            if (reviewButton != null) reviewButton.onClick.AddListener(() => { string name = TypedName, brand = TypedBrand; Hide(); ReviewRequested?.Invoke(name, brand); });
        }

        /// <summary>
        /// Opens the pop-up. `entries` = what the scan suggests (may be empty). `onPicked` gets the chosen product and what
        /// stood in the name box at that moment ("" when nothing was typed); `onTyped` gets the typed name and brand.
        /// </summary>
        public void Show(string title, string message, List<ScanShortlist.Entry> entries, string noneText,
                         Action<ScanShortlist.Entry, string> onPicked, Action<string, string> onTyped, Action onNone, Action onScanAgain)
        {
            if (panel == null) return;
            Wire();
            offered = entries ?? new List<ScanShortlist.Entry>();
            picked = onPicked; typed = onTyped; none = onNone; scanAgain = onScanAgain;
            if (titleText != null) titleText.text = title ?? "";
            if (messageText != null) messageText.text = message ?? "";
            if (noneLabel != null && !string.IsNullOrEmpty(noneText)) noneLabel.text = noneText;
            if (nameInput != null) nameInput.SetTextWithoutNotify("");
            if (brandInput != null) brandInput.SetTextWithoutNotify("");
            Refresh();
            panel.Show();
        }

        public void Hide() { if (panel != null) panel.Hide(); }

        string TypedName => nameInput != null ? nameInput.text.Trim() : "";
        string TypedBrand => brandInput != null ? brandInput.text.Trim() : "";

        /// <summary>The rows: what the scan suggests, or (as soon as something is typed) what fits the typed words.</summary>
        void Refresh()
        {
            string name = TypedName, brand = TypedBrand;
            bool typing = name.Length > 0 || brand.Length > 0;
            shown.Clear();
            if (!typing) shown.AddRange(offered);
            else if (Search != null)
            {
                List<ScanShortlist.Entry> found = Search(name, brand);
                if (found != null) shown.AddRange(found);
            }

            for (int i = 0; i < rows.Length; i++)
            {
                bool has = i < shown.Count && shown[i] != null;
                if (rows[i] != null) rows[i].gameObject.SetActive(has);
                if (!has) continue;
                ScanShortlist.Entry entry = shown[i];
                if (i < rowNames.Length && rowNames[i] != null) rowNames[i].text = entry.productName;
                string detail = entry.Detail;
                if (entry.byLook) detail = detail.Length > 0 ? string.Format(seenBeforeFormat, detail) : seenBeforeOnly;
                if (i < rowDetails.Length && rowDetails[i] != null) rowDetails[i].text = detail;
            }
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(shown.Count == 0);
                emptyText.text = typing ? msgNothingTyped : msgNothingToOffer;
            }
            bool usable = name.Length >= Mathf.Max(1, shortestTypedName);
            if (useTypedButton != null) useTypedButton.interactable = usable;
            if (useTypedLabel != null) useTypedLabel.text = usable ? string.Format(useTypedFormat, name) : useTypedIdle;
        }

        void Pick(int row)
        {
            if (row < 0 || row >= shown.Count || shown[row] == null) return;
            ScanShortlist.Entry entry = shown[row];
            string name = TypedName;
            Action<ScanShortlist.Entry, string> then = picked;
            Hide();
            then?.Invoke(entry, name);
        }

        void UseTyped()
        {
            string name = TypedName, brand = TypedBrand;
            if (name.Length < Mathf.Max(1, shortestTypedName)) return;
            Action<string, string> then = typed;
            Hide();
            then?.Invoke(name, brand);
        }

        void Leave(Action then)
        {
            Hide();
            then?.Invoke();
        }
    }
}
