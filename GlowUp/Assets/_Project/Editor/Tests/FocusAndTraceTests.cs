using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the two helpers of hold-to-scan that were added on 13 October 2026:
    ///
    ///  - FocusKeeper, the rule of the focus lock: when does it say "focus once and hold", when "focus again", and when
    ///    does it keep quiet (the lens is settling, the picture is sharp, the user wants the camera free);
    ///  - TextTracer: does it follow the block of text, ask for close looks only at small print, in every other
    ///    picture only, and put the places of a close look back into the places of the frame.
    /// </summary>
    public class FocusAndTraceTests
    {
        const bool Moving = true, Still = false, Soft = true, Sharp = false;

        // one look every sixth of a second, as on the scan screen
        static bool Looks(FocusKeeper keeper, ref float now, params bool[] soft)
        {
            bool ordered = false;
            foreach (bool s in soft) { ordered |= keeper.Look(Still, true, s, now); now += 0.17f; }
            return ordered;
        }

        // ------------------------------------------------------------------ the focus lock
        [Test]
        public void A_sharp_picture_never_locks_the_focus()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Sharp, Sharp, Sharp, Sharp, Sharp, Sharp, Sharp, Sharp, Sharp));
            Assert.IsFalse(keeper.Holding);
        }

        [Test]
        public void Losing_the_focus_twice_while_held_still_locks_it()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Soft, Sharp), "one loss is not hunting yet");
            Assert.IsTrue(Looks(keeper, ref now, Soft), "sharp, soft, sharp, soft = the camera is hunting");
            Assert.IsTrue(keeper.Holding);
            Assert.AreEqual(1, keeper.Orders);
        }

        [Test]
        public void A_picture_that_stays_soft_locks_the_focus()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            Assert.IsFalse(Looks(keeper, ref now, Soft, Soft, Soft, Soft));
            Assert.IsTrue(Looks(keeper, ref now, Soft), "five soft looks in a row: the camera does not find the focus by itself");
        }

        [Test]
        public void While_the_lens_settles_nothing_is_judged()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            Looks(keeper, ref now, Soft, Soft, Soft, Soft, Soft);            // locked now
            Assert.AreEqual(1, keeper.Orders);
            // the lens is moving for 1.2 seconds: the six soft looks of that time are its way, not a new fault
            Assert.IsFalse(Looks(keeper, ref now, Soft, Soft, Soft, Soft, Soft, Soft));
            Assert.AreEqual(1, keeper.Orders);
        }

        [Test]
        public void A_locked_focus_is_left_alone_while_the_picture_is_sharp_and_renewed_when_it_stays_soft()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            keeper.HeldFromOutside(now);                                     // the padlock button
            now += 2f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Sharp, Soft, Sharp, Soft, Soft, Sharp), "a soft look now and then changes nothing while locked");
            Assert.IsFalse(Looks(keeper, ref now, Soft, Soft, Soft));
            Assert.IsTrue(Looks(keeper, ref now, Soft), "four soft looks in a row: the distance changed, focus once more");
            Assert.IsTrue(keeper.Holding, "and it is held again");
        }

        [Test]
        public void After_a_move_two_soft_looks_are_enough_to_focus_again()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            keeper.HeldFromOutside(now);
            now += 2f;
            Looks(keeper, ref now, Sharp, Sharp);
            keeper.Look(Moving, false, false, now); now += 0.17f;
            keeper.Look(Moving, false, false, now); now += 0.17f;            // the product was turned
            Assert.IsFalse(Looks(keeper, ref now, Soft));
            Assert.IsTrue(Looks(keeper, ref now, Soft));
        }

        [Test]
        public void A_move_that_ends_sharp_does_not_touch_the_focus()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            keeper.HeldFromOutside(now);
            now += 2f;
            keeper.Look(Moving, false, false, now); now += 0.17f;
            keeper.Look(Moving, false, false, now); now += 0.17f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Soft, Sharp, Soft, Sharp));
            Assert.AreEqual(0, keeper.Orders);
        }

        [Test]
        public void Moving_the_phone_is_not_hunting()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            // sharp, soft, a move, sharp, soft: the focus was lost once before the move and once after it. Not twice in one place.
            Looks(keeper, ref now, Sharp, Soft);
            keeper.Look(Moving, false, false, now); now += 0.17f;
            keeper.Look(Moving, false, false, now); now += 0.17f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Soft));
            Assert.IsFalse(keeper.Holding);
        }

        [Test]
        public void A_plain_surface_or_a_dark_picture_says_nothing_about_the_focus()
        {
            var keeper = new FocusKeeper(); float now = 0f;
            for (int i = 0; i < 20; i++) { Assert.IsFalse(keeper.Look(Still, false, false, now)); now += 0.17f; }
            Assert.IsFalse(keeper.Holding);
        }

        [Test]
        public void When_the_user_wants_the_camera_free_it_never_locks_by_itself()
        {
            var keeper = new FocusKeeper { Automatic = false }; float now = 0f;
            Assert.IsFalse(Looks(keeper, ref now, Sharp, Soft, Sharp, Soft, Soft, Soft, Soft, Soft, Soft, Soft));
            Assert.IsFalse(keeper.Holding);
        }

        [Test]
        public void Releasing_ends_the_hold()
        {
            var keeper = new FocusKeeper { Automatic = false }; float now = 0f;
            keeper.HeldFromOutside(now);
            keeper.Released();
            now += 2f;
            Assert.IsFalse(Looks(keeper, ref now, Soft, Soft, Soft, Soft, Soft, Soft));
            Assert.IsFalse(keeper.Holding);
        }

        // ------------------------------------------------------------------ the text tracer
        // a reading of `count` lines one under the other, each `high` of the frame high, starting at `top`
        static FrameReading Lines(int count, float left, float top, float right, float high, float confidence = 0.9f)
        {
            var reading = new FrameReading();
            for (int i = 0; i < count; i++)
                reading.lines.Add(new FrameReading.Line { text = "Aqua, Glycerin, Mica", confidence = confidence, left = left, right = right,
                                                         top = top + i * high * 1.4f, bottom = top + i * high * 1.4f + high });
            return reading;
        }

        [Test]
        public void Without_text_every_picture_is_the_whole_frame()
        {
            var tracer = new TextTracer();
            for (int i = 0; i < 4; i++)
            {
                Assert.IsFalse(tracer.Next(out Rect part, out float enlarge));
                Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), part);
                Assert.AreEqual(1f, enlarge);
            }
            Assert.IsFalse(tracer.Following);
        }

        [Test]
        public void Large_letters_are_followed_but_get_no_close_look()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(3, 0.2f, 0.3f, 0.8f, 0.08f), 700f, false);     // 0.08 of 700 pixels = 56 pixels high
            Assert.IsTrue(tracer.Following);
            Assert.IsFalse(tracer.SmallPrint);
            Assert.IsFalse(tracer.Next(out _, out _));
            Assert.IsFalse(tracer.Next(out _, out _));
        }

        [Test]
        public void Small_print_gets_a_close_look_in_every_other_picture()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f), 700f, false);     // 0.02 of 700 = 14 pixels: small print
            Assert.IsTrue(tracer.SmallPrint);
            Assert.AreEqual(14f, tracer.LetterPixels, 0.01f);

            Assert.IsTrue(tracer.Next(out Rect part, out float enlarge), "the first picture after it is a close look");
            Assert.AreEqual(32f / 14f, enlarge, 0.01f, "enlarged until the letters are about 32 pixels high");
            // the block (0.2 to 0.8 across, 0.4 to 0.56 down) with its margin of 0.05
            Assert.AreEqual(0.15f, part.xMin, 0.001f); Assert.AreEqual(0.85f, part.xMax, 0.001f);
            Assert.AreEqual(0.35f, part.yMin, 0.001f); Assert.AreEqual(0.61f, part.yMax, 0.001f);

            Assert.IsFalse(tracer.Next(out part, out enlarge), "the next one is the whole frame again");
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), part);
            Assert.IsTrue(tracer.Next(out _, out _));
        }

        [Test]
        public void A_close_look_is_never_enlarged_more_than_allowed()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.01f), 700f, false);     // 7 pixels: 32 / 7 would be 4.6 times
            tracer.Next(out _, out float enlarge);
            Assert.AreEqual(2.5f, enlarge, 0.001f);
        }

        [Test]
        public void Switched_off_it_follows_the_text_but_asks_for_no_close_look()
        {
            var tracer = new TextTracer { CloseLooks = false };
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f), 700f, false);
            Assert.IsTrue(tracer.Following);
            Assert.IsFalse(tracer.Next(out _, out _));
            Assert.IsFalse(tracer.Next(out _, out _));
        }

        [Test]
        public void Lines_the_reader_is_not_sure_of_do_not_make_a_block()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f, 0.1f), 700f, false);
            Assert.IsFalse(tracer.Following);
        }

        [Test]
        public void A_headline_does_not_hide_the_small_print_under_it()
        {
            var tracer = new TextTracer();
            FrameReading reading = Lines(6, 0.2f, 0.4f, 0.8f, 0.02f);
            reading.lines.Add(new FrameReading.Line { text = "INGREDIENTS", confidence = 0.95f, left = 0.2f, right = 0.7f, top = 0.2f, bottom = 0.3f });
            tracer.Take(reading, 700f, false);
            Assert.IsTrue(tracer.SmallPrint, "the middle line decides, not the one big line");
        }

        [Test]
        public void It_lets_go_when_the_phone_moves_and_when_the_text_is_gone()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f), 700f, false);
            tracer.Moved();
            Assert.IsFalse(tracer.Following);
            Assert.IsFalse(tracer.Next(out _, out _));

            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f), 700f, false);
            tracer.Take(new FrameReading(), 700f, false);
            tracer.Take(new FrameReading(), 700f, true);
            Assert.IsTrue(tracer.Following, "two empty readings are not the end yet");
            tracer.Take(new FrameReading(), 700f, false);
            Assert.IsFalse(tracer.Following, "three in a row are");
        }

        [Test]
        public void A_close_look_keeps_the_block_where_the_whole_picture_put_it()
        {
            var tracer = new TextTracer();
            tracer.Take(Lines(6, 0.2f, 0.4f, 0.8f, 0.02f), 700f, false);
            Rect before = tracer.Block;
            tracer.Take(Lines(2, 0.4f, 0.45f, 0.6f, 0.02f), 700f, true);     // a close look read only two lines in the middle
            Assert.AreEqual(before, tracer.Block);
            Assert.IsTrue(tracer.Following);
        }

        [Test]
        public void The_places_of_a_close_look_become_places_of_the_frame()
        {
            var reading = new FrameReading { barcode = "4800000000001", barcodeLeft = 0f, barcodeTop = 0.5f, barcodeRight = 1f, barcodeBottom = 1f };
            reading.lines.Add(new FrameReading.Line { text = "Aqua", left = 0f, top = 0f, right = 0.5f, bottom = 0.25f });
            TextTracer.ToFrame(reading, Rect.MinMaxRect(0.2f, 0.4f, 0.8f, 0.6f));    // the part that was shown: 0.6 wide, 0.2 high
            FrameReading.Line line = reading.lines[0];
            Assert.AreEqual(0.2f, line.left, 0.0001f); Assert.AreEqual(0.5f, line.right, 0.0001f);
            Assert.AreEqual(0.4f, line.top, 0.0001f); Assert.AreEqual(0.45f, line.bottom, 0.0001f);
            Assert.AreEqual(0.2f, reading.barcodeLeft, 0.0001f); Assert.AreEqual(0.8f, reading.barcodeRight, 0.0001f);
            Assert.AreEqual(0.5f, reading.barcodeTop, 0.0001f); Assert.AreEqual(0.6f, reading.barcodeBottom, 0.0001f);
        }

        [Test]
        public void A_reading_of_the_whole_frame_keeps_its_places()
        {
            var reading = new FrameReading();
            reading.lines.Add(new FrameReading.Line { text = "Aqua", left = 0.1f, top = 0.2f, right = 0.5f, bottom = 0.25f });
            TextTracer.ToFrame(reading, new Rect(0f, 0f, 1f, 1f));
            Assert.AreEqual(0.1f, reading.lines[0].left, 0.0001f);
            Assert.AreEqual(0.25f, reading.lines[0].bottom, 0.0001f);
        }

        [Test]
        public void Primary_label_mode_does_not_collect_ingredient_list_text()
        {
            var reading = new FrameReading();
            reading.lines.Add(new FrameReading.Line { text = "GLOWUP", confidence = 0.95f });
            reading.lines.Add(new FrameReading.Line { text = "VELVET MATTE LIPSTICK", confidence = 0.95f });
            reading.lines.Add(new FrameReading.Line { text = "INGREDIENTS: AQUA, GLYCERIN, MICA", confidence = 0.95f });
            reading.lines.Add(new FrameReading.Line { text = "TALC, IRON OXIDES", confidence = 0.95f });
            reading.barcode = "4801234567890";

            FrameReading primary = ScanEvidence.PrimaryLabelOnly(reading);
            var evidence = new ScanEvidence();
            evidence.Add(primary, new ScanEvidence.Known { brandSeen = true }, 1f, false, false);

            Assert.AreEqual(2, primary.lines.Count);
            Assert.AreEqual(reading.barcode, primary.barcode);
            Assert.AreEqual(0, evidence.Text.Counted);
            StringAssert.Contains("VELVET MATTE LIPSTICK", evidence.FrontText);
            StringAssert.DoesNotContain("GLYCERIN", evidence.FrontText);
            Assert.AreEqual(0f, evidence.Progress(ScanZone.Ingredients));
        }

        [Test]
        public void Cosmetic_type_can_be_detected_from_primary_label_text()
        {
            Assert.AreEqual("Lipstick", ProductScope.TypeFromLabel("GLOWUP VELVET MATTE LIPSTICK"));
            Assert.AreEqual("Powder", ProductScope.TypeFromLabel("Pressed Face Powder"));
            Assert.AreEqual("", ProductScope.TypeFromLabel("GLOWUP ROSE SHADE 03"));
            // capitals never matter
            foreach (string written in new[] { "Foundation", "FOUNDATION", "foundation", "FoUnDaTiOn" }) Assert.AreEqual("Foundation", ProductScope.TypeFromLabel(written), written);
            // other spellings and plurals
            Assert.AreEqual("Foundation", ProductScope.TypeFromLabel("FOUNDATIONS"));
            Assert.AreEqual("Eyeshadow", ProductScope.TypeFromLabel("EYE SHADOW"));
            Assert.AreEqual("Blush", ProductScope.TypeFromLabel("BLUSHER"));
            Assert.AreEqual("Foundation", ProductScope.TypeFromLabel("BB CREAM"));
            // what a camera does to the word
            foreach (string read in new[] { "F0UNDATION", "FOUNDAT1ON", "FOUNDATlON", "F O U N D A T I O N", "FOUN DATION", "FOUN-\nDATION", "FOUNDATON" })
                Assert.AreEqual("Foundation", ProductScope.TypeFromLabel(read), read);
            Assert.AreEqual("Lipstick", ProductScope.TypeFromLabel("L1PSTICK"));
            Assert.AreEqual("Primer", ProductScope.TypeFromLabel("Prirner"));
            // near words that are something else stay what they are
            foreach (string other in new[] { "BROWN SUGAR", "FOUNDER EDITION", "CONCEALED ZIP", "MASCARPONE", "BLUSHING ROSE", "HIGH GLOSS FINISH", "A B C D E" })
                Assert.AreEqual("", ProductScope.TypeFromLabel(other), other);
        }
    }
}
