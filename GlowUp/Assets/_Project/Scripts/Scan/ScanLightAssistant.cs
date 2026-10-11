using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// Helps the scanner in a dark place. When the camera picture stays dark for a moment, a small
    /// question appears on the camera view: "It is dark here. Turn on the flashlight?" The flashlight
    /// is only switched on after the user says yes. The lightning button on the camera view switches
    /// it on and off at any time. The question is not asked when "Flashlight in the dark" is off on
    /// the Settings page.
    ///
    /// Some phones do not allow the flashlight while the camera picture is shown. The user is told,
    /// and the screen is made brighter instead.
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
        [Tooltip("The lightning button on the camera view")]
        [SerializeField] private Button flashButton;
        [Tooltip("Its icon: clear when the flashlight is on, faint when it is off")]
        [SerializeField] private Graphic flashIcon;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenOff = 0.45f;

        [Header("When is it dark?")]
        [Tooltip("The picture counts as dark below this brightness (0 = black, 1 = white)")]
        [Range(0.02f, 0.5f)] [SerializeField] private float darkBelow = 0.16f;
        [Tooltip("Seconds it must stay dark before the question appears")]
        [SerializeField] private float darkSeconds = 1.5f;
        [Tooltip("On = the question is asked once each time the scanner is opened")]
        [SerializeField] private bool askOnlyOnce = true;
        [Tooltip("On = in the dark the flashlight is switched on at once, without asking. Off = the question is asked first.")]
        [SerializeField] private bool switchOnByItself = true;

        [Header("Wording")]
        [SerializeField] private string question = "It is dark here. Turn on the flashlight?";
        [SerializeField] private string cannotMessage = "This phone cannot use the flashlight while the camera is on. The screen was made brighter instead.";
        [Tooltip("Seconds the 'cannot' message stays")]
        [SerializeField] private float messageSeconds = 4.5f;

        float darkFor, messageFor = -1f;
        bool asked;

        void Awake()
        {
            if (yesButton != null) yesButton.onClick.AddListener(() => { HidePrompt(); Switch(true); });
            if (noButton != null) noButton.onClick.AddListener(HidePrompt);
            if (flashButton != null) flashButton.onClick.AddListener(() => Switch(!Flashlight.IsOn));
            if (prompt != null) prompt.SetActive(false);
            PaintIcon();
        }

        void OnDisable() { Flashlight.TrySet(false, out _); PaintIcon(); }
        void OnApplicationPause(bool paused) { if (paused) { Flashlight.TrySet(false, out _); PaintIcon(); } }

        void Update()
        {
            if (messageFor >= 0f)
            {
                messageFor -= Time.unscaledDeltaTime;
                if (messageFor < 0f) HidePrompt();
                return;
            }
            if (cameraFeed == null || !cameraFeed.IsPlaying || Flashlight.IsOn || !UiPreferences.AutoFlashlight || (asked && askOnlyOnce)) { darkFor = 0f; return; }
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
            if (Flashlight.TrySet(on, out _)) { PaintIcon(); return; }
            PaintIcon();
            // the phone refused: say so, and help with a bright screen instead
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
            if (flashIcon == null) return;
            Color c = flashIcon.color; c.a = Flashlight.IsOn ? 1f : iconAlphaWhenOff; flashIcon.color = c;
        }
    }
}
