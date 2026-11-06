using System.Collections.Generic;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// The "not in our catalog" photo (7 November 2026): an unclear reading is never used or shown, the clean text keeps only
    /// lines that read as real words, and a brand read from a label is written the way the catalogue spells it.
    /// </summary>
    public class PhotoCaptureTests
    {
        static readonly HashSet<string> Catalogue = new HashSet<string> { "maybelline", "new", "york", "superstay", "matte", "ink", "happy", "skin" };
        static bool Known(string lower) => Catalogue.Contains(lower);

        [Test]
        public void GarbledTextIsNotReadable()
        {
            PhotoTextQuality.Verdict v = PhotoTextQuality.Judge("MAPEM BUPEKGINY WATTE", Known);
            Assert.IsFalse(v.readable, "the example from the brief");
            Assert.AreEqual("", v.cleanText, "nothing of an unclear reading is kept");
            Assert.IsFalse(PhotoTextQuality.Judge("", Known).readable);
            Assert.IsFalse(PhotoTextQuality.Judge("XQZT LORBM\nPWEKRT", Known).readable);
        }

        [Test]
        public void AClearPackIsReadableAndOnlyItsRealLinesAreKept()
        {
            PhotoTextQuality.Verdict v = PhotoTextQuality.Judge("MAYBELLINE\nNEW YORK\nSUPERSTAY MATTE INK\nLIQUID LIPSTICK\nKRXQ ZZPW\n5 ml", Known);
            Assert.IsTrue(v.readable);
            StringAssert.Contains("SUPERSTAY MATTE INK", v.cleanText);
            StringAssert.DoesNotContain("KRXQ", v.cleanText, "a garbled line is dropped");
            StringAssert.Contains("5 ml", v.cleanText, "the amount line is kept");
            Assert.Greater(v.score, 0.5f);
        }

        [Test]
        public void TheCleanTextCannotCarryMarkup()
        {
            PhotoTextQuality.Verdict v = PhotoTextQuality.Judge("MATTE <b>LIQUID</b> LIPSTICK", Known);
            StringAssert.DoesNotContain("<", v.cleanText);
        }

        [Test]
        public void ABrandIsSpelledLikeTheCatalogue()
        {
            var brands = new List<string> { "Maybelline New York", "Happy Skin", "L'Oréal Paris" };
            Assert.AreEqual("Maybelline New York", BrandNameNormalizer.Normalize("MAYBELLINE NEWYORK", brands), "spaces and capitals do not matter");
            Assert.AreEqual("Maybelline New York", BrandNameNormalizer.Normalize("Maybeline New York", brands), "one misread letter is forgiven");
            Assert.AreEqual("L'Oréal Paris", BrandNameNormalizer.Normalize("LOREAL PARIS", brands), "accents and marks do not matter");
            Assert.AreEqual("Happy Skin", BrandNameNormalizer.Normalize("happyskin", brands));
        }

        [Test]
        public void ANewBrandKeepsItsWordsWithOrdinaryCapitals()
        {
            var brands = new List<string> { "Maybelline New York" };
            Assert.AreEqual("Glow Lab Cosmetics", BrandNameNormalizer.Normalize("GLOW  LAB COSMETICS", brands));
            Assert.AreEqual("Ever New York", BrandNameNormalizer.Normalize("ever newyork", brands), "a glued place name is split");
            Assert.AreEqual("bareMinerals", BrandNameNormalizer.Normalize("bareMinerals", brands), "a brand's own mixed capitals are kept");
            Assert.AreEqual("Ink", BrandNameNormalizer.Normalize("INK", new List<string> { "Inka" }), "very short names are never matched loosely");
        }
    }
}
