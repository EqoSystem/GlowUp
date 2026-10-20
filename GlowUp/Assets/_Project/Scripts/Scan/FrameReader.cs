using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace GlowUp.Scan
{
    /// <summary>
    /// Reads camera pictures for hold-to-scan, several a second: the text in a picture (line by line, with how sure the
    /// reader is of each line and where it is) and its barcode, on the phone itself (Google ML Kit, no internet) and
    /// off the thread that draws the app.
    ///
    /// Only one picture is read at a time. While <see cref="Busy"/> is true the scan screen keeps the camera's newest
    /// picture for the next turn, so pictures never queue up ("always the latest frame").
    ///
    /// The Java half is Assets/Plugins/Android/GlowUpFrameReader.java. In the Editor there is no ML Kit: the older
    /// Tesseract reader is used instead (slowly, without positions), so the screen can still be tried with a webcam.
    /// </summary>
    public class FrameReader
    {
        /// <summary>The answer for one picture: what was read, or (reading == null) why nothing could be read. NOT called on Unity's own thread.</summary>
        public delegate void Answer(FrameReading reading, string problem);

        /// <summary>The reader for this device (never null). <see cref="Available"/> says whether it can read.</summary>
        public static FrameReader Create()
        {
            var reader = new FrameReader();
            reader.Start();
            return reader;
        }

        /// <summary>True when pictures can be read on this device.</summary>
        public bool Available { get; private set; }
        /// <summary>True when barcodes are read as well.</summary>
        public bool ReadsBarcodes { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.glowup.ocr.GlowUpFrameReader";
        AndroidJavaClass java;
        Callback callback;

        void Start()
        {
            try
            {
                java = new AndroidJavaClass(JavaClass);
                Available = java.CallStatic<bool>("isAvailable");
                ReadsBarcodes = Available && java.CallStatic<bool>("hasBarcodes");
                callback = new Callback();
            }
            catch (Exception e)
            {
                Available = false;
                Debug.LogWarning("[Scan] The phone's text reader is not in this build: " + e.Message);
            }
        }

        /// <summary>True while the picture before is still being read.</summary>
        public bool Busy
        {
            get { try { return java != null && java.CallStatic<bool>("isBusy"); } catch (Exception) { return true; } }
        }

        /// <summary>
        /// Reads an UPRIGHT picture: `pixels` row by row from the TOP, each pixel as one ARGB number (only the first
        /// width x height numbers are used). Returns false when the picture was not taken (busy, or no reader).
        /// </summary>
        public bool Read(int[] pixels, int width, int height, bool wantBarcode, Answer done, Action<string> onBarcode = null)
        {
            if (!Available || java == null || done == null) return false;
            try
            {
                callback.done = done;                 // one picture at a time, so one callback is enough
                callback.barcodeEarly = onBarcode;
                return java.CallStatic<bool>("read", pixels, width, height, wantBarcode && ReadsBarcodes, callback);
            }
            catch (Exception e) { Debug.LogWarning("[Scan] " + e.Message); return false; }
        }

        class Callback : AndroidJavaProxy
        {
            public volatile Answer done;
            public volatile Action<string> barcodeEarly;
            public Callback() : base(JavaClass + "$Callback") { }
            [Preserve] public void onResult(string lines, string barcode) { done?.Invoke(Parse(lines, barcode), null); }
            [Preserve] public void onError(string message) { done?.Invoke(null, string.IsNullOrEmpty(message) ? "unknown problem" : message); }
            [Preserve] public void onBarcode(string barcode) { barcodeEarly?.Invoke(barcode); }
        }
#else
        volatile bool busy;

        void Start()
        {
            OcrService.Shared.Initialise();
            Available = true;                         // the Editor's reader loads in the background; Read answers "nothing" until it is ready
            ReadsBarcodes = false;
        }

        public bool Busy => busy;

        /// <summary>The Editor's way: the picture is read by the Tesseract engine, at once and without positions.</summary>
        public bool Read(int[] pixels, int width, int height, bool wantBarcode, Answer done, Action<string> onBarcode = null)
        {
            if (busy || done == null || pixels == null || width <= 0 || height <= 0) return false;
            if (!OcrService.Shared.Ready) { done(new FrameReading(), null); return true; }
            busy = true;
            Texture2D picture = null;
            try
            {
                // the text reader wants a texture: rows from the bottom, colours as bytes
                var colours = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = pixels[(height - 1 - y) * width + x];
                        colours[y * width + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, 255);
                    }
                picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                picture.SetPixels32(colours); picture.Apply();
                string text = OcrService.Shared.Recognise(picture);
                var reading = new FrameReading();
                foreach (string line in (text ?? "").Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) reading.lines.Add(new FrameReading.Line { text = line.Trim(), confidence = 0.8f });
                done(reading, null);
            }
            catch (Exception e) { done(null, e.Message); }
            finally
            {
                if (picture != null) UnityEngine.Object.Destroy(picture);
                busy = false;
            }
            return true;
        }
#endif

        /// <summary>
        /// What the Java half writes, turned into a reading. `lines` = one row per line of text, the parts separated by
        /// tabs: confidence, left, top, right, bottom, text. A row that cannot be understood is left out.
        /// `barcode` = the digits, and when the place of the barcode is known a tab and left, top, right, bottom after them.
        /// </summary>
        public static FrameReading Parse(string lines, string barcode)
        {
            string[] code = (barcode ?? "").Split('\t');
            var reading = new FrameReading { barcode = code[0].Trim() };
            if (code.Length >= 5 && reading.barcode.Length > 0 && Number(code[1], out float codeLeft) && Number(code[2], out float codeTop)
                && Number(code[3], out float codeRight) && Number(code[4], out float codeBottom))
            {
                reading.barcodeLeft = Mathf.Clamp01(codeLeft); reading.barcodeTop = Mathf.Clamp01(codeTop);
                reading.barcodeRight = Mathf.Clamp01(codeRight); reading.barcodeBottom = Mathf.Clamp01(codeBottom);
            }
            if (string.IsNullOrEmpty(lines)) return reading;
            foreach (string row in lines.Split('\n'))
            {
                string[] parts = row.Split(new[] { '\t' }, 6);
                if (parts.Length < 6 || string.IsNullOrWhiteSpace(parts[5])) continue;
                if (!Number(parts[0], out float confidence) || !Number(parts[1], out float left) || !Number(parts[2], out float top)
                    || !Number(parts[3], out float right) || !Number(parts[4], out float bottom)) continue;
                reading.lines.Add(new FrameReading.Line
                {
                    text = parts[5].Trim(), confidence = Mathf.Clamp01(confidence),
                    left = Mathf.Clamp01(left), top = Mathf.Clamp01(top), right = Mathf.Clamp01(right), bottom = Mathf.Clamp01(bottom)
                });
            }
            return reading;
        }

        static bool Number(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// Where the lines of a reading are, and its barcode, for the outlines on the camera picture: x, y, width, height
        /// from 0 to 1, measured from the BOTTOM-left corner (the way Unity's UI counts). Lines without a position are left out.
        /// </summary>
        public static Rect[] BoxesOf(FrameReading reading, float minimumConfidence = 0f)
        {
            if (reading == null) return new Rect[0];
            var boxes = new System.Collections.Generic.List<Rect>();
            foreach (FrameReading.Line line in reading.lines)
            {
                if (line == null || line.confidence < minimumConfidence) continue;
                if (line.right - line.left < 0.01f || line.bottom - line.top < 0.004f) continue;
                boxes.Add(new Rect(line.left, 1f - line.bottom, line.right - line.left, line.bottom - line.top));
            }
            if (reading.barcode.Length > 0 && reading.barcodeRight - reading.barcodeLeft >= 0.01f && reading.barcodeBottom - reading.barcodeTop >= 0.004f)
                boxes.Add(new Rect(reading.barcodeLeft, 1f - reading.barcodeBottom, reading.barcodeRight - reading.barcodeLeft, reading.barcodeBottom - reading.barcodeTop));
            return boxes.ToArray();
        }
    }
}
