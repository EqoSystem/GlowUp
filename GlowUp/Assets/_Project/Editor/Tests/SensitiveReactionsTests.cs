using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>The optional "How does your skin react?" answer (Sensitive skin only): what is kept, and what it points at.</summary>
    public class SensitiveReactionsTests
    {
        [Test]
        public void OnlyKnownReactionsAreKeptEachOnceInTheFixedOrder()
        {
            var clean = SensitiveReactions.Clean(new[] { "itching", "bogus", "REDNESS", "itching", " ", null });
            CollectionAssert.AreEqual(new[] { "redness", "itching" }, clean);
        }

        [Test]
        public void TheQuestionBelongsToSensitiveSkinOnly()
        {
            var dry = new UserProfileData { skinType = "Dry", sensitiveReactions = new List<string> { "redness" } };
            Assert.AreEqual(0, SensitiveReactions.Of(dry).Count, "a saved answer is ignored when the skin type is no longer Sensitive");
            var sensitive = new UserProfileData { skinType = "Sensitive", sensitiveReactions = new List<string> { "redness", "tightness" } };
            Assert.AreEqual(2, SensitiveReactions.Of(sensitive).Count);
            Assert.AreEqual(0, SensitiveReactions.Of(null).Count);
            Assert.AreEqual(0, SensitiveReactions.Of(new UserProfileData { skinType = "Sensitive" }).Count, "not answered = nothing extra is flagged");
        }

        [Test]
        public void EachReactionPointsAtSomethingTheAppAlreadyUnderstands()
        {
            foreach (var r in SensitiveReactions.All)
                Assert.IsTrue(r.condition != null || r.skinType != null || r.allergen != null, r.id + " must point at a condition, a skin type or an allergen");
            Assert.AreEqual("Rosacea", SensitiveReactions.Find("redness").condition);
            Assert.AreEqual("Dry", SensitiveReactions.Find("tightness").skinType);
            Assert.AreEqual("Fragrance / Parfum", SensitiveReactions.Find("products").allergen);
            Assert.IsNull(SensitiveReactions.Find("nothing"));
        }
    }
}
