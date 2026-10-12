using GlowUp.Common;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks where a sideways drag leads (SwipeSceneNavigator.Neighbour), as the project owner set it on 13 October 2026:
    /// the screens stand in the order of the bottom bar (Recent | the dashboard | Try On) and a drag goes to the screen on
    /// that side. The scanner and Settings are left with their back button only.
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
        public void On_the_dashboard_left_is_Recent_Scans_and_right_is_AR_Try_On()
        {
            Assert.AreEqual(SceneNames.RecentScans, Leads(SceneNames.Dashboard, Left));
            Assert.AreEqual(SceneNames.ArTryOn, Leads(SceneNames.Dashboard, Right));
        }

        [Test]
        public void From_AR_Try_On_a_drag_to_the_left_returns_to_the_dashboard()
        {
            Assert.AreEqual(SceneNames.Dashboard, Leads(SceneNames.ArTryOn, Left));
            Assert.IsNull(Leads(SceneNames.ArTryOn, Right), "there is no screen to the right of Try On");
        }

        [Test]
        public void From_Recent_Scans_a_drag_to_the_right_returns_to_the_dashboard()
        {
            Assert.AreEqual(SceneNames.Dashboard, Leads(SceneNames.RecentScans, Right));
            Assert.IsNull(Leads(SceneNames.RecentScans, Left), "there is no screen to the left of Recent Scans");
        }

        [Test]
        public void The_scanner_and_Settings_have_no_sideways_drag()
        {
            foreach (string screen in new[] { SceneNames.AiSmartAnalysis, SceneNames.Settings, SceneNames.AiResult, SceneNames.Login })
            {
                Assert.IsNull(Leads(screen, Left), screen);
                Assert.IsNull(Leads(screen, Right), screen);
            }
        }

        [Test]
        public void No_drag_ever_lands_in_the_scanner()
        {
            CollectionAssert.DoesNotContain(SwipeSceneNavigator.DefaultRow, SceneNames.AiSmartAnalysis);
        }

        [Test]
        public void The_row_is_in_the_order_of_the_bottom_bar()
        {
            CollectionAssert.AreEqual(new[] { SceneNames.RecentScans, SceneNames.Dashboard, SceneNames.ArTryOn }, SwipeSceneNavigator.DefaultRow);
        }
    }
}
