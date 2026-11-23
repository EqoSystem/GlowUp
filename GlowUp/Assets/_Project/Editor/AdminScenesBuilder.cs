using GlowUp.Admin;
using GlowUp.Common;
using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Staff capture companion (plan "Mobile Admin UI & Real-Time Website Integration", 23 November 2026).
    /// Menu: GlowUp > Build Scene > 16 Staff Access (all four scenes) and the "Admin?" row of the Login scene (built by
    /// LoginSceneBuilder through AddLoginAccordion). Every object is a scene object, editable in the Hierarchy; the
    /// controllers keep their Inspector settings across rebuilds.
    /// </summary>
    public static class AdminScenesBuilder
    {
        const float P = A.Pad;

        [MenuItem("GlowUp/Build Scene/16 Staff Access (all four scenes)", false, 126)]
        public static void BuildAll()
        {
            BuildStaffLogin();
            BuildDashboard();
            BuildCapture();
            BuildSubmissions();
            BuildSettings();
            BuildScannerPrefs();
            Debug.Log("[GlowUp] Staff scenes built: AdminLoginScene, AdminDashboardScene, AdminCaptureScene, AdminSubmissionsScene, AdminSettingsScene, AdminScannerPrefsScene.");
        }

        // =====================================================================================
        //  Login: the "Admin?" row (closed by default) with ONE gold Staff / Admin Access button
        // =====================================================================================
        const float ToggleBottom = 142;          // above the legal line (56 + 70 high) with a gap

        public static void AddLoginAccordion(Transform card, float y)
        {
            Transform old = Kit.Loose(card.root, "AdminAccordion");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform oldController = Kit.Loose(card.root, "AdminAccessAccordion");
            if (oldController != null && oldController.GetComponent<Canvas>() == null && oldController.parent == null) Object.DestroyImmediate(oldController.gameObject);

            // 23 Nov 2026 (owner's mark-up): the row sits at the BOTTOM of the card, just above the legal line, and opens UPWARDS into the
            // free space under the Google button. Everything is anchored to the card bottom, so it follows any screen height.
            GameObject root = A.Node(card, "AdminAccordion");
            A.Fill(root.transform, Kit.PadX, 0, Kit.PadX, 0);

            Button toggle = A.LinkButton(root.transform, "AdminToggle", "Admin?", 32, ThemeRole.SecondaryText);
            A.BottomStrip(toggle.transform, ToggleBottom, 84, 0, 0);
            TextMeshProUGUI toggleLabel = A.LabelOf(toggle);
            toggleLabel.text = "Admin?";
            A.Fill(toggleLabel.transform, 0, 0, 0, 0);
            toggleLabel.alignment = TextAlignmentOptions.Center;
            Image chevron = A.Icon(toggle.transform, "Chevron", IconFactory.ChevronDown, 40, ThemeRole.SecondaryText);
            A.At(chevron.transform, A.MC, new Vector2(78, 0), new Vector2(40, 40));

            GameObject panelObject = A.Node(root.transform, "StaffAccessPanel", typeof(CanvasGroup));
            A.BottomStrip(panelObject.transform, ToggleBottom + 84 + 8, 112, 0, 0);
            ((RectTransform)panelObject.transform).pivot = new Vector2(0.5f, 0f);          // grows upwards from the row
            Button staff = A.PillButton(panelObject.transform, "StaffAccessButton", "Staff / Admin Access", 36, ThemeRole.Gold, ThemeRole.Background, IconFactory.Scan);
            A.Fill(staff.transform, 0, 0, 0, 0);
            panelObject.SetActive(false);

            var accordion = SceneBuilderCore.Ensure<AdminAccessAccordion>(root);
            A.Set(accordion, "toggleButton", toggle); A.Set(accordion, "toggleLabel", toggleLabel);
            A.Set(accordion, "chevron", chevron.rectTransform); A.Set(accordion, "panel", panelObject.GetComponent<CanvasGroup>());
            A.Set(accordion, "staffButton", staff);
        }

        // =====================================================================================
        //  1. Staff sign-in
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/16a Staff Sign-In", false, 127)]
        public static void BuildStaffLogin()
        {
            A.AppScreen s = A.Begin("AdminLoginScene", "", true, "AdminLoginCard");
            Transform card = s.card;
            float x = Kit.PadX;

            TextMeshProUGUI logo = A.Label(card, "LogoText", "Glow Up", 112, ThemeRole.Gold, FontRole.Title, TextAlignmentOptions.Center);
            A.TopStrip(logo.transform, 150, 150, x, x);
            TextMeshProUGUI heading = A.Label(card, "StaffAccessTitle", "Staff Access", 50, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.Center);
            A.TopStrip(heading.transform, 306, 70, x, x);
            TextMeshProUGUI note = A.Label(card, "AuthorizedNote", "Authorized personnel only", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(note.transform, 378, 46, x, x);

            TextMeshProUGUI emailLabel = A.Label(card, "EmailText", "Staff email:", 36, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.BottomLeft);
            A.TopStrip(emailLabel.transform, 470, 54, x + 12, x);
            TMP_InputField email = Field(card, "EmailInputField", "Enter your staff email", 532, x, TMP_InputField.ContentType.EmailAddress);
            TextMeshProUGUI pwLabel = A.Label(card, "PasswordText", "Password:", 36, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.BottomLeft);
            A.TopStrip(pwLabel.transform, 696, 54, x + 12, x);
            TMP_InputField pw = Field(card, "PasswordInputField", "Enter your password", 758, x, TMP_InputField.ContentType.Password);
            Kit.AddEye(pw);
            Button forgot = A.LinkButton(card, "ForgotPasswordButton", "Forgot Password?", 32, ThemeRole.SecondaryText);
            A.TopStrip(forgot.transform, 904, 84, x, 500);
            A.LabelOf(forgot).alignment = TextAlignmentOptions.MidlineLeft;

            Button signIn = A.PillButton(card, "SignInButton", "Sign In", 40, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.TopStrip(signIn.transform, 1110, Kit.ButtonHeight, x, x);
            Button customer = A.LinkButton(card, "CustomerLoginButton", "Back to customer login", 32, ThemeRole.AccentText);
            A.BottomStrip(customer.transform, 60, 84, x, x);

            var c = A.Controller<AdminLoginController>("AdminLoginController");
            A.Set(c, "backButton", s.back); A.Set(c, "emailInput", email); A.Set(c, "passwordInput", pw);
            A.Set(c, "signInButton", signIn); A.Set(c, "signInLabel", A.LabelOf(signIn)); A.Set(c, "forgotButton", forgot);
            A.Set(c, "customerLoginButton", customer);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        // =====================================================================================
        //  2. Scanner dashboard ("Scan Product", from the owner's mock-up of 23 Nov 2026)
        // =====================================================================================
        const float SwitchWidth = 116, SwitchHeight = 64;

        [MenuItem("GlowUp/Build Scene/16b Staff Dashboard", false, 128)]
        public static void BuildDashboard()
        {
            A.AppScreen s = A.Begin("AdminDashboardScene", "", false, "AdminDashboardCard");
            Transform card = s.card;

            // ---- header: Glow Up | Admin Scanner, settings on the right ----
            TextMeshProUGUI logo = A.Label(card, "LogoText", "Glow Up", 64, ThemeRole.Gold, FontRole.Title, TextAlignmentOptions.MidlineLeft);
            A.At(logo.transform, A.TL, new Vector2(56, -40), new Vector2(300, 92));
            A.Shrink(logo, 44);
            Image divider = A.Panel(card, "HeaderDivider", ThemeRole.Gold, 0.6f, 2f);
            divider.sprite = IconFactory.Square; divider.type = Image.Type.Simple;
            A.At(divider.transform, A.TL, new Vector2(372, -60), new Vector2(3, 52));
            TextMeshProUGUI adminLabel = A.Label(card, "AdminScannerText", "Admin Scanner", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.At(adminLabel.transform, A.TL, new Vector2(396, -40), new Vector2(380, 92));
            A.Shrink(adminLabel, 22);
            Button settings = A.IconButton(card, "SettingsButton", IconFactory.Gear, 92, 52, ThemeRole.AccentText, ThemeRole.SurfaceAlt);
            A.At(settings.transform, A.TR, new Vector2(-36, -38), new Vector2(92, 92));

            TextMeshProUGUI title = A.Label(card, "ScreenTitle", "Scan Product", 64, ThemeRole.Heading, FontRole.Title, TextAlignmentOptions.Center);
            A.TopStrip(title.transform, 160, 84, 56, 56); A.Shrink(title, 44);
            TextMeshProUGUI sub = A.Label(card, "SubtitleText", "Align the barcode or take a photo", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(sub.transform, 246, 48, 56, 56); A.Shrink(sub, 22);

            // ---- the scanner preview: tapping it starts a barcode scan ----
            GameObject viewportObject = A.Node(card, "ScannerPreview", typeof(Image), typeof(Button));
            Image viewport = viewportObject.GetComponent<Image>();
            Kit.StyleRounded(viewport); Kit.SetRadius(viewport, 48f); viewport.color = new Color32(46, 36, 36, 255);
            viewportObject.GetComponent<Button>().targetGraphic = viewport;
            viewportObject.GetComponent<Button>().transition = Selectable.Transition.None;
            A.TopStrip(viewportObject.transform, 316, 520, 56, 56);
            GameObject frame = A.Node(viewportObject.transform, "GuideFrame", typeof(Image));
            A.At(frame.transform, A.MC, new Vector2(0, 22), new Vector2(500, 320));
            Image frameImage = frame.GetComponent<Image>(); frameImage.sprite = IconFactory.ScanFrame; frameImage.type = Image.Type.Simple; frameImage.color = new Color(1, 1, 1, 0.92f); frameImage.raycastTarget = false;
            Image laser = A.RawPanel(frame.transform, "ScanLine", new Color(0.95f, 0.36f, 0.58f, 0.95f), 12f);
            laser.sprite = IconFactory.ScanLine; laser.type = Image.Type.Simple; laser.raycastTarget = false;
            A.At(laser.transform, A.MC, Vector2.zero, new Vector2(440, 22));
            var sweep = SceneBuilderCore.Ensure<ScanLineSweep>(frame);
            A.Set(sweep, "line", laser.rectTransform); A.Set(sweep, "area", (RectTransform)frame.transform);

            Button flashChip = A.PillButton(viewportObject.transform, "FlashChip", "Flash Off", 26, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Bolt, 0.9f);
            A.At(flashChip.transform, A.TL, new Vector2(24, -24), new Vector2(210, 64));
            A.Shrink(A.LabelOf(flashChip), 20);
            Image focusChip = A.RawPanel(viewportObject.transform, "AutoFocusChip", new Color(0, 0, 0, 0.45f), 40f);
            A.At(focusChip.transform, A.BC, new Vector2(0, 24), new Vector2(280, 60));
            focusChip.raycastTarget = false;
            TextMeshProUGUI focusLabel = A.RawLabel(focusChip.transform, "Label", "Auto Focus", 26, Color.white, FontRole.Label, TextAlignmentOptions.Center);
            A.Fill(focusLabel.transform, 12, 0, 12, 0); A.Shrink(focusLabel, 18);

            // ---- the four ways in: Gallery, Scan, Camera, Manual ----
            float rowTop = 874;
            Button gallery = ModeButton(card, "GalleryButton", IconFactory.Document, "Gallery", 0, rowTop, false);
            Button scan = ModeButton(card, "ScanButton", IconFactory.Scan, "Scan", 1, rowTop, true);
            Button capture = ModeButton(card, "CameraButton", IconFactory.CameraIcon, "Camera", 2, rowTop, false);
            Button manual = ModeButton(card, "ManualButton", IconFactory.Pencil, "Manual", 3, rowTop, false);

            // ---- View Tips ----
            GameObject tipsObject = A.Node(card, "ViewTipsButton", typeof(Image), typeof(Button));
            Image tipsBack = tipsObject.GetComponent<Image>(); Kit.StyleRounded(tipsBack); Kit.SetRadius(tipsBack, 60f); tipsBack.color = Color.white;
            SceneBuilderCore.Themed(tipsObject, ThemeRole.SurfaceAlt);
            tipsObject.GetComponent<Button>().targetGraphic = tipsBack;
            A.TopStrip(tipsObject.transform, 1094, 112, 56, 56);
            Image tipsIcon = A.IconTile(tipsObject.transform, "BulbTile", IconFactory.Bulb, 72, 40, ThemeRole.Background, ThemeRole.AccentText);
            A.At(tipsIcon.transform.parent, A.ML, new Vector2(26, 0), new Vector2(72, 72));
            TextMeshProUGUI tipsTitle = A.Label(tipsObject.transform, "TipsTitle", "View Tips", 34, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.Fill(tipsTitle.transform, 122, 54, 96, 6); A.Shrink(tipsTitle, 24);
            TextMeshProUGUI tipsCaption = A.Label(tipsObject.transform, "TipsCaption", "Get the best scan result", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.Fill(tipsCaption.transform, 122, 6, 96, 54); A.Shrink(tipsCaption, 19);
            Image tipsChevron = A.Icon(tipsObject.transform, "Chevron", IconFactory.ChevronRight, 40, ThemeRole.SecondaryText);
            A.At(tipsChevron.transform, A.MR, new Vector2(-28, 0), new Vector2(40, 40));

            // two small buttons share one row: the unsent single capture (left) and the batch waiting to be sent (right)
            Button resume = A.PillButton(card, "ResumeButton", "Unsent capture", 26, ThemeRole.Background, ThemeRole.AccentText, IconFactory.History);
            A.At(resume.transform, A.TL, new Vector2(56, -1220), new Vector2(436, 74));
            A.Shrink(A.LabelOf(resume), 18);
            resume.gameObject.SetActive(false);
            Button batchBtn = A.PillButton(card, "BatchButton", "Batch waiting", 26, ThemeRole.Background, ThemeRole.AccentText, IconFactory.ListIcon);
            A.At(batchBtn.transform, A.TL, new Vector2(508, -1220), new Vector2(436, 74));
            A.Shrink(A.LabelOf(batchBtn), 18);
            batchBtn.gameObject.SetActive(false);

            // ---- recent submissions (live) ----
            TextMeshProUGUI recentTitle = A.Label(card, "RecentTitle", "Recent Submissions", 36, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(recentTitle.transform, 1312, 56, 56, 290); A.Shrink(recentTitle, 26);
            Button viewAll = A.LinkButton(card, "ViewAllButton", "View all", 30, ThemeRole.AccentText);
            A.At(viewAll.transform, A.TR, new Vector2(-48, -1308), new Vector2(220, 64));
            GameObject list = A.Node(card, "RecentList");
            A.Between(list.transform, 1376, 170);
            list.AddComponent<RectMask2D>();
            A.VStack(list, 8);
            GameObject template = Row(list.transform, "RowTemplate", 96);
            TextMeshProUGUI empty = A.Label(list.transform, "EmptyText", "Loading your submissions...", 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.Size(empty, height: 96);

            // ---- the live status bar: Online | Total Scans | Synced ----
            GameObject bar = A.Node(card, "StatusBar", typeof(Image), typeof(Button));
            Image barBack = bar.GetComponent<Image>(); Kit.StyleRounded(barBack); Kit.SetRadius(barBack, 60f); barBack.color = Color.white;
            SceneBuilderCore.Themed(bar, ThemeRole.SurfaceAlt);
            bar.GetComponent<Button>().targetGraphic = barBack;
            A.BottomStrip(bar.transform, 34, 110, 56, 56);
            RectTransform left = A.Share(A.Node(bar.transform, "OnlinePart").transform, 0f, 0.27f);
            RectTransform mid = A.Share(A.Node(bar.transform, "ScansPart").transform, 0.27f, 0.68f);
            RectTransform right = A.Share(A.Node(bar.transform, "SyncPart").transform, 0.68f, 1f);
            Image dot = A.RawPanel(left, "OnlineDot", new Color(0.20f, 0.62f, 0.42f), 12f);
            A.At(dot.transform, A.ML, new Vector2(26, 0), new Vector2(18, 18));
            TextMeshProUGUI online = A.Label(left, "OnlineText", "Online", 26, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft);
            A.Fill(online.transform, 54, 0, 6, 0); A.Shrink(online, 18);
            Image layers = A.Icon(mid, "ScansIcon", IconFactory.ListIcon, 34, ThemeRole.AccentText);
            A.At(layers.transform, A.ML, new Vector2(14, 0), new Vector2(34, 34));
            TextMeshProUGUI total = A.Label(mid, "TotalScansText", "Total Scans: 0", 26, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft);
            A.Fill(total.transform, 62, 0, 6, 0); A.Shrink(total, 18);
            Image syncIcon = A.Icon(right, "SyncIcon", IconFactory.Sync, 34, ThemeRole.AccentText);
            A.At(syncIcon.transform, A.ML, new Vector2(14, 0), new Vector2(34, 34));
            TextMeshProUGUI syncWord = A.Label(right, "SyncText", "Synced", 26, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.MidlineLeft);
            A.Fill(syncWord.transform, 58, 0, 10, 0); A.Shrink(syncWord, 18);
            foreach (float at in new[] { 0.27f, 0.68f })
            {
                Image line = A.Panel(bar.transform, "Divider", ThemeRole.Gold, 0.5f, 2f);
                line.sprite = IconFactory.Square; line.type = Image.Type.Simple; line.raycastTarget = false;
                RectTransform lrt = line.rectTransform; lrt.anchorMin = new Vector2(at, 0.24f); lrt.anchorMax = new Vector2(at, 0.76f); lrt.sizeDelta = new Vector2(2, 0); lrt.anchoredPosition = Vector2.zero;
            }

            var tips = TipsPopup(s, "TipsPanel");
            var confirm = ConfirmPopup(s, "ConfirmPanel");
            var c = A.Controller<AdminDashboardController>("AdminDashboardController");
            A.Set(c, "settingsButton", settings); A.Set(c, "confirmDialog", confirm);
            A.Set(c, "previewButton", viewportObject.GetComponent<Button>()); A.Set(c, "flashChip", flashChip); A.Set(c, "flashChipLabel", A.LabelOf(flashChip));
            A.Set(c, "autoFocusChipLabel", focusLabel); A.Set(c, "guideFrame", (RectTransform)frame.transform);
            A.Set(c, "scanButton", scan); A.Set(c, "captureButton", capture); A.Set(c, "galleryButton", gallery); A.Set(c, "manualButton", manual);
            A.Set(c, "resumeButton", resume); A.Set(c, "resumeText", A.LabelOf(resume)); A.Set(c, "batchButton", batchBtn); A.Set(c, "batchText", A.LabelOf(batchBtn));
            A.Set(c, "tipsButton", tipsObject.GetComponent<Button>()); A.Set(c, "tipsPanel", tips);
            A.Set(c, "statusBar", bar.GetComponent<Button>()); A.Set(c, "onlineText", online); A.Set(c, "onlineDot", dot);
            A.Set(c, "totalScansText", total); A.Set(c, "syncText", syncWord);
            A.Set(c, "recentContent", (RectTransform)list.transform); A.Set(c, "rowTemplate", template); A.Set(c, "emptyText", empty);
            A.Set(c, "viewAllButton", viewAll);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        /// <summary>One of the four round entry buttons with its caption under it; the middle one (Scan) is larger and filled.</summary>
        static Button ModeButton(Transform parent, string name, Sprite icon, string label, int index, float top, bool primary)
        {
            float column = (1000f - 2 * 56f) / 4f, cx = 56f + column * (index + 0.5f);
            float size = primary ? 150 : 124;
            Button b = primary ? A.IconButton(parent, name, icon, size, 72, ThemeRole.ButtonText, ThemeRole.ButtonFill)
                               : A.IconButton(parent, name, icon, size, 58, ThemeRole.AccentText, ThemeRole.SurfaceAlt);
            A.At(b.transform, A.TL, new Vector2(cx - size / 2f, -(top + (150f - size) / 2f)), new Vector2(size, size));
            TextMeshProUGUI caption = A.Label(parent, name.Replace("Button", "Caption"), label, 28, primary ? ThemeRole.AccentText : ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.Center);
            A.At(caption.transform, A.TL, new Vector2(cx - column / 2f, -(top + 160)), new Vector2(column, 46));
            A.Shrink(caption, 20);
            return b;
        }

        // =====================================================================================
        //  Scan Tips (a pop-up on the dashboard and on the capture screen)
        // =====================================================================================
        static OverlayPanel TipsPopup(A.AppScreen s, string name)
        {
            A.AppPopup p = A.Overlay(s, name, "Scan Tips");
            A.At(p.box, A.MC, new Vector2(0, 40), new Vector2(940, 1090));
            string[] titles = { "Avoid glare", "Keep it steady", "Use good lighting", "Include the whole item" };
            string[] texts = { "Tilt the product to reduce reflections.", "Hold your phone still while scanning.", "Make sure the product is well lit.", "Keep the entire product inside the frame." };
            Sprite[] icons = { IconFactory.Sparkle, IconFactory.CameraIcon, IconFactory.Sun, IconFactory.Scan };
            for (int i = 0; i < 4; i++)
            {
                float y = 140 + i * 196;
                Image tile = A.IconTile(p.box, "TipIcon" + (i + 1), icons[i], 112, 58, ThemeRole.SurfaceAlt, ThemeRole.AccentText);
                A.At(tile.transform.parent, A.TL, new Vector2(48, -y), new Vector2(112, 112));
                TextMeshProUGUI t = A.Label(p.box, "TipTitle" + (i + 1), titles[i], 36, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
                A.TopStrip(t.transform, y, 56, 190, 48); A.Shrink(t, 26);
                TextMeshProUGUI d = A.Label(p.box, "TipText" + (i + 1), texts[i], 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
                A.TopStrip(d.transform, y + 58, 60, 190, 48); A.ShrinkWrapped(d, 20);
            }
            Button gotIt = A.PillButton(p.box, "GotItButton", "Got it", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(gotIt.transform, 44, 116, 48, 48);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(gotIt.onClick, p.panel.Hide);
            return p.panel;
        }

        // =====================================================================================
        //  Settings + Scanner Preferences (from the owner's mock-up)
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/16e Staff Settings", false, 131)]
        public static void BuildSettings()
        {
            A.AppScreen s = A.Begin("AdminSettingsScene", "Settings", true, "AdminSettingsCard");
            Transform card = s.card;

            Image account = A.Panel(card, "AccountCard", ThemeRole.SurfaceAlt, 1f, 56f);
            A.TopStrip(account.transform, A.HeaderBottom, 150, 56, 56);
            Image avatar = A.IconTile(account.transform, "Avatar", IconFactory.User, 96, 52, ThemeRole.Background, ThemeRole.AccentText);
            A.At(avatar.transform.parent, A.ML, new Vector2(28, 0), new Vector2(96, 96));
            TextMeshProUGUI accountName = A.Label(account.transform, "AccountNameText", "Admin", 38, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.Fill(accountName.transform, 150, 78, 28, 12); A.Shrink(accountName, 26);
            TextMeshProUGUI accountEmail = A.Label(account.transform, "AccountEmailText", "staff@example.com", 27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.Fill(accountEmail.transform, 150, 14, 28, 72); A.Shrink(accountEmail, 18);

            float y = A.HeaderBottom + 190;
            Button prefs = SettingsRow(card, "ScannerPreferencesRow", IconFactory.Gauge, "Scanner Preferences", "Flash, Auto Focus, Scan Line", y, true, out _); y += 138;
            Button subs = SettingsRow(card, "SubmissionsRow", IconFactory.ListIcon, "Submissions", "Your captures and their review status", y, true, out _); y += 138;
            Button web = SettingsRow(card, "WebsiteRow", IconFactory.ExternalLink, "Open Admin Website", "Brands, ingredients and approvals", y, true, out _); y += 138;
            Button about = SettingsRow(card, "AboutRow", IconFactory.Info, "About GlowUp", "Version", y, false, out TextMeshProUGUI aboutCaption);

            Button signOut = A.PillButton(card, "SignOutButton", "Sign Out", 36, ThemeRole.SurfaceAlt, ThemeRole.AccentText, IconFactory.SignOut);
            A.BottomStrip(signOut.transform, 44, 112, 56, 56);

            var confirm = ConfirmPopup(s, "ConfirmPanel");
            var c = A.Controller<AdminSettingsController>("AdminSettingsController");
            A.Set(c, "backButton", s.back); A.Set(c, "accountNameText", accountName); A.Set(c, "accountEmailText", accountEmail);
            A.Set(c, "prefsButton", prefs); A.Set(c, "submissionsButton", subs); A.Set(c, "websiteButton", web); A.Set(c, "aboutButton", about);
            A.Set(c, "signOutButton", signOut); A.Set(c, "aboutCaption", aboutCaption); A.Set(c, "confirmDialog", confirm);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        /// <summary>A settings row: icon tile, title, caption and (when it opens something) a chevron. The whole row is the button.</summary>
        static Button SettingsRow(Transform card, string name, Sprite icon, string title, string caption, float y, bool chevron, out TextMeshProUGUI captionText)
        {
            GameObject go = A.Node(card, name, typeof(Image), typeof(Button));
            Image back = go.GetComponent<Image>(); Kit.StyleRounded(back); Kit.SetRadius(back, 56f); back.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.SurfaceAlt);
            go.GetComponent<Button>().targetGraphic = back;
            A.TopStrip(go.transform, y, 120, 56, 56);
            Image tile = A.IconTile(go.transform, "IconTile", icon, 76, 42, ThemeRole.Background, ThemeRole.AccentText);
            A.At(tile.transform.parent, A.ML, new Vector2(26, 0), new Vector2(76, 76));
            TextMeshProUGUI t = A.Label(go.transform, "TitleText", title, 34, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.Fill(t.transform, 124, 58, chevron ? 90 : 28, 8); A.Shrink(t, 24);
            captionText = A.Label(go.transform, "CaptionText", caption, 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.Fill(captionText.transform, 124, 8, chevron ? 90 : 28, 62); A.Shrink(captionText, 18);
            if (chevron)
            {
                Image ch = A.Icon(go.transform, "Chevron", IconFactory.ChevronRight, 40, ThemeRole.SecondaryText);
                A.At(ch.transform, A.MR, new Vector2(-28, 0), new Vector2(40, 40));
            }
            return go.GetComponent<Button>();
        }

        [MenuItem("GlowUp/Build Scene/16f Staff Scanner Preferences", false, 132)]
        public static void BuildScannerPrefs()
        {
            A.AppScreen s = A.Begin("AdminScannerPrefsScene", "Scanner Preferences", true, "AdminScannerPrefsCard");
            Transform card = s.card;
            float y = A.HeaderBottom + 10;
            Toggle flash = PrefRow(card, "FlashRow", IconFactory.Bolt, "Flash", "Turn on flash for darker areas", y, true); y += 134;
            Toggle focus = PrefRow(card, "AutoFocusRow", IconFactory.Scan, "Auto Focus", "Automatically focus on the barcode", y, true); y += 134;
            PrefInfoRow(card, "ScanModeRow", IconFactory.ListIcon, "Scan Mode", "Barcode & QR Code", y); y += 134;
            Toggle line = PrefRow(card, "ScanLineRow", IconFactory.Minus, "Scan Line", "Show the scanning guide line", y, true); y += 134;
            Toggle batch = PrefRow(card, "BatchModeRow", IconFactory.ListIcon, "Batch Mode", "Keep scanning, or add many gallery photos", y, true); y += 134 + 36;

            TextMeshProUGUI photoTitle = A.Label(card, "PhotoOptionsTitle", "Photo Options", 32, ThemeRole.SecondaryText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(photoTitle.transform, y, 52, 70, 56); y += 62;
            GameObject qualityRow = PrefRowShell(card, "ImageQualityRow", IconFactory.CameraIcon, "Image Quality", "Size of the copies sent to the reviewer", y, true);
            Button quality = qualityRow.GetComponent<Button>();
            TextMeshProUGUI qualityValue = A.Label(qualityRow.transform, "ValueText", "High Quality", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineRight);
            A.At(qualityRow.transform.Find("Chevron"), A.MR, new Vector2(-26, 0), new Vector2(40, 40));
            A.At(qualityValue.transform, A.MR, new Vector2(-76, 0), new Vector2(220, 52));
            A.Shrink(qualityValue, 18);
            y += 134;
            Toggle guide = PrefRow(card, "GuideFrameRow", IconFactory.ScanFrame, "Show Guide Frame", "Show the corner frame on the camera", y, true);

            var c = A.Controller<AdminScannerPrefsController>("AdminScannerPrefsController");
            A.Set(c, "backButton", s.back); A.Set(c, "flashToggle", flash); A.Set(c, "autoFocusToggle", focus);
            A.Set(c, "scanLineToggle", line); A.Set(c, "guideFrameToggle", guide); A.Set(c, "batchToggle", batch);
            A.Set(c, "qualityButton", quality); A.Set(c, "qualityValue", qualityValue);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        /// <summary>The shared shell of a preferences row (icon tile, title, caption). With `chevron` it is a button that opens or cycles something.</summary>
        static GameObject PrefRowShell(Transform card, string name, Sprite icon, string title, string caption, float y, bool chevron)
        {
            GameObject go = A.Node(card, name, typeof(Image), typeof(Button));
            Image back = go.GetComponent<Image>(); Kit.StyleRounded(back); Kit.SetRadius(back, 56f); back.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.SurfaceAlt);
            go.GetComponent<Button>().targetGraphic = back;
            A.TopStrip(go.transform, y, 120, 56, 56);
            Image tile = A.IconTile(go.transform, "IconTile", icon, 76, 42, ThemeRole.Background, ThemeRole.AccentText);
            A.At(tile.transform.parent, A.ML, new Vector2(26, 0), new Vector2(76, 76));
            TextMeshProUGUI t = A.Label(go.transform, "TitleText", title, 33, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.MidlineLeft);
            A.Fill(t.transform, 124, 58, 170, 8); A.Shrink(t, 22);
            TextMeshProUGUI cap = A.Label(go.transform, "CaptionText", caption, 25, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.Fill(cap.transform, 124, 8, 170, 62); A.Shrink(cap, 17);
            if (chevron)
            {
                Image ch = A.Icon(go.transform, "Chevron", IconFactory.ChevronRight, 40, ThemeRole.SecondaryText);
                A.At(ch.transform, A.MR, new Vector2(-28, 0), new Vector2(40, 40));
            }
            return go;
        }

        /// <summary>A row with an on/off switch on the right (the whole row toggles it).</summary>
        static Toggle PrefRow(Transform card, string name, Sprite icon, string title, string caption, float y, bool unused)
        {
            GameObject row = PrefRowShell(card, name, icon, title, caption, y, false);
            Object.DestroyImmediate(row.GetComponent<Button>());                           // the switch itself is the control
            Toggle t = Switch(row.transform, "Switch", true);
            A.At(t.transform, A.MR, new Vector2(-28, 0), new Vector2(SwitchWidth, SwitchHeight));
            return t;
        }

        /// <summary>A row that only shows a value (no control).</summary>
        static void PrefInfoRow(Transform card, string name, Sprite icon, string title, string caption, float y)
        {
            GameObject row = PrefRowShell(card, name, icon, title, caption, y, false);
            Object.DestroyImmediate(row.GetComponent<Button>());
            Image lock_ = A.Icon(row.transform, "Info", IconFactory.Check, 40, ThemeRole.AccentText);
            A.At(lock_.transform, A.MR, new Vector2(-34, 0), new Vector2(40, 40));
        }

        /// <summary>An on/off switch: a round track with a knob that slides. Its animation is on its ToggleSwitch.</summary>
        static Toggle Switch(Transform parent, string name, bool on)
        {
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Toggle), typeof(GlowUp.AppSettings.ToggleSwitch));
            A.Size(go.transform, SwitchWidth, SwitchHeight);
            Image hit = go.GetComponent<Image>();
            Kit.StyleRounded(hit); hit.color = new Color(1, 1, 1, 0.004f);
            Image track = A.RawPanel(go.transform, "Track", new Color32(196, 180, 181, 255), SwitchHeight);
            A.Fill(track.transform, 0, 4, 0, 4);
            Image knob = A.RawPanel(track.transform, "Knob", new Color32(250, 247, 242, 255), 46f);
            A.At(knob.transform, A.MC, new Vector2(on ? 28 : -28, 0), new Vector2(44, 44));
            Toggle toggle = go.GetComponent<Toggle>();
            toggle.transition = Selectable.Transition.None; toggle.targetGraphic = track; toggle.graphic = null; toggle.isOn = on;
            var view = go.GetComponent<GlowUp.AppSettings.ToggleSwitch>();
            A.Set(view, "track", track); A.Set(view, "knob", knob.rectTransform); A.Set(view, "knobImage", knob); A.Set(view, "knobTravel", 28f);
            return toggle;
        }

        /// <summary>A submission row: name and brand on the left, the status pill and the time on the right (filled by SubmissionRows).</summary>
        static GameObject Row(Transform parent, string name, float height)
        {
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Button));
            Image img = go.GetComponent<Image>(); Kit.StyleRounded(img); img.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.Background);
            go.GetComponent<Button>().targetGraphic = img;
            A.Size(img, height: height);
            TextMeshProUGUI n = A.Label(go.transform, "NameText", "Luminous Skin Tint", 30, ThemeRole.MainText, FontRole.Heading);
            A.Fill(n.transform, 30, height * 0.48f, 300, 6); A.Shrink(n, 22);
            TextMeshProUGUI b = A.Label(go.transform, "BrandText", "Brand", 25, ThemeRole.SecondaryText, FontRole.Body);
            A.Fill(b.transform, 30, 6, 300, height * 0.52f); A.Shrink(b, 20);
            Image pill = A.RawPanel(go.transform, "StatusPill", new Color(0.80f, 0.50f, 0.58f, 0.16f), 24f);
            A.At(pill.transform, A.TR, new Vector2(-22, -10), new Vector2(262, 44));
            TextMeshProUGUI st = A.RawLabel(pill.transform, "StatusText", "Pending review", 24, new Color(0.80f, 0.50f, 0.58f), FontRole.Label, TextAlignmentOptions.Center);
            A.Fill(st.transform, 8, 0, 8, 0); A.Shrink(st, 18);
            TextMeshProUGUI time = A.Label(go.transform, "TimeText", "2h ago", 23, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineRight);
            A.At(time.transform, A.BR, new Vector2(-26, 8), new Vector2(240, 38));
            return go;
        }

        // =====================================================================================
        //  3. Capture: camera / photos / gallery, then the review form
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/16c Staff Capture", false, 129)]
        public static void BuildCapture()
        {
            A.AppScreen s = A.Begin("AdminCaptureScene", "Scan Barcode", true, "AdminCaptureCard");
            Transform card = s.card;
            Button tipsButton = A.IconButton(card, "TipsButton", IconFactory.Bulb, 92, 52, ThemeRole.AccentText, ThemeRole.SurfaceAlt);
            A.At(tipsButton.transform, A.TR, new Vector2(-28, -28), new Vector2(92, 92));

            // ---------------- capture panel ----------------
            GameObject cap = A.Node(card, "CapturePanel");
            A.Fill(cap.transform);
            Image viewport = A.RawPanel(cap.transform, "CameraViewport", new Color32(30, 26, 25, 255), 48f);
            A.TopStrip(viewport.transform, A.HeaderBottom, 860);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            GameObject mirror = A.Node(viewport.transform, "MirrorRoot");
            A.Fill(mirror.transform);
            GameObject displayObject = A.Node(mirror.transform, "CameraDisplay", typeof(RawImage));
            A.Fill(displayObject.transform);
            var display = displayObject.GetComponent<RawImage>();
            display.color = new Color(0, 0, 0, 0); display.raycastTarget = false;
            GameObject previewObject = A.Node(viewport.transform, "GalleryPreview", typeof(RawImage));
            A.Fill(previewObject.transform, 30, 150, 30, 30);
            var preview = previewObject.GetComponent<RawImage>(); preview.raycastTarget = false;
            previewObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            previewObject.SetActive(false);
            GameObject frame = A.Node(viewport.transform, "ScanFrame", typeof(Image));
            A.At(frame.transform, A.MC, new Vector2(0, 40), new Vector2(700, 520));
            var frameImage = frame.GetComponent<Image>(); frameImage.sprite = IconFactory.ScanFrame; frameImage.type = Image.Type.Simple; frameImage.color = new Color(1, 1, 1, 0.9f); frameImage.raycastTarget = false;
            Image laser = A.RawPanel(frame.transform, "ScanLine", new Color(0.95f, 0.36f, 0.58f, 0.95f), 12f);
            laser.sprite = IconFactory.ScanLine; laser.type = Image.Type.Simple; laser.raycastTarget = false;
            A.At(laser.transform, A.MC, Vector2.zero, new Vector2(620, 22));
            var sweep = SceneBuilderCore.Ensure<ScanLineSweep>(frame);
            A.Set(sweep, "line", laser.rectTransform); A.Set(sweep, "area", (RectTransform)frame.transform);

            Image hintBack = A.RawPanel(viewport.transform, "HintBackdrop", new Color(0, 0, 0, 0.5f), 36f);
            A.TopStrip(hintBack.transform, 24, 64, 90, 90); hintBack.raycastTarget = false;
            TextMeshProUGUI hint = A.RawLabel(hintBack.transform, "HintText", "Keep the product in frame", 28, Color.white, FontRole.Body, TextAlignmentOptions.Center);
            A.Fill(hint.transform, 18, 0, 18, 0); A.Shrink(hint, 20);

            // Focus / Lighting / Clear Image: ticks that turn green from the live camera picture (Take Photo mock-up)
            Image checkBack = A.RawPanel(viewport.transform, "ChecklistCard", new Color(1, 1, 1, 0.88f), 36f);
            A.BottomStrip(checkBack.transform, 24, 168, 120, 120); checkBack.raycastTarget = false;
            string[] checkNames = { "Focus", "Lighting", "Clear Image" };
            var checkTicks = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                TextMeshProUGUI cl = A.RawLabel(checkBack.transform, "CheckLabel_" + checkNames[i], checkNames[i], 28, new Color(0.25f, 0.20f, 0.21f), FontRole.Label, TextAlignmentOptions.MidlineLeft);
                A.TopStrip(cl.transform, 14 + i * 50, 46, 28, 90); A.Shrink(cl, 20);
                checkTicks[i] = A.RawIcon(checkBack.transform, "CheckTick_" + checkNames[i], IconFactory.Check, 36, new Color(0.70f, 0.66f, 0.67f));
                A.At(checkTicks[i].transform, A.TR, new Vector2(-30, -(14 + i * 50 + 5)), new Vector2(36, 36));
            }

            float controlsY = A.HeaderBottom + 880;
            Button galleryBtn = A.IconButton(cap.transform, "GalleryButton", IconFactory.Document, 112, 56, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(galleryBtn.transform, A.TL, new Vector2(80, -controlsY - 20), new Vector2(112, 112));
            Button shutter = A.IconButton(cap.transform, "ShutterButton", IconFactory.CameraIcon, 150, 64, ThemeRole.ButtonText, ThemeRole.ButtonFill);
            A.At(shutter.transform, A.TC, new Vector2(0, -controlsY), new Vector2(150, 150));
            Button torch = A.IconButton(cap.transform, "TorchButton", IconFactory.Bolt, 112, 56, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(torch.transform, A.TR, new Vector2(-80, -controlsY - 20), new Vector2(112, 112));

            GameObject roles = A.Node(cap.transform, "RoleBar");
            A.TopStrip(roles.transform, controlsY + 170, 76);
            A.HStack(roles, 10, 0, 0, 0, 0, TextAnchor.MiddleCenter);
            string[] roleNames = { "Front", "Back", "Ingredients", "Barcode", "Shade" };
            var roleButtons = new Button[roleNames.Length];
            for (int i = 0; i < roleNames.Length; i++)
            {
                roleButtons[i] = A.PillButton(roles.transform, "Role_" + roleNames[i], roleNames[i], 26, ThemeRole.Background, ThemeRole.MainText);
                A.Size(roleButtons[i], -1, 72, 1);
            }

            GameObject thumbs = A.Node(cap.transform, "Thumbnails");
            A.TopStrip(thumbs.transform, controlsY + 262, 150);
            A.HStack(thumbs, 12, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            GameObject thumb = A.Node(thumbs.transform, "ThumbTemplate", typeof(Image));
            Image thumbBack = thumb.GetComponent<Image>(); Kit.StyleRounded(thumbBack); thumbBack.color = Color.white; SceneBuilderCore.Themed(thumb, ThemeRole.SurfaceAlt);
            A.Size(thumbBack, 132, 150);
            GameObject pic = A.Node(thumb.transform, "Picture", typeof(RawImage));
            A.Fill(pic.transform, 6, 46, 6, 6);
            Button roleBtn = A.PillButton(thumb.transform, "RoleButton", "Front", 20, ThemeRole.Background, ThemeRole.MainText);
            A.BottomStrip(roleBtn.transform, 4, 40, 4, 4);
            A.LabelOf(roleBtn).name = "RoleText";
            Button remove = A.IconButton(thumb.transform, "RemoveButton", IconFactory.Close, 48, 26, ThemeRole.MainText, ThemeRole.Background);
            A.At(remove.transform, A.TR, new Vector2(4, 4), new Vector2(48, 48));

            Button cont = A.PillButton(cap.transform, "ContinueButton", "Read & review", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.BottomStrip(cont.transform, 120, 116);
            Button typeInstead = A.LinkButton(cap.transform, "TypeInsteadButton", "Type details instead", 30, ThemeRole.AccentText);
            A.BottomStrip(typeInstead.transform, 34, 76, 200, 200);

            // ---------------- batch list (Batch Mode) ----------------
            GameObject batch = A.Node(card, "BatchPanel");
            A.Fill(batch.transform);
            GameObject batchArea = A.Node(batch.transform, "BatchArea");
            A.Between(batchArea.transform, A.HeaderBottom, 290, 30, 30);
            A.Scroll(batchArea.transform, "BatchScrollView", out RectTransform batchContent, 12, 10, 18, 6, 40);
            GameObject batchRow = Row(batchContent, "BatchRowTemplate", 118);
            TextMeshProUGUI batchEmpty = A.Label(batch.transform, "BatchEmptyText", "Nothing in the batch yet.", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(batchEmpty.transform, A.HeaderBottom + 80, 160, 80, 80);
            Button batchAdd = A.PillButton(batch.transform, "BatchAddButton", "Add photos", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Plus);
            A.At(batchAdd.transform, A.BL, new Vector2(30, 170), new Vector2(440, 100));
            A.Shrink(A.LabelOf(batchAdd), 20);
            Button batchClear = A.PillButton(batch.transform, "BatchClearButton", "Clear batch", 30, ThemeRole.SurfaceAlt, ThemeRole.AccentText, IconFactory.Trash);
            A.At(batchClear.transform, A.BR, new Vector2(-30, 170), new Vector2(440, 100));
            A.Shrink(A.LabelOf(batchClear), 20);
            Button batchSend = A.PillButton(batch.transform, "BatchSubmitButton", "Send ready", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.BottomStrip(batchSend.transform, 34, 116, 30, 30);
            batch.SetActive(false);

            // ---------------- review form ----------------
            GameObject form = A.Node(card, "FormPanel");
            A.Between(form.transform, A.HeaderBottom, 30, 30, 30);
            A.Scroll(form.transform, "FormScrollView", out RectTransform content, 14, 12, 18, 6, 40);
            GameObject resultCard = A.Node(content, "ScanResultCard", typeof(Image));
            Image resultBack = resultCard.GetComponent<Image>(); Kit.StyleRounded(resultBack); Kit.SetRadius(resultBack, 56f); resultBack.color = Color.white;
            SceneBuilderCore.Themed(resultCard, ThemeRole.SurfaceAlt);
            A.Size(resultBack, -1, 236);
            Image thumbFrame = A.Panel(resultCard.transform, "ThumbFrame", ThemeRole.Background, 1f, 36f);
            A.At(thumbFrame.transform, A.ML, new Vector2(24, 0), new Vector2(170, 170));
            GameObject thumbPic = A.Node(thumbFrame.transform, "Picture", typeof(RawImage));
            A.Fill(thumbPic.transform, 8, 8, 8, 8);
            var resultThumb = thumbPic.GetComponent<RawImage>(); resultThumb.color = new Color(1, 1, 1, 0); resultThumb.raycastTarget = false;
            TextMeshProUGUI resultName = A.Label(resultCard.transform, "ResultNameText", "Product name needed", 34, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(resultName.transform, 20, 56, 212, 24); A.Shrink(resultName, 22);
            TextMeshProUGUI resultSub = A.Label(resultCard.transform, "ResultSubText", "No barcode yet", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(resultSub.transform, 82, 40, 212, 24); A.Shrink(resultSub, 18);
            Image resultPill = A.RawPanel(resultCard.transform, "ResultPill", new Color(0.2f, 0.62f, 0.42f, 0.16f), 28f);
            A.At(resultPill.transform, A.BL, new Vector2(212, 24), new Vector2(300, 48));
            TextMeshProUGUI resultPillText = A.RawLabel(resultPill.transform, "ResultPillText", "Product Found", 24, new Color(0.2f, 0.62f, 0.42f), FontRole.Label, TextAlignmentOptions.Center);
            A.Fill(resultPillText.transform, 10, 0, 10, 0); A.Shrink(resultPillText, 17);
            string[] keys = CaptureDraft.Keys;
            var inputs = new TMP_InputField[keys.Length]; var states = new TextMeshProUGUI[keys.Length]; var checks = new Button[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                bool required = keys[i] == "name" || keys[i] == "brand" || keys[i] == "cosmeticType";
                TextMeshProUGUI label = A.Label(content, "Label_" + keys[i], CaptureDraft.Label(keys[i]) + (required ? " *" : ""), 32, ThemeRole.MainText, FontRole.Label);
                A.Size(label, height: 48);
                states[i] = A.RawLabel(content, "State_" + keys[i], "Missing", 24, new Color(0.78f, 0.30f, 0.38f), FontRole.Body, TextAlignmentOptions.MidlineLeft, true);
                Fit(states[i]);
                bool tall = keys[i] == "ingredients";
                inputs[i] = SceneBuilderCore.MakeInput(content, "Input_" + keys[i], Placeholder(keys[i]), A.TC, Vector2.zero, new Vector2(860, tall ? 260 : Kit.FieldHeight));
                Kit.StyleInput(inputs[i], Placeholder(keys[i]), ThemeRole.InputBackground);
                if (tall) { inputs[i].lineType = TMP_InputField.LineType.MultiLineNewline; inputs[i].textComponent.alignment = TextAlignmentOptions.TopLeft; }
                if (keys[i] == "barcode") inputs[i].contentType = TMP_InputField.ContentType.IntegerNumber;
                inputs[i].characterLimit = tall ? 4000 : keys[i] == "barcode" ? 14 : 100;
                A.Size(inputs[i], -1, tall ? 260 : Kit.FieldHeight);
                checks[i] = A.LinkButton(content, "Check_" + keys[i], "Mark checked against the pack", 26, ThemeRole.AccentText);
                A.Size(checks[i], -1, 64);
                A.LabelOf(checks[i]).alignment = TextAlignmentOptions.MidlineRight; A.LabelOf(checks[i]).margin = new Vector4(0, 0, 30, 0);          // clear of the scroll bar
            }
            TextMeshProUGUI raw = A.Label(content, "RawText", "No photos. Add photos so the reviewer can compare.", 24, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Fit(raw);
            Button addPhotos = A.PillButton(content, "AddPhotosButton", "Add photos", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.CameraIcon);
            A.Size(addPhotos, -1, 96);
            Button submit = A.PillButton(content, "SubmitButton", "Save Product", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.Size(submit, -1, 124);
            Button tryAgain = A.PillButton(content, "TryAgainButton", "Try Again", 36, ThemeRole.Background, ThemeRole.AccentText, IconFactory.Sync);
            A.Size(tryAgain, -1, 112);
            Kit.EnsureOutline(tryAgain.transform, "Outline", ThemeRole.Accent);
            GameObject after = A.Node(content, "AfterSubmit");
            A.Size(after.AddComponent<LayoutElement>(), -1, 108);
            Button viewStatus = A.PillButton(after.transform, "ViewStatusButton", "View status", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.Fill(viewStatus.transform, 0, 0, 450, 0);
            Button again = A.PillButton(after.transform, "NewCaptureButton", "New capture", 32, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.Fill(again.transform, 450, 0, 0, 0);
            after.SetActive(false);
            form.SetActive(false);

            var tips = TipsPopup(s, "TipsPanel");
            var confirm = ConfirmPopup(s, "ConfirmPanel");
            var c = A.Controller<AdminCaptureController>("AdminCaptureController");
            var camera = SceneBuilderCore.Ensure<CameraFeedController>(c.gameObject);
            A.Set(camera, "display", display); A.Set(camera, "viewport", viewport.rectTransform); A.Set(camera, "mirrorRoot", mirror.transform);
            A.Set(c, "backButton", s.back); A.Set(c, "titleText", s.title); A.Set(c, "confirmDialog", confirm);
            A.Set(c, "capturePanel", cap); A.Set(c, "cameraFeed", camera); A.Set(c, "galleryPreview", preview); A.Set(c, "scanFrame", (RectTransform)frame.transform);
            A.Set(c, "hintText", hint); A.Set(c, "tipsButton", tipsButton); A.Set(c, "tipsPanel", tips); A.Set(c, "scanLine", sweep);
            A.Set(c, "checklist", checkBack.gameObject); A.Set(c, "checkFocus", checkTicks[0]); A.Set(c, "checkLighting", checkTicks[1]); A.Set(c, "checkClear", checkTicks[2]);
            A.Set(c, "galleryButton", galleryBtn); A.Set(c, "shutterButton", shutter); A.Set(c, "torchButton", torch);
            A.Set(c, "roleBar", roles); Kit.SetArray(c, "roleButtons", roleButtons);
            A.Set(c, "thumbsContent", (RectTransform)thumbs.transform); A.Set(c, "thumbTemplate", thumb);
            A.Set(c, "continueButton", cont); A.Set(c, "continueLabel", A.LabelOf(cont)); A.Set(c, "typeInsteadButton", typeInstead);
            A.Set(c, "batchPanel", batch); A.Set(c, "batchContent", batchContent); A.Set(c, "batchRowTemplate", batchRow); A.Set(c, "batchEmptyText", batchEmpty);
            A.Set(c, "batchSubmitButton", batchSend); A.Set(c, "batchSubmitLabel", A.LabelOf(batchSend)); A.Set(c, "batchAddButton", batchAdd); A.Set(c, "batchClearButton", batchClear);
            A.Set(c, "formPanel", form); Kit.SetArray(c, "fieldInputs", inputs); Kit.SetArray(c, "fieldStates", states); Kit.SetArray(c, "checkButtons", checks);
            A.Set(c, "resultThumb", resultThumb); A.Set(c, "resultName", resultName); A.Set(c, "resultSub", resultSub);
            A.Set(c, "resultPillText", resultPillText); A.Set(c, "resultPill", resultPill); A.Set(c, "tryAgainButton", tryAgain);
            A.Set(c, "rawText", raw); A.Set(c, "addPhotosButton", addPhotos);
            A.Set(c, "submitButton", submit); A.Set(c, "submitLabel", A.LabelOf(submit));
            A.Set(c, "afterSubmit", after); A.Set(c, "viewStatusButton", viewStatus); A.Set(c, "newCaptureButton", again);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        static string Placeholder(string key)
        {
            switch (key)
            {
                case "name": return "e.g. Glow Matte Lipstick";
                case "brand": return "Brand as printed on the pack";
                case "cosmeticType": return "e.g. Lipstick, Foundation, Mascara";
                case "barcode": return "Digits under the bars (zeros kept)";
                case "ingredients": return "Ingredients as printed, separated by commas (optional)";
                case "shade": return "Shade code as printed, e.g. N20 (optional)";
                default: return "";
            }
        }

        static void Fit(TextMeshProUGUI t)
        {
            var f = SceneBuilderCore.Ensure<ContentSizeFitter>(t.gameObject);
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // =====================================================================================
        //  4. Submissions (live list + detail)
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/16d Staff Submissions", false, 130)]
        public static void BuildSubmissions()
        {
            A.AppScreen s = A.Begin("AdminSubmissionsScene", "Submissions", true, "AdminSubmissionsCard");
            Transform card = s.card;

            GameObject tabs = A.Node(card, "Tabs");
            A.TopStrip(tabs.transform, A.HeaderBottom, 84);
            A.HStack(tabs, 10, 0, 0, 0, 0, TextAnchor.MiddleCenter);
            string[] names = { "All", "Pending", "Approved", "Rejected" };
            var tabButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                tabButtons[i] = A.PillButton(tabs.transform, "Tab_" + names[i], names[i], 26, ThemeRole.Background, ThemeRole.MainText);
                A.Size(tabButtons[i], -1, 76, 1); A.Shrink(A.LabelOf(tabButtons[i]), 18);
            }
            TextMeshProUGUI sync = A.Label(card, "SyncText", "Connecting...", 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center);
            A.TopStrip(sync.transform, A.HeaderBottom + 92, 46);
            A.Shrink(sync, 20);

            GameObject area = A.Node(card, "ListArea");
            A.Between(area.transform, A.HeaderBottom + 150, 30, 30, 30);
            A.Scroll(area.transform, "ListScrollView", out RectTransform content, 12, 10, 18, 6, 40);
            GameObject template = Row(content, "RowTemplate", 118);
            TextMeshProUGUI empty = A.Label(card, "EmptyText", "Loading your submissions...", 30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(empty.transform, A.HeaderBottom + 200, 120, 80, 80);

            A.AppPopup detail = A.Overlay(s, "DetailPanel", "Submission");
            A.At(detail.box, A.MC, Vector2.zero, new Vector2(940, 1180));
            TextMeshProUGUI dName = A.Label(detail.box, "NameText", "Luminous Skin Tint", 42, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(dName.transform, 130, 110, 48, 48); A.ShrinkWrapped(dName, 30);
            TextMeshProUGUI dBrand = A.Label(detail.box, "BrandText", "Brand", 32, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(dBrand.transform, 244, 50, 48, 48);
            TextMeshProUGUI dStatus = A.RawLabel(detail.box, "StatusText", "Pending review", 32, new Color(0.80f, 0.50f, 0.58f), FontRole.Heading);
            A.TopStrip(dStatus.transform, 300, 52, 48, 48);
            TextMeshProUGUI dInfo = A.Label(detail.box, "InfoText", "", 27, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            dInfo.richText = true;
            A.TopStrip(dInfo.transform, 370, 470, 48, 48); A.ShrinkWrapped(dInfo, 20);
            TextMeshProUGUI dNote = A.Label(detail.box, "NoteText", "", 27, ThemeRole.Warning, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(dNote.transform, 850, 160, 48, 48); A.ShrinkWrapped(dNote, 20);
            Button openWeb = A.PillButton(detail.box, "OpenWebsiteButton", "Open on the website", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.ExternalLink);
            A.BottomStrip(openWeb.transform, 40, 108, 48, 48);

            var c = A.Controller<AdminSubmissionsController>("AdminSubmissionsController");
            A.Set(c, "backButton", s.back); Kit.SetArray(c, "tabButtons", tabButtons); A.Set(c, "syncText", sync);
            A.Set(c, "content", content); A.Set(c, "rowTemplate", template); A.Set(c, "emptyText", empty);
            A.Set(c, "detailPanel", detail.panel); A.Set(c, "detailName", dName); A.Set(c, "detailBrand", dBrand); A.Set(c, "detailStatus", dStatus);
            A.Set(c, "detailInfo", dInfo); A.Set(c, "detailNote", dNote); A.Set(c, "openWebsiteButton", openWeb);
            A.Controller<StaffSkin>("StaffSkin"); A.Finish(s);
        }

        // =====================================================================================
        //  shared: a Yes / Cancel pop-up (ConfirmDialog), a round input field
        // =====================================================================================
        static GlowUp.AppSettings.ConfirmDialog ConfirmPopup(A.AppScreen s, string name)
        {
            A.AppPopup p = A.Overlay(s, name, "Are you sure?");
            A.At(p.box, A.MC, new Vector2(0, 60), new Vector2(900, 560));
            TextMeshProUGUI message = A.Label(p.box, "MessageText", "", 31, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(message.transform, 48, 190, 48, 130);
            A.ShrinkWrapped(message, 24);
            Button cancel = A.PillButton(p.box, "CancelButton", "Cancel", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(cancel.transform, 44, 108, 48, 462);
            Button ok = A.PillButton(p.box, "ConfirmButton", "OK", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(ok.transform, 44, 108, 462, 48);
            Object.DestroyImmediate(ok.GetComponent<ThemedGraphic>());                      // ConfirmDialog paints this button itself
            Object.DestroyImmediate(A.LabelOf(ok).GetComponent<ThemedGraphic>());
            var dialog = A.Controller<GlowUp.AppSettings.ConfirmDialog>(name + "Dialog");
            A.Set(dialog, "panel", p.panel); A.Set(dialog, "titleText", p.title); A.Set(dialog, "messageText", message);
            A.Set(dialog, "confirmButton", ok); A.Set(dialog, "confirmLabel", A.LabelOf(ok));
            A.Set(dialog, "cancelButton", cancel); A.Set(dialog, "cancelLabel", A.LabelOf(cancel));
            return dialog;
        }

        static TMP_InputField Field(Transform card, string name, string placeholder, float y, float x, TMP_InputField.ContentType type)
        {
            TMP_InputField f = SceneBuilderCore.MakeInput(card, name, placeholder, A.TC, Vector2.zero, new Vector2(800, Kit.FieldHeight));
            A.TopStrip(f.transform, y, Kit.FieldHeight, x, x);
            Kit.StyleInput(f, placeholder, ThemeRole.InputBackground);
            f.contentType = type;
            f.characterLimit = type == TMP_InputField.ContentType.EmailAddress ? Kit.EmailLimit : 128;
            f.text = "";
            return f;
        }
    }
}
