using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Tells whether a camera picture on screen is really empty. A camera that has just been opened (the first time the camera is
    /// allowed, or after the app came back from a question) can run, with the phone's camera light on, while the picture it
    /// gives stays pure black. The AR Try-On screen and the camera box use this to notice that and start the picture again.
    /// A dark room or a covered lens still gives sensor noise, so only a picture with no light at all counts as empty.
    /// </summary>
    public static class BlackFrameProbe
    {
        const int Size = 16;
        static RenderTexture tiny;
        static Texture2D read;

        /// <summary>True when every sampled pixel of the picture is (almost) black. False for a missing picture or a probe that failed.</summary>
        public static bool LooksBlack(Texture source)
        {
            if (source == null) return false;
            try
            {
                if (tiny == null) tiny = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { name = "Black frame probe" };
                if (read == null) read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                RenderTexture before = RenderTexture.active;
                Graphics.Blit(source, tiny);
                RenderTexture.active = tiny;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                RenderTexture.active = before;
                int brightest = 0;
                foreach (Color32 c in read.GetPixels32()) brightest = Mathf.Max(brightest, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                return brightest <= 3;
            }
            catch (System.Exception e)
            {
                { }
                return false;
            }
        }
    }
}
