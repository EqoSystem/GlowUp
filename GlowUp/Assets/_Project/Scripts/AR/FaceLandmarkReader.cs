using System;
using UnityEngine;

namespace GlowUp.AR
{
    /// <summary>
    /// The phone's own face finder (Google MediaPipe Face Landmarker, no internet needed). It is given a small upright picture of the
    /// camera view and answers with the 468 points of the standard face mesh (the numbering of FaceMeshData). The guided AR Try-On
    /// draws its make-up on those points, so the colour follows the real face on every phone, also without ARCore.
    /// The picture stays on the phone: it is not saved and not sent anywhere.
    ///
    /// The Java half is Assets/Plugins/Android/GlowUpFaceReader.java; the library is the line "com.google.mediapipe:tasks-vision" in
    /// Assets/Plugins/Android/mainTemplate.gradle (the finger signs use it too) and the model is Assets/StreamingAssets/face_landmarker.task.
    /// It needs Android 7.0 or newer. Not available in the Editor (there the old guided preview is used).
    ///
    /// Loading the model takes a moment, so Begin() only STARTS it: ask State until it says Ready or Failed. Look() hands over a picture
    /// and returns at once; TryTake() gives the answer once it is there (asked once per frame).
    /// </summary>
    public class FaceLandmarkReader
    {
        public const string ModelFile = "face_landmarker.task";
        public const int Points = FaceMeshData.PointCount;

        public enum Status { Starting, Ready, Failed }

        /// <summary>Why the face finder could not start, in words that can be shown ("" while there is no problem).</summary>
        public string Problem { get; private set; } = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpFaceReader";
        const int JavaReady = 2, JavaFailed = 3;        // GlowUpFaceReader.READY and FAILED

        AndroidJavaClass reader;
        Status status = Status.Starting;

        /// <summary>
        /// Starts the face finder and returns a reader whose State says how far it is. Returns null when it cannot even be started
        /// (an old Android, or a build without the library): `problem` then says why.
        /// </summary>
        public static FaceLandmarkReader Begin(out string problem)
        {
            problem = "";
            AndroidJavaClass javaClass = null;
            try
            {
                javaClass = new AndroidJavaClass(JavaClass);
                if (!javaClass.CallStatic<bool>("isAvailable"))
                {
                    problem = "Following the face needs Android 7 or newer.";
                    javaClass.Dispose();
                    return null;
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    javaClass.CallStatic("start", activity, ModelFile);
                return new FaceLandmarkReader { reader = javaClass };
            }
            catch (Exception e)
            {
                problem = "Face finding is not part of this build of the app.";
                Debug.LogWarning("[GuidedFace] " + e.Message);
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
                        Problem = "Face finding could not start on this phone: " + reader.CallStatic<string>("failure");
                        status = Status.Failed;
                    }
                }
                catch (Exception e)
                {
                    Problem = "Face finding could not start on this phone.";
                    Debug.LogWarning("[GuidedFace] " + e.Message);
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

        /// <summary>How long the last picture took, in milliseconds (0 before the first). Used to slow down or speed up.</summary>
        public int LastMilliseconds
        {
            get { try { return reader != null ? reader.CallStatic<int>("lastMillis") : 0; } catch (Exception) { return 0; } }
        }

        /// <summary>
        /// Hands over an UPRIGHT picture: `pixels` row by row from the TOP, each pixel one ARGB number (CameraFeedController.UprightArgb).
        /// Returns false when it was not taken (not ready, still busy, or closed). The answer comes through TryTake.
        /// </summary>
        public bool Look(int[] pixels, int width, int height)
        {
            if (reader == null || status != Status.Ready) return false;
            try { return reader.CallStatic<bool>("look", pixels, width, height); }
            catch (Exception e) { Debug.LogWarning("[GuidedFace] " + e.Message); return false; }
        }

        /// <summary>
        /// True when a new answer has come since the last call. `points` then holds 468 x 3 numbers (x and y as shares of the picture from its
        /// TOP LEFT, z), or is EMPTY when the picture showed no face.
        /// </summary>
        public bool TryTake(out float[] points)
        {
            points = null;
            if (reader == null || status != Status.Ready) return false;
            try
            {
                if (!reader.CallStatic<bool>("hasNew")) return false;
                points = reader.CallStatic<float[]>("take") ?? new float[0];
            }
            catch (Exception e) { Debug.LogWarning("[GuidedFace] " + e.Message); return false; }
            return true;
        }

        /// <summary>Lets go of the face finder (the model leaves the phone's memory).</summary>
        public void Close()
        {
            if (reader == null) return;
            try { reader.CallStatic("stop"); } catch (Exception) { }
            reader.Dispose();
            reader = null;
        }
#else
        public static FaceLandmarkReader Begin(out string problem)
        {
            problem = "Following the face only works on an Android phone.";
            return null;
        }
        public Status State => Status.Failed;
        public bool Busy => false;
        public int LastMilliseconds => 0;
        public bool Look(int[] pixels, int width, int height) { return false; }
        public bool TryTake(out float[] points) { points = null; return false; }
        public void Close() { }
#endif
    }
}
