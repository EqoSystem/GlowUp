using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Common
{
    /// <summary>
    /// The phone's own hand finder (Google MediaPipe Hand Landmarker, no internet needed). It is given a small picture
    /// from the selfie camera and answers with the 21 points of the hand it sees; FingerCounter counts the raised
    /// fingers from those points. The picture stays on the phone: it is not saved and not sent anywhere.
    ///
    /// The Java half is Assets/Plugins/Android/GlowUpHandReader.java; the library is the line
    /// "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle and the model is
    /// Assets/StreamingAssets/hand_landmarker.task. It needs Android 7.0 or newer. Not available in the Editor.
    ///
    /// Loading the model takes a moment, so Begin() only STARTS it (on the reader's own thread): ask State until it
    /// says Ready or Failed.
    /// </summary>
    public class HandLandmarkReader
    {
        public const string ModelFile = "hand_landmarker.task";

        public enum Status { Starting, Ready, Failed }

        /// <summary>The answer for one picture: 63 numbers (x, y, z of the 21 points; x and z as shares of the picture's width, y of its height), or null when no hand was seen.</summary>
        public delegate void Answer(float[] points);

        /// <summary>Why the hand finder could not start, in words the Settings page can show ("" while there is no problem).</summary>
        public string Problem { get; private set; } = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpHandReader";
        const int JavaReady = 2, JavaFailed = 3;        // GlowUpHandReader.READY and FAILED

        AndroidJavaClass reader;
        Callback callback;
        Status status = Status.Starting;

        /// <summary>
        /// Starts the hand finder and returns a reader whose State says how far it is. Returns null when it cannot even
        /// be started (an old Android, or a build without the library): `problem` then says why.
        /// </summary>
        public static HandLandmarkReader Begin(out string problem)
        {
            problem = "";
            AndroidJavaClass javaClass = null;
            try
            {
                javaClass = new AndroidJavaClass(JavaClass);
                if (!javaClass.CallStatic<bool>("isAvailable"))
                {
                    problem = "Finger signs need Android 7 or newer, and the hand-detection library in the app.";
                    javaClass.Dispose();
                    return null;
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    javaClass.CallStatic("start", activity, ModelFile);
                var made = new HandLandmarkReader { reader = javaClass };
                made.callback = new Callback();
                return made;
            }
            catch (Exception e)
            {
                problem = "Hand detection is not part of this build of the app.";
                { }
                if (javaClass != null) javaClass.Dispose();
                return null;
            }
        }

        /// <summary>Starting (the model is still being loaded), Ready, or Failed (Problem says why).</summary>
        public Status State
        {
            get
            {
                if (status != Status.Starting || reader == null) return status;
                try
                {
                    int state = reader.CallStatic<int>("state");
                    if (state == JavaReady) status = Status.Ready;
                    else if (state == JavaFailed)
                    {
                        Problem = "Hand detection could not start on this phone: " + reader.CallStatic<string>("failure");
                        status = Status.Failed;
                    }
                }
                catch (Exception e)
                {
                    Problem = "Hand detection could not start on this phone.";
                    { }
                    status = Status.Failed;
                }
                return status;
            }
        }

        /// <summary>True while the picture before is still being looked at.</summary>
        public bool Busy
        {
            get { try { return reader != null && reader.CallStatic<bool>("isBusy"); } catch (Exception) { return true; } }
        }

        /// <summary>
        /// Looks for a hand in an UPRIGHT picture: `pixels` row by row from the TOP, each pixel as one ARGB number.
        /// `done` is called when it has finished, NOT on Unity's own thread: it may only store the answer.
        /// Returns false when the picture was not taken (not ready, still busy, or the reader was closed).
        /// </summary>
        public bool Read(int[] pixels, int width, int height, Answer done)
        {
            if (reader == null || status != Status.Ready) return false;
            try
            {
                callback.done = done;           // one picture at a time (the Java half refuses a second), so one callback is enough
                return reader.CallStatic<bool>("read", pixels, width, height, 0, callback);
            }
            catch (Exception e) { { } return false; }
        }

        /// <summary>Lets go of the hand finder (the model leaves the phone's memory).</summary>
        public void Close()
        {
            if (reader == null) return;
            try { reader.CallStatic("stop"); } catch (Exception) { }
            reader.Dispose();
            reader = null;
            if (callback != null) callback.done = null;
        }

        class Callback : AndroidJavaProxy
        {
            public volatile Answer done;
            public Callback() : base(JavaClass + "$Callback") { }
            [Preserve] public void onResult(string points, string worldPoints) { done?.Invoke(Parse(points)); }
            [Preserve] public void onError(string message) { done?.Invoke(null); }
        }
#else
        public static HandLandmarkReader Begin(out string problem)
        {
            problem = "Finger signs only work on an Android phone.";
            return null;
        }
        public Status State => Status.Failed;
        public bool Busy => false;
        public bool Read(int[] pixels, int width, int height, Answer done) { return false; }
        public void Close() { }
#endif

        /// <summary>63 numbers separated by spaces, as the Java half writes them. Null when the text does not hold 63 numbers.</summary>
        public static float[] Parse(string numbers)
        {
            if (string.IsNullOrWhiteSpace(numbers)) return null;
            string[] parts = numbers.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 63) return null;
            var points = new float[63];
            for (int i = 0; i < 63; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out points[i])) return null;
            return points;
        }
    }
}
