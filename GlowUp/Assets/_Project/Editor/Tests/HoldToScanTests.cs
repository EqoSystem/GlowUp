using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the logic of tap-to-scan without a camera: the button toggle, quality gate (FrameQuality), what tells the zones apart
    /// (ScanZones), putting the readings of many pictures together (ScanTextMerger), the ring (ScanEvidence) and its
    /// words (ScanGuidance). The ingredients used are in the app's built-in list.
    /// </summary>
    public class HoldToScanTests
    {
        [SetUp]
        public void SetUp()
        {
            IngredientKnowledge.FuzzyThreshold = 0.85f;
            IngredientStoplist.MaximumLength = 70;
            IngredientTextParser.SetNotIngredients(null);
            FrameQuality.DarkBelow = 0.14f; FrameQuality.GlareLevel = 0.97f; FrameQuality.GlareShare = 0.18f;
            FrameQuality.MovingAbove = 0.06f; FrameQuality.BlurBelow = 0.4f; FrameQuality.MinimumEdges = 0.1f;
            ScanTextMerger.SeenToCount = 2; ScanTextMerger.QuietFramesForDone = 2;
            ScanEvidence.ClearLine = 0.55f; ScanEvidence.UsableLine = 0.3f; ScanEvidence.FrontPictures = 2;
            ScanEvidence.ViewsAround = 3; ScanEvidence.NewViewBelow = 0.4f; ScanEvidence.ColourPictures = 2; ScanEvidence.MinimumIngredients = 2;
        }

        [Test]
        public void EachTapTogglesScanningOnAndOff()
        {
            var gameObject = new GameObject("TapToScanButtonTest");
            try
            {
                HoldToScanButton button = gameObject.AddComponent<HoldToScanButton>();
                int started = 0, paused = 0;
                button.ScanStarted += () => started++;
                button.ScanPaused += () => paused++;

                button.OnPointerDown(null);
                Assert.IsFalse(button.IsScanning, "scanning starts on tap release, not while the finger is down");
                Assert.AreEqual(0, started);
                button.OnPointerUp(null);
                Assert.IsTrue(button.IsScanning);
                Assert.AreEqual(1, started);

                button.OnPointerDown(null);
                button.OnPointerUp(null);
                Assert.IsFalse(button.IsScanning);
                Assert.AreEqual(1, paused);

                button.OnPointerDown(null);
                button.OnPointerUp(null);
                Assert.IsTrue(button.IsScanning, "the next tap resumes scanning");
                Assert.AreEqual(2, started);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                SwipeSceneNavigator.Suppressed = false;
            }
        }

        // ------------------------------------------------------------------------------------ pictures made of numbers
        const int Size = 96;

        /// <summary>Dark and light stripes eight pixels wide, like rows of print. `smear` = over how many pixels each edge is spread (1 = razor sharp).</summary>
        static byte[] Stripes(int smear = 1, int dark = 40, int light = 220, int shift = 0)
        {
            var sharp = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    sharp[y * Size + x] = ((x + shift) / 8) % 2 == 0 ? dark : light;
            var picture = new byte[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -(smear - 1) / 2; k <= smear / 2; k++) { int xx = x + k; if (xx < 0 || xx >= Size) continue; sum += sharp[y * Size + xx]; n++; }
                    picture[y * Size + x] = (byte)(sum / n);
                }
            return picture;
        }

        static byte[] Plain(int value)
        {
            var picture = new byte[Size * Size];
            for (int i = 0; i < picture.Length; i++) picture[i] = (byte)value;
            return picture;
        }

        static FrameQuality.Measure Measured(byte[] picture, byte[] before = null) => FrameQuality.Of(picture, Size, Size, before, picture, Size, Size);

        // ------------------------------------------------------------------------------------ the quality gate
        [Test]
        public void ASharpWellLitPictureIsAccepted()
        {
            FrameQuality.Measure m = Measured(Stripes());
            Assert.Greater(m.sharpness, 0.9f, "razor-sharp stripes");
            Assert.AreEqual(FrameQuality.Fault.None, FrameQuality.Judge(m));
        }

        [Test]
        public void AnOutOfFocusPictureIsRejected()
        {
            FrameQuality.Measure soft = Measured(Stripes(7));
            Assert.Less(soft.sharpness, 0.4f, "every edge is spread over seven pixels");
            Assert.AreEqual(FrameQuality.Fault.Blurry, FrameQuality.Judge(soft));
            Assert.Greater(Measured(Stripes(2)).sharpness, Measured(Stripes(5)).sharpness, "sharper edges score higher");
        }

        [Test]
        public void FocusDoesNotDependOnHowMuchContrastThePrintHas()
        {
            // pale grey print on white is as sharp as black print on white
            Assert.AreEqual(FrameQuality.Fault.None, FrameQuality.Judge(Measured(Stripes(1, 150, 215))));
            Assert.AreEqual(FrameQuality.Fault.Blurry, FrameQuality.Judge(Measured(Stripes(7, 150, 235))));
        }

        [Test]
        public void APlainSurfaceIsNotCalledBlurry()
        {
            FrameQuality.Measure m = Measured(Plain(128));
            Assert.Less(m.edges, 0.1f);
            Assert.AreEqual(FrameQuality.Fault.None, FrameQuality.Judge(m), "nothing to read is not the same as out of focus");
        }

        [Test]
        public void ADarkPictureIsRejectedFirst()
        {
            Assert.AreEqual(FrameQuality.Fault.Dark, FrameQuality.Judge(Measured(Stripes(1, 5, 30))));
            Assert.AreEqual(FrameQuality.Fault.Dark, FrameQuality.Judge(Measured(Plain(10))));
        }

        [Test]
        public void GlareIsRejected()
        {
            byte[] picture = Stripes();
            for (int y = 0; y < Size / 2; y++) for (int x = 0; x < Size / 2; x++) picture[y * Size + x] = 255;      // a quarter of the picture is burnt out
            FrameQuality.Measure m = Measured(picture);
            Assert.Greater(m.glare, 0.2f);
            Assert.AreEqual(FrameQuality.Fault.Glare, FrameQuality.Judge(m));
            Assert.AreEqual(FrameQuality.Fault.None, FrameQuality.Judge(Measured(Stripes(1, 40, 235))), "white paper is not glare");
        }

        [Test]
        public void APictureTakenWhileThePhoneMovesIsRejected()
        {
            byte[] first = Stripes(), moved = Stripes(1, 40, 220, 4), still = Stripes();
            Assert.AreEqual(FrameQuality.Fault.Moving, FrameQuality.Judge(Measured(moved, first)));
            Assert.AreEqual(FrameQuality.Fault.None, FrameQuality.Judge(Measured(still, first)));
            Assert.Less(Measured(first).motion, 0f, "the first picture has nothing to be compared with");
        }

        // ------------------------------------------------------------------------------------ what tells the zones apart
        [Test]
        public void CodesAreRecognised()
        {
            foreach (string text in new[] { "FDA Notification No. NN-1000014495482", "LOT 2304A", "Batch No: B23X9", "EXP 05/2027", "MFG 2026-01", "BEST BEFORE 12/27", "CPR No. DR-XY12345" })
                Assert.IsTrue(ScanZones.HasCode(text, out _), text);
            foreach (string text in new[] { "Dimethicone, Mica, Talc", "SUPERSTAY MATTE INK", "Apply evenly on the lips", "" })
                Assert.IsFalse(ScanZones.HasCode(text, out _), text);
            Assert.AreEqual("NN-1000014495482", ScanZones.RegistrationNumber("FDA PH NN 1000014495482"));
            Assert.AreEqual("", ScanZones.RegistrationNumber("LOT 2304A"));
        }

        [Test]
        public void ShadesAreRecognised()
        {
            Assert.AreEqual("05 Rosewood", ScanZones.ShadeIn("Shade: 05 Rosewood"));
            Assert.AreEqual("120 Classic Ivory", ScanZones.ShadeIn("FIT ME\n120 Classic Ivory\n30 ml"));
            Assert.AreEqual("12", ScanZones.ShadeIn("No. 12"));
            Assert.AreEqual("25 Pioneer", ScanZones.ShadeIn("SUPERSTAY #25 Pioneer"));
            Assert.AreEqual("", ScanZones.ShadeIn("Long lasting colour that stays"), "the word colour in a sentence is not a shade");
            Assert.AreEqual("", ScanZones.ShadeIn("Dimethicone, Mica, Talc"));
            Assert.AreEqual("", ScanZones.ShadeIn(""));
        }

        // ------------------------------------------------------------------------------------ putting readings together
        static FrameReading Frame(float confidence, params string[] lines)
        {
            var frame = new FrameReading();
            foreach (string line in lines) frame.lines.Add(new FrameReading.Line { text = line, confidence = confidence });
            return frame;
        }
        static FrameReading Frame(params string[] lines) => Frame(0.9f, lines);

        [Test]
        public void EachLineIsKeptOnceInItsBestReading()
        {
            var merger = new ScanTextMerger();
            Assert.AreEqual(2, merger.AddLines(Frame(0.5f, "SUPERSTAY MATTE 1NK", "Maybelline New York").lines));
            Assert.AreEqual(1, merger.AddLines(Frame(0.95f, "SUPERSTAY MATTE INK", "Liquid Lipstick").lines), "one line is new, the other is a better reading");
            Assert.AreEqual(3, merger.Lines.Count);
            Assert.AreEqual("SUPERSTAY MATTE INK", merger.Lines[0].text, "the reading the reader was surest of");
            Assert.AreEqual(2, merger.Lines[0].seen);
            StringAssert.Contains("Liquid Lipstick", merger.AllText());
        }

        [Test]
        public void ALineReadWithOneWrongLetterIsTheSameLine()
        {
            var merger = new ScanTextMerger();
            merger.AddLines(Frame(0.6f, "Dermatologist recommended brand").lines);
            Assert.AreEqual(0, merger.AddLines(Frame(0.9f, "Dermatologist recornmended brand", "Dermatologist recomnended brand").lines));
            Assert.AreEqual(1, merger.Lines.Count);
        }

        [Test]
        public void AListThatComesInPiecesIsPutTogetherInOrder()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("INGREDIENTS: Talc, Mica, Dimethicone,"));
            merger.AddIngredients(IngredientTextParser.Read("Mica, Dimethicone, Silica, Phenoxyethanol,"));
            merger.AddIngredients(IngredientTextParser.Read("Silica, Phenoxyethanol, Glycerin"));
            CollectionAssert.AreEqual(new[] { "Talc", "Mica", "Dimethicone", "Silica", "Phenoxyethanol", "Glycerin" }, merger.CountedNames());
            Assert.IsTrue(merger.AnchorSeen);
        }

        [Test]
        public void ThePiecesMayComeInAnyOrder()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("Silica, Phenoxyethanol, Glycerin"));                    // the end of the list first
            merger.AddIngredients(IngredientTextParser.Read("Dimethicone, Silica, Phenoxyethanol,"));
            merger.AddIngredients(IngredientTextParser.Read("INGREDIENTS: Talc, Mica, Dimethicone,"));
            CollectionAssert.AreEqual(new[] { "Talc", "Mica", "Dimethicone", "Silica", "Phenoxyethanol", "Glycerin" }, merger.CountedNames());
        }

        [Test]
        public void ANameTheAppDoesNotKnowCountsOnlyWhenItIsReadTwice()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("Ingredients: Talc, Zinc Stearate, Mica, Qwertyuine Blorp"));
            CollectionAssert.AreEqual(new[] { "Talc", "Mica" }, merger.CountedNames(), "read once: could be a misreading");
            Assert.AreEqual(2, merger.Waiting);
            merger.AddIngredients(IngredientTextParser.Read("Ingredients: Talc, Zinc Stearate, Mica"));
            CollectionAssert.AreEqual(new[] { "Talc", "Zinc Stearate", "Mica" }, merger.CountedNames(), "read twice: it is on the label");
            Assert.AreEqual(1, merger.Waiting, "the scrap that was read once still waits");
        }

        [Test]
        public void TwoReadingsOfOneUnknownNameAreOneName()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("Ingredients: Talc, Disteardimonium Hectorite, Mica"));
            merger.AddIngredients(IngredientTextParser.Read("Ingredients: Talc, Disteardimonium Hectonte, Mica"));        // one letter off
            CollectionAssert.AreEqual(new[] { "Talc", "Disteardimonium Hectorite", "Mica" }, merger.CountedNames());
        }

        [Test]
        public void AMisreadKnownNameIsTheKnownIngredient()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("Ingredients: Talc, D1methicone, Mica"));
            CollectionAssert.AreEqual(new[] { "Talc", "Dimethicone", "Mica" }, merger.CountedNames());
            Assert.AreEqual(3, merger.KnownCount);
        }

        [Test]
        public void TheListIsReadToItsEndWhenNothingNewComes()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("INGREDIENTS: Talc, Mica, Dimethicone, Silica"));
            float early = merger.IngredientProgress();
            Assert.Greater(early, 0f); Assert.Less(early, 1f, "it could go on");
            merger.AddIngredients(IngredientTextParser.Read("INGREDIENTS: Talc, Mica, Dimethicone, Silica, Glycerin"));
            Assert.Less(merger.IngredientProgress(), 1f, "this picture brought a new name");
            merger.AddIngredients(IngredientTextParser.Read("Mica, Dimethicone, Silica, Glycerin"));
            merger.AddIngredients(IngredientTextParser.Read("Talc, Mica, Dimethicone, Silica, Glycerin"));
            Assert.AreEqual(1f, merger.IngredientProgress(), "two pictures in a row brought nothing new");
        }

        [Test]
        public void TheMergedListReadsBackAsTheSameList()
        {
            var merger = new ScanTextMerger();
            merger.AddIngredients(IngredientTextParser.Read("INGREDIENTS: Talc, Mica, Dimethicone. MAY CONTAIN: CI 77491, CI 77891"));
            string text = merger.IngredientText();
            StringAssert.StartsWith("Ingredients: Talc, Mica, Dimethicone", text);
            IngredientTextParser.ParsedList again = IngredientTextParser.Read(text);
            CollectionAssert.AreEqual(new[] { "Talc", "Mica", "Dimethicone", "Iron Oxides", "Titanium Dioxide" }, again.ingredients);
            CollectionAssert.AreEqual(new[] { "Iron Oxides", "Titanium Dioxide" }, again.mayContain, "the colourants stay apart");
            Assert.AreEqual("", new ScanTextMerger().IngredientText());
        }

        // ------------------------------------------------------------------------------------ the ring
        [Test]
        public void AKnownProductFillsTheFrontAtOnce()
        {
            var ring = new ScanEvidence();
            ScanEvidence.Result result = ring.Add(Frame("MAYBELLINE", "SUPERSTAY MATTE INK"), new ScanEvidence.Known { productSeen = true });
            Assert.IsTrue(ring.IsFull(ScanZone.Front));
            Assert.IsTrue(result.filledNow[(int)ScanZone.Front]);
            Assert.IsTrue(result.zones[(int)ScanZone.Around], "and it is the first view of the product");
        }

        [Test]
        public void AnUnknownProductNeedsTwoClearPicturesOfItsFront()
        {
            var ring = new ScanEvidence();
            ring.Add(Frame("GLOWY BRAND", "Velvet Cloud Tint"), default);
            Assert.AreEqual(0.5f, ring.Progress(ScanZone.Front), 0.001f);
            ScanEvidence.Result second = ring.Add(Frame("GLOWY BRAND", "Velvet Cloud Tint"), default);
            Assert.IsTrue(ring.IsFull(ScanZone.Front));
            Assert.IsTrue(second.filledNow[(int)ScanZone.Front]);
            StringAssert.Contains("Velvet Cloud Tint", ring.FrontText);
        }

        [Test]
        public void TextTheReaderIsNotSureOfFillsNothing()
        {
            var ring = new ScanEvidence();
            ring.Add(Frame(0.4f, "GL0WY 8RAND", "Ve1vet C1oud"), default);
            ring.Add(Frame(0.2f, "scribble", "more scribble"), default);
            Assert.AreEqual(0f, ring.Progress(ScanZone.Front));
            Assert.AreEqual(0f, ring.Progress(ScanZone.Around));
        }

        [Test]
        public void SmallPrintIsNotTheFront()
        {
            var ring = new ScanEvidence();
            ring.Add(Frame("Directions: apply evenly on the lips", "Keep out of reach of children", "Made in France", "www.somebrand.com"), default);
            Assert.AreEqual(0f, ring.Progress(ScanZone.Front));
            // but a front with advertising words IS the front
            ring.Add(Frame("NEW", "SUPER MATTE LIP COLOUR", "24H WEAR"), new ScanEvidence.Known { brandSeen = true });
            Assert.IsTrue(ring.IsFull(ScanZone.Front), "a brand the app knows counts for two pictures");
        }

        [Test]
        public void ProductNameAndTypeSurviveInTheSameFrameAsTheIngredientList()
        {
            var ring = new ScanEvidence();
            ScanEvidence.Result result = ring.Add(
                Frame("EVER BILENA SPLASH RED", "LIP TINT", "INGREDIENTS: Aqua, Glycerin, Mica, Dimethicone"),
                new ScanEvidence.Known { brandSeen = true });

            Assert.IsTrue(result.zones[(int)ScanZone.Front]);
            StringAssert.Contains("EVER BILENA SPLASH RED", ring.FrontText);
            StringAssert.Contains("LIP TINT", ring.FrontText);
            StringAssert.DoesNotContain("INGREDIENTS", ring.FrontText);
            for (int i = 0; i < 20; i++)
                ring.Add(Frame("EVER BILENA SPLASH RED", "LIP TINT", "INGREDIENTS: Aqua, Glycerin, Mica, Dimethicone"),
                    new ScanEvidence.Known { brandSeen = true });
            CollectionAssert.AreEqual(new[] { "Aqua", "Glycerin", "Mica", "Dimethicone" }, ring.Text.CountedNames());
        }

        [Test]
        public void TheIngredientZoneGrowsAndThenFills()
        {
            var ring = new ScanEvidence();
            ScanEvidence.Result first = ring.Add(Frame("INGREDIENTS: Talc, Mica, Dimethicone,"), default);
            Assert.IsTrue(first.zones[(int)ScanZone.Ingredients]);
            Assert.IsNotNull(first.list);
            float one = ring.Progress(ScanZone.Ingredients);
            Assert.Greater(one, 0f); Assert.Less(one, 1f);
            ring.Add(Frame("Dimethicone, Silica, Phenoxyethanol, Glycerin"), default);
            Assert.Greater(ring.Progress(ScanZone.Ingredients), one, "more names, more progress");
            ring.Add(Frame("Silica, Phenoxyethanol, Glycerin"), default);
            ScanEvidence.Result last = ring.Add(Frame("Talc, Mica, Dimethicone, Silica"), default);
            Assert.IsTrue(ring.IsFull(ScanZone.Ingredients));
            Assert.IsTrue(last.filledNow[(int)ScanZone.Ingredients]);
            CollectionAssert.AreEqual(new[] { "Talc", "Mica", "Dimethicone", "Silica", "Phenoxyethanol", "Glycerin" }, ring.Text.CountedNames());
            Assert.AreEqual(0f, ring.Progress(ScanZone.Front), "an ingredient list is not the front");
        }

        [Test]
        public void ABarcodeOrACodeFillsTheCodesZone()
        {
            var ring = new ScanEvidence();
            var withBarcode = Frame("some text");
            withBarcode.barcode = "4800888141125";
            Assert.IsTrue(ring.Add(withBarcode, default).filledNow[(int)ScanZone.Codes]);
            Assert.AreEqual("4800888141125", ring.Barcode);

            var other = new ScanEvidence();
            other.Add(Frame("LOT 2304A", "EXP 05/2027", "NN-1000014495482"), default);
            Assert.IsTrue(other.IsFull(ScanZone.Codes));
            Assert.AreEqual("NN-1000014495482", other.RegistrationNumber);
            Assert.AreEqual("", other.Barcode);

            var none = new ScanEvidence();
            var short1 = Frame("text"); short1.barcode = "123";
            none.Add(short1, default);
            Assert.AreEqual(0f, none.Progress(ScanZone.Codes), "three digits are not a barcode");
        }

        [Test]
        public void TheShadeIsItsNameOrTheSameColourTwice()
        {
            var named = new ScanEvidence();
            named.Add(Frame("FIT ME", "120 Classic Ivory"), default);
            Assert.IsTrue(named.IsFull(ScanZone.Shade));
            Assert.AreEqual("120 Classic Ivory", named.ShadeText);

            var coloured = new ScanEvidence();
            coloured.Add(Frame("VELVET TINT"), new ScanEvidence.Known { colourHex = "#C24B62" });
            Assert.AreEqual(0.5f, coloured.Progress(ScanZone.Shade), 0.001f);
            coloured.Add(Frame("VELVET TINT"), new ScanEvidence.Known { colourHex = "#3050C0" });
            Assert.AreEqual(0.5f, coloured.Progress(ScanZone.Shade), 0.001f, "another colour: counted from one again");
            coloured.Add(Frame("VELVET TINT"), new ScanEvidence.Known { colourHex = "#3555BB" });
            Assert.IsTrue(coloured.IsFull(ScanZone.Shade));
            Assert.AreEqual("#3555BB", coloured.ShadeHex);

            var clear = new ScanEvidence();
            clear.Add(Frame("ALL NIGHTER SETTING SPRAY"), new ScanEvidence.Known { noShadeNeeded = true });
            Assert.IsTrue(clear.IsFull(ScanZone.Shade), "a product without a shade needs none");
        }

        [Test]
        public void ThreeDifferentViewsFillTheLastZone()
        {
            var ring = new ScanEvidence();
            ring.Add(Frame("GLOWY BRAND", "Velvet Cloud Tint"), default);
            ring.Add(Frame("GLOWY BRAND", "Velvet Cloud Tint"), default);                                   // the same view again
            Assert.AreEqual(1f / 3f, ring.Progress(ScanZone.Around), 0.001f);
            ring.Add(Frame("INGREDIENTS: Talc, Mica, Dimethicone", "Silica, Phenoxyethanol, Glycerin"), default);
            Assert.AreEqual(2f / 3f, ring.Progress(ScanZone.Around), 0.001f);
            ring.Add(Frame("LOT 2304A EXP 05/2027", "Made in France for Glowy Brand"), default);
            Assert.IsTrue(ring.IsFull(ScanZone.Around));
        }

        [Test]
        public void TheWholeRingAndWhatIsMissingNext()
        {
            var ring = new ScanEvidence();
            Assert.AreEqual(ScanZone.Front, ring.NextMissing);
            Assert.IsFalse(ring.EssentialsFull);
            ring.Add(Frame("GLOWY BRAND", "Velvet Cloud Tint", "Shade: 05 Rosewood"), new ScanEvidence.Known { productSeen = true });
            Assert.AreEqual(ScanZone.Ingredients, ring.NextMissing);
            for (int i = 0; i < 3; i++) ring.Add(Frame("INGREDIENTS: Talc, Mica,", "Dimethicone, Silica"), default);
            Assert.IsTrue(ring.EssentialsFull, "the product is named and its list is read");
            Assert.AreEqual(ScanZone.Codes, ring.NextMissing);
            var bottom = Frame("LOT 2304A", "Made in France"); bottom.barcode = "4800888141125";
            ring.Add(bottom, default);
            Assert.IsTrue(ring.AllFull, "front, ingredients, codes, shade and three views");
            Assert.AreEqual(5, ring.FullZones);
            Assert.IsNull(ring.NextMissing);

            ring.Reset();
            Assert.AreEqual(0, ring.FullZones);
            Assert.AreEqual("", ring.Barcode + ring.ShadeText + ring.FrontText);
            Assert.AreEqual(0, ring.Text.Lines.Count);
        }

        [Test]
        public void WhatTheAppKnowsAlreadyFillsZonesWithoutAPicture()
        {
            var ring = new ScanEvidence();
            Assert.IsTrue(ring.ProductKnown()[(int)ScanZone.Front]);
            Assert.IsTrue(ring.ListKnown()[(int)ScanZone.Ingredients]);
            Assert.IsTrue(ring.EssentialsFull);
            Assert.IsFalse(ring.ProductKnown()[(int)ScanZone.Front], "it only becomes full once");
        }

        // ------------------------------------------------------------------------------------ the words
        [Test]
        public void TheStatusLineAndTheHints()
        {
            var words = new ScanGuidance.Words();
            var ring = new ScanEvidence();
            Assert.AreEqual("Tap once to start scanning.", ScanGuidance.Status(words, false, false, false));
            Assert.AreEqual("Recognizing the product...", ScanGuidance.Status(words, true, true, false));
            Assert.AreEqual("Paused. Tap once to continue.", ScanGuidance.Status(words, false, true, false));
            Assert.AreEqual("Got it!", ScanGuidance.Status(words, true, true, true));

            // a fault of the picture comes before what is missing
            Assert.AreEqual(words.glare, ScanGuidance.Hint(words, FrameQuality.Fault.Glare, ring));
            Assert.AreEqual(words.moving, ScanGuidance.Hint(words, FrameQuality.Fault.Moving, ring));
            Assert.AreEqual(words.front, ScanGuidance.Hint(words, FrameQuality.Fault.None, ring));
            ring.ProductKnown();
            Assert.AreEqual(words.ingredients, ScanGuidance.Hint(words, FrameQuality.Fault.None, ring));
            ring.Add(Frame("INGREDIENTS: Talc, Mica, Dimethicone"), default);
            Assert.AreEqual(words.ingredientsMore, ScanGuidance.Hint(words, FrameQuality.Fault.None, ring));
            ring.ListKnown();
            Assert.AreEqual(words.codes, ScanGuidance.Hint(words, FrameQuality.Fault.None, ring));
        }

        // ------------------------------------------------------------------------------------ what the phone's reader hands over
        [Test]
        public void TheReadersAnswerIsUnderstood()
        {
            // one row per line of text: confidence, left, top, right, bottom, text (separated by tabs)
            string rows = "0.91\t0.10\t0.20\t0.60\t0.26\tMAYBELLINE\n"
                        + "0.42\t0.10\t0.30\t0.90\t0.36\tFit Me\tMatte\n"            // a tab inside the text stays part of the text
                        + "nonsense\n"                                             // a row that cannot be understood is left out
                        + "0.80\t0.1\t0.4\t0.5\t0.45\t   \n"                       // so is a row without text
                        + "1.7\t-0.2\t0.5\t1.4\t0.56\tSPF 22";                     // numbers outside 0..1 are pulled back into it
            FrameReading reading = FrameReader.Parse(rows, " 4800123456789 ");
            Assert.AreEqual("4800123456789", reading.barcode);
            Assert.AreEqual(3, reading.lines.Count);
            Assert.AreEqual("MAYBELLINE", reading.lines[0].text);
            Assert.AreEqual(0.91f, reading.lines[0].confidence, 0.001f);
            Assert.AreEqual("Fit Me\tMatte", reading.lines[1].text);
            Assert.AreEqual(1f, reading.lines[2].confidence, 0.001f);
            Assert.AreEqual(0f, reading.lines[2].left, 0.001f);
            Assert.AreEqual(1f, reading.lines[2].right, 0.001f);

            // nothing read: an empty reading, never null
            Assert.AreEqual(0, FrameReader.Parse(null, null).lines.Count);
            Assert.AreEqual("", FrameReader.Parse("", null).barcode);
        }

        [Test]
        public void TheOutlinesAreMeasuredFromTheBottom()
        {
            FrameReading reading = FrameReader.Parse("0.9\t0.10\t0.20\t0.60\t0.30\tMAYBELLINE\n0.2\t0.1\t0.5\t0.6\t0.6\tfaint\n0.9\t0\t0\t0\t0\tno place", "");
            UnityEngine.Rect[] boxes = FrameReader.BoxesOf(reading, 0.3f);
            Assert.AreEqual(1, boxes.Length, "a line the reader is not sure of, and a line without a place, get no outline");
            Assert.AreEqual(0.10f, boxes[0].x, 0.001f);
            Assert.AreEqual(0.70f, boxes[0].y, 0.001f, "the top edge 0.20 from the top with a height of 0.10 starts 0.70 from the bottom");
            Assert.AreEqual(0.50f, boxes[0].width, 0.001f);
            Assert.AreEqual(0.10f, boxes[0].height, 0.001f);
            Assert.AreEqual(0, FrameReader.BoxesOf(null).Length);

            // the barcode gets an outline too, when the reader says where it is
            FrameReading withCode = FrameReader.Parse("", "4800123456789\t0.2\t0.7\t0.8\t0.9");
            Assert.AreEqual("4800123456789", withCode.barcode);
            boxes = FrameReader.BoxesOf(withCode);
            Assert.AreEqual(1, boxes.Length);
            Assert.AreEqual(0.2f, boxes[0].x, 0.001f);
            Assert.AreEqual(0.1f, boxes[0].y, 0.001f);
            Assert.AreEqual(0.6f, boxes[0].width, 0.001f);
            Assert.AreEqual(0.2f, boxes[0].height, 0.001f);
            Assert.AreEqual(0, FrameReader.BoxesOf(FrameReader.Parse("", "4800123456789")).Length, "digits without a place: no outline");
        }

        [Test]
        public void ARuleChangeReachesTheScanner()
        {
            // the Scan Rules asset hands its numbers to the quality gate and the ring
            ScanRules rules = UnityEngine.ScriptableObject.CreateInstance<ScanRules>();
            try
            {
                rules.pictureDarkBelow = 0.2f; rules.frontPictures = 3; rules.viewsAround = 4; rules.picturesForAnUnknownName = 3;
                rules.Apply();
                Assert.AreEqual(0.2f, FrameQuality.DarkBelow, 0.0001f);
                Assert.AreEqual(3, ScanEvidence.FrontPictures);
                Assert.AreEqual(4, ScanEvidence.ViewsAround);
                Assert.AreEqual(3, ScanTextMerger.SeenToCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
                ScanRules.Current.Apply();           // the numbers in force again, for the tests that follow
            }
        }
        [Test]
        public void The_hint_follows_the_ring_of_five()
        {
            var words = new ScanGuidance.Words();
            var ring = new ScanGuidance.Ring();
            Assert.AreEqual(words.front, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring), "nothing read: show the front");
            Assert.AreEqual(words.glare, ScanGuidance.HintForRing(words, FrameQuality.Fault.Glare, ring), "a fault of the picture comes first");
            ring.name = true;
            Assert.AreEqual(words.brandMissing, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            ring.brand = true; ring.name = false;
            Assert.AreEqual(words.nameMissing, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            ring.name = true;
            Assert.AreEqual(words.kindMissing, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            ring.kind = true;
            Assert.AreEqual(words.ingredients, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            ring.list = 0.4f;
            Assert.AreEqual(words.ingredientsMore, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            ring.list = 1f;
            Assert.AreEqual(words.barcodeOptional, ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring), "the barcode comes last and is optional");
            ring.barcode = true;
            Assert.AreEqual("", ScanGuidance.HintForRing(words, FrameQuality.Fault.None, ring));
            // a hint never mentions the shade or the other sides: the ring does not show them
            foreach (string hint in new[] { words.front, words.brandMissing, words.nameMissing, words.kindMissing, words.ingredients, words.ingredientsMore, words.barcodeOptional })
                StringAssert.DoesNotContain("shade", hint.ToLowerInvariant());
        }

    }
}
