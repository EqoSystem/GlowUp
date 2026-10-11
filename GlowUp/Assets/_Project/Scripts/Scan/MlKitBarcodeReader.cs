using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Scan
{
    /// <summary>
    /// The phone's own barcode reader (Google ML Kit, no internet needed). The scan screen uses the number
    /// under a product's barcode to find that product in the inventory at once. The Java half is
    /// Assets/Plugins/Android/GlowUpBarcodeReader.java. Not available in the Editor.
    /// </summary>
    public class MlKitBarcodeReader
    {
        /// <summary>The digits of a barcode, without spaces and without the zeros in front ("" = not a barcode). UPC and EAN numbers of one product compare equal this way.</summary>
        public static string Key(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            var digits = new System.Text.StringBuilder();
            foreach (char c in code) if (c >= '0' && c <= '9') digits.Append(c);
            string key = digits.ToString().TrimStart('0');
            return key.Length >= 6 && key.Length <= 14 ? key : "";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpBarcodeReader";
        AndroidJavaClass reader;

        /// <summary>True (and a reader) when the barcode library is part of this build.</summary>
        public static bool TryCreate(out MlKitBarcodeReader created)
        {
            created = null;
            try
            {
                var javaClass = new AndroidJavaClass(JavaClass);
                if (!javaClass.CallStatic<bool>("isAvailable")) { javaClass.Dispose(); return false; }
                created = new MlKitBarcodeReader { reader = javaClass };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Scan] The phone's barcode reader is not in this build: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Looks for a barcode in an upright picture. `done(code)` is called when it has finished ("" = none seen),
        /// NOT on Unity's own thread: it may only store the answer.
        /// </summary>
        public void Read(Texture2D picture, Action<string> done)
        {
            try
            {
                byte[] jpg = picture.EncodeToJPG(90);
                var bytes = new sbyte[jpg.Length];
                Buffer.BlockCopy(jpg, 0, bytes, 0, jpg.Length);
                reader.CallStatic("read", bytes, new Answer(done));
            }
            catch (Exception e) { Debug.LogWarning("[Scan] Barcode: " + e.Message); done(""); }
        }

        class Answer : AndroidJavaProxy
        {
            readonly Action<string> done;
            public Answer(Action<string> done) : base(JavaClass + "$Callback") { this.done = done; }
            [Preserve] public void onResult(string code) { done(code ?? ""); }
            [Preserve] public void onError(string message) { done(""); }
        }
#else
        public static bool TryCreate(out MlKitBarcodeReader created) { created = null; return false; }
        public void Read(Texture2D picture, Action<string> done) { done(""); }
#endif
    }
}
