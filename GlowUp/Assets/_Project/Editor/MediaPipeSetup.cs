using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// What an Android build needs for the face finder of the guided AR Try-On (Google MediaPipe Face Landmarker). It is checked each time
    /// the Editor loads and just before an Android build (GlowUpProjectSetup calls it), and it only changes what is not right yet:
    ///
    ///  1. the library line in Assets/Plugins/Android/mainTemplate.gradle (ArFaceTrackingSetup.FixGradleTemplate keeps it).
    ///  2. Assets/Plugins/Android/LauncherManifest.xml with one line that lets the app keep installing on Android 6.0:
    ///     the MediaPipe library itself asks for Android 7.0, and without that line the build stops with
    ///     "minSdkVersion 23 cannot be smaller than version 24 declared in library". On an Android 6 phone the app
    ///     checks the Android version before it touches the library, so the guided face finder simply stays off there.
    /// </summary>
    public static class MediaPipeSetup
    {
        const string LauncherManifestPath = "Assets/Plugins/Android/LauncherManifest.xml";
        const string Libraries = "com.google.mediapipe.tasks.vision,com.google.mediapipe.tasks.core";

        // Finger signs (brought back 19 November 2026) use the same library with a hand model (Google MediaPipe Hand Landmarker, 7.8 MB).
        public const string HandModelPath = "Assets/StreamingAssets/hand_landmarker.task";
        public const string HandModelAddress = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task";

        /// <summary>Puts right what is not. `notes` gets one line per change or problem (none when everything was right).</summary>
        public static bool Apply(List<string> notes)
        {
            notes = notes ?? new List<string>();
            bool ready = true;
            if (false)      // finger signs were removed on 28 November 2026: the hand model is no longer needed
            {
                ready = false;
                notes.Add("- Finger signs: the hand model is missing. Download " + HandModelAddress + " and save it as " + HandModelPath +
                          ". Without it the app still builds, but finger signs stay off and the Settings page says why.");
            }
            ArFaceTrackingSetup.FixGradleTemplate(notes);
            return EnsureLauncherManifest(notes) && ready;
        }

        /// <summary>The launcher manifest must tell the build that the library's "Android 7.0" is looked after by the app itself.</summary>
        static bool EnsureLauncherManifest(List<string> notes)
        {
            const string line = "    <uses-sdk tools:overrideLibrary=\"" + Libraries + "\" />";
            if (!File.Exists(LauncherManifestPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LauncherManifestPath));
                File.WriteAllText(LauncherManifestPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest\n" +
                    "    xmlns:android=\"http://schemas.android.com/apk/res/android\"\n" +
                    "    xmlns:tools=\"http://schemas.android.com/tools\"\n" +
                    "    android:installLocation=\"preferExternal\">\n" +
                    "    <!-- GlowUp: the face finder (Google MediaPipe) asks for Android 7.0. This line lets the app keep\n" +
                    "         installing on Android 6.0; there the app checks the Android version first and the guided face finder simply stays off. -->\n" +
                    line + "\n" +
                    "    <supports-screens\n" +
                    "        android:smallScreens=\"true\"\n" +
                    "        android:normalScreens=\"true\"\n" +
                    "        android:largeScreens=\"true\"\n" +
                    "        android:xlargeScreens=\"true\"\n" +
                    "        android:anyDensity=\"true\"/>\n\n" +
                    "    <application android:label=\"@string/app_name\"\n" +
                    "                 android:icon=\"@mipmap/app_icon\"/>\n" +
                    "</manifest>\n");
                AssetDatabase.ImportAsset(LauncherManifestPath);
                notes.Add("- MediaPipe: " + LauncherManifestPath + " was made (it lets the app keep installing on Android 6.0 although the library asks for Android 7.0).");
                return true;
            }

            string text = File.ReadAllText(LauncherManifestPath);
            if (text.Contains("com.google.mediapipe.tasks.vision")) return true;
            int at = text.IndexOf("<supports-screens", System.StringComparison.Ordinal);
            if (at < 0) at = text.IndexOf("<application", System.StringComparison.Ordinal);
            if (at < 0 || !text.Contains("xmlns:tools"))
            {
                notes.Add("- MediaPipe: add this line inside <manifest> of " + LauncherManifestPath + " (and xmlns:tools=\"http://schemas.android.com/tools\" on <manifest>): " + line.Trim());
                return false;
            }
            File.WriteAllText(LauncherManifestPath, text.Insert(at, line.TrimStart() + "\n    "));
            AssetDatabase.ImportAsset(LauncherManifestPath);
            notes.Add("- MediaPipe: the line for the library was added to " + LauncherManifestPath + ".");
            return true;
        }
    }
}
