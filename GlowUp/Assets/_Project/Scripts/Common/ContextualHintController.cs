using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Decides whether helper text / badges are shown. Hints appear only for a newly created
    /// user (first-run) or when the app senses trouble (repeated errors, long idle). Existing
    /// accounts never see them unless trouble is detected. Tune every threshold in the Inspector.
    /// </summary>
    public class ContextualHintController : MonoBehaviour
    {
        public const string NewUserKey = "GlowUp.IsNewUser";
        public static ContextualHintController Instance { get; private set; }
        public static event System.Action<bool> HintsChanged;

        [Header("Policy")]
        [SerializeField] private bool forceShowForTesting = false;
        [Tooltip("Show hints when the user is brand new (set by Onboarding / sign-up)")]
        [SerializeField] private bool showForNewUsers = true;
        [Tooltip("Number of validation errors in one scene that count as 'having trouble'")]
        [SerializeField] private int errorsBeforeTrouble = 3;
        [Tooltip("Seconds without any tap before we assume the user is stuck (0 = off)")]
        [SerializeField] private float idleSecondsBeforeTrouble = 25f;
        [Tooltip("Back/forward scene bounces counted as confusion")]
        [SerializeField] private int bouncesBeforeTrouble = 3;

        int errors, bounces;
        float lastInput;
        bool troubleDetected;

        public bool HintsVisible =>
            forceShowForTesting || troubleDetected ||
            (showForNewUsers && PlayerPrefs.GetInt(NewUserKey, 0) == 1);

        void Awake() { Instance = this; lastInput = Time.unscaledTime; }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void Start() => HintsChanged?.Invoke(HintsVisible);

        void Update()
        {
            if (Input.anyKeyDown || Input.touchCount > 0 || Input.GetMouseButton(0)) lastInput = Time.unscaledTime;
            if (!troubleDetected && idleSecondsBeforeTrouble > 0 && Time.unscaledTime - lastInput > idleSecondsBeforeTrouble)
                RaiseTrouble();
        }

        public static void MarkNewUser(bool value) { PlayerPrefs.SetInt(NewUserKey, value ? 1 : 0); PlayerPrefs.Save(); }

        public void ReportValidationError() { if (++errors >= errorsBeforeTrouble) RaiseTrouble(); }
        public void ReportSceneBounce() { if (++bounces >= bouncesBeforeTrouble) RaiseTrouble(); }

        void RaiseTrouble()
        {
            if (troubleDetected) return;
            troubleDetected = true;
            HintsChanged?.Invoke(true);
        }
    }

    /// <summary>
    /// Put on helper text, adornment badges or tile status pills. Active only when the
    /// ContextualHintController says hints should be visible.
    /// </summary>
    public class HintVisibilityTarget : MonoBehaviour
    {
        [Tooltip("Objects shown/hidden together (defaults to this GameObject)")]
        [SerializeField] private GameObject[] targets;

        void OnEnable()
        {
            ContextualHintController.HintsChanged += Set;
            Set(ContextualHintController.Instance != null && ContextualHintController.Instance.HintsVisible);
        }
        void OnDisable() => ContextualHintController.HintsChanged -= Set;

        void Set(bool on)
        {
            if (targets == null || targets.Length == 0) return;
            foreach (var t in targets) if (t != null && t != gameObject) t.SetActive(on);
        }
    }
}
