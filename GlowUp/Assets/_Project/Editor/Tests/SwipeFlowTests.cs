using GlowUp.Common;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks where a sideways drag leads (SwipeSceneNavigator.Neighbour), as the project owner DREW it on 13 October 2026.
    /// The screens stand in the order of the bottom bar (Recent | the dashboard | Try On) and the page follows the finger
    /// like a page being turned, so a drag pulls in the screen from the other side:
    ///
    ///   on the dashboard:   swipe RIGHT = Recent Scans,   swipe LEFT = AR Try-On
    ///   on Recent Scans:    swipe LEFT  = back to the dashboard
    ///   on AR Try-On:       swipe RIGHT = back to the dashboard
    ///
    /// The scanner and Settings are left with their back button only.
    /// </summary>
    public class SwipeFlowTests
    {
        const bool Left = false, Right = true;

        static string Leads(string from, bool dragToTheRight)
        {
            string[] row = SwipeSceneNavigator.DefaultRow;
            int target = SwipeSceneNavigator.Neighbour(row, from, dragToTheRight);
            return target < 0 ? null : row[target];
        }

        [Test]
        public void On_the_dashboard_a_swipe_to_the_right_opens_Recent_Scans()
        {
            Assert.AreEqual(SceneNames.RecentScans, Leads(SceneNames.Dashboard, Right));
        }

        [Test]
        public void On_the_dashboard_a_swipe_to_the_left_opens_AR_Try_On()
        {
            Assert.AreEqual(SceneNames.ArTryOn, Leads(SceneNames.Dashboard, Left));
        }

        [Test]
        public void From_Recent_Scans_a_swipe_to_the_left_returns_to_the_dashboard()
        {
            Assert.AreEqual(SceneNames.Dashboard, Leads(SceneNames.RecentScans, Left));
            Assert.IsNull(Leads(SceneNames.RecentScans, Right), "Recent Scans is the first screen: nothing comes in from its left");
        }

        [Test]
        public void From_AR_Try_On_a_swipe_to_the_right_returns_to_the_dashboard()
        {
            Assert.AreEqual(SceneNames.Dashboard, Leads(SceneNames.ArTryOn, Right));
            Assert.IsNull(Leads(SceneNames.ArTryOn, Left), "Try On is the last screen: nothing comes in from its right");
        }

        [Test]
        public void The_scanner_and_Settings_have_no_sideways_swipe()
        {
            foreach (string screen in new[] { SceneNames.AiSmartAnalysis, SceneNames.Settings, SceneNames.AiResult, SceneNames.Login })
            {
                Assert.IsNull(Leads(screen, Left), screen);
                Assert.IsNull(Leads(screen, Right), screen);
            }
        }

        [Test]
        public void No_swipe_ever_lands_in_the_scanner()
        {
            CollectionAssert.DoesNotContain(SwipeSceneNavigator.DefaultRow, SceneNames.AiSmartAnalysis);
        }

        [Test]
        public void The_row_is_in_the_order_of_the_bottom_bar()
        {
            CollectionAssert.AreEqual(new[] { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.ArTryOn }, SwipeSceneNavigator.DefaultRow);
        }

        [Test]
        public void The_switch_turned_off_swaps_the_two_directions()
        {
            string[] row = SwipeSceneNavigator.DefaultRow;
            Assert.AreEqual(SceneNames.ArTryOn, row[SwipeSceneNavigator.Neighbour(row, SceneNames.Dashboard, Right, false)]);
            Assert.AreEqual(SceneNames.RecentScans, row[SwipeSceneNavigator.Neighbour(row, SceneNames.Dashboard, Left, false)]);
        }
    }
}
