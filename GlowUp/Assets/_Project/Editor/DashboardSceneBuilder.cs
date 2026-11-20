using GlowUp.Common;
using GlowUp.Dashboard;
using GlowUp.Notifications;
using GlowUp.Theme;
using GlowUp.Views;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 8 User Dashboard.
    /// Avatar + greeting + Settings button, the quick summary of the user's profile, the list of
    /// recommended products, the bottom action bar and the account pop-up.
    /// (The dark mode button now lives on the Settings page.)
    /// </summary>
    public static class DashboardSceneBuilder
    {
        const float TileHeight = 128, TileStep = 148, AllergenTileHeight = 132;

        [MenuItem("GlowUp/Build Scene/8 User Dashboard", false, 118)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("UserDashboardScene", null, false, "DashboardCard", "UserDashboardPanel");
            Transform card = s.card;

            // ---- top row: avatar, greeting, settings ----
            Button avatar = A.IconButton(card, "AvatarButton", IconFactory.User, 104, 60, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(avatar.transform, A.TL, new Vector2(28, -28), new Vector2(104, 104));
            A.Outline(avatar.transform, ThemeRole.Gold, 104f);

            TextMeshProUGUI greeting = A.Label(card, "GreetingText", "Hi, there", 62, ThemeRole.Heading, FontRole.Title);
            A.TopStrip(greeting.transform, 28, 104, 152, 236);          // room for the bell and the gear (New22)
            A.Shrink(greeting, 40);

            // the Settings page (dark mode, graphics quality, account, alerts ...) opens from here
            Button settings = A.IconButton(card, "SettingsButton", IconFactory.Gear, 92, 58, ThemeRole.MainText);
            A.At(settings.transform, A.TR, new Vector2(-28, -34), new Vector2(92, 92));

            // New22 (20 November 2026): the bell opens the Notifications screen; its unread badge is drawn by NotificationBell
            Button bell = A.IconButton(card, "NotificationsButton", IconFactory.Bell, 92, 58, ThemeRole.MainText);
            A.At(bell.transform, A.TR, new Vector2(-128, -34), new Vector2(92, 92));
            SceneBuilderCore.Ensure<GlowUp.Notifications.NotificationBell>(bell.gameObject);

            // ---- quick summary ----
            TextMeshProUGUI summaryHeader = A.Label(card, "SummaryHeaderText", "Quick Summary", 40, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(summaryHeader.transform, 160, 58, 48, 48);
            TextMeshProUGUI summaryHint = A.Label(card, "SummaryHintText", "Tap your picture for your account", 26, ThemeRole.SecondaryText, FontRole.Body,
                                                  TextAlignmentOptions.MidlineRight);
            A.TopStrip(summaryHint.transform, 160, 58, 420, 48);
            A.Shrink(summaryHint, 20); A.HintOnly(summaryHint);

            float panelHeight = 24 + 2 * TileStep + AllergenTileHeight + 24;
            Image summary = A.Panel(card, "QuickSummaryPanel", ThemeRole.SurfaceAlt, Kit.PanelAlpha, 56f);
            A.TopStrip(summary.transform, 224, panelHeight);
            TextMeshProUGUI age = SummaryTile(summary.transform, "Age", "Age", "24", 0, 0);
            TextMeshProUGUI birthday = SummaryTile(summary.transform, "Birthday", "Birthday", "May 12, 2002", 1, 0);
            TextMeshProUGUI skinType = SummaryTile(summary.transform, "SkinType", "Skin Type", "Combination", 0, 1);
            TextMeshProUGUI skinTone = SummaryTile(summary.transform, "SkinTone", "Skin Tone", "Medium", 1, 1);

            Image allergenTile = A.Panel(summary.transform, "AllergensTile", ThemeRole.InputBackground, 1f, 40f);
            A.TopStrip(allergenTile.transform, 24 + 2 * TileStep, AllergenTileHeight, 24, 24);
            TextMeshProUGUI allergenCaption = A.Label(allergenTile.transform, "Caption", "Allergens", 27, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(allergenCaption.transform, 12, 40, 30, 20);
            TextMeshProUGUI allergens = A.Label(allergenTile.transform, "AllergensValue", "Fragrance / Parfum, Parabens", 32, ThemeRole.MainText, FontRole.Label,
                                                TextAlignmentOptions.TopLeft, true);
            A.TopStrip(allergens.transform, 50, 76, 30, 20);
            A.ShrinkWrapped(allergens, 22);

            // ---- recommendations ----
            float listTop = 224 + panelHeight + 20;
            TextMeshProUGUI recommendedHeader = A.Label(card, "RecommendedHeaderText", "Recommended for you", 40, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(recommendedHeader.transform, listTop, 58, 48, 48);
            // what a recommendation means, and does not mean (the app writes the line: it is longer for a user with a skin condition)
            TextMeshProUGUI recommendedNote = A.Label(card, "RecommendedNoteText", "No ingredients flagged for your profile. This is not medical advice.", 25,
                                                      ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(recommendedNote.transform, listTop + 56, 62, 48, 48);
            A.ShrinkWrapped(recommendedNote, 20);
            recommendedNote.gameObject.SetActive(false);        // 12 November 2026: the line is inside the "i" after the heading (HelpBadge), not printed under it
            // the small "i" right after "Recommended for you": its bubble holds the note (tap it; it closes by itself)
            recommendedHeader.ForceMeshUpdate();
            HelpBadge recommendedBadge = OnboardingKit.InfoBadge(card, recommendedHeader, null, "No ingredients flagged for your profile. This is not medical advice.");
            var badgeRect = (RectTransform)recommendedBadge.transform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f); badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(recommendedHeader.preferredWidth + 18f, 0f);        // a first guess; TextEndFollower corrects it from the real font
            var bubbleRect = (RectTransform)recommendedBadge.transform.Find("HelpBubble");
            if (bubbleRect != null)
            {
                bubbleRect.anchorMin = bubbleRect.anchorMax = new Vector2(0f, 0f); bubbleRect.pivot = new Vector2(0f, 1f);
                bubbleRect.anchoredPosition = new Vector2(0f, -10f);      // TextEndFollower puts its left edge on the heading's left edge
                bubbleRect.sizeDelta = new Vector2(880f, 96f);
            }
            var follower = SceneBuilderCore.Ensure<TextEndFollower>(recommendedBadge.gameObject);
            A.Set(follower, "text", recommendedHeader); A.Set(follower, "bubble", bubbleRect);
            follower.Apply();

            GameObject listArea = A.Node(card, "RecommendationsArea");
            A.Between(listArea.transform, listTop + 70, A.AboveBar);        // the note line is gone from under the heading: the list starts higher
            // long rows, one product each: closed they show the kind's icon, the name and the brand; a tap makes the row
            // longer and shows Save / Try On / AI (the user's choice of 12 October 2026: a long list must not overwhelm)
            AppTiles.ListScroll(listArea.transform, "RecommendationsScrollView", out RectTransform content);
            ProductTileView template = AppTiles.ExpandingRow(content, "RecommendationTemplate", AppTiles.TileKind.Dashboard);
            // placeholder rows for the time the list is fetched: a row's panel with bars for the icon, the name and the brand
            // (inactive here: the app switches them on)
            GameObject skeleton = AppTiles.SkeletonRows(listArea.transform, "RecommendationsSkeleton", 5);
            skeleton.SetActive(false);

            TextMeshProUGUI empty = A.Label(listArea.transform, "RecommendationsEmptyText",
                "No make-up products matched your profile yet. Scan a product to check it.",
                32, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.Fill(empty.transform, 60, 0, 60, 0);
            empty.gameObject.SetActive(false);            // the app shows it when there is nothing to recommend

            // ---- "the ingredient rules were updated" (appears by itself; the pill is inactive until then) ----
            GameObject noticeHolder = A.Node(card, "RuleUpdateBanner", typeof(RuleUpdateBanner));
            A.BottomStrip(noticeHolder.transform, A.AboveBar, 104, 28, 28);
            Image noticePill = A.Panel(noticeHolder.transform, "Notice", ThemeRole.Surface, 1f, 104f);
            noticePill.raycastTarget = true;
            A.Fill(noticePill.transform);
            A.Outline(noticePill.transform, ThemeRole.Gold, 104f);
            var noticeGroup = SceneBuilderCore.Ensure<CanvasGroup>(noticePill.gameObject);
            Image noticeIcon = A.Icon(noticePill.transform, "Icon", IconFactory.Shield, 52, ThemeRole.Gold);
            A.At(noticeIcon.transform, A.ML, new Vector2(30, 0), new Vector2(52, 52));
            TextMeshProUGUI noticeText = A.Label(noticePill.transform, "MessageText", "Ingredient safety rules were updated. New scans use them.", 28,
                                                 ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.MidlineLeft, true);
            A.Fill(noticeText.transform, 100, 8, 100, 8);
            A.ShrinkWrapped(noticeText, 22);
            Button noticeClose = A.IconButton(noticePill.transform, "CloseButton", IconFactory.Close, 80, 38, ThemeRole.MainText);
            A.At(noticeClose.transform, A.MR, new Vector2(-12, 0), new Vector2(80, 80));
            var notice = noticeHolder.GetComponent<RuleUpdateBanner>();
            A.Set(notice, "notice", noticeGroup); A.Set(notice, "messageText", noticeText); A.Set(notice, "closeButton", noticeClose);
            noticePill.gameObject.SetActive(false);

            BottomActionBar bar = A.BottomBar(s, 0, 85f);      // the liquid drop is 85 x 85 on Recent Scans and the dashboard (owner, 11 November 2026)
            AppTiles.SwipeNavigation(s, bar);       // drag the dashboard right: Recent Scans comes in; left: AR Try-On (the scanner is opened with its button only)

            // ---- account pop-up (opens from the avatar) ----
            A.AppPopup account = A.Overlay(s, "AccountPanel", "Your account");
            // New22 (20 November 2026): taller, with the photo (tap it to change), the full skin profile and "Edit skin profile"
            A.At(account.box, A.MC, new Vector2(0, 60), new Vector2(880, 1120));
            Image accountAvatar = A.Panel(account.box, "AccountAvatar", ThemeRole.SurfaceAlt, 1f, 150f);
            A.At(accountAvatar.transform, A.TC, new Vector2(0, -146), new Vector2(150, 150));
            A.Icon(accountAvatar.transform, "Icon", IconFactory.User, 84, ThemeRole.MainText);
            accountAvatar.raycastTarget = true;
            Button accountAvatarButton = SceneBuilderCore.Ensure<Button>(accountAvatar.gameObject);
            accountAvatarButton.targetGraphic = accountAvatar;
            TextMeshProUGUI photoHint = A.Label(account.box, "PhotoHintText", "Tap your picture to change it", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(photoHint.transform, 228, 40, 48, 48); A.Shrink(photoHint, 20); A.HintOnly(photoHint);
            TextMeshProUGUI accountName = A.Label(account.box, "AccountNameText", "Your name", 44, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.Center);
            A.TopStrip(accountName.transform, 276, 68, 48, 48); A.Shrink(accountName, 30);
            TextMeshProUGUI accountEmail = A.Label(account.box, "AccountEmailText", "name@email.com", 31, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(accountEmail.transform, 344, 50, 48, 48); A.Shrink(accountEmail, 24);
            TextMeshProUGUI skinHeader = A.Label(account.box, "SkinProfileHeaderText", "Skin profile", 36, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(skinHeader.transform, 418, 50, 60, 60);
            Image skinPanel = A.Panel(account.box, "SkinProfilePanel", ThemeRole.SurfaceAlt, Kit.PanelAlpha, 40f);
            A.TopStrip(skinPanel.transform, 474, 236, 48, 48);
            TextMeshProUGUI skinProfile = A.Label(skinPanel.transform, "SkinProfileText", "<b>Skin type:</b> Combination\n<b>Skin tone:</b> Medium\n<b>Skin concerns:</b> Acne\n<b>Allergens:</b> None",
                                                  29, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(skinProfile.transform, 32, 22, 32, 22);
            A.ShrinkWrapped(skinProfile, 22);
            Button editSkin = A.PillButton(account.box, "EditSkinProfileButton", "Edit skin profile", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Pencil);
            A.TopStrip(editSkin.transform, 730, 120, 48, 48);
            TextMeshProUGUI accountNote = A.Label(account.box, "AccountNoteText", "You can sign back in at any time with your email and password.",
                                                  27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(accountNote.transform, 868, 64, 60, 60);
            Button signOut = A.PillButton(account.box, "SignOutButton", "Log Out", 40, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.SignOut);
            A.BottomStrip(signOut.transform, 44, 120, 48, 48);

            // ---- "Log out?" : asked before anything happens (the same question as on the Settings page) ----
            A.AppPopup signOutPopup = A.Overlay(s, "SignOutConfirmPanel", "Log out?");
            A.At(signOutPopup.box, A.MC, new Vector2(0, 60), new Vector2(900, 600));
            TextMeshProUGUI signOutMessage = A.Label(signOutPopup.box, "MessageText", "You can sign back in at any time with your email and password.",
                                                     32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(signOutMessage.transform, 48, 190, 48, 130);
            A.ShrinkWrapped(signOutMessage, 26);
            Button signOutCancel = A.PillButton(signOutPopup.box, "CancelButton", "Cancel", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(signOutCancel.transform, 44, 108, 48, 462);
            Button signOutOk = A.PillButton(signOutPopup.box, "ConfirmButton", "Log Out", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(signOutOk.transform, 44, 108, 462, 48);
            // ConfirmDialog paints this button itself (the warning colour for logging out)
            Object.DestroyImmediate(signOutOk.GetComponent<ThemedGraphic>());
            Object.DestroyImmediate(A.LabelOf(signOutOk).GetComponent<ThemedGraphic>());
            var signOutConfirm = A.Controller<GlowUp.AppSettings.ConfirmDialog>("DashboardFlowController");       // on the controller object, so it survives rebuilds
            A.Set(signOutConfirm, "panel", signOutPopup.panel); A.Set(signOutConfirm, "titleText", signOutPopup.title); A.Set(signOutConfirm, "messageText", signOutMessage);
            A.Set(signOutConfirm, "confirmButton", signOutOk); A.Set(signOutConfirm, "confirmLabel", A.LabelOf(signOutOk));
            A.Set(signOutConfirm, "cancelButton", signOutCancel); A.Set(signOutConfirm, "cancelLabel", A.LabelOf(signOutCancel));

            AiInsightPanel aiPanel = AppTiles.AiPanel(s);

            // ---- controller ----
            var controller = A.Controller<DashboardFlowController>("DashboardFlowController");
            A.Set(controller, "recommendationsSkeleton", skeleton); A.Set(controller, "aiPanel", aiPanel);
            A.Set(controller, "greetingText", greeting);
            A.Set(controller, "ageValue", age); A.Set(controller, "birthdayValue", birthday);
            A.Set(controller, "skinTypeValue", skinType); A.Set(controller, "skinToneValue", skinTone);
            A.Set(controller, "allergensValue", allergens);
            A.Set(controller, "recommendationsContent", content); A.Set(controller, "recommendationTemplate", template);
            A.Set(controller, "recommendationsEmptyText", empty); A.Set(controller, "typeIcons", A.TypeIcons());
            A.Set(controller, "recommendationsNoteText", recommendedNote); A.Set(controller, "recommendationsBadge", recommendedBadge);
            // reworded for "make-up only" (wording changed in the Inspector is kept)
            SceneBuilderCore.ReplaceText(controller, "msgNone", "No make-up products matched your profile yet. Scan a product to check it.",
                "No recommendations yet. Products that are safe for your skin will appear here as the catalogue grows.");
            SceneBuilderCore.ReplaceText(controller, "msgLoading", "Looking for make-up that matches your profile...", "Looking for products that suit you...");
            A.Set(controller, "avatarButton", avatar); A.Set(controller, "accountPanel", account.panel);
            A.Set(controller, "accountNameText", accountName); A.Set(controller, "accountEmailText", accountEmail);
            A.Set(controller, "signOutButton", signOut);
            A.Set(controller, "accountAvatarButton", accountAvatarButton);
            A.Set(controller, "skinProfileText", skinProfile);
            A.Set(controller, "editSkinProfileButton", editSkin);
            A.Set(controller, "signOutConfirm", signOutConfirm);
            A.Set(controller, "settingsButton", settings);

            // What is FETCHED on this screen: the five values of the summary (bars while the profile is fetched and nothing is
            // on the phone yet) and the rows under "Recommended for you" (their own placeholder rows, RecommendationsSkeleton).
            // The greeting, the titles, the labels "Age", "Birthday" ..., the buttons and the bottom bar are never bars.
            AppTiles.WordSkeleton(s, AppTiles.TextsOf(controller, "ageValue", "birthdayValue", "skinTypeValue", "skinToneValue", "allergensValue"));

            A.Finish(s);
            Debug.Log("[GlowUp] UserDashboardScene built. RecommendationTemplate is the pattern row (left open in the Hierarchy so its buttons can be styled; the app closes every row); RecommendationsSkeleton and AiInsightPanel are inactive until needed; AccountPanel is inactive until the avatar is tapped. The gear opens the Settings page.");
        }

        /// <summary>One tile of the quick summary: a small caption and the value under it. Two tiles per line.</summary>
        static TextMeshProUGUI SummaryTile(Transform panel, string key, string caption, string sample, int column, int row)
        {
            Image tile = A.Panel(panel, key + "Tile", ThemeRole.InputBackground, 1f, 40f);
            var rt = (RectTransform)tile.transform;
            float top = 24 + row * TileStep;
            rt.anchorMin = new Vector2(column * 0.5f, 1); rt.anchorMax = new Vector2(column * 0.5f + 0.5f, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(column == 0 ? 24 : 10, -top - TileHeight); rt.offsetMax = new Vector2(column == 0 ? -10 : -24, -top);

            TextMeshProUGUI captionText = A.Label(tile.transform, "Caption", caption, 27, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(captionText.transform, 14, 40, 30, 20);
            TextMeshProUGUI value = A.Label(tile.transform, key + "Value", sample, 40, ThemeRole.MainText, FontRole.Heading);
            A.TopStrip(value.transform, 54, 62, 30, 20);
            A.Shrink(value, 26);
            return value;
        }
    }
}
