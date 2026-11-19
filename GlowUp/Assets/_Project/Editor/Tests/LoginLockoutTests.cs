using System;
using System.Collections.Generic;
using GlowUp.Auth;
using NUnit.Framework;

namespace GlowUp.EditorTools.Tests
{
    /// <summary>20 November 2026: 3 wrong passwords lock Log-In for that email for 5 minutes (same rule as the admin website).</summary>
    public class LoginLockoutTests
    {
        Dictionary<string, string> store;
        DateTime now;
        Func<DateTime> oldNow; Func<string, string> oldRead; Action<string, string> oldWrite; Action<string> oldForget;

        [SetUp]
        public void SetUp()
        {
            store = new Dictionary<string, string>();
            now = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
            oldNow = LoginLockout.Now; oldRead = LoginLockout.Read; oldWrite = LoginLockout.Write; oldForget = LoginLockout.Forget;
            LoginLockout.Now = () => now;
            LoginLockout.Read = k => store.TryGetValue(k, out string v) ? v : "";
            LoginLockout.Write = (k, v) => store[k] = v;
            LoginLockout.Forget = k => store.Remove(k);
        }

        [TearDown]
        public void TearDown() { LoginLockout.Now = oldNow; LoginLockout.Read = oldRead; LoginLockout.Write = oldWrite; LoginLockout.Forget = oldForget; }

        [Test]
        public void ThirdWrongPasswordLocksForFiveMinutes()
        {
            Assert.AreEqual(2, LoginLockout.Fail("ana@gmail.com").left);
            Assert.AreEqual(1, LoginLockout.Fail("ana@gmail.com").left);
            var r = LoginLockout.Fail("ana@gmail.com");
            Assert.AreEqual(TimeSpan.FromMinutes(5), r.locked);
            Assert.AreEqual(TimeSpan.FromMinutes(5), LoginLockout.Remaining(" ANA@gmail.com "), "case and spaces do not matter");
            Assert.AreEqual(TimeSpan.Zero, LoginLockout.Remaining("ben@gmail.com"), "another email is not locked");
        }

        [Test]
        public void LockEndsAfterFiveMinutesAndStartsFromZero()
        {
            for (int i = 0; i < 3; i++) LoginLockout.Fail("ana@gmail.com");
            now = now.AddMinutes(5).AddSeconds(1);
            Assert.AreEqual(TimeSpan.Zero, LoginLockout.Remaining("ana@gmail.com"));
            Assert.AreEqual(2, LoginLockout.Fail("ana@gmail.com").left, "after the lock the count starts again");
        }

        [Test]
        public void SuccessClearsTheCountAndEmailIsNotStored()
        {
            LoginLockout.Fail("ana@gmail.com"); LoginLockout.Fail("ana@gmail.com");
            LoginLockout.Clear("ana@gmail.com");
            Assert.AreEqual(2, LoginLockout.Fail("ana@gmail.com").left);
            foreach (var kv in store) Assert.IsFalse(kv.Key.Contains("ana") || kv.Value.Contains("ana"), "the address itself is never stored");
        }

        [Test]
        public void MinSecFormat() => Assert.AreEqual("4:05", LoginLockout.MinSec(TimeSpan.FromSeconds(244.2)));
    }
}
