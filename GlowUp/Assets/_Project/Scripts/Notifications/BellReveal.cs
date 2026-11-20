using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Notifications
{
    /// <summary>
    /// Dashboard (22 Nov 2026, owner's request): the notification bell rests hidden behind the Settings gear. The first tap on the
    /// gear slides the bell out to the left (a short ease, 0.22 s); a second tap opens Settings as before.
    /// The bell slides back after a few seconds without a tap, and whenever the screen is left. While it is hidden, a small dot on
    /// the gear says there are unread notifications, so nothing new is missed.
    /// </summary>
    public class BellReveal : MonoBehaviour
    {
        [SerializeField] private RectTransform bell;          // the NotificationsButton
        [SerializeField] private RectTransform gear;          // the SettingsButton (the bell sits under it)
        [SerializeField] private Image unreadDot;             // a small dot on the gear, shown while the bell is hidden
        [SerializeField] private float outX = -128f;          // where the bell stands when it is out (its place before 22 Nov 2026)
        [SerializeField] private float stayOutSeconds = 6f;

        public bool IsOut { get; private set; }
        Vector2 home;
        CanvasGroup group;
        Coroutine moving, autoHide;

        void Awake()
        {
            if (bell == null || gear == null) { enabled = false; return; }
            home = gear.anchoredPosition;
            group = bell.GetComponent<CanvasGroup>() ?? bell.gameObject.AddComponent<CanvasGroup>();
            bell.SetSiblingIndex(Mathf.Max(0, gear.GetSiblingIndex()));      // drawn under the gear
            Place(0f);
        }

        void OnEnable() { InboxService.Ensure(); InboxService.Changed += PaintDot; PaintDot(); }
        void OnDisable() { InboxService.Changed -= PaintDot; }

        /// <summary>Slides the bell out (first tap on the gear). Returns false when it was already out (the tap then opens Settings).</summary>
        public bool Reveal()
        {
            if (!enabled) return false;
            if (IsOut) { RestartTimer(); return false; }
            IsOut = true;
            Slide(1f);
            RestartTimer();
            return true;
        }

        public void Tuck() { if (!IsOut) return; IsOut = false; Slide(0f); }

        void RestartTimer()
        {
            if (autoHide != null) StopCoroutine(autoHide);
            autoHide = StartCoroutine(HideLater());
        }
        IEnumerator HideLater() { yield return new WaitForSecondsRealtime(stayOutSeconds); autoHide = null; Tuck(); }

        void Slide(float to)
        {
            if (moving != null) StopCoroutine(moving);
            if (!gameObject.activeInHierarchy) { Place(to); PaintDot(); return; }
            moving = StartCoroutine(Animate(to));
        }

        IEnumerator Animate(float to)
        {
            float from = Mathf.InverseLerp(home.x, home.x + outX, bell.anchoredPosition.x), t = 0f;
            const float time = 0.22f;
            while (t < time)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / time));
                Place(Mathf.Lerp(from, to, k));
                yield return null;
            }
            Place(to);
            moving = null;
            PaintDot();
        }

        // 0 = hidden behind the gear, 1 = out to the left
        void Place(float k)
        {
            bell.anchoredPosition = new Vector2(home.x + outX * k, home.y);
            group.alpha = Mathf.Clamp01(k * 1.4f);
            group.interactable = k > 0.95f;
            group.blocksRaycasts = k > 0.95f;
        }

        void PaintDot()
        {
            if (unreadDot == null) return;
            int n = InboxService.Instance != null ? InboxService.Instance.UnreadCount : 0;
            unreadDot.gameObject.SetActive(n > 0 && !IsOut);
        }
    }
}
