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
    /// The same card as every app page: back button, the title, one scrolling list. The rows (the admins' replies and the product and ingredient changes
    /// notices) are made at run time by NotificationsScreen from live data; the scene holds
    /// only what stays: the list, the line shown when it is empty, and the controller.
    /// </summary>
    public static class NotificationsSceneBuilder
    {
        [MenuItem("GlowUp/Build Scene/15 Notifications", false, 125)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("NotificationsScene", "Notifications", true, "NotificationsCard");
            Transform card = s.card;

            // 22 Nov 2026 (panel): two buttons above the list; the list moves down to make room
            GameObject actions = A.Node(card, "ListActions");
            A.TopStrip(actions.transform, A.HeaderBottom + 6, 96, 40, 40);
            Button markAll = A.PillButton(actions.transform, "MarkAllReadButton", "Mark all as read", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.Fill(markAll.transform, 0, 0, 548, 0);
            Button deleteAll = A.PillButton(actions.transform, "DeleteAllButton", "Delete all", 30, ThemeRole.SurfaceAlt, ThemeRole.Warning);
            A.Fill(deleteAll.transform, 548, 0, 0, 0);
            A.AppPopup deletePopup = A.Overlay(s, "DeleteAllPanel", "Delete all?");
            A.At(deletePopup.box, A.MC, new Vector2(0, 60), new Vector2(900, 520));
            TextMeshProUGUI deleteMessage = A.Label(deletePopup.box, "MessageText", "Every notification in this list is removed.", 32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(deleteMessage.transform, 48, 190, 48, 130);
            A.ShrinkWrapped(deleteMessage, 26);
            Button deleteCancel = A.PillButton(deletePopup.box, "CancelButton", "Cancel", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(deleteCancel.transform, 44, 108, 48, 462);
            Button deleteOk = A.PillButton(deletePopup.box, "ConfirmButton", "Delete all", 36, ThemeRole.Warning, ThemeRole.Background);
            A.BottomStrip(deleteOk.transform, 44, 108, 462, 48);
            Object.DestroyImmediate(deleteOk.GetComponent<ThemedGraphic>());                  // ConfirmDialog paints this button itself
            Object.DestroyImmediate(A.LabelOf(deleteOk).GetComponent<ThemedGraphic>());
            var deleteDialog = A.Controller<GlowUp.AppSettings.ConfirmDialog>("NotificationsScreen");
            A.Set(deleteDialog, "panel", deletePopup.panel); A.Set(deleteDialog, "titleText", deletePopup.title); A.Set(deleteDialog, "messageText", deleteMessage);
            A.Set(deleteDialog, "confirmButton", deleteOk); A.Set(deleteDialog, "confirmLabel", A.LabelOf(deleteOk));
            A.Set(deleteDialog, "cancelButton", deleteCancel); A.Set(deleteDialog, "cancelLabel", A.LabelOf(deleteCancel));

            GameObject listArea = A.Node(card, "NotificationsArea");
            A.Between(listArea.transform, A.HeaderBottom + 118, 30, 26, 14);
            A.Scroll(listArea.transform, "NotificationsScrollView", out RectTransform content, 24, 30, 14, 34, 60);

            TextMeshProUGUI empty = A.Label(card, "EmptyText", "No notifications yet. Replies to your reports and news about GlowUp appear here.",
                                            32, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(empty.transform, A.HeaderBottom + 120, 200, 80, 80);

            var controller = A.Controller<NotificationsScreen>("NotificationsScreen");
            A.Set(controller, "backButton", s.back);
            A.Set(controller, "markAllButton", markAll); A.Set(controller, "deleteAllButton", deleteAll);
            A.Set(controller, "deleteAllConfirm", deleteDialog); A.Set(controller, "listActions", actions);
            A.Set(controller, "content", content);
            A.Set(controller, "emptyText", empty);
            A.Set(controller, "fontSource", s.title);

            A.Finish(s);
            Debug.Log("[GlowUp] NotificationsScene built. The rows are made at run time from the live notifications.");
        }
    }
}
