using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.Common
{
    /// <summary>
    /// Finger signs: hold 1 to 5 fingers up to the selfie camera to open a screen.
    ///   As delivered: 1 Home (dashboard), 2 AI Smart Scan, 3 Recent Scans, 4 AR Try-On, 5 Settings.
    ///   The user can give each number another screen on the Settings page (Sounds and Motion > Finger signs).
    /// It took the place of "wave to change screen". It only works when the user has switched it on, and only on the
    /// screens listed below (the scanner and AR Try-On use the camera themselves).
    ///
    /// How it sees a sign: the selfie camera is opened with a small picture. A few times a second one picture is given
    /// to the phone's hand finder (HandLandmarkReader), which answers with the 21 points of the hand; FingerCounter
    /// counts the raised fingers. When the SAME number is seen for as long as the user chose (about a second), the
    /// screen for that number opens. A small sign at the top of the screen shows what is seen and fills while it is held.
    ///
    /// The camera picture is only looked at here, on the phone: nothing is saved or sent anywhere. The phone shows its
    /// "camera in use" sign while this is on. One of these lives on the UiServices object and stays alive across scenes.
    /// </summary>
    public class FingerSignNavigator : MonoBehaviour
    {
        public static FingerSignNavigator Instance { get; private set; }

        /// <summary>Why finger signs cannot work on this phone right now ("" = they can). The Settings page shows it.</summary>
        public static string Problem { get; private set; } = "";

        [Header("Where it works")]
        [Tooltip("A sign is only looked for on these scenes (screens that do not use the camera themselves)")]
        [SerializeField] private string[] activeScenes = { SceneNames.Dashboard, SceneNames.RecentScans, SceneNames.Settings, SceneNames.AiResult };

        [Header("Camera (the selfie camera, with a small picture)")]
        [SerializeField] private int requestedWidth = 320;
        [SerializeField] private int requestedHeight = 240;
        [SerializeField] private int requestedFps = 15;
        [Tooltip("The picture given to the hand finder is made this small first (its longer side, in pixels). Smaller = faster.")]
        [Range(128, 640)] [SerializeField] private int pictureSize = 320;
        [Tooltip("How many pictures a second are given to the hand finder")]
        [Range(1f, 10f)] [SerializeField] private float looksPerSecond = 5f;
        [Tooltip("Seconds after a screen opens before the camera is started")]
        [SerializeField] private float startDelaySeconds = 0.6f;

        [Header("What counts as a sign")]
        [Tooltip("The sign may be lost for this long (a picture in which the hand was not found) without starting again")]
        [SerializeField] private float lostGraceSeconds = 0.45f;
        [Tooltip("Seconds before another sign is accepted after one worked")]
        [SerializeField] private float pauseAfterSign = 1.6f;

        [Header("Feedback")]
        [Tooltip("The small sign at the top of the screen that shows what is seen")]
        [SerializeField] private FingerSignIndicator indicator;
        [Tooltip("Play the tap sound when a sign opens a screen")]
        [SerializeField] private bool playSound = true;

        [Header("Wording (shown on the Settings page)")]
        [SerializeField] private string cameraRefusedText = "GlowUp is not allowed to use the camera, so finger signs were switched off. Allow the camera for GlowUp in your phone's settings, then switch finger signs on again.";
        [SerializeField] private string noSelfieCameraText = "This phone has no selfie camera for finger signs.";

        WebCamTexture cam;
        HandLandmarkReader reader;
        bool noCamera, permissionAsked, readerFailed, wasOn;
        volatile bool permissionRefused;        // set by the phone's answer (not on Unity's own thread), acted on in Update
        Color32[] pixels;
        int[] upright;
        float startAfter, nextLook, quietUntil;

        // what the hand finder said last (written by its own thread, read by Update)
        readonly object answerLock = new object();
        float[] answerPoints; bool answerWaiting; float answerAspect = 1f, sentAspect = 1f;

        int candidate;                 // the number of fingers being held (0 = none)
        float heldFor, lastSeen = -10f;

        /// <summary>True while the selfie camera is open for finger signs.</summary>
        public bool IsWatching => cam != null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable()
        {
            wasOn = UiPreferences.FingerSigns;
            SceneManager.sceneLoaded += OnSceneLoaded; SceneNavigator.Leaving += OnLeaving; UiPreferences.Changed += OnPreferencesChanged;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneNavigator.Leaving -= OnLeaving; UiPreferences.Changed -= OnPreferencesChanged;
            StopCamera();
        }

        void OnDestroy() { if (Instance == this) { Instance = null; CloseReader(); } }
        void OnApplicationPause(bool paused) { if (paused) StopCamera(); }

        // A screen that uses the camera itself (the scanner, AR Try-On) must find it free: the selfie camera is let go of
        // as soon as a screen change starts, before the next screen wakes up, and stays closed until that screen is there.
        void OnLeaving(string nextScene)
        {
            StopCamera();
            startAfter = Time.unscaledTime + 3f;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            noCamera = false;
            if (!IsActiveScene()) StopCamera();
            startAfter = Time.unscaledTime + startDelaySeconds;
            Forget();
        }

        void OnPreferencesChanged()
        {
            bool on = UiPreferences.FingerSigns;
            if (on && !wasOn) Problem = "";                 // switched on again: whatever went wrong before is tried afresh
            wasOn = on;
            if (on) return;
            StopCamera(); CloseReader();
            permissionAsked = false; readerFailed = false; noCamera = false; Problem = "";
        }

        void Update()
        {
            if (Instance != this) return;
            if (permissionRefused)
            {
                // refused: the switch on the Settings page goes off again (so it never looks on while nothing happens) and says why
                permissionRefused = false;
                UiPreferences.FingerSigns = false;
                Problem = cameraRefusedText;
                return;
            }
            bool wanted = UiPreferences.FingerSigns && Application.isFocused && !ScreenTransition.IsBusy && !noCamera && !readerFailed
                       && IsActiveScene() && CameraAllowed() && ReaderReady();
            if (!wanted) { if (cam != null) StopCamera(); startAfter = Mathf.Max(startAfter, Time.unscaledTime + startDelaySeconds); return; }
            if (cam == null) { if (Time.unscaledTime >= startAfter) StartCamera(); return; }
            if (!cam.isPlaying || cam.width <= 32) return;

            TakeAnswer();
            if (cam.didUpdateThisFrame && Time.unscaledTime >= nextLook && !reader.Busy) Look();
            Judge();
        }

        bool IsActiveScene()
        {
            string current = SceneManager.GetActiveScene().name;
            foreach (string scene in activeScenes) if (scene == current) return true;
            return false;
        }

        /// <summary>True when the app may use the camera. The first time it is not allowed yet, the user is asked once.</summary>
        bool CameraAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Camera)) return true;
            if (permissionAsked) return false;
            permissionAsked = true;
            var callbacks = new PermissionCallbacks();
            // the answer may arrive on another thread: only a note is made here, Update acts on it
            callbacks.PermissionDenied += _ => { permissionRefused = true; };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { permissionRefused = true; };
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            return false;
#else
            return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
        }

        /// <summary>
        /// The hand finder is started the first time it is needed; loading its model takes a moment, and this says false
        /// until it is ready. When it cannot start, Problem says why and nothing is tried again until the switch is
        /// turned off and on.
        /// </summary>
        bool ReaderReady()
        {
            if (reader == null)
            {
                reader = HandLandmarkReader.Begin(out string problem);
                if (reader == null) { Problem = problem; readerFailed = true; return false; }
            }
            switch (reader.State)
            {
                case HandLandmarkReader.Status.Ready:
                    return true;
                case HandLandmarkReader.Status.Failed:
                    Problem = reader.Problem; readerFailed = true;
                    CloseReader();
                    return false;
                default:
                    return false;               // still loading the model
            }
        }

        void CloseReader()
        {
            if (reader == null) return;
            reader.Close();
            reader = null;
        }

        void StartCamera()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            int chosen = -1;
            if (devices != null)
                for (int i = 0; i < devices.Length; i++)
                    if (devices[i].isFrontFacing && (chosen < 0 || devices[i].kind == WebCamKind.WideAngle)) { chosen = i; if (devices[i].kind == WebCamKind.WideAngle) break; }
            if (chosen < 0) { noCamera = true; Problem = noSelfieCameraText; return; }
            cam = new WebCamTexture(devices[chosen].name, Mathf.Max(160, requestedWidth), Mathf.Max(120, requestedHeight), Mathf.Clamp(requestedFps, 5, 30));
            cam.Play();
            Forget();
            quietUntil = Time.unscaledTime + 0.8f;            // the camera sets its exposure first
        }

        void StopCamera()
        {
            if (cam != null)
            {
                if (cam.isPlaying) cam.Stop();
                Destroy(cam);
                cam = null;
                CameraHandover.Released();                  // the phone closes the camera a moment later: AR Try-On waits for that
            }
            Forget();
        }

        void Forget()
        {
            candidate = 0; heldFor = 0f; lastSeen = -10f;
            lock (answerLock) { answerWaiting = false; answerPoints = null; }
            if (indicator != null) indicator.Hide();
        }

        // ---------------------------------------------------------------------------------------
        //  One picture for the hand finder
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// Gives the camera's picture to the hand finder: made small, set upright, and written row by row from the top
        /// (Unity keeps a camera picture from the bottom up, and on most phones lying on its side).
        /// </summary>
        void Look()
        {
            nextLook = Time.unscaledTime + 1f / Mathf.Max(1f, looksPerSecond);
            int w = cam.width, h = cam.height;
            if (pixels == null || pixels.Length != w * h) pixels = new Color32[w * h];
            cam.GetPixels32(pixels);

            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(w, h) / (float)Mathf.Max(64, pictureSize)));
            int sw = w / step, sh = h / step;                               // the small picture, still as the camera holds it
            int angle = ((cam.videoRotationAngle % 360) + 360) % 360;       // how far it must be turned clockwise to stand upright
            bool sideways = angle == 90 || angle == 270;
            int ow = sideways ? sh : sw, oh = sideways ? sw : sh;           // the upright picture
            if (ow < 16 || oh < 16) return;
            if (upright == null || upright.Length != ow * oh) upright = new int[ow * oh];
            bool flipped = cam.videoVerticallyMirrored;

            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    // (u, v) = the same spot in the camera's own picture, v counted from the top
                    int u, v;
                    switch (angle)
                    {
                        case 90: u = y; v = sh - 1 - x; break;
                        case 180: u = sw - 1 - x; v = sh - 1 - y; break;
                        case 270: u = sw - 1 - y; v = x; break;
                        default: u = x; v = y; break;
                    }
                    int row = flipped ? v : sh - 1 - v;                      // Unity's rows run from the bottom up
                    Color32 c = pixels[row * step * w + u * step];
                    upright[y * ow + x] = unchecked((int)0xFF000000) | (c.r << 16) | (c.g << 8) | c.b;
                }

            sentAspect = ow / (float)oh;
            reader.Read(upright, ow, oh, points =>
            {
                // called on the hand finder's own thread: only the answer is stored
                lock (answerLock) { answerPoints = points; answerAspect = sentAspect; answerWaiting = true; }
            });
        }

        int seenNow = -1;              // the count in the newest answer (-1 = no new answer this frame)

        void TakeAnswer()
        {
            seenNow = -1;
            float[] points; float aspect;
            lock (answerLock)
            {
                if (!answerWaiting) return;
                answerWaiting = false; points = answerPoints; aspect = answerAspect;
            }
            seenNow = points == null ? 0 : Mathf.Max(0, FingerCounter.Count(points, aspect));
        }

        // ---------------------------------------------------------------------------------------
        //  Is a sign being held?
        // ---------------------------------------------------------------------------------------
        void Judge()
        {
            float time = Time.unscaledTime;
            if (time < quietUntil) { if (indicator != null) indicator.Hide(); return; }

            // seenNow: -1 = no new answer in this frame, 0 = no hand (or a fist), 1 to 5 = that many fingers
            if (seenNow >= 1)
            {
                string target = UiPreferences.FingerSignScreen(seenNow);
                bool useful = target.Length > 0 && target != SceneManager.GetActiveScene().name;
                if (useful)
                {
                    if (seenNow != candidate) { candidate = seenNow; heldFor = 0f; }      // another number: the hold starts again
                    lastSeen = time;
                }
                else { candidate = 0; heldFor = 0f; }       // a number that opens nothing here (or the screen that is open already)
            }
            // a picture without a hand does not end the hold at once: the hand finder misses one now and then (lostGraceSeconds)

            if (candidate == 0 || time - lastSeen > lostGraceSeconds)
            {
                candidate = 0; heldFor = 0f;
                if (indicator != null) indicator.Hide();
                return;
            }

            heldFor += Time.unscaledDeltaTime;
            float needed = UiPreferences.FingerSignHoldSeconds;
            string screen = UiPreferences.FingerSignScreen(candidate);
            if (indicator != null) indicator.Show(candidate, UiPreferences.FingerSignScreenName(screen), Mathf.Clamp01(heldFor / needed));
            if (heldFor >= needed) Open(screen);
        }

        void Open(string scene)
        {
            quietUntil = Time.unscaledTime + pauseAfterSign;
            candidate = 0; heldFor = 0f;
            if (indicator != null) indicator.Confirm();
            if (ScreenTransition.IsBusy || string.IsNullOrEmpty(scene) || !IsActiveScene()) return;
            if (playSound && UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.PlayTap();
            StopCamera();                                   // the next screen may need the camera itself
            SceneNavigator.Go(scene);
        }
    }
}
