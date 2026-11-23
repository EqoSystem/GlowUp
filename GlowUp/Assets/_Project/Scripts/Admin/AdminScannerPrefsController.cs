using GlowUp.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.Admin
{
    /// <summary>
    /// Scanner Preferences (AdminScannerPrefsScene): Flash, Auto Focus, Scan Line, Guide Frame and the photo quality. Every change is
    /// saved at once and used by the scanner the next time it opens. Back and Escape return to Settings.
    /// </summary>
    public class AdminScannerPrefsController : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private Toggle flashToggle;
        [SerializeField] private Toggle autoFocusToggle;
        [SerializeField] private Toggle scanLineToggle;
        [SerializeField] private Toggle guideFrameToggle;
        [SerializeField] private Toggle batchToggle;
        [SerializeField] private Button qualityButton;
        [SerializeField] private TextMeshProUGUI qualityValue;
        [SerializeField] private string settingsScene = "AdminSettingsScene";

        void Start()
        {
            Bind(flashToggle, ScannerPrefs.Flash, v => ScannerPrefs.Flash = v);
            Bind(autoFocusToggle, ScannerPrefs.AutoFocus, v => ScannerPrefs.AutoFocus = v);
            Bind(scanLineToggle, ScannerPrefs.ScanLine, v => ScannerPrefs.ScanLine = v);
            Bind(batchToggle, ScannerPrefs.BatchMode, v => ScannerPrefs.BatchMode = v);
            Bind(guideFrameToggle, ScannerPrefs.GuideFrame, v => ScannerPrefs.GuideFrame = v);
            if (backButton != null) backButton.onClick.AddListener(Back);
            if (qualityButton != null) qualityButton.onClick.AddListener(() => { ScannerPrefs.HighQuality = !ScannerPrefs.HighQuality; PaintQuality(); });
            PaintQuality();
        }

        void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Back(); }

        static void Bind(Toggle t, bool value, UnityEngine.Events.UnityAction<bool> set)
        {
            if (t == null) return;
            t.SetIsOnWithoutNotify(value);
            t.onValueChanged.AddListener(set);
        }

        void PaintQuality() { if (qualityValue != null) qualityValue.text = ScannerPrefs.HighQuality ? "High Quality" : "Standard"; }

        void Back() { SceneNavigator.Go(settingsScene); }
    }
}
