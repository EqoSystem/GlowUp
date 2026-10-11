using System.Collections.Generic;
using GlowUp.Data;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the phone-side look-up table of products (ProductIndex): finding by barcode, by brand and name, by alias,
    /// the closest names, a name read on a pack, and the ids that keep a product from being added twice.
    /// </summary>
    public class ProductIndexTests
    {
        static ProductData P(string id, string brand, string name, string barcode = "", int ingredients = 0, params string[] aliases)
        {
            var list = new List<string>();
            for (int i = 0; i < ingredients; i++) list.Add("Ingredient " + i);
            return new ProductData { id = id, brandName = brand, name = name, barcode = barcode, ingredients = list, aliases = new List<string>(aliases), cosmeticType = "Lipstick", status = "Active" };
        }

        static ProductIndex Sample() => ProductIndex.Build(new[]
        {
            P("fda_1", "Maybelline", "Superstay Matte Ink Lipstick Initiator", "0041554496536", 12),
            P("fda_2", "Maybelline", "Superstay Lumi-Matte Foundation", "", 30),
            P("fda_3", "Maybelline", "Fit Me Matte + Poreless Foundation", "3600531324506", 0),
            P("fda_4", "Maybelline", "Fit Me Concealer", "", 20),
            P("fda_5", "Absidy", "Complexion Blur Translucent Perfecting Powder", "", 18, "Complexion Blur Powder"),
            P("fda_6", "L'Or\u00e9al Paris", "Color Riche Intense Volume Matte", "", 0),
            P("obf_7", "NIVEA", "Lip Tint", "", 5),
            P("fda_8", "Careline", "Lip Tint", "", 0)
        });

        [Test]
        public void ABarcodeFindsItsProductWhateverTheZerosInFront()
        {
            ProductIndex index = Sample();
            Assert.AreEqual("fda_1", index.ByBarcode("0041554496536").id);
            Assert.AreEqual("fda_1", index.ByBarcode("41554496536").id);
            Assert.AreEqual("fda_1", index.ByBarcode(" 0 041554 496536 ").id);
            Assert.IsNull(index.ByBarcode("123"), "too short to be a product barcode");
            Assert.IsNull(index.ByBarcode(""));
            Assert.IsNull(index.ByBarcode("9999999999999"));
        }

        [Test]
        public void TheSameNameIsFoundWhateverTheCapitalsAccentsAndPunctuation()
        {
            ProductIndex index = Sample();
            Assert.AreEqual("fda_6", index.Exact("L'Oreal Paris", "COLOR RICHE - Intense Volume, Matte").id);
            Assert.AreEqual("fda_6", index.Exact("LOREAL PARIS", "color riche intense volume matte").id);
            Assert.AreEqual("fda_3", index.Exact("Maybelline New York", "Fit Me! Matte + Poreless Foundation").id, "the brand may be written longer");
            Assert.IsNull(index.Exact("Revlon", "Fit Me Matte + Poreless Foundation"), "another brand's product is not it");
        }

        [Test]
        public void AnAliasCountsAsTheName()
        {
            Assert.AreEqual("fda_5", Sample().Exact("Absidy", "Complexion Blur Powder").id);
            ProductIndex learned = ProductIndex.Build(new[] { P("fda_5", "Absidy", "Complexion Blur Translucent Perfecting Powder") },
                new Dictionary<string, string> { { "Absidy blur powder", "fda_5" } });
            Assert.AreEqual("fda_5", learned.Exact("Absidy", "ABSIDY BLUR POWDER").id, "a name this phone has learned");
        }

        [Test]
        public void WithoutABrandAPlainNameIdentifiesNothing()
        {
            ProductIndex index = Sample();
            Assert.IsNull(index.Exact("", "Lip Tint"), "two brands sell a 'Lip Tint'");
            Assert.AreEqual("obf_7", index.Exact("Nivea", "Lip Tint").id);
            Assert.AreEqual("fda_4", index.Exact("", "Fit Me Concealer").id, "a name of several words that only one product has");
        }

        [Test]
        public void TheClosestNamesComeFirstAndOnlyAClearOneIsSure()
        {
            ProductIndex index = Sample();
            List<ProductIndex.Candidate> found = index.Search("Absidy", "Complexion Blur Translucent Powder", 3);
            Assert.AreEqual("fda_5", found[0].product.id);
            Assert.GreaterOrEqual(found[0].score, ProductIndex.Sure, "one name holds all the words of the other");

            found = index.Search("Maybelline", "Superstay Matte Ink", 3);
            Assert.AreEqual("fda_1", found[0].product.id);
            Assert.GreaterOrEqual(found[0].score, ProductIndex.Sure);
            Assert.Less(found[1].score, ProductIndex.Sure, "the Superstay foundation is a possibility, not a certainty");

            found = index.Search("Maybelline", "Fit Me", 3);
            Assert.AreEqual(2, found.Count, "two products of the Fit Me line");
            Assert.Less(found[0].score, ProductIndex.Sure, "'Fit Me' alone does not say which one: the user has to choose");

            found = index.Search("Maybelline", "Fit Me Matte + Poreless Powder", 3);
            Assert.AreEqual("fda_3", found[0].product.id);
            Assert.Less(found[0].score, ProductIndex.Sure, "the powder is not the foundation");

            Assert.IsEmpty(index.Search("Revlon", "Superstay Matte Ink", 3), "another brand's products are left out");
            Assert.IsEmpty(index.Search("Maybelline", "Totally Different Thing", 3));
        }

        [Test]
        public void AReadingMistakeInANameIsForgiven()
        {
            List<ProductIndex.Candidate> found = Sample().Search("Maybelline", "Superstay Matte 1nk Lipstlck Initiator", 3);
            Assert.AreEqual("fda_1", found[0].product.id);
            Assert.GreaterOrEqual(found[0].score, ProductIndex.Sure);
        }

        [Test]
        public void AProductIsRecognisedFromTheTextOnItsPack()
        {
            ProductIndex index = Sample();
            List<ProductIndex.Candidate> found = index.InText("MAYBELLINE NEW YORK\nSUPERSTAY\nMATTE INK\nLIQUID LIPSTICK 20 INITIATOR\n5 ml", 3);
            Assert.AreEqual("fda_1", found[0].product.id);
            Assert.IsTrue(found[0].exactName, "every word of the name is on the pack");
            Assert.IsTrue(found[0].brandAgrees);
            Assert.GreaterOrEqual(found[0].score, ProductIndex.Sure);

            found = index.InText("ABSIDY BEAUTY BASE\nCOMPLEXION BLUR\ntranslucent perfecting powder\nMILK", 3);
            Assert.AreEqual("fda_5", found[0].product.id);

            found = index.InText("some other brand\nmatte lipstick", 3);
            Assert.IsTrue(found.Count == 0 || found[0].score < ProductIndex.Sure, "two common words do not make a product");
        }

        [Test]
        public void BrandNamesAgreeWhenOneIsTheOtherWrittenLonger()
        {
            Assert.IsTrue(ProductIndex.BrandsAgree("Maybelline", "Maybelline New York"));
            Assert.IsTrue(ProductIndex.BrandsAgree("L'Oreal", "L'Or\u00e9al Paris"));
            Assert.IsTrue(ProductIndex.BrandsAgree("NYX", "NYX Professional Makeup"));
            Assert.IsTrue(ProductIndex.BrandsAgree("happy skin", "HappySkin"));
            Assert.IsTrue(ProductIndex.BrandsAgree("", "Anything"), "no brand given: any brand will do");
            Assert.IsFalse(ProductIndex.BrandsAgree("Maybelline", "Revlon"));
            Assert.IsFalse(ProductIndex.BrandsAgree("Nivea", "Neutrogena"));
        }

        [Test]
        public void TheIdOfAScannedProductIsMadeFromItsBrandAndName()
        {
            Assert.AreEqual("scan_maybelline__superstay_matte_ink", ProductIndex.ScanId("Maybelline", "SuperStay Matte Ink"));
            Assert.AreEqual(ProductIndex.ScanId("MAYBELLINE", "superstay  matte-ink!"), ProductIndex.ScanId("Maybelline", "SuperStay Matte Ink"), "the same product always gets the same id");
            Assert.AreEqual(ProductIndex.ScanId("L'Oreal Paris", "Color Riche"), ProductIndex.ScanId("L'Or\u00e9al Paris", "Color Riche"), "an accent makes no second entry");
            Assert.AreEqual("scan_nobrand__mystery_blush", ProductIndex.ScanId("", "Mystery Blush"));
            Assert.AreEqual("", ProductIndex.ScanId("Maybelline", ""), "no name, no id");
            Assert.AreEqual("scan_l_oreal_paris", ProductIndex.ScanBrandId("L'Or\u00e9al Paris"));
            StringAssert.StartsWith("scan_", ProductIndex.ScanId("Brand", "Name"));
            Assert.LessOrEqual(ProductIndex.ScanId("Brand", new string('x', 400)).Length, 110);
        }

        [Test]
        public void AProductThatIsListedTwiceInTheSourceIsIndexedOnce()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("a", "B", "One Name"), P("a", "B", "One Name"), null, P("", "B", "No Id"), P("c", "B", "") });
            Assert.AreEqual(1, index.Count);
        }
    }
}
