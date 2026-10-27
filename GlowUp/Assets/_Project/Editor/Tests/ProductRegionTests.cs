using System.Linq;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Product detection (ProductRegion): the words on the product are told apart from the words of things around it.
    /// The first case is laid out like the test photo of 27 October 2026: a Maybelline foundation lying on a laptop, whose
    /// badge "Faster Data Transit" used to be taken for the product's name.
    /// </summary>
    public class ProductRegionTests
    {
        // the scan frame of that photo in screen pixels: left 125, top 255, 653 wide, 930 high
        const float FrameLeft = 125f, FrameTop = 255f, FrameWide = 653f, FrameHigh = 930f;
        const float Aspect = FrameWide / FrameHigh;

        static FrameReading.Line Px(string text, float x1, float y1, float x2, float y2, float confidence = 0.9f) => new FrameReading.Line
        {
            text = text, confidence = confidence,
            left = Mathf.Clamp01((x1 - FrameLeft) / FrameWide), right = Mathf.Clamp01((x2 - FrameLeft) / FrameWide),
            top = Mathf.Clamp01((y1 - FrameTop) / FrameHigh), bottom = Mathf.Clamp01((y2 - FrameTop) / FrameHigh)
        };

        static bool Maybelline(string words) => words.IndexOf("MAYBELLINE", System.StringComparison.OrdinalIgnoreCase) >= 0;

        static FrameReading Photo()
        {
            var reading = new FrameReading();
            // the bottle (its words are turned sideways)
            reading.lines.Add(Px("SUPER", 195, 525, 262, 655));
            reading.lines.Add(Px("STAY", 262, 525, 330, 655));
            reading.lines.Add(Px("MAYBELLINE NEW YORK", 180, 700, 215, 830));
            reading.lines.Add(Px("UP TO 30H", 215, 840, 305, 905));
            reading.lines.Add(Px("LUMI-MATTE FOUNDATION", 215, 912, 300, 935));
            reading.lines.Add(Px("SPF 16", 262, 945, 300, 960));
            reading.lines.Add(Px("119", 262, 970, 300, 1010));
            // the laptop behind it
            reading.lines.Add(Px("NITRO V15", 518, 620, 655, 650));
            reading.lines.Add(Px("Faster Data Transit", 485, 730, 575, 745));
            reading.lines.Add(Px("Wi-Fi 6", 598, 700, 650, 712));
            reading.lines.Add(Px("Wireless Connection", 607, 730, 655, 745));
            reading.lines.Add(Px("THUNDERBOLT", 695, 685, 735, 700));
            reading.lines.Add(Px("L-ACOUSTICS", 510, 955, 690, 975));
            reading.lines.Add(Px("shift", 485, 460, 535, 475));
            reading.lines.Add(Px("enter", 700, 515, 760, 530));
            return reading;
        }

        [Test]
        public void The_words_of_the_laptop_are_not_the_words_of_the_product()
        {
            ProductRegion.Result result = ProductRegion.Find(Photo(), Aspect, Maybelline, 0.3f);

            Assert.IsTrue(result.found);
            string kept = result.reading.Text();
            StringAssert.Contains("SUPER", kept);
            StringAssert.Contains("STAY", kept);
            StringAssert.Contains("MAYBELLINE NEW YORK", kept);
            StringAssert.Contains("UP TO 30H", kept);
            StringAssert.Contains("FOUNDATION", kept);
            StringAssert.DoesNotContain("Faster Data Transit", kept);
            StringAssert.DoesNotContain("NITRO", kept);
            StringAssert.DoesNotContain("L-ACOUSTICS", kept);
            Assert.GreaterOrEqual(result.dropped, 6);
        }

        [Test]
        public void The_box_holds_every_line_of_the_product_and_none_of_the_laptop()
        {
            FrameReading photo = Photo();
            ProductRegion.Result result = ProductRegion.Find(photo, Aspect, Maybelline, 0.3f);

            Assert.IsTrue(result.found);
            foreach (FrameReading.Line line in result.reading.lines)
            {
                Rect inFrame = new Rect(line.left, 1f - line.bottom, line.right - line.left, line.bottom - line.top);
                Assert.IsTrue(result.box.Contains(inFrame.center), line.text + " is outside the product's box");
            }
            FrameReading.Line transit = photo.lines.First(l => l.text.StartsWith("Faster"));
            Assert.IsFalse(result.box.Contains(new Vector2((transit.left + transit.right) / 2f, 1f - (transit.top + transit.bottom) / 2f)));
        }

        [Test]
        public void Without_a_known_brand_the_group_with_a_make_up_kind_is_the_product()
        {
            // the same photo, and the brand is not in the app's list: "FOUNDATION" still marks the bottle
            ProductRegion.Result result = ProductRegion.Find(Photo(), Aspect, words => false, 0.3f);

            Assert.IsTrue(result.found);
            StringAssert.Contains("FOUNDATION", result.reading.Text());
            StringAssert.DoesNotContain("Faster Data Transit", result.reading.Text());
        }

        [Test]
        public void Lines_without_a_position_are_never_dropped()
        {
            var reading = new FrameReading();
            reading.lines.Add(new FrameReading.Line { text = "MAYBELLINE", confidence = 0.9f });
            reading.lines.Add(new FrameReading.Line { text = "SUPER STAY", confidence = 0.9f });
            reading.lines.Add(new FrameReading.Line { text = "NITRO V15", confidence = 0.9f });

            ProductRegion.Result result = ProductRegion.Find(reading, Aspect, Maybelline, 0.3f);

            Assert.IsFalse(result.found);
            Assert.AreEqual(3, result.reading.lines.Count);
            Assert.AreEqual(0, result.dropped);
        }

        [Test]
        public void A_picture_with_too_few_lines_is_left_as_it_is()
        {
            var reading = new FrameReading();
            reading.lines.Add(Px("MAYBELLINE", 180, 700, 215, 830));
            reading.lines.Add(Px("NITRO V15", 518, 620, 655, 650));

            ProductRegion.Result result = ProductRegion.Find(reading, Aspect, Maybelline, 0.3f);

            Assert.IsFalse(result.found);
            Assert.AreEqual(2, result.reading.lines.Count);
        }

        [Test]
        public void A_scrap_the_reader_is_unsure_of_does_not_join_two_groups()
        {
            FrameReading photo = Photo();
            // a misread scrap half way between the bottle and the laptop badge would link them if it counted
            photo.lines.Add(Px("~!", 335, 600, 480, 640, 0.1f));

            ProductRegion.Result result = ProductRegion.Find(photo, Aspect, Maybelline, 0.3f);

            Assert.IsTrue(result.found);
            StringAssert.DoesNotContain("Faster Data Transit", result.reading.Text());
        }

        [Test]
        public void The_barcode_is_kept()
        {
            FrameReading photo = Photo();
            photo.barcode = "4800000000017";
            photo.barcodeLeft = 0.1f; photo.barcodeTop = 0.9f; photo.barcodeRight = 0.3f; photo.barcodeBottom = 0.95f;

            ProductRegion.Result result = ProductRegion.Find(photo, Aspect, Maybelline, 0.3f);

            Assert.AreEqual(photo.barcode, result.reading.barcode);
            Assert.AreEqual(0.3f, result.reading.barcodeRight, 0.0001f);
        }
    }
}
