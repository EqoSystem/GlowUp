using System;
using UnityEngine;

namespace GlowUp.Theme
{
    /// <summary>
    /// Holds the current light/dark mode. Persisted in PlayerPrefs ("GlowUp.DarkMode").
    /// Lives in each scene (or DontDestroyOnLoad). Assign the ThemeProfile in the Inspector.
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        public const string PrefKey = "GlowUp.DarkMode";

        public static ThemeManager Instance { get; private set; }
        public static event Action<bool> ThemeChanged;

        [SerializeField] private ThemeProfile profile;
        [Tooltip("Mode used the very first time the app opens")]
        [SerializeField] private bool defaultDarkMode = false;
        [SerializeField] private bool persistAcrossScenes = true;

        public ThemeProfile Profile => profile;
        public bool IsDark { get; private set; }
        /// <summary>A screen group may re-colour the LIGHT palette (the staff scanner uses the pink look of its mock-up). Null = the app's own palette.
        /// Dark mode is never re-coloured. Set and cleared by StaffSkin; the customer screens never set it.</summary>
        public static Func<ThemeProfile.Palette, ThemeProfile.Palette> Skin;
        public static void SkinChanged() { if (Instance != null) ThemeChanged?.Invoke(Instance.IsDark); }

        public ThemeProfile.Palette Palette
        {
            get
            {
                ThemeProfile.Palette p = profile != null ? profile.Get(IsDark) : new ThemeProfile.Palette();
                return Skin != null && !IsDark ? Skin(p) : p;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (persistAcrossScenes) { transform.SetParent(null); DontDestroyOnLoad(gameObject); }
            IsDark = PlayerPrefs.GetInt(PrefKey, defaultDarkMode ? 1 : 0) == 1;
        }

        void Start() => ThemeChanged?.Invoke(IsDark);

        public void Toggle() => SetDark(!IsDark);

        public void SetDark(bool dark)
        {
            IsDark = dark;
            PlayerPrefs.SetInt(PrefKey, dark ? 1 : 0);
            PlayerPrefs.Save();
            ThemeChanged?.Invoke(dark);
        }
    }
}
