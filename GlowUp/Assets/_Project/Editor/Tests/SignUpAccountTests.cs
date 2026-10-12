using GlowUp.Auth;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the waiting time of "Send again" on the "Confirm your email" pop-up (SignUpAccount.SecondsUntilSendAgain).
    /// Making the account and sending the message need the sign-in service and are not run by a test.
    /// </summary>
    public class SignUpAccountTests
    {
        [Test]
        public void TheMessageCanBeSentAgainAfterTheWait()
        {
            Assert.AreEqual(0, SignUpAccount.SecondsUntilSendAgain(-1f, 100f, 60f), "nothing was sent yet: it can be sent");
            Assert.AreEqual(60, SignUpAccount.SecondsUntilSendAgain(100f, 100f, 60f), "just sent");
            Assert.AreEqual(31, SignUpAccount.SecondsUntilSendAgain(100f, 129.5f, 60f), "half seconds count as a whole one, so the button never says 0 too early");
            Assert.AreEqual(1, SignUpAccount.SecondsUntilSendAgain(100f, 159.2f, 60f));
            Assert.AreEqual(0, SignUpAccount.SecondsUntilSendAgain(100f, 160f, 60f));
            Assert.AreEqual(0, SignUpAccount.SecondsUntilSendAgain(100f, 500f, 60f));
            Assert.AreEqual(0, SignUpAccount.SecondsUntilSendAgain(100f, 100f, 0f), "no wait asked for");
            Assert.AreEqual(0, SignUpAccount.SecondsUntilSendAgain(100f, 100f, -5f));
        }
    }
}
