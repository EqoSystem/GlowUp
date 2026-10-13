using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AR
{
    /// <summary>
    /// The card that fills the camera box of AR Try-On while there is no camera picture yet.
    ///
    /// Without it the box was an empty, nearly black area until the camera ran, and the phone's own question "Allow GlowUp
    /// to take pictures?" stood over a dark screen (the project owner saw "the screen goes black", 13 October 2026). The
    /// card is in the page's light colours and says what is going on:
    ///   - Asking:   the camera is not allowed yet, and why the app needs it (the phone's question follows by itself);
    ///   - Starting: the camera is allowed and is being switched on;
    ///   - Refused:  the user said no; a button asks again, or opens the phone's settings when Android no longer asks;
    ///   - Problem:  the camera is allowed but did not start; a button tries again.
    /// As soon as a picture is there the card fades away.
    ///
    /// It lies INACTIVE in the Hierarchy (it would hide the camera box and the face guide there); tick it active to see
    /// and style it. Every wording is an Inspector field. ArTryOnFlowController shows and hides it.
    /// </summary>
    public class CameraWaitCard : MonoBehaviour
    {
        public enum State { Hidden, Asking, Starting, Refused, Problem }

        [Header("References")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TextMeshProUGUI actionLabel;

        [Header("Wording: the camera is not allowed yet")]
        [SerializeField] private string askingTitle = "Allow the camera";
        [TextArea(2, 4)] [SerializeField] private string askingMessage = "Try-On uses the front camera to show the colour on your own face. Nothing is recorded or saved.";

        [Header("Wording: the camera is being switched on")]
        [SerializeField] private string startingTitle = "Starting the camera";
        [TextArea(2, 4)] [SerializeField] private string startingMessage = "One moment...";

        [Header("Wording: the user said no")]
        [SerializeField] private string refusedTitle = "The camera is off";
        [TextArea(2, 4)] [SerializeField] private string refusedMessage = "Allow the camera to see the colour on your own face. Nothing is recorded or saved.";
        [SerializeField] private string askAgainLabel = "Allow camera";
        [Tooltip("Shown instead when Android no longer shows its question (the user said no twice)")]
        [TextArea(2, 4)] [SerializeField] private string settingsMessage = "The camera is switched off for GlowUp in your phone's settings. Open them, tap Permissions, and allow the camera.";
        [SerializeField] private string settingsLabel = "Open phone settings";

        [Header("Wording: the camera did not start")]
        [SerializeField] private string problemTitle = "The camera did not start";
        [SerializeField] private string tryAgainLabel = "Try again";

        [Header("Behaviour")]
        [Tooltip("Seconds the card takes to fade away when the picture is there")]
        [SerializeField] private float fadeSeconds = 0.25f;

        /// <summary>What the card says now.</summary>
        public State Current { get; private set; } = State.Hidden;
        /// <summary>True while the card offers "Open phone settings" instead of "Allow camera".</summary>
        public bool OffersSettings { get; private set; }
        /// <summary>The button of the card was pressed (ask again, open the settings, or try again).</summary>
        public event Action ActionPressed;

        bool wired, leaving;

        void Wire()
        {
            if (wired) return;
            wired = true;
            if (actionButton != null) actionButton.onClick.AddListener(() => ActionPressed?.Invoke());
        }

        /// <summary>
        /// Shows the card. `message` replaces the standard wording of Problem (the camera's own reason).
        /// `settingsInstead` (Refused only) = Android no longer asks, so the button opens the phone's settings.
        /// </summary>
        public void Show(State state, string message = null, bool settingsInstead = false)
        {
            if (state == State.Hidden) { Hide(); return; }
            Wire();
            Current = state; leaving = false;
            OffersSettings = state == State.Refused && settingsInstead;
            gameObject.SetActive(true);
            if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true; }

            string title, text, label = null;
            switch (state)
            {
                case State.Asking: title = askingTitle; text = askingMessage; break;
                case State.Starting: title = startingTitle; text = startingMessage; break;
                case State.Refused: title = refusedTitle; text = OffersSettings ? settingsMessage : refusedMessage; label = OffersSettings ? settingsLabel : askAgainLabel; break;
                default: title = problemTitle; text = string.IsNullOrWhiteSpace(message) ? "" : message; label = tryAgainLabel; break;
            }
            if (titleText != null) titleText.text = title;
            if (messageText != null) messageText.text = text;
            if (actionButton != null) actionButton.gameObject.SetActive(label != null);
            if (actionLabel != null && label != null) actionLabel.text = label;
        }

        /// <summary>The camera picture is there: the card fades away.</summary>
        public void Hide()
        {
            if (Current == State.Hidden) { if (!leaving && gameObject.activeSelf) gameObject.SetActive(false); return; }
            Current = State.Hidden; OffersSettings = false;
            if (!gameObject.activeInHierarchy || group == null || fadeSeconds <= 0.01f) { leaving = false; gameObject.SetActive(false); return; }
            leaving = true;
            group.blocksRaycasts = false; group.interactable = false;       // the buttons on the camera picture can be used at once
        }

        void Update()
        {
            if (!leaving || group == null) return;
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (group.alpha > 0.001f) return;
            leaving = false;
            gameObject.SetActive(false);
        }
    }
}
