using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// What an Android build needs for FINGER SIGNS (hold 1 to 5 fingers up to the selfie camera to open a screen).
    /// It is checked each time the Editor loads and just before an Android build (GlowUpProjectSetup calls it), and it
    /// only changes what is not right yet:
    ///
    ///  1. the hand model, Assets/StreamingAssets/hand_landmarker.task (Google MediaPipe, 7.8 MB). When it is missing
    ///     the app still builds; finger signs then say so on the Settings page and stay off.
    ///  2. the library line in Assets/Plugins/Android/mainTemplate.gradle (ArFaceTrackingSetup.FixGradleTemplate keeps it).
    ///  3. Assets/Plugins/Android/LauncherManifest.xml with one line that lets the app keep installing on Android 6.0:
    ///     the hand library itself asks for Android 7.0, and without that line the build stops with
    ///     "minSdkVersion 23 cannot be smaller than version 24 declared in library". On an Android 6 phone the app
    ///     checks the Android version before it touches the library, so finger signs simply stay off there.
    ///
    /// Menu: GlowUp > Set Up Finger Signs runs the same check and says what it found.
    /// </summary>
    public static class FingerSignSetup
    {
        public const string ModelPath = "Assets/StreamingAssets/hand_landmarker.task";
        public const string ModelAddress = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task";
        const string LauncherManifestPath = "Assets/Plugins/Android/LauncherManifest.xml";
        const string Libraries = "com.google.mediapipe.tasks.vision,com.google.mediapipe.tasks.core";

        [MenuItem("GlowUp/Set Up Finger Signs", false, 212)]
        static void RunFromMenu()
        {
            var notes = new List<string>();
            bool ready = Apply(notes);
            if (notes.Count == 0) notes.Add("Everything finger signs need for an Android build is in place.");
            EditorUtility.DisplayDialog("GlowUp: finger signs", (ready ? "" : "Not everything is in place yet.\n\n") + string.Join("\n", notes), "OK");
        }

        /// <summary>Puts right what is not. `notes` gets one line per change or problem (none when everything was right).</summary>
        public static bool Apply(List<string> notes)
        {
            notes = notes ?? new List<string>();
            bool ready = true;

            if (!File.Exists(ModelPath))
            {
                ready = false;
                notes.Add("- Finger signs: the hand model is missing. Download " + ModelAddress + " and save it as " + ModelPath +
                          ". Without it the app still builds, but finger signs stay off.");
            }
            ArFaceTrackingSetup.FixGradleTemplate(notes);
            ready &= EnsureLauncherManifest(notes);
            return ready;
        }

        /// <summary>The launcher manifest must tell the build that the hand library's "Android 7.0" is looked after by the app itself.</summary>
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
                    "    <!-- GlowUp: the hand finder of the finger signs (Google MediaPipe) asks for Android 7.0. This line lets the app keep\n" +
                    "         installing on Android 6.0; there the app checks the Android version first and finger signs simply stay off. -->\n" +
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
                notes.Add("- Finger signs: " + LauncherManifestPath + " was made (it lets the app keep installing on Android 6.0 although the hand library asks for Android 7.0).");
                return true;
            }

            string text = File.ReadAllText(LauncherManifestPath);
            if (text.Contains("com.google.mediapipe.tasks.vision")) return true;
            int at = text.IndexOf("<supports-screens", System.StringComparison.Ordinal);
            if (at < 0) at = text.IndexOf("<application", System.StringComparison.Ordinal);
            if (at < 0 || !text.Contains("xmlns:tools"))
            {
                notes.Add("- Finger signs: add this line inside <manifest> of " + LauncherManifestPath + " (and xmlns:tools=\"http://schemas.android.com/tools\" on <manifest>): " + line.Trim());
                return false;
            }
            File.WriteAllText(LauncherManifestPath, text.Insert(at, line.TrimStart() + "\n    "));
            AssetDatabase.ImportAsset(LauncherManifestPath);
            notes.Add("- Finger signs: the line for the hand library was added to " + LauncherManifestPath + ".");
            return true;
        }
    }
}
