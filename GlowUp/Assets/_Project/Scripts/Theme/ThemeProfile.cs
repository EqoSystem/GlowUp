using TMPro;
using UnityEngine;

namespace GlowUp.Theme
{
    /// <summary>
    /// All light/dark theme colours and fonts in one asset (Assets/_Project/ThemeProfile.asset).
    /// Edit a colour or font here and every themed object in every scene follows.
    ///
    /// Look: white dominates, pink is for actions and important selections, gold is kept for
    /// premium details (the logo, thin borders, small highlights).
    ///   Pearl White #FAF7F2, Champagne Gold #C6A15B, Dusty Rose #D98EA0, Rose Mist #EAD9DC, Rose Cocoa #382C30.
    ///
    /// Readability (checked against WCAG contrast, 4.5 : 1 for small text):
    ///   - White text on the light-mode pink button is only 3.3 : 1, so button labels are near-black (5.2 : 1).
    ///   - Gold is 3.0 : 1 on the light background: use the Gold role for the logo, borders and icons,
    ///     never for small text.
    ///   - Pink as small text on a pale background must use the Accent Text role (5.0 : 1), not Accent.
    /// </summary>
    [CreateAssetMenu(menuName = "GlowUp/Theme Profile", fileName = "ThemeProfile")]
    public class ThemeProfile : ScriptableObject
    {
        /// <summary>Bump when the default palette changes so the scene builders re-apply it once.</summary>
        public const int CurrentPaletteVersion = 7;   // 6: Information, Border and Disabled tokens (16 November 2026); 7: New21 AA on Rose Mist (17 November 2026)
        [HideInInspector] public int paletteVersion;

        [System.Serializable]
        public class Palette
        {
            [Tooltip("Main canvas / page background")] public Color background = Color.white;
            [Tooltip("Cards: the main panel that sits on the background")] public Color surface = Color.white;
            [Tooltip("Soft sections: scroll-view backdrops, list containers, tile frames (Rose Mist)")] public Color surfaceAlt = Color.gray;
            [Tooltip("Main titles and major headings")] public Color heading = Color.black;
            [Tooltip("Body copy and field labels")] public Color mainText = Color.black;
            [Tooltip("Subtitles, hints and secondary labels")] public Color secondaryText = Color.gray;
            [Tooltip("Primary button fill (pink: actions)")] public Color buttonFill = Color.black;
            [Tooltip("Primary button label / any text placed on the pink button colour")] public Color buttonText = Color.white;
            [Tooltip("Input field and list-row background")] public Color inputBackground = Color.white;
            [Tooltip("Text typed into input fields")] public Color inputText = Color.black;
            [Tooltip("Placeholder text and idle (unfocused) borders")] public Color inputPlaceholder = Color.gray;
            [Tooltip("Pink for shapes: glow, interactive borders, selected states. Not for small text on pale backgrounds.")] public Color accent = Color.magenta;
            [Tooltip("Success (requirement met, safe)")] public Color success = Color.green;
            [Tooltip("Caution (use with care)")] public Color caution = Color.yellow;
            [Tooltip("Warning (harmful, error)")] public Color warning = Color.red;
            [Tooltip("Gold highlights: the logo, thin borders, small icons. Premium details only; not for small text.")]
            public Color gold = new Color32(184, 138, 59, 255);
            [Tooltip("Pink that stays readable as small text (links, selected labels)")]
            public Color accentText = new Color32(176, 74, 105, 255);
            // colour guideline §5 and §11 (16 November 2026): Information, Border and Disabled tokens, light and dark
            [Tooltip("Information (helpful notices, Info messages): blue, readable as text")]
            public Color info = new Color32(58, 106, 150, 255);
            [Tooltip("Thin borders and dividers between neutral surfaces")]
            public Color border = new Color32(205, 187, 184, 255);
            [Tooltip("Disabled controls and their labels (muted, still distinguishable)")]
            public Color disabled = new Color32(163, 149, 154, 255);
        }

        [Header("Light Mode - white dominates, pink for actions, gold for details")]
        public Palette light = LightDefault();

        [Header("Dark Mode - Rose Cocoa base")]
        public Palette dark = DarkDefault();

        [Header("Fonts (TextMeshPro font assets)")]
        [Tooltip("The Glow Up logo / big titles")] public TMP_FontAsset titleFont;
        [Tooltip("Section headings and sub titles")] public TMP_FontAsset headingFont;
        [Tooltip("Field labels")] public TMP_FontAsset labelFont;
        [Tooltip("Body copy, helper text, descriptions")] public TMP_FontAsset bodyFont;
        [Tooltip("Text typed into input fields")] public TMP_FontAsset inputFont;
        [Tooltip("Button captions")] public TMP_FontAsset buttonFont;

        public Palette Get(bool isDark) => isDark ? dark : light;

        public TMP_FontAsset GetFont(FontRole role)
        {
            switch (role)
            {
                case FontRole.Title: return titleFont;
                case FontRole.Heading: return headingFont;
                case FontRole.Label: return labelFont;
                case FontRole.Input: return inputFont;
                case FontRole.Button: return buttonFont;
                default: return bodyFont;
            }
        }

        public void ResetPalettes() { light = LightDefault(); dark = DarkDefault(); paletteVersion = CurrentPaletteVersion; }

        public static Palette LightDefault() => new Palette
        {
            background = Hex("#FFFAF6"),       // background
            surface = Hex("#F6EDE7"),          // cards
            surfaceAlt = Hex("#EAD9DC"),       // Rose Mist: soft sections
            heading = Hex("#382C30"),          // Rose Cocoa
            mainText = Hex("#3A2F2E"),         // main text
            secondaryText = Hex("#6B5759"),    // 6.5 : 1 on the background, 4.9 : 1 on Rose Mist
            buttonFill = Hex("#D86C8A"),       // primary buttons
            buttonText = Hex("#241A1C"),       // near-black on pink: 5.2 : 1 (white would be 3.3 : 1)
            inputBackground = Hex("#FAF7F2"),  // Pearl White
            inputText = Hex("#3A2F2E"),
            inputPlaceholder = Hex("#6A5C5C"), // New21: 4.6 : 1 on every light surface, Rose Mist included
            accent = Hex("#D98EA0"),           // Dusty Rose: borders, glow, selected states
            success = Hex("#47673A"),          // sage (New21): 4.6 : 1 or more on every light surface
            caution = Hex("#805610"),          // amber (New21): 4.6 : 1 or more on every light surface
            warning = Hex("#A4402A"),          // brick (New21): 4.6 : 1 or more on every light surface
            gold = Hex("#B88A3B"),             // gold highlights
            accentText = Hex("#9C425D"),       // deep rose (New21): 4.6 : 1 or more on every light surface
            info = Hex("#35628A"),             // information blue (New21): 4.6 : 1 or more on every light surface
            border = Hex("#CDBBB8"),           // warm gray dividers
            disabled = Hex("#A3959A")          // muted warm gray (disabled controls are exempt from the text ratio)
        };

        public static Palette DarkDefault() => new Palette
        {
            background = Hex("#211D20"),       // background
            surface = Hex("#30282B"),          // cards
            surfaceAlt = Hex("#382C30"),       // Rose Cocoa
            heading = Hex("#F7EFE8"),
            mainText = Hex("#F7EFE8"),         // main text
            secondaryText = Hex("#CDBDC0"),
            buttonFill = Hex("#D98EA0"),       // primary buttons
            buttonText = Hex("#211D20"),       // 6.6 : 1 on the pink
            inputBackground = Hex("#3F3337"),
            inputText = Hex("#F7EFE8"),
            inputPlaceholder = Hex("#B3A3A6"),
            accent = Hex("#D98EA0"),
            success = Hex("#8DB56B"),
            caution = Hex("#E0A94F"),
            warning = Hex("#E78675"),          // New21: 4.6 : 1 on input fields too
            gold = Hex("#D4AF70"),             // gold highlights
            accentText = Hex("#E3A1B1"),
            info = Hex("#8DB7E0"),             // information blue: 7.9 : 1 on the background, 6.8 : 1 on cards
            border = Hex("#55474B"),
            disabled = Hex("#7A6C70")
        };

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }

    public enum FontRole { Title, Heading, Label, Body, Input, Button }
}
