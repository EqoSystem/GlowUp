using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Creates the TextMeshPro font assets from the TTF files in Assets/_Project/Fonts and returns them.
    /// Fonts used (all SIL Open Font License, from Google Fonts):
    ///   DM Serif Display - the Glow Up logo / titles   |  Poppins SemiBold - headings and buttons
    ///   Poppins Medium - field labels                  |  Poppins Regular - body and helper text
    ///   Lato Regular - text typed into inputs
    /// To use a different font: drop a .ttf in that folder, make a TMP asset (Window > TextMeshPro > Font Asset Creator)
    /// and assign it on the ThemeProfile asset - nothing else needs to change.
    /// </summary>
    public static class FontFactory
    {
        const string Folder = "Assets/_Project/Fonts/";
        const string TmpFolder = "Assets/_Project/Fonts/TMP/";

        public static TMP_FontAsset Title => Get("DMSerifDisplay-Regular");
        public static TMP_FontAsset HeadingAndButton => Get("Poppins-SemiBold");
        public static TMP_FontAsset Label => Get("Poppins-Medium");
        public static TMP_FontAsset Body => Get("Poppins-Regular");
        public static TMP_FontAsset Input => Get("Lato-Regular");

        static TMP_FontAsset Get(string ttfName)
        {
            string assetPath = TmpFolder + ttfName + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(Folder + ttfName + ".ttf");
            if (font == null) { AssetDatabase.Refresh(); font = AssetDatabase.LoadAssetAtPath<Font>(Folder + ttfName + ".ttf"); }
            if (font == null) { Debug.LogWarning("[GlowUp] Font file missing: " + Folder + ttfName + ".ttf"); return null; }

            Directory.CreateDirectory(TmpFolder);
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa == null) { Debug.LogWarning("[GlowUp] Could not create TMP font asset for " + ttfName); return null; }
            fa.name = ttfName + " SDF";
            AssetDatabase.CreateAsset(fa, assetPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            { fa.atlasTextures[0].name = ttfName + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa); }
            if (fa.material != null) { fa.material.name = ttfName + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            // pre-build the glyphs the UI uses so they exist even before first use
            fa.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?@#$%&*()-_+=/\\'\"<>[]{}|~^`", out string missing);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>Fills any empty font slot on the profile with the default font for that role.</summary>
        public static void FillProfile(GlowUp.Theme.ThemeProfile p)
        {
            if (p.titleFont == null) p.titleFont = Title;
            if (p.headingFont == null) p.headingFont = HeadingAndButton;
            if (p.buttonFont == null) p.buttonFont = HeadingAndButton;
            if (p.labelFont == null) p.labelFont = Label;
            if (p.bodyFont == null) p.bodyFont = Body;
            if (p.inputFont == null) p.inputFont = Input;
        }
    }
}
