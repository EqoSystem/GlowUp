using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// Makes sure EVERY screen has the safe-area structure (12 November 2026), with nothing to set up per screen:
    ///  - when the app starts and whenever a scene is loaded, every root canvas gets the structure (SafeAreaStructure.Convert);
    ///  - a canvas made later at run time is found by a light check twice a second;
    ///  - in development builds the debug overlay is started (SafeAreaDebugOverlay).
    /// Scenes that were migrated in the Editor already have it; then nothing moves here.
    /// </summary>
    public class SafeAreaBootstrap : MonoBehaviour
    {
        static SafeAreaBootstrap instance;
        readonly HashSet<int> done = new HashSet<int>();
        float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("SafeAreaBootstrap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SafeAreaBootstrap>();
            SceneManager.sceneLoaded += (_, __) => instance.ConvertAll();
            instance.ConvertAll();
#if DEVELOPMENT_BUILD || SAFEAREA_DEBUG
            if (SafeAreaConfig.Current.debugOverlay && !Application.isEditor) SafeAreaDebugOverlay.Create();
#endif
        }

        void ConvertAll()
        {
            foreach (Canvas canvas in FindObjectsOfType<Canvas>())
            {
                if (!SafeAreaStructure.Wanted(canvas) || done.Contains(canvas.GetInstanceID())) continue;
                done.Add(canvas.GetInstanceID());
                SafeAreaStructure.Convert(canvas, false, null, null);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            ConvertAll();
        }
    }
}
