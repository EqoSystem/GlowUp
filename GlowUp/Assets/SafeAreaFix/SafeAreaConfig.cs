using UnityEngine;

namespace GlowUp.SafeAreaFix
{
    /// <summary>
    /// Which screen edges keep clear of the phone's cutout, status bar and gesture bar (12 November 2026). One asset for the whole app:
    /// Assets/SafeAreaFix/Resources/SafeAreaConfig.asset. Without the asset the defaults below are used. The debug overlay of a development
    /// build can change these at run time (only in memory: the asset is never changed by the app).
    /// </summary>
    [CreateAssetMenu(menuName = "GlowUp/Safe Area Config", fileName = "SafeAreaConfig")]
    public class SafeAreaConfig : ScriptableObject
    {
        [Tooltip("Keep content below the camera hole / status bar")]
        public bool applyTop = true;
        [Tooltip("Keep content above the gesture bar / navigation buttons")]
        public bool applyBottom = false;
        [Tooltip("Keep content clear of a cutout on the left (landscape)")]
        public bool applyLeft = false;
        [Tooltip("Keep content clear of a cutout on the right (landscape)")]
        public bool applyRight = false;
        [Tooltip("Development builds only: the on-screen safe-area overlay (values, cutouts, calibration line)")]
        public bool debugOverlay = true;
        [Tooltip("Pixels kept free at the top when the phone reports nothing at all (0 = off). The last resort of InsetProvider.")]
        [Min(0)] public int manualTopInsetPx = 0;

        static SafeAreaConfig current;

        /// <summary>The config in use (the asset in Resources, or the defaults).</summary>
        public static SafeAreaConfig Current
        {
            get
            {
                if (current != null) return current;
                SafeAreaConfig asset = Resources.Load<SafeAreaConfig>("SafeAreaConfig");
                current = asset != null ? Instantiate(asset) : CreateInstance<SafeAreaConfig>();    // a copy: run-time toggles never touch the asset
                current.hideFlags = HideFlags.DontSave;
                return current;
            }
        }

        /// <summary>Raised after a value was changed at run time (the debug overlay); every SafeAreaRoot applies it again.</summary>
        public static event System.Action Changed;

        public static void RaiseChanged() => Changed?.Invoke();

        /// <summary>Back to the rollback default: only the top edge.</summary>
        public void TopOnly() { applyTop = true; applyBottom = applyLeft = applyRight = false; RaiseChanged(); }
    }
}
