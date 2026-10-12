using System.Collections.Generic;
using GlowUp.Auth;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the list of emails that have signed in on this phone (SavedAccounts): newest first, never twice, five at
    /// the most, only texts that are written like an email, and that an email can be taken off again. The tests keep the
    /// list in memory only, so nothing is written to the computer they run on.
    /// </summary>
    public class SavedAccountsTests
    {
        [TearDown]
        public void TearDown() { SavedAccounts.UseKeptFile(); }

        [Test]
        public void TheNewestEmailIsFirstAndNoneIsThereTwice()
        {
            var list = new List<string> { "ana@gmail.com", "ben@up.edu.ph" };
            CollectionAssert.AreEqual(new[] { "cara@yahoo.com", "ana@gmail.com", "ben@up.edu.ph" }, SavedAccounts.With(list, "cara@yahoo.com", 5));
            CollectionAssert.AreEqual(new[] { "ben@up.edu.ph", "ana@gmail.com" }, SavedAccounts.With(list, " Ben@UP.edu.ph ", 5), "the same email again moves to the top; capitals and spaces make no difference");
            CollectionAssert.AreEqual(new[] { "ana@gmail.com", "ben@up.edu.ph" }, SavedAccounts.With(list, "ana@gmail.com", 5));
            CollectionAssert.AreEqual(new[] { "ana@gmail.com" }, SavedAccounts.With(null, "ana@gmail.com", 5));
        }

        [Test]
        public void OnlyWhatIsWrittenLikeAnEmailIsKept()
        {
            var list = new List<string> { "ana@gmail.com" };
            foreach (string notAnEmail in new[] { "", null, "ana", "ana@", "ana@gmail", "my password 123", "ana cruz@gmail.com" })
                CollectionAssert.AreEqual(new[] { "ana@gmail.com" }, SavedAccounts.With(list, notAnEmail, 5), "'" + notAnEmail + "'");
        }

        [Test]
        public void TheListNeverGrowsPastItsLimit()
        {
            var list = new List<string>();
            for (int i = 1; i <= 8; i++) list = SavedAccounts.With(list, "user" + i + "@gmail.com", SavedAccounts.Most);
            Assert.AreEqual(SavedAccounts.Most, list.Count);
            Assert.AreEqual("user8@gmail.com", list[0], "the newest stays");
            Assert.IsFalse(list.Contains("user1@gmail.com"), "the oldest drops off");
            Assert.AreEqual(0, SavedAccounts.With(list, "ana@gmail.com", 0).Count);
        }

        [Test]
        public void AnEmailCanBeTakenOff()
        {
            var list = new List<string> { "ana@gmail.com", "ben@up.edu.ph", "cara@yahoo.com" };
            CollectionAssert.AreEqual(new[] { "ana@gmail.com", "cara@yahoo.com" }, SavedAccounts.Without(list, "BEN@up.edu.ph "));
            CollectionAssert.AreEqual(new[] { "ana@gmail.com", "ben@up.edu.ph", "cara@yahoo.com" }, SavedAccounts.Without(list, "nobody@gmail.com"));
            CollectionAssert.AreEqual(new[] { "ana@gmail.com", "ben@up.edu.ph", "cara@yahoo.com" }, SavedAccounts.Without(list, null));
            Assert.AreEqual(0, SavedAccounts.Without(null, "ana@gmail.com").Count);
        }

        [Test]
        public void RememberAndForgetWorkOnTheKeptList()
        {
            SavedAccounts.UseMemoryOnly(new[] { "ana@gmail.com" });
            SavedAccounts.Remember("ben@up.edu.ph");
            SavedAccounts.Remember("not an email");
            CollectionAssert.AreEqual(new[] { "ben@up.edu.ph", "ana@gmail.com" }, SavedAccounts.All());
            SavedAccounts.Forget("ANA@gmail.com");
            CollectionAssert.AreEqual(new[] { "ben@up.edu.ph" }, SavedAccounts.All());
            SavedAccounts.Forget("ben@up.edu.ph");
            Assert.AreEqual(0, SavedAccounts.All().Count);
        }
    }
}
