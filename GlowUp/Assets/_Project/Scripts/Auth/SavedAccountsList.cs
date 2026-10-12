using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Auth
{
    /// <summary>
    /// The app's own list of emails under the email field of the log-in page ("Used on this phone"). It replaces the
    /// phone's own saved-account list, which used to open by itself at an odd place (see Common/SystemAutofill).
    ///
    /// How it behaves (the brief of 11 October 2026):
    ///   - tapping the email field shows the list, ONCE: it does not come back while the user stays in the field,
    ///     only after they have left the field and tapped it again;
    ///   - tapping an email fills the field and closes the list;
    ///   - starting to type, or tapping anywhere else, closes the list;
    ///   - the small x of a row takes that email off the list (the account itself is not touched).
    /// Only emails are listed, never a password: see SavedAccounts for what gets on the list.
    ///
    /// The rows are real objects in the Hierarchy (SavedAccountsPanel > SavedAccountRow_1 ...). The panel is switched
    /// off while it has nothing to show: tick it active to see and style it. This script sits on the email field,
    /// which is always on.
    /// </summary>
    public class SavedAccountsList : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_InputField emailField;
        [Tooltip("The box under the email field")]
        [SerializeField] private RectTransform panel;
        [Tooltip("One button per row: tapping it fills the email field")]
        [SerializeField] private Button[] rows;
        [SerializeField] private TextMeshProUGUI[] rowLabels;
        [Tooltip("The small x of each row: takes that email off the list")]
        [SerializeField] private Button[] removeButtons;
        [Tooltip("Optional: the field the cursor moves to after an email was chosen (the password)")]
        [SerializeField] private TMP_InputField nextField;
        [Tooltip("Optional: where the note about a removed email is shown")]
        [SerializeField] private TextMeshProUGUI messageText;

        [Header("Behaviour")]
        [Tooltip("Off = the list is never shown")]
        [SerializeField] private bool offerSavedEmails = true;
        [Tooltip("After an email was chosen, the cursor goes on to the next field")]
        [SerializeField] private bool moveOnAfterChoosing = true;
        [SerializeField] private string msgRemoved = "{0} was taken off this list. The account itself is not changed.";

        readonly List<string> shown = new List<string>();
        bool shownThisVisit;
        Canvas canvas;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        void Awake()
        {
            if (emailField == null) emailField = GetComponent<TMP_InputField>();
            if (emailField != null)
            {
                emailField.onSelect.AddListener(_ => OnFieldEntered());
                emailField.onDeselect.AddListener(_ => OnFieldLeft());
                emailField.onValueChanged.AddListener(_ => Close());            // typing closes the list (choosing an email does too)
            }
            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                int row = i;
                if (rows[i] != null) rows[i].onClick.AddListener(() => Choose(row));
                if (removeButtons != null && i < removeButtons.Length && removeButtons[i] != null) removeButtons[i].onClick.AddListener(() => Remove(row));
            }
            Canvas nearest = panel != null ? panel.GetComponentInParent<Canvas>(true) : null;
            canvas = nearest != null ? nearest.rootCanvas : null;
            Close();
        }

        void OnFieldEntered()
        {
            if (shownThisVisit) return;
            shownThisVisit = true;
            Open();
        }

        void OnFieldLeft()
        {
            shownThisVisit = false;
            // the field is also left by the very tap that lands on the list: then the list stays for that tap
            if (!(Input.GetMouseButton(0) && Inside(panel))) Close();
        }

        void Update()
        {
            // a tap anywhere else closes the list; the tap itself still reaches what was tapped
            if (IsOpen && Input.GetMouseButtonDown(0) && !Inside(panel) && !(emailField != null && Inside((RectTransform)emailField.transform))) Close();
        }

        void Open()
        {
            if (!offerSavedEmails || panel == null || rows == null) return;
            Fill();
            if (shown.Count == 0) return;
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();              // drawn above the rows under the email field
        }

        public void Close()
        {
            if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
        }

        /// <summary>Writes the kept emails into the rows; a row without an email is switched off.</summary>
        void Fill()
        {
            shown.Clear();
            foreach (string email in SavedAccounts.All())
                if (shown.Count < rows.Length) shown.Add(email);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null) continue;
                bool used = i < shown.Count;
                if (rows[i].gameObject.activeSelf != used) rows[i].gameObject.SetActive(used);
                if (used && rowLabels != null && i < rowLabels.Length && rowLabels[i] != null) rowLabels[i].text = shown[i];
            }
        }

        void Choose(int row)
        {
            if (row < 0 || row >= shown.Count || emailField == null) return;
            emailField.text = shown[row];           // tells the page (and closes the list, as typing does)
            Close();
            if (moveOnAfterChoosing && nextField != null) { nextField.Select(); nextField.ActivateInputField(); }
        }

        void Remove(int row)
        {
            if (row < 0 || row >= shown.Count) return;
            string email = shown[row];
            SavedAccounts.Forget(email);
            if (messageText != null && !string.IsNullOrEmpty(msgRemoved)) messageText.text = string.Format(msgRemoved, email);
            Fill();
            if (shown.Count == 0) Close();
        }

        bool Inside(RectTransform area)
        {
            if (area == null || !area.gameObject.activeInHierarchy) return false;
            Camera eye = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(area, Input.mousePosition, eye);
        }
    }
}
