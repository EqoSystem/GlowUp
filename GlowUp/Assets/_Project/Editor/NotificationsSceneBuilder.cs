using GlowUp.Notifications;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 15 Notifications (New22, 20 November 2026).
    /// The same card as every app page: back button, the title, one scrolling list. The rows (the admins' replies, maintenance
    /// notices, app updates and product changes) are made at run time by NotificationsScreen from live data; the scene holds
    /// only what stays: the list, the line shown when it is empty, and the controller.
    /// </summary>
    public static class NotificationsSceneBuilder
    {
        [MenuItem("GlowUp/Build Scene/15 Notifications", false, 125)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("NotificationsScene", "Notifications", true, "NotificationsCard");
            Transform card = s.card;

            GameObject listArea = A.Node(card, "NotificationsArea");
            A.Between(listArea.transform, A.HeaderBottom, 30, 26, 14);
            A.Scroll(listArea.transform, "NotificationsScrollView", out RectTransform content, 24, 30, 14, 34, 60);

            TextMeshProUGUI empty = A.Label(card, "EmptyText", "No notifications yet. Replies to your reports and news about GlowUp appear here.",
                                            32, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(empty.transform, A.HeaderBottom + 120, 200, 80, 80);

            var controller = A.Controller<NotificationsScreen>("NotificationsScreen");
            A.Set(controller, "backButton", s.back);
            A.Set(controller, "content", content);
            A.Set(controller, "emptyText", empty);
            A.Set(controller, "fontSource", s.title);

            A.Finish(s);
            Debug.Log("[GlowUp] NotificationsScene built. The rows are made at run time from the live notifications.");
        }
    }
}
