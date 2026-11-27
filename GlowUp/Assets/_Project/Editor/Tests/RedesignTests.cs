using GlowUp.Review;
using GlowUp.Scan;
using GlowUp.Views;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>Redesign plan of 27 Nov 2026: Send for Review, the result words, and the hand-over from AI Smart Scan.</summary>
    public class RedesignTests
    {
        [Test]
        public void TheIngredientPartStartsAfterTheWordIngredients()
        {
            Assert.AreEqual("Aqua, Dimethicone, Parfum", SendForReviewController.IngredientPart("Matte Lip\nIngredients: Aqua, Dimethicone,\nParfum."));
            Assert.AreEqual("Aqua, Mica", SendForReviewController.IngredientPart("Aqua, Mica"));
            Assert.AreEqual("", SendForReviewController.IngredientPart(null));
        }

        [Test]
        public void TheHandOverIsTakenOnce()
        {
            SendForReviewHandoff.Set(" 4800000000000 ", "Brand", "Tint");
            var first = SendForReviewHandoff.Take();
            Assert.AreEqual("4800000000000", first.barcode);
            Assert.AreEqual("Tint", first.productName);
            Assert.AreEqual("", SendForReviewHandoff.Take().barcode);
        }

        [Test]
        public void ResultWordsNeverSaySafe()
        {
            Assert.AreEqual("Caution", StatusPillView.Current("Use with care"));
            Assert.AreEqual("No caution found", StatusPillView.Current("Nothing flagged"));
            Assert.AreEqual("No caution found", StatusPillView.Current("Safe"));
            Assert.AreEqual("Avoid", StatusPillView.Current("Avoid"));
        }

        [Test]
        public void TheOldTreatmentPhraseIsReplaced()
        {
            string now = ProductScope.CurrentWording("May aggravate eczema. " + ProductScope.OldDermatologistLine);
            StringAssert.DoesNotContain("May aggravate", now);
            StringAssert.DoesNotContain("best treatment", now);
            StringAssert.Contains("Not medical advice", now);
        }
    }
}
