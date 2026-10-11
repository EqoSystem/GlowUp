using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks who may be recommended what (ProductRecommender): make-up only, nothing flagged for the user, and the
    /// stricter check for a user who saved a skin condition. Everything here runs without the internet: the ingredients
    /// used are in the app's built-in list.
    /// </summary>
    public class ProductRecommenderTests
    {
        static readonly string[] CleanList = { "Water", "Glycerin", "Dimethicone", "Mica", "Silica" };

        static UserProfileData UserWith(params string[] conditions) => new UserProfileData
        {
            age = 25, skinType = "Normal", skinConditions = new List<string>(conditions), knownAllergens = new List<string> { UserProfileData.NoKnownAllergens }
        };

        static ProductData Product(string type, string name, string source, bool reviewed, params string[] ingredients) => new ProductData
        {
            id = "test_" + name, name = name, brandName = "Test Brand", cosmeticType = type, status = "Active",
            source = source, reviewed = reviewed, ingredients = new List<string>(ingredients)
        };

        [SetUp]
        public void SetUp() { ProductRecommender.UnknownAllowedWithCondition = 0; }

        [Test]
        public void CleanMakeupIsRecommended()
        {
            var product = Product("Foundation", "Clean Foundation", "", true, CleanList);
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith(), out ScanRecord record));
            Assert.AreEqual("recommendation", record.source);
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith("Eczema (Atopic Dermatitis)"), out _));
        }

        [Test]
        public void AUserWithASkinConditionNeverGetsASkinCareProduct()
        {
            var who = UserWith("Eczema (Atopic Dermatitis)", "Rosacea");
            var products = new List<ProductData>
            {
                Product("Moisturizer", "Hydrating Face Cream", "", true, CleanList),
                Product("Serum", "Barrier Repair Serum", "", true, CleanList),
                Product("Sunscreen", "Daily Sunscreen SPF 50", "", true, CleanList),
                Product("Skincare", "Calming Gel", "", true, CleanList),
                Product("Foundation", "Clean Foundation", "", true, CleanList)
            };
            List<ProductRecommender.Pick> picks = ProductRecommender.For(who, products, 10);
            Assert.AreEqual(1, picks.Count);
            Assert.AreEqual("Clean Foundation", picks[0].product.name);
            foreach (ProductData product in products)
                if (product.name != "Clean Foundation")
                    Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, ProductRecommender.Check(product, who, out _), product.name);
        }

        [Test]
        public void NobodyGetsASkinCareHairOrBodyProduct()
        {
            var who = UserWith();
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, ProductRecommender.Check(Product("Moisturizer", "Face Cream", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, ProductRecommender.Check(Product("Shampoo", "Daily Shampoo", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, ProductRecommender.Check(Product("", "Body Lotion", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, ProductRecommender.Check(Product("", "", "", true, CleanList), who, out _), "an unclear product is not recommended either");
        }

        [Test]
        public void AnIngredientFlaggedForTheUsersConditionExcludesTheProduct()
        {
            // Fragrance is flagged for eczema in the app's built-in list
            var product = Product("Blush", "Scented Blush", "", true, "Talc", "Mica", "Fragrance");
            Assert.AreEqual(ProductRecommender.Refusal.Flagged, ProductRecommender.Check(product, UserWith("Eczema (Atopic Dermatitis)"), out ScanRecord record));
            Assert.AreNotEqual("safe", record.status);
            Assert.IsEmpty(ProductRecommender.For(UserWith("Eczema (Atopic Dermatitis)"), new[] { product }, 10));
        }

        [Test]
        public void AnAllergenOfTheUserExcludesTheProduct()
        {
            var who = UserWith();
            who.knownAllergens = new List<string> { "Lanolin" };
            var product = Product("Lipstick", "Rich Lipstick", "", true, "Castor Oil", "Lanolin", "Beeswax");
            Assert.AreEqual(ProductRecommender.Refusal.Flagged, ProductRecommender.Check(product, who, out _));
        }

        [Test]
        public void AMissingIngredientListExcludesTheProduct()
        {
            var product = Product("Lipstick", "No List Lipstick", "", true);
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, ProductRecommender.Check(product, UserWith("Psoriasis"), out _));
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, ProductRecommender.Check(product, UserWith(), out _));
            product.ingredients = null;
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, ProductRecommender.Check(product, UserWith("Psoriasis"), out _));
        }

        [Test]
        public void WithASkinConditionAListNobodyVouchesForExcludesTheProduct()
        {
            // read by a scan and not reviewed by an admin: nobody vouches for this list
            var product = Product("Powder", "Scanned Powder", "scan", false, CleanList);
            Assert.AreEqual(ProductRecommender.Refusal.Unsourced, ProductRecommender.Check(product, UserWith("Acne"), out _));
            product.reviewed = true;
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith("Acne"), out _));
            product.reviewed = false; product.sources = new List<string> { "Brand page | https://example.com/product" };
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith("Acne"), out _));
        }

        [Test]
        public void WithASkinConditionAnUnknownIngredientExcludesTheProduct()
        {
            var product = Product("Foundation", "Mystery Foundation", "", true, "Water", "Glycerin", "Mica", "Zzyzxolate Crosspolymer Nine");
            Assert.AreEqual(ProductRecommender.Refusal.NotFullyChecked, ProductRecommender.Check(product, UserWith("Rosacea"), out _));
            // a user without a skin condition keeps the earlier behaviour: an unknown ingredient is not a flag
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith(), out _));
            // the limit can be loosened in the Inspector
            ProductRecommender.UnknownAllowedWithCondition = 1;
            Assert.AreEqual(ProductRecommender.Refusal.None, ProductRecommender.Check(product, UserWith("Rosacea"), out _));
        }

        [Test]
        public void AProductForOlderUsersIsNotRecommendedToAYoungerOne()
        {
            var product = Product("Foundation", "Grown Up Foundation", "", true, CleanList);
            product.minAge = 18;
            UserProfileData who = UserWith(); who.age = 15;
            Assert.AreEqual(ProductRecommender.Refusal.TooYoung, ProductRecommender.Check(product, who, out _));
        }

        [Test]
        public void WhenNothingPassesTheListIsEmptyAndNothingRiskyIsShownInstead()
        {
            var who = UserWith("Eczema (Atopic Dermatitis)");
            var products = new List<ProductData>
            {
                Product("Blush", "Scented Blush", "", true, "Talc", "Mica", "Fragrance"),
                Product("Moisturizer", "Face Cream", "", true, CleanList),
                Product("Lipstick", "No List Lipstick", "", true)
            };
            Assert.IsEmpty(ProductRecommender.For(who, products, 10));
        }

        [Test]
        public void AnAnswerLikeNoneIsNotASkinCondition()
        {
            Assert.IsFalse(ProductScope.HasSkinCondition(UserWith()));
            Assert.IsFalse(ProductScope.HasSkinCondition(UserWith("None", " ")));
            Assert.IsTrue(ProductScope.HasSkinCondition(UserWith("Psoriasis")));
            Assert.IsTrue(ProductScope.HasSkinCondition(UserWith("my own typed condition")));
        }
    }
}
