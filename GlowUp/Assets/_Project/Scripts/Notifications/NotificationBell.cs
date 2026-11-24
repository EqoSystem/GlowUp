using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Notifications
{
    /// <summary>
    /// The bell beside the dashboard's gear (New22, 20 November 2026): opens NotificationsScene and shows how many notifications
    /// are unread in a small badge (a number, so the meaning never rests on the colour alone). Live through InboxService.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class NotificationBell : MonoBehaviour
    {
        [SerializeField] private string scene = SceneNames.Notifications;
        Image badge;
        TextMeshProUGUI count;
        Button button;

        void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => SceneNavigator.Go(scene));
            var p = RuntimeTheme.Palette;
            badge = RuntimeUiKit.Box(transform, "UnreadBadge", p.warning);
            badge.raycastTarget = false;
            var rt = badge.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(8f, 8f); rt.sizeDelta = new Vector2(38f, 38f);   // on the corner, mostly outside the bell: the bell itself is never covered
            count = RuntimeUiKit.Label(badge.transform, "Count", "", 22, GlowUp.Theme.ContrastInk.On(p.warning), RuntimeTheme.Font(GlowUp.Theme.FontRole.Label, GetComponentInChildren<TMP_Text>(true)));
            count.fontStyle = FontStyles.Bold; count.enableWordWrapping = false;
            RuntimeUiKit.Stretch(count.rectTransform);
            badge.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            InboxService.Ensure();
            InboxService.Changed += Paint;
            Paint();
        }

        void OnDisable() { InboxService.Changed -= Paint; }

        void Paint()
        {
            int n = InboxService.Instance != null ? InboxService.Instance.UnreadCount : 0;
            if (badge == null) return;
            badge.gameObject.SetActive(n > 0);
            count.text = n > 99 ? "99+" : n.ToString();
            badge.rectTransform.sizeDelta = new Vector2(n > 9 ? 52f : 38f, 38f);
        }
    }
}
