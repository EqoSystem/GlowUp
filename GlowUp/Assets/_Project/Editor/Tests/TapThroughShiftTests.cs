using GlowUp.Common;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>The rule for a tap that the page slid away from under the finger (keyboard closing, 17 Nov 2026: Sign Up did not respond).</summary>
    public class TapThroughShiftTests
    {
        [Test] public void AQuickPressThatDidNotMoveAndGotNoClickIsATap() => Assert.IsTrue(TapThroughShift.IsShiftedTap(0.12f, 2f, false));
        [Test] public void ANormalClickIsLeftAlone() => Assert.IsFalse(TapThroughShift.IsShiftedTap(0.12f, 0f, true));
        [Test] public void ADragIsNotATap() => Assert.IsFalse(TapThroughShift.IsShiftedTap(0.2f, 120f, false));
        [Test] public void ALongPressIsNotATap() => Assert.IsFalse(TapThroughShift.IsShiftedTap(1.5f, 1f, false));
    }
}
