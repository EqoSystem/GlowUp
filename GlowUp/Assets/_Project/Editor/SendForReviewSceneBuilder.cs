using GlowUp.Common;
using GlowUp.Review;
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
    /// Menu: GlowUp > Build Scene > 17 Send for Review. The one new screen of the redesign plan (27 November 2026): a product GlowUp
    /// could not find is sent to the admins as confirmed text only. Built from the existing parts (AppKit header with the Back arrow
    /// top left, fields, pill buttons, the confirm dialog, the camera picture of AI Smart Scan); no new animation.
    /// Every object is a scene object; texts and timings are Inspector fields on SendForReviewController.
    /// </summary>
    public static class SendForReviewSceneBuilder
    {
        const float FieldH = Kit.FieldHeight;

        [MenuItem("GlowUp/Build Scene/17 Send for Review", false, 127)]
        public static void Build()
        {
            A.AppScreen s = A.Begin(SceneNames.SendForReview, "Send for Review", true, "SendForReviewCard");
            Transform card = s.card;

            // ---------------- the form ----------------
            GameObject form = A.Node(card, "FormPanel");
            A.Between(form.transform, A.HeaderBottom, 30, 30, 30);
            ScrollRect scroll = A.Scroll(form.transform, "FormScrollView", out RectTransform content, 18, 12, 18, 6, 40);

            // messages that show only when they apply, at the top of the form
            GameObject cameraOff = StatusCard(content, "CameraOffCard", ThemeRole.SurfaceAlt, "Camera is off", "Allow the camera to scan the barcode or take photos.", 220);
            Button openSettings = A.PillButton(cameraOff.transform, "OpenSettingsButton", "Open Settings", 30, ThemeRole.Background, ThemeRole.AccentText);
            A.Share(openSettings.transform, 0f, 0.5f, 12, 18, 330 - 18 - 88); A.Outline(openSettings.transform, ThemeRole.Accent, 88f);
            Button offGallery = A.PillButton(cameraOff.transform, "ChooseFromGalleryButton", "Choose from gallery", 30, ThemeRole.Background, ThemeRole.AccentText);
            A.Share(offGallery.transform, 0.5f, 1f, 12, 18, 330 - 18 - 88); A.Outline(offGallery.transform, ThemeRole.Accent, 88f);
            A.Shrink(A.LabelOf(offGallery), 20);
            A.Size(cameraOff.GetComponent<Image>(), -1, 330);
            cameraOff.SetActive(false);

            GameObject failed = StatusCard(content, "NotSentCard", ThemeRole.Caution, "Not sent", "Something went wrong. Your entries are kept.", 200);
            Button retry = A.PillButton(failed.transform, "RetryButton", "Retry", 32, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Sync);
            A.BottomStrip(retry.transform, 18, 92, 24, 24);
            A.Size(failed.GetComponent<Image>(), -1, 310);
            failed.SetActive(false);

            GameObject sendingCard = StatusCard(content, "SendingCard", ThemeRole.SurfaceAlt, "Sending", "Sending...", 200);
            TextMeshProUGUI sendingText = sendingCard.transform.Find("BodyText").GetComponent<TextMeshProUGUI>();
            Button cancelSend = A.PillButton(sendingCard.transform, "CancelButton", "Cancel", 30, ThemeRole.Background, ThemeRole.MainText);
            A.BottomStrip(cancelSend.transform, 18, 88, 24, 24);
            A.Size(sendingCard.GetComponent<Image>(), -1, 300);
            sendingCard.SetActive(false);

            TextMeshProUGUI intro = A.Label(content, "IntroText", "This product is not in GlowUp yet. Send what you can see and an admin will check it.", 30,
                ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            Fit(intro);

            Heading(content, "BarcodeHeading", "Barcode");
            GameObject barcodeRow = A.Node(content, "BarcodeRow");
            A.Size(barcodeRow.AddComponent<LayoutElement>(), -1, FieldH);
            TMP_InputField barcode = Input(barcodeRow.transform, "BarcodeInput", "Barcode digits");
            A.Fill(barcode.transform, 0, 0, 250, 0);
            barcode.contentType = TMP_InputField.ContentType.IntegerNumber; barcode.characterLimit = 14;
            Button scan = A.PillButton(barcodeRow.transform, "ScanButton", "Scan", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Scan);
            A.At(scan.transform, A.MR, Vector2.zero, new Vector2(230, FieldH));
            ((RectTransform)scan.transform).pivot = new Vector2(1f, 0.5f);

            Heading(content, "NameHeading", "Brand and product name");
            TMP_InputField brand = Input(content, "BrandInput", "Brand");
            brand.characterLimit = 60; A.Size(brand, -1, FieldH);
            TMP_InputField productName = Input(content, "ProductNameInput", "Product name");
            productName.characterLimit = 100; A.Size(productName, -1, FieldH);

            Heading(content, "LabelHeading", "Label text");
            GameObject photoRow = A.Node(content, "PhotoRow");
            A.Size(photoRow.AddComponent<LayoutElement>(), -1, 112);
            Button takePhoto = A.PillButton(photoRow.transform, "TakePhotoButton", "Take photo", 32, ThemeRole.Background, ThemeRole.AccentText, IconFactory.CameraIcon);
            A.Share(takePhoto.transform, 0f, 0.5f, 6); A.Outline(takePhoto.transform, ThemeRole.Accent, 112f);
            Button gallery = A.PillButton(photoRow.transform, "ChooseFromGalleryButton", "Choose from gallery", 32, ThemeRole.Background, ThemeRole.AccentText, IconFactory.Document);
            A.Share(gallery.transform, 0.5f, 1f, 6); A.Outline(gallery.transform, ThemeRole.Accent, 112f);
            A.Shrink(A.LabelOf(gallery), 20);
            TMP_InputField ingredients = Input(content, "IngredientTextInput", "Ingredient list, e.g. Aqua, Dimethicone, Parfum ...");
            ingredients.lineType = TMP_InputField.LineType.MultiLineNewline; ingredients.characterLimit = 4000;
            if (ingredients.textComponent != null) ingredients.textComponent.alignment = TextAlignmentOptions.TopLeft;
            A.Size(ingredients, -1, 280);
            TextMeshProUGUI readNote = A.Label(content, "ReadNoteText", "Read on this phone. Check and correct it. The photo is not sent.", 26,
                ThemeRole.SecondaryText, FontRole.Body, TextAlignmentOptions.Center, true);
            Fit(readNote);
            TMP_InputField note = Input(content, "NoteInput", "Note (optional)");
            note.characterLimit = 500; A.Size(note, -1, FieldH);
            TextMeshProUGUI feedback = A.Label(content, "FeedbackText", "", 28, ThemeRole.AccentText, FontRole.Body, TextAlignmentOptions.Center, true);
            Fit(feedback);
            feedback.gameObject.SetActive(false);
            Button send = A.PillButton(content, "ConfirmAndSendButton", "Confirm and send", 38, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Check);
            A.Size(send, -1, 124);

            // ---------------- sent ----------------
            GameObject sentPanel = A.Node(card, "SentPanel");
            A.Between(sentPanel.transform, A.HeaderBottom, 30, 30, 30);
            Image sentCard = A.Panel(sentPanel.transform, "SentCard", ThemeRole.Success, 0.16f, 48f);
            A.TopStrip(sentCard.transform, 20, 300, 0, 0);
            Image tick = A.Icon(sentCard.transform, "Tick", IconFactory.Check, 64, ThemeRole.Success);
            A.At(tick.transform, A.TC, new Vector2(0, -30), new Vector2(64, 64));
            TextMeshProUGUI sentTitle = A.Label(sentCard.transform, "TitleText", "Sent for review", 40, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.Center);
            A.TopStrip(sentTitle.transform, 106, 60, 30, 30);
            TextMeshProUGUI sentText = A.Label(sentCard.transform, "BodyText", "An admin will check it. The reply appears in Notifications.", 28,
                ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.Top, true);
            A.TopStrip(sentText.transform, 172, 110, 40, 40); A.ShrinkWrapped(sentText, 20);
            GameObject offline = StatusCard(sentPanel.transform, "OfflineCard", ThemeRole.Warning, "No internet", "Your report is kept on this phone and sent when you are online.", 200);
            A.TopStrip(offline.transform, 340, 200, 0, 0);
            offline.SetActive(false);
            Button scanAnother = A.PillButton(sentPanel.transform, "ScanAnotherButton", "Scan another", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.Scan);
            A.TopStrip(scanAnother.transform, 580, 120, 0, 0);
            Button home = A.PillButton(sentPanel.transform, "HomeButton", "Home", 36, ThemeRole.Background, ThemeRole.AccentText);
            A.TopStrip(home.transform, 724, 120, 0, 0); A.Outline(home.transform, ThemeRole.Accent, 120f);
            sentPanel.SetActive(false);

            // ---------------- camera (barcode or one label photo) ----------------
            GameObject cam = A.Node(card, "CameraPanel");
            A.Between(cam.transform, A.HeaderBottom, 30, 30, 30);
            Image viewport = A.RawPanel(cam.transform, "CameraViewport", new Color32(30, 26, 25, 255), 48f);
            A.Fill(viewport.transform, 0, 300, 0, 0);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            GameObject mirror = A.Node(viewport.transform, "MirrorRoot");
            A.Fill(mirror.transform);
            GameObject displayObject = A.Node(mirror.transform, "CameraDisplay", typeof(RawImage));
            A.Fill(displayObject.transform);
            var display = displayObject.GetComponent<RawImage>();
            display.color = new Color(0, 0, 0, 0); display.raycastTarget = false;
            GameObject frame = A.Node(viewport.transform, "ScanFrame", typeof(Image));
            A.Fill(frame.transform, 56, 196, 56, 140);
            var frameImage = frame.GetComponent<Image>(); frameImage.sprite = IconFactory.ScanFrame; frameImage.type = Image.Type.Sliced; frameImage.preserveAspect = false;
            frameImage.color = Color.white; frameImage.raycastTarget = false;
            Image hintBack = A.RawPanel(viewport.transform, "HintBackdrop", new Color(0, 0, 0, 0.5f), 60f);
            A.BottomStrip(hintBack.transform, 46, 104, 56, 56); hintBack.raycastTarget = false;
            TextMeshProUGUI hint = A.RawLabel(hintBack.transform, "HintText", "Hold the barcode inside the frame", 30, Color.white, FontRole.Label, TextAlignmentOptions.Center, true);
            A.Fill(hint.transform, 34, 6, 34, 6);
            Button shutter = A.PillButton(cam.transform, "TakePhotoButton", "Take photo", 36, ThemeRole.ButtonFill, ThemeRole.ButtonText, IconFactory.CameraIcon);
            A.BottomStrip(shutter.transform, 150, 120, 0, 0);
            Button camCancel = A.PillButton(cam.transform, "CancelButton", "Cancel", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(camCancel.transform, 10, 112, 0, 0);
            cam.SetActive(false);

            var confirm = ConfirmPopup(s, "DiscardPanel");

            var c = A.Controller<SendForReviewController>("SendForReviewController");
            var camera = SceneBuilderCore.Ensure<CameraFeedController>(c.gameObject);
            A.Set(camera, "display", display); A.Set(camera, "viewport", viewport.rectTransform); A.Set(camera, "mirrorRoot", mirror.transform);
            A.Set(camera, "useFrontCamera", false);

            A.Set(c, "backButton", s.back); A.Set(c, "formPanel", form); A.Set(c, "formScroll", scroll);
            A.Set(c, "barcodeInput", barcode); A.Set(c, "brandInput", brand); A.Set(c, "nameInput", productName);
            A.Set(c, "ingredientInput", ingredients); A.Set(c, "noteInput", note);
            A.Set(c, "scanButton", scan); A.Set(c, "takePhotoButton", takePhoto); A.Set(c, "galleryButton", gallery);
            A.Set(c, "sendButton", send); A.Set(c, "sendLabel", A.LabelOf(send)); A.Set(c, "feedbackText", feedback);
            A.Set(c, "cameraOffCard", cameraOff); A.Set(c, "openSettingsButton", openSettings); A.Set(c, "cameraOffGalleryButton", offGallery);
            A.Set(c, "offlineCard", offline); A.Set(c, "failedCard", failed); A.Set(c, "retryButton", retry);
            A.Set(c, "sendingCard", sendingCard); A.Set(c, "sendingText", sendingText); A.Set(c, "cancelSendButton", cancelSend);
            A.Set(c, "cameraPanel", cam); A.Set(c, "cameraFeed", camera); A.Set(c, "scanFrame", (RectTransform)frame.transform);
            A.Set(c, "cameraHint", hint); A.Set(c, "shutterButton", shutter); A.Set(c, "cameraCancelButton", camCancel);
            A.Set(c, "sentPanel", sentPanel); A.Set(c, "sentText", sentText); A.Set(c, "scanAnotherButton", scanAnother); A.Set(c, "homeButton", home);
            A.Set(c, "confirmDialog", confirm);
            A.Finish(s);
            Debug.Log("[GlowUp] SendForReviewScene built. Form, camera panel, Sent panel and the discard dialog are scene objects; words are on SendForReviewController.");
        }

        static void Heading(Transform parent, string name, string text)
        {
            TextMeshProUGUI t = A.Label(parent, name, text, 32, ThemeRole.MainText, FontRole.Label);
            A.Size(t, -1, 52);
        }

        static TMP_InputField Input(Transform parent, string name, string placeholder)
        {
            TMP_InputField f = SceneBuilderCore.MakeInput(parent, name, placeholder, A.TC, Vector2.zero, new Vector2(860, FieldH));
            Kit.StyleInput(f, placeholder, ThemeRole.InputBackground);
            f.text = "";
            return f;
        }

        /// <summary>A rounded message card: a bold title and one line under it. Height is set by the caller.</summary>
        static GameObject StatusCard(Transform parent, string name, ThemeRole tint, string title, string body, float height)
        {
            Image back = A.Panel(parent, name, tint, tint == ThemeRole.SurfaceAlt ? 1f : 0.18f, 40f);
            A.Size(back, -1, height);
            TextMeshProUGUI t = A.Label(back.transform, "TitleText", title, 32, ThemeRole.Heading, FontRole.Heading, TextAlignmentOptions.TopLeft);
            A.TopStrip(t.transform, 22, 48, 30, 30);
            TextMeshProUGUI b = A.Label(back.transform, "BodyText", body, 28, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.TopStrip(b.transform, 76, 96, 30, 30); A.ShrinkWrapped(b, 20);
            return back.gameObject;
        }

        static void Fit(TextMeshProUGUI t)
        {
            var f = SceneBuilderCore.Ensure<ContentSizeFitter>(t.gameObject);
            f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static GlowUp.AppSettings.ConfirmDialog ConfirmPopup(A.AppScreen s, string name)
        {
            A.AppPopup p = A.Overlay(s, name, "Discard this report?");
            A.At(p.box, A.MC, new Vector2(0, 60), new Vector2(900, 560));
            TextMeshProUGUI message = A.Label(p.box, "MessageText", "", 31, ThemeRole.MainText, FontRole.Body, TextAlignmentOptions.TopLeft, true);
            A.Fill(message.transform, 48, 190, 48, 130);
            A.ShrinkWrapped(message, 24);
            Button cancel = A.PillButton(p.box, "CancelButton", "Keep editing", 34, ThemeRole.SurfaceAlt, ThemeRole.MainText);
            A.BottomStrip(cancel.transform, 44, 108, 48, 462);
            Button ok = A.PillButton(p.box, "ConfirmButton", "Discard", 34, ThemeRole.ButtonFill, ThemeRole.ButtonText);
            A.BottomStrip(ok.transform, 44, 108, 462, 48);
            Object.DestroyImmediate(ok.GetComponent<ThemedGraphic>());                      // ConfirmDialog paints this button itself
            Object.DestroyImmediate(A.LabelOf(ok).GetComponent<ThemedGraphic>());
            var dialog = A.Controller<GlowUp.AppSettings.ConfirmDialog>(name + "Dialog");
            A.Set(dialog, "panel", p.panel); A.Set(dialog, "titleText", p.title); A.Set(dialog, "messageText", message);
            A.Set(dialog, "confirmButton", ok); A.Set(dialog, "confirmLabel", A.LabelOf(ok));
            A.Set(dialog, "cancelButton", cancel); A.Set(dialog, "cancelLabel", A.LabelOf(cancel));
            return dialog;
        }
    }
}
