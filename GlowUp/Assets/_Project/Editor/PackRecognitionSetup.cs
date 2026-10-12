using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// What an Android build needs so the app KNOWS A PACK AGAIN by its look (the scan screen's "Is this ...?").
    /// It is checked each time the Editor loads and just before an Android build (GlowUpProjectSetup calls it):
    ///
    ///  1. the picture model, Assets/StreamingAssets/pack_embedder.tflite (Google MediaPipe "MobileNet V3 small" image
    ///     embedder, 4 MB). When it is missing the app still builds and works: it then compares packs by its simpler
    ///     colour-and-pattern signature, which finds a pack again far less often.
    ///  2. the library line "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle: the same
    ///     line the finger signs need (ArFaceTrackingSetup.FixGradleTemplate keeps it).
    ///
    /// Menu: GlowUp > Set Up Pack Recognition runs the same check and says what it found.
    /// </summary>
    public static class PackRecognitionSetup
    {
        public const string ModelPath = "Assets/StreamingAssets/pack_embedder.tflite";
        public const string ModelAddress = "https://storage.googleapis.com/mediapipe-models/image_embedder/mobilenet_v3_small/float32/1/mobilenet_v3_small.tflite";

        [MenuItem("GlowUp/Set Up Pack Recognition", false, 213)]
        static void RunFromMenu()
        {
            var notes = new List<string>();
            bool ready = Apply(notes);
            if (notes.Count == 0) notes.Add("Everything the app needs to know a pack again is in place.");
            EditorUtility.DisplayDialog("GlowUp: pack recognition", (ready ? "" : "Not everything is in place yet.\n\n") + string.Join("\n", notes), "OK");
        }

        /// <summary>`notes` gets one line per problem (none when everything is right). Returns true when the model is there.</summary>
        public static bool Apply(List<string> notes)
        {
            notes = notes ?? new List<string>();
            if (File.Exists(ModelPath)) return true;
            notes.Add("- Pack recognition: the picture model is missing. Download " + ModelAddress + " and save it as " + ModelPath +
                      ". Without it the app still builds, but it finds a pack it has scanned before far less often.");
            return false;
        }
    }
}
