using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Scan
{
    /// <summary>
    /// The phone's own "what does this pack look like" model (MobileNet V3 small through Google's MediaPipe Image
    /// Embedder, no internet needed). It is given a picture of the product and answers with about a thousand numbers.
    /// Two pictures of the same pack give numbers that point the same way, also in other light, from another angle or
    /// distance; PackagingMemory compares them, so the app knows a pack again without asking the AI.
    ///
    /// The picture stays on the phone: it is not saved and not sent anywhere, and it cannot be rebuilt from the numbers.
    ///
    /// The Java half is Assets/Plugins/Android/GlowUpPackEmbedder.java; the library is the line
    /// "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle (the finger signs use it too)
    /// and the model is Assets/StreamingAssets/pack_embedder.tflite (4 MB). It needs Android 7.0 or newer. On an older
    /// phone, in a build without the model, and in the Editor, Begin() returns null and the app uses its simpler
    /// colour-and-pattern signature instead.
    /// </summary>
    public class PackEmbedder
    {
        public const string ModelFile = "pack_embedder.tflite";

        /// <summary>The share of the picture's width and height that is looked at, round its centre: the edges are mostly the hand and the background.</summary>
        public static float Middle = 0.8f;

        /// <summary>The answer for one picture: the numbers as Base64 text (one byte per number), or null when it could not be done. NOT called on Unity's own thread.</summary>
        public delegate void Answer(string numbers);

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpPackEmbedder";
        const int JavaReady = 2, JavaFailed = 3;        // GlowUpPackEmbedder.READY and FAILED

        AndroidJavaClass java;
        Callback callback;
        bool ready, failed;

        /// <summary>Starts loading the model (on its own thread) and returns at once. Null when the phone or the build cannot do it.</summary>
        public static PackEmbedder Begin()
        {
            AndroidJavaClass javaClass = null;
            try
            {
                javaClass = new AndroidJavaClass(JavaClass);
                if (!javaClass.CallStatic<bool>("isAvailable")) { javaClass.Dispose(); return null; }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    javaClass.CallStatic("start", activity, ModelFile);
                return new PackEmbedder { java = javaClass, callback = new Callback() };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Scan] The picture model is not part of this build: " + e.Message);
                if (javaClass != null) javaClass.Dispose();
                return null;
            }
        }

        /// <summary>True once the model is loaded. False while it loads, and for good when it could not be loaded.</summary>
        public bool Ready
        {
            get
            {
                if (ready || failed || java == null) return ready;
                try
                {
                    int state = java.CallStatic<int>("state");
                    if (state == JavaReady) ready = true;
                    else if (state == JavaFailed)
                    {
                        failed = true;
                        Debug.LogWarning("[Scan] The picture model could not start: " + java.CallStatic<string>("failure"));
                    }
                }
                catch (Exception e) { failed = true; Debug.LogWarning("[Scan] " + e.Message); }
                return ready;
            }
        }

        /// <summary>True while the picture before is still being looked at.</summary>
        public bool Busy
        {
            get { try { return java != null && java.CallStatic<bool>("isBusy"); } catch (Exception) { return true; } }
        }

        /// <summary>
        /// Looks at an UPRIGHT picture: `pixels` row by row from the TOP, each pixel as one ARGB number. `done` is called
        /// when it has finished, NOT on Unity's own thread: it may only store the answer. Returns false when the picture
        /// was not taken (not ready, still busy, or closed).
        /// </summary>
        public bool Embed(int[] pixels, int width, int height, Answer done)
        {
            if (java == null || !Ready || done == null) return false;
            try
            {
                callback.done = done;           // one picture at a time (the Java half refuses a second), so one callback is enough
                return java.CallStatic<bool>("embed", pixels, width, height, Mathf.Clamp(Middle, 0.3f, 1f), callback);
            }
            catch (Exception e) { Debug.LogWarning("[Scan] " + e.Message); return false; }
        }

        /// <summary>Lets go of the model (it leaves the phone's memory).</summary>
        public void Close()
        {
            if (java == null) return;
            try { java.CallStatic("stop"); } catch (Exception) { }
            java.Dispose();
            java = null; ready = false;
            if (callback != null) callback.done = null;
        }

        class Callback : AndroidJavaProxy
        {
            public volatile Answer done;
            public Callback() : base(JavaClass + "$Callback") { }
            [Preserve] public void onResult(string numbers) { done?.Invoke(string.IsNullOrEmpty(numbers) ? null : numbers); }
            [Preserve] public void onError(string message) { Debug.LogWarning("[Scan] The picture model: " + message); done?.Invoke(null); }
        }
#else
        public static PackEmbedder Begin() { return null; }
        public bool Ready => false;
        public bool Busy => false;
        public bool Embed(int[] pixels, int width, int height, Answer done) { return false; }
        public void Close() { }
#endif
    }
}
