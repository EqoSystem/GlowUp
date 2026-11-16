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
            /// <summary>Everything the reader saw in its best turn, even when no word was known (the catalogue matcher is tolerant of misreads).</summary>
            public string anyText = "";
            /// <summary>Up to three known words, in reading order, for the "We read" line.</summary>
            public List<string> knownWords = new List<string>();
        }

        const float SecondsPerReading = 10f;

        /// <summary>
        /// Reads `photo` (readable texture) in up to three turns. `cancelled` is polled between readings. `onShapes` is called each
        /// time a better turn is found, with that ONE turn's text boxes (0 to 1 in the photo): the outlines never pile up.
        /// </summary>
        public static async Task<Result> Read(Texture2D photo, Func<string, bool> isKnownWord, Func<bool> cancelled, Action<Rect[], List<string>> onShapes = null)
        {
            var result = new Result();
            if (photo == null) return result;
            // the phone's text reader for still pictures. Live scan reads through its own Java reader (GlowUpFrameReader), so on a
            // phone nothing else ever starts this one: without this line every photo read as "no text" (found 11 November 2026).
            OcrService.Shared.Initialise();
            float started = 0f;
            while (!OcrService.Shared.Ready && started < 4f)
            {
                await Task.Delay(50); started += 0.05f;
                if (cancelled != null && cancelled()) return result;
            }
            if (!OcrService.Shared.Ready)
            {
                { }
                return result;
            }
            var texts = new StringBuilder();
            var boxes = new List<Rect>();
            string bestText = ""; PhotoTextQuality.Verdict best = null;
            int shownKnown = -1, shownLength = 0;

            // the part inside the user's box is often a narrow strip: enlarged so its letters are big enough for the reader
            Texture2D source = Enlarged(photo);
            try
            {
                foreach (int turn in new[] { 0, 90, 270 })
                {
                    Texture2D picture = turn == 0 ? source : Turned(source, turn == 90);
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
                        var turnBoxes = new Rect[read.Boxes.Length];
                        for (int i = 0; i < turnBoxes.Length; i++) turnBoxes[i] = turn == 0 ? read.Boxes[i] : Back(read.Boxes[i], turn == 90);

                        // the outlines of the best turn so far: most known words, then most text
                        int len = read.Text.Trim().Length;
                        if (v.knownWords > shownKnown || (v.knownWords == shownKnown && len > shownLength))
                        {
                            shownKnown = v.knownWords; shownLength = len;
                            result.anyText = read.Text.Trim();
                            result.knownWords = KnownWords(read.Text, isKnownWord, 3);
                            onShapes?.Invoke(turnBoxes, result.knownWords);
                        }

                        if (v.knownWords < 1 || v.score < PhotoTextQuality.MinimumKnownShare) continue;     // a wrong turn reads nonsense: left out
                        if (texts.Length > 0) texts.Append('\n');
                        texts.Append(read.Text.Trim());
                        boxes.AddRange(turnBoxes);
                        result.turns.Add(turn);
                        if (best == null || v.knownWords > best.knownWords) { best = v; bestText = read.Text; }
                    }
                    finally { if (turn != 0 && picture != null) UnityEngine.Object.Destroy(picture); }
                }
            }
            finally { if (source != photo) UnityEngine.Object.Destroy(source); }

            if (best == null) return result;
            result.rawText = texts.ToString();
            result.boxes = boxes.ToArray();
            // the joined words are judged once more with the normal thresholds (two known words, a third of all words)
            PhotoTextQuality.Verdict whole = PhotoTextQuality.Judge(result.rawText, isKnownWord);
            if (!whole.readable) whole = PhotoTextQuality.Judge(bestText, isKnownWord);       // the best single turn alone
            result.verdict = whole;
            return result;
        }

        static readonly System.Text.RegularExpressions.Regex Word = new System.Text.RegularExpressions.Regex(@"[A-Za-zÀ-ÿ]{3,}");

        // small pack words that say nothing about the product: never shown in the "We read" line
        static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "the", "and", "for", "with", "use", "only", "out", "keep", "made", "net", "new", "lot", "exp", "mfg", "batch", "store", "place", "dry", "cool" };

        /// <summary>The first known words of a reading, each once, in ordinary capitals ("MAYBELLINE" = "Maybelline").</summary>
        public static List<string> KnownWords(string text, Func<string, bool> isKnownWord, int most)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in Word.Matches(text ?? ""))
            {
                if (list.Count >= most) break;
                if (Filler.Contains(m.Value) || !PhotoTextQuality.IsKnown(m.Value, isKnownWord) || !seen.Add(m.Value)) continue;
                list.Add(BrandNameNormalizer.Tidy(m.Value));
            }
            return list;
        }

        /// <summary>Shorter side below this: the picture is enlarged (at most 2x, longer side at most 2400) before reading.</summary>
        public const int SmallestReadSide = 900;

        /// <summary>The picture itself when big enough, else a smooth enlarged copy (boxes are 0 to 1, so they need no change).</summary>
        static Texture2D Enlarged(Texture2D src)
        {
            int shortSide = Mathf.Min(src.width, src.height), longSide = Mathf.Max(src.width, src.height);
            float k = Mathf.Min(2f, SmallestReadSide / (float)shortSide, 2400f / longSide);
            if (k <= 1.05f) return src;
            int w = Mathf.RoundToInt(src.width * k), h = Mathf.RoundToInt(src.height * k);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++) px[y * w + x] = src.GetPixelBilinear((x + 0.5f) / w, v);
            }
            var big = new Texture2D(w, h, TextureFormat.RGBA32, false);
            big.SetPixels32(px);
            big.Apply(false);
            return big;
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
