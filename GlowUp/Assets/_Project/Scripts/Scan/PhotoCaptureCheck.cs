using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// PHOTO PATH ONLY (New13 item 8, 10 November 2026). Before "Check your photo" opens, the captured picture is checked: a near-black
    /// picture (the camera was off or not ready, or the room is dark), a picture too small to be a camera frame, or a very blurry one is
    /// not shown; the user is told to retake it instead. Pure, tested in PhotoCaptureCheckTests. The live scan does not use this.
    /// </summary>
    public static class PhotoCaptureCheck
    {
        public enum Verdict { Ok, NotReady, TooDark, TooBlurry }

        /// <summary>Mean brightness (0..255) under which a photo counts as black.</summary>
        public const float DarkMean = 22f;
        /// <summary>Share of pixels brighter than 40 under which a photo counts as black (a dark photo with a few lit letters still passes).</summary>
        public const float LitShare = 0.04f;
        /// <summary>Mean brightness difference between neighbours under which a photo counts as very blurry (or flat).</summary>
        public const float SharpEdge = 1.2f;
        public const int SmallestSide = 100;

        public static Verdict Judge(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width < SmallestSide || height < SmallestSide || pixels.Length < width * height) return Verdict.NotReady;
            // every 4th pixel of every 4th row: enough to judge, fast on a phone
            double sum = 0, edge = 0; int count = 0, lit = 0, pairs = 0;
            for (int y = 0; y < height; y += 4)
            {
                int row = y * width;
                float last = -1f;
                for (int x = 0; x < width; x += 4)
                {
                    Color32 c = pixels[row + x];
                    float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    sum += l; count++;
                    if (l > 40f) lit++;
                    if (last >= 0f) { edge += Mathf.Abs(l - last); pairs++; }
                    last = l;
                }
            }
            if (count == 0) return Verdict.NotReady;
            float mean = (float)(sum / count), litShare = lit / (float)count, sharp = pairs > 0 ? (float)(edge / pairs) : 0f;
            if (mean < DarkMean && litShare < LitShare) return Verdict.TooDark;
            if (sharp < SharpEdge) return Verdict.TooBlurry;
            return Verdict.Ok;
        }

        public static Verdict Judge(Texture2D photo) => photo == null ? Verdict.NotReady : Judge(photo.GetPixels32(), photo.width, photo.height);
    }
}
