using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the shared completeness and profile-analysis gates used by recommendations and alternatives.
    /// </summary>
    public class ProductRecommenderTests
    {
        static readonly string[] CleanList = { "Water", "Glycerin", "Dimethicone", "Mica", "Silica" };

        static UserProfileData UserWith(params string[] conditions) => new UserProfileData
        {
            age = 25, skinType = "Normal", skinTone = "Medium", skinConditions = new List<string>(conditions), knownAllergens = new List<string> { UserProfileData.NoKnownAllergens }
        };

        static BrandData TestBrand() => new BrandData
        {
            id = "test_brand", name = "Test Brand", category = "Makeup", status = "Active"
        };

        static ProductData Product(string type, string name, string source, bool reviewed, params string[] ingredients) => new ProductData
        {
            id = "test_" + name, name = name, brandId = "test_brand", brandName = "Test Brand", cosmeticType = type, status = "Active", recommendable = true,
            source = source, reviewed = reviewed, ingredients = new List<string>(ingredients)
        };

        static ProductRecommender.Refusal Check(ProductData product, UserProfileData who, out ScanRecord record) =>
            ProductRecommender.Check(product, TestBrand(), who, out record);

        [Test]
        public void CleanMakeupIsRecommended()
        {
            var product = Product("Foundation", "Clean Foundation", "", true, CleanList);
            Assert.AreEqual(ProductRecommender.Refusal.None, Check(product, UserWith(), out ScanRecord record));
            Assert.AreEqual("recommendation", record.source);
            Assert.AreEqual(ProductRecommender.Refusal.None, Check(product, UserWith("Eczema (Atopic Dermatitis)"), out _));
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
            List<ProductRecommender.Pick> picks = ProductRecommender.For(who, products, new[] { TestBrand() }, 10);
            Assert.AreEqual(1, picks.Count);
            Assert.AreEqual("Clean Foundation", picks[0].product.name);
            foreach (ProductData product in products)
                if (product.name != "Clean Foundation")
                    Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, Check(product, who, out _), product.name);
        }

        [Test]
        public void NobodyGetsASkinCareHairOrBodyProduct()
        {
            var who = UserWith();
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, Check(Product("Moisturizer", "Face Cream", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.NotMakeup, Check(Product("Shampoo", "Daily Shampoo", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.MissingCategory, Check(Product("", "Body Lotion", "", true, CleanList), who, out _));
            Assert.AreEqual(ProductRecommender.Refusal.MissingProductName, Check(Product("", "", "", true, CleanList), who, out _), "an unclear product is not recommended either");
        }

        [Test]
        public void AnIngredientFlaggedForTheUsersConditionExcludesTheProduct()
        {
            // Fragrance is flagged for eczema in the app's built-in list
            var product = Product("Blush", "Scented Blush", "", true, "Talc", "Mica", "Fragrance");
            Assert.AreEqual(ProductRecommender.Refusal.Flagged, Check(product, UserWith("Eczema (Atopic Dermatitis)"), out ScanRecord record));
            Assert.AreNotEqual("safe", record.status);
            Assert.IsEmpty(ProductRecommender.For(UserWith("Eczema (Atopic Dermatitis)"), new[] { product }, new[] { TestBrand() }, 10));
        }

        [Test]
        public void AnAllergenOfTheUserExcludesTheProduct()
        {
            var who = UserWith();
            who.knownAllergens = new List<string> { "Lanolin" };
            var product = Product("Lipstick", "Rich Lipstick", "", true, "Castor Oil", "Lanolin", "Beeswax");
            Assert.AreEqual(ProductRecommender.Refusal.Flagged, Check(product, who, out _));
        }

        [Test]
        public void AMissingIngredientListExcludesTheProduct()
        {
            var product = Product("Lipstick", "No List Lipstick", "", true);
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, Check(product, UserWith("Psoriasis"), out _));
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, Check(product, UserWith(), out _));
            product.ingredients = null;
            Assert.AreEqual(ProductRecommender.Refusal.NoIngredientList, Check(product, UserWith("Psoriasis"), out _));
        }

        [Test]
        public void WithASkinConditionAListNobodyVouchesForExcludesTheProduct()
        {
            // read by a scan and not reviewed by an admin: nobody vouches for this list
            var product = Product("Powder", "Scanned Powder", "scan", false, CleanList);
            Assert.AreEqual(ProductRecommender.Refusal.Unsourced, Check(product, UserWith("Acne"), out _));
            product.reviewed = true;
            Assert.AreEqual(ProductRecommender.Refusal.None, Check(product, UserWith("Acne"), out _));
            product.reviewed = false; product.sources = new List<string> { "Brand page | https://example.com/product" };
            Assert.AreEqual(ProductRecommender.Refusal.None, Check(product, UserWith("Acne"), out _));
        }

        [Test]
        public void AnUnknownIngredientExcludesTheProductForEveryProfile()
        {
            var product = Product("Foundation", "Mystery Foundation", "", true, "Water", "Glycerin", "Mica", "Zzyzxolate Crosspolymer Nine");
            Assert.AreEqual(ProductRecommender.Refusal.NotFullyChecked, Check(product, UserWith("Rosacea"), out _));
            Assert.AreEqual(ProductRecommender.Refusal.NotFullyChecked, Check(product, UserWith(), out _));
        }

        [Test]
        public void AProductForOlderUsersIsNotRecommendedToAYoungerOne()
        {
            var product = Product("Foundation", "Grown Up Foundation", "", true, CleanList);
            product.minAge = 18;
            UserProfileData who = UserWith(); who.age = 15;
            Assert.AreEqual(ProductRecommender.Refusal.TooYoung, Check(product, who, out _));
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
            Assert.IsEmpty(ProductRecommender.For(who, products, new[] { TestBrand() }, 10));
        }

        [Test]
        public void AnAnswerLikeNoneIsNotASkinCondition()
        {
            Assert.IsFalse(ProductScope.HasSkinCondition(UserWith()));
            Assert.IsFalse(ProductScope.HasSkinCondition(UserWith("None", " ")));
            Assert.IsTrue(ProductScope.HasSkinCondition(UserWith("Psoriasis")));
            Assert.IsTrue(ProductScope.HasSkinCondition(UserWith("my own typed condition")));
        }

        [Test]
        public void CompletenessGateRequiresRecommendableFlagValidBrandAndKnownIngredients()
        {
            ProductData product = Product("Foundation", "Complete Foundation", "", true, CleanList);
            Assert.IsTrue(ProductRecommender.IsRecommendable(product, TestBrand(), out string reason));
            Assert.IsEmpty(reason);

            product.recommendable = false;
            Assert.IsFalse(ProductRecommender.IsRecommendable(product, TestBrand(), out reason));
            Assert.AreEqual("recommendable-flag-false", reason);

            product.recommendable = true;
            Assert.IsFalse(ProductRecommender.IsRecommendable(product, null, out reason));
            Assert.AreEqual("brand-missing", reason);

            Assert.IsFalse(ProductRecommender.IsRecommendable(Product("Foundation", "Mystery Foundation", "", true, "Unknownol"), TestBrand(), out reason));
            Assert.AreEqual("ingredient-unresolved", reason);
        }

        [Test]
        public void PersonalizedRecommendationsRequireSkinTypeToneAndAnAllergenAnswer()
        {
            Assert.IsTrue(ProductRecommender.IsProfileComplete(UserWith()));
            UserProfileData profile = UserWith();
            profile.skinTone = "";
            Assert.IsFalse(ProductRecommender.IsProfileComplete(profile));
            profile = UserWith();
            profile.skinType = "";
            Assert.IsFalse(ProductRecommender.IsProfileComplete(profile));
            profile = UserWith();
            profile.knownAllergens.Clear();
            Assert.IsFalse(ProductRecommender.IsProfileComplete(profile));
        }
    }
}
