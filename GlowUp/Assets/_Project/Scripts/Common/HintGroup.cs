using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Put on a helper-text / badge group. It stays active in the Hierarchy (so you can always
    /// select and edit it) but is invisible - and takes no layout space - unless
    /// ContextualHintController says hints should show: new user, or the app senses trouble.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class HintGroup : MonoBehaviour
    {
        [Tooltip("Always show (ignores the policy). Useful while styling the hint in Play mode.")]
        [SerializeField] private bool alwaysVisible = false;
        [SerializeField] private float fadeSeconds = 0.25f;
        [Tooltip("When hidden, collapse the row's height so lists close the gap")]
        [SerializeField] private bool collapseWhenHidden = true;

        CanvasGroup group;
        LayoutElement layout;
        float shownHeight;
        float target;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            layout = GetComponent<LayoutElement>();
            if (layout != null) shownHeight = layout.preferredHeight;
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        }

        void OnEnable()
        {
            ContextualHintController.HintsChanged += Set;
            Set(alwaysVisible || (ContextualHintController.Instance != null && ContextualHintController.Instance.HintsVisible));
            ApplyHeight(target > 0.5f);
        }
        void OnDisable() => ContextualHintController.HintsChanged -= Set;

        void Set(bool on)
        {
            target = (alwaysVisible || on) ? 1f : 0f;
            ApplyHeight(target > 0.5f);
        }

        void ApplyHeight(bool visible)
        {
            if (layout == null || !collapseWhenHidden) return;
            layout.preferredHeight = visible ? shownHeight : 0f;
            layout.minHeight = visible ? shownHeight : 0f;
        }

        void Update()
        {
            if (Mathf.Approximately(group.alpha, target)) return;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        }
    }
}
