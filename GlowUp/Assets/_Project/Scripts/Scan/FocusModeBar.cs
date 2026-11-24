using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Scan
{
    /// <summary>
    /// The focus control on a live camera view (24 Nov 2026): a small pill at the TOP CENTRE of the picture with two choices.
    ///   Auto   - the camera keeps the picture sharp by itself; one tap on the picture refocuses there for a moment.
    ///   Manual - the camera focuses where you tap and HOLDS that focus until you tap elsewhere or choose Auto.
    /// A circle shows where it focuses. A camera that cannot be told where to focus (many front cameras) says so instead of pretending.
    /// Built when the screen starts, on top of the camera's viewport; nothing in the scene needs to be wired.
    /// </summary>
    public class FocusModeBar : MonoBehaviour
    {
        CameraFeedController feed;
        Image autoBack, manualBack;
        TextMeshProUGUI autoText, manualText, note;
        float noteUntil;
        bool manual;

        public const string Auto = "Auto", Manual = "Manual";

        public static FocusModeBar Attach(CameraFeedController feed)
        {
            if (feed == null || feed.Viewport == null) return null;
            var existing = feed.Viewport.GetComponentInChildren<FocusModeBar>(true);
            if (existing != null) return existing;
            var host = RuntimeUiKit.Rect("FocusModeBar", feed.Viewport);
            var bar = host.gameObject.AddComponent<FocusModeBar>();
            bar.Build(host, feed);
            return bar;
        }

        void Build(RectTransform host, CameraFeedController camera)
        {
            feed = camera;
            var p = RuntimeTheme.Palette;
            TMP_FontAsset font = RuntimeTheme.Font(GlowUp.Theme.FontRole.Label, feed.Viewport.GetComponentInChildren<TMP_Text>(true));
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 1f); host.pivot = new Vector2(0.5f, 1f);
            host.anchoredPosition = new Vector2(0f, -22f); host.sizeDelta = new Vector2(330f, 108f);

            Image back = RuntimeUiKit.Box(host, "Pill", new Color(0.09f, 0.07f, 0.08f, 0.55f));
            RuntimeUiKit.Stretch(back.rectTransform);
            back.raycastTarget = true;                       // a tap on the pill never reaches the picture below it
            autoBack = Segment(host, "Auto", Auto, font, 0f, 0.5f, out autoText);
            manualBack = Segment(host, "Manual", Manual, font, 0.5f, 1f, out manualText);
            autoBack.GetComponent<Button>().onClick.AddListener(() => Choose(false));
            manualBack.GetComponent<Button>().onClick.AddListener(() => Choose(true));

            note = RuntimeUiKit.Label(host, "FocusNote", "", 26, Color.white, font, TextAlignmentOptions.Center);
            var nr = note.rectTransform; nr.anchorMin = new Vector2(0.5f, 0f); nr.anchorMax = new Vector2(0.5f, 0f); nr.pivot = new Vector2(0.5f, 1f);
            nr.anchoredPosition = new Vector2(0f, -10f); nr.sizeDelta = new Vector2(620f, 60f);
            note.enableWordWrapping = false; note.raycastTarget = false;
            var shade = RuntimeUiKit.Box(nr, "NoteBack", new Color(0.09f, 0.07f, 0.08f, 0.55f)); shade.raycastTarget = false;
            RuntimeUiKit.Stretch(shade.rectTransform); shade.transform.SetAsFirstSibling(); noteBack = shade;
            noteBack.gameObject.SetActive(false);

            if (!feed.HasFocusRing)
            {
                Image ring = RuntimeUiKit.Icon(feed.Viewport, "FocusRing", RuntimeIcons.Ring, new Color(1f, 0.86f, 0.35f, 1f), 150f);
                ring.gameObject.SetActive(false);
                feed.SetFocusRing(ring.rectTransform);
            }
            feed.TapToFocus = true;
            Paint();
        }

        Image noteBack;

        Image Segment(RectTransform host, string name, string words, TMP_FontAsset font, float from, float to, out TextMeshProUGUI label)
        {
            var p = RuntimeTheme.Palette;
            Image seg = RuntimeUiKit.Box(host, name + "Segment", Color.clear);
            var rt = seg.rectTransform; rt.anchorMin = new Vector2(from, 0f); rt.anchorMax = new Vector2(to, 1f); rt.offsetMin = new Vector2(8f, 8f); rt.offsetMax = new Vector2(-8f, -8f);
            seg.gameObject.AddComponent<Button>().targetGraphic = seg;
            label = RuntimeUiKit.Label(seg.transform, "Label", words, 32, Color.white, font, TextAlignmentOptions.Center);
            label.raycastTarget = false; label.fontStyle = FontStyles.Bold;
            RuntimeUiKit.Stretch(label.rectTransform);
            return seg;
        }

        void Choose(bool toManual)
        {
            manual = toManual;
            feed.TapLocksFocus = toManual;
            feed.TapToFocus = true;
            if (toManual)
            {
                bool locked = feed.CanLockFocus && feed.LockFocus();
                Say(locked ? "Tap the picture to focus. It stays sharp there." : "This camera cannot be focused by hand. It focuses by itself.");
            }
            else
            {
                feed.ReleaseFocus();
                feed.Refocus();
                Say("Auto focus. Tap the picture to refocus.");
            }
            Paint();
        }

        void Say(string words) { note.text = words; noteBack.gameObject.SetActive(true); noteUntil = Time.unscaledTime + 3.2f; }

        void Update()
        {
            if (noteBack != null && noteBack.gameObject.activeSelf && Time.unscaledTime > noteUntil) noteBack.gameObject.SetActive(false);
        }

        void Paint()
        {
            var p = RuntimeTheme.Palette;
            Color on = p.buttonFill, off = Color.clear;
            autoBack.color = manual ? off : on; manualBack.color = manual ? on : off;
            autoText.color = manualText.color = Color.white;
        }
    }
}
