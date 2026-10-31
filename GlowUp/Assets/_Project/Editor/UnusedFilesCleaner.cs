using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// One clean-up, run once from the menu GlowUp > Remove Unused Files (Unity removes each file with its .meta, which is why this is a menu
    /// and not a delete in Explorer). It takes out what the app no longer uses:
    ///   - the Facebook SDK and everything that came with it. Facebook sign-in was removed on request: Google is the only social sign-in.
    ///     The SDK caused the "zlib1" errors, "You didn't specify a Facebook app ID", "Your Android setup is not correct" and the obsolete
    ///     warnings from its Examples folder. An account made with Facebook in the past stays recognised (SocialSignIn.cs).
    ///   - its lines in the Android manifest and the Gradle template, and the scripting symbol GLOWUP_FACEBOOK;
    ///   - scripts nothing refers to, and empty folders.
    /// It removes itself at the end. Everything it deletes is in Git.
    /// </summary>
    public static class UnusedFilesCleaner
    {
        const string Symbol = "GLOWUP_FACEBOOK";
        const string Manifest = "Assets/Plugins/Android/AndroidManifest.xml";
        const string GradleTemplate = "Assets/Plugins/Android/mainTemplate.gradle";

        static readonly string[] Unused =
        {
            "Assets/FacebookSDK",
            "Assets/IAPiOS",                                 // Facebook's in-app purchase files for iOS
            "Assets/Editor/FBXcodePostProcess.cs",
            "Assets/Editor/DisableBitcode.cs",
            "Assets/Scripts/IAPWrapper.cs",
            "Assets/Scripts/IAPAndroidImpl.cs",
            "Assets/_Project/Editor/FacebookSdkDefine.cs",
            "Assets/_Project/Editor/FacebookSignInSetup.cs",
            "Assets/_Project/Scripts/Auth/OnboardingData.cs",     // nothing refers to it
            "Assets/_Project/Scripts/Common/ProfanityFilter.cs",  // nothing refers to it
        };
        // removed only when nothing is left inside
        static readonly string[] FoldersIfEmpty = { "Assets/Editor", "Assets/Assets", "Assets/Images" };

        [MenuItem("GlowUp/Remove Unused Files")]
        public static void FromMenu()
        {
            if (!EditorUtility.DisplayDialog("Remove unused files", "Removes the Facebook SDK and the scripts nothing uses. Everything removed is in Git, so it can be brought back.\n\nUnity recompiles afterwards.", "Remove", "Cancel")) return;
            EditorUtility.DisplayDialog("Remove unused files", Run(), "OK");
        }

        /// <summary>Also for batch mode: -executeMethod GlowUp.EditorTools.UnusedFilesCleaner.RunFromCommandLine</summary>
        public static void RunFromCommandLine() => Debug.Log("[GlowUp] " + Run());

        static string Run()
        {
            RemoveSymbol();
            int manifestLines = DropLines(Manifest, line => line.Contains("com.facebook"));
            int gradleLines = DropLines(GradleTemplate, line => line.Contains("com.facebook") || line.Contains("Facebook"));

            var failed = new List<string>();
            string[] present = Unused.Where(path => AssetDatabase.IsValidFolder(path) || File.Exists(path)).ToArray();
            AssetDatabase.DeleteAssets(present, failed);
            foreach (string folder in FoldersIfEmpty)
                if (AssetDatabase.IsValidFolder(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.DeleteAsset("Assets/_Project/Editor/UnusedFilesCleaner.cs");
            AssetDatabase.Refresh();

            string text = (present.Length - failed.Count) + " items removed, " + manifestLines + " lines out of the Android manifest, " + gradleLines + " out of the Gradle template.";
            if (failed.Count > 0) text += "\n\nCould not remove (a file is in use: restart Unity and delete these in the Project window):\n" + string.Join("\n", failed);
            Debug.Log("[GlowUp] " + text);
            return text;
        }

        static void RemoveSymbol()
        {
            string[] symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android).Split(';');
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, string.Join(";", symbols.Where(s => s != Symbol)));
        }

        static int DropLines(string path, System.Func<string, bool> unwanted)
        {
            if (!File.Exists(path)) return 0;
            string[] lines = File.ReadAllLines(path);
            string[] kept = lines.Where(line => !unwanted(line)).ToArray();
            if (kept.Length != lines.Length) File.WriteAllLines(path, kept);
            return lines.Length - kept.Length;
        }
    }
}
