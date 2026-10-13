using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// THE RING of hold-to-scan, as numbers. It collects what the camera pictures of ONE product show and says how
    /// full each of the five zones is (0 to 1):
    ///
    ///   Front        the brand and the product's name. Full at once when the app knows the product from its name;
    ///                otherwise after two clear pictures of a side with text that is not small print.
    ///   Ingredients  grows with every ingredient name that counts (see ScanTextMerger); full when the list was read
    ///                to its end.
    ///   Codes        a barcode, a registration number (FDA Philippines "NN-...") or a batch / expiry code.
    ///   Shade        the shade's name or number, or a clear colour seen in two pictures. Full at once for a product
    ///                that has no shade (the caller says so).
    ///   Around       three clearly different views of the product (the user turned it).
    ///
    /// A picture belongs to a zone because of what is IN it, never because of how the phone is held, and one picture
    /// can serve several zones (the back of a box often shows the list, the barcode and the shade).
    ///
    /// Releasing the button only pauses: nothing here is forgotten until Reset() is called.
    /// Pure logic: no camera, no Unity scene. The numbers below are set from the Scan Rules asset.
    /// </summary>
    public class ScanEvidence
    {
        /// <summary>A line of text counts as clear when the text reader is at least this sure of it (0 to 1).</summary>
        public static float ClearLine = 0.55f;
        /// <summary>Lines the reader is less sure of than this are not used at all.</summary>
        public static float UsableLine = 0.3f;
        /// <summary>Clear pictures of the front that fill the Front zone when the product is not known by name.</summary>
        public static int FrontPictures = 2;
        /// <summary>Different views that fill the Around zone.</summary>
        public static int ViewsAround = 3;
        /// <summary>A picture is a new view when fewer than this share of its lines were read before.</summary>
        public static float NewViewBelow = 0.4f;
        /// <summary>Pictures with the same clear colour that fill the Shade zone when no shade name is printed.</summary>
        public static int ColourPictures = 2;
        /// <summary>Ingredient names that must count before the Ingredients zone can be full.</summary>
        public static int MinimumIngredients = 2;

        /// <summary>What the caller (the scan screen) found in the picture with the app's own lists.</summary>
        public struct Known
        {
            /// <summary>A brand the app knows stands in the picture.</summary>
            public bool brandSeen;
            /// <summary>A product the app knows stands in the picture (its whole name was read).</summary>
            public bool productSeen;
            /// <summary>The strongest real colour in the picture as hex ("" = none).</summary>
            public string colourHex;
            /// <summary>The product is of a kind that has no shade (a setting spray, a clear brow gel): the Shade zone needs nothing.</summary>
            public bool noShadeNeeded;
        }

        /// <summary>What one picture did for the ring.</summary>
        public class Result
        {
            /// <summary>The zones this picture showed.</summary>
            public readonly bool[] zones = new bool[ScanZones.Count];
            /// <summary>The zones that became full because of this picture.</summary>
            public readonly bool[] filledNow = new bool[ScanZones.Count];
            /// <summary>True when the picture added anything at all.</summary>
            public bool useful;
            /// <summary>How good the picture is as a picture OF its zones: sharpness times how sure the reader was. Used to keep the best picture of each zone.</summary>
            public float score;
            /// <summary>What the ingredient reader made of this picture (null when it showed no list).</summary>
            public IngredientTextParser.ParsedList list;
        }

        readonly float[] progress = new float[ScanZones.Count];
        readonly ScanTextMerger merger = new ScanTextMerger();
        int frontPictures, views, colourPictures;
        string lastColour = "";

        /// <summary>The readings put together: every line in its best reading, and the ingredient list.</summary>
        public ScanTextMerger Text => merger;
        /// <summary>The digits under the barcode, once it was read ("" until then).</summary>
        public string Barcode { get; private set; } = "";
        /// <summary>A registration number, batch code or date that was read ("" = none).</summary>
        public string Code { get; private set; } = "";
        /// <summary>The FDA Philippines notification number printed on the pack ("NN-..."), or "".</summary>
        public string RegistrationNumber { get; private set; } = "";
        /// <summary>The shade's name or number as printed ("" = not read).</summary>
        public string ShadeText { get; private set; } = "";
        /// <summary>The colour that was seen in several pictures, as hex ("" = none).</summary>
        public string ShadeHex { get; private set; } = "";
        /// <summary>The text of the pictures that showed the front (and of every side that is not the ingredient list): what the product is recognised from.</summary>
        public string FrontText => frontText.ToString();
        readonly System.Text.StringBuilder frontText = new System.Text.StringBuilder();
        /// <summary>How many pictures were taken in.</summary>
        public int Pictures { get; private set; }

        public float Progress(ScanZone zone) => progress[(int)zone];
        public bool IsFull(ScanZone zone) => progress[(int)zone] >= 1f;
        /// <summary>How many of the five zones are full.</summary>
        public int FullZones { get { int n = 0; foreach (float p in progress) if (p >= 1f) n++; return n; } }
        public bool AllFull => FullZones == ScanZones.Count;
        /// <summary>The two zones a scan cannot do without are full: the product can be named and its ingredients are read.</summary>
        public bool EssentialsFull => IsFull(ScanZone.Front) && IsFull(ScanZone.Ingredients);
        /// <summary>The first zone that is not full yet, in the order the user is asked for them (null = all full).</summary>
        public ScanZone? NextMissing
        {
            get
            {
                foreach (ScanZone zone in new[] { ScanZone.Front, ScanZone.Ingredients, ScanZone.Codes, ScanZone.Shade, ScanZone.Around })
                    if (!IsFull(zone)) return zone;
                return null;
            }
        }

        /// <summary>Forgets everything: a new product, or the same one from the start.</summary>
        public void Reset()
        {
            Array.Clear(progress, 0, progress.Length);
            merger.Clear();
            frontPictures = views = colourPictures = 0;
            lastColour = ""; Barcode = Code = RegistrationNumber = ShadeText = ShadeHex = "";
            frontText.Length = 0; Pictures = 0;
        }

        /// <summary>The product became known by name (a barcode, the app's product list, the look of the pack): the Front zone is full.</summary>
        public bool[] ProductKnown()
        {
            var filled = new bool[ScanZones.Count];
            Raise(ScanZone.Front, 1f, filled);
            return filled;
        }

        /// <summary>The ingredient list is known without reading it (the product is in the inventory with its list): the Ingredients zone is full.</summary>
        public bool[] ListKnown()
        {
            var filled = new bool[ScanZones.Count];
            Raise(ScanZone.Ingredients, 1f, filled);
            return filled;
        }

        /// <summary>
        /// Takes in one picture that passed the quality gate. `sharpness` = how crisp it is (FrameQuality), for choosing the best picture of a zone.
        /// `closeLook` = the picture shows only one block of the frame, enlarged (the text tracer): what it reads counts like
        /// any other reading, but it is never taken for another side of the product.
        /// </summary>
        public Result Add(FrameReading frame, Known known, float sharpness = 1f, bool closeLook = false)
        {
            var result = new Result();
            if (frame == null) return result;
            Pictures++;

            // the lines the reader is sure enough of
            var usable = new List<FrameReading.Line>(); var clearLines = new List<FrameReading.Line>(); float sure = 0f;
            foreach (FrameReading.Line line in frame.lines)
            {
                if (line == null || line.confidence < UsableLine || string.IsNullOrWhiteSpace(line.text)) continue;
                usable.Add(line); sure += line.confidence;
                if (line.confidence >= ClearLine && Letters(line.text) >= 3) clearLines.Add(line);
            }
            int clear = clearLines.Count;
            string text = frame.Text(UsableLine);
            result.score = Math.Max(0.05f, sharpness) * (usable.Count > 0 ? sure / usable.Count : 0.2f);

            // ---- Around: is this a view that was not seen before? (asked before the lines are added; only clear lines
            //      count, so a picture of misread scraps is never taken for a new side of the product)
            if (clear >= 2 && !closeLook)
            {
                int already = merger.KnownAmong(clearLines);
                if (already < clear * NewViewBelow)
                {
                    views++;
                    result.zones[(int)ScanZone.Around] = true;
                    Raise(ScanZone.Around, views / (float)Math.Max(1, ViewsAround), result.filledNow);
                }
            }
            if (merger.AddLines(usable) > 0) result.useful = true;

            // ---- Ingredients: the word "Ingredients", or a row of names with commas
            IngredientTextParser.ParsedList list = text.Length > 0 ? IngredientTextParser.Read(text) : null;
            bool isList = list != null && (list.ingredients.Count >= 2 || (list.ingredients.Count >= 1 && list.Count >= 5 && IngredientTextParser.HasListShape(text))
                                           || (list.anchorFound && list.Count >= 1));
            if (isList)
            {
                result.list = list;
                result.zones[(int)ScanZone.Ingredients] = true;
                if (merger.AddIngredients(list) > 0) result.useful = true;
                Raise(ScanZone.Ingredients, merger.IngredientProgress(MinimumIngredients), result.filledNow);
            }

            // ---- Codes: a barcode, a registration number, a batch code
            string barcode = ProductIndex.BarcodeKey(frame.barcode).Length > 0 ? frame.barcode.Trim() : "";
            if (barcode.Length > 0 && Barcode.Length == 0) { Barcode = barcode; result.useful = true; }
            if (ScanZones.HasCode(text, out string code) && Code.Length == 0) { Code = code; result.useful = true; }
            string registration = ScanZones.RegistrationNumber(text);
            if (registration.Length > 0 && RegistrationNumber.Length == 0) RegistrationNumber = registration;
            if (barcode.Length > 0 || code.Length > 0)
            {
                result.zones[(int)ScanZone.Codes] = true;
                Raise(ScanZone.Codes, 1f, result.filledNow);
            }

            // ---- Front: the brand or the product's name; or clear text that is neither a list nor small print
            bool smallPrint = isList || MostlySmallPrint(usable);
            if (known.productSeen)
            {
                result.zones[(int)ScanZone.Front] = true;
                Raise(ScanZone.Front, 1f, result.filledNow);
            }
            else if (!smallPrint && (known.brandSeen || clear >= 1))
            {
                frontPictures += known.brandSeen ? 2 : 1;              // a brand the app knows is as good as two pictures
                result.zones[(int)ScanZone.Front] = true;
                Raise(ScanZone.Front, frontPictures / (float)Math.Max(1, FrontPictures), result.filledNow);
            }
            if (!isList && text.Length > 0) { frontText.Append(text); if (frontText.Length > 6000) frontText.Remove(0, frontText.Length - 6000); }

            // ---- Shade: its name or number, or the same clear colour twice
            string shade = isList ? "" : ScanZones.ShadeIn(text);
            if (shade.Length > 0 && ShadeText.Length == 0) { ShadeText = shade; result.useful = true; }
            if (known.noShadeNeeded || shade.Length > 0 || ShadeText.Length > 0)
            {
                if (shade.Length > 0) result.zones[(int)ScanZone.Shade] = true;
                Raise(ScanZone.Shade, 1f, result.filledNow);
            }
            else if (!string.IsNullOrEmpty(known.colourHex) && !isList)
            {
                colourPictures = SameColour(known.colourHex, lastColour) ? colourPictures + 1 : 1;
                lastColour = known.colourHex;
                if (colourPictures >= ColourPictures) ShadeHex = known.colourHex;
                result.zones[(int)ScanZone.Shade] = true;
                Raise(ScanZone.Shade, colourPictures / (float)Math.Max(1, ColourPictures), result.filledNow);
            }

            foreach (bool filled in result.filledNow) if (filled) result.useful = true;
            return result;
        }

        void Raise(ScanZone zone, float value, bool[] filledNow)
        {
            int i = (int)zone;
            bool wasFull = progress[i] >= 1f;
            progress[i] = Math.Max(progress[i], Math.Min(1f, value));
            if (!wasFull && progress[i] >= 1f && filledNow != null) filledNow[i] = true;
        }

        static int Letters(string text) { int n = 0; foreach (char c in text) if (char.IsLetter(c)) n++; return n; }

        /// <summary>Directions, warnings, addresses, codes: the back or the bottom of a pack, not its front.</summary>
        static bool MostlySmallPrint(List<FrameReading.Line> lines)
        {
            if (lines.Count == 0) return false;
            int small = 0;
            foreach (FrameReading.Line line in lines) if (IngredientStoplist.SmallPrintHit(line.text) != null || IngredientStoplist.WrongShape(line.text)) small++;
            return small * 2 > lines.Count;
        }

        /// <summary>Two colours that a person would call the same (the camera never gives exactly the same value twice).</summary>
        static bool SameColour(string a, string b)
        {
            if (!TryRgb(a, out int ar, out int ag, out int ab) || !TryRgb(b, out int br, out int bg, out int bb)) return false;
            return Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb) <= 90;
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
