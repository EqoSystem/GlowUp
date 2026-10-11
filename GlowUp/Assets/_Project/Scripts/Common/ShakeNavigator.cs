using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.Common
{
    /// <summary>
    /// NO LONGER USED (October 2026): AirGestureNavigator (a wave past the selfie camera) replaced it, and the
    /// scene builders take this component off UiServices. The file is kept so nothing that still points at
    /// it breaks; it can be deleted.
    ///
    /// Shake the phone to change screen: a flick to the RIGHT opens the next main screen, a flick to the
    /// LEFT goes back to the screen the user came from. It only works when the user has switched it on
    /// (Settings page) and only on the screens listed below, so a bump never throws someone out of
    /// sign-up or a running scan.
    ///
    /// One of these lives on the UiServices object and stays alive across scenes.
    /// </summary>
    public class ShakeNavigator : MonoBehaviour
    {
        public static ShakeNavigator Instance { get; private set; }

        [Header("Where it works")]
        [Tooltip("Shaking is only listened to on these scenes")]
        [SerializeField] private string[] activeScenes = { SceneNames.Dashboard, SceneNames.RecentScans, SceneNames.AiSmartAnalysis, SceneNames.ArTryOn, SceneNames.Settings };
        [Tooltip("\"Next\" walks through this list from left to right")]
        [SerializeField] private string[] nextOrder = { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.AiSmartAnalysis, SceneNames.ArTryOn };
        [Tooltip("Where a flick to the left goes when there is no earlier screen to return to")]
        [SerializeField] private string homeScene = SceneNames.Dashboard;

        [Header("How hard a shake has to be")]
        [Tooltip("Sideways push needed, in g (1 = the pull of gravity). Lower = more sensitive.")]
        [SerializeField] private float pushNeeded = 1.15f;
        [Tooltip("How quickly the phone's resting position is followed (higher = follows faster, so slow tilts are ignored)")]
        [SerializeField] private float restFollowSpeed = 6f;
        [Tooltip("Seconds before another shake is accepted")]
        [SerializeField] private float pauseAfterShake = 1.2f;
        [Tooltip("Swap the two directions")]
        [SerializeField] private bool swapDirections = false;

        Vector3 rest;
        float quietUntil;
        bool hasRest;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Instance != this || !UiPreferences.ShakeToNavigate || !SystemInfo.supportsAccelerometer) return;
            Vector3 now = Input.acceleration;
            if (!hasRest) { rest = now; hasRest = true; return; }
            // what is left after taking away the slowly changing part (gravity, the way the phone is held) is the shake
            Vector3 push = now - rest;
            rest = Vector3.Lerp(rest, now, 1f - Mathf.Exp(-restFollowSpeed * Time.unscaledDeltaTime));

            if (Time.unscaledTime < quietUntil || Mathf.Abs(push.x) < pushNeeded) return;
            if (Mathf.Abs(push.x) < Mathf.Abs(push.y) * 1.2f) return;           // mostly up-and-down: not a sideways flick
            if (ScreenTransition.IsBusy || !IsActiveScene()) return;

            quietUntil = Time.unscaledTime + pauseAfterShake;
            bool right = push.x > 0f;
            if (swapDirections) right = !right;
            if (right) GoNext(); else GoBack();
        }

        bool IsActiveScene()
        {
            string current = SceneManager.GetActiveScene().name;
            foreach (string scene in activeScenes) if (scene == current) return true;
            return false;
        }

        void GoNext()
        {
            string current = SceneManager.GetActiveScene().name;
            int index = System.Array.IndexOf(nextOrder, current);
            if (index >= 0 && index + 1 < nextOrder.Length) SceneNavigator.Go(nextOrder[index + 1], 1);
        }

        void GoBack()
        {
            if (SceneNavigator.Back()) return;
            string current = SceneManager.GetActiveScene().name;
            int index = System.Array.IndexOf(nextOrder, current);
            if (index > 0) SceneNavigator.Go(nextOrder[index - 1], -1);
            else if (current != homeScene && !string.IsNullOrEmpty(homeScene)) SceneNavigator.Go(homeScene, -1);
        }
    }
}
