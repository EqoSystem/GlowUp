using System;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE SHADE BEHIND A CAP. Many lipsticks, tints and compacts are scanned with the cap on, or in a case that is not
    /// see-through: the camera then sees the colour of the cap, the tube or the box, and that must never be shown as the
    /// product's shade.
    ///
    /// When the AI has seen that the cap is on and no shade can be seen (CapSeen), this class remembers the colour that
    /// was in view then: the PACK's colour. From then on it watches the colour inside the scan frame. A different clear
    /// colour that stays in view for a few pictures in a row most likely is the make-up itself: the user has taken the cap
    /// off. Look says so, once, and the scan screen then asks the AI for the shade again (or, when it may not ask any
    /// more, takes the camera's own colour).
    ///
    /// Pure logic: no camera, no Unity scene. Colours are "#RRGGBB"; "" = the picture had no clear colour.
    /// </summary>
    public class CapShadeWatch
    {
        /// <summary>A new colour counts when it was seen in this many pictures in a row.</summary>
        public int LooksToTrust = 3;
        /// <summary>Two colours are "the same" when their red, green and blue differ by no more than this in all (as ScanEvidence does).</summary>
        public int SameWithin = 90;

        /// <summary>True while the cap hides the shade: the colour in view is the pack's, not the product's.</summary>
        public bool Hidden { get; private set; }
        /// <summary>The colour that was in view when the cap was seen ("" = none clear, for example a black or silver tube).</summary>
        public string PackColour { get; private set; } = "";
        /// <summary>The new colour that Look reported last.</summary>
        public string NewColour { get; private set; } = "";

        string candidate = "";
        int run;

        /// <summary>The AI saw the cap on and no shade. `colourInView` = the clear colour inside the frame at that moment, or "".</summary>
        public void CapSeen(string colourInView)
        {
            Hidden = true;
            PackColour = colourInView ?? "";
            candidate = ""; run = 0;
        }

        /// <summary>
        /// One picture of the whole frame: its clear colour, or "". Returns true ONCE when a colour that is not the pack's
        /// has stayed in view: the product itself is probably showing now. (It can say so again later for another colour.)
        /// </summary>
        public bool Look(string colour)
        {
            if (!Hidden || string.IsNullOrEmpty(colour)) return false;       // a picture without a clear colour says nothing
            if (PackColour.Length > 0 && Same(colour, PackColour)) { candidate = ""; run = 0; return false; }
            if (candidate.Length > 0 && Same(colour, candidate)) run++;
            else { candidate = colour; run = 1; }
            if (run < Math.Max(1, LooksToTrust)) return false;
            NewColour = candidate;
            candidate = ""; run = 0;
            return true;
        }

        /// <summary>The shade is known now (or cannot be hidden by a cap after all).</summary>
        public void Solved() { Hidden = false; candidate = ""; run = 0; }

        /// <summary>A new scan.</summary>
        public void Reset() { Solved(); PackColour = ""; NewColour = ""; }

        /// <summary>Two colours a person would call the same (a camera never gives exactly the same value twice).</summary>
        public bool Same(string a, string b)
        {
            if (!TryRgb(a, out int ar, out int ag, out int ab) || !TryRgb(b, out int br, out int bg, out int bb)) return false;
            return Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb) <= SameWithin;
        }

        static bool TryRgb(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.TrimStart('#');
            if (h.Length < 6) return false;
            try
            {
                r = Convert.ToInt32(h.Substring(0, 2), 16); g = Convert.ToInt32(h.Substring(2, 2), 16); b = Convert.ToInt32(h.Substring(4, 2), 16);
                return true;
            }
            catch (FormatException) { return false; }
        }
    }
}
