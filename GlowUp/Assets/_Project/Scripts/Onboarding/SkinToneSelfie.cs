using System.Collections;
using System.Collections.Generic;
using GlowUp.AppSettings;
using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlowUp.Onboarding
{
    /// <summary>
    /// "Find my tone with a selfie" on Step 3 (panel revision, 22 Nov 2026). The user agrees first (what is read, what is kept), the front
    /// camera shows a live picture, a tap on Take photo reads the colour of the skin in the middle and selects the nearest tone tile
    /// (SkinToneMatch). The user can still pick another tile: lighting changes what a camera sees.
    /// The picture is kept in the app's own folder on THIS phone, small, and never sent anywhere (no AI, no database). "Delete my selfie" removes it.
    /// Every object is in the scene (SelfiePanel) so it can be restyled; every sentence is a field.
    /// </summary>
    public class SkinToneSelfie : MonoBehaviour
    {
        [Header("On the page")]
        [SerializeField] private Button openButton;
        [SerializeField] private TileSelectionGroup toneGroup;
        [Header("The pop-up (SelfiePanel)")]
        [SerializeField] private OverlayPanel panel;
        [SerializeField] private RawImage preview;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button takeButton;
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
        [SerializeField] private string msgNoCamera = "The camera is not available. Allow it in the phone settings, or choose the tone yourself.";
        [SerializeField] private string msgResult = "Your skin looks like {0}. It is selected for you: change it if it does not look right.";
        [SerializeField] private string msgKept = "The photo is kept on this phone.";
        [SerializeField] private string msgDeleted = "Your selfie was deleted from this phone.";
        [SerializeField] private float permissionWaitSeconds = 12f;

        WebCamTexture cam;
        Coroutine starting;
        const string ConsentKey = "glowup.selfie.consent";

        void Awake()
        {
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (takeButton != null) takeButton.onClick.AddListener(Take);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (deleteButton != null) deleteButton.onClick.AddListener(DeletePhoto);
            if (panel != null) panel.Closed += StopCamera;
            RefreshDelete();
        }

        void OnDestroy() { StopCamera(); }

        void Open()
        {
            bool agreed = false;
            try { agreed = PlayerPrefs.GetInt(ConsentKey, 0) == 1; } catch (System.Exception) { }
            if (agreed || consentDialog == null) { StartCamera(); return; }
            consentDialog.Ask(consentTitle, consentBody, consentAgree, () =>
            {
                try { PlayerPrefs.SetInt(ConsentKey, 1); PlayerPrefs.Save(); } catch (System.Exception) { }
                StartCamera();
            }, false, consentCancel);
        }

        void StartCamera()
        {
            if (starting != null) StopCoroutine(starting);
            if (panel != null) panel.Show();
            Say(msgGuide);
            if (takeButton != null) takeButton.interactable = false;
            starting = StartCoroutine(Starting());
        }

        IEnumerator Starting()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                float until = Time.unscaledTime + permissionWaitSeconds;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.unscaledTime < until) yield return null;
            }
#endif
            yield return CameraHandover.WaitUntilFree();
            if (!CameraPermission.Granted || WebCamTexture.devices.Length == 0) { Say(msgNoCamera); yield break; }
            string name = WebCamTexture.devices[0].name;
            foreach (WebCamDevice d in WebCamTexture.devices) if (d.isFrontFacing) { name = d.name; break; }
            cam = new WebCamTexture(name, 640, 480, 30);
            cam.Play();
            float wait = Time.unscaledTime + 6f;
            while (cam != null && cam.width < 100 && Time.unscaledTime < wait) yield return null;
            if (cam == null || cam.width < 100) { Say(msgNoCamera); yield break; }
            if (preview != null)
            {
                preview.texture = cam;
                preview.rectTransform.localEulerAngles = new Vector3(0, 0, -cam.videoRotationAngle);
                preview.uvRect = cam.videoVerticallyMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            }
            if (takeButton != null) takeButton.interactable = true;
        }

        void Take()
        {
            if (cam == null || cam.width < 100) return;
            int w = cam.width, h = cam.height;
            Color32[] all = cam.GetPixels32();
            // a grid of points inside the middle of the picture (where a face held in the middle has cheeks and forehead)
            var points = new List<Color32>();
            int x0 = (int)(w * 0.38f), x1 = (int)(w * 0.62f), y0 = (int)(h * 0.38f), y1 = (int)(h * 0.62f);
            for (int y = y0; y < y1; y += 3) for (int x = x0; x < x1; x += 3) points.Add(all[y * w + x]);
            int index = SkinToneMatch.Nearest(SkinToneMatch.Median(points));
            string tone = index >= 0 ? SkinToneMatch.Names[index] : "";
            if (!string.IsNullOrEmpty(tone) && toneGroup != null) toneGroup.Select(tone, true);

            // the kept picture: a small square from the middle, JPEG
            int side = Mathf.Min(w, h), size = 256;
            var square = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    square.SetPixel(x, y, all[((h - side) / 2 + y * side / size) * w + (w - side) / 2 + x * side / size]);
            square.Apply();
            bool kept = SelfieStore.Save(square.EncodeToJPG(80));
            Destroy(square);
            RefreshDelete();
            Say(string.Format(msgResult, tone) + (kept ? " " + msgKept : ""));
            StopCamera();
        }

        void DeletePhoto() { SelfieStore.Delete(); RefreshDelete(); Say(msgDeleted); }
        void RefreshDelete() { if (deleteButton != null) deleteButton.gameObject.SetActive(SelfieStore.Exists); }
        void Close() { StopCamera(); if (panel != null) panel.Hide(); }
        void Say(string text) { if (statusText != null) statusText.text = text; }

        void StopCamera()
        {
            if (starting != null) { StopCoroutine(starting); starting = null; }
            if (cam != null) { try { cam.Stop(); } catch (System.Exception) { } Destroy(cam); cam = null; CameraHandover.Released(); }
            if (preview != null) preview.texture = null;
        }
    }
}
