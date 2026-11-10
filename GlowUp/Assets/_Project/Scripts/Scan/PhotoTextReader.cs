using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// READS A CAPTURED PRODUCT PHOTO THE WAY THE LIVE SCAN READS ITS FRAMES (11 November 2026, New13 item 9).
    ///
    /// The live scan reads every other frame turned by 90 and 270 degrees, because packs carry text sideways (a bottle's name runs up its
    /// side). The photo path read the picture once, upright, so sideways words came out as nonsense: "We couldn't read this clearly".
    /// This reader reads the photo upright, turned clockwise and turned counter-clockwise, judges each reading by its words
    /// (PhotoTextQuality), and joins the readings that hold real words. The boxes are turned back to the photo's own frame.
    ///
    /// Photo only: the live matcher (SmartScanFlowController, ScanShortlist, ...) is not touched.
    /// </summary>
    public static class PhotoTextReader
    {
        public class Result
        {
            public string rawText = "";
            public Rect[] boxes = new Rect[0];
            public PhotoTextQuality.Verdict verdict = new PhotoTextQuality.Verdict();
            /// <summary>0 = upright, 90 = read after turning clockwise, 270 = counter-clockwise; the turns that held words.</summary>
            public List<int> turns = new List<int>();
        }

        const float SecondsPerReading = 10f;

        /// <summary>Reads `photo` (readable texture) in up to three turns. `cancelled` is polled between readings.</summary>
        public static async Task<Result> Read(Texture2D photo, Func<string, bool> isKnownWord, Func<bool> cancelled)
        {
            var result = new Result();
            if (photo == null) return result;
            var texts = new StringBuilder();
            var boxes = new List<Rect>();
            int knownSum = 0, wordSum = 0;
            string bestText = ""; PhotoTextQuality.Verdict best = null;

            foreach (int turn in new[] { 0, 90, 270 })
            {
                Texture2D picture = turn == 0 ? photo : Turned(photo, turn == 90);
                try
                {
                    OcrRead read = OcrService.Shared.Read(picture);
                    float waited = 0f;
                    while (!read.Done && waited < SecondsPerReading)
                    {
                        await Task.Delay(40); waited += 0.04f;
                        if (cancelled != null && cancelled()) return result;
                    }
                    if (!read.Done || string.IsNullOrWhiteSpace(read.Text)) continue;
                    PhotoTextQuality.Verdict v = PhotoTextQuality.Judge(read.Text, isKnownWord, 1, PhotoTextQuality.MinimumKnownShare);
                    if (v.knownWords < 1 || v.score < PhotoTextQuality.MinimumKnownShare) continue;     // a wrong turn reads nonsense: left out
                    knownSum += v.knownWords; wordSum += v.words;
                    if (texts.Length > 0) texts.Append('\n');
                    texts.Append(read.Text.Trim());
                    foreach (Rect b in read.Boxes) boxes.Add(turn == 0 ? b : Back(b, turn == 90));
                    result.turns.Add(turn);
                    if (best == null || v.knownWords > best.knownWords) { best = v; bestText = read.Text; }
                }
                finally { if (turn != 0 && picture != null) UnityEngine.Object.Destroy(picture); }
            }

            if (best == null) return result;
            result.rawText = texts.ToString();
            result.boxes = boxes.ToArray();
            // the joined words are judged once more with the normal thresholds (two known words, a third of all words)
            PhotoTextQuality.Verdict whole = PhotoTextQuality.Judge(result.rawText, isKnownWord);
            if (!whole.readable) whole = PhotoTextQuality.Judge(bestText, isKnownWord);       // the best single turn alone
            result.verdict = whole;
            return result;
        }

        /// <summary>A copy of the picture turned a quarter: clockwise or counter-clockwise (y counts from the bottom).</summary>
        static Texture2D Turned(Texture2D src, bool clockwise)
        {
            int w = src.width, h = src.height;
            Color32[] from = src.GetPixels32(), to = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = clockwise ? y : h - 1 - y;
                    int ny = clockwise ? w - 1 - x : x;
                    to[ny * h + nx] = from[y * w + x];
                }
            var turned = new Texture2D(h, w, TextureFormat.RGBA32, false);
            turned.SetPixels32(to);
            turned.Apply(false);
            return turned;
        }

        /// <summary>A box from a turned picture, in the original photo's frame (0 to 1 from the bottom-left).</summary>
        public static Rect Back(Rect b, bool turnedClockwise)
        {
            return turnedClockwise
                ? new Rect(1f - (b.y + b.height), b.x, b.height, b.width)
                : new Rect(b.y, 1f - (b.x + b.width), b.height, b.width);
        }
    }
}
