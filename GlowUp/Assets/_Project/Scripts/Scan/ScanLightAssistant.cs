using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The flashlight of the scan screen: its lightning button, and the help in a dark place.
    ///
    ///  - The button switches the light on and off at any time and always shows the REAL state (bright = on). On a phone
    ///    without a flashlight the button is hidden.
    ///  - When the camera picture stays dark for a moment, the light is switched on by itself (or the user is asked
    ///    first: "Switch On By Itself"). Not when "Flashlight in the dark" is off on the Settings page.
    ///  - THE USER'S OWN CHOICE WINS: once the button was used (on or off), the app does not switch the light by itself
    ///    again in this visit of the scan screen. A scan that is done again keeps the choice: a light the user switched on
    ///    comes back on when the camera does.
    ///  - The light goes off when the scan screen is left and when the app goes to the background.
    ///  - When the phone refuses (it is hot, or another app has the camera), the user is told in one short sentence, and
    ///    the screen is made brighter instead.
    /// </summary>
    public class ScanLightAssistant : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("The question on the camera view (hidden until it is dark)")]
        [SerializeField] private GameObject prompt;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;
        [Tooltip("The lightning button on the camera view (hidden on a phone without a flashlight)")]
        [SerializeField] private Button flashButton;
        [Tooltip("Its icon: clear when the flashlight is on, faint when it is off")]
        [SerializeField] private Graphic flashIcon;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenOff = 0.45f;
        [Tooltip("The round background of the button: it takes this colour while the flashlight is on. Optional.")]
        [SerializeField] private Graphic flashBackground;
        [SerializeField] private Color backgroundWhenOn = new Color32(226, 184, 96, 235);
        [SerializeField] private Color backgroundWhenOff = new Color(0f, 0f, 0f, 0.5f);

        [Header("When is it dark?")]
        [Tooltip("The picture counts as dark below this brightness (0 = black, 1 = white)")]
        [Range(0.02f, 0.5f)] [SerializeField] private float darkBelow = 0.16f;
        [Tooltip("Seconds it must stay dark before the light is switched on (or the question appears)")]
        [SerializeField] private float darkSeconds = 1.5f;
        [Tooltip("On = the app helps once each time the scanner is opened")]
        [SerializeField] private bool askOnlyOnce = true;
        [Tooltip("On = in the dark the flashlight is switched on at once, without asking. Off = the question is asked first.")]
        [SerializeField] private bool switchOnByItself = true;

        [Header("Wording")]
        [SerializeField] private string question = "It is dark here. Turn on the flashlight?";
        [Tooltip("Shown for a moment when the phone refuses to switch the flashlight")]
        [SerializeField] private string cannotMessage = "The flashlight is not available right now: the phone may be hot, or another app is using the camera. The screen was made brighter instead.";
        [Tooltip("Seconds the 'cannot' message stays")]
        [SerializeField] private float messageSeconds = 4.5f;
        [Tooltip("A light the user switched on is put back this many seconds after the camera came back on")]
        [SerializeField] private float putBackAfterSeconds = 0.5f;

        float darkFor, playingFor, messageFor = -1f;
        bool asked, putBackTried, painted, paintedOn;
        int choice;         // what the USER chose in this visit: 0 = nothing yet, 1 = on, -1 = off. A choice wins over the automatic light.

        void Awake()
        {
            // a scan that is done again keeps the user's choice; a new visit of the scan screen starts without one
            choice = ScanSession.KeepLightChoice ? ScanSession.LightChoice : 0;
            ScanSession.KeepLightChoice = false; ScanSession.LightChoice = choice;

            if (yesButton != null) yesButton.onClick.AddListener(() => { HidePrompt(); Choose(true); });
            if (noButton != null) noButton.onClick.AddListener(() => { HidePrompt(); Choose(false); });
            if (flashButton != null) flashButton.onClick.AddListener(() => Choose(!Flashlight.IsOn));
            if (prompt != null) prompt.SetActive(false);
            if (flashButton != null) flashButton.gameObject.SetActive(Flashlight.Available);
            PaintIcon();
        }

        void OnEnable() { if (cameraFeed != null) cameraFeed.Started += OnCameraStarted; }

        void OnDisable()
        {
            if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted;
            Flashlight.TrySet(false, out _);            // leaving the scan screen: the light goes off
            PaintIcon();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            Flashlight.TrySet(false, out _);            // the app goes to the background: the light goes off
            putBackTried = false; playingFor = 0f;      // back in the app, a light the user chose comes back with the camera
            PaintIcon();
        }

        // Android gives the light to the camera when the app's camera opens: a light that was on before went out
        void OnCameraStarted(bool ok, string message) { if (ok) { Flashlight.CameraStarted(); playingFor = 0f; putBackTried = false; } }

        /// <summary>The user used the button (or answered the question): that choice holds for the rest of this visit.</summary>
        void Choose(bool on)
        {
            choice = on ? 1 : -1;
            ScanSession.LightChoice = choice;
            putBackTried = true;                        // the choice is carried out right now
            Switch(on);
        }

        void Update()
        {
            if (!painted || paintedOn != Flashlight.IsOn) PaintIcon();          // always the real state
            if (messageFor >= 0f)
            {
                messageFor -= Time.unscaledDeltaTime;
                if (messageFor < 0f) HidePrompt();
                return;
            }
            bool playing = cameraFeed != null && cameraFeed.IsPlaying;
            if (!playing) { playingFor = 0f; putBackTried = false; darkFor = 0f; return; }
            playingFor += Time.unscaledDeltaTime;

            // the user's own "on" is put back when the camera is back (the scan was paused, started over, or the app was away)
            if (choice == 1 && !Flashlight.IsOn && !putBackTried && playingFor >= putBackAfterSeconds) { putBackTried = true; Switch(true); return; }

            // the automatic help in the dark: never against the user's own choice
            if (choice != 0 || Flashlight.IsOn || !Flashlight.Available || !UiPreferences.AutoFlashlight || (asked && askOnlyOnce)) { darkFor = 0f; return; }
            darkFor = cameraFeed.MeasureBrightness() < darkBelow ? darkFor + Time.unscaledDeltaTime : 0f;
            if (darkFor < darkSeconds || prompt == null || prompt.activeSelf) return;
            asked = true; darkFor = 0f;
            if (switchOnByItself) { Switch(true); return; }              // the lightning button switches it off again
            if (promptText != null) promptText.text = question;
            SetAnswerButtons(true);
            prompt.SetActive(true);
        }

        void Switch(bool on)
        {
            bool done = Flashlight.TrySet(on, out _);
            PaintIcon();
            if (done || !on) return;                    // switching off never needs a message
            // the phone refused: say so in one sentence, and help with a bright screen instead
            UiPreferences.ScanBrightScreen = true;
            if (prompt == null) return;
            if (promptText != null) promptText.text = cannotMessage;
            SetAnswerButtons(false);
            prompt.SetActive(true);
            messageFor = messageSeconds;
        }

        void HidePrompt() { messageFor = -1f; if (prompt != null) prompt.SetActive(false); }

        void SetAnswerButtons(bool show)
        {
            if (yesButton != null) yesButton.gameObject.SetActive(show);
            if (noButton != null) noButton.gameObject.SetActive(show);
        }

        void PaintIcon()
        {
            painted = true; paintedOn = Flashlight.IsOn;
            if (flashIcon != null) { Color c = flashIcon.color; c.a = paintedOn ? 1f : iconAlphaWhenOff; flashIcon.color = c; }
            if (flashBackground != null) flashBackground.color = paintedOn ? backgroundWhenOn : backgroundWhenOff;
        }
    }
}
