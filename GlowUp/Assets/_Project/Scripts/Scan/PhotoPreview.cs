using System;
using GlowUp.Common;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE PHOTO, BEFORE IT IS USED (7 November 2026): the whole photo that was just taken, on a card over the scan screen, with
    /// "Retake" and "Use photo". Before the first photo is ever sent it also says, in one line, that GlowUp will review it.
    /// While the photo is read, "Use photo" shows the spinner inside it and "Retake" becomes "Not now" (always usable). A problem is
    /// said in the status line under the photo, with a link where the user can go on ("Type it in").
    ///
    /// Built by code on the screen's own canvas, so the scan scene needs no rebuild. Words come from ScanSnapshotController.
    /// </summary>
    public class PhotoPreview
    {
        public event Action Retake, Use, NotNow;
        /// <summary>The card was closed without a choice: the X, a tap beside the card, or the phone's back key.</summary>
        public event Action Closed;

        PhotoCropBox crop;
        /// <summary>The part of the photo the user boxed, in the photo's 0..1 space (bottom-left origin).</summary>
        public Rect CropArea => crop != null ? crop.Area : new Rect(0f, 0f, 1f, 1f);
        public void ShowTextShapes(Rect[] inCrop) { if (crop != null) crop.ShowShapes(inCrop); }
        public void CloseByBack() { if (IsOpen) Closed?.Invoke(); }

        GameObject root;
        RawImage picture;
        AspectRatioFitter fit;
        TextMeshProUGUI notice, title;
        StatusMessage status;
        Button retakeButton, useButton;
        TextMeshProUGUI retakeLabel, useLabel;
        bool busy;
        string retakeWords = "Retake", useWords = "Use photo", notNowWords = "Not now";

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>Builds the card (hidden) on the top canvas above `near`. `fontFrom` gives the fonts when the scene has no theme.</summary>
        public static PhotoPreview Build(Transform near, TMP_Text fontFrom, string titleWords, string noticeWords, string retake, string use, string notNow)
        {
            Canvas canvas = near != null ? near.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            Transform top = canvas.rootCanvas.transform;
            ThemeProfile.Palette p = RuntimeTheme.Palette;
            var preview = new PhotoPreview { retakeWords = retake, useWords = use, notNowWords = notNow };

            RectTransform rootRt = RuntimeUiKit.Rect("PhotoPreview", top);
            RuntimeUiKit.Stretch(rootRt);
            preview.root = rootRt.gameObject;
            var layer = preview.root.AddComponent<Canvas>();
            layer.overrideSorting = true; layer.sortingOrder = 5000;
            preview.root.AddComponent<GraphicRaycaster>();
            var dim = preview.root.AddComponent<Image>();
            dim.color = new Color32(43, 27, 33, 158);                   // a warm deep rose dim (not black or blue): blocks the screen behind it
            var outside = preview.root.AddComponent<Button>();          // a tap beside the card closes it
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(() => preview.Closed?.Invoke());

            Image card = RuntimeUiKit.Box(rootRt, "Card", p.surface);
            RectTransform cardRt = card.rectTransform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f); cardRt.anchorMax = new Vector2(0.5f, 0.5f); cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(Mathf.Min(980f, rootRt.rect.width > 0 ? rootRt.rect.width - 60f : 980f), 0f);
            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(40, 40, 40, 40); column.spacing = 28;
            column.childControlWidth = true; column.childControlHeight = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            preview.title = RuntimeUiKit.Label(cardRt, "Title", titleWords, 50, p.heading, RuntimeTheme.Font(FontRole.Heading, fontFrom), TextAlignmentOptions.Left);
            preview.title.fontStyle = FontStyles.Bold;
            preview.title.margin = new Vector4(0f, 0f, 110f, 0f);                     // room for the X
            // the X at the top right of the card (outside the column's layout)
            Button close = RuntimeUiKit.Pill(cardRt, "CloseButton", "×", p.surfaceAlt, p.mainText, RuntimeTheme.Font(FontRole.Button, fontFrom));
            var closeElement = close.GetComponent<LayoutElement>(); closeElement.ignoreLayout = true;
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = closeRt.anchorMax = closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(96f, 96f); closeRt.anchoredPosition = new Vector2(-24f, -24f);
            close.GetComponentInChildren<TextMeshProUGUI>().text = "";
            for (int i = 0; i < 2; i++)                                                 // the X drawn as two bars (no font glyph needed)
            {
                Image bar = RuntimeUiKit.Box(closeRt, "Bar" + i, p.mainText, false);
                bar.raycastTarget = false;
                bar.rectTransform.sizeDelta = new Vector2(40f, 5f);
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            }
            close.onClick.AddListener(() => preview.Closed?.Invoke());
            // a click on the card itself must not reach the "tap beside" button behind it
            card.raycastTarget = true;

            // the photo: shown whole (fitted), on a soft frame
            Image frame = RuntimeUiKit.Box(cardRt, "PhotoFrame", p.surfaceAlt);
            var frameElement = frame.gameObject.AddComponent<LayoutElement>();
            frameElement.preferredHeight = 760; frameElement.minHeight = 400;
            RectTransform holder = RuntimeUiKit.Rect("Photo", frame.transform);
            RuntimeUiKit.Stretch(holder, 12, 12, 12, 12);
            preview.picture = holder.gameObject.AddComponent<RawImage>();
            preview.picture.raycastTarget = false;
            preview.fit = holder.gameObject.AddComponent<AspectRatioFitter>();
            preview.fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            preview.crop = PhotoCropBox.Over(holder);                  // the box the user moves over the product's text

            preview.notice = RuntimeUiKit.Label(cardRt, "Notice", noticeWords, 40, p.secondaryText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Left);

            TextMeshProUGUI statusText = RuntimeUiKit.Label(cardRt, "Status", "", 40, p.mainText, RuntimeTheme.Font(FontRole.Body, fontFrom), TextAlignmentOptions.Left);
            statusText.enableWordWrapping = true;
            preview.status = StatusMessage.Wrap(statusText);
            preview.status.Hide();
            // Phase 1C: the way on ("Type it in") is a full-width button under the message, not small underlined words inside it
            preview.linkButton = RuntimeUiKit.Pill(cardRt, "MessageLinkButton", "", p.accentText, p.accentText, RuntimeTheme.Font(FontRole.Button, fontFrom), true);
            preview.linkLabel = preview.linkButton.GetComponentInChildren<TextMeshProUGUI>();
            preview.linkButton.onClick.AddListener(() => preview.linkAction?.Invoke());
            preview.linkButton.gameObject.SetActive(false);

            RectTransform row = RuntimeUiKit.Rect("Buttons", cardRt);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 24; rowLayout.childControlWidth = true; rowLayout.childControlHeight = true; rowLayout.childForceExpandWidth = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 132;
            TMP_FontAsset buttonFont = RuntimeTheme.Font(FontRole.Button, fontFrom);
            preview.retakeButton = RuntimeUiKit.Pill(row, "RetakeButton", retake, p.accent, p.mainText, buttonFont, true);
            preview.useButton = RuntimeUiKit.Pill(row, "UsePhotoButton", use, p.buttonFill, p.buttonText, buttonFont);
            preview.retakeLabel = preview.retakeButton.GetComponentInChildren<TextMeshProUGUI>();
            preview.useLabel = preview.useButton.GetComponentInChildren<TextMeshProUGUI>();
            preview.retakeButton.onClick.AddListener(() => { if (preview.busy) preview.NotNow?.Invoke(); else preview.Retake?.Invoke(); });
            preview.useButton.onClick.AddListener(() => { if (!preview.busy) preview.Use?.Invoke(); });

            preview.root.SetActive(false);
            return preview;
        }

        /// <summary>Opens the card with `photo` (not owned: the caller destroys it). `firstTime` shows the review notice.</summary>
        public void Show(Texture2D photo, bool firstTime)
        {
            if (root == null) return;
            picture.texture = photo;
            if (crop != null) { crop.Reset(); crop.SetEditable(true); }
            fit.aspectRatio = photo != null && photo.height > 0 ? photo.width / (float)photo.height : 1f;
            notice.gameObject.SetActive(firstTime);
            status.Hide();
            HideLink();
            useButton.gameObject.SetActive(true);
            SetBusy(false, "", "");
            root.transform.SetAsLastSibling();
            root.SetActive(true);
        }

        public void Hide()
        {
            if (root == null) return;
            SetBusy(false, "", "");
            picture.texture = null;
            root.SetActive(false);
        }

        /// <summary>Busy: "Use photo" turns its spinner and cannot be pressed; "Retake" reads "Not now".</summary>
        public void SetBusy(bool on, string busyWords, string shortBusyWords)
        {
            busy = on;
            if (crop != null) crop.SetEditable(!on);            // the box cannot move while its text is being read
            ButtonBusy.Set(useButton, useLabel, on, on ? busyWords : useWords, shortBusyWords);
            if (retakeLabel != null) retakeLabel.text = on ? notNowWords : retakeWords;
        }

        public void Say(StatusMessage.Kind kind, string words, string link = null, Action onLink = null)
        {
            bool hasLink = !string.IsNullOrEmpty(link) && onLink != null && linkButton != null;
            if (status != null) status.Show(kind, words, hasLink ? null : link, hasLink ? null : onLink);
            if (!hasLink) { HideLink(); return; }
            linkAction = onLink;
            if (linkLabel != null) linkLabel.text = StatusMessage.Plain(link);
            linkButton.gameObject.SetActive(true);
        }

        Button linkButton;
        TextMeshProUGUI linkLabel;
        Action linkAction;

        void HideLink()
        {
            linkAction = null;
            if (linkButton != null) linkButton.gameObject.SetActive(false);
        }

        /// <summary>A problem with this photo: said in the status line; "Use photo" goes away when the photo cannot be used.</summary>
        public void Problem(string words, string link, Action onLink, bool photoStillUsable)
        {
            SetBusy(false, "", "");
            useButton.gameObject.SetActive(photoStillUsable);
            Say(StatusMessage.Kind.Error, words, link, onLink);
        }
    }
}
