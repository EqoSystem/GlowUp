using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Onboarding;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using static GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>Menus: GlowUp > Build Scene > 5 Onboarding Step 4 / 6 Onboarding Step 5 / 7 Terms And Conditions.</summary>
    public static class OnboardingFinalBuilders
    {
        [MenuItem("GlowUp/Build Scene/All Login + Onboarding + Terms", false, 91)]
        public static void BuildAll()
        {
            LoginSceneBuilder.Build();
            OnboardingStep1Builder.Build();
            OnboardingTileBuilders.BuildStep2();
            OnboardingTileBuilders.BuildStep3();
            BuildStep4();
            BuildStep5();
            BuildTerms();
            BuildSettingsSync.Run();
            Debug.Log("[GlowUp] Login, Onboarding 1-5 and Terms scenes built.");
        }

        // =====================================================================================
        //  Step 4 - allergens and sensitivities
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/5 Onboarding Step 4", false, 115)]
        public static void BuildStep4()
        {
            var scene = SceneBuilderCore.Open("OnboardingStep4Scene");
            var c = BuildChrome(4, "Allergen Selection");
            var ct = c.canvas;

            float y = HeaderBottom + 2;
            Transform headT = Loose(ct, "SectionHeader2Text");
            if (headT == null) headT = NewText(c.card, "SectionHeader2Text", "").transform;
            var head = headT.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(head, "Known Skin Allergens & Sensitivities", HeaderSize, TextAlignmentOptions.MidlineLeft);
            Top(headT, c.card, y, 56); Role(head.transform, ThemeRole.Heading); y += 58;
            var desc = TextOn(c.card, "AllergenHintText", "", HelperSize, TextAlignmentOptions.MidlineLeft);
            SetText(desc, "Tick all that apply, add your own, or tick \"No known allergens\".", HelperSize, TextAlignmentOptions.MidlineLeft);
            desc.enableWordWrapping = false; desc.enableAutoSizing = true; desc.fontSizeMin = 22; desc.fontSizeMax = HelperSize;
            Top(desc.transform, c.card, y, 44); Role(desc.transform, ThemeRole.SecondaryText); y += 54;

            var oldList = Loose(ct, "UserAddedAllergenListBox");     // superseded by the checklist; hidden, not removed
            if (oldList != null) oldList.gameObject.SetActive(false);

            var panel = ChecklistBuilder.Build(c.card, "AllergenContainerCard", y,
                new[] { "Fragrance / Parfum", "Parabens", "Sulfates (SLS / SLES)", "Formaldehyde releasers", "Lanolin", "Essential oils", "Nut-derived oils" },
                "Not listed? Add it here", Loose(ct, "UserAllergenInputField"), Loose(ct, "AddButton"), Loose(ct, "AllergenContainerCard"), c.feedback,
                noneOption: GlowUp.Data.UserProfileData.NoKnownAllergens);

            var go = SceneBuilderCore.EnsureRoot("OnboardingStep4FlowController");
            var ctl = SceneBuilderCore.Ensure<OnboardingStep4FlowController>(go);
            SceneBuilderCore.SetField(ctl, "allergens", panel);
            SceneBuilderCore.SetField(ctl, "continueButton", c.cont); SceneBuilderCore.SetField(ctl, "continueAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);

            // the earlier, vaguer wording of the "nothing selected" message is replaced once; your own wording is kept
            var so = new SerializedObject(ctl);
            var message = so.FindProperty("msgChooseOne");
            const string wording = "Select at least one, or tick \"No known allergens\", to continue.";
            if (message != null && (string.IsNullOrEmpty(message.stringValue) || message.stringValue == "Please choose at least one option to continue."
                                    || message.stringValue == "Select at least one allergen or sensitivity to continue."))
            {
                message.stringValue = wording;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            FinishScene(); SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] OnboardingStep4Scene built. Continue stays locked until at least one allergen or sensitivity, or the Option_None row (No known allergens), is ticked. Edit each Option_* row's Label text; the row animation is ChecklistRowAnimator on every row.");
        }

        // =====================================================================================
        //  Step 5 - profile summary
        //  Plain "Label ... Value" lines. Each line holds an input field (or dropdown) that stays
        //  INACTIVE until the user taps the Edit button above "Generate Profile".
        // =====================================================================================
        const float SummaryLineHeight = 68, SummaryEditorHeight = 116;

        [MenuItem("GlowUp/Build Scene/6 Onboarding Step 5", false, 116)]
        public static void BuildStep5()
        {
            var scene = SceneBuilderCore.Open("OnboardingStep5Scene");
            var c = BuildChrome(5, "Profile Summary", continueLabel: "Generate Profile");
            var ct = c.canvas;

            // old read-only summary objects of the original scene: hidden (not removed), the new summary replaces them
            foreach (var n in new[] { "ProfileDetailsText", "KnownAllegensText", "AllergenChipsContainer" })
            { var o = Loose(ct, n); if (o != null) o.gameObject.SetActive(false); }

            // footer: Generate Profile (from the chrome) < Edit button < message line
            const float editY = 204, editH = 96, messageY = 308, panelBottom = 366;
            var edit = SceneBuilderCore.MakeButton(c.card, "EditButton", "Edit", 40, new Vector2(0.5f, 0), Vector2.zero, new Vector2(320, editH), Color.white);
            if (edit.transform.parent != c.card) edit.transform.SetParent(c.card, false);
            var ert = (RectTransform)edit.transform; ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0); ert.pivot = new Vector2(0.5f, 0);
            ert.anchoredPosition = new Vector2(0, editY); ert.sizeDelta = new Vector2(320, editH);
            var editImg = SceneBuilderCore.Ensure<Image>(edit.gameObject); StyleRounded(editImg); SetAlpha(editImg, 1f); edit.targetGraphic = editImg;
            SceneBuilderCore.Themed(edit.gameObject, ThemeRole.ButtonFill);
            var editLabel = edit.GetComponentInChildren<TextMeshProUGUI>(true);
            SetText(editLabel, "Edit", 40, TextAlignmentOptions.Center);
            SceneBuilderCore.Themed(editLabel.gameObject, ThemeRole.ButtonText);
            BottomRow(c.feedback.transform, c.card, messageY, 50);

            // the scene's SummaryContainerCard becomes the panel that holds the summary lines
            var root = EnsurePanel(c.card, "SummaryContainerCard", ThemeRole.SurfaceAlt, PanelAlpha, Loose(ct, "SummaryContainerCard"));
            TopBottom(root, c.card, HeaderBottom, panelBottom);
            foreach (Transform child in root) if (child.GetComponent<TextMeshProUGUI>() != null) child.gameObject.SetActive(false);

            var scrollRt = MakeScroll(root, "SummaryScrollView", out var content);
            Stretch(scrollRt, 16, 16, 16, 16);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 24, 8, 8); layout.spacing = 6;

            // the lines are generated objects: rebuilt on every run
            var stale = new List<GameObject>();
            foreach (Transform child in content) stale.Add(child.gameObject);
            foreach (var s in stale) Object.DestroyImmediate(s);

            var rows = new List<SummaryRow>();
            var firstRow = NewRow(content, "FirstName", "First Name", rows); var first = AddInput(firstRow, "First name", 50);
            var middleRow = NewRow(content, "MiddleName", "Middle Name", rows); var middle = AddInput(middleRow, "Middle name", 50);
            var lastRow = NewRow(content, "LastName", "Last Name", rows); var last = AddInput(lastRow, "Last name", 50);
            var bdayRow = NewRow(content, "Birthday", "Birthday", rows); var bday = AddInput(bdayRow, "Tap to choose", 30);
            var ageRow = NewRow(content, "Age", "Age", rows);
            SceneBuilderCore.SetField(ageRow, "editable", false);                       // calculated from the birthday
            var emailRow = NewRow(content, "Email", "Email", rows); var email = AddInput(emailRow, "name@example.com", EmailLimit, TMP_InputField.ContentType.EmailAddress);
            AddSpaceGuard(email, c.feedback, "Spaces are not allowed in the email.", EmailLimit);
            // the same email rule as Step 1; its messages show in the message line of this page. The script sits on the row,
            // which is always on (the editor inside it is switched off until Edit)
            var emailCheck = AddEmailCheck(email, host: emailRow.gameObject);
            var typeRow = NewRow(content, "SkinType", "Skin Type", rows); var skinType = AddDropdown(typeRow);
            var toneRow = NewRow(content, "SkinTone", "Skin Tone", rows); var skinTone = AddDropdown(toneRow);
            var condRow = NewRow(content, "Conditions", "Conditions", rows); var conditions = AddInput(condRow, "e.g. Acne, Eczema", 200);
            var allergenRow = NewRow(content, "Allergens", "Allergens", rows); var allergens = AddInput(allergenRow, "e.g. Fragrance, Parabens", 200);

            var popup = OnboardingStep1Builder.BuildDatePopup(c.card);
            var field = SceneBuilderCore.Ensure<DatePickerField>(bday.gameObject);
            SceneBuilderCore.SetField(field, "popup", popup);
            SceneBuilderCore.SetField(c.leave, "calendar", popup);          // the phone's back key closes the calendar before anything else

            var go = SceneBuilderCore.EnsureRoot("OnboardingStep5FlowController");
            var ctl = SceneBuilderCore.Ensure<OnboardingStep5FlowController>(go);
            SetArray(ctl, "rows", rows.ToArray());
            SceneBuilderCore.SetField(ctl, "ageRow", ageRow);
            SceneBuilderCore.SetField(ctl, "firstName", first); SceneBuilderCore.SetField(ctl, "middleName", middle); SceneBuilderCore.SetField(ctl, "lastName", last);
            SceneBuilderCore.SetField(ctl, "birthday", field);
            SceneBuilderCore.SetField(ctl, "email", email); SceneBuilderCore.SetField(ctl, "emailCheck", emailCheck); SceneBuilderCore.SetField(ctl, "emailMax", EmailLimit);
            SceneBuilderCore.SetField(ctl, "skinType", skinType); SceneBuilderCore.SetField(ctl, "skinTone", skinTone);
            SceneBuilderCore.SetField(ctl, "conditions", conditions); SceneBuilderCore.SetField(ctl, "allergens", allergens);
            SceneBuilderCore.SetField(ctl, "editButton", edit); SceneBuilderCore.SetField(ctl, "editButtonLabel", editLabel);
            SceneBuilderCore.SetField(ctl, "scroll", scrollRt.GetComponent<ScrollRect>());
            SceneBuilderCore.SetField(ctl, "generateButton", c.cont); SceneBuilderCore.SetField(ctl, "generateAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);

            FinishScene(); SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] OnboardingStep5Scene built. Each SummaryRow_* has an Editor child that is inactive until the user taps Edit.");
        }

        /// <summary>SummaryRow_Key [round strip + SummaryRow] > Line (Label left, ValueText right).</summary>
        static SummaryRow NewRow(Transform content, string key, string labelText, List<SummaryRow> all)
        {
            var go = new GameObject("SummaryRow_" + key, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(SummaryRow));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>(); StyleRounded(img); SetRadius(img, 36f); img.raycastTarget = false;
            SceneBuilderCore.Themed(go, ThemeRole.InputBackground);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 8, 8); v.spacing = 6; v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var line = new GameObject("Line", typeof(RectTransform), typeof(LayoutElement));
            line.transform.SetParent(go.transform, false);
            var le = line.GetComponent<LayoutElement>(); le.preferredHeight = SummaryLineHeight; le.minHeight = SummaryLineHeight; le.flexibleHeight = 0;

            var label = ChildText(line.transform, "Label", labelText, 34, TextAlignmentOptions.MidlineLeft);
            label.enableWordWrapping = false;
            var lrt = label.rectTransform; lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0.36f, 1); lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(10, 0); lrt.offsetMax = Vector2.zero;
            SceneBuilderCore.Themed(label.gameObject, ThemeRole.SecondaryText);

            var value = ChildText(line.transform, "ValueText", "Not set", 36, TextAlignmentOptions.MidlineRight);
            value.enableWordWrapping = true; value.overflowMode = TextOverflowModes.Ellipsis;
            value.enableAutoSizing = true; value.fontSizeMin = 24; value.fontSizeMax = 36;      // long values shrink, then wrap to two lines
            var vrt = value.rectTransform; vrt.anchorMin = new Vector2(0.36f, 0); vrt.anchorMax = new Vector2(1, 1); vrt.pivot = new Vector2(0.5f, 0.5f);
            vrt.offsetMin = new Vector2(8, 0); vrt.offsetMax = new Vector2(-10, 0);
            SceneBuilderCore.Themed(value.gameObject, ThemeRole.MainText);

            var row = go.GetComponent<SummaryRow>();
            SceneBuilderCore.SetField(row, "label", label);
            SceneBuilderCore.SetField(row, "valueText", value);
            all.Add(row);
            return row;
        }

        /// <summary>The input field of a summary line. It sits under the label and is INACTIVE until Edit is tapped.</summary>
        static TMP_InputField AddInput(SummaryRow row, string placeholder, int limit, TMP_InputField.ContentType type = TMP_InputField.ContentType.Standard, bool alnum = false)
        {
            var f = SceneBuilderCore.MakeInput(row.transform, "Editor", placeholder, new Vector2(0.5f, 1), Vector2.zero, new Vector2(700, SummaryEditorHeight));
            var le = SceneBuilderCore.Ensure<LayoutElement>(f.gameObject); le.preferredHeight = SummaryEditorHeight; le.minHeight = SummaryEditorHeight; le.flexibleHeight = 0;
            StyleInput(f, placeholder, ThemeRole.Background);
            var b = SceneBuilderCore.Ensure<InputFieldBehaviour>(f.gameObject);
            SceneBuilderCore.SetField(b, "characterLimit", limit);
            SceneBuilderCore.SetField(b, "contentType", (int)type);
            SceneBuilderCore.SetField(b, "alphanumericOnly", alnum);
            SceneBuilderCore.SetField(row, "editor", f.gameObject);
            SceneBuilderCore.SetField(row, "input", f);
            f.gameObject.SetActive(false);
            return f;
        }

        /// <summary>The dropdown of a summary line (skin type / tone). INACTIVE until Edit is tapped.</summary>
        static TMP_Dropdown AddDropdown(SummaryRow row)
        {
            var d = MakeDropdown(row.transform, "Editor", Vector2.zero, new Vector2(700, SummaryEditorHeight));
            var le = SceneBuilderCore.Ensure<LayoutElement>(d.gameObject); le.preferredHeight = SummaryEditorHeight; le.minHeight = SummaryEditorHeight; le.flexibleHeight = 0;
            var img = d.GetComponent<Image>(); if (img != null) SceneBuilderCore.Themed(img.gameObject, ThemeRole.Background);
            SceneBuilderCore.SetField(row, "editor", d.gameObject);
            SceneBuilderCore.SetField(row, "dropdown", d);
            d.gameObject.SetActive(false);
            return d;
        }

        // =====================================================================================
        //  Privacy Policy / Terms
        // =====================================================================================
        [MenuItem("GlowUp/Build Scene/7 Terms And Conditions", false, 117)]
        public static void BuildTerms()
        {
            var scene = SceneBuilderCore.Open("TermsAndConditionScene");
            var c = BuildChrome(5, "Privacy Policy & Terms", continueLabel: "Continue", subtitleExisting: "PrivacyPolicySubtitleText");
            var ct = c.canvas;
            const float toggleY = FooterTop + 6, toggleH = 110, healthY = toggleY + toggleH + 8;      // the health-information box sits above the policy box

            // ---- the scrollable policy text ----
            var scrollT = Loose(ct, "PrivacyPolicy_Scroll View");
            ScrollRect scroll = scrollT != null ? scrollT.GetComponent<ScrollRect>() : null;
            if (scrollT != null)
            {
                var panel = EnsurePanel(c.card, "PrivacyPolicy_Scroll View", ThemeRole.SurfaceAlt, PanelAlpha, scrollT);
                TopBottom(panel, c.card, HeaderBottom, healthY + toggleH + 14);
                if (scroll != null)
                {
                    scroll.vertical = true; scroll.horizontal = false; scroll.scrollSensitivity = 40;
                    scroll.horizontalScrollbar = null;
                    scroll.movementType = ScrollRect.MovementType.Elastic;
                    for (int i = scroll.onValueChanged.GetPersistentEventCount() - 1; i >= 0; i--)
                        UnityEventTools.RemovePersistentListener(scroll.onValueChanged, i);
                    EnsureScrollbar(scroll);                       // also stops the ScrollRect from resizing the viewport

                    // viewport: padded inside the round panel
                    RectTransform vp = scroll.viewport;
                    if (vp == null) { var v = scrollT.Find("Viewport"); if (v != null) { vp = (RectTransform)v; scroll.viewport = vp; } }
                    if (vp != null) Stretch(vp, 30, 26, 38, 26);

                    // content: exactly as wide as the viewport (it used to be wider, which cut the text on both sides)
                    // and as tall as the document, so the page scrolls
                    RectTransform content = scroll.content;
                    if (content == null && vp != null) { var ch = vp.Find("Content"); if (ch != null) { content = (RectTransform)ch; scroll.content = content; } }
                    if (content != null)
                    {
                        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
                        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, content.sizeDelta.y);
                        var layout = SceneBuilderCore.Ensure<VerticalLayoutGroup>(content.gameObject);
                        layout.enabled = true; layout.padding = new RectOffset(4, 8, 8, 40); layout.spacing = 0; layout.childAlignment = TextAnchor.UpperLeft;
                        layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
                        var fitter = SceneBuilderCore.Ensure<ContentSizeFitter>(content.gameObject);
                        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                        // the document itself comes from Assets/_Project/Legal/PrivacyPolicyAndTerms.txt (edit that file to change the wording)
                        var doc = content.GetComponentInChildren<TextMeshProUGUI>(true);
                        if (doc == null) doc = NewText(content, "PolicyText", "");
                        doc.enableAutoSizing = false; doc.fontSize = 34; doc.enableWordWrapping = true; doc.richText = true;
                        doc.alignment = TextAlignmentOptions.TopLeft; doc.overflowMode = TextOverflowModes.Overflow; doc.lineSpacing = 6; doc.paragraphSpacing = 10;
                        doc.margin = Vector4.zero; doc.raycastTarget = false;
                        SceneBuilderCore.Themed(doc.gameObject, ThemeRole.MainText);
                        const string legalPath = "Assets/_Project/Legal/PrivacyPolicyAndTerms.txt";
                        var legal = AssetDatabase.LoadAssetAtPath<TextAsset>(legalPath);
                        if (legal == null) { AssetDatabase.ImportAsset(legalPath); legal = AssetDatabase.LoadAssetAtPath<TextAsset>(legalPath); }
                        var source = SceneBuilderCore.Ensure<LegalTextSource>(doc.gameObject);
                        SceneBuilderCore.SetField(source, "document", legal);
                    }
                }
            }

            // ---- "I have read ..." checkbox: unlocked once the user reaches the bottom ----
            var toggleT = Loose(ct, "Toggle");
            Toggle toggle = toggleT != null ? toggleT.GetComponent<Toggle>() : null;
            TextMeshProUGUI toggleLabel = null;
            if (toggle != null)
            {
                RetireLegacy(toggleT.gameObject);
                for (int i = toggle.onValueChanged.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(toggle.onValueChanged, i);
                BottomRow(toggleT, c.card, toggleY, toggleH);

                Image boxImg = null; RectTransform check = null;
                Transform box = toggleT.Find("Background");
                if (box != null)
                {
                    var brt = (RectTransform)box; brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f); brt.pivot = new Vector2(0, 0.5f);
                    brt.anchoredPosition = new Vector2(8, 0); brt.sizeDelta = new Vector2(64, 64);
                    boxImg = box.GetComponent<Image>(); if (boxImg != null) StyleRounded(boxImg);
                    Transform ck = box.Find("Checkmark");
                    if (ck != null)
                    {
                        check = (RectTransform)ck; Stretch(ck, 13, 13, 13, 13);
                        var ci = ck.GetComponent<Image>();
                        if (ci != null) { ci.sprite = IconFactory.Check; ci.type = Image.Type.Simple; ci.preserveAspect = true; ci.raycastTarget = false; }
                        ck.localScale = Vector3.zero;             // unticked; the animator snaps it in
                    }
                }
                // The scene's own "Label" is an old-style UI Text (it cannot use the project fonts), so a TextMeshPro
                // label takes its place. The old one is hidden, not removed.
                Transform oldLabel = toggleT.Find("Label");
                if (oldLabel != null && oldLabel.GetComponent<TextMeshProUGUI>() == null) oldLabel.gameObject.SetActive(false);
                toggleLabel = ChildText(toggleT, "ReadLabel", "I have read and agree to the Privacy Policy and the Terms & Conditions", 32, TextAlignmentOptions.MidlineLeft);
                toggleLabel.enableWordWrapping = true; toggleLabel.raycastTarget = false;
                var lrt = toggleLabel.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = new Vector2(92, 0); lrt.offsetMax = Vector2.zero;
                SceneBuilderCore.Themed(toggleLabel.gameObject, ThemeRole.MainText);

                // same tick animation as the checklists (scale punch, colour change, checkmark snap)
                toggle.graphic = null;
                if (boxImg != null) toggle.targetGraphic = boxImg;
                var anim = SceneBuilderCore.Ensure<ChecklistRowAnimator>(toggleT.gameObject);
                SceneBuilderCore.SetField(anim, "checkBoxBackground", boxImg);
                SceneBuilderCore.SetField(anim, "checkmark", check);
                SceneBuilderCore.SetField(anim, "punchScale", 1.02f);
            }

            // ---- second box: consent for health information (skin conditions and allergies), a copy of the first so it looks the same ----
            Toggle healthToggle = null; TextMeshProUGUI healthLabel = null;
            if (toggle != null)
            {
                Transform old = toggleT.parent.Find("HealthToggle");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var healthT = Object.Instantiate(toggleT.gameObject, toggleT.parent).transform;
                healthT.name = "HealthToggle";
                healthT.gameObject.SetActive(true);
                BottomRow(healthT, c.card, healthY, toggleH);
                healthToggle = healthT.GetComponent<Toggle>();
                healthToggle.isOn = false;
                Transform label = healthT.Find("ReadLabel");
                if (label != null) healthLabel = label.GetComponent<TextMeshProUGUI>();
                if (healthLabel != null) { healthLabel.text = "I agree that GlowUp keeps my skin type, skin tone, skin conditions and allergies, to personalise my results"; healthLabel.fontSize = 28; }
            }

            var go = SceneBuilderCore.EnsureRoot("TermsAcceptanceFlowController");
            var ctl = SceneBuilderCore.Ensure<TermsAcceptanceFlowController>(go);
            SceneBuilderCore.SetField(ctl, "healthToggle", healthToggle); SceneBuilderCore.SetField(ctl, "healthLabel", healthLabel);
            SceneBuilderCore.SetField(ctl, "policyScroll", scroll); SceneBuilderCore.SetField(ctl, "readToggle", toggle);
            SceneBuilderCore.SetField(ctl, "toggleLabel", toggleLabel);
            SceneBuilderCore.SetField(ctl, "continueButton", c.cont); SceneBuilderCore.SetField(ctl, "continueAnimator", c.anim);
            SceneBuilderCore.SetField(ctl, "backButton", c.back); SceneBuilderCore.SetField(ctl, "feedbackText", c.feedback);

            FinishScene(); SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] TermsAndConditionScene built. Account creation happens when the user ticks the box and taps Continue.");
        }
    }
}
