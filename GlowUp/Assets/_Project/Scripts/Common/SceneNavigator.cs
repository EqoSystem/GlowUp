using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.Common
{
    /// <summary>
    /// Single place for scene names. If a scene file is renamed, change it here instead of hunting
    /// through controllers (each controller also shows its own scene fields in the Inspector).
    /// </summary>
    public static class SceneNames
    {
        public const string Startup = "StartupScene";
        public const string Login = "LogInScene";
        public const string Onboarding1 = "OnboardingStep1Scene";
        public const string Onboarding2 = "OnboardingStep2Scene";
        public const string Onboarding3 = "OnboardingStep3Scene";
        public const string Onboarding4 = "OnboardingStep4Scene";
        public const string Onboarding5 = "OnboardingStep5Scene";
        public const string Terms = "TermsAndConditionScene";
        public const string Dashboard = "UserDashboardScene";
        public const string RecentScans = "RecentScanActivitiesScene";
        public const string AiSmartAnalysis = "AiSmartAnalysisScene";
        public const string Loading = "LoadingScene";
        public const string AiResult = "AiSmartAnalysisResultScene";
        public const string ArTryOn = "ARTryOnScene";
        public const string Settings = "SettingsScene";
    }

    /// <summary>
    /// Moves between scenes; optionally through the LoadingScene, which can run a piece of work
    /// (for example the scan analysis) while it tells the user to wait.
    /// </summary>
    public static class SceneNavigator
    {
        /// <summary>Work for the loading scene. Call report(stepIndex, optionalMessage) as each step starts.</summary>
        public delegate Task LoadingWork(Action<int, string> report);

        public static string PendingTarget { get; private set; }
        public static string PendingMessage { get; private set; }
        public static LoadingWork PendingWork { get; private set; }

        /// <summary>The main screens from left to right. Swiping, shaking and the bottom bar move along this row.</summary>
        public static readonly string[] MainOrder = { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.AiSmartAnalysis, SceneNames.ArTryOn };

        // the screens the user came through (newest last), so "back" has somewhere to go
        static readonly List<string> history = new List<string>();
        const int HistoryLimit = 24;

        /// <summary>The screen "back" would return to, or null.</summary>
        public static string Previous => history.Count > 0 ? history[history.Count - 1] : null;

        /// <summary>Opens a scene. Between two main screens the change slides in the direction of the bottom bar.</summary>
        public static void Go(string sceneName)
        {
            string current = SceneManager.GetActiveScene().name;
            int from = Array.IndexOf(MainOrder, current), to = Array.IndexOf(MainOrder, sceneName);
            Go(sceneName, from >= 0 && to >= 0 && from != to ? (to > from ? 1 : -1) : 0);
        }

        /// <param name="enterFrom">1 = the next screen comes in from the right, -1 = from the left, 0 = it fades in</param>
        public static void Go(string sceneName, int enterFrom)
        {
            if (string.IsNullOrEmpty(sceneName)) { Debug.LogWarning("SceneNavigator: no scene name given"); return; }
            if (ScreenTransition.IsBusy) return;                    // a change is already on its way
            Remember(SceneManager.GetActiveScene().name, sceneName);
            Load(sceneName, enterFrom);
        }

        /// <summary>Returns to the screen the user came from. False when there is none.</summary>
        public static bool Back(int enterFrom = -1)
        {
            if (ScreenTransition.IsBusy) return true;
            while (history.Count > 0)
            {
                string target = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (string.IsNullOrEmpty(target) || target == SceneManager.GetActiveScene().name) continue;
                Load(target, enterFrom);
                return true;
            }
            return false;
        }

        static void Remember(string leaving, string next)
        {
            // the loading screen, the start-up animation and the sign-in pages are not places to come back to
            if (string.IsNullOrEmpty(leaving) || leaving == next || leaving == SceneNames.Loading || leaving == SceneNames.Startup) return;
            if (next == SceneNames.Login) { history.Clear(); return; }
            history.Add(leaving);
            if (history.Count > HistoryLimit) history.RemoveAt(0);
        }

        static void Load(string sceneName, int enterFrom)
        {
            if (ScreenTransition.Instance != null && ScreenTransition.Instance.TryGo(sceneName, enterFrom)) return;
            SceneManager.LoadScene(sceneName);
        }

        public static void GoViaLoading(string targetScene, string message = null, LoadingWork work = null)
        {
            PendingTarget = targetScene;
            PendingMessage = message;
            PendingWork = work;
            SceneManager.LoadScene(SceneNames.Loading);
        }

        /// <summary>Lets the running work send the user somewhere else when it finishes (e.g. back to the scanner if nothing could be read).</summary>
        public static void Retarget(string targetScene) { PendingTarget = targetScene; }

        public static void ClearPending() { PendingTarget = null; PendingMessage = null; PendingWork = null; }
    }

    /// <summary>
    /// Put on any Button: choose the destination scene in the Inspector. The button opens it when tapped.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public class SceneNavigateButton : MonoBehaviour
    {
        [SerializeField] private string targetScene;
        [SerializeField] private bool goThroughLoadingScene = false;
        [SerializeField] private string loadingMessage = "Please wait...";

        void Awake() { GetComponent<UnityEngine.UI.Button>().onClick.AddListener(Navigate); }

        public void Navigate()
        {
            if (string.IsNullOrEmpty(targetScene)) { Debug.LogWarning("SceneNavigateButton: target scene not set", this); return; }
            if (goThroughLoadingScene) SceneNavigator.GoViaLoading(targetScene, loadingMessage);
            else SceneNavigator.Go(targetScene);
        }
    }
}
