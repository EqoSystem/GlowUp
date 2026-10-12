using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Menu: GlowUp > Set Up Start-Up Logo And App Icon. Puts the Glow Up logo on the screen that shows
    /// while the app starts, and gives the app its own icon. It also runs by itself (when the Editor
    /// loads and before GlowUp > Build Android APK) and only changes what is not set yet.
    ///
    /// The pictures are in Assets/_Project/Branding; replace a file there (same name) to change the art:
    ///   GlowUpLogo.png             the logo of Unity's start-up screen (transparent)
    ///   GlowUpSplash.png           Android's own picture shown before that (logo on the app's pearl white)
    ///   GlowUpIcon.png             the app icon as one square picture
    ///   GlowUpIcon_Background.png, GlowUpIcon_Foreground.png   the two layers Android 8+ launchers use
    ///
    /// About "Made with Unity": Unity 2022 with a Personal licence always shows its own small logo on the
    /// start-up screen; only Unity Pro (or Unity 6) lets a project switch it off. This script switches it
    /// off when the licence allows it and otherwise shows it small, UNDER the Glow Up logo.
    /// The same settings are under Edit > Project Settings > Player (Icon, Splash Image).
    /// </summary>
    public static class BrandingSetup
    {
        const string Folder = "Assets/_Project/Branding/";
        /// <summary>The background of Unity's original start screen.</summary>
        static readonly Color UnityDark = new Color(0.1372549f, 0.1215686f, 0.1254902f, 1f);

        [MenuItem("GlowUp/Set Up Start-Up Logo And App Icon", false, 210)]
        static void RunFromMenu()
        {
            var notes = new List<string>();
            bool ready = Apply(notes);
            if (notes.Count == 0) notes.Add(ready ? "The start-up logo and the app icon were already set." : "Nothing could be set.");
            EditorUtility.DisplayDialog("GlowUp: start-up logo and icon", string.Join("\n", notes), "OK");
        }

        /// <summary>Sets whatever is not set yet. `notes` gets one line per change (none when nothing changed).</summary>
        public static bool Apply(List<string> notes)
        {
            notes = notes ?? new List<string>();
            var icon = Load<Texture2D>("GlowUpIcon.png", false);
            var iconBackground = Load<Texture2D>("GlowUpIcon_Background.png", false);
            var iconForeground = Load<Texture2D>("GlowUpIcon_Foreground.png", false);
            if (icon == null) notes.Add("- App icon: the pictures in " + Folder + " are missing.");

            // ---- Unity's start-up screen: Unity's OWN original one (its logo, its colours, its movement), nothing of the app on it.
            //      The app's own start-up is the animation of StartupScene, which follows it; a Glow Up logo here as well
            //      made two start-up logos in a row (the project owner's note of 12 October 2026).
            {
                bool changed = false;
                PlayerSettings.SplashScreenLogo[] logos = PlayerSettings.SplashScreen.logos;
                if (logos != null && logos.Length > 0) { PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0]; changed = true; }
                if (PlayerSettings.SplashScreen.backgroundColor != UnityDark) { PlayerSettings.SplashScreen.backgroundColor = UnityDark; changed = true; }
                if (PlayerSettings.SplashScreen.unityLogoStyle != PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark)
                { PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark; changed = true; }
                if (PlayerSettings.SplashScreen.drawMode != PlayerSettings.SplashScreen.DrawMode.AllSequential)
                { PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential; changed = true; }
                if (PlayerSettings.SplashScreen.animationMode != PlayerSettings.SplashScreen.AnimationMode.Dolly)
                { PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly; changed = true; }
                if (!PlayerSettings.SplashScreen.showUnityLogo) { PlayerSettings.SplashScreen.showUnityLogo = true; changed = true; }
                if (changed) notes.Add("- Start-up screen: Unity's own original one. The Glow Up animation (StartupScene) follows it.");

                // Android's own picture before that: none, so the Glow Up logo is not shown a second time
                Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (settings != null && settings.Length > 0 && settings[0] != null)
                {
                    var serialized = new SerializedObject(settings[0]);
                    SerializedProperty picture = serialized.FindProperty("androidSplashScreen");
                    if (picture != null && picture.objectReferenceValue != null)
                    {
                        picture.objectReferenceValue = null;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        notes.Add("- Android start picture: none (the Glow Up logo is shown by the app's own animation).");
                    }
                }
            }

            // ---- the app icon ----
            if (icon != null)
            {
                bool changed = false;
                Texture2D[] current = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown);
                if (current == null || current.Length == 0 || current[0] != icon)
                {
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
                    changed = true;
                }
#if UNITY_ANDROID
                changed |= SetAndroidIcons(UnityEditor.Android.AndroidPlatformIconKind.Legacy, icon);
                if (iconBackground != null && iconForeground != null)
                    changed |= SetAndroidIcons(UnityEditor.Android.AndroidPlatformIconKind.Adaptive, iconBackground, iconForeground);
#endif
                if (changed) notes.Add("- App icon: the Glow Up icon (Player > Icon).");
            }

            if (notes.Count > 0) AssetDatabase.SaveAssets();
            return true;
        }

#if UNITY_ANDROID
        static bool SetAndroidIcons(PlatformIconKind kind, params Texture2D[] layers)
        {
            PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
            if (icons == null || icons.Length == 0) return false;
            bool changed = false;
            foreach (PlatformIcon slot in icons)
            {
                bool same = slot.layerCount >= layers.Length;
                for (int i = 0; same && i < layers.Length; i++) same = slot.GetTexture(i) == layers[i];
                if (same) continue;
                slot.SetTextures(layers);
                changed = true;
            }
            if (changed) PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
            return changed;
        }
#endif

        /// <summary>Loads a picture from the Branding folder, imported sharp (no compression, no smaller copies).</summary>
        static T Load<T>(string file, bool asSprite) where T : Object
        {
            string path = Folder + file;
            if (!File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                TextureImporterType wanted = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                bool right = importer.textureType == wanted && importer.textureCompression == TextureImporterCompression.Uncompressed
                          && !importer.mipmapEnabled && importer.npotScale == TextureImporterNPOTScale.None && importer.alphaIsTransparency;
                if (!right)
                {
                    importer.textureType = wanted;
                    if (asSprite) importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
