using System;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE QUALITY GATE of hold-to-scan: is this camera picture good enough to be read at all?
    /// While the scan button is held, the camera gives several pictures a second. A picture that is too dark, taken
    /// while the phone moves fast, burnt out by glare or out of focus is thrown away before the text reader sees it,
    /// so it can never count towards the ring.
    ///
    /// It works on two small grey pictures (one number 0 to 255 per pixel, row by row):
    ///   overview - the whole scan frame made small (about 64 x 48): brightness, glare and movement
    ///   detail   - a patch from the middle of the frame at the camera's own sharpness (about 160 x 160): focus
    ///
    /// Pure logic: no camera, no Unity scene. The limits below are set from the Scan Rules asset (ScanRules).
    /// </summary>
    public static class FrameQuality
    {
        public enum Fault { None, Dark, Moving, Glare, Blurry }

        /// <summary>What was measured in one picture.</summary>
        public struct Measure
        {
            /// <summary>Average brightness, 0 (black) to 1 (white).</summary>
            public float brightness;
            /// <summary>The share of the picture that is burnt out to white, 0 to 1.</summary>
            public float glare;
            /// <summary>How crisp the edges are, 0 to 1: about 1 for a razor-sharp edge, about 0.33 for an edge smeared over three pixels.</summary>
            public float sharpness;
            /// <summary>How much there is to see at all (the strength of the strongest edges), 0 to 1. Near 0 = a plain surface: nothing to judge focus by.</summary>
            public float edges;
            /// <summary>How much the picture changed since the one before, 0 to 1. Negative = not known (the first picture).</summary>
            public float motion;
        }

        /// <summary>Darker than this on average = too dark to read.</summary>
        public static float DarkBelow = 0.14f;
        /// <summary>A pixel at least this bright counts as burnt out.</summary>
        public static float GlareLevel = 0.97f;
        /// <summary>More than this share of burnt-out pixels = glare.</summary>
        public static float GlareShare = 0.18f;
        /// <summary>The picture changed more than this since the one before = the phone is moving fast.</summary>
        public static float MovingAbove = 0.06f;
        /// <summary>Edges less crisp than this = out of focus.</summary>
        public static float BlurBelow = 0.4f;
        /// <summary>Focus is only judged when the strongest edges are at least this strong (a plain surface has no edges to judge).</summary>
        public static float MinimumEdges = 0.1f;

        /// <summary>Measures one picture. `before` = the overview of the picture before (same size), or null.</summary>
        public static Measure Of(byte[] overview, int width, int height, byte[] before, byte[] detail, int detailWidth, int detailHeight)
        {
            var m = new Measure { motion = -1f };
            int count = overview != null ? Math.Min(overview.Length, width * height) : 0;
            if (count > 0)
            {
                long sum = 0; int burnt = 0;
                int burntFrom = (int)Math.Round(GlareLevel * 255f);
                for (int i = 0; i < count; i++) { int v = overview[i]; sum += v; if (v >= burntFrom) burnt++; }
                m.brightness = sum / (255f * count);
                m.glare = burnt / (float)count;
                if (before != null && before.Length >= count)
                {
                    long change = 0;
                    for (int i = 0; i < count; i++) change += Math.Abs(overview[i] - before[i]);
                    m.motion = change / (255f * count);
                }
            }
            Edges(detail, detailWidth, detailHeight, out m.sharpness, out m.edges);
            return m;
        }

        /// <summary>
        /// How crisp the edges of a grey picture are. Across a sharp edge the brightness jumps within one pixel; across a
        /// smeared one it climbs over several. So the biggest one-pixel steps are compared with the biggest three-pixel
        /// steps: sharp = about the same size (1), smeared over three pixels = a third (0.33). It does not depend on how
        /// much text is in the picture, only on how its edges look.
        /// </summary>
        public static void Edges(byte[] grey, int width, int height, out float sharpness, out float edges)
        {
            sharpness = 0f; edges = 0f;
            if (grey == null || width < 8 || height < 8 || grey.Length < width * height) return;
            var near = new int[256]; var far = new int[256];
            int steps = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x + 3 < width; x++)
                {
                    near[Math.Abs(grey[row + x + 1] - grey[row + x])]++;
                    far[Math.Abs(grey[row + x + 3] - grey[row + x])]++;
                    steps++;
                }
            }
            for (int x = 0; x < width; x++)
                for (int y = 0; y + 3 < height; y++)
                {
                    near[Math.Abs(grey[(y + 1) * width + x] - grey[y * width + x])]++;
                    far[Math.Abs(grey[(y + 3) * width + x] - grey[y * width + x])]++;
                    steps++;
                }
            if (steps == 0) return;
            // the strongest 3% of the steps: the edges of the picture
            int strongNear = TopValue(near, steps, 0.03f), strongFar = TopValue(far, steps, 0.03f);
            edges = strongFar / 255f;
            sharpness = strongFar <= 0 ? 0f : Math.Min(1f, strongNear / (float)strongFar);
        }

        // the value that only `share` of the counted steps reach or exceed
        static int TopValue(int[] histogram, int total, float share)
        {
            int wanted = Math.Max(1, (int)(total * share)), seen = 0;
            for (int value = 255; value >= 0; value--)
            {
                seen += histogram[value];
                if (seen >= wanted) return value;
            }
            return 0;
        }

        /// <summary>What is wrong with the picture, if anything. The first fault that applies is named: dark, moving, glare, blurry.</summary>
        public static Fault Judge(Measure m)
        {
            if (m.brightness < DarkBelow) return Fault.Dark;
            if (m.motion >= 0f && m.motion > MovingAbove) return Fault.Moving;
            if (m.glare > GlareShare) return Fault.Glare;
            if (m.edges >= MinimumEdges && m.sharpness < BlurBelow) return Fault.Blurry;
            return Fault.None;
        }
    }
}
