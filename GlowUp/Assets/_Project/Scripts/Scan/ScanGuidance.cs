namespace GlowUp.Scan
{
    /// <summary>
    /// The words of hold-to-scan: the short status line ("Holding, 2 of 5") and the hint that reacts to what is
    /// missing or wrong ("Turn to the back", "Tilt slightly, there is glare", "Hold steadier").
    /// Pure logic, so the wording can be tested; the scan screen can replace every text in its Inspector.
    /// </summary>
    public static class ScanGuidance
    {
        /// <summary>The texts. The scan screen fills this from its Inspector fields.</summary>
        public class Words
        {
            public string holding = "Holding, {0} of 5";
            public string paused = "Paused at {0} of 5. Hold the button to go on.";
            public string notStarted = "Hold the button and show the product.";
            public string done = "Got it!";
            // what is wrong with the picture
            public string dark = "It is too dark. Turn on the flashlight or move to the light.";
            public string moving = "Hold steadier";
            public string glare = "Tilt slightly, there is glare";
            public string blurry = "Move a little back so the text is sharp";
            // what is still missing, in the order it is asked for
            public string front = "Show the front: the brand and the product's name";
            public string ingredients = "Turn to the back: show the ingredient list";
            public string ingredientsMore = "Keep the list in view, moving slowly along it";
            public string codes = "Show the barcode (on the bottom or the back)";
            public string shade = "Show the shade's name or number";
            public string around = "Turn the product slowly to show its other sides";
        }

        /// <summary>The status line. `held` = the button is down now; `started` = the ring holds anything at all.</summary>
        public static string Status(Words words, bool held, bool started, int fullZones, bool finished)
        {
            if (finished) return words.done;
            if (held) return string.Format(words.holding, fullZones);
            return started ? string.Format(words.paused, fullZones) : words.notStarted;
        }

        /// <summary>
        /// The hint. A fault of the picture comes first (the user must fix it before anything can be read); otherwise the
        /// first zone that is still missing. "" when nothing is missing.
        /// </summary>
        public static string Hint(Words words, FrameQuality.Fault fault, ScanEvidence evidence)
        {
            switch (fault)
            {
                case FrameQuality.Fault.Dark: return words.dark;
                case FrameQuality.Fault.Moving: return words.moving;
                case FrameQuality.Fault.Glare: return words.glare;
                case FrameQuality.Fault.Blurry: return words.blurry;
            }
            ScanZone? missing = evidence != null ? evidence.NextMissing : ScanZone.Front;
            if (missing == null) return "";
            switch (missing.Value)
            {
                case ScanZone.Front: return words.front;
                case ScanZone.Ingredients: return evidence != null && evidence.Progress(ScanZone.Ingredients) > 0f ? words.ingredientsMore : words.ingredients;
                case ScanZone.Codes: return words.codes;
                case ScanZone.Shade: return words.shade;
                default: return words.around;
            }
        }
    }
}
