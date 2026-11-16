using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Scan
{
    /// <summary>
    /// The phone's own text reader (Google ML Kit, no internet needed). It is the reader the scan screen
    /// uses on Android because it exists for 32-bit AND 64-bit phones. The Java half is
    /// Assets/Plugins/Android/GlowUpTextReader.java. Not available in the Editor: there the older
    /// Tesseract reader is used (see OcrService).
    /// </summary>
    public class MlKitTextReader
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpTextReader";
        AndroidJavaClass reader;

        /// <summary>True (and a reader) when the text recognition library is part of this build.</summary>
        public static bool TryCreate(out MlKitTextReader created)
        {
            created = null;
            try
            {
                var javaClass = new AndroidJavaClass(JavaClass);
                if (!javaClass.CallStatic<bool>("isAvailable")) { javaClass.Dispose(); return false; }
                created = new MlKitTextReader { reader = javaClass };
                return true;
            }
            catch (Exception e)
            {
                { }
                return false;
            }
        }

        /// <summary>
        /// Reads the text in an upright picture. `done(text, boxes, null)` or `done(null, null, problem)` is called
        /// when it has finished, NOT on Unity's own thread: it may only store the answer. `boxes` says where the
        /// lines of text are (see GlowUpTextReader.java).
        /// </summary>
        public void Read(Texture2D picture, Action<string, string, string> done)
        {
            try
            {
                byte[] jpg = picture.EncodeToJPG(95);
                var bytes = new sbyte[jpg.Length];
                Buffer.BlockCopy(jpg, 0, bytes, 0, jpg.Length);
                reader.CallStatic("read", bytes, new Answer(done));
            }
            catch (Exception e) { done(null, null, e.Message); }
        }

        class Answer : AndroidJavaProxy
        {
            readonly Action<string, string, string> done;
            public Answer(Action<string, string, string> done) : base(JavaClass + "$Callback") { this.done = done; }
            [Preserve] public void onResult(string text, string boxes) { done(text ?? "", boxes ?? "", null); }
            [Preserve] public void onError(string message) { done(null, null, string.IsNullOrEmpty(message) ? "unknown problem" : message); }
        }
#else
        public static bool TryCreate(out MlKitTextReader created) { created = null; return false; }
        public void Read(Texture2D picture, Action<string, string, string> done) { done(null, null, "Only available on an Android phone."); }
#endif
    }
}
