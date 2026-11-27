using GlowUp.Scan;
using GlowUp.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using A = GlowUp.EditorTools.AppKit;

namespace GlowUp.EditorTools
{
    /// <summary>The sun (auto brighten) and padlock (focus lock) controls on a camera picture, shared by the selfie camera of Step 3.
    /// Moved out of the removed Admin Scanner builder (27 Nov 2026) unchanged.</summary>
    public static class CameraControlsKit
    {
        internal static GlowUp.Scan.AutoBrighten AutoBrightenControl(Transform viewport, CameraFeedController camera, Vector2 offset)
        {
            GameObject host = A.Node(viewport, "AutoBrighten", typeof(GlowUp.Scan.AutoBrighten));
            A.Fill(host.transform);
            Image veil = A.RawPanel(host.transform, "BrightenVeil", new Color(1, 1, 1, 0.22f), 0f);
            veil.raycastTarget = false;
            A.Fill(veil.transform);
            veil.gameObject.SetActive(false);
            // the veil sits right above the camera picture, below the checklist, the hint and every round button
            Transform picture = camera != null && camera.Display != null ? camera.Display.transform : null;
            while (picture != null && picture.parent != viewport) picture = picture.parent;
            veil.transform.SetParent(viewport, false);
            if (picture != null) veil.transform.SetSiblingIndex(picture.GetSiblingIndex() + 1);
            Button sun = SmartScanSceneBuilder.CameraButton(host.transform, "BrightenButton", IconFactory.Sun, out Image sunIcon);
            A.At(sun.transform, A.TR, offset, new Vector2(92, 92));
            var brighten = host.GetComponent<GlowUp.Scan.AutoBrighten>();
            A.Set(brighten, "button", sun); A.Set(brighten, "icon", sunIcon); A.Set(brighten, "background", sun.targetGraphic); A.Set(brighten, "veil", veil);
            return brighten;
        }

        internal static void FocusLockControl(Transform viewport, CameraFeedController camera)
        {
            GameObject focusHelper = A.Node(viewport, "FocusAssistant", typeof(GlowUp.Scan.ScanFocusAssistant));
            A.Fill(focusHelper.transform);
            Button focusLock = SmartScanSceneBuilder.CameraButton(focusHelper.transform, "FocusLockButton", IconFactory.LockOpen, out Image focusLockIcon);
            A.At(focusLock.transform, A.TC, new Vector2(0, -24), new Vector2(92, 92));
            Image focusNote = A.RawPanel(focusHelper.transform, "FocusNote", new Color(0, 0, 0, 0.72f), 40f);
            focusNote.raycastTarget = false;
            A.At(focusNote.transform, A.TC, new Vector2(0, -126), new Vector2(560, 72));
            TextMeshProUGUI focusNoteText = A.RawLabel(focusNote.transform, "NoteText", "Focus locked. Tap the padlock to release it.", 26, Color.white, FontRole.Label, TextAlignmentOptions.Center, true);
            A.Fill(focusNoteText.transform, 22, 6, 22, 6);
            A.ShrinkWrapped(focusNoteText, 20);
            CanvasGroup focusNoteGroup = SceneBuilderCore.Ensure<CanvasGroup>(focusNote.gameObject);
            focusNoteGroup.blocksRaycasts = false; focusNoteGroup.interactable = false;
            focusNote.gameObject.SetActive(false);
            var assistant = focusHelper.GetComponent<GlowUp.Scan.ScanFocusAssistant>();
            A.Set(assistant, "lockButton", focusLock); A.Set(assistant, "lockIcon", focusLockIcon);
            A.Set(assistant, "lockedSprite", IconFactory.LockClosed); A.Set(assistant, "freeSprite", IconFactory.LockOpen);
            A.Set(assistant, "lockBackground", focusLock.targetGraphic);
            A.Set(assistant, "note", focusNoteGroup); A.Set(assistant, "noteText", focusNoteText);
            Image focusRing = A.RawIcon(viewport, "FocusRing", IconFactory.RingThin, 170, new Color(1f, 0.86f, 0.5f, 1f));
            focusRing.raycastTarget = false;
            A.At(focusRing.transform, A.MC, Vector2.zero, new Vector2(170, 170));
            focusRing.gameObject.SetActive(false);
            A.Set(camera, "focusRing", focusRing.rectTransform);
            A.Set(camera, "tapToFocus", true);
            A.Set(camera, "tapLocksFocus", true);
            A.Set(assistant, "cameraFeed", camera);
        }
    }
}
