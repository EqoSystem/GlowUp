using GlowUp.Common;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// New20: fast typing on the password fields must never be undone as a paste (letters vanished and came back), while a real paste
    /// of a whole password still is. PasteGuard.LooksPasted is the plain rule both password fields use.
    /// </summary>
    public class PasteGuardTests
    {
        const float NormalFrame = 1f / 30f;

        [Test]
        public void FastTypingInOneFrameIsNotAPaste()
        {
            Assert.IsFalse(PasteGuard.LooksPasted("pass", "passw", NormalFrame), "one letter");
            Assert.IsFalse(PasteGuard.LooksPasted("pass", "passwo", NormalFrame), "two letters read in one frame (the reported fault)");
            Assert.IsFalse(PasteGuard.LooksPasted("pass", "passwor", NormalFrame), "three letters read in one frame");
        }

        [Test]
        public void APasteIsStillCaught()
        {
            Assert.IsTrue(PasteGuard.LooksPasted("", "MySecretPass1", NormalFrame), "a whole password at once");
            Assert.IsTrue(PasteGuard.LooksPasted("ab", "ab123456", NormalFrame), "six characters at once");
        }

        [Test]
        public void AStallOfTheAppAllowsTheLettersTypedDuringIt()
        {
            Assert.IsFalse(PasteGuard.LooksPasted("pass", "password", 0.4f), "four letters typed during a 0.4 s stall");
            Assert.IsTrue(PasteGuard.LooksPasted("", "averyveryverylongpassword", 0.4f), "but not a long paste");
        }

        [Test]
        public void DeletingIsNeverAPaste()
        {
            Assert.IsFalse(PasteGuard.LooksPasted("password", "", NormalFrame));
            Assert.IsFalse(PasteGuard.LooksPasted(null, null, NormalFrame));
        }

        [Test]
        public void VolumeKeysLineFollowsTheChoice()
        {
            StringAssert.StartsWith("Volume + = photo capture", UiPreferences.VolumeKeysLine(true));
            StringAssert.StartsWith("Volume + = live scan", UiPreferences.VolumeKeysLine(false));
        }
    }
}
