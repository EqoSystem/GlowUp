using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.Common
{
    /// <summary>
    /// Wave a hand past the selfie camera to change screen ("air wave"): a wave to the RIGHT opens the next
    /// main screen of the row (Recent Scans, Dashboard, AR Try-On), a wave to the LEFT the one before, and
    /// a wave UP goes back to the screen the user came from (the back button does the same). It replaces the old
    /// "shake to change screen". It only works when the user has switched it on (Settings page) and only on
    /// the screens listed below.
    ///
    /// How it sees a wave: the selfie camera is opened with a very small picture. Each new picture is
    /// compared with the one before; the part that changed is where something moved. When that moving part
    /// travels sideways across the picture quickly, in one direction, it is a wave. If nearly the whole
    /// picture changes (the phone itself was moved, the light changed) it is ignored.
    ///
    /// The camera picture is only looked at here, on the phone: nothing is saved or sent anywhere.
    /// The scanner and AR Try-On use the camera themselves, so the air wave rests on those screens (the
    /// camera is handed over before they open). The phone shows its "camera in use" sign while this is on.
    ///
    /// One of these lives on the UiServices object and stays alive across scenes.
    /// </summary>
    public class AirGestureNavigator : MonoBehaviour
    {
        public static AirGestureNavigator Instance { get; private set; }

        [Header("Where it works")]
        [Tooltip("A wave is only looked for on these scenes (screens that do not use the camera themselves)")]
        [SerializeField] private string[] activeScenes = { SceneNames.Dashboard, SceneNames.RecentScans, SceneNames.Settings };
        [Tooltip("\"Next\" walks through this list from left to right")]
        [SerializeField] private string[] nextOrder = { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.ArTryOn };
        [Tooltip("Where a wave up goes when there is no earlier screen to return to")]
        [SerializeField] private string homeScene = SceneNames.Dashboard;

        [Header("Camera (the selfie camera, with a very small picture)")]
        [SerializeField] private int requestedWidth = 160;
        [SerializeField] private int requestedHeight = 120;
        [SerializeField] private int requestedFps = 15;
        [Tooltip("The picture is looked at as a grid of this many columns and rows")]
        [SerializeField] private int gridColumns = 24;
        [SerializeField] private int gridRows = 18;
        [Tooltip("Seconds after a screen opens before the camera is started")]
        [SerializeField] private float startDelaySeconds = 0.6f;

        [Header("What counts as a wave")]
        [Tooltip("How much a spot of the picture must change to count as moving (0 = nothing, 1 = black to white). Higher = less sensitive.")]
        [Range(0.02f, 0.4f)] [SerializeField] private float changeNeeded = 0.09f;
        [Tooltip("At least this share of the picture must be moving")]
        [Range(0.01f, 0.3f)] [SerializeField] private float leastMoving = 0.05f;
        [Tooltip("More than this share moving = the phone itself moved or the light changed: ignored")]
        [Range(0.3f, 1f)] [SerializeField] private float mostMoving = 0.6f;
        [Tooltip("The moving part must travel this share of the picture's width")]
        [Range(0.1f, 0.9f)] [SerializeField] private float travelNeeded = 0.3f;
        [Tooltip("A wave must be this many times more sideways than up-and-down")]
        [SerializeField] private float sidewaysRatio = 1.4f;
        [Tooltip("A wave takes at least this long (seconds) ...")]
        [SerializeField] private float shortestWave = 0.08f;
        [Tooltip("... and at most this long")]
        [SerializeField] private float longestWave = 0.75f;
        [Tooltip("This share of the steps must go the same way as the whole wave")]
        [Range(0.5f, 1f)] [SerializeField] private float steadyShare = 0.65f;
        [Tooltip("Seconds without movement that end a wave")]
        [SerializeField] private float gapSeconds = 0.18f;
        [Tooltip("Seconds before another wave is accepted")]
        [SerializeField] private float pauseAfterWave = 1.3f;
        [Tooltip("How much the user's Sensitivity slider (Settings page) changes the numbers above: at the lowest setting they are multiplied by this, at the highest divided by it")]
        [Range(1f, 4f)] [SerializeField] private float sensitivityRange = 2.2f;
        [Tooltip("Swap the two directions (if a wave to the right goes back on some phone)")]
        [SerializeField] private bool swapDirections = false;

        [Header("Feedback")]
        [Tooltip("Play the tap sound when a wave is recognised")]
        [SerializeField] private bool playSound = true;

        WebCamTexture cam;
        bool frontCamera, noCamera, permissionAsked;
        Color32[] pixels;
        float[] now, before;
        bool hasBefore;
        float startAfter, quietUntil, lastMovingTime;
        readonly List<Vector3> track = new List<Vector3>();        // x, y = where the movement was (0 to 1 of the picture), z = when

        /// <summary>True while the selfie camera is open for the air wave.</summary>
        public bool IsWatching => cam != null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; UiPreferences.Changed += OnPreferencesChanged; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; UiPreferences.Changed -= OnPreferencesChanged; StopCamera(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnApplicationPause(bool paused) { if (paused) StopCamera(); }

        // a screen that uses the camera itself must find it free: let go before that screen starts
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            noCamera = false;
            if (!IsActiveScene()) StopCamera();
            startAfter = Time.unscaledTime + startDelaySeconds;
        }

        void OnPreferencesChanged() { if (!UiPreferences.AirGestures) { StopCamera(); permissionAsked = false; } }

        void Update()
        {
            if (Instance != this) return;
            bool wanted = UiPreferences.AirGestures && Application.isFocused && !ScreenTransition.IsBusy && !noCamera && IsActiveScene() && CameraAllowed();
            if (!wanted) { if (cam != null) StopCamera(); startAfter = Time.unscaledTime + startDelaySeconds; return; }
            if (cam == null) { if (Time.unscaledTime >= startAfter) StartCamera(); return; }
            if (!cam.isPlaying || cam.width <= 32 || !cam.didUpdateThisFrame) return;
            Look();
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
            // refused: the switch on the Settings page goes off again, so it never looks on while nothing happens
            callbacks.PermissionDenied += _ => { UiPreferences.AirGestures = false; };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { UiPreferences.AirGestures = false; };
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            return false;
#else
            return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
        }

        void StartCamera()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            int chosen = -1;
            if (devices != null)
                for (int i = 0; i < devices.Length; i++)
                    if (devices[i].isFrontFacing && (chosen < 0 || devices[i].kind == WebCamKind.WideAngle)) { chosen = i; if (devices[i].kind == WebCamKind.WideAngle) break; }
            if (chosen < 0 && devices != null && devices.Length > 0 && Application.isEditor) chosen = 0;      // a computer's webcam, for trying it in the Editor
            if (chosen < 0) { noCamera = true; return; }                                                    // no selfie camera: nothing to watch on this screen
            frontCamera = devices[chosen].isFrontFacing || Application.isEditor;
            cam = new WebCamTexture(devices[chosen].name, Mathf.Max(64, requestedWidth), Mathf.Max(48, requestedHeight), Mathf.Clamp(requestedFps, 5, 30));
            cam.Play();
            hasBefore = false; track.Clear();
            quietUntil = Time.unscaledTime + 1f;            // the camera sets its exposure first: that is not a wave
        }

        void StopCamera()
        {
            if (cam == null) return;
            if (cam.isPlaying) cam.Stop();
            Destroy(cam);
            cam = null; hasBefore = false; track.Clear();
        }

        // ---------------------------------------------------------------------------------------
        //  Looking at one picture
        // ---------------------------------------------------------------------------------------
        /// <summary>The user's Sensitivity as a factor for the thresholds: above 1 = harder to trigger, below 1 = easier.</summary>
        float Strictness => Mathf.Pow(Mathf.Max(1f, sensitivityRange), 1f - 2f * UiPreferences.AirSensitivity);

        void Look()
        {
            int w = cam.width, h = cam.height;
            if (pixels == null || pixels.Length != w * h) pixels = new Color32[w * h];
            cam.GetPixels32(pixels);

            int cols = Mathf.Clamp(gridColumns, 8, 48), rows = Mathf.Clamp(gridRows, 6, 36);
            if (now == null || now.Length != cols * rows) { now = new float[cols * rows]; before = new float[cols * rows]; hasBefore = false; }

            // the brightness of each cell of the grid (a few points of each cell are enough)
            for (int r = 0; r < rows; r++)
            {
                int y0 = r * h / rows, y1 = (r + 1) * h / rows, stepY = Mathf.Max(1, (y1 - y0) / 3);
                for (int c = 0; c < cols; c++)
                {
                    int x0 = c * w / cols, x1 = (c + 1) * w / cols, stepX = Mathf.Max(1, (x1 - x0) / 3);
                    int sum = 0, count = 0;
                    for (int y = y0; y < y1; y += stepY)
                    {
                        int line = y * w;
                        for (int x = x0; x < x1; x += stepX) { Color32 p = pixels[line + x]; sum += p.r * 2 + p.g * 5 + p.b; count++; }
                    }
                    now[r * cols + c] = count > 0 ? sum / (count * 8f * 255f) : 0f;
                }
            }
            if (!hasBefore) { Swap(); hasBefore = true; return; }

            // where did the picture change?
            float strict = Mathf.Sqrt(Strictness);
            float sumX = 0f, sumY = 0f; int moving = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (Mathf.Abs(now[i] - before[i]) <= changeNeeded * strict) continue;
                    moving++; sumX += (c + 0.5f) / cols; sumY += (r + 0.5f) / rows;
                }
            Swap();

            float time = Time.unscaledTime;
            if (time < quietUntil) { track.Clear(); return; }
            float share = moving / (float)(cols * rows);
            if (share > mostMoving) { track.Clear(); return; }                 // everything changed at once: the phone moved, or the light did
            if (share >= leastMoving * strict)
            {
                track.Add(new Vector3(sumX / moving, sumY / moving, time));
                lastMovingTime = time;
                if (time - track[0].z > longestWave) Judge();                 // moving for too long to be one wave: judge it and start afresh
            }
            else if (track.Count > 0 && time - lastMovingTime > gapSeconds) Judge();
        }

        void Swap() { float[] keep = before; before = now; now = keep; }

        /// <summary>Was what moved a wave? If so, change screen.</summary>
        void Judge()
        {
            if (track.Count >= 3)
            {
                Vector3 first = track[0], last = track[track.Count - 1];
                float seconds = last.z - first.z;
                Vector2 moved = ToScreen(new Vector2(last.x - first.x, last.y - first.y));
                int steps = 0, same = 0;
                for (int i = 1; i < track.Count; i++)
                {
                    float step = ToScreen(new Vector2(track[i].x - track[i - 1].x, track[i].y - track[i - 1].y)).x;
                    if (Mathf.Abs(step) < 0.004f) continue;
                    steps++;
                    if ((step > 0f) == (moved.x > 0f)) same++;
                }
                bool oneWay = steps == 0 || same >= steps * steadyShare;
                bool inTime = seconds >= shortestWave && seconds <= longestWave;
                float travel = Mathf.Clamp(travelNeeded * Strictness, 0.08f, 0.85f);
                if (inTime && oneWay && Mathf.Abs(moved.x) >= travel && Mathf.Abs(moved.x) >= Mathf.Abs(moved.y) * sidewaysRatio)
                    Wave(moved.x > 0f);
                else if (inTime && moved.y >= travel && moved.y >= Mathf.Abs(moved.x) * sidewaysRatio)
                    WaveUp();
            }
            track.Clear();
        }

        /// <summary>
        /// A movement in the camera's own picture, turned the way the user sees it: the camera's picture lies on
        /// its side on most phones, and the selfie camera is shown like a mirror (a hand moving to the user's
        /// right moves to the right).
        /// </summary>
        Vector2 ToScreen(Vector2 v)
        {
            if (cam == null) return v;
            if (cam.videoVerticallyMirrored) v.y = -v.y;
            float angle = -cam.videoRotationAngle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var turned = new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
            if (frontCamera) turned.x = -turned.x;
            return turned;
        }

        void Wave(bool right)
        {
            quietUntil = Time.unscaledTime + pauseAfterWave;
            if (ScreenTransition.IsBusy || !IsActiveScene()) return;
            if (swapDirections) right = !right;
            if (playSound && UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.PlayTap();
            if (right) GoNext(); else GoBack();
        }

        /// <summary>A wave up: back to the screen the user came from (or home).</summary>
        void WaveUp()
        {
            quietUntil = Time.unscaledTime + pauseAfterWave;
            if (ScreenTransition.IsBusy || !IsActiveScene()) return;
            if (playSound && UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.PlayTap();
            StopCamera();
            if (SceneNavigator.Back()) return;
            if (SceneManager.GetActiveScene().name != homeScene && !string.IsNullOrEmpty(homeScene)) SceneNavigator.Go(homeScene, -1);
        }

        void GoNext() { Step(1); }
        void GoBack() { Step(-1); }

        /// <summary>The neighbour in the row: 1 = the one to the right, -1 = the one to the left.</summary>
        void Step(int direction)
        {
            int index = System.Array.IndexOf(nextOrder, SceneManager.GetActiveScene().name);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= nextOrder.Length) return;
            StopCamera();
            SceneNavigator.Go(nextOrder[target], direction);
        }
    }
}
