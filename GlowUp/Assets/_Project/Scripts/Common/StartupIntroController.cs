using System;
using System.Collections;
using System.Threading.Tasks;
using Firebase.Auth;
using GlowUp.Data;
using GlowUp.Performance;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// The start-up animation (StartupScene, the first scene of the app):
    ///
    ///   1. the word "Glow" comes DOWN to the middle of the screen with a glowing aura round it,
    ///      while the word "Up" comes UP to the middle, next to it;
    ///   2. the two join into "GlowUp" with a burst of light;
    ///   3. a "Made with Unity" plate shoots in and knocks the word off the screen;
    ///   4. the app opens: the dashboard for someone who is signed in, otherwise the log-in page.
    ///
    /// About Unity's own start-up screen: a Unity Personal licence always shows the engine's "Made with Unity" screen
    /// FIRST, before any scene, and that cannot be changed or animated. This animation plays right after it.
    ///
    /// Every text, timing, distance and curve below is an Inspector field. The scene is built in its END position
    /// (the finished word in the middle), so it looks right in Edit mode; the script moves the words to their starting
    /// places when the app runs. A tap skips the animation. With "reduce effects" on (Performance Mode) only a short
    /// fade is shown.
    /// </summary>
    public class StartupIntroController : MonoBehaviour
    {
        public enum Ending { KnockedOffByMadeWithUnity, FadeOut }

        [Header("The words")]
        [Tooltip("The object that holds both words (it is what gets knocked off the screen)")]
        [SerializeField] private RectTransform wordmark;
        [SerializeField] private RectTransform glowWord;
        [SerializeField] private RectTransform upWord;

        [Header("The glowing aura")]
        [SerializeField] private Image aura;
        [Range(0f, 1f)] [SerializeField] private float auraStrength = 0.85f;
        [Tooltip("The aura breathes: its size swings by this share, this many times a second")]
        [Range(0f, 0.4f)] [SerializeField] private float auraPulse = 0.12f;
        [SerializeField] private float auraPulsesPerSecond = 1.6f;
        [Tooltip("How much bigger the aura gets for a moment when the two words join")]
        [SerializeField] private float burstScale = 1.9f;
        [Tooltip("Small star that flashes when the words join. Optional.")]
        [SerializeField] private Image sparkle;

        [Header("The plate that knocks the word off")]
        [SerializeField] private RectTransform plate;
        [SerializeField] private TextMeshProUGUI plateText;
        [SerializeField] private string plateWords = "Made with Unity";
        [Tooltip("Where the plate waits before it shoots in (canvas units from the middle of the screen)")]
        [SerializeField] private Vector2 plateStart = new Vector2(-1500f, 70f);
        [Tooltip("Where the plate comes to rest (canvas units from the middle of the screen). The scene builder sets it to the place of the word.")]
        [SerializeField] private Vector2 plateRest = new Vector2(0f, 70f);

        [Header("How the animation ends")]
        [SerializeField] private Ending ending = Ending.KnockedOffByMadeWithUnity;

        [Header("Timing (seconds)")]
        [SerializeField] private float startDelay = 0.1f;
        [Tooltip("How long the two words travel to the middle")]
        [SerializeField] private float travelSeconds = 0.85f;
        [SerializeField] private float joinSeconds = 0.35f;
        [Tooltip("How long the finished word stays before the plate comes")]
        [SerializeField] private float holdSeconds = 0.45f;
        [SerializeField] private float plateEnterSeconds = 0.24f;
        [Tooltip("How long the knocked word flies before it is gone")]
        [SerializeField] private float flightSeconds = 0.7f;
        [SerializeField] private float plateHoldSeconds = 0.6f;
        [SerializeField] private float fadeSeconds = 0.25f;

        [Header("Movement")]
        [Tooltip("How far above (Glow) and below (Up) the middle the words start, in canvas units")]
        [SerializeField] private float startDistance = 1250f;
        [Tooltip("0 to 1 over the travel: how far along the way a word is. Going a little past 1 and back makes it settle with a bounce.")]
        [SerializeField] private AnimationCurve travelCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(0.72f, 1.06f, 0.4f, 0.4f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("How much the word swells for a moment when the two parts join (0.12 = 12 percent)")]
        [Range(0f, 0.4f)] [SerializeField] private float joinPunch = 0.12f;
        [Tooltip("The speed the word is knocked away with (canvas units a second: right, up)")]
        [SerializeField] private Vector2 knockVelocity = new Vector2(1700f, 950f);
        [Tooltip("How fast the knocked word falls (canvas units a second, every second)")]
        [SerializeField] private float gravity = 4600f;
        [SerializeField] private float spinDegreesPerSecond = -520f;
        [Tooltip("How far the plate slides past the middle before it settles (canvas units)")]
        [SerializeField] private float plateOvershoot = 46f;

        [Header("After the animation")]
        [SerializeField] private string loginScene = SceneNames.Login;
        [SerializeField] private string dashboardScene = SceneNames.Dashboard;
        [Tooltip("On = someone who is still signed in goes straight to the dashboard. Off = always the log-in page (which then sends a signed-in user on by itself).")]
        [SerializeField] private bool signedInGoesToDashboard = true;
        [Tooltip("The animation never waits longer than this for the sign-in check")]
        [SerializeField] private float longestSignInCheckSeconds = 2.5f;
        [SerializeField] private bool tapSkips = true;
        [Tooltip("Everything that fades out at the end (the whole stage)")]
        [SerializeField] private CanvasGroup stage;

        Task<string> whereNext;
        bool leaving;
        float startedAt;

        void Start()
        {
            startedAt = Time.unscaledTime;
            if (plateText != null && !string.IsNullOrWhiteSpace(plateWords)) plateText.text = plateWords;
            whereNext = DecideWhereNext();
            bool calm = PerformanceSettings.ReduceEffects || wordmark == null || glowWord == null || upWord == null;
            StartCoroutine(calm ? PlayCalm() : Play());
        }

        void Update()
        {
            if (!tapSkips || leaving || Time.unscaledTime - startedAt < 0.25f) return;
            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0) Leave();
        }

        // ---------------------------------------------------------------------------------------
        //  The animation
        // ---------------------------------------------------------------------------------------
        IEnumerator Play()
        {
            Vector2 glowHome = glowWord.anchoredPosition, upHome = upWord.anchoredPosition, markHome = wordmark.anchoredPosition;
            Vector2 glowFrom = glowHome + Vector2.up * startDistance, upFrom = upHome + Vector2.down * startDistance;
            Vector3 auraHome = aura != null ? aura.rectTransform.localScale : Vector3.one;

            // ---- the starting picture: both words out of sight, no light, the plate waiting at the side
            glowWord.anchoredPosition = glowFrom; upWord.anchoredPosition = upFrom;
            SetAlpha(aura, 0f); SetAlpha(sparkle, 0f);
            if (plate != null) { plate.gameObject.SetActive(ending == Ending.KnockedOffByMadeWithUnity); plate.anchoredPosition = plateStart; }
            if (stage != null) stage.alpha = 1f;
            yield return Wait(startDelay);

            // ---- 1. "Glow" comes down with its aura, "Up" comes up
            for (float t = 0f; t < travelSeconds; t += Time.unscaledDeltaTime)
            {
                float k = travelCurve.Evaluate(Mathf.Clamp01(t / Mathf.Max(0.01f, travelSeconds)));
                glowWord.anchoredPosition = Vector2.LerpUnclamped(glowFrom, glowHome, k);
                upWord.anchoredPosition = Vector2.LerpUnclamped(upFrom, upHome, k);
                FollowWithAura(glowWord, auraHome, Mathf.Clamp01(t / (travelSeconds * 0.45f)) * auraStrength);
                yield return null;
            }
            glowWord.anchoredPosition = glowHome; upWord.anchoredPosition = upHome;

            // ---- 2. the two join into one word: a short swell and a burst of light
            for (float t = 0f; t < joinSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, joinSeconds)), wave = Mathf.Sin(k * Mathf.PI);
                wordmark.localScale = Vector3.one * (1f + joinPunch * wave);
                if (aura != null)
                {
                    aura.rectTransform.position = Vector3.Lerp(aura.rectTransform.position, wordmark.position, k);
                    aura.rectTransform.localScale = auraHome * Mathf.Lerp(1f, burstScale, wave);
                    SetAlpha(aura, Mathf.Lerp(auraStrength, 1f, wave));
                }
                if (sparkle != null)
                {
                    SetAlpha(sparkle, wave);
                    sparkle.rectTransform.localScale = Vector3.one * (0.6f + 0.9f * wave);
                    sparkle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f * k);
                }
                yield return null;
            }
            wordmark.localScale = Vector3.one; SetAlpha(sparkle, 0f);
            if (aura != null) aura.rectTransform.position = wordmark.position;

            // ---- the finished word stays for a moment, its aura breathing
            for (float t = 0f; t < holdSeconds; t += Time.unscaledDeltaTime) { Breathe(auraHome); yield return null; }

            if (ending == Ending.KnockedOffByMadeWithUnity && plate != null)
            {
                // ---- 3. the plate shoots in ...
                Vector2 past = plateRest + (plateRest - plateStart).normalized * plateOvershoot;
                for (float t = 0f; t < plateEnterSeconds; t += Time.unscaledDeltaTime)
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.01f, plateEnterSeconds));
                    plate.anchoredPosition = Vector2.Lerp(plateStart, past, k * k);            // faster and faster
                    Breathe(auraHome);
                    yield return null;
                }
                // ---- ... and knocks the word off: it flies away turning, the light goes out, the plate settles
                Vector2 velocity = knockVelocity;
                float turned = 0f;
                for (float t = 0f; t < flightSeconds; t += Time.unscaledDeltaTime)
                {
                    float dt = Time.unscaledDeltaTime, k = Mathf.Clamp01(t / Mathf.Max(0.01f, flightSeconds));
                    velocity.y -= gravity * dt;
                    wordmark.anchoredPosition += velocity * dt;
                    turned += spinDegreesPerSecond * dt;
                    wordmark.localRotation = Quaternion.Euler(0f, 0f, turned);
                    SetAlpha(aura, Mathf.Lerp(auraStrength, 0f, k * 3f));
                    plate.anchoredPosition = Vector2.Lerp(past, plateRest, Mathf.SmoothStep(0f, 1f, k * 2.5f));
                    yield return null;
                }
                wordmark.gameObject.SetActive(false);
                plate.anchoredPosition = plateRest;
                yield return Wait(plateHoldSeconds);
            }

            // put back what was moved, in case the scene is shown again
            wordmark.anchoredPosition = markHome;
            yield return FadeAndLeave();
        }

        /// <summary>Performance Mode ("reduce effects"): the finished word simply fades in and out.</summary>
        IEnumerator PlayCalm()
        {
            if (plate != null) plate.gameObject.SetActive(false);
            SetAlpha(sparkle, 0f);
            if (stage != null)
            {
                for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime) { stage.alpha = Mathf.Clamp01(t / 0.35f); yield return null; }
                stage.alpha = 1f;
            }
            yield return Wait(0.7f);
            yield return FadeAndLeave();
        }

        IEnumerator FadeAndLeave()
        {
            // the sign-in check may still be running: it gets a little longer, but never holds the app up
            while (whereNext != null && !whereNext.IsCompleted && Time.unscaledTime - startedAt < longestSignInCheckSeconds) yield return null;
            if (stage != null)
                for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime) { stage.alpha = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, fadeSeconds)); yield return null; }
            Leave();
        }

        // ---------------------------------------------------------------------------------------
        void FollowWithAura(RectTransform word, Vector3 homeScale, float alpha)
        {
            if (aura == null) return;
            aura.rectTransform.position = word.position;
            SetAlpha(aura, alpha);
            Breathe(homeScale, false);
        }

        void Breathe(Vector3 homeScale, bool keepAlpha = true)
        {
            if (aura == null) return;
            float swing = 1f + auraPulse * Mathf.Sin(Time.unscaledTime * auraPulsesPerSecond * Mathf.PI * 2f);
            aura.rectTransform.localScale = homeScale * swing;
            if (keepAlpha) SetAlpha(aura, auraStrength);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;
            Color c = graphic.color; c.a = Mathf.Clamp01(alpha); graphic.color = c;
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        // ---------------------------------------------------------------------------------------
        //  Where the app opens
        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// The dashboard for someone who is still signed in and has a skin profile on this phone (also without internet);
        /// the log-in page for everybody else. The log-in page handles every other case itself, as before.
        /// </summary>
        async Task<string> DecideWhereNext()
        {
            if (!signedInGoesToDashboard) return loginScene;
            try
            {
                bool firebase = await FirebaseBootstrap.Ready;
                FirebaseUser user = firebase ? FirebaseAuth.DefaultInstance.CurrentUser : null;
                UserProfileData saved = UserProfileRepository.Cached();
                bool hasProfile = saved != null && !string.IsNullOrWhiteSpace(saved.skinType);
                if (user != null && hasProfile) return dashboardScene;
                // signed in without internet last time: that session is kept too
                if (user == null && !string.IsNullOrEmpty(UserProfileRepository.OfflineEmail) && hasProfile) return dashboardScene;
            }
            catch (Exception e) { Debug.LogWarning("Start-up: the sign-in check was skipped: " + e.Message); }
            return loginScene;
        }

        void Leave()
        {
            if (leaving) return;
            leaving = true;
            StopAllCoroutines();
            string target = whereNext != null && whereNext.Status == TaskStatus.RanToCompletion && !string.IsNullOrEmpty(whereNext.Result) ? whereNext.Result : loginScene;
            SceneNavigator.Go(target, 0);
        }
    }
}
