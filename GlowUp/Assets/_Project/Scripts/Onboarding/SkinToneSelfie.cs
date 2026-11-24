using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Common;
using GlowUp.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// "Find my tone with a selfie" on Step 3 (panel revision, 22 Nov 2026; flow redone 24 Nov 2026).
    ///  1. The user agrees first (what is read, what is kept).
    ///  2. The FRONT camera shows a live picture through the same camera component as the AI Smart Scan screen (CameraFeedController),
    ///     with no scan grid and no moving line. Tap Take photo.
    ///  3. The picture freezes and the panel SHOWS HOW the tone was found: a box on the photo marks the part of the face that was read, then
    ///     two swatches: the skin colour found there, and the nearest of the six tones (SkinToneMatch, measured in CIE Lab).
    ///  4. "Use tone" selects that tone tile and closes; "Retake" goes back to the live picture. The user can still pick another tile:
    ///     lighting changes what a camera sees.
    /// The picture is kept small in the app's own folder on THIS phone and never sent anywhere (no AI, no database). "Delete my selfie" removes it.
    /// Every object is in the scene (SelfiePanel) so it can be restyled; every sentence is a field.
    /// </summary>
    public class SkinToneSelfie : MonoBehaviour
    {
        [Header("On the page")]
        [SerializeField] private Button openButton;
        [SerializeField] private TileSelectionGroup toneGroup;
        [Header("The pop-up (SelfiePanel)")]
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private CameraFeedController cameraFeed;
        [Tooltip("The frozen photo shown after Take photo (same size as the live view)")]
        [SerializeField] private RawImage resultImage;
        [Tooltip("The box drawn on the frozen photo: the part of the face that was read")]
        [SerializeField] private RectTransform sampleBox;
        [SerializeField] private GameObject resultRow;
        [SerializeField] private Image foundSwatch;
        [SerializeField] private Image toneSwatch;
        [SerializeField] private TextMeshProUGUI foundLabel;
        [SerializeField] private TextMeshProUGUI toneLabel;
        [SerializeField] private TextMeshProUGUI statusText;
        [Tooltip("Take photo; after a photo it reads \"Use tone\"")]
        [SerializeField] private Button takeButton;
        [Tooltip("Close; after a photo it reads \"Retake\"")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button deleteButton;
        [Tooltip("Asks for the user's agreement before the camera starts (ConfirmDialog)")]
        [SerializeField] private ConfirmDialog consentDialog;

        [Header("Words")]
        [SerializeField] private string consentTitle = "Use a selfie?";
        [TextArea(3, 6)] [SerializeField] private string consentBody = "GlowUp reads the colour of the skin in the middle of one photo to suggest a skin tone. The photo is kept small in your profile on this phone only: it is not sent to the AI or to the database. You can delete it at any time, and the suggestion is yours to change.";
        [SerializeField] private string consentAgree = "I agree";
        [SerializeField] private string consentCancel = "Cancel";
        [SerializeField] private string msgGuide = "Face the window or a lamp, take off glasses, and keep your face in the middle. Then tap Take photo.";
        [SerializeField] private string msgStarting = "Starting the front camera...";
        [SerializeField] private string msgNoCamera = "The camera is not available. Allow it in the phone settings, or choose the tone yourself.";
        [SerializeField] private string msgResult = "GlowUp read the box on your face. Your skin looks like {0}. Tap Use tone, or Retake if the light was poor.";
        [SerializeField] private string msgFoundLabel = "Skin colour found";
        [SerializeField] private string msgToneLabel = "Closest tone: {0}";
        [SerializeField] private string msgKept = "The photo is kept on this phone.";
        [SerializeField] private string msgDeleted = "Your selfie was deleted from this phone.";
        [SerializeField] private string wordTake = "Take photo", wordUse = "Use tone", wordClose = "Close", wordRetake = "Retake";

        /// <summary>The middle part of the picture that is read, as fractions of its width (the height follows, so the box is square).</summary>
        const float BoxLeft = 0.36f, BoxWidth = 0.28f;
        const string ConsentKey = "glowup.selfie.consent";

        [SerializeField] private AutoBrighten brighten;     // 24 Nov 2026: the sun button (live veil; evens out the photo shown after the capture)
        Texture2D enhanced;               // the brightened copy of the frozen photo, while Auto brighten is on
        Texture2D frozen;                 // the photo shown on the result card (destroyed when the card closes)
        int foundIndex = -1;              // the tone tile that fits; -1 = no result yet
        bool cameraReady;

        bool ShowingResult => foundIndex >= 0;

        void Awake()
        {
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (takeButton != null) takeButton.onClick.AddListener(OnRight);
            if (closeButton != null) closeButton.onClick.AddListener(OnLeft);
            if (deleteButton != null) deleteButton.onClick.AddListener(DeletePhoto);
            if (panel != null) panel.Closed += Reset;
            if (cameraFeed != null) cameraFeed.Started += OnCameraStarted;
            if (brighten != null) brighten.Changed += OnBrighten;
            RefreshDelete();
        }

        void OnDestroy()
        {
            if (cameraFeed != null) cameraFeed.Started -= OnCameraStarted;
            if (brighten != null) brighten.Changed -= OnBrighten;
            Reset();
        }

        void Open()
        {
            bool agreed = false;
            try { agreed = PlayerPrefs.GetInt(ConsentKey, 0) == 1; } catch (System.Exception) { }
            if (agreed || consentDialog == null) { StartLive(); return; }
            consentDialog.Ask(consentTitle, consentBody, consentAgree, () =>
            {
                try { PlayerPrefs.SetInt(ConsentKey, 1); PlayerPrefs.Save(); } catch (System.Exception) { }
                StartLive();
            }, false, consentCancel);
        }

        /// <summary>The live front-camera picture (also used by Retake).</summary>
        void StartLive()
        {
            ClearResult();
            cameraReady = false;
            if (brighten != null) brighten.ShowVeil(true);
            if (panel != null && !panel.IsOpen) panel.Show();
            Say(msgStarting);
            SetWords();
            if (cameraFeed != null) cameraFeed.StartCamera(); else Say(msgNoCamera);
        }

        void OnCameraStarted(bool ok, string message)
        {
            if (this == null || ShowingResult) return;
            cameraReady = ok;
            Say(ok ? msgGuide : (string.IsNullOrEmpty(message) ? msgNoCamera : message));
            SetWords();
        }

        // the two buttons change their words with the step: [Close | Take photo] before a photo, [Retake | Use tone] after it
        void OnLeft() { if (ShowingResult) StartLive(); else Close(); }
        void OnRight() { if (ShowingResult) UseTone(); else Take(); }

        void Take()
        {
            if (cameraFeed == null || !cameraFeed.IsPlaying) { Say(msgNoCamera); return; }
            Texture2D photo = cameraFeed.CaptureUpright(cameraFeed.Viewport, 720);
            if (photo == null) { Say(msgNoCamera); return; }
            int w = photo.width, h = photo.height;
            // the box that is read: a square in the middle of the picture (where a face held in the middle has cheeks and forehead)
            float heightFraction = Mathf.Clamp(BoxWidth * w / Mathf.Max(1, h), 0.05f, 0.9f);
            float boxBottom = 0.5f - heightFraction / 2f;
            int x0 = (int)(w * BoxLeft), x1 = (int)(w * (BoxLeft + BoxWidth)), y0 = (int)(h * boxBottom), y1 = (int)(h * (boxBottom + heightFraction));
            Color32[] all = photo.GetPixels32();
            var points = new List<Color32>();
            for (int y = y0; y < y1; y += 3) for (int x = x0; x < x1; x += 3) points.Add(all[y * w + x]);
            Color32 found = SkinToneMatch.Median(points);
            foundIndex = SkinToneMatch.Nearest(found);
            string tone = foundIndex >= 0 ? SkinToneMatch.Names[foundIndex] : "";

            // the kept picture: a small square from the middle, JPEG (as before)
            int side = Mathf.Min(w, h), size = 256;
            var square = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    square.SetPixel(x, y, all[((h - side) / 2 + y * side / size) * w + (w - side) / 2 + x * side / size]);
            square.Apply();
            bool kept = SelfieStore.Save(square.EncodeToJPG(80));
            Destroy(square);
            RefreshDelete();

            // freeze the picture and show how the tone was found
            if (cameraFeed != null) cameraFeed.StopCamera();
            if (frozen != null) Destroy(frozen);
            // 24 Nov 2026 (owner: the photo came out flipped): the live selfie view is a mirror, so the kept picture is turned the same way
            if (cameraFeed != null && cameraFeed.ShowsMirrored)
            {
                for (int y = 0; y < h; y++) System.Array.Reverse(all, y * w, w);
                photo.SetPixels32(all); photo.Apply();
            }
            frozen = photo;
            if (brighten != null) brighten.ShowVeil(false);
            ShowFrozen();
            if (sampleBox != null)
            {
                sampleBox.anchorMin = new Vector2(BoxLeft, boxBottom); sampleBox.anchorMax = new Vector2(BoxLeft + BoxWidth, boxBottom + heightFraction);
                sampleBox.offsetMin = sampleBox.offsetMax = Vector2.zero;
                sampleBox.gameObject.SetActive(true);
            }
            if (foundSwatch != null) foundSwatch.color = found;
            if (toneSwatch != null && foundIndex >= 0) toneSwatch.color = SkinToneMatch.Palette[foundIndex];
            if (foundLabel != null) foundLabel.text = msgFoundLabel;
            if (toneLabel != null) toneLabel.text = string.Format(msgToneLabel, tone);
            if (resultRow != null) resultRow.SetActive(true);
            Say(string.Format(msgResult, tone) + (kept ? " " + msgKept : ""));
            SetWords();
        }

        /// <summary>The photo on the result card: evened out while Auto brighten is on, as taken otherwise. The tone was read from the untouched photo.</summary>
        void ShowFrozen()
        {
            if (resultImage == null || frozen == null) return;
            if (enhanced != null) { Destroy(enhanced); enhanced = null; }
            if (brighten != null && brighten.On) enhanced = AutoBrighten.Enhanced(frozen);
            resultImage.texture = enhanced != null ? enhanced : frozen;
            resultImage.gameObject.SetActive(true);
        }

        void OnBrighten(bool on)
        {
            if (ShowingResult) ShowFrozen(); else if (brighten != null) brighten.ShowVeil(true);
        }

        void UseTone()
        {
            if (foundIndex >= 0 && toneGroup != null) toneGroup.Select(SkinToneMatch.Names[foundIndex], true);
            Close();
        }

        void DeletePhoto() { SelfieStore.Delete(); RefreshDelete(); Say(msgDeleted); }
        void RefreshDelete() { if (deleteButton != null) deleteButton.gameObject.SetActive(SelfieStore.Exists); }
        void Close() { Reset(); if (panel != null) panel.Hide(); }
        void Say(string text) { if (statusText != null) statusText.text = text; }

        void SetWords()
        {
            SetLabel(takeButton, ShowingResult ? wordUse : wordTake);
            SetLabel(closeButton, ShowingResult ? wordRetake : wordClose);
            if (takeButton != null) takeButton.interactable = ShowingResult || cameraReady;
        }

        static void SetLabel(Button b, string words)
        {
            if (b == null) return;
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = words;
        }

        void ClearResult()
        {
            foundIndex = -1;
            if (resultImage != null) { resultImage.texture = null; resultImage.gameObject.SetActive(false); }
            if (sampleBox != null) sampleBox.gameObject.SetActive(false);
            if (resultRow != null) resultRow.SetActive(false);
            if (frozen != null) { Destroy(frozen); frozen = null; }
            if (enhanced != null) { Destroy(enhanced); enhanced = null; }
        }

        /// <summary>The camera is released and the frozen photo dropped (the panel closes, the page goes away, or Back is pressed).</summary>
        void Reset()
        {
            if (cameraFeed != null) cameraFeed.StopCamera();
            ClearResult();
            cameraReady = false;
        }
    }
}
