using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 2 Onboarding Step 1 (account details).
    /// Layout: header, then ONE visible scroll panel (FormPanel) that holds every label and input field,
    /// then - outside the panel - the message line and the Continue button.
    /// </summary>
    public static class OnboardingStep1Builder
    {
        [MenuItem("GlowUp/Build Scene/2 Onboarding Step 1", false, 112)]
        public static void Build()
        {
            var scene = SceneBuilderCore.Open("OnboardingStep1Scene");
            var c = BuildChrome(1, "Fill in your details");
            var ct = c.canvas;

            // ---- the scroll panel: fills the space between the header and the Continue button on any phone ----
            var panel = EnsurePanel(c.card, "FormPanel", ThemeRole.SurfaceAlt, PanelAlpha);
            TopBottom(panel, c.card, HeaderBottom, FooterTop);
            var oldScroll = Loose(c.card, "FormScrollView");
            if (oldScroll != null && oldScroll.parent != panel) oldScroll.SetParent(panel, false);
            var scroll = MakeScroll(panel, "FormScrollView", out var content);
            Stretch(scroll, 24, 24, 24, 24);

            // ---- names ----
            // each reminder now lives in the small "i" badge on its label; the old line under the field is switched off (not deleted)
            var firstLabel = LabelRow(content, Loose(ct, "FirstNameText"), "FirstNameText", "First Name");
            var first = FieldRow(content, Loose(ct, "FirstNameInputField"), "FirstNameInputField", "Enter your first name", 50);
            InfoBadge(content, firstLabel, "FirstNameHelperText", "Needed. Up to 50 characters. Your first, middle and last name identify your record.");

            var middleLabel = LabelRow(content, Loose(ct, "MiddleNameText"), "MiddleNameText", "Middle Name (optional)");
            var middle = FieldRow(content, Loose(ct, "MiddleNameInputField"), "MiddleNameInputField", "Enter your middle name", 50);
            InfoBadge(content, middleLabel, "MiddleNameHelperText", "Optional. Up to 50 characters.");

            var lastLabel = LabelRow(content, Loose(ct, "LastNameText"), "LastNameText", "Last Name");
            var last = FieldRow(content, Loose(ct, "LastNameInputField"), "LastNameInputField", "Enter your last name", 50);   // the existing object is named 'LastNameInputField ' (trailing space) - found anyway
            InfoBadge(content, lastLabel, "LastNameHelperText", "Needed. Up to 50 characters.");

            // ---- username: REMOVED on 11 October 2026 (the user's request). The dashboard greets the user by first name, so
            //      the username's label, its field, its old reminder line and its two checklist rows are taken out of the scene.
            RemoveUsername(ct);

            // ---- birthday + age ----
            var birthdayLabel = LabelRow(content, Loose(ct, "BirthdayText"), "BirthdayText", "Birthday");
            var bday = FieldRow(content, Loose(ct, "BirthdayInputField"), "BirthdayInputField", "Tap to choose your birthday", 30);
            InfoBadge(content, birthdayLabel, "BirthdayHelperText", "Needed. Tapping the field opens a calendar. You must be 13 or older.");
            var ageLabel = LabelRow(content, Loose(ct, "AgeText"), "AgeText", "Age");
            var age = FieldRow(content, Loose(ct, "AgeInputField"), "AgeInputField", "Calculated from your birthday", 3);
            InfoBadge(content, ageLabel, "AgeHelperText", "Filled in automatically from your birthday.");
            // 28 Nov 2026: the age rule is a checklist row under the Age field, like the password rules (it ticks green at 13 or older)
            var aReq = Checklist(content, "Age", new[] { "Minimum of 13 years old" });
            Transform ageRow = age.transform;
            while (ageRow.parent != null && ageRow.parent != content) ageRow = ageRow.parent;
            if (ageRow.parent == content) aReq[0].transform.SetSiblingIndex(ageRow.GetSiblingIndex() + 1);

            // ---- email ----
            var emailLabel = LabelRow(content, Loose(ct, "EmailAddressText"), "EmailAddressText", "Email");
            var email = FieldRow(content, Loose(ct, "EmailAddressInputField"), "EmailAddressInputField", "name@example.com", EmailLimit, type: TMP_InputField.ContentType.EmailAddress);
            AddSpaceGuard(email, c.feedback, "Spaces are not allowed in the email.", EmailLimit);
            // any real mail domain may sign up (12 October 2026): well-known providers at once, others after an online check
            InfoBadge(content, emailLabel, "EmailHelperText", "Needed. Your own email: Gmail, Outlook, Yahoo, iCloud, or a school or work address. Other addresses are checked online to make sure they receive email. Temporary addresses are not accepted.");
            var emailCheck = AddEmailCheck(email, content);
            // "Use another email": a small link under the email's line. It shows once the sign-up has its account (from then
            // on the email is fixed); switched off in the Hierarchy until then.
            Button useAnotherEmail = UseAnotherEmailLink(content);

            // ---- password ----
            var passwordLabel = LabelRow(content, Loose(ct, "PasswordText"), "PasswordText", "Password");
            var pw = FieldRow(content, Loose(ct, "PasswordInputField"), "PasswordInputField", "Create a password", 64, type: TMP_InputField.ContentType.Password, blockPaste: true);
            AddEye(pw);
            AddSpaceGuard(pw, c.feedback, "Spaces are not allowed in the password.");
            InfoBadge(content, passwordLabel, "PasswordHelperText", "Needed. Tap the eye to check what you typed. Pasting is switched off here.");
            // one character of each kind is enough, so the rows name the kind and do not count
            var pReq = Checklist(content, "Password", new[] { "Uppercase", "Lowercase", "Number", "Special character", "8–64 characters" },
                                 new[] { "1 uppercase letter", "1 lowercase letter", "1 number", "1 special character", "8–64 characters" });

            // ---- re-type password ----
            var retypeLabel = LabelRow(content, Loose(ct, "RetypePasswordText"), "RetypePasswordText", "Re-type Password");
            var retype = FieldRow(content, Loose(ct, "RetypePasswordInputField"), "RetypePasswordInputField", "Re-type your password", 64, type: TMP_InputField.ContentType.Password, blockPaste: true);
            AddEye(retype);
            AddSpaceGuard(retype, c.feedback, "Spaces are not allowed in the password.");
            InfoBadge(content, retypeLabel, "RetypeHelperText", "Needed. It must match your password exactly.");
            var mReq = Checklist(content, "Retype", new[] { "Passwords match" });

            // the phone's keyboard must not cover what belongs to a field: the email's check line, the password's rules, the "match" row
            KeyboardKit.KeepVisible(email, Loose(content, "EmailCheckText"), useAnotherEmail);
            KeyboardKit.KeepVisible(pw, pReq);
            KeyboardKit.KeepVisible(retype, mReq);

            // ---- calendar popup (covers the panel while open) ----
            var popup = BuildDatePopup(c.card);
            var field = SceneBuilderCore.Ensure<DatePickerField>(bday.gameObject);
            SceneBuilderCore.SetField(field, "popup", popup);
            SceneBuilderCore.SetField(c.leave, "calendar", popup);          // the phone's back key closes the calendar before anything else

            // ---- "Confirm your email": the pop-up that waits until the link in the message was tapped ----
            OverlayPanel confirmPanel = BuildConfirmEmailPanel(ct, out TextMeshProUGUI confirmMessage, out TextMeshProUGUI confirmStatus,
                                                               out Button confirmedButton, out Button sendAgainButton, out Button changeEmailButton, out Button openGmailButton);

            // ---- controller ----
            var go = SceneBuilderCore.EnsureRoot("OnboardingStep1FlowController");
            var ctl = SceneBuilderCore.Ensure<OnboardingStep1FlowController>(go);
            SceneBuilderCore.SetField(ctl, "firstName", first); SceneBuilderCore.SetField(ctl, "middleName", middle); SceneBuilderCore.SetField(ctl, "lastName", last);
            SceneBuilderCore.SetField(ctl, "birthday", field); SceneBuilderCore.SetField(ctl, "age", age);
            SceneBuilderCore.SetField(ctl, "email", email); SceneBuilderCore.SetField(ctl, "password", pw); SceneBuilderCore.SetField(ctl, "retypePassword", retype);
            SceneBuilderCore.SetField(ctl, "emailCheck", emailCheck);
            SceneBuilderCore.SetField(ctl, "pwUpper", pReq[0]); SceneBuilderCore.SetField(ctl, "pwLower", pReq[1]);
            SceneBuilderCore.SetField(ctl, "pwNumber", pReq[2]); SceneBuilderCore.SetField(ctl, "pwSpecial", pReq[3]);
            SceneBuilderCore.SetField(ctl, "pwLength", pReq[4]); SceneBuilderCore.SetField(ctl, "pwMatch", mReq[0]);
            SceneBuilderCore.SetField(ctl, "ageMinimum", aReq[0]);
            SceneBuilderCore.SetField(ctl, "continueButton", c.cont); SceneBuilderCore.SetField(ctl, "continueAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);
            SceneBuilderCore.SetField(ctl, "emailMax", EmailLimit);
            SceneBuilderCore.SetField(ctl, "confirmPanel", confirmPanel);
            SceneBuilderCore.SetField(ctl, "confirmMessageText", confirmMessage); SceneBuilderCore.SetField(ctl, "confirmStatusText", confirmStatus);
            SceneBuilderCore.SetField(ctl, "confirmedButton", confirmedButton);
            SceneBuilderCore.SetField(ctl, "openGmailButton", openGmailButton);
            SceneBuilderCore.SetField(ctl, "sendAgainButton", sendAgainButton); SceneBuilderCore.SetField(ctl, "sendAgainLabel", AppKit.LabelOf(sendAgainButton));
            SceneBuilderCore.ReplaceText(ctl, "sendAgainText", "Resend", "Send again");                // one word on the button (24 Nov 2026)
            SceneBuilderCore.ReplaceText(ctl, "sendAgainWaitText", "Resend {0}s", "Send again in {0} s");
            SceneBuilderCore.SetField(ctl, "codeGroup", lastCodeGroup); SceneBuilderCore.SetField(ctl, "codeInput", lastCodeInput); SceneBuilderCore.SetField(ctl, "codeButton", lastCodeButton);
            SceneBuilderCore.SetField(ctl, "changeEmailButton", changeEmailButton); SceneBuilderCore.SetField(ctl, "useAnotherEmailButton", useAnotherEmail);
            // "Use another email?" is asked with the same pop-up as "Leave sign-up?"
            SceneBuilderCore.SetField(ctl, "questionDialog", c.leave != null ? c.leave.GetComponent<GlowUp.AppSettings.ConfirmDialog>() : null);

            FinishScene();
            SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] OnboardingStep1Scene built. Everything is inside FormPanel > FormScrollView > Viewport > Content; Continue is outside the panel.");
        }

        /// <summary>The link "Use another email" as a row of the form, right under the email's own line (a generated object: rebuilt on every run).</summary>
        static Button UseAnotherEmailLink(Transform content)
        {
            Transform old = Loose(content, "UseAnotherEmailButton");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Button link = AppKit.LinkButton(content, "UseAnotherEmailButton", "Use another email", 28, ThemeRole.AccentText);
            AppKit.Size(link, -1, 52);
            TextMeshProUGUI label = AppKit.LabelOf(link);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.margin = new Vector4(14, 0, 0, 0);
            Transform line = Loose(content, "EmailCheckText");
            if (line != null && line.parent == content) link.transform.SetSiblingIndex(line.GetSiblingIndex() + 1);
            link.gameObject.SetActive(false);
            return link;
        }

        /// <summary>
        /// ConfirmEmailPanel: what the user sees after Continue on a correctly filled page. The app has sent the "confirm your
        /// email" message; this pop-up says so, waits, and goes on by itself once the link was tapped. Its texts are examples:
        /// the real wording is on OnboardingStep1FlowController (Messages: confirming the email). A generated object, switched
        /// off in the Hierarchy: tick it active to see and style it.
        /// </summary>
        static OverlayPanel BuildConfirmEmailPanel(Transform canvas, out TextMeshProUGUI message, out TextMeshProUGUI status,
                                                   out Button confirmed, out Button sendAgain, out Button changeEmail, out Button openGmail)
        {
            Transform old = Loose(canvas, "ConfirmEmailPanel");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            AppKit.AppPopup p = AppKit.Overlay(new AppKit.AppScreen { canvas = canvas }, "ConfirmEmailPanel", "Confirm your email");
            AppKit.At(p.box, AppKit.MC, new Vector2(0, 40), new Vector2(900, 930));
            // Layout (24 Nov 2026, from the top; every block has its own band, so nothing overlaps and the keyboard never covers the code row):
            //   title 34-114 | message 150-350 | status 360-444 | code row 460-580 | three icon buttons 638-742 | primary 762-882 (box 930 high)

            message = AppKit.Label(p.box, "MessageText",
                "We sent a message to name@example.com. Open it and tap the link in it, then come back here. This page goes on by itself once the email is confirmed.",
                31, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            AppKit.TopStrip(message.transform, 150, 200, 48, 48);
            AppKit.ShrinkWrapped(message, 24);

            status = AppKit.Label(p.box, "StatusText", "Not confirmed yet. Open the message and tap its link (look in the spam folder too).",
                                  27, ThemeRole.AccentText, FontRole.Body, TextAlignmentOptions.Top, true);
            AppKit.TopStrip(status.transform, 360, 84, 48, 48);
            AppKit.ShrinkWrapped(status, 21);

            // ---- the 6-digit code row: hidden until GlowUp's server has mailed one (the controller shows it); the link in the e-mail still works ----
            GameObject codeGroup = AppKit.Node(p.box, "CodeGroup");
            AppKit.TopStrip(codeGroup.transform, 460, 120, 48, 48);
            TMP_InputField codeInput = SceneBuilderCore.MakeInput(codeGroup.transform, "CodeInputField", "6-digit code", AppKit.TL, Vector2.zero, new Vector2(660, 110));
            AppKit.At(codeInput.transform, AppKit.TL, new Vector2(0, -5), new Vector2(660, 110));
            codeInput.contentType = TMP_InputField.ContentType.IntegerNumber; codeInput.characterLimit = 6;
            StyleInput(codeInput, "6-digit code", ThemeRole.InputBackground);          // without this the typed digits were white on a light field (not visible)
            if (codeInput.textComponent != null) { codeInput.textComponent.alignment = TextAlignmentOptions.Center; codeInput.textComponent.fontSize = 46; codeInput.textComponent.characterSpacing = 10; }
            if (codeInput.placeholder is TMP_Text codePh) { codePh.alignment = TextAlignmentOptions.Center; codePh.fontSize = 32; }
            Button codeButton = AppKit.IconButton(codeGroup.transform, "CodeButton", IconFactory.Check, 110, 58, ThemeRole.ButtonText, ThemeRole.ButtonFill);
            AppKit.At(codeButton.transform, AppKit.TR, new Vector2(0, -5), new Vector2(110, 110));
            codeGroup.SetActive(false);
            lastCodeGroup = codeGroup; lastCodeInput = codeInput; lastCodeButton = codeButton;

            // ---- three equal icon buttons with one word each (24 Nov 2026) ----
            GameObject actions = AppKit.Node(p.box, "ActionRow");
            AppKit.BottomStrip(actions.transform, 188, 104, 48, 48);
            AppKit.HStack(actions, 16, 0, 0, 0, 0, TextAnchor.MiddleCenter, true);
            openGmail = AppKit.PillButton(actions.transform, "OpenGmailButton", "Gmail", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Mail);
            sendAgain = AppKit.PillButton(actions.transform, "SendAgainButton", "Resend", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Sync);
            changeEmail = AppKit.PillButton(actions.transform, "ChangeEmailButton", "Change", 30, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Swap);
            foreach (Button b in new[] { openGmail, sendAgain, changeEmail }) AppKit.Size(b, -1, 104, 1);

            confirmed = AppKit.PillButton(p.box, "ConfirmedButton", "Confirmed", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            AppKit.BottomStrip(confirmed.transform, 48, 120, 48, 48);
            return p.panel;
        }

        static GameObject lastCodeGroup; static TMP_InputField lastCodeInput; static Button lastCodeButton;

        /// <summary>Takes every object that belonged to the username out of the scene (label, field, reminder line, checklist rows).</summary>
        static void RemoveUsername(Transform canvas)
        {
            var doomed = new System.Collections.Generic.List<GameObject>();
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.Trim();
                if (n == "UsernameText" || n == "UsernameInputField" || n == "UsernameHelperText" || n.StartsWith("Username_Req")) doomed.Add(t.gameObject);
            }
            foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);      // a row's own children are in the list too: gone with their parent
        }

        // =====================================================================================
        //  Calendar popup (generated object: rebuilt on every run so it always matches the current design)
        // =====================================================================================
        public static DatePickerPopup BuildDatePopup(Transform card)
        {
            var old = card.Find("DatePickerPopup");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var root = new GameObject("DatePickerPopup", typeof(RectTransform), typeof(DatePickerPopup));
            root.transform.SetParent(card, false);
            Stretch(root.transform, 0, 0, 0, 0);

            // dim layer over the panel; it is also the object that is switched on/off
            var panel = new GameObject("DatePickerPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            Stretch(panel.transform, 0, 0, 0, 0);
            var dim = panel.GetComponent<Image>(); StyleRounded(dim); dim.color = new Color(0.14f, 0.12f, 0.12f, 0.6f);

            var box = new GameObject("CalendarBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(panel.transform, false);
            var brt = (RectTransform)box.transform; brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(920, 1060);
            var boxImg = box.GetComponent<Image>(); StyleRounded(boxImg); SetRadius(boxImg, 56f);
            SceneBuilderCore.Themed(box, ThemeRole.Background);

            var title = SceneBuilderCore.Text(box.transform, "CalendarTitle", "Select your birthday", 46, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(640, 70));
            SceneBuilderCore.Themed(title.gameObject, ThemeRole.Heading);

            var close = IconButton(box.transform, "CalendarCloseButton", IconFactory.Close, new Vector2(1, 1), new Vector2(-76, -80), 88, 44);
            var prev = IconButton(box.transform, "PreviousMonthButton", IconFactory.Back, new Vector2(0, 1), new Vector2(84, -200), 96, 52);
            var next = IconButton(box.transform, "NextMonthButton", IconFactory.Arrow, new Vector2(1, 1), new Vector2(-84, -200), 96, 52);

            var month = MakeDropdown(box.transform, "MonthDropdown", new Vector2(-135, -200), new Vector2(370, 110), 38);
            var year = MakeDropdown(box.transform, "YearDropdown", new Vector2(195, -200), new Vector2(250, 110), 38);

            // weekday header
            string[] wd = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            var head = new GameObject("WeekdayHeader", typeof(RectTransform), typeof(GridLayoutGroup));
            head.transform.SetParent(box.transform, false);
            var hrt = (RectTransform)head.transform; hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.anchoredPosition = new Vector2(0, -280); hrt.sizeDelta = new Vector2(832, 60);
            var hg = head.GetComponent<GridLayoutGroup>(); hg.cellSize = new Vector2(112, 60); hg.spacing = new Vector2(8, 0);
            hg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; hg.constraintCount = 7; hg.childAlignment = TextAnchor.MiddleCenter;
            foreach (var d in wd)
            {
                var l = NewText(head.transform, "WD_" + d, d);
                l.fontSize = 32; l.alignment = TextAlignmentOptions.Center;
                SceneBuilderCore.Themed(l.gameObject, ThemeRole.SecondaryText);
            }

            // the 42 day cells are real objects (Day_01 .. Day_42)
            var grid = new GameObject("DaysGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            grid.transform.SetParent(box.transform, false);
            var grt = (RectTransform)grid.transform; grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1); grt.pivot = new Vector2(0.5f, 1);
            grt.anchoredPosition = new Vector2(0, -352); grt.sizeDelta = new Vector2(832, 640);
            var gg = grid.GetComponent<GridLayoutGroup>(); gg.cellSize = new Vector2(112, 100); gg.spacing = new Vector2(8, 8);
            gg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gg.constraintCount = 7; gg.childAlignment = TextAnchor.UpperCenter;
            for (int i = 1; i <= 42; i++)
            {
                var cell = SceneBuilderCore.MakeButton(grid.transform, "Day_" + i.ToString("00"), i.ToString(), 38, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112, 100), Color.white);
                StyleRounded(cell.GetComponent<Image>());
                cell.transition = Selectable.Transition.None;      // the popup paints the cell colours itself
            }

            var popup = root.GetComponent<DatePickerPopup>();
            SceneBuilderCore.SetField(popup, "panelRoot", panel);
            SceneBuilderCore.SetField(popup, "monthDropdown", month); SceneBuilderCore.SetField(popup, "yearDropdown", year);
            SceneBuilderCore.SetField(popup, "previousMonthButton", prev); SceneBuilderCore.SetField(popup, "nextMonthButton", next);
            SceneBuilderCore.SetField(popup, "closeButton", close); SceneBuilderCore.SetField(popup, "daysGrid", grt);
            panel.SetActive(false);   // closed by default - tick DatePickerPanel in the Hierarchy to see and style it
            return popup;
        }

        /// <summary>A round, text-less button with a centred icon.</summary>
        static Button IconButton(Transform parent, string name, Sprite icon, Vector2 anchor, Vector2 pos, float size, float iconSize)
        {
            var b = SceneBuilderCore.MakeButton(parent, name, "", 30, anchor, pos, new Vector2(size, size), Color.white);
            var lbl = b.transform.Find("Label"); if (lbl != null) Object.DestroyImmediate(lbl.gameObject);
            var img = b.GetComponent<Image>(); StyleRounded(img); SetAlpha(img, 1f);
            SceneBuilderCore.Themed(b.gameObject, ThemeRole.InputBackground);
            var ic = Icon(b.transform, name.Replace("Button", "Icon"), icon, iconSize);
            SceneBuilderCore.Themed(ic.gameObject, ThemeRole.MainText);
            return b;
        }
    }
}
