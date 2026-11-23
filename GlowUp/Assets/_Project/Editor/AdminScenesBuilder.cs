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
            Debug.Log("[GlowUp] Staff scenes built: AdminLoginScene, AdminDashboardScene, AdminCaptureScene, AdminSubmissionsScene.");
        }

        // =====================================================================================
        //  Login: the "Admin?" row (closed by default) with ONE gold Staff / Admin Access button
        // =====================================================================================
        public static void AddLoginAccordion(Transform card, float y)
        {
            Transform old = Kit.Loose(card.root, "AdminAccordion");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform oldController = Kit.Loose(card.root, "AdminAccessAccordion");
            if (oldController != null && oldController.GetComponent<Canvas>() == null && oldController.parent == null) Object.DestroyImmediate(oldController.gameObject);

            GameObject root = A.Node(card, "AdminAccordion");
            A.TopStrip(root.transform, y, 300, Kit.PadX, Kit.PadX);

            Button toggle = A.LinkButton(root.transform, "AdminToggle", "Admin?", 32, ThemeRole.SecondaryText);
            A.At(toggle.transform, A.TC, Vector2.zero, new Vector2(320, 84));
            TextMeshProUGUI toggleLabel = A.LabelOf(toggle);
            toggleLabel.text = "Admin?";
            A.Fill(toggleLabel.transform, 0, 0, 56, 0);
            toggleLabel.alignment = TextAlignmentOptions.MidlineRight;
            Image chevron = A.Icon(toggle.transform, "Chevron", IconFactory.ChevronDown, 40, ThemeRole.SecondaryText);
            A.At(chevron.transform, A.MR, new Vector2(-6, 0), new Vector2(40, 40));

            GameObject panelObject = A.Node(root.transform, "StaffAccessPanel", typeof(CanvasGroup));
            A.TopStrip(panelObject.transform, 92, 200, 0, 0);
            ((RectTransform)panelObject.transform).pivot = new Vector2(0.5f, 1f);
            Button staff = A.PillButton(panelObject.transform, "StaffAccessButton", "Staff / Admin Access", 36, ThemeRole.Gold, ThemeRole.Background, IconFactory.Scan);
            A.TopStrip(staff.transform, 0, 120, 0, 0);
            TextMeshProUGUI helper = A.Label(panelObject.transform, "HelperText", "For authorized personnel registering products.", 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(helper.transform, 132, 56, 0, 0);
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

            TextMeshProUGUI message = A.Label(card, "MessageText", "", 30, ThemeRole.Warning, FontRole.Body, TextAlignmentOptions.Center, true);
            A.TopStrip(message.transform, 996, 100, x, x);
            A.ShrinkWrapped(message, 24);
            message.gameObject.SetActive(false);

            Button signIn = A.PillButton(card, "SignInButton", "Sign In", 40, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.TopStrip(signIn.transform, 1110, Kit.ButtonHeight, x, x);
            Button customer = A.LinkButton(card, "CustomerLoginButton", "Back to customer login", 32, ThemeRole.AccentText);
            A.BottomStrip(customer.transform, 60, 84, x, x);

            var c = A.Controller<AdminLoginController>("AdminLoginController");
            A.Set(c, "backButton", s.back); A.Set(c, "emailInput", email); A.Set(c, "passwordInput", pw);
            A.Set(c, "signInButton", signIn); A.Set(c, "signInLabel", A.LabelOf(signIn)); A.Set(c, "forgotButton", forgot);
            A.Set(c, "customerLoginButton", customer); A.Set(c, "messageText", message);
            A.Finish(s);
        }

        // =====================================================================================
        //  2. Capture dashboard (deliberately small)
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/16b Staff Dashboard", false, 128)]
        public static void BuildDashboard()
        {
            A.AppScreen s = A.Begin("AdminDashboardScene", "", false, "AdminDashboardCard");
            Transform card = s.card;

            TextMeshProUGUI logo = A.Label(card, "LogoText", "Glow Up", 72, ThemeRole.Gold, FontRole.Title, TextAlignmentOptions.MidlineLeft);
            A.TopStrip(logo.transform, 36, 96, 56, 200);
            Button signOut = A.IconButton(card, "SignOutButton", IconFactory.SignOut, 92, 52, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.At(signOut.transform, A.TR, new Vector2(-36, -38), new Vector2(92, 92));

            TextMeshProUGUI welcome = A.Label(card, "WelcomeText", "Welcome, Staff!", 54, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(welcome.transform, 150, 72, 56, 56);
            TextMeshProUGUI account = A.Label(card, "AccountText", "staff@example.com · Admin", 30, ThemeRole.SecondaryText, FontRole.Body);
            A.TopStrip(account.transform, 222, 46, 56, 56);
            A.Shrink(account, 22);

            // ---- Product Registration: the four ways in ----
            Image reg = A.Panel(card, "RegistrationPanel", ThemeRole.SurfaceAlt, 0.9f, 48f);
            A.TopStrip(reg.transform, 290, 830);
            TextMeshProUGUI regTitle = A.Label(reg.transform, "TitleText", "Product Registration", 40, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(regTitle.transform, 22, 56, 36, 36);
            TextMeshProUGUI regSub = A.Label(reg.transform, "SubtitleText", "Choose how you want to add a product. It goes to the website for review.", 27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(regSub.transform, 80, 70, 36, 36);
            Button scan = Tile(reg.transform, "ScanBarcodeTile", IconFactory.Scan, "Scan Barcode", "Identify products automatically", 0, 0);
            Button capture = Tile(reg.transform, "CaptureProductTile", IconFactory.CameraIcon, "Capture Product", "Take product and label photos", 1, 0);
            Button gallery = Tile(reg.transform, "UploadGalleryTile", IconFactory.Document, "Upload from Gallery", "Use existing photos", 0, 1);
            Button manual = Tile(reg.transform, "ManualEntryTile", IconFactory.Pencil, "Manual Entry", "For products the reader cannot identify", 1, 1);

            Button resume = A.PillButton(card, "ResumeButton", "Unsent capture: tap to continue", 28, ThemeRole.Background, ThemeRole.AccentText, IconFactory.History);
            A.TopStrip(resume.transform, 1136, 78);
            A.Shrink(A.LabelOf(resume), 22);
            resume.gameObject.SetActive(false);

            // ---- live status (from the database listener) ----
            Image status = A.Panel(card, "StatusPanel", ThemeRole.Background, 1f, 40f);
            A.TopStrip(status.transform, 1228, 132);
            A.Outline(status.transform, ThemeRole.Gold, 40f, 0.5f);
            Image dot = A.RawPanel(status.transform, "StatusDot", new Color(0.62f, 0.55f, 0.56f), 14f);
            A.At(dot.transform, A.TL, new Vector2(34, -36), new Vector2(26, 26));
            TextMeshProUGUI statusTitle = A.Label(status.transform, "StatusTitle", "Connecting...", 34, ThemeRole.MainText, FontRole.Heading);
            A.TopStrip(statusTitle.transform, 20, 50, 76, 30);
            TextMeshProUGUI statusDetail = A.Label(status.transform, "StatusDetail", "Product details are read on the phone and prepared for review on the website.", 25, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(statusDetail.transform, 68, 60, 76, 30);
            A.ShrinkWrapped(statusDetail, 20);

            // ---- recent submissions ----
            TextMeshProUGUI recentTitle = A.Label(card, "RecentTitle", "Recent Submissions", 38, ThemeRole.Heading, FontRole.Heading);
            A.TopStrip(recentTitle.transform, 1378, 56, 56, 260);
            Button viewAll = A.LinkButton(card, "ViewAllButton", "View all", 30, ThemeRole.AccentText);
            A.At(viewAll.transform, A.TR, new Vector2(-48, -1374), new Vector2(200, 64));
            GameObject list = A.Node(card, "RecentList");
            A.Between(list.transform, 1442, 150);                      // up to the bottom buttons; rows that do not fit are clipped (short phones)
            list.AddComponent<RectMask2D>();
            A.VStack(list, 8);
            GameObject template = Row(list.transform, "RowTemplate", 96);
            TextMeshProUGUI empty = A.Label(list.transform, "EmptyText", "Loading your submissions...", 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            A.Size(empty, height: 96);

            // ---- bottom: submissions + the website (everything else lives there) ----
            Button navSubs = A.PillButton(card, "SubmissionsButton", "Submissions", 32, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.ListIcon);
            A.BottomStrip(navSubs.transform, 30, 104, P, 520);
            Button website = A.PillButton(card, "OpenWebsiteButton", "Open Admin Website", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.ExternalLink);
            A.BottomStrip(website.transform, 30, 104, 480, P);

            var confirm = ConfirmPopup(s, "ConfirmPanel");
            var c = A.Controller<AdminDashboardController>("AdminDashboardController");
            A.Set(c, "welcomeText", welcome); A.Set(c, "accountText", account); A.Set(c, "signOutButton", signOut); A.Set(c, "confirmDialog", confirm);
            A.Set(c, "scanButton", scan); A.Set(c, "captureButton", capture); A.Set(c, "galleryButton", gallery); A.Set(c, "manualButton", manual);
            A.Set(c, "resumeButton", resume); A.Set(c, "resumeText", A.LabelOf(resume));
            A.Set(c, "statusTitle", statusTitle); A.Set(c, "statusDetail", statusDetail); A.Set(c, "statusDot", dot);
            A.Set(c, "recentContent", (RectTransform)list.transform); A.Set(c, "rowTemplate", template); A.Set(c, "emptyText", empty);
            A.Set(c, "viewAllButton", viewAll); A.Set(c, "websiteButton", website); A.Set(c, "navSubmissions", navSubs);
            A.Finish(s);
        }

        static Button Tile(Transform parent, string name, Sprite icon, string title, string description, int col, int row)
        {
            const float w = 410, h = 300, gap = 20, left = 30, top = 172;
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Button));
            Image img = go.GetComponent<Image>(); Kit.StyleRounded(img); img.color = Color.white;
            SceneBuilderCore.Themed(go, ThemeRole.Background);
            A.At(go.transform, A.TL, new Vector2(left + col * (w + gap), -(top + row * (h + gap))), new Vector2(w, h));
            ((RectTransform)go.transform).pivot = new Vector2(0, 1);
            ((RectTransform)go.transform).anchoredPosition = new Vector2(left + col * (w + gap), -(top + row * (h + gap)));
            A.Outline(go.transform, ThemeRole.Accent, 48f, 0.35f);
            Image disc = A.Panel(go.transform, "IconDisc", ThemeRole.SurfaceAlt, 1f, 60f);
            A.At(disc.transform, A.TC, new Vector2(0, -26), new Vector2(116, 116));
            Image ic = A.Icon(disc.transform, "Icon", icon, 62, ThemeRole.AccentText);
            A.At(ic.transform, A.MC, Vector2.zero, new Vector2(62, 62));
            TextMeshProUGUI t = A.Label(go.transform, "TitleText", title, 32, ThemeRole.MainText, FontRole.Heading, TextAlignmentOptions.Center);
            A.TopStrip(t.transform, 156, 50, 14, 14); A.Shrink(t, 24);
            TextMeshProUGUI d = A.Label(go.transform, "DescriptionText", description, 24, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Top, true);
            A.TopStrip(d.transform, 210, 74, 22, 22); A.ShrinkWrapped(d, 19);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            return b;
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
            TextMeshProUGUI hint = A.RawLabel(viewport.transform, "HintText", "Align the barcode within the frame", 28, Color.white, FontRole.Body, TextAlignmentOptions.Center, true);
            A.BottomStrip(hint.transform, 24, 104, 40, 40);
            Image hintBack = A.RawPanel(viewport.transform, "HintBackdrop", new Color(0, 0, 0, 0.45f), 36f);
            A.BottomStrip(hintBack.transform, 20, 112, 30, 30);
            hintBack.transform.SetSiblingIndex(hint.transform.GetSiblingIndex());

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

            // ---------------- review form ----------------
            GameObject form = A.Node(card, "FormPanel");
            A.Between(form.transform, A.HeaderBottom, 30, 30, 30);
            A.Scroll(form.transform, "FormScrollView", out RectTransform content, 14, 12, 18, 6, 40);
            TextMeshProUGUI notice = A.Label(content, "NoticeText", "", 28, ThemeRole.AccentText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Fit(notice);
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
                A.LabelOf(checks[i]).alignment = TextAlignmentOptions.MidlineRight;
            }
            TextMeshProUGUI raw = A.Label(content, "RawText", "No photos. Add photos so the reviewer can compare.", 24, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Fit(raw);
            Button addPhotos = A.PillButton(content, "AddPhotosButton", "Add photos", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.CameraIcon);
            A.Size(addPhotos, -1, 96);
            Button submit = A.PillButton(content, "SubmitButton", "Submit to Website", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.Size(submit, -1, 124);
            TextMeshProUGUI result = A.Label(content, "ResultText", "", 28, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Fit(result);
            GameObject after = A.Node(content, "AfterSubmit");
            A.Size(after.AddComponent<LayoutElement>(), -1, 108);
            Button viewStatus = A.PillButton(after.transform, "ViewStatusButton", "View status", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.Fill(viewStatus.transform, 0, 0, 450, 0);
            Button again = A.PillButton(after.transform, "NewCaptureButton", "New capture", 32, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.Fill(again.transform, 450, 0, 0, 0);
            after.SetActive(false);
            form.SetActive(false);

            var confirm = ConfirmPopup(s, "ConfirmPanel");
            var c = A.Controller<AdminCaptureController>("AdminCaptureController");
            var camera = SceneBuilderCore.Ensure<CameraFeedController>(c.gameObject);
            A.Set(camera, "display", display); A.Set(camera, "viewport", viewport.rectTransform); A.Set(camera, "mirrorRoot", mirror.transform);
            A.Set(c, "backButton", s.back); A.Set(c, "titleText", s.title); A.Set(c, "confirmDialog", confirm);
            A.Set(c, "capturePanel", cap); A.Set(c, "cameraFeed", camera); A.Set(c, "galleryPreview", preview); A.Set(c, "scanFrame", (RectTransform)frame.transform);
            A.Set(c, "hintText", hint); A.Set(c, "galleryButton", galleryBtn); A.Set(c, "shutterButton", shutter); A.Set(c, "torchButton", torch);
            A.Set(c, "roleBar", roles); Kit.SetArray(c, "roleButtons", roleButtons);
            A.Set(c, "thumbsContent", (RectTransform)thumbs.transform); A.Set(c, "thumbTemplate", thumb);
            A.Set(c, "continueButton", cont); A.Set(c, "continueLabel", A.LabelOf(cont)); A.Set(c, "typeInsteadButton", typeInstead);
            A.Set(c, "formPanel", form); Kit.SetArray(c, "fieldInputs", inputs); Kit.SetArray(c, "fieldStates", states); Kit.SetArray(c, "checkButtons", checks);
            A.Set(c, "noticeText", notice); A.Set(c, "rawText", raw); A.Set(c, "addPhotosButton", addPhotos);
            A.Set(c, "submitButton", submit); A.Set(c, "submitLabel", A.LabelOf(submit)); A.Set(c, "resultText", result);
            A.Set(c, "afterSubmit", after); A.Set(c, "viewStatusButton", viewStatus); A.Set(c, "newCaptureButton", again);
            A.Finish(s);
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
            A.Finish(s);
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
