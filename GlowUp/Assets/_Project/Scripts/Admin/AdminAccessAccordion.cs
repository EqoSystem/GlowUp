using System.Collections;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// The small "Admin?" row on the customer login, under Google sign-in and above the legal line. Closed by default; a tap
    /// opens it and shows ONE gold "Staff / Admin Access" button that goes to the staff sign-in (never to the dashboard).
    /// It does not change the customer sign-in. Coming back from the staff sign-in, the row is open again (the user had opened it).
    /// </summary>
    public class AdminAccessAccordion : MonoBehaviour
    {
        [SerializeField] private Button toggleButton;
        [SerializeField] private TextMeshProUGUI toggleLabel;
        [SerializeField] private RectTransform chevron;
        [SerializeField] private CanvasGroup panel;
        [SerializeField] private Button staffButton;
        [SerializeField] private string staffScene = "AdminLoginScene";
        [SerializeField] private string closedText = "Admin?";
        [SerializeField] private string openText = "Admin?";
        [SerializeField] private float animationSeconds = 0.22f;

        static bool reopen;                  // the staff sign-in was opened from here: show the row open on return
        bool open;
        Coroutine running;

        void Awake()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
            if (staffButton != null) staffButton.onClick.AddListener(() => { reopen = true; SceneNavigator.Go(staffScene); });
            Show(reopen, false);
            reopen = false;
        }

        public void Toggle() => Show(!open, true);

        void Show(bool on, bool animate)
        {
            open = on;
            if (toggleLabel != null) toggleLabel.text = on ? openText : closedText;
            if (running != null) StopCoroutine(running);
            if (!animate || !isActiveAndEnabled) { Apply(on ? 1f : 0f); return; }
            running = StartCoroutine(Animate(on));
        }

        IEnumerator Animate(bool on)
        {
            float from = panel != null && panel.gameObject.activeSelf ? panel.alpha : 0f, to = on ? 1f : 0f;
            for (float t = 0; t < animationSeconds; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / animationSeconds)));
                yield return null;
            }
            Apply(to);
            running = null;
        }

        void Apply(float k)
        {
            if (chevron != null) chevron.localEulerAngles = new Vector3(0, 0, 180f * k);
            if (panel == null) return;
            panel.gameObject.SetActive(k > 0.001f);
            panel.alpha = k;
            panel.interactable = panel.blocksRaycasts = k > 0.99f;
            panel.transform.localScale = new Vector3(1f, Mathf.Lerp(0.85f, 1f, k), 1f);
        }
    }
}
