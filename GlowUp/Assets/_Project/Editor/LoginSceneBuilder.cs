using GlowUp.Auth;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Kit = GlowUp.EditorTools.OnboardingKit;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Build Scene > 1 Login.
    /// Adds missing Login/Sign-up objects and behaviours to LogInScene, then arranges the whole
    /// screen. Safe to run again: existing objects are reused by name, nothing of yours is deleted.
    /// The canvas is 1080 wide on every phone; the panel has equal 40-unit margins on all four sides.
    /// </summary>
    public static class LoginSceneBuilder
    {
        // ---- layout (canvas units; the panel is 1000 wide) ----
        const float Inner = Kit.InnerWidth;
        const float FormStartY = 170;            // from the panel top to the title
        const float TitleFont = 120, SubtitleFont = 46, LabelFont = 38, LinkFont = 34, SmallFont = 30;
        const float SocialSize = 120, SocialGap = 36;

        [MenuItem("GlowUp/Build Scene/1 Login", false, 111)]
        public static void Build()
        {
            var scene = SceneBuilderCore.Open("LogInScene");
            var canvas = SceneBuilderCore.EnsureCanvas();
            var ct = canvas.transform;
            SceneBuilderCore.EnsureSceneServices();

            Transform card = Kit.Loose(ct, "LogInCard");
            if (card == null) { var g = new GameObject("LogInCard", typeof(RectTransform), typeof(Image)); g.transform.SetParent(ct, false); card = g.transform; }
            var bg = Kit.Loose(ct, "DarkBackground");
            var title = Kit.Loose(ct, "TitleText");
            var subtitle = Kit.Loose(ct, "SubtitleText");
            var emailLabel = Kit.Loose(ct, "EmailText");
            var emailField = Kit.Loose(ct, "EmailInputField");
            var pwLabel = Kit.Loose(ct, "PasswordText");
            var pwField = Kit.Loose(ct, "PasswordInputField");
            var forgot = Kit.Loose(ct, "ForgotPasswordText");
            var signUp = Kit.Loose(ct, "SignUpPromptText");
            var signIn = Kit.Loose(ct, "SignInButton");
            var feedback = Kit.Loose(ct, "ErrorFeedbackText");
            var termsText = Kit.Loose(ct, "TermsText");

            TMP_InputField emailInput = emailField != null ? emailField.GetComponent<TMP_InputField>() : null;
            TMP_InputField pwInput = pwField != null ? pwField.GetComponent<TMP_InputField>() : null;
            TextMeshProUGUI fb = feedback != null ? feedback.GetComponentInChildren<TextMeshProUGUI>(true) : null;

            // ----- wording -----
            SetText(title, "Glow Up"); SetText(subtitle, "Welcome!"); SetText(emailLabel, "Email:"); SetText(pwLabel, "Password:");
            SetText(forgot, "Forgot Password?"); SetText(signUp, "Sign Up"); SetText(signIn, "Log-In");
            if (fb != null)
            {
                fb.text = "";
                SceneBuilderCore.Ensure<AutoHideText>(fb.gameObject);       // a message stays 3 seconds, then fades out (set the time on this component)
            }

            // ----- hint rows under the fields were removed on request (Login is for existing users) -----
            foreach (var n in new[] { "EmailHelperText", "PasswordHelperText" })
            { var h = Kit.Loose(ct, n); if (h != null) Object.DestroyImmediate(h.gameObject); }

            // ----- dark mode toggle (inside the panel, top-right) -----
            var darkBtn = SceneBuilderCore.MakeButton(ct, "DarkModeButton", "", 40, new Vector2(1, 1), Vector2.zero, new Vector2(100, 100), new Color(1, 1, 1, 0.004f));
            var oldGlyph = darkBtn.transform.Find("Label"); if (oldGlyph != null) Object.DestroyImmediate(oldGlyph.gameObject);
            var darkIcon = Kit.Icon(darkBtn.transform, "DarkModeIcon", IconFactory.Moon, 60);
            var dm = SceneBuilderCore.Ensure<DarkModeToggleButton>(darkBtn.gameObject);
            SceneBuilderCore.SetField(dm, "iconImage", darkIcon);
            SceneBuilderCore.SetField(dm, "moonIcon", IconFactory.Moon);
            SceneBuilderCore.SetField(dm, "sunIcon", IconFactory.Sun);
            SceneBuilderCore.Themed(darkIcon.gameObject, ThemeRole.MainText);

            // ----- inputs: 64 characters max, no spaces (with an immediate message), eye toggle on the password -----
            if (emailInput != null)
            {
                Kit.StyleInput(emailInput, "Enter your email", ThemeRole.InputBackground);
                ClearTypedText(emailInput);
                var b = SceneBuilderCore.Ensure<InputFieldBehaviour>(emailField.gameObject);
                SceneBuilderCore.SetField(b, "characterLimit", 64);
                SceneBuilderCore.SetField(b, "contentType", (int)TMP_InputField.ContentType.EmailAddress);
                Kit.AddSpaceGuard(emailInput, fb, "Spaces are not allowed in the email.");
            }
            if (pwInput != null)
            {
                Kit.StyleInput(pwInput, "Enter your password", ThemeRole.InputBackground);
                ClearTypedText(pwInput);
                Kit.AddEye(pwInput);
                Kit.AddSpaceGuard(pwInput, fb, "Spaces are not allowed in the password.");
            }

            // ----- social sign-in (icons only) and the legal links -----
            var orText = SceneBuilderCore.Text(ct, "OrContinueWithText", "or continue with", SmallFont, new Vector2(0.5f, 1), Vector2.zero, new Vector2(600, 50));
            var oldInstagram = Kit.Loose(ct, "InstagramSignInButton");   // removed on request
            if (oldInstagram != null) Object.DestroyImmediate(oldInstagram.gameObject);
            var google = Social(ct, "GoogleSignInButton", IconFactory.Google);
            var facebook = Social(ct, "FacebookSignInButton", IconFactory.Facebook);
            var privacy = SceneBuilderCore.MakeButton(ct, "PrivacyPolicyLink", "Privacy Policy", SmallFont, new Vector2(0.5f, 0), Vector2.zero, new Vector2(330, 70), new Color(0, 0, 0, 0));
            var terms = SceneBuilderCore.MakeButton(ct, "TermsAndConditionsLink", "Terms & Conditions", SmallFont, new Vector2(0.5f, 0), Vector2.zero, new Vector2(400, 70), new Color(0, 0, 0, 0));

            var forgotBtn = MakeClickable(forgot); var signUpBtn = MakeClickable(signUp);
            Button signInBtn = signIn != null ? SceneBuilderCore.Ensure<Button>(signIn.gameObject) : null;
            if (signInBtn != null) Kit.ClearClicks(signInBtn);          // the old LoginController click skipped real authentication

            // ----- arrange -----
            if (bg != null) { Reparent(bg, ct); bg.SetAsFirstSibling(); Kit.Stretch(bg, 0, 0, 0, 0); }

            var crt = (RectTransform)card;                                // the panel: equal margins on all four sides
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = new Vector2(Kit.CardMargin, Kit.CardMargin); crt.offsetMax = new Vector2(-Kit.CardMargin, -Kit.CardMargin);
            foreach (var lg in card.GetComponents<LayoutGroup>()) lg.enabled = false;
            var cardImg = SceneBuilderCore.Ensure<Image>(card.gameObject);
            Kit.StyleRounded(cardImg); Kit.SetAlpha(cardImg, Kit.CardAlpha);
            Kit.EnsureOutline(card, "PanelOutline", ThemeRole.Gold);

            Reparent(darkBtn.transform, card);
            var drt = (RectTransform)darkBtn.transform; drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1, 1);
            drt.anchoredPosition = new Vector2(-30, -30); drt.sizeDelta = new Vector2(100, 100);

            float y = FormStartY;
            TopRow(title, card, y, 160, TitleFont, TextAlignmentOptions.Center); y += 160;
            TopRow(subtitle, card, y, 64, SubtitleFont, TextAlignmentOptions.Center); y += 64 + 56;

            TopRow(emailLabel, card, y, 52, LabelFont, TextAlignmentOptions.BottomLeft); y += 52 + 8;
            TopRow(emailField, card, y, Kit.FieldHeight, null, null); y += Kit.FieldHeight + 36;

            TopRow(pwLabel, card, y, 52, LabelFont, TextAlignmentOptions.BottomLeft); y += 52 + 8;
            TopRow(pwField, card, y, Kit.FieldHeight, null, null); y += Kit.FieldHeight + 24;

            // Forgot Password (left)  |  Sign Up (right)
            Half(forgot, card, y, 60, LinkFont, true); Half(signUp, card, y, 60, LinkFont, false); y += 60 + 12;

            TopRow(feedback, card, y, 52, SmallFont, TextAlignmentOptions.Center); y += 52 + 12;
            TopRow(signIn, card, y, Kit.ButtonHeight, null, null); y += Kit.ButtonHeight + 44;
            if (signIn != null)
            {
                var img = SceneBuilderCore.Ensure<Image>(signIn.gameObject); Kit.StyleRounded(img); Kit.SetAlpha(img, 1f);
                if (signInBtn != null) signInBtn.targetGraphic = img;
                var label = signIn.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) { Kit.SetText(label, null, Kit.ButtonSize, TextAlignmentOptions.Center); Kit.Stretch(label.transform, 0, 0, 0, 0); }
            }

            // "or continue with" + round icon buttons, close together, right under Sign In
            TopAt(orText.transform, card, 0, y, 600, 50); Kit.SetText(orText, null, SmallFont, TextAlignmentOptions.Center); y += 50 + 20;
            Button[] social = { google, facebook };
            for (int i = 0; i < social.Length; i++)
                TopAt(social[i].transform, card, (i - (social.Length - 1) / 2f) * (SocialSize + SocialGap), y, SocialSize, SocialSize);

            // legal text + links: anchored to the panel bottom
            BottomAt(privacy.transform, card, -200, 36, 330, 70, SmallFont);
            BottomAt(terms.transform, card, 190, 36, 400, 70, SmallFont);
            BottomAt(termsText, card, 0, 110, Inner, 70, 28);

            // ----- theme roles -----
            Role(bg, ThemeRole.Background); Role(card, ThemeRole.Surface);
            Role(title, ThemeRole.Gold); Role(subtitle, ThemeRole.SecondaryText);       // the logo is the one large gold text
            Role(emailLabel, ThemeRole.MainText); Role(pwLabel, ThemeRole.MainText);
            Role(forgot, ThemeRole.SecondaryText); Role(signUp, ThemeRole.AccentText);  // a pink link that is readable as small text
            Role(termsText, ThemeRole.SecondaryText); Role(feedback, ThemeRole.Warning);
            Role(orText.transform, ThemeRole.SecondaryText);
            Role(signIn, ThemeRole.ButtonFill); RoleLabel(signIn, ThemeRole.ButtonText);
            foreach (var s in social) Role(s.transform, ThemeRole.InputBackground);
            RoleLabel(privacy.transform, ThemeRole.SecondaryText); RoleLabel(terms.transform, ThemeRole.SecondaryText);

            // ----- controller -----
            var flow = SceneBuilderCore.Ensure<LoginFlowController>(SceneBuilderCore.EnsureRoot("LoginFlowController"));
            SceneBuilderCore.SetField(flow, "emailInput", emailInput);
            SceneBuilderCore.SetField(flow, "passwordInput", pwInput);
            SceneBuilderCore.SetField(flow, "feedbackText", fb);
            SceneBuilderCore.SetField(flow, "signInButton", signInBtn);
            SceneBuilderCore.SetField(flow, "forgotPasswordButton", forgotBtn);
            SceneBuilderCore.SetField(flow, "signUpButton", signUpBtn);
            SceneBuilderCore.SetField(flow, "googleButton", google);
            SceneBuilderCore.SetField(flow, "facebookButton", facebook);
            SceneBuilderCore.SetField(flow, "privacyPolicyButton", privacy);
            SceneBuilderCore.SetField(flow, "termsButton", terms);

            ThemeKit.FinishScene();       // canvas scaler, paper background, input text, round shapes, fonts, colours
            SceneBuilderCore.Save(scene);
            Debug.Log("[GlowUp] LogInScene built and arranged. Tune LoginFlowController / ThemeManager / ThemeProfile (colours + fonts) in the Inspector.");
        }

        // =====================================================================================
        //  Layout helpers (all relative to the panel)
        // =====================================================================================
        /// <summary>Row anchored to the panel top, stretched across with the standard side padding.</summary>
        static void TopRow(Transform t, Transform parent, float y, float h, float? font, TextAlignmentOptions? align)
        {
            if (t == null) return;
            Kit.Top(t, parent, y, h);
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null && font.HasValue)
            {
                Kit.SetText(tmp, null, font.Value, align ?? TextAlignmentOptions.Center);
                // field labels line up with the text inside the round fields
                tmp.margin = align == TextAlignmentOptions.BottomLeft ? new Vector4(12, 0, 0, 4) : Vector4.zero;
            }
        }

        /// <summary>Half-width text on the left or right of a row (Forgot Password | Sign Up).</summary>
        static void Half(Transform t, Transform parent, float y, float h, float font, bool left)
        {
            if (t == null) return;
            Reparent(t, parent);
            var rt = (RectTransform)t;
            rt.anchorMin = new Vector2(left ? 0f : 0.5f, 1); rt.anchorMax = new Vector2(left ? 0.5f : 1f, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left ? Kit.PadX + 8 : 0, -y - h); rt.offsetMax = new Vector2(left ? 0 : -(Kit.PadX + 8), -y);
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                Kit.SetText(tmp, null, font, left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                tmp.enableWordWrapping = false;
            }
        }

        /// <summary>Fixed-size object, centred horizontally at offset x, measured from the panel top.</summary>
        static void TopAt(Transform t, Transform parent, float x, float y, float w, float h)
        {
            if (t == null) return;
            Reparent(t, parent);
            var rt = (RectTransform)t; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Fixed-size object, centred horizontally at offset x, measured from the panel bottom.</summary>
        static void BottomAt(Transform t, Transform parent, float x, float y, float w, float h, float font)
        {
            if (t == null) return;
            Reparent(t, parent);
            var rt = (RectTransform)t; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
            var tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) { Kit.SetText(tmp, null, font, TextAlignmentOptions.Center); if (tmp.transform != t) Kit.Stretch(tmp.transform, 0, 0, 0, 0); }
        }

        static void Reparent(Transform t, Transform parent) { if (t.parent != parent) t.SetParent(parent, false); }

        // =====================================================================================
        //  Small helpers
        // =====================================================================================
        static void SetText(Transform t, string value)
        {
            if (t == null) return;
            var tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = value;
        }

        static void Role(Transform t, ThemeRole r) { if (t != null && t.GetComponent<Graphic>() != null) SceneBuilderCore.Themed(t.gameObject, r); }

        static void RoleLabel(Transform t, ThemeRole r)
        {
            if (t == null) return;
            var l = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (l != null) SceneBuilderCore.Themed(l.gameObject, r);
        }

        /// <summary>Icon-only round social button (no text): a circle with the brand logo centred.</summary>
        static Button Social(Transform parent, string name, Sprite icon)
        {
            var b = SceneBuilderCore.MakeButton(parent, name, "", 30, new Vector2(0.5f, 1), Vector2.zero, new Vector2(SocialSize, SocialSize), Color.white);
            var label = b.transform.Find("Label");
            if (label != null) Object.DestroyImmediate(label.gameObject);
            var img = SceneBuilderCore.Ensure<Image>(b.gameObject); Kit.StyleRounded(img); Kit.SetAlpha(img, 1f); b.targetGraphic = img;
            Kit.Icon(b.transform, name.Replace("SignInButton", "Icon"), icon, 64);
            return b;
        }

        /// <summary>Makes a text object tappable (Forgot Password, Sign Up).</summary>
        /// <summary>The box is saved EMPTY: the grey hint is the only thing in it until the user types (it used to hold "Enter your Email:" as real text).</summary>
        static void ClearTypedText(TMP_InputField field)
        {
            SceneBuilderCore.SetField(field, "m_Text", "");
            if (field.textComponent != null) { field.textComponent.text = ""; EditorUtility.SetDirty(field.textComponent); }
            if (field.placeholder != null) { field.placeholder.gameObject.SetActive(true); field.placeholder.enabled = true; }
        }

        static Button MakeClickable(Transform t)
        {
            if (t == null) return null;
            var b = SceneBuilderCore.Ensure<Button>(t.gameObject);
            var g = t.GetComponent<Graphic>();
            if (g != null) { b.targetGraphic = g; g.raycastTarget = true; }
            b.transition = Selectable.Transition.None;       // the press feedback (scale punch) is its tap response
            return b;
        }
    }
}
