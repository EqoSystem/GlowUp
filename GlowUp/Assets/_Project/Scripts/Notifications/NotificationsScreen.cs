using System;
using System.Collections.Generic;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Notifications
{
    /// <summary>
    /// NotificationsScene (New22, 20 November 2026): the admins' replies to the user's reports, and live notices when an admin changes a product or an ingredient,
    /// newest first, live (InboxService). A tap on a row opens it to its full text and marks it read; the X
    /// deletes it (a reply from the account, an announcement from this phone) and a toast confirms. Opening the screen marks
    /// nothing read by itself, so the bell's count only drops for what was looked at. Back (button or the phone's key) returns.
    /// The rows are made at run time from the theme's colours and fonts; the scene itself comes from NotificationsSceneBuilder.
    /// </summary>
    public class NotificationsScreen : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private RectTransform content;
        [SerializeField] private TextMeshProUGUI emptyText;
        [SerializeField] private TextMeshProUGUI fontSource;
        [Header("22 Nov 2026 (panel): the two buttons above the list (NotificationsScene > ListActions)")]
        [SerializeField] private Button markAllButton;
        [SerializeField] private Button deleteAllButton;
        [SerializeField] private GlowUp.AppSettings.ConfirmDialog deleteAllConfirm;
        [SerializeField] private GameObject listActions;
        [SerializeField] private string deleteAllTitle = "Delete all?";
        [SerializeField] private string deleteAllBody = "Every notification in this list is removed. This cannot be undone.";
        [SerializeField] private string deleteAllLabel = "Delete all";
        [SerializeField] private string msgDeleteAllFailed = "Some notifications could not be deleted. Check your connection and try again.";
        [SerializeField] private string msgEmpty = "No notifications yet. Replies to your reports and news about GlowUp appear here.";
        [SerializeField] private string msgLoading = "Loading your notifications...";
        [SerializeField] private string msgDeleted = "Notification deleted.";
        [SerializeField] private string msgDeleteFailed = "It could not be deleted. Check your connection and try again.";

        readonly List<GameObject> rows = new List<GameObject>();
        readonly HashSet<string> open = new HashSet<string>();

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(Leave);
            if (markAllButton != null) markAllButton.onClick.AddListener(() => { InboxService.Instance?.MarkAllRead(); });
            if (deleteAllButton != null) deleteAllButton.onClick.AddListener(AskDeleteAll);
        }

        void AskDeleteAll()
        {
            if (InboxService.Instance == null) return;
            if (deleteAllConfirm == null) { _ = DoDeleteAll(); return; }
            deleteAllConfirm.Ask(deleteAllTitle, deleteAllBody, deleteAllLabel, () => { _ = DoDeleteAll(); }, true);
        }

        async System.Threading.Tasks.Task DoDeleteAll()
        {
            int failed = await InboxService.Instance.DeleteAll();
            if (failed > 0 && FormalToast.Current != null) FormalToast.Current.Show(FormalToast.Kind.Warning, msgDeleteAllFailed);
        }

        void OnEnable()
        {
            InboxService.Ensure();
            InboxService.Changed += Paint;
            Paint();
        }

        void OnDisable() { InboxService.Changed -= Paint; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Leave();
        }

        void Leave()
        {
            if (!SceneNavigator.Back()) SceneNavigator.Go(SceneNames.Dashboard);
        }

        void Paint()
        {
            if (content == null) return;
            foreach (GameObject r in rows) if (r != null) Destroy(r);
            rows.Clear();
            InboxService inbox = InboxService.Instance;
            List<InboxService.Item> items = inbox != null ? inbox.Items() : new List<InboxService.Item>();
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(items.Count == 0);
                emptyText.text = inbox != null && inbox.Loaded ? msgEmpty : msgLoading;
            }
            if (listActions != null) listActions.SetActive(items.Count > 0);
            if (markAllButton != null) markAllButton.interactable = items.Exists(i => !i.read);
            foreach (InboxService.Item item in items) rows.Add(MakeRow(item));
            LayoutRebuilder.MarkLayoutForRebuild(content);
        }

        GameObject MakeRow(InboxService.Item item)
        {
            var p = RuntimeTheme.Palette;
            TMP_FontAsset body = RuntimeTheme.Font(GlowUp.Theme.FontRole.Body, fontSource), heading = RuntimeTheme.Font(GlowUp.Theme.FontRole.Heading, fontSource);
            bool expanded = open.Contains(item.id);

            Image back = RuntimeUiKit.Box(content, "Notification_" + item.id, item.read ? p.surfaceAlt : RuntimeTheme.Mix(p.surfaceAlt, p.accent, 0.10f));
            back.raycastTarget = true;
            var row = back.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(36, 20, 30, 30); row.spacing = 24; row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = false;
            back.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tap = back.gameObject.AddComponent<Button>();
            tap.targetGraphic = back; tap.transition = Selectable.Transition.ColorTint;
            tap.onClick.AddListener(() =>
            {
                if (!open.Remove(item.id)) open.Add(item.id);
                InboxService.Instance?.MarkRead(item);
                Paint();
            });

            // unread: a small accent dot (and the word "New" for screen readers and colour-blind users)
            Image dot = RuntimeUiKit.Icon(back.transform, "UnreadDot", RuntimeIcons.Circle, item.read ? Color.clear : p.accent, 22);
            var dotSize = dot.gameObject.AddComponent<LayoutElement>(); dotSize.preferredWidth = 22; dotSize.preferredHeight = 22; dotSize.minWidth = 22;

            RectTransform words = RuntimeUiKit.Rect("Words", back.transform);
            var column = words.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 10; column.childControlWidth = true; column.childControlHeight = true; column.childForceExpandHeight = false; column.childForceExpandWidth = true;
            words.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            string label = item.kind == "reply" ? "Reply from GlowUp" : item.kind == "product" ? "Product change" : item.kind == "ingredient" ? "Ingredient change" : "GlowUp news";
            TextMeshProUGUI kind = RuntimeUiKit.Label(words, "Kind", (item.read ? "" : "New · ") + label + " · " + When(item.createdAt), 26, p.secondaryText, body, TextAlignmentOptions.Left);
            TextMeshProUGUI title = RuntimeUiKit.Label(words, "Title", item.title, 36, p.heading, heading, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            // New23 (security review): a notice carries names typed by users (an app-added product), so its words are plain text, never markup
            kind.richText = false; title.richText = false;
            if (!string.IsNullOrEmpty(item.body))
            {
                TextMeshProUGUI text = RuntimeUiKit.Label(words, "Body", item.body, 32, p.mainText, body, TextAlignmentOptions.TopLeft);
                text.richText = false;
                if (!expanded) { text.maxVisibleLines = 2; text.overflowMode = TextOverflowModes.Ellipsis; }
            }

            // the X: 144 x 144 units = 48 dp
            Image xBack = RuntimeUiKit.Box(back.transform, "DeleteButton", Color.clear, false);
            xBack.raycastTarget = true;
            var xSize = xBack.gameObject.AddComponent<LayoutElement>(); xSize.preferredWidth = 120; xSize.preferredHeight = 120; xSize.minWidth = 120;
            TextMeshProUGUI x = RuntimeUiKit.Label(xBack.transform, "X", "×", 64, p.secondaryText, body);
            RuntimeUiKit.Stretch(x.rectTransform);
            var delete = xBack.gameObject.AddComponent<Button>();
            delete.targetGraphic = xBack;
            delete.onClick.AddListener(async () =>
            {
                bool ok = InboxService.Instance != null && await InboxService.Instance.Delete(item);
                FormalToast toast = FormalToast.OnScreen(transform, fontSource);
                if (toast != null) toast.Show(ok ? FormalToast.Kind.Success : FormalToast.Kind.Error, ok ? msgDeleted : msgDeleteFailed);
            });
            return back.gameObject;
        }

        static string When(DateTime t)
        {
            TimeSpan ago = DateTime.Now - t;
            if (ago.TotalMinutes < 1) return "just now";
            if (ago.TotalHours < 1) return (int)ago.TotalMinutes + " min ago";
            if (ago.TotalDays < 1) return (int)ago.TotalHours + " h ago";
            if (ago.TotalDays < 7) return (int)ago.TotalDays + " d ago";
            return t.ToString("MMM d, yyyy");
        }
    }
}
