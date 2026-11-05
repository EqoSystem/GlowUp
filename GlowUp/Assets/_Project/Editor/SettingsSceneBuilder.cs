using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Performance;
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
    /// Menu: GlowUp > Build Scene > 14 Settings.
    /// One scrolling page. Seven groups are long rounded tiles that open when tapped (Appearance and
    /// Performance, Sounds and Motion, Camera Helpers, Account, Skin Profile, Ingredient Safety Alerts,
    /// Notifications); About and Support and Account Actions are always shown. Plus four pop-ups
    /// (confirm, delete account, privacy policy and terms, contact support).
    ///
    /// A tile's settings are shut in the Scene view. To see and style them, select the group (for example
    /// AppearanceSection) and tick "Start Open" on its SettingsSectionTile; untick it when done.
    /// The icon animation of each tile is on its IconDisc > SettingsIconAnimator.
    ///
    /// Where things are in the Hierarchy:
    ///  - Everything you SEE is under Canvas > SettingsCard (and the pop-ups directly under Canvas).
    ///    These objects are rebuilt each time this menu runs.
    ///  - What each group DOES is one script per group, on the objects under "SettingsFeatures". Those
    ///    objects are kept when the scene is rebuilt, so wording and options typed on them stay.
    ///  - Each switch has a ToggleSwitch and each button a ButtonPressFeedback: their animation is set there.
    /// </summary>
    public static class SettingsSceneBuilder
    {
        const float RowTitle = 38, RowCaption = 29, ButtonFont = 34, ButtonHeight = 92, SwitchWidth = 116, SwitchHeight = 64;
        const string LegalPath = "Assets/_Project/Legal/PrivacyPolicyAndTerms.txt";
        const string FeaturesRootName = "SettingsFeatures";

        [MenuItem("GlowUp/Build Scene/14 Settings", false, 124)]
        public static void Build()
        {
            A.AppScreen s = A.Begin("SettingsScene", "Settings", true, "SettingsCard");
            Transform card = s.card;
            PerformanceProfile performanceProfile = EnsurePerformanceProfile();
            GameObject features = SceneBuilderCore.EnsureRoot(FeaturesRootName);

            // ---- the scrolling list ----
            GameObject listArea = A.Node(card, "SettingsArea");
            A.Between(listArea.transform, A.HeaderBottom, 30, 26, 14);
            ScrollRect scroll = A.Scroll(listArea.transform, "SettingsScrollView", out RectTransform content, 30, 14, 34, 6, 60);

            // =====================================================================================
            //  Pop-ups (inactive in the Hierarchy; tick one active to see and style it)
            // =====================================================================================
            // ---- "are you sure?" ----
            A.AppPopup confirmPopup = A.Overlay(s, "ConfirmPanel", "Are you sure?");
            A.At(confirmPopup.box, A.MC, new Vector2(0, 60), new Vector2(900, 640));
            TextMeshProUGUI confirmMessage = A.Label(confirmPopup.box, "MessageText", "What will happen is explained here before anything is changed.",
                                                     32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(confirmMessage.transform, 48, 190, 48, 130);
            A.ShrinkWrapped(confirmMessage, 26);
            Button confirmCancel = A.PillButton(confirmPopup.box, "CancelButton", "Cancel", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(confirmCancel.transform, 44, 108, 48, 462);
            Button confirmOk = A.PillButton(confirmPopup.box, "ConfirmButton", "Confirm", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(confirmOk.transform, 44, 108, 462, 48);
            // ConfirmDialog paints this button itself (pink, or the warning colour for a dangerous action)
            Object.DestroyImmediate(confirmOk.GetComponent<ThemedGraphic>());
            Object.DestroyImmediate(A.LabelOf(confirmOk).GetComponent<ThemedGraphic>());

            var confirm = Feature<ConfirmDialog>(features, "ConfirmDialog");
            A.Set(confirm, "panel", confirmPopup.panel); A.Set(confirm, "titleText", confirmPopup.title); A.Set(confirm, "messageText", confirmMessage);
            A.Set(confirm, "confirmButton", confirmOk); A.Set(confirm, "confirmLabel", A.LabelOf(confirmOk));
            A.Set(confirm, "cancelButton", confirmCancel); A.Set(confirm, "cancelLabel", A.LabelOf(confirmCancel));

            // ---- delete account ----
            A.AppPopup deletePopup = A.Overlay(s, "DeleteAccountPanel", "Delete account");
            A.At(deletePopup.box, A.MC, new Vector2(0, 80), new Vector2(900, 1010));
            TextMeshProUGUI deleteWarning = A.Label(deletePopup.box, "WarningText",
                "This permanently deletes your GlowUp account, your skin profile and your scan history. It cannot be undone.",
                32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(deleteWarning.transform, 124, 190, 48, 48);
            TextMeshProUGUI passwordLabel = A.Label(deletePopup.box, "PasswordLabel", "Type your password to confirm", 34, ThemeRole.MainText, FontRole.Label);
            A.TopStrip(passwordLabel.transform, 330, 54, 48, 48);
            TMP_InputField password = SceneBuilderCore.MakeInput(deletePopup.box, "DeletePasswordField", "Your password", A.TC, Vector2.zero, new Vector2(800, Kit.FieldHeight));
            A.TopStrip(password.transform, 394, Kit.FieldHeight, 48, 48);
            Kit.StyleInput(password, "Your password", ThemeRole.InputBackground);
            password.contentType = TMP_InputField.ContentType.Password;
            Kit.AddEye(password);
            TextMeshProUGUI deleteMessage = A.Label(deletePopup.box, "DeleteMessageText", "", 29, ThemeRole.Warning, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(deleteMessage.transform, 546, 150, 48, 48);
            Button deleteConfirm = A.PillButton(deletePopup.box, "ConfirmDeleteButton", "Delete My Account", 36, ThemeRole.Warning, ThemeRole.Background, IconFactory.Trash);
            A.BottomStrip(deleteConfirm.transform, 168, 108, 48, 48);
            Button deleteCancel = A.PillButton(deletePopup.box, "CancelDeleteButton", "Keep My Account", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(deleteCancel.transform, 44, 108, 48, 48);

            // ---- privacy policy and terms ----
            A.AppPopup legalPopup = A.Overlay(s, "LegalPanel", "Privacy Policy & Terms");
            A.Fill(legalPopup.box, 40, 40, 40, 150);
            GameObject legalArea = A.Node(legalPopup.box, "TextArea");
            A.Fill(legalArea.transform, 36, 44, 30, 130);
            ScrollRect legalScroll = A.Scroll(legalArea.transform, "LegalScrollView", out RectTransform legalContent, 0, 8, 30, 8, 40);
            TextMeshProUGUI legalText = A.Label(legalContent, "LegalText", "The Privacy Policy and the Terms and Conditions show here.", 30,
                                                ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            var legalSource = SceneBuilderCore.Ensure<LegalTextSource>(legalText.gameObject);
            var legalDocument = AssetDatabase.LoadAssetAtPath<TextAsset>(LegalPath);
            if (legalDocument != null) { A.Set(legalSource, "document", legalDocument); legalSource.Apply(); }
            else Debug.LogWarning("[GlowUp] Settings: " + LegalPath + " was not found, so the legal pop-up has no text. Assign a document on LegalText > Legal Text Source.");

            // ---- contact support ----
            A.AppPopup supportPopup = A.Overlay(s, "SupportPanel", "Contact support");
            A.At(supportPopup.box, A.MC, new Vector2(0, 40), new Vector2(940, 1230));
            TextMeshProUGUI supportIntro = A.Label(supportPopup.box, "IntroText", "Tell us what went wrong. Your message opens in your email app, ready to send.",
                                                   30, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(supportIntro.transform, 118, 88, 48, 48);
            ChoiceTabs categoryTabs = Tabs(supportPopup.box, "CategoryTabs", new[] { "Bug", "Wrong scan result", "Other" }, 0, out _, 28);
            A.TopStrip(categoryTabs.transform.parent, 222, 96, 48, 48);
            TMP_InputField feedbackInput = SceneBuilderCore.MakeInput(supportPopup.box, "FeedbackInputField", "Describe what happened...", A.TC, Vector2.zero, new Vector2(840, 420));
            A.TopStrip(feedbackInput.transform, 340, 420, 48, 48);
            Kit.StyleInput(feedbackInput, "Describe what happened...", ThemeRole.InputBackground);
            SmartScanSceneBuilder.MakeMultiLine(feedbackInput, 32);
            var feedbackBehaviour = SceneBuilderCore.Ensure<InputFieldBehaviour>(feedbackInput.gameObject);
            A.Set(feedbackBehaviour, "characterLimit", 1000);
            GameObject includeRow = A.Node(supportPopup.box, "IncludeLastScanRow");
            A.TopStrip(includeRow.transform, 784, 84, 56, 56);
            A.HStack(includeRow, 20);
            TextMeshProUGUI includeLabel = A.Label(includeRow.transform, "Label", "Include my last scan (product and ingredients)", 30, ThemeRole.MainText, FontRole.Label,
                                                   TextAlignmentOptions.MidlineLeft, true);
            A.Size(includeLabel, -1, -1, 1);
            Toggle includeScan = Switch(includeRow.transform, "IncludeLastScanSwitch", true);
            TextMeshProUGUI supportMessage = A.Label(supportPopup.box, "FeedbackText", "", 28, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(supportMessage.transform, 886, 140, 48, 48);
            Button supportSend = A.PillButton(supportPopup.box, "SendButton", "Send Report", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Mail);
            A.BottomStrip(supportSend.transform, 44, 112, 48, 48);

            var support = Feature<SupportFeedbackForm>(features, "SupportFeedbackForm");
            A.Set(support, "panel", supportPopup.panel); A.Set(support, "categoryTabs", categoryTabs); A.Set(support, "messageInput", feedbackInput);
            A.Set(support, "includeLastScanToggle", includeScan); A.Set(support, "sendButton", supportSend); A.Set(support, "feedbackText", supportMessage);

            // =====================================================================================
            //  1. Appearance and performance
            // =====================================================================================
            Transform appearance = TileSection(content, scroll, "AppearanceSection", "Appearance & Performance", "Dark mode, graphics, frame rate", IconFactory.Gauge, SettingsIconAnimator.Style.PopIn, 0);

            GameObject darkRow = Row(appearance, "DarkModeRow");
            TextMeshProUGUI darkCaption = RowTexts(darkRow.transform, "Dark mode", "Light mode is on. Tap the moon for dark mode.");
            SceneBuilderCore.Ensure<ThemeModeLabel>(darkCaption.gameObject);
            Button dark = A.IconButton(darkRow.transform, "DarkModeButton", IconFactory.Moon, 92, 56, ThemeRole.MainText, ThemeRole.SurfaceAlt);
            A.Size(dark, 92, 92);
            A.Outline(dark.transform, ThemeRole.Gold, 92f);
            var darkToggle = SceneBuilderCore.Ensure<DarkModeToggleButton>(dark.gameObject);
            A.Set(darkToggle, "iconImage", dark.transform.Find("Icon").GetComponent<Image>());
            A.Set(darkToggle, "moonIcon", IconFactory.Moon); A.Set(darkToggle, "sunIcon", IconFactory.Sun);

            Divider(appearance);
            GameObject qualityBlock = Block(appearance, "GraphicsQualityBlock");
            A.Label(qualityBlock.transform, "Title", "Graphics quality", RowTitle, ThemeRole.MainText, FontRole.Label);
            ChoiceTabs qualityTabs = Tabs(qualityBlock.transform, "GraphicsQualityTabs", new[] { "Performance Mode", "High Quality" }, 1, out _, 32);
            TextMeshProUGUI qualityStatus = A.Label(qualityBlock.transform, "StatusText",
                "This phone: 4 GB memory, 8-core processor. High Quality was chosen for it automatically.\nFull graphics and animations.",
                RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Button detect = A.LinkButton(qualityBlock.transform, "DetectAutomaticallyButton", "Let the app choose for this phone", 29, ThemeRole.AccentText);
            A.Size(detect, -1, 56);

            Divider(appearance);
            GameObject frameBlock = Block(appearance, "FrameRateBlock");
            A.Label(frameBlock.transform, "Title", "Frame rate cap", RowTitle, ThemeRole.MainText, FontRole.Label);
            ChoiceTabs frameTabs = Tabs(frameBlock.transform, "FrameRateTabs", new[] { "30 FPS", "60 FPS" }, 1, out TextMeshProUGUI[] frameLabels, 32);
            TextMeshProUGUI frameStatus = A.Label(frameBlock.transform, "StatusText", "60 frames per second: smoother animations, uses more battery.",
                                                  RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);

            // AR Try-On quality: which of the two AR pipelines runs (Automatic decides from the phone and a frame-rate check)
            Divider(appearance);
            GameObject arBlock = Block(appearance, "ArQualityBlock");
            A.Label(arBlock.transform, "Title", "AR Try-On quality", RowTitle, ThemeRole.MainText, FontRole.Label);
            ChoiceTabs arTabs = Tabs(arBlock.transform, "ArQualityTabs", new[] { "Automatic", "High", "Lighter" }, 0, out _, 32);
            TextMeshProUGUI arStatus = A.Label(arBlock.transform, "StatusText",
                "This phone: 4 GB memory, 8-core processor. AR Try-On uses the full pipeline (chosen for it automatically).\nDense face tracking, soft blending, light that follows the room, and make-up that stays off your teeth.",
                RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            var arQuality = Feature<ArQualitySetting>(features, "ArQualitySetting");
            A.Set(arQuality, "tabs", arTabs); A.Set(arQuality, "statusText", arStatus);

            var quality = Feature<GraphicsQualitySetting>(features, "GraphicsQualitySetting");
            A.Set(quality, "modeTabs", qualityTabs); A.Set(quality, "statusText", qualityStatus); A.Set(quality, "detectButton", detect);
            A.Set(quality, "profile", performanceProfile);
            var frameRate = Feature<FrameRateCapSetting>(features, "FrameRateCapSetting");
            A.Set(frameRate, "frameRateTabs", frameTabs); A.Set(frameRate, "lowLabel", frameLabels[0]); A.Set(frameRate, "highLabel", frameLabels[1]);
            A.Set(frameRate, "statusText", frameStatus);

            // =====================================================================================
            //  Sounds and motion: tap sounds, shake to change screen, the light round the screen edge
            // =====================================================================================
            Transform sounds = TileSection(content, scroll, "SoundsMotionSection", "Sounds & Motion", "Tap and delete sounds, edge light", IconFactory.Speaker, SettingsIconAnimator.Style.Pulse, 1);
            Toggle tapSounds = SwitchRow(sounds, "TapSoundsRow", "Tap sounds", "A short sound when you tap a button.", true);
            GameObject soundBlock = Block(sounds, "TapSoundBlock", 14);
            soundBlock.AddComponent<CanvasGroup>();
            A.Label(soundBlock.transform, "Title", "Sound", RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft);
            ChoiceTabs soundTabs = Tabs(soundBlock.transform, "TapSoundTabs", new[] { "Soft tap", "Pop", "Click", "Bubble", "Chime" }, 0, out TextMeshProUGUI[] soundLabels, 28);
            GameObject volumeRow = Row(soundBlock.transform, "VolumeRow", 80);
            TextMeshProUGUI volumeTitle = A.Label(volumeRow.transform, "Title", "Volume", RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Left);
            A.Size(volumeTitle, 150, -1);
            Slider volume = ArTryOnSceneBuilder.BuildSlider(volumeRow.transform, "VolumeSlider");
            A.Size(volume, -1, 64, 1);
            volume.value = 0.7f;
            A.Set(SceneBuilderCore.Ensure<UiPreferenceToggle>(tapSounds.gameObject), "preference", (int)UiPreferenceToggle.Preference.TapSounds);
            A.Set(tapSounds.GetComponent<UiPreferenceToggle>(), "toggle", tapSounds);
            A.Set(tapSounds.GetComponent<UiPreferenceToggle>(), "onlyWhenOn", soundBlock.GetComponent<CanvasGroup>());
            A.Set(tapSounds.GetComponent<UiPreferenceToggle>(), "playSoundWhenTurnedOn", true);
            var soundPicker = SceneBuilderCore.Ensure<TapSoundPicker>(soundBlock);
            A.Set(soundPicker, "tabs", soundTabs); Kit.SetArray(soundPicker, "tabLabels", soundLabels); A.Set(soundPicker, "volumeSlider", volume);
            Divider(sounds);
            // the freezing sound when a scan is removed in Recent Scans (DeleteSoundPlayer in that scene); on until switched off
            PreferenceRow(sounds, "DeleteSoundRow", "Delete sound", "A freezing sound when you remove a scan in Recent Scans.", true, UiPreferenceToggle.Preference.DeleteSound);
            Divider(sounds);
            PreferenceRow(sounds, "EdgeLightRow", "Edge light", "A light that runs round the edge of every screen.", true, UiPreferenceToggle.Preference.EdgeLight);

            // =====================================================================================
            //  Camera helpers: bright screen, flashlight, screen light
            // =====================================================================================
            Transform cameraHelpers = TileSection(content, scroll, "CameraSection", "Camera Helpers", "Bright screen, flashlight, face light", IconFactory.CameraIcon, SettingsIconAnimator.Style.Spin, 2);
            PreferenceRow(cameraHelpers, "ScanBrightRow", "Bright screen while scanning", "Full screen brightness in AI Smart Scan.", true, UiPreferenceToggle.Preference.ScanBrightScreen);
            Divider(cameraHelpers);
            PreferenceRow(cameraHelpers, "FlashlightRow", "Flashlight in the dark", "Offer the flashlight when the scanner finds it too dark.", true, UiPreferenceToggle.Preference.AutoFlashlight);
            Divider(cameraHelpers);
            PreferenceRow(cameraHelpers, "TryOnBrightRow", "Bright screen in AR Try-On", "Full screen brightness while you try a product on.", true, UiPreferenceToggle.Preference.TryOnBrightScreen);
            Divider(cameraHelpers);
            PreferenceRow(cameraHelpers, "ScreenLightRow", "Face light in the dark", "A bright white frame on the screen lights your face.", true, UiPreferenceToggle.Preference.TryOnScreenLight);

            // =====================================================================================
            //  2. Account
            // =====================================================================================
            Transform account = TileSection(content, scroll, "AccountSection", "Account", "Your details, sync, storage", IconFactory.User, SettingsIconAnimator.Style.Assemble, 3);

            GameObject infoBlock = Block(account, "AccountInfoBlock", 4);
            TextMeshProUGUI accountName = A.Label(infoBlock.transform, "NameText", "Your name", 42, ThemeRole.MainText, FontRole.Heading);
            A.Shrink(accountName, 30);
            TextMeshProUGUI accountEmail = A.Label(infoBlock.transform, "EmailText", "name@email.com", 32, ThemeRole.MainText, FontRole.Body);
            A.Shrink(accountEmail, 24);
            TextMeshProUGUI accountId = A.Label(infoBlock.transform, "UserIdText", "Account ID: a1B2c3D4e5F6g7H8i9J0k1L2m3N4", 26, ThemeRole.SecondaryText, FontRole.Body);
            A.Shrink(accountId, 18);
            TextMeshProUGUI accountState = A.Label(infoBlock.transform, "SignInStateText", "Signed in. Your scans and profile are saved to your account.",
                                                   RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            var accountInfo = Feature<AccountInfoPanel>(features, "AccountInfoPanel");
            A.Set(accountInfo, "nameText", accountName); A.Set(accountInfo, "emailText", accountEmail);
            A.Set(accountInfo, "userIdText", accountId); A.Set(accountInfo, "signInStateText", accountState);

            Divider(account);
            GameObject syncBlock = Block(account, "SyncStatusBlock");
            GameObject syncRow = A.Node(syncBlock.transform, "SyncRow");
            A.HStack(syncRow, 20);
            GameObject syncTexts = A.Node(syncRow.transform, "Texts");
            A.VStack(syncTexts, 4); A.Size(syncTexts.transform, -1, -1, 1);
            GameObject statusLine = A.Node(syncTexts.transform, "StatusLine");
            A.HStack(statusLine, 14);
            Image statusDot = A.RawPanel(statusLine.transform, "StatusDot", new Color32(79, 114, 64, 255), 24f);
            A.Size(statusDot, 24, 24);
            TextMeshProUGUI statusText = A.RawLabel(statusLine.transform, "StatusText", "Online", RowTitle, new Color32(79, 114, 64, 255), FontRole.Label);
            TextMeshProUGUI pendingText = A.Label(syncTexts.transform, "PendingText", "Everything is saved to your account.", RowCaption, ThemeRole.SecondaryText, FontRole.Body,
                                                  TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI lastSyncText = A.Label(syncTexts.transform, "LastSyncText", "Last sent: 9 Oct 2026, 21:41", 26, ThemeRole.SecondaryText, FontRole.Body,
                                                   TextAlignmentOptions.TopLeft, true);
            Button syncNow = A.PillButton(syncRow.transform, "SyncNowButton", "Sync Now", ButtonFont, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sync);
            A.Size(syncNow, 270, ButtonHeight);
            TextMeshProUGUI syncMessage = Message(syncBlock.transform, "SyncMessageText");
            var sync = Feature<SyncStatusPanel>(features, "SyncStatusPanel");
            A.Set(sync, "statusDot", statusDot); A.Set(sync, "statusText", statusText); A.Set(sync, "pendingText", pendingText); A.Set(sync, "lastSyncText", lastSyncText);
            A.Set(sync, "syncButton", syncNow); A.Set(sync, "syncButtonLabel", A.LabelOf(syncNow)); A.Set(sync, "messageText", syncMessage);

            Divider(account);
            GameObject cacheBlock = Block(account, "LocalCacheBlock");
            GameObject cacheRow = Row(cacheBlock.transform, "CacheRow");
            TextMeshProUGUI cacheSize = RowTexts(cacheRow.transform, "Storage on this phone", "Saved copies on this phone: 1.4 MB");
            Button clearCache = A.PillButton(cacheRow.transform, "ClearCacheButton", "Clear Cache", ButtonFont, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Trash);
            A.Size(clearCache, 290, ButtonHeight);
            TextMeshProUGUI cacheMessage = Message(cacheBlock.transform, "CacheMessageText");
            var cache = Feature<LocalCacheCleaner>(features, "LocalCacheCleaner");
            A.Set(cache, "sizeText", cacheSize); A.Set(cache, "clearButton", clearCache); A.Set(cache, "messageText", cacheMessage); A.Set(cache, "confirm", confirm);

            // =====================================================================================
            //  3. Skin profile
            // =====================================================================================
            Transform skin = TileSection(content, scroll, "SkinProfileSection", "Skin Profile", "Skin type, tone, allergens", IconFactory.Sparkle, SettingsIconAnimator.Style.Sparkle, 4);
            GameObject skinBlock = Block(skin, "SkinProfileBlock", 16);
            TextMeshProUGUI skinCurrent = A.Label(skinBlock.transform, "CurrentProfileText", "Skin type: Mix\nSkin tone: Medium\nSkin conditions: 1   |   Allergens: 2",
                                                  32, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            TextMeshProUGUI skinHint = A.Label(skinBlock.transform, "HintText", "Skin changed, or picked the wrong type? Take the skin quiz again. Your answers are replaced when you finish it.",
                                               RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            Button retake = A.PillButton(skinBlock.transform, "RetakeQuizButton", "Reset & Retake Skin Quiz", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sync);
            A.Size(retake, -1, 104);
            var skinReset = Feature<SkinProfileResetAction>(features, "SkinProfileResetAction");
            A.Set(skinReset, "currentProfileText", skinCurrent); A.Set(skinReset, "retakeButton", retake); A.Set(skinReset, "confirm", confirm);

            // =====================================================================================
            //  4. Ingredient safety alerts
            // =====================================================================================
            Transform alerts = TileSection(content, scroll, "SafetyAlertsSection", "Ingredient Safety Alerts", "Warnings while you scan", IconFactory.ShieldPlain, SettingsIconAnimator.Style.ShieldThenCheck, 5);
            Toggle alertMaster = SwitchRow(alerts, "InstantAlertsRow", "Instant safety alerts", "Warn me as soon as a scan contains one of the things below.", true);

            GameObject triggerGroup = A.Node(alerts, "TriggerGroup", typeof(CanvasGroup));
            A.VStack(triggerGroup, 0);
            Divider(triggerGroup.transform);
            Toggle alertVibrate = SwitchRow(triggerGroup.transform, "VibrateRow", "Vibrate with the alert", null, true);
            Divider(triggerGroup.transform);
            TextMeshProUGUI warnHeader = A.Label(triggerGroup.transform, "WarnMeAboutText", "Warn me about", 30, ThemeRole.SecondaryText, FontRole.Heading);
            A.Size(warnHeader, -1, 70);

            string[] triggerKeys = { "allergens", "harmful", "acne", "eczema", "psoriasis", "rosacea", "seborrheic", "melasma", "contact" };
            string[] triggerTitles =
            {
                "My allergens", "Banned or harmful ingredients", "Acne triggers", "Eczema flare-up triggers", "Psoriasis flare-up triggers",
                "Rosacea triggers", "Seborrheic dermatitis triggers", "Melasma and dark-spot triggers", "Contact dermatitis triggers"
            };
            var triggerToggles = new Toggle[triggerKeys.Length];
            for (int i = 0; i < triggerKeys.Length; i++)
                triggerToggles[i] = SwitchRow(triggerGroup.transform, "Trigger_" + triggerKeys[i], triggerTitles[i], null, i < 2, 84);

            var alertSettings = Feature<SafetyAlertSettings>(features, "SafetyAlertSettings");
            A.Set(alertSettings, "masterToggle", alertMaster); A.Set(alertSettings, "vibrateToggle", alertVibrate);
            Kit.SetStrings(alertSettings, "triggerKeys", triggerKeys); Kit.SetArray(alertSettings, "triggerToggles", triggerToggles);
            A.Set(alertSettings, "triggerGroup", triggerGroup.GetComponent<CanvasGroup>());

            // =====================================================================================
            //  5. Notifications
            // =====================================================================================
            Transform notifications = TileSection(content, scroll, "NotificationsSection", "Notifications", "Daily reminder, rule updates", IconFactory.Bell, SettingsIconAnimator.Style.BellSwing, 6);
            Toggle dailyReminder = SwitchRow(notifications, "DailyReminderRow", "Daily skin log reminder", "A reminder on your phone once a day.", false);
            GameObject timeRow = Row(notifications, "ReminderTimeRow");
            RowTexts(timeRow.transform, "Reminder time", null);
            TMP_Dropdown hourDropdown = Kit.MakeDropdown(timeRow.transform, "ReminderHourDropdown", Vector2.zero, new Vector2(330, 92), 34);
            A.Size(hourDropdown, 330, 92);
            hourDropdown.ClearOptions();
            hourDropdown.AddOptions(new List<string> { "8:00 PM" });
            hourDropdown.RefreshShownValue();
            Divider(notifications);
            Toggle ruleUpdates = SwitchRow(notifications, "RuleUpdatesRow", "Ingredient rule updates", "Tell me when the ingredient safety rules change.", true);
            TextMeshProUGUI notificationMessage = Message(notifications, "NotificationMessageText");
            var notificationPanel = Feature<NotificationSettingsPanel>(features, "NotificationSettingsPanel");
            A.Set(notificationPanel, "dailyReminderToggle", dailyReminder); A.Set(notificationPanel, "reminderHourDropdown", hourDropdown);
            A.Set(notificationPanel, "ruleUpdatesToggle", ruleUpdates); A.Set(notificationPanel, "messageText", notificationMessage);

            // =====================================================================================
            //  6. About and support
            // =====================================================================================
            Transform about = Section(content, "AboutSection", "About & Support", IconFactory.Info);
            TextMeshProUGUI version = A.Label(about, "VersionText", "GlowUp  version 0.1.0", RowTitle, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            SceneBuilderCore.Ensure<AppVersionLabel>(version.gameObject).Apply();
            Divider(about);
            GameObject legalRow = Row(about, "LegalRow");
            RowTexts(legalRow.transform, "Privacy Policy & Terms", "How your skin profile and scans are kept and used.");
            Button openLegal = A.PillButton(legalRow.transform, "OpenLegalButton", "Read", ButtonFont, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Document);
            A.Size(openLegal, 210, ButtonHeight);
            var openLegalAction = SceneBuilderCore.Ensure<OpenPanelButton>(openLegal.gameObject);
            A.Set(openLegalAction, "panel", legalPopup.panel); A.Set(openLegalAction, "scrollToTop", legalScroll);
            Divider(about);
            GameObject supportRow = Row(about, "SupportRow");
            RowTexts(supportRow.transform, "Contact support", "Report a bug or a scan result that looks wrong.");
            Button openSupport = A.PillButton(supportRow.transform, "OpenSupportButton", "Write", ButtonFont, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.Mail);
            A.Size(openSupport, 210, ButtonHeight);
            A.Set(SceneBuilderCore.Ensure<OpenPanelButton>(openSupport.gameObject), "panel", supportPopup.panel);

            // =====================================================================================
            //  7. Account actions
            // =====================================================================================
            Transform actions = Section(content, "AccountActionsSection", "Account Actions", IconFactory.SignOut);
            GameObject actionBlock = Block(actions, "AccountActionsBlock", 20);
            Button logOut = A.PillButton(actionBlock.transform, "LogOutButton", "Log Out", 36, ThemeRole.SurfaceAlt, ThemeRole.MainText, IconFactory.SignOut);
            A.Size(logOut, -1, 104);
            Button deleteAccount = A.PillButton(actionBlock.transform, "DeleteAccountButton", "Delete Account", 36, ThemeRole.InputBackground, ThemeRole.Warning, IconFactory.Trash);
            A.Size(deleteAccount, -1, 104);
            A.Outline(deleteAccount.transform, ThemeRole.Warning, 104f);
            TextMeshProUGUI deleteHint = A.Label(actionBlock.transform, "DeleteHintText", "Deleting removes your account, skin profile and scan history for good.",
                                                 26, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Top, true);

            var accountActions = Feature<AccountActions>(features, "AccountActions");
            A.Set(accountActions, "logOutButton", logOut); A.Set(accountActions, "confirm", confirm);
            A.Set(accountActions, "deleteAccountButton", deleteAccount); A.Set(accountActions, "deletePanel", deletePopup.panel);
            A.Set(accountActions, "passwordInput", password); A.Set(accountActions, "confirmDeleteButton", deleteConfirm);
            A.Set(accountActions, "confirmDeleteLabel", A.LabelOf(deleteConfirm)); A.Set(accountActions, "cancelDeleteButton", deleteCancel);
            A.Set(accountActions, "deleteMessageText", deleteMessage);

            // ---- the page itself ----
            var controller = A.Controller<SettingsFlowController>("SettingsFlowController");
            A.Set(controller, "backButton", s.back); A.Set(controller, "list", scroll);
            Kit.SetArray(controller, "popups", new Object[] { confirmPopup.panel, deletePopup.panel, legalPopup.panel, supportPopup.panel });

            // What is FETCHED on this page: the account's name, e-mail, ID and sign-in state, and the line that sums up the
            // skin profile (bars only while nothing is on the phone yet). Every title, label, switch and button is never a bar.
            var fetched = new System.Collections.Generic.List<Object>();
            fetched.AddRange(AppTiles.TextsOf(Object.FindObjectOfType<AccountInfoPanel>(true), "nameText", "emailText", "userIdText", "signInStateText"));
            fetched.AddRange(AppTiles.TextsOf(Object.FindObjectOfType<SkinProfileResetAction>(true), "currentProfileText"));
            AppTiles.WordSkeleton(s, fetched.ToArray());

            A.Finish(s);
            Debug.Log("[GlowUp] SettingsScene built. The pop-ups (ConfirmPanel, DeleteAccountPanel, LegalPanel, SupportPanel) are inactive until used. " +
                      "Set the support address on SettingsFeatures > SupportFeedbackForm > Support Email.");
        }

        // =========================================================================================
        //  Building blocks of the page
        // =========================================================================================
        /// <summary>The script of one group of settings. It lives under SettingsFeatures and is kept across rebuilds.</summary>
        static T Feature<T>(GameObject root, string name) where T : Component
        {
            GameObject go = SceneBuilderCore.FindInScene(name);
            if (go == null) go = new GameObject(name);
            if (go.transform.parent != root.transform) go.transform.SetParent(root.transform, false);
            return SceneBuilderCore.Ensure<T>(go);
        }

        /// <summary>A group: a small gold icon and a heading, then a white rounded panel that holds the rows. Returns the panel.</summary>
        static Transform Section(Transform content, string name, string title, Sprite icon)
        {
            GameObject section = A.Node(content, name);
            A.VStack(section, 14);

            GameObject header = A.Node(section.transform, "Header");
            A.HStack(header, 16, 14);
            A.Size(header.transform, -1, 60);
            Image headerIcon = A.Icon(header.transform, "Icon", icon, 42, ThemeRole.Gold);
            A.Size(headerIcon, 42, 42);
            A.Label(header.transform, "Title", title, 42, ThemeRole.Heading, FontRole.Heading);

            GameObject panel = A.Node(section.transform, "Panel");
            A.VStack(panel, 0, 34, 34, 22, 28);
            A.Backdrop(panel.transform, ThemeRole.InputBackground, 1f, 52f);
            A.Outline(panel.transform, ThemeRole.SurfaceAlt, 52f);
            return panel.transform;
        }

        /// <summary>
        /// A group that opens and shuts: a long rounded tile (round icon, title, one short line, arrow) and, under
        /// it, the white panel with the rows. Returns the panel. `order` staggers the icons' first animation.
        /// </summary>
        static Transform TileSection(Transform content, ScrollRect scroll, string name, string title, string summary, Sprite icon,
            SettingsIconAnimator.Style style, int order)
        {
            const float tileHeight = 136, disc = 88, iconSize = 48;
            GameObject section = A.Node(content, name, typeof(SettingsSectionTile));
            A.VStack(section, 0);

            // ---- the tile ----
            GameObject tile = A.Node(section.transform, "Tile", typeof(Image), typeof(Button));
            A.Size(tile.transform, -1, tileHeight);
            Image hit = tile.GetComponent<Image>();
            Kit.StyleRounded(hit); Kit.SetRadius(hit, tileHeight); hit.color = Color.white;
            SceneBuilderCore.Themed(tile, ThemeRole.InputBackground);
            var button = tile.GetComponent<Button>(); button.transition = Selectable.Transition.None; button.targetGraphic = hit;
            A.Outline(tile.transform, ThemeRole.SurfaceAlt, tileHeight);

            // the glow of the open tile: the accent colour, a little larger than the tile, invisible while the group is shut
            GameObject openGlow = A.Node(tile.transform, "OpenGlow", typeof(Image), typeof(CanvasGroup));
            var openGlowImage = openGlow.GetComponent<Image>();
            openGlowImage.sprite = IconFactory.NeonOutline; openGlowImage.type = Image.Type.Sliced; openGlowImage.raycastTarget = false;
            float glowHalo = IconFactory.NeonOutlineMargin * (tileHeight * 0.5f) / PillCorners.SpriteRadius;
            A.Fill(openGlow.transform, -glowHalo, -glowHalo, -glowHalo, -glowHalo);
            var glowCorners = SceneBuilderCore.Ensure<PillCorners>(openGlow);
            A.Set(glowCorners, "spriteMargin", IconFactory.NeonOutlineMargin); A.Set(glowCorners, "maxRadius", 200f);
            SceneBuilderCore.Ensure<LayoutElement>(openGlow).ignoreLayout = true;
            SceneBuilderCore.Themed(openGlow, ThemeRole.Accent);
            var openGlowGroup = openGlow.GetComponent<CanvasGroup>(); openGlowGroup.alpha = 0f; openGlowGroup.blocksRaycasts = false; openGlowGroup.interactable = false;

            GameObject line = A.Node(tile.transform, "Line");
            A.Fill(line.transform);
            HorizontalLayoutGroup lineLayout = A.HStack(line, 24, 24, 40, 0, 0);

            Image discImage = A.Panel(line.transform, "IconDisc", ThemeRole.SurfaceAlt, 1f, disc);
            A.Size(discImage, disc, disc);
            var animator = discImage.gameObject.AddComponent<SettingsIconAnimator>();
            A.Set(animator, "style", (int)style);
            A.Set(animator, "startDelay", 0.15f + order * 0.09f);
            Transform d = discImage.transform;
            switch (style)
            {
                case SettingsIconAnimator.Style.Assemble:
                {
                    // the same icon twice, each showing one half, so the halves can fly in and join
                    GameObject holder = A.Node(d, "Icon");
                    A.At(holder.transform, A.MC, Vector2.zero, new Vector2(iconSize, iconSize));
                    Image top = A.Icon(holder.transform, "TopHalf", icon, iconSize, ThemeRole.Gold);
                    top.type = Image.Type.Filled; top.fillMethod = Image.FillMethod.Vertical; top.fillOrigin = (int)Image.OriginVertical.Top; top.fillAmount = 0.5f;
                    Image bottom = A.Icon(holder.transform, "BottomHalf", icon, iconSize, ThemeRole.Gold);
                    bottom.type = Image.Type.Filled; bottom.fillMethod = Image.FillMethod.Vertical; bottom.fillOrigin = (int)Image.OriginVertical.Bottom; bottom.fillAmount = 0.5f;
                    A.Set(animator, "icon", (RectTransform)holder.transform);
                    A.Set(animator, "topHalf", top.rectTransform); A.Set(animator, "bottomHalf", bottom.rectTransform);
                    break;
                }
                case SettingsIconAnimator.Style.Sparkle:
                {
                    Image glow = A.Icon(d, "Glow", IconFactory.SoftDisc, disc * 1.5f, ThemeRole.Gold);
                    Color clear = glow.color; clear.a = 0f; glow.color = clear;
                    Image main = A.Icon(d, "Icon", icon, iconSize, ThemeRole.Gold);
                    var stars = new Object[3];
                    Vector2[] at = { new Vector2(30, 28), new Vector2(-32, 18), new Vector2(24, -30) };
                    for (int i = 0; i < 3; i++)
                    {
                        Image star = A.Icon(d, "Star" + (i + 1), IconFactory.Star4, i == 0 ? 26 : 20, ThemeRole.Gold);
                        star.rectTransform.anchoredPosition = at[i];
                        star.rectTransform.localScale = Vector3.zero;                  // only seen while the animation plays
                        stars[i] = star.rectTransform;
                    }
                    A.Set(animator, "icon", main.rectTransform); A.Set(animator, "glow", glow); Kit.SetArray(animator, "sparkles", stars);
                    A.Set(animator, "seconds", 1.1f);
                    break;
                }
                case SettingsIconAnimator.Style.ShieldThenCheck:
                {
                    Image shield = A.Icon(d, "Icon", icon, iconSize, ThemeRole.Gold);
                    Image check = A.Icon(d, "Check", IconFactory.Check, iconSize * 0.46f, ThemeRole.Gold);
                    check.rectTransform.anchoredPosition = new Vector2(0, 1);
                    A.Set(animator, "icon", shield.rectTransform); A.Set(animator, "second", check.rectTransform);
                    A.Set(animator, "seconds", 0.9f);
                    break;
                }
                default:
                    A.Set(animator, "icon", A.Icon(d, "Icon", icon, iconSize, ThemeRole.Gold).rectTransform);
                    break;
            }

            GameObject texts = A.Node(line.transform, "Texts");
            A.VStack(texts, 2, 0, 0, 0, 0, TextAnchor.MiddleLeft);
            A.Size(texts.transform, -1, -1, 1);
            A.Shrink(A.Label(texts.transform, "Title", title, 40, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.Left), 30);
            A.Shrink(A.Label(texts.transform, "Summary", summary, 27, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Left), 22);

            Image arrow = A.Icon(line.transform, "Arrow", IconFactory.ChevronRight, 34, ThemeRole.SecondaryText);
            A.Size(arrow, 34, 34);

            UiEffectsKit.AddPressSweep(tile, tileHeight * 0.5f, true);

            // ---- the settings under the tile (shut until the tile is tapped) ----
            GameObject body = A.Node(section.transform, "Body", typeof(RectMask2D), typeof(CanvasGroup));
            LayoutElement bodySize = A.Size(body.transform, -1, 0);
            GameObject panel = A.Node(body.transform, "Panel");
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0, 0); panelRect.anchorMax = new Vector2(1, 0); panelRect.pivot = new Vector2(0.5f, 0);      // it hangs on the body's bottom edge, so it slides out from under the tile
            panelRect.offsetMin = Vector2.zero; panelRect.offsetMax = Vector2.zero;
            A.VStack(panel, 0, 34, 34, 22, 28);
            var fit = panel.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            A.Backdrop(panel.transform, ThemeRole.InputBackground, 1f, 52f);
            A.Outline(panel.transform, ThemeRole.SurfaceAlt, 52f);

            var view = section.GetComponent<SettingsSectionTile>();
            A.Set(view, "tileButton", button); A.Set(view, "arrow", arrow.rectTransform);
            A.Set(view, "body", bodySize); A.Set(view, "bodyGroup", body.GetComponent<CanvasGroup>());
            A.Set(view, "panel", panelRect); A.Set(view, "iconAnimator", animator); A.Set(view, "scroll", scroll);
            A.Set(view, "tileVisual", (RectTransform)tile.transform); A.Set(view, "openGlow", openGlowGroup); A.Set(view, "line", lineLayout);
            return panel.transform;
        }

        /// <summary>A switch row that belongs to one of the choices kept in UiPreferences.</summary>
        static Toggle PreferenceRow(Transform parent, string name, string title, string caption, bool on, UiPreferenceToggle.Preference preference)
        {
            Toggle toggle = SwitchRow(parent, name, title, caption, on);
            var link = SceneBuilderCore.Ensure<UiPreferenceToggle>(toggle.gameObject);
            A.Set(link, "preference", (int)preference); A.Set(link, "toggle", toggle);
            return toggle;
        }

        /// <summary>A line with something on the left and a control on the right.</summary>
        static GameObject Row(Transform parent, string name, float minHeight = 104)
        {
            GameObject row = A.Node(parent, name);
            A.HStack(row, 22, 0, 0, 6, 6);
            SceneBuilderCore.Ensure<LayoutElement>(row).minHeight = minHeight;
            return row;
        }

        /// <summary>A stack of things one under the other inside a panel (a title, tabs, a caption ...).</summary>
        static GameObject Block(Transform parent, string name, float spacing = 12)
        {
            GameObject block = A.Node(parent, name);
            A.VStack(block, spacing, 0, 0, 12, 12);
            return block;
        }

        /// <summary>The title and the caption on the left of a row. Returns the caption (the title when there is no caption).</summary>
        static TextMeshProUGUI RowTexts(Transform row, string title, string caption)
        {
            GameObject texts = A.Node(row, "Texts");
            A.VStack(texts, 4);
            A.Size(texts.transform, -1, -1, 1);
            TextMeshProUGUI titleText = A.Label(texts.transform, "Title", title, RowTitle, ThemeRole.MainText, FontRole.Label, TextAlignmentOptions.TopLeft, true);
            if (caption == null) return titleText;
            return A.Label(texts.transform, "Caption", caption, RowCaption, ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
        }

        /// <summary>A row with a title, an optional caption and an on/off switch.</summary>
        static Toggle SwitchRow(Transform parent, string name, string title, string caption, bool on, float minHeight = 104)
        {
            GameObject row = Row(parent, name, minHeight);
            RowTexts(row.transform, title, caption);
            return Switch(row.transform, "Switch", on);
        }

        /// <summary>An on/off switch: a round track with a knob that slides. Its animation is on its ToggleSwitch.</summary>
        static Toggle Switch(Transform parent, string name, bool on)
        {
            GameObject go = A.Node(parent, name, typeof(Image), typeof(Toggle), typeof(ToggleSwitch));
            A.Size(go.transform, SwitchWidth, SwitchHeight);
            Image hit = go.GetComponent<Image>();
            Kit.StyleRounded(hit); hit.color = new Color(1, 1, 1, 0.004f);          // the tappable area

            Image track = A.RawPanel(go.transform, "Track", new Color32(196, 180, 181, 255), SwitchHeight);
            A.Fill(track.transform, 0, 4, 0, 4);
            Image knob = A.RawPanel(track.transform, "Knob", new Color32(250, 247, 242, 255), 46f);
            A.At(knob.transform, A.MC, new Vector2(on ? 28 : -28, 0), new Vector2(44, 44));

            Toggle toggle = go.GetComponent<Toggle>();
            toggle.transition = Selectable.Transition.None; toggle.targetGraphic = track; toggle.graphic = null; toggle.isOn = on;
            var view = go.GetComponent<ToggleSwitch>();
            A.Set(view, "track", track); A.Set(view, "knob", knob.rectTransform); A.Set(view, "knobImage", knob); A.Set(view, "knobTravel", 28f);
            return toggle;
        }

        /// <summary>A thin line between two rows.</summary>
        static void Divider(Transform parent)
        {
            Image line = A.Panel(parent, "Divider", ThemeRole.SurfaceAlt, 1f, 2f);
            line.sprite = IconFactory.Square; line.type = Image.Type.Simple;
            A.Size(line, -1, 3);
        }

        /// <summary>The line under a group where its script writes what happened ("Cleared 1.4 MB."). Empty until then.</summary>
        static TextMeshProUGUI Message(Transform parent, string name)
        {
            TextMeshProUGUI text = A.Label(parent, name, "", RowCaption, ThemeRole.AccentText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            return text;
        }

        /// <summary>
        /// Pill tabs where one is chosen, on a soft track so the tabs that are not chosen still show on a white panel.
        /// Returns the ChoiceTabs (its parent is the track object to place or size).
        /// </summary>
        static ChoiceTabs Tabs(Transform parent, string name, string[] captions, int selected, out TextMeshProUGUI[] labels, float fontSize)
        {
            GameObject trackObject = A.Node(parent, name + "Track");
            A.Size(trackObject.transform, -1, 96);
            A.Backdrop(trackObject.transform, ThemeRole.SurfaceAlt, 1f, 96f);

            GameObject tabsObject = A.Node(trackObject.transform, name, typeof(ChoiceTabs));
            A.Fill(tabsObject.transform, 8, 8, 8, 8);
            A.HStack(tabsObject, 8, 0, 0, 0, 0, TextAnchor.MiddleCenter, true);
            var buttons = new Button[captions.Length];
            labels = new TextMeshProUGUI[captions.Length];
            for (int i = 0; i < captions.Length; i++)
            {
                Button tab = A.PillButton(tabsObject.transform, "Tab" + (i + 1), captions[i], fontSize, ThemeRole.InputBackground, ThemeRole.MainText);
                A.Size(tab, 120, 80, 1);
                buttons[i] = tab; labels[i] = A.LabelOf(tab);
                A.Shrink(labels[i], fontSize * 0.7f);
                // ChoiceTabs paints the chosen / not chosen colours itself
                Object.DestroyImmediate(tab.GetComponent<ThemedGraphic>());
                Object.DestroyImmediate(labels[i].GetComponent<ThemedGraphic>());
            }
            var tabs = tabsObject.GetComponent<ChoiceTabs>();
            Kit.SetArray(tabs, "tabs", buttons); Kit.SetArray(tabs, "labels", labels);
            A.Set(tabs, "selectedIndex", Mathf.Clamp(selected, 0, captions.Length - 1));
            return tabs;
        }

        /// <summary>The asset that holds the optimisation settings (created once; edit it in the Inspector).</summary>
        public static PerformanceProfile EnsurePerformanceProfile()
        {
            const string folder = "Assets/_Project/Resources";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            string path = folder + "/" + PerformanceProfile.ResourceName + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<PerformanceProfile>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PerformanceProfile>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            return asset;
        }
    }
}
