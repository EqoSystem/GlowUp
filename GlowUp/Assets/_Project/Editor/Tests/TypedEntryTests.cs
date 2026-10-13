using System.Collections.Generic;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks what the app makes of things a person TYPES on the scan screen's "Type it in" pop-up. Before 13 October 2026
    /// it answered everything that was not a comma list with "type at least two ingredients", read a row of names without
    /// commas as one made-up ingredient, and took "fragrance free" for fragrance. Everything here runs without the
    /// internet, with the ingredients of the app's built-in list.
    /// </summary>
    public class TypedEntryTests
    {
        [SetUp]
        public void SetUp()
        {
            IngredientKnowledge.FuzzyThreshold = 0.85f;
            IngredientStoplist.MaximumLength = 70;
            IngredientTextParser.SetNotIngredients(null);
        }

        static void AssertNames(IEnumerable<string> expected, List<string> actual, string what)
        {
            CollectionAssert.AreEqual(new List<string>(expected), actual, what + ": got [" + string.Join(", ", actual) + "]");
        }

        // ------------------------------------------------------------------ the reader, with typed text
        [Test]
        public void Names_typed_without_commas_are_read_one_by_one()
        {
            var label = IngredientTextParser.Read("talc mica dimethicone fragrance");
            AssertNames(new[] { "Talc", "Mica", "Dimethicone", "Fragrance" }, label.ingredients, "known ingredients");
            Assert.AreEqual(0, label.unrecognized.Count);
        }

        [Test]
        public void And_stands_between_two_names()
        {
            var label = IngredientTextParser.Read("glycerin and dimethicone and fragrance");
            AssertNames(new[] { "Glycerin", "Dimethicone", "Fragrance" }, label.ingredients, "known ingredients");

            label = IngredientTextParser.Read("Talc, Mica, Glycerin and Dimethicone");
            AssertNames(new[] { "Talc", "Mica", "Glycerin", "Dimethicone" }, label.ingredients, "the last two are joined by 'and'");

            label = IngredientTextParser.Read("talc & mica plus dimethicone");
            AssertNames(new[] { "Talc", "Mica", "Dimethicone" }, label.ingredients, "'&' and 'plus' too");
        }

        [Test]
        public void A_name_the_app_does_not_know_is_still_kept_apart_from_the_others()
        {
            var label = IngredientTextParser.Read("glycerin and zzyzxine and dimethicone");
            AssertNames(new[] { "Glycerin", "Dimethicone" }, label.ingredients, "known ingredients");
            AssertNames(new[] { "Zzyzxine" }, label.unrecognized, "the unknown one stands alone");
        }

        [Test]
        public void A_claim_that_something_is_not_in_it_is_never_that_ingredient()
        {
            foreach (string claim in new[] { "paraben-free, fragrance free lipstick", "Talc, Mica, fragrance free", "no parabens, no fragrance", "without fragrance, Talc, Mica",
                                             "0% alcohol, Talc, Mica", "free of fragrance, Talc, Mica" })
            {
                var label = IngredientTextParser.Read(claim);
                CollectionAssert.DoesNotContain(label.ingredients, "Fragrance", "'" + claim + "' does not contain fragrance");
                CollectionAssert.DoesNotContain(label.ingredients, "Paraben", "'" + claim + "' does not contain parabens");
                foreach (string name in label.ingredients) StringAssert.DoesNotContain("Alcohol", name, "'" + claim + "' does not contain alcohol");
            }
            Assert.IsTrue(IngredientStoplist.IsFreeOfClaim("Fragrance-Free"));
            Assert.IsTrue(IngredientStoplist.IsFreeOfClaim("free from parabens"));
            Assert.IsTrue(IngredientStoplist.IsFreeOfClaim("No parabens"));
            Assert.IsTrue(IngredientStoplist.IsFreeOfClaim("0 % alcohol"));
        }

        [Test]
        public void A_colour_number_and_a_real_percentage_are_not_claims()
        {
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Red No. 7"), "'No.' of a colour is a number");
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Blue No 1"));
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Glycerin 10%"), "ten per cent is not zero per cent");
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Niacinamide 5.0%"));
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Nonoxynol-9"));
            Assert.IsFalse(IngredientStoplist.IsFreeOfClaim("Fragrance"));
        }

        [Test]
        public void An_ordinary_list_is_read_as_before()
        {
            var label = IngredientTextParser.Read("Aqua, Glycerin, Dimethicone, Parfum, Phenoxyethanol");
            AssertNames(new[] { "Water", "Glycerin", "Dimethicone", "Fragrance", "Phenoxyethanol" }, label.ingredients, "known ingredients");
        }

        // ------------------------------------------------------------------ what was typed?
        [Test]
        public void An_ingredient_list_is_ingredients()
        {
            Assert.AreEqual(TypedEntry.Kind.Ingredients, TypedEntry.Understand("Aqua, Glycerin, Dimethicone, Parfum").kind);
            Assert.AreEqual(TypedEntry.Kind.Ingredients, TypedEntry.Understand("talc\nmica\ndimethicone").kind);
            Assert.AreEqual(TypedEntry.Kind.Ingredients, TypedEntry.Understand("talc mica dimethicone fragrance").kind);
        }

        [Test]
        public void One_ingredient_the_app_knows_can_be_checked_alone()
        {
            TypedEntry.Reading reading = TypedEntry.Understand("fragrance");
            Assert.AreEqual(TypedEntry.Kind.Ingredients, reading.kind);
            AssertNames(new[] { "Fragrance" }, reading.list.ingredients, "the one ingredient");
        }

        [Test]
        public void A_products_name_is_a_product()
        {
            foreach (string typed in new[] { "Maybelline Fit Me Matte + Poreless Foundation", "careline blush on", "lipstick", "Vice Cosmetics Phenomenal Velvet lip tint", "  ever bilena   matte lipstick  " })
            {
                TypedEntry.Reading reading = TypedEntry.Understand(typed);
                Assert.AreEqual(TypedEntry.Kind.Product, reading.kind, "'" + typed + "'");
                Assert.IsFalse(reading.product.StartsWith(" ") || reading.product.EndsWith(" ") || reading.product.Contains("  "), "tidied: '" + reading.product + "'");
            }
            Assert.AreEqual("ever bilena matte lipstick", TypedEntry.Understand("  ever bilena   matte lipstick  ").product);
        }

        [Test]
        public void Names_the_app_does_not_know_in_the_shape_of_a_list_are_still_ingredients()
        {
            // unknown names are looked up online by the analysis: they must not be taken for a product's name
            Assert.AreEqual(TypedEntry.Kind.Ingredients, TypedEntry.Understand("Zzyzxine, Qwertol, Plumbate").kind);
        }

        [Test]
        public void An_empty_box_is_nothing()
        {
            Assert.AreEqual(TypedEntry.Kind.Nothing, TypedEntry.Understand("").kind);
            Assert.AreEqual(TypedEntry.Kind.Nothing, TypedEntry.Understand("   \n ").kind);
            Assert.AreEqual(TypedEntry.Kind.Nothing, TypedEntry.Understand(null).kind);
        }

        // ------------------------------------------------------------------ the web's answer about a typed product
        [Test]
        public void When_the_words_fit_several_products_the_app_asks_back()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(
                "{\"same\":false,\"question\":\"Which Maybelline lipstick? Add the name printed on it.\",\"notMakeup\":false}", new List<string>(), true);
            Assert.IsFalse(match.same);
            Assert.AreEqual("Which Maybelline lipstick? Add the name printed on it.", match.question);
            Assert.IsFalse(match.notMakeup);
            Assert.IsFalse(match.list.found);
        }

        [Test]
        public void Something_that_is_not_make_up_is_said_to_be_so()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom("{\"same\":false,\"notMakeup\":true,\"question\":\"\"}", new List<string>(), true);
            Assert.IsTrue(match.notMakeup);
            Assert.IsFalse(match.same);
        }

        [Test]
        public void A_typed_product_that_was_found_comes_with_its_name_as_sold_and_its_list()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(
                "{\"same\":true,\"brandName\":\"Careline\",\"productName\":\"Careline Oil Control Blush On\",\"cosmeticType\":\"Blush\",\"confidence\":0.8,\"question\":\"\",\"notMakeup\":false," +
                "\"page\":\"Careline | https://www.watsons.com.ph/careline-blush\",\"ingredients\":[\"Talc\",\"Mica\",\"Dimethicone\",\"Zinc Stearate\"]," +
                "\"sources\":[\"Careline | https://www.watsons.com.ph/careline-blush\"]}", new List<string>(), true, "test-model", "careline");
            Assert.IsTrue(match.same);
            Assert.AreEqual("Oil Control Blush On", match.productName);
            Assert.AreEqual("Careline", match.brandName);
            Assert.AreEqual("", match.question);
            Assert.IsTrue(match.list.found);
            Assert.AreEqual(4, match.list.ingredients.Count);
        }
    }
}
