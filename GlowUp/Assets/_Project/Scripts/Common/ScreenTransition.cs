using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.Common
{
    /// <summary>
    /// The change from one screen to the next. A sheet in the page colour glides across (or fades in) with
    /// a soft shadow at its edges and a small turning sparkle in its middle; the next scene is loaded
    /// behind it, and the sheet glides on and uncovers the new screen.
    ///
    /// What keeps it smooth:
    ///  - the next scene starts loading while the sheet is still coming in, so the wait is shorter;
    ///  - when the new screen has loaded, the sheet waits the few frames the screen needs to set itself up
    ///    before it moves away, so the uncovering never stutters;
    ///  - one slow frame never makes the sheet jump: each frame moves it by a small step at most.
    /// If loading takes longer than a moment (a slow phone, a heavy screen) grey placeholder shapes with a
    /// moving shine appear on the sheet, so the user sees that something is coming.
    ///
    /// One of these lives on the UiServices object and stays alive across scenes. Every scene change that
    /// goes through SceneNavigator.Go uses it; if it is missing the scene is simply loaded at once.
    /// </summary>
    public class ScreenTransition : MonoBehaviour
    {
        public static ScreenTransition Instance { get; private set; }
        /// <summary>True while a screen change is running (other scripts must not start another one).</summary>
        public static bool IsBusy => Instance != null && Instance.busy;

        [Header("References (children of this object)")]
        [Tooltip("The canvas drawn above everything; it is switched off between changes")]
        [SerializeField] private Canvas overlayCanvas;
        [Tooltip("The sheet that covers the screen")]
        [SerializeField] private RectTransform sheet;
        [SerializeField] private CanvasGroup sheetGroup;
        [Tooltip("The placeholder shapes shown when loading is slow")]
        [SerializeField] private CanvasGroup skeletonGroup;
        [Tooltip("The small sparkle in the middle of the sheet (optional)")]
        [SerializeField] private RectTransform mark;
        [SerializeField] private CanvasGroup markGroup;

        [Header("Timing (seconds)")]
        [Tooltip("How long the sheet takes to cover the old screen")]
        [SerializeField] private float coverSeconds = 0.2f;
        [Tooltip("How long it takes to uncover the new screen")]
        [SerializeField] private float revealSeconds = 0.34f;
        [Tooltip("The placeholder shapes appear when loading has taken this long")]
        [SerializeField] private float skeletonAfterSeconds = 0.35f;
        [SerializeField] private float skeletonFadeSeconds = 0.15f;
        [Tooltip("How the sheet comes in: quick at first, then gentle")]
        [SerializeField] private AnimationCurve coverEase = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("How the sheet leaves: gentle at both ends")]
        [SerializeField] private AnimationCurve revealEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Smoothness")]
        [Tooltip("The longest step one frame may move the sheet, in seconds. A frame that took longer (the new screen starting up) does not make the sheet jump.")]
        [SerializeField] private float longestFrameStep = 0.034f;
        [Tooltip("After the new screen has loaded the sheet waits until the phone draws smoothly again, at most this many frames")]
        [SerializeField] private int settleFrames = 8;
        [Tooltip("A frame shorter than this (seconds) counts as smooth")]
        [SerializeField] private float smoothFrameSeconds = 0.05f;
        [Tooltip("On = the next screen starts loading while the sheet is still coming in (shorter wait)")]
        [SerializeField] private bool loadWhileCovering = true;

        [Header("Style")]
        [Tooltip("On = the sheet slides in from the side the next screen is on. Off = it always fades.")]
        [SerializeField] private bool slide = true;
        [Tooltip("In Performance Mode the change is a plain quick fade")]
        [SerializeField] private bool fadeOnlyInPerformanceMode = true;
        [Tooltip("On = grey placeholder shapes appear on the sheet when loading is slow. Off = only the sparkle shows.")]
        [SerializeField] private bool showPlaceholderShapes = false;
        [Tooltip("How fast the sparkle turns, in degrees per second")]
        [SerializeField] private float markSpin = 140f;
        [Tooltip("The sparkle shows when the sheet has covered the screen for this long (seconds)")]
        [SerializeField] private float markAfterSeconds = 0.1f;

        [Header("Safety")]
        [Tooltip("If a change has not finished after this many seconds (a scene that cannot be loaded), the sheet is taken away so the app can be used again")]
        [SerializeField] private float giveUpAfterSeconds = 20f;

        bool busy;
        float busySince;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!busy || Time.unscaledTime - busySince < Mathf.Max(5f, giveUpAfterSeconds)) return;
            Debug.LogWarning("ScreenTransition: the screen change did not finish; the sheet was taken away.");
            StopAllCoroutines();
            Finish();
        }

        /// <summary>
        /// Starts the change to another scene. `enterFrom`: 1 = the sheet comes in from the right, -1 = from the
        /// left, 0 = it fades. Returns false when it cannot run (then the caller loads the scene itself).
        /// </summary>
        public bool TryGo(string sceneName, int enterFrom)
        {
            if (busy || overlayCanvas == null || sheet == null || !isActiveAndEnabled) return false;
            StartCoroutine(Run(sceneName, enterFrom));
            return true;
        }

        IEnumerator Run(string sceneName, int enterFrom)
        {
            busy = true; busySince = Time.unscaledTime;
            bool calm = fadeOnlyInPerformanceMode && GlowUp.Performance.PerformanceSettings.ReduceEffects;
            bool sliding = slide && enterFrom != 0 && !calm;
            overlayCanvas.gameObject.SetActive(true);
            if (skeletonGroup != null) skeletonGroup.alpha = 0f;
            if (markGroup != null) markGroup.alpha = 0f;
            float width = ((RectTransform)overlayCanvas.transform).rect.width;
            if (width < 10f) width = 1080f;
            ThreadPriority priorityBefore = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;          // the next screen is what the user is waiting for

            // 1. the next scene starts loading now; it is only switched on when the old screen is covered
            AsyncOperation loading = null;
            if (loadWhileCovering)
            {
                loading = SceneManager.LoadSceneAsync(sceneName);
                if (loading != null) loading.allowSceneActivation = false;
            }

            // 2. cover the old screen
            yield return Move(sliding, enterFrom * width, 0f, 0f, 1f, calm ? coverSeconds * 0.6f : coverSeconds, coverEase);

            // 3. switch the next scene on behind the sheet
            if (loading == null) loading = SceneManager.LoadSceneAsync(sceneName);
            if (loading != null) loading.allowSceneActivation = true;
            float waited = 0f;
            while (loading != null && !loading.isDone)
            {
                waited += Time.unscaledDeltaTime;
                ShowWaiting(waited);
                yield return null;
            }
            Application.backgroundLoadingPriority = priorityBefore;

            // 4. the new screen sets itself up in its first frames (texts, lists, the camera): wait until the phone draws smoothly again
            yield return null;
            for (int i = 0; i < Mathf.Max(1, settleFrames); i++)
            {
                waited += Time.unscaledDeltaTime;
                ShowWaiting(waited);
                yield return null;
                if (Time.unscaledDeltaTime <= smoothFrameSeconds) break;
            }

            // 5. uncover the new screen: the sheet carries on in the same direction
            yield return Move(sliding, 0f, -enterFrom * width, 1f, 0f, calm ? revealSeconds * 0.6f : revealSeconds, revealEase);
            Finish();
        }

        void Finish()
        {
            if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
            busy = false;
        }

        /// <summary>While the sheet covers the screen: the sparkle turns, and the placeholder shapes come when it takes long.</summary>
        void ShowWaiting(float waited)
        {
            float dt = Time.unscaledDeltaTime;
            if (markGroup != null && waited > markAfterSeconds) markGroup.alpha = Mathf.MoveTowards(markGroup.alpha, 1f, dt / 0.12f);
            if (mark != null) mark.Rotate(0f, 0f, -markSpin * Mathf.Min(dt, 0.05f));
            if (showPlaceholderShapes && skeletonGroup != null && waited > skeletonAfterSeconds)
            {
                skeletonGroup.alpha = Mathf.MoveTowards(skeletonGroup.alpha, 1f, dt / Mathf.Max(0.01f, skeletonFadeSeconds));
                if (markGroup != null) markGroup.alpha = Mathf.MoveTowards(markGroup.alpha, 0f, dt / 0.08f);       // the shapes take the sparkle's place
            }
        }

        IEnumerator Move(bool sliding, float fromX, float toX, float fromAlpha, float toAlpha, float seconds, AnimationCurve curve)
        {
            float t = 0f;
            float markFrom = markGroup != null ? markGroup.alpha : 0f;
            while (t < 1f)
            {
                // a single slow frame must not make the sheet jump across the screen
                float dt = Mathf.Min(Time.unscaledDeltaTime, Mathf.Max(0.01f, longestFrameStep));
                t = seconds <= 0.01f ? 1f : Mathf.Min(1f, t + dt / seconds);
                float k = curve != null && curve.length > 1 ? Mathf.Clamp01(curve.Evaluate(t)) : t;
                if (sliding)
                {
                    sheet.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, k), 0f);
                    if (sheetGroup != null) sheetGroup.alpha = 1f;
                }
                else
                {
                    sheet.anchoredPosition = Vector2.zero;
                    if (sheetGroup != null) sheetGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
                }
                if (toAlpha < fromAlpha)
                {
                    // leaving: the sparkle and the shapes fade first
                    if (markGroup != null) markGroup.alpha = Mathf.Lerp(markFrom, 0f, Mathf.Clamp01(t * 3f));
                    if (skeletonGroup != null && skeletonGroup.alpha > 0f) skeletonGroup.alpha = Mathf.MoveTowards(skeletonGroup.alpha, 0f, dt / 0.12f);
                }
                if (mark != null) mark.Rotate(0f, 0f, -markSpin * dt);
                yield return null;
            }
        }
    }
}
