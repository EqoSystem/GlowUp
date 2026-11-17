using GlowUp.Common;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>The rule that lets the phone close a camera before another screen opens it (17 Nov 2026, AR Try-On black screen).</summary>
    public class CameraHandoverTests
    {
        [Test]
        public void AfterACameraIsLetGoTheNextUserWaitsAtMostTheSettleTime()
        {
            CameraHandover.Released();
            float left = CameraHandover.WaitLeft();
            Assert.Greater(left, 0f, "right after a release the camera is not free yet");
            Assert.LessOrEqual(left, CameraHandover.SettleSeconds, "and the wait is never longer than the settle time");
        }

        [Test]
        public void TheWaitIsOneSecondAtMost()
        {
            Assert.LessOrEqual(CameraHandover.SettleSeconds, 1.5f, "a longer wait would make AR Try-On feel stuck");
        }
    }
}
