using GlowUp.Common;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the rules of skeleton loading (SkeletonLook) without a screen: when the bars may give way to the words, how the
    /// shine travels, and how a bar is cut at the edge of a list's window.
    /// </summary>
    public class SkeletonLoadingTests
    {
        const float Shortest = 0.35f, Longest = 8f;

        // ------------------------------------------------------------------------------------ when the bars may go
        [Test]
        public void The_bars_never_go_in_the_first_two_frames()
        {
            // every script of the screen must have had the chance to say it is fetching
            Assert.IsFalse(SkeletonLook.MayEnd(5f, 0, 0, 0f, Longest));
            Assert.IsFalse(SkeletonLook.MayEnd(5f, 1, 0, 0f, Longest));
            Assert.IsTrue(SkeletonLook.MayEnd(5f, 2, 0, 0f, Longest));
        }

        [Test]
        public void The_bars_stay_while_a_script_is_still_fetching()
        {
            Assert.IsFalse(SkeletonLook.MayEnd(2f, 60, 1, Shortest, Longest));
            Assert.IsFalse(SkeletonLook.MayEnd(2f, 60, 3, Shortest, Longest));
            Assert.IsTrue(SkeletonLook.MayEnd(2f, 60, 0, Shortest, Longest));
        }

        [Test]
        public void The_bars_do_not_flash_when_the_data_is_there_at_once()
        {
            Assert.IsFalse(SkeletonLook.MayEnd(0.1f, 6, 0, Shortest, Longest), "nothing is fetched, but the bars were only seen for a tenth of a second");
            Assert.IsTrue(SkeletonLook.MayEnd(0.36f, 20, 0, Shortest, Longest));
        }

        [Test]
        public void With_no_shortest_time_the_bars_only_show_while_something_is_fetched()
        {
            Assert.IsTrue(SkeletonLook.MayEnd(0.02f, 2, 0, 0f, Longest));
            Assert.IsFalse(SkeletonLook.MayEnd(0.02f, 2, 1, 0f, Longest));
        }

        [Test]
        public void The_time_limit_shows_the_words_even_when_a_fetch_never_ends()
        {
            Assert.IsFalse(SkeletonLook.MayEnd(7.9f, 400, 1, Shortest, Longest));
            Assert.IsTrue(SkeletonLook.MayEnd(8f, 400, 1, Shortest, Longest));
            Assert.IsFalse(SkeletonLook.MayEnd(100f, 400, 1, Shortest, 0f), "0 = no time limit");
        }

        // ------------------------------------------------------------------------------------ the shine
        [Test]
        public void The_shine_is_full_in_its_middle_and_gone_outside_its_width()
        {
            // half-way through a pass the middle of the shine is in the middle of the screen
            Assert.AreEqual(1f, SkeletonLook.ShineAt(0.5f, 0.5f, 0.75f, 1.5f, 0.6f, 0f), 0.001f);
            Assert.AreEqual(0f, SkeletonLook.ShineAt(0.5f + 0.6f, 0.5f, 0.75f, 1.5f, 0.6f, 0f), 0.001f);
            Assert.AreEqual(0f, SkeletonLook.ShineAt(0.5f - 0.6f, 0.5f, 0.75f, 1.5f, 0.6f, 0f), 0.001f);
            float half = SkeletonLook.ShineAt(0.5f + 0.3f, 0.5f, 0.75f, 1.5f, 0.6f, 0f);
            Assert.IsTrue(half > 0.3f && half < 0.7f, "soft edges: half-way out the shine is about half as strong, got " + half);
        }

        [Test]
        public void The_shine_travels_from_left_to_right()
        {
            // early in a pass it is stronger on the left, late in a pass on the right
            Assert.Greater(SkeletonLook.ShineAt(0.1f, 0.5f, 0.45f, 1.5f, 0.6f, 0f), SkeletonLook.ShineAt(0.9f, 0.5f, 0.45f, 1.5f, 0.6f, 0f));
            Assert.Greater(SkeletonLook.ShineAt(0.9f, 0.5f, 1.05f, 1.5f, 0.6f, 0f), SkeletonLook.ShineAt(0.1f, 0.5f, 1.05f, 1.5f, 0.6f, 0f));
        }

        [Test]
        public void A_pass_begins_and_ends_with_the_shine_off_the_screen()
        {
            // the middle of the shine starts one width left of the screen: the left edge is just touched, the rest is at rest
            Assert.AreEqual(0f, SkeletonLook.ShineAt(0f, 0.5f, 0f, 1.5f, 0.6f, 0f), 0.001f);
            Assert.AreEqual(0f, SkeletonLook.ShineAt(0.5f, 0.5f, 0f, 1.5f, 0.6f, 0f), 0.001f);
            Assert.AreEqual(0f, SkeletonLook.ShineAt(1f, 0.5f, 1.4999f, 1.5f, 0.6f, 0f), 0.01f);
        }

        [Test]
        public void The_shine_repeats_with_every_pass()
        {
            for (float across = 0f; across <= 1f; across += 0.25f)
                Assert.AreEqual(SkeletonLook.ShineAt(across, 0.3f, 0.4f), SkeletonLook.ShineAt(across, 0.3f, 0.4f + 1.5f * 7f), 0.01f);
        }

        // ------------------------------------------------------------------------------------ a bar at the edge of a list
        [Test]
        public void A_bar_inside_the_window_is_left_whole()
        {
            Rect bar = new Rect(20, 30, 200, 24), window = new Rect(0, 0, 400, 300);
            Assert.AreEqual(bar, SkeletonLook.Inside(bar, window));
        }

        [Test]
        public void A_bar_that_sticks_out_is_cut_at_the_edge()
        {
            Rect cut = SkeletonLook.Inside(new Rect(300, 290, 200, 24), new Rect(0, 0, 400, 300));
            Assert.AreEqual(100f, cut.width, 0.001f);
            Assert.AreEqual(10f, cut.height, 0.001f);
        }

        [Test]
        public void A_bar_outside_the_window_is_gone()
        {
            Rect cut = SkeletonLook.Inside(new Rect(20, 400, 200, 24), new Rect(0, 0, 400, 300));
            Assert.IsTrue(cut.width <= 0f || cut.height <= 0f);
        }
    }
}
