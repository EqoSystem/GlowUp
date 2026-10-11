using System;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>One reading of a picture. It is finished when <see cref="Done"/> is true; <see cref="Text"/> is "" when nothing was read.</summary>
    public class OcrRead
    {
        volatile bool done;
        volatile string text = "";
        public bool Done => done;
        public string Text => text;
        /// <summary>
        /// Where the lines of text are in the picture: x, y, width and height from 0 to 1, measured from the
        /// picture's BOTTOM-left corner (the way Unity's UI counts). Empty when the reader cannot tell
        /// (the Editor's reader) or when nothing was read.
        /// </summary>
        public Rect[] Boxes { get; private set; } = new Rect[0];

        internal void Finish(string result, string boxes = null)
        {
            text = result ?? "";
            Boxes = Parse(boxes);
            done = true;
        }

        static Rect[] Parse(string boxes)
        {
            if (string.IsNullOrEmpty(boxes)) return new Rect[0];
            var list = new System.Collections.Generic.List<Rect>();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string one in boxes.Split(';'))
            {
                string[] n = one.Split(',');
                if (n.Length != 4) continue;
                if (!float.TryParse(n[0], System.Globalization.NumberStyles.Float, culture, out float left) ||
                    !float.TryParse(n[1], System.Globalization.NumberStyles.Float, culture, out float top) ||
                    !float.TryParse(n[2], System.Globalization.NumberStyles.Float, culture, out float right) ||
                    !float.TryParse(n[3], System.Globalization.NumberStyles.Float, culture, out float bottom)) continue;
                left = Mathf.Clamp01(left); right = Mathf.Clamp01(right); top = Mathf.Clamp01(top); bottom = Mathf.Clamp01(bottom);
                if (right - left < 0.01f || bottom - top < 0.004f) continue;
                list.Add(new Rect(left, 1f - bottom, right - left, bottom - top));      // the reader counts from the top, Unity from the bottom
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// Reads text from a picture on the device (no internet needed). One shared reader is kept for the
    /// whole app because starting one takes a moment.
    ///
    /// On an Android phone the phone's own text reader is used (Google ML Kit, see MlKitTextReader): it
    /// exists for 32-bit and 64-bit phones. Everywhere else (the Unity Editor), or if that reader is not
    /// part of the build, the Tesseract engine that is already in this project (TesseractDriver) is used.
    /// </summary>
    public class OcrService
    {
        static OcrService shared;
        public static OcrService Shared => shared ?? (shared = new OcrService());

        MlKitTextReader phoneReader;
        TesseractDriver driver;
        bool started;

        /// <summary>True once a reader can read text.</summary>
        public bool Ready { get; private set; }
        /// <summary>Why no reader could start, if none could.</summary>
        public string Error { get; private set; }
        /// <summary>Which reader is in use: "ML Kit" or "Tesseract".</summary>
        public string Engine { get; private set; } = "";

        /// <summary>Starts the reader (safe to call more than once).</summary>
        public void Initialise()
        {
            if (started) return;
            started = true;
            if (MlKitTextReader.TryCreate(out phoneReader)) { Engine = "ML Kit"; Ready = true; return; }
            StartTesseract();
        }

        void StartTesseract()
        {
            try
            {
                Engine = "Tesseract";
                driver = new TesseractDriver();
                driver.Setup(() => { Ready = true; });
            }
            catch (Exception e)
            {
                Error = e.Message;
                Debug.LogWarning("[OcrService] Text reading is not available: " + e.Message);
            }
        }

        /// <summary>
        /// Starts reading the text in an upright picture and returns at once. Wait until the answer's
        /// <see cref="OcrRead.Done"/> is true (in a coroutine: `while (!read.Done) yield return null;`).
        /// The picture must stay alive until then.
        /// </summary>
        public OcrRead Read(Texture2D uprightPicture)
        {
            var read = new OcrRead();
            if (!Ready || uprightPicture == null) { read.Finish(""); return read; }

            if (phoneReader != null)
            {
                phoneReader.Read(uprightPicture, (text, boxes, problem) =>
                {
                    if (problem != null) Debug.LogWarning("[OcrService] Could not read this frame: " + problem);
                    read.Finish(text, boxes);
                });
                return read;
            }

            read.Finish(Recognise(uprightPicture));
            return read;
        }

        /// <summary>
        /// Reads with the Tesseract engine and waits for the answer (the Editor's way). Returns "" when nothing
        /// could be read. The texture must be readable; the engine marks the words it found on it.
        /// </summary>
        public string Recognise(Texture2D uprightPicture)
        {
            if (!Ready || driver == null || uprightPicture == null) return "";
            try { return driver.Recognize(uprightPicture) ?? ""; }
            catch (Exception e)
            {
                Debug.LogWarning("[OcrService] Could not read this frame: " + e.Message);
                return "";
            }
        }
    }
}
