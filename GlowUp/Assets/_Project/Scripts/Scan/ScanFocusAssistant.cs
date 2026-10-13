using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The FOCUS LOCK of the scan screen: its padlock button, and the lock that comes by itself.
    ///
    /// A phone camera left alone looks for focus all the time; on small print held close it often goes sharp and soft
    /// again and again. Locked, the camera focuses ONCE and then keeps the lens where it is.
    ///
    ///  - The padlock button locks the focus on the text in view (or the middle of the scan frame) and releases it
    ///    again. It always shows the REAL state: closed and bright = locked. It is hidden while the camera is off and on
    ///    a camera that cannot hold its focus (a fixed-focus camera, a computer's webcam).
    ///  - A tap on the camera picture locks the focus on the spot that was tapped.
    ///  - BY ITSELF: while the scan button is held the app watches the pictures. When the camera keeps losing the focus
    ///    it is locked without asking, and a short line says so. A locked focus is renewed only when the picture has
    ///    gone soft and stays soft (the product was moved nearer or further): FocusKeeper holds this rule.
    ///  - THE USER'S OWN CHOICE WINS: after the padlock was used to RELEASE, the app does not lock by itself again in
    ///    this visit of the scan screen. A lock the user made comes back when the camera is switched on again.
    ///
    /// Every number and wording is an Inspector field. SmartScanFlowController tells it about every look (Looked).
    /// </summary>
    public class ScanFocusAssistant : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("The padlock button on the camera view (hidden while the camera is off, and on a camera that cannot hold its focus)")]
        [SerializeField] private Button lockButton;
        [SerializeField] private Image lockIcon;
        [Tooltip("The icon while the focus is locked (a closed padlock)")]
        [SerializeField] private Sprite lockedSprite;
        [Tooltip("The icon while the camera focuses by itself (an open padlock)")]
        [SerializeField] private Sprite freeSprite;
        [Range(0f, 1f)] [SerializeField] private float iconAlphaWhenFree = 0.6f;
        [Tooltip("The round background of the button: it takes this colour while the focus is locked. Optional.")]
        [SerializeField] private Graphic lockBackground;
        [SerializeField] private Color backgroundWhenLocked = new Color32(226, 184, 96, 235);
        [SerializeField] private Color backgroundWhenFree = new Color(0f, 0f, 0f, 0.5f);
        [Tooltip("The short line beside the button that says what happened (hidden until then)")]
        [SerializeField] private CanvasGroup note;
        [SerializeField] private TextMeshProUGUI noteText;

        [Header("Locking by itself")]
        [Tooltip("On = the focus is locked without asking when the camera keeps losing it. Off = only the padlock button and a tap lock it.")]
        [SerializeField] private bool lockByItself = true;
        [Tooltip("The camera lost the focus this many times while the phone was held still = it is hunting: lock")]
        [Range(1, 6)] [SerializeField] private int focusLossesToLock = 2;
        [Tooltip("...or the picture stayed soft for this many looks in a row: lock")]
        [Range(2, 20)] [SerializeField] private int softLooksToLock = 5;
        [Tooltip("How many looks are remembered for counting the losses")]
        [Range(4, 40)] [SerializeField] private int looksRemembered = 12;
        [Tooltip("Locked: the picture was soft for this many looks in a row = the distance changed: focus once more and hold again")]
        [Range(2, 20)] [SerializeField] private int softLooksToRefocus = 4;
        [Tooltip("Locked, after the phone or the product was moved: this many soft looks are enough")]
        [Range(1, 10)] [SerializeField] private int softLooksAfterAMove = 2;
        [Tooltip("Seconds the lens gets to settle after it was told to focus; the pictures of that time are not judged")]
        [Range(0.3f, 4f)] [SerializeField] private float settleSeconds = 1.2f;
        [Tooltip("A lock the user made is put back this many seconds after the camera came back on")]
        [SerializeField] private float putBackAfterSeconds = 0.6f;

        [Header("Wording")]
        [SerializeField] private string lockedNote = "Focus locked. Tap the padlock to release it.";
        [SerializeField] private string lockedByItselfNote = "Focus locked: the camera kept losing it";
        [SerializeField] private string releasedNote = "The camera focuses by itself again";
        [Tooltip("Seconds the line stays")]
        [SerializeField] private float noteSeconds = 2.6f;
        [SerializeField] private float noteFadeSeconds = 0.35f;

        /// <summary>Where the camera should focus when it is locked: the text the tracer follows, or the scan frame. Null = the middle of the picture.</summary>
        public RectTransform FocusOn { get; set; }
        /// <summary>True while the focus is held here: the scan screen then leaves all focusing to this helper.</summary>
        public bool LooksAfterFocus => keeper.Holding;

        readonly FocusKeeper keeper = new FocusKeeper();
        int choice;                 // what the USER chose in this visit: 0 = nothing yet, 1 = locked, -1 = free
        bool byRule, painted, paintedLocked, paintedShown, putBackTried;
        float noteFor = -1f, playingFor;

        void Awake()
        {
            if (lockButton != null) lockButton.onClick.AddListener(OnButton);
            if (cameraFeed != null) cameraFeed.FocusLockChanged += OnLockChanged;
            if (note != null) { note.alpha = 0f; note.gameObject.SetActive(false); }
            Paint();
        }

        void OnDestroy() { if (cameraFeed != null) cameraFeed.FocusLockChanged -= OnLockChanged; }

        void OnButton()
        {
            if (cameraFeed == null) return;
            if (cameraFeed.FocusLocked) { choice = -1; cameraFeed.ReleaseFocus(); }
            else { choice = 1; cameraFeed.LockFocus(FocusOn); }
        }

        // the camera's lock went on or off: by the button, a tap on the picture, the rule, or because the camera was closed
        void OnLockChanged(bool locked, bool byTap)
        {
            if (locked)
            {
                if (byTap) choice = 1;
                if (!byRule) keeper.HeldFromOutside(Time.unscaledTime);
                Say(byRule ? lockedByItselfNote : lockedNote);
            }
            else
            {
                keeper.Released();
                if (cameraFeed != null && cameraFeed.IsPlaying) Say(releasedNote);       // not when the camera itself went off
            }
            Paint();
        }

        /// <summary>
        /// One look of the scan (several a second while the button is held). `moving` = the picture changed a lot since the
        /// look before; `judged` = there are edges to judge the focus by; `soft` = they are out of focus.
        /// </summary>
        public void Looked(bool moving, bool judged, bool soft)
        {
            if (cameraFeed == null || !cameraFeed.CanLockFocus) return;
            keeper.Automatic = lockByItself && choice >= 0;
            keeper.LossesToLock = focusLossesToLock; keeper.SoftLooksToLock = softLooksToLock; keeper.LooksRemembered = looksRemembered;
            keeper.SoftLooksToRefocus = softLooksToRefocus; keeper.SoftLooksAfterAMove = softLooksAfterAMove; keeper.SettleSeconds = settleSeconds;
            if (!keeper.Look(moving, judged, soft, Time.unscaledTime)) return;
            byRule = true;
            try { if (!cameraFeed.LockFocus(FocusOn)) keeper.Released(); }       // focus once (again) and hold
            finally { byRule = false; }
        }

        void Update()
        {
            bool can = cameraFeed != null && cameraFeed.CanLockFocus;
            playingFor = can ? playingFor + Time.unscaledDeltaTime : 0f;
            if (!can) putBackTried = false;
            // a lock the user made comes back with the camera
            else if (choice > 0 && !cameraFeed.FocusLocked && !putBackTried && playingFor >= putBackAfterSeconds)
            {
                putBackTried = true;
                cameraFeed.LockFocus(FocusOn);
            }
            Paint();

            if (noteFor < 0f || note == null) return;
            noteFor += Time.unscaledDeltaTime;
            float left = noteSeconds - noteFor;
            note.alpha = left <= 0f ? 0f : Mathf.Clamp01(left / Mathf.Max(0.01f, noteFadeSeconds));
            if (left <= 0f) { noteFor = -1f; note.gameObject.SetActive(false); }
        }

        void Say(string message)
        {
            if (note == null || noteText == null || string.IsNullOrEmpty(message)) return;
            noteText.text = message;
            note.gameObject.SetActive(true);
            note.alpha = 1f;
            noteFor = 0f;
        }

        // the button shows the camera's real state, and is there only when the camera can hold its focus
        void Paint()
        {
            bool shown = cameraFeed != null && cameraFeed.CanLockFocus;
            bool locked = shown && cameraFeed.FocusLocked;
            if (painted && shown == paintedShown && locked == paintedLocked) return;
            painted = true; paintedShown = shown; paintedLocked = locked;
            if (lockButton != null) lockButton.gameObject.SetActive(shown);
            if (lockIcon != null)
            {
                Sprite sprite = locked ? lockedSprite : freeSprite;
                if (sprite != null) lockIcon.sprite = sprite;
                Color c = lockIcon.color; c.a = locked ? 1f : iconAlphaWhenFree; lockIcon.color = c;
            }
            if (lockBackground != null) lockBackground.color = locked ? backgroundWhenLocked : backgroundWhenFree;
            if (!shown && note != null && noteFor >= 0f) { noteFor = -1f; note.alpha = 0f; note.gameObject.SetActive(false); }
        }
    }
}
