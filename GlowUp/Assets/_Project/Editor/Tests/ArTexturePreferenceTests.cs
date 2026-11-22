using GlowUp.AR;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>Panel revision of 22 Nov 2026: where AR Try-On's starting texture comes from.</summary>
    public class ArTexturePreferenceTests
    {
        static readonly string[] Names = { "matte", "satin", "glossy", "shimmer", "metallic" };

        [Test]
        public void TheProductsOwnFinishWinsOverEverything()
        {
            Assert.AreEqual("shimmer", ArTexturePreference.Choose("shimmer", "matte", "Lip gloss", Names, "satin", out var source));
            Assert.AreEqual(ArTexturePreference.Source.Product, source);
        }

        [Test]
        public void ThenTheUsersOwnChoice()
        {
            Assert.AreEqual("matte", ArTexturePreference.Choose("", "matte", "Lip gloss", Names, "satin", out var source));
            Assert.AreEqual(ArTexturePreference.Source.Preference, source);
        }

        [Test]
        public void ThenAGuessFromTheKindThenTheDefault()
        {
            Assert.AreEqual("glossy", ArTexturePreference.Choose(null, "", "Lip Gloss", Names, "satin", out var s1));
            Assert.AreEqual(ArTexturePreference.Source.Kind, s1);
            Assert.AreEqual("matte", ArTexturePreference.Choose(null, "", "Foundation", Names, "satin", out var s2));
            Assert.AreEqual("satin", ArTexturePreference.Choose(null, "", "Blush", Names, "satin", out var s3));
            Assert.AreEqual(ArTexturePreference.Source.Default, s3);
        }

        [Test]
        public void AnUnknownNameIsNeverUsedAndTheSourceLineIsSaid()
        {
            Assert.AreEqual("satin", ArTexturePreference.Choose("wet", "dewy", "Blush", Names, "satin", out var source));
            Assert.AreEqual(ArTexturePreference.Source.Default, source);
            StringAssert.Contains("scan", ArTexturePreference.SourceLine(ArTexturePreference.Source.Product));
            StringAssert.Contains("last time", ArTexturePreference.SourceLine(ArTexturePreference.Source.Preference));
        }
    }
}
