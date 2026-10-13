using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE TEXT TRACER of hold-to-scan: it helps the text reader with small print.
    ///
    /// The text reader gets the whole scan frame, several times a second. An ingredient list is printed so small that
    /// in the whole frame its letters are only a few camera pixels high, and such letters are read badly. The tracer:
    ///
    ///  1. FOLLOWS the block of text from one reading to the next (every reading says where each line lies);
    ///  2. measures how high the letters of that block are in the camera's picture;
    ///  3. when they are small, asks for a CLOSE LOOK in every other picture: only the block, enlarged until its letters
    ///     have a good height. The pictures in between still show the whole frame, so nothing next to the block is
    ///     missed (the brand, the barcode);
    ///  4. says where the block is, so the screen can draw it and the camera can focus on it.
    ///
    /// It lets go of the block when the phone moves or no text is read any more; the next reading of the whole frame
    /// finds it again. With large letters it does nothing at all: every picture is the whole frame, as before.
    ///
    /// Pure logic: no camera, no Unity scene. Places are 0 to 1 of the scan frame, measured from its top-left corner.
    /// </summary>
    public class TextTracer
    {
        /// <summary>Off = every picture is the whole frame (the tracer still says where the text is).</summary>
        public bool CloseLooks = true;
        /// <summary>A line counts when the reader is at least this sure of it (0 to 1).</summary>
        public float UsableLine = 0.3f;
        /// <summary>A block needs at least this many lines.</summary>
        public int MinimumLines = 2;
        /// <summary>Letters lower than this many camera pixels are small print: the block gets close looks.</summary>
        public float SmallBelowPixels = 24f;
        /// <summary>A close look is enlarged until the letters are about this many pixels high.</summary>
        public float GoodPixels = 32f;
        /// <summary>A close look is enlarged this many times at most (more only smears the letters).</summary>
        public float MostEnlarged = 2.5f;
        /// <summary>Room round the block on every side, as a share of the frame.</summary>
        public float Margin = 0.05f;
        /// <summary>How quickly the block follows a new reading (1 = at once, lower = calmer).</summary>
        public float Follow = 0.65f;
        /// <summary>The block is let go after this many readings in a row without text in it.</summary>
        public int ReadingsToLose = 3;

        /// <summary>True while a block of text is followed.</summary>
        public bool Following { get; private set; }
        /// <summary>The followed block with its margin (0 to 1 of the frame, from the top-left corner).</summary>
        public Rect Block { get; private set; }
        /// <summary>How high the letters of the block are in the camera's picture, in pixels (0 = not known).</summary>
        public float LetterPixels { get; private set; }
        /// <summary>True while the block is small print (it gets close looks).</summary>
        public bool SmallPrint => Following && LetterPixels > 0f && LetterPixels < SmallBelowPixels;

        static readonly Rect Whole = new Rect(0f, 0f, 1f, 1f);
        readonly List<float> heights = new List<float>();
        int missed;
        bool closeTurn;

        /// <summary>
        /// The picture to take next: `part` of the frame, `enlarge` times the camera's own size. The whole frame and 1
        /// unless small print is followed; then every other picture is a close look. Returns true for a close look.
        /// </summary>
        public bool Next(out Rect part, out float enlarge)
        {
            part = Whole; enlarge = 1f;
            if (!CloseLooks || !SmallPrint) { closeTurn = false; return false; }
            closeTurn = !closeTurn;
            if (!closeTurn) return false;
            part = Block;
            enlarge = Mathf.Clamp(GoodPixels / Mathf.Max(1f, LetterPixels), 1f, Mathf.Max(1f, MostEnlarged));
            return true;
        }

        /// <summary>
        /// A reading came back. Its lines must already be places of the FRAME (see ToFrame). `framePixelsHigh` = how many
        /// camera pixels the whole frame is high. `closeLook` = it was a close look: it shows only the block, so it can
        /// say "the text is still there" but not where the block ends.
        /// </summary>
        public void Take(FrameReading reading, float framePixelsHigh, bool closeLook)
        {
            float left = 1f, top = 1f, right = 0f, bottom = 0f;
            heights.Clear();
            if (reading != null)
                foreach (FrameReading.Line line in reading.lines)
                {
                    if (line == null || line.confidence < UsableLine || string.IsNullOrWhiteSpace(line.text)) continue;
                    float high = line.bottom - line.top;
                    if (line.right - line.left < 0.01f || high < 0.004f) continue;
                    left = Math.Min(left, line.left); top = Math.Min(top, line.top);
                    right = Math.Max(right, line.right); bottom = Math.Max(bottom, line.bottom);
                    heights.Add(high);
                }
            if (heights.Count < Math.Max(1, closeLook ? 1 : MinimumLines))
            {
                if (Following && ++missed >= Math.Max(1, ReadingsToLose)) LetGo();
                return;
            }
            missed = 0;
            if (closeLook) return;                           // the block keeps its place; it is measured in whole pictures only

            heights.Sort();
            float letters = heights[heights.Count / 2] * Math.Max(1f, framePixelsHigh);       // the middle line: one big headline does not count
            var found = Rect.MinMaxRect(Mathf.Clamp01(left - Margin), Mathf.Clamp01(top - Margin), Mathf.Clamp01(right + Margin), Mathf.Clamp01(bottom + Margin));
            if (Following)
            {
                float f = Mathf.Clamp01(Follow);
                Block = Rect.MinMaxRect(Mathf.Lerp(Block.xMin, found.xMin, f), Mathf.Lerp(Block.yMin, found.yMin, f),
                                        Mathf.Lerp(Block.xMax, found.xMax, f), Mathf.Lerp(Block.yMax, found.yMax, f));
                LetterPixels = Mathf.Lerp(LetterPixels, letters, f);
            }
            else { Block = found; LetterPixels = letters; Following = true; }
        }

        /// <summary>The phone or the product moved: the block is no longer where it was.</summary>
        public void Moved() { LetGo(); }

        /// <summary>A new scan.</summary>
        public void Reset() { LetGo(); }

        void LetGo()
        {
            Following = false; LetterPixels = 0f; missed = 0; closeTurn = false;
            Block = Whole;
        }

        /// <summary>
        /// Turns the places of a reading of ONE PART of the frame into places of the whole frame (lines and barcode), so
        /// that everything after it (the outlines on screen, the tracer itself) can treat every reading alike.
        /// </summary>
        public static void ToFrame(FrameReading reading, Rect part)
        {
            if (reading == null || (part.xMin <= 0f && part.yMin <= 0f && part.xMax >= 1f && part.yMax >= 1f)) return;
            foreach (FrameReading.Line line in reading.lines)
            {
                if (line == null) continue;
                line.left = part.xMin + line.left * part.width; line.right = part.xMin + line.right * part.width;
                line.top = part.yMin + line.top * part.height; line.bottom = part.yMin + line.bottom * part.height;
            }
            if (reading.barcodeRight > reading.barcodeLeft || reading.barcodeBottom > reading.barcodeTop)
            {
                reading.barcodeLeft = part.xMin + reading.barcodeLeft * part.width; reading.barcodeRight = part.xMin + reading.barcodeRight * part.width;
                reading.barcodeTop = part.yMin + reading.barcodeTop * part.height; reading.barcodeBottom = part.yMin + reading.barcodeBottom * part.height;
            }
        }
    }
}
