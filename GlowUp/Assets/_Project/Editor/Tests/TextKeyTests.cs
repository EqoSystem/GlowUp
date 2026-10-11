using GlowUp.Data;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>Checks the one rule that turns a name into a comparable key (TextKey). The website's normalizeName must give the same keys.</summary>
    public class TextKeyTests
    {
        // accented letters are written as codes here so this file stays plain text: e-acute, o-circumflex, n-tilde, u-umlaut
        const string EAcute = "\u00e9", OCircumflex = "\u00f4", NTilde = "\u00f1", UUmlaut = "\u00fc";

        [Test]
        public void CapitalsSpacesAndPunctuationDoNotMatter()
        {
            Assert.AreEqual("vitamin e", TextKey.Normalize("VITAMIN E"));
            Assert.AreEqual("vitamin e", TextKey.Normalize("vitamin e"));
            Assert.AreEqual("vitamin e", TextKey.Normalize("  Vitamin   E  "));
            Assert.AreEqual("vitamin e", TextKey.Normalize("Vitamin-E."));
            Assert.AreEqual("caprylic capric triglyceride", TextKey.Normalize("Caprylic/Capric Triglyceride"));
            Assert.AreEqual("", TextKey.Normalize(null));
            Assert.AreEqual("", TextKey.Normalize("   "));
            Assert.AreEqual("", TextKey.Normalize("--- !!"));
        }

        [Test]
        public void AccentsAreRemoved()
        {
            Assert.AreEqual("l oreal paris", TextKey.Normalize("L'Or" + EAcute + "al Paris"));
            Assert.AreEqual(TextKey.Normalize("L'Oreal Paris"), TextKey.Normalize("L'OR" + EAcute.ToUpperInvariant() + "AL  PARIS"));
            Assert.AreEqual("lancome", TextKey.Normalize("Lanc" + OCircumflex + "me"));
            Assert.AreEqual("nino", TextKey.Normalize("Ni" + NTilde + "o"));
            Assert.AreEqual("uber", TextKey.Normalize(UUmlaut + "ber"));
        }

        [Test]
        public void TheSlugIsTheKeyWithUnderscores()
        {
            Assert.AreEqual("l_oreal_paris", TextKey.Slug("L'Or" + EAcute + "al Paris"));
            Assert.AreEqual("maybelline_new_york", TextKey.Slug(" Maybelline  New York "));
            Assert.AreEqual("", TextKey.Slug("!!"));
        }

        [Test]
        public void SameComparesKeys()
        {
            Assert.IsTrue(TextKey.Same("Fit Me! Matte + Poreless", "fit me matte poreless"));
            Assert.IsFalse(TextKey.Same("Fit Me", "Fit Me Matte"));
            Assert.IsFalse(TextKey.Same("", ""), "two empty names are not 'the same product'");
        }

        [Test]
        public void CompactIgnoresWhereTheSpacesAre()
        {
            Assert.AreEqual(TextKey.Compact("Maybelline New York"), TextKey.Compact("MaybellineNewYork"));
            Assert.AreEqual("superstay", TextKey.Compact("Super Stay"));
        }

        [Test]
        public void ANameInAnotherAlphabetIsKeptAsItIs()
        {
            string korean = "\uc124\ud654\uc218";          // a brand name written in Korean
            Assert.AreEqual(korean, TextKey.Normalize(" " + korean + " "));
            Assert.IsTrue(TextKey.Same(korean, korean));
        }
    }
}
