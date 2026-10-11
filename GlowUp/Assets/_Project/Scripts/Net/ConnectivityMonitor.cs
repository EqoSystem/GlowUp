using System;
using UnityEngine;

namespace GlowUp.Net
{
    /// <summary>
    /// Watches Application.internetReachability and raises an event when it changes.
    /// Place once (DontDestroyOnLoad). Poll interval is an Inspector field.
    /// </summary>
    public class ConnectivityMonitor : MonoBehaviour
    {
        public static ConnectivityMonitor Instance { get; private set; }
        public static bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;
        public static event Action<bool> ConnectivityChanged;

        [SerializeField] private float pollSeconds = 2f;
        [Tooltip("Treat the app as offline even if the network is up (testing)")]
        [SerializeField] private bool simulateOffline = false;

        bool last;
        float timer;

        public static bool Online => Instance != null && Instance.simulateOffline ? false : IsOnline;

        // A phone can be connected (Wi-Fi with no internet behind it, one bar of signal) and still get no answer.
        // After a download got no answer, the other downloads are skipped for a short while, so one dead
        // connection costs a single wait and not one wait per list. They are tried again by themselves.
        static float noAnswerUntil = -1f;

        /// <summary>Online, and the cloud has not just failed to answer. Use this before downloading a list.</summary>
        public static bool Reachable => Online && Time.realtimeSinceStartup >= noAnswerUntil;

        /// <summary>A download got no answer in time: downloads pause for `pauseSeconds`.</summary>
        public static void ReportNoAnswer(float pauseSeconds = 45f) { noAnswerUntil = Time.realtimeSinceStartup + Mathf.Max(0f, pauseSeconds); }

        /// <summary>The cloud answered: downloads are allowed again at once.</summary>
        public static void ReportAnswer() { noAnswerUntil = -1f; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            last = Online;
        }

        void Update()
        {
            timer += Time.unscaledDeltaTime;
            if (timer < pollSeconds) return;
            timer = 0;
            bool now = Online;
            if (now != last) { last = now; ReportAnswer(); ConnectivityChanged?.Invoke(now); }      // a new connection gets a fresh try
        }
    }
}
