using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// PRODUCT DETECTION from the lines of text the phone's reader found and where it found them (no extra model, works offline).
    /// A camera picture shows more than the product: a keyboard, a laptop badge, a shelf label. Their words used to be taken for
    /// the product's name. The words of ONE product stand close together (a name over a brand over a claim), the words of other
    /// things stand elsewhere, so:
    ///
    ///   1. lines whose boxes lie near each other (within <see cref="ReachFactor"/> times the print size) form one group;
    ///   2. the group that holds a known brand, then a make-up kind, then sits nearest the middle of the frame is the product;
    ///   3. only the lines of that group (and any line inside its outer box) are kept. Everything else is dropped.
    ///
    /// The product's outer box is given back so the camera picture can show what was detected. A picture with fewer than
    /// <see cref="MinimumLines"/> lines in the product group, or lines without a position (the Editor's reader), is left alone:
    /// nothing is ever hidden when the group cannot be told with some certainty.
    /// Pure logic: tested in Editor/Tests/ProductRegionTests.cs.
    /// </summary>
    public static class ProductRegion
    {
        /// <summary>Two lines belong together when the gap between their boxes is at most this many times the bigger print size.</summary>
        public static float ReachFactor = 1.6f;
        /// <summary>A product group with fewer lines than this is not trusted: the picture is used as it is.</summary>
        public static int MinimumLines = 2;
        /// <summary>The outer box is drawn this much (0 to 1 of the picture) bigger than the text it holds.</summary>
        public static float DrawMargin = 0.012f;

        /// <summary>What was found in one picture.</summary>
        public class Result
        {
            /// <summary>True when a product group was told apart.</summary>
            public bool found;
            /// <summary>The product's outer box: x, y, width, height from 0 to 1, measured from the picture's BOTTOM-left corner (like FrameReader.BoxesOf).</summary>
            public Rect box;
            /// <summary>The picture with only the lines on the product (the picture itself when nothing was told apart). The barcode is kept.</summary>
            public FrameReading reading;
            /// <summary>How many lines were dropped as not on the product.</summary>
            public int dropped;
        }

        /// <summary>
        /// `aspect` = the picture's width divided by its height (positions are 0 to 1 in both directions, so sizes are compared in
        /// the same unit). `isBrand` says whether a text holds a brand the app knows (null = not asked). Lines the reader is less
        /// sure of than `minimumConfidence` do not join any group (a misread scrap must not link two groups).
        /// </summary>
        public static Result Find(FrameReading frame, float aspect, Func<string, bool> isBrand, float minimumConfidence = 0f)
        {
            var result = new Result { reading = frame };
            if (frame == null || frame.lines == null) return result;
            if (aspect <= 0.05f) aspect = 0.75f;

            var placed = new List<FrameReading.Line>();
            foreach (FrameReading.Line line in frame.lines)
                if (IsPlaced(line) && line.confidence >= minimumConfidence) placed.Add(line);
            if (placed.Count <= MinimumLines) return result;            // too little to tell a product from its surroundings

            // ---- 1. groups of lines that stand close together
            int[] group = new int[placed.Count];
            for (int i = 0; i < group.Length; i++) group[i] = i;
            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                    if (Gap(placed[i], placed[j], aspect) <= ReachFactor * Math.Max(PrintSize(placed[i], aspect), PrintSize(placed[j], aspect)))
                        Join(group, i, j);

            var members = new Dictionary<int, List<FrameReading.Line>>();
            for (int i = 0; i < placed.Count; i++)
            {
                int root = Root(group, i);
                if (!members.TryGetValue(root, out List<FrameReading.Line> list)) members[root] = list = new List<FrameReading.Line>();
                list.Add(placed[i]);
            }

            // ---- 2. the group that is the product
            List<FrameReading.Line> best = null; float bestScore = float.MinValue;
            foreach (List<FrameReading.Line> list in members.Values)
            {
                float score = Score(list, aspect, isBrand);
                if (score > bestScore) { bestScore = score; best = list; }
            }
            if (best == null || best.Count < MinimumLines) return result;

            // ---- 3. keep the group's lines and any line inside its outer box
            Bounds(best, out float left, out float top, out float right, out float bottom);
            var kept = new FrameReading
            {
                barcode = frame.barcode, barcodeLeft = frame.barcodeLeft, barcodeTop = frame.barcodeTop,
                barcodeRight = frame.barcodeRight, barcodeBottom = frame.barcodeBottom
            };
            foreach (FrameReading.Line line in frame.lines)
            {
                if (line == null) continue;
                float cx = (line.left + line.right) / 2f, cy = (line.top + line.bottom) / 2f;
                if (!IsPlaced(line) || best.Contains(line) || (cx >= left && cx <= right && cy >= top && cy <= bottom)) kept.lines.Add(line);
                else result.dropped++;
            }
            float l = Mathf.Clamp01(left - DrawMargin), t = Mathf.Clamp01(top - DrawMargin);
            float r = Mathf.Clamp01(right + DrawMargin), b = Mathf.Clamp01(bottom + DrawMargin);
            result.found = true;
            result.box = new Rect(l, 1f - b, r - l, b - t);
            result.reading = kept;
            return result;
        }

        /// <summary>The group's weight as "the product": a known brand and a make-up kind count most, then closeness to the middle of the frame.</summary>
        static float Score(List<FrameReading.Line> lines, float aspect, Func<string, bool> isBrand)
        {
            var text = new StringBuilder();
            foreach (FrameReading.Line line in lines) text.Append(line.text).Append('\n');
            string all = text.ToString();
            float score = 0f;
            if (isBrand != null && isBrand(all)) score += 100f;
            if (ProductScope.TypeFromLabel(all).Length > 0) score += 60f;
            if (ScanZones.HasCode(all, out _)) score += 10f;

            Bounds(lines, out float left, out float top, out float right, out float bottom);
            float dx = ((left + right) / 2f - 0.5f) * aspect, dy = (top + bottom) / 2f - 0.5f;
            float centre = 1f - Mathf.Min(1f, Mathf.Sqrt(dx * dx + dy * dy) / 0.7f);
            score += 30f * centre;
            score += 20f * Mathf.Min(1f, lines.Count / 6f);
            return score;
        }

        static bool IsPlaced(FrameReading.Line line) =>
            line != null && !string.IsNullOrWhiteSpace(line.text) && line.right - line.left >= 0.01f && line.bottom - line.top >= 0.004f;

        /// <summary>How big the print is: the thin side of the line's box (text is long and thin, upright or turned), in picture heights.</summary>
        static float PrintSize(FrameReading.Line line, float aspect) => Mathf.Min((line.right - line.left) * aspect, line.bottom - line.top);

        /// <summary>The distance between two boxes, in picture heights (0 when they touch or overlap).</summary>
        static float Gap(FrameReading.Line a, FrameReading.Line b, float aspect)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.left, b.left) - Mathf.Min(a.right, b.right)) * aspect;
            float dy = Mathf.Max(0f, Mathf.Max(a.top, b.top) - Mathf.Min(a.bottom, b.bottom));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static void Bounds(List<FrameReading.Line> lines, out float left, out float top, out float right, out float bottom)
        {
            left = top = float.MaxValue; right = bottom = float.MinValue;
            foreach (FrameReading.Line line in lines)
            {
                left = Mathf.Min(left, line.left); top = Mathf.Min(top, line.top);
                right = Mathf.Max(right, line.right); bottom = Mathf.Max(bottom, line.bottom);
            }
        }

        static int Root(int[] group, int i)
        {
            while (group[i] != i) { group[i] = group[group[i]]; i = group[i]; }
            return i;
        }

        static void Join(int[] group, int a, int b)
        {
            int x = Root(group, a), y = Root(group, b);
            if (x != y) group[y] = x;
        }
    }
}
