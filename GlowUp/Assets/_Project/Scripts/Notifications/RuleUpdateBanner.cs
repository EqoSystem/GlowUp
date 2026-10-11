using GlowUp.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Notifications
{
    /// <summary>
    /// The small notice on the dashboard that says "the ingredient rules were updated". It appears when
    /// IngredientRuleWatcher noticed a change (and the user has rule updates switched on in Settings) and
    /// stays until the user closes it. Fade time and wording are Inspector fields.
    /// </summary>
    public class RuleUpdateBanner : MonoBehaviour
    {
        [Tooltip("The visible notice. It is switched off while there is nothing to say.")]
        [SerializeField] private CanvasGroup notice;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button closeButton;
        [SerializeField] private float fadeSeconds = 0.25f;
        [Tooltip("{0} = when the change was noticed")]
        [SerializeField] private string message = "Ingredient safety rules were updated ({0}). New scans use them.";

        float target;

        void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
            if (notice != null) { notice.alpha = 0f; notice.gameObject.SetActive(false); }
        }

        void OnEnable() { IngredientRuleWatcher.Updated += Refresh; NotificationPreferences.Changed += Refresh; }
        void OnDisable() { IngredientRuleWatcher.Updated -= Refresh; NotificationPreferences.Changed -= Refresh; }
        void Start() { Refresh(); }

        void Refresh()
        {
            if (this == null || notice == null) return;
            bool show = IngredientRuleWatcher.HasUnseenUpdate && NotificationPreferences.RuleUpdates;
            target = show ? 1f : 0f;
            if (show)
            {
                if (messageText != null) messageText.text = string.Format(message, ScanHistoryRepository.FormatTime(IngredientRuleWatcher.LastChangeUnix));
                notice.gameObject.SetActive(true);
            }
        }

        void Dismiss()
        {
            IngredientRuleWatcher.MarkSeen();
            target = 0f;
        }

        void Update()
        {
            if (notice == null || !notice.gameObject.activeSelf) return;
            if (!Mathf.Approximately(notice.alpha, target))
                notice.alpha = Mathf.MoveTowards(notice.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            else if (target <= 0f) notice.gameObject.SetActive(false);
            notice.blocksRaycasts = target > 0f;
        }
    }
}
