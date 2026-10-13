using System.Collections.Generic;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks how the app READS what the AI answers during a scan (the AI itself is not asked here), and the rule for the
    /// shade behind a cap:
    ///
    ///  - Identify: brand and product name are kept apart, "cap on" is understood, and with the cap on a colour said
    ///    to be the make-up's own is not believed;
    ///  - ConfirmOnWeb: nothing counts as confirmed without a live web search; a list for a product that is only
    ///    similar is never taken; a list needs a source that is not an ingredient database;
    ///  - CapShadeWatch: the pack's colour is never the shade; a different colour that stays in view means the cap is off.
    /// </summary>
    public class EarlyAiTests
    {
        // ------------------------------------------------------------------ reading the first look
        [Test]
        public void The_first_look_is_read_with_cap_and_shade()
        {
            ProductAiAdvisor.Identity seen = ProductAiAdvisor.IdentityFrom(
                "{\"found\":true,\"productName\":\"SuperStay Matte Ink\",\"brandName\":\"Maybelline\",\"cosmeticType\":\"Lipstick\",\"capOn\":false," +
                "\"shadeHex\":\"#a8323e\",\"shadeFrom\":\"product\",\"shadeName\":\"20 Pioneer\",\"finish\":\"Matte\",\"confidence\":0.9}", "test-model");
            Assert.IsTrue(seen.found);
            Assert.AreEqual("SuperStay Matte Ink", seen.productName);
            Assert.AreEqual("Maybelline", seen.brandName);
            Assert.AreEqual("#A8323E", seen.shadeHex);
            Assert.AreEqual("product", seen.shadeFrom);
            Assert.AreEqual("matte", seen.finish);
            Assert.IsFalse(seen.capOn);
            Assert.AreEqual(0.9f, seen.confidence, 0.0001f);
            Assert.AreEqual("test-model", seen.model);
        }

        [Test]
        public void With_the_cap_on_a_colour_said_to_be_the_make_up_itself_is_not_believed()
        {
            ProductAiAdvisor.Identity seen = ProductAiAdvisor.IdentityFrom(
                "{\"found\":true,\"productName\":\"Velvet Tint\",\"brandName\":\"Vice\",\"cosmeticType\":\"Lip Tint\",\"capOn\":true,\"shadeHex\":\"#111111\",\"shadeFrom\":\"product\",\"confidence\":0.8}");
            Assert.IsTrue(seen.capOn);
            Assert.AreEqual("", seen.shadeHex, "the cap's colour is not the shade");
            Assert.AreEqual("", seen.shadeFrom);
        }

        [Test]
        public void With_the_cap_on_a_shade_shown_on_the_pack_is_taken()
        {
            ProductAiAdvisor.Identity seen = ProductAiAdvisor.IdentityFrom(
                "{\"found\":true,\"productName\":\"Velvet Tint\",\"brandName\":\"Vice\",\"cosmeticType\":\"Lip Tint\",\"capOn\":\"true\",\"shadeHex\":\"#B03A48\",\"shadeFrom\":\"packaging\",\"confidence\":0.8}");
            Assert.IsTrue(seen.capOn, "a yes written as a word counts too");
            Assert.AreEqual("#B03A48", seen.shadeHex);
            Assert.AreEqual("packaging", seen.shadeFrom);
        }

        [Test]
        public void A_shade_source_the_app_does_not_know_and_a_broken_colour_are_dropped()
        {
            ProductAiAdvisor.Identity seen = ProductAiAdvisor.IdentityFrom(
                "{\"found\":true,\"productName\":\"X\",\"brandName\":\"Y\",\"shadeHex\":\"red\",\"shadeFrom\":\"guess\",\"confidence\":7}");
            Assert.AreEqual("", seen.shadeHex);
            Assert.AreEqual("", seen.shadeFrom);
            Assert.AreEqual(1f, seen.confidence, "never more than 1");
        }

        [Test]
        public void The_brand_is_taken_out_of_the_product_name()
        {
            Assert.AreEqual("SuperStay Matte Ink", ProductAiAdvisor.WithoutBrand("Maybelline SuperStay Matte Ink", "Maybelline"));
            Assert.AreEqual("Fit Me Foundation", ProductAiAdvisor.WithoutBrand("MAYBELLINE - Fit Me Foundation", "Maybelline"));
            Assert.AreEqual("", ProductAiAdvisor.WithoutBrand("Maybelline", "maybelline"), "a name that is only the brand is no product name");
            Assert.AreEqual("Maybellinex Tint", ProductAiAdvisor.WithoutBrand("Maybellinex Tint", "Maybelline"), "only a whole word is the brand");
            Assert.AreEqual("Matte Ink", ProductAiAdvisor.WithoutBrand("Matte Ink", ""));
            Assert.AreEqual("", ProductAiAdvisor.WithoutBrand(null, "Maybelline"));
        }

        [Test]
        public void An_answer_with_only_the_brand_as_name_keeps_the_brand_and_no_name()
        {
            ProductAiAdvisor.Identity seen = ProductAiAdvisor.IdentityFrom("{\"found\":true,\"productName\":\"Careline\",\"brandName\":\"Careline\",\"cosmeticType\":\"Blush\",\"confidence\":0.7}");
            Assert.IsTrue(seen.found, "the brand and the kind are still something");
            Assert.AreEqual("", seen.productName);
            Assert.AreEqual("Careline", seen.brandName);
        }

        [Test]
        public void No_answer_or_not_found_is_nothing()
        {
            Assert.IsFalse(ProductAiAdvisor.IdentityFrom("").found);
            Assert.IsFalse(ProductAiAdvisor.IdentityFrom("sorry, I cannot see it").found);
            Assert.IsFalse(ProductAiAdvisor.IdentityFrom("{\"found\":false}").found);
            Assert.IsFalse(ProductAiAdvisor.IdentityFrom("{\"found\":true}").found, "found without a name, a brand or a kind");
        }

        // ------------------------------------------------------------------ reading the web check
        const string WebAnswer =
            "```json\n{\"same\":true,\"brandName\":\"Maybelline\",\"productName\":\"Maybelline SuperStay Matte Ink\",\"cosmeticType\":\"Lipstick\"," +
            "\"page\":\"SuperStay Matte Ink | https://www.maybelline.com/lip-makeup/superstay\",\"confidence\":0.85," +
            "\"ingredients\":[\"Dimethicone\",\"Trimethylsiloxysilicate\",\"Isododecane\",\"Nylon-611/Dimethicone Copolymer\"]," +
            "\"sources\":[\"SuperStay Matte Ink | https://www.maybelline.com/lip-makeup/superstay\"],\"note\":\"\"}\n```";

        [Test]
        public void The_web_check_gives_the_name_as_sold_and_the_list()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(WebAnswer, new List<string>(), true, "test-model", "Maybeline");
            Assert.IsTrue(match.same);
            Assert.AreEqual("Maybelline", match.brandName);
            Assert.AreEqual("SuperStay Matte Ink", match.productName, "without the brand in front");
            StringAssert.Contains("maybelline.com", match.page);
            Assert.IsTrue(match.list.found);
            Assert.AreEqual(4, match.list.ingredients.Count);
            Assert.AreEqual(1, match.list.sources.Count);
        }

        [Test]
        public void Without_a_live_web_search_nothing_is_confirmed()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(WebAnswer, new List<string>(), false);
            Assert.IsFalse(match.same, "the AI's memory is not the web");
            Assert.IsFalse(match.list.found);
        }

        [Test]
        public void A_product_that_is_only_similar_brings_no_list()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(
                "{\"same\":false,\"brandName\":\"Other\",\"productName\":\"Lookalike Ink\",\"ingredients\":[\"Aqua\",\"Glycerin\",\"Mica\"],\"sources\":[\"Shop | https://www.watsons.com.ph/x\"]}",
                new List<string>(), true);
            Assert.IsFalse(match.same);
            Assert.IsFalse(match.list.found);
            Assert.AreEqual(0, match.list.ingredients.Count);
        }

        [Test]
        public void A_list_whose_only_source_is_an_ingredient_database_is_not_taken()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(
                "{\"same\":true,\"brandName\":\"Vice\",\"productName\":\"Velvet Tint\",\"confidence\":0.8,\"ingredients\":[\"Aqua\",\"Glycerin\",\"Mica\"]," +
                "\"sources\":[\"INCIDecoder | https://incidecoder.com/products/vice-velvet-tint\"]}", new List<string>(), true);
            Assert.IsTrue(match.same, "the product can be the same");
            Assert.IsFalse(match.list.found, "but its list must come from a page that may carry one");
        }

        [Test]
        public void The_same_product_without_a_published_list_is_still_the_same()
        {
            ProductAiAdvisor.WebMatch match = ProductAiAdvisor.WebMatchFrom(
                "{\"same\":true,\"brandName\":\"Vice\",\"productName\":\"Velvet Tint\",\"confidence\":0.8,\"ingredients\":[],\"sources\":[]}", new List<string>(), true);
            Assert.IsTrue(match.same);
            Assert.IsFalse(match.list.found);
        }

        // ------------------------------------------------------------------ the shade behind a cap
        [Test]
        public void While_nothing_says_the_cap_is_on_the_watch_does_nothing()
        {
            var watch = new CapShadeWatch();
            Assert.IsFalse(watch.Hidden);
            for (int i = 0; i < 6; i++) Assert.IsFalse(watch.Look("#C0392B"));
        }

        [Test]
        public void The_packs_own_colour_never_counts()
        {
            var watch = new CapShadeWatch();
            watch.CapSeen("#1E4FA8");                        // a blue tube
            Assert.IsTrue(watch.Hidden);
            for (int i = 0; i < 8; i++) Assert.IsFalse(watch.Look("#2456AE"), "nearly the same blue: still the pack");
        }

        [Test]
        public void A_different_colour_that_stays_means_the_cap_is_off()
        {
            var watch = new CapShadeWatch();
            watch.CapSeen("#1E4FA8");
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsFalse(watch.Look("#B8323F"));
            Assert.IsTrue(watch.Look("#B22E3A"), "the same red three times in a row");
            Assert.AreEqual("#B5303C", watch.NewColour);
            Assert.IsTrue(watch.Hidden, "it stays hidden until the scan screen says the shade is known");
            watch.Solved();
            Assert.IsFalse(watch.Hidden);
        }

        [Test]
        public void A_colour_that_only_flashes_by_does_not_count()
        {
            var watch = new CapShadeWatch();
            watch.CapSeen("#1E4FA8");
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsFalse(watch.Look("#2456AE"));           // the pack again: the count starts anew
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsFalse(watch.Look("#3FA35B"));           // another colour: again from one
            Assert.IsFalse(watch.Look(""));                  // a picture without a clear colour says nothing
            Assert.IsFalse(watch.Look("#3FA35B"));
            Assert.IsTrue(watch.Look("#3FA35B"));
        }

        [Test]
        public void A_tube_without_a_clear_colour_is_handled_too()
        {
            var watch = new CapShadeWatch();
            watch.CapSeen("");                               // a black or silver tube: the camera named no colour
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsFalse(watch.Look("#B5303C"));
            Assert.IsTrue(watch.Look("#B5303C"));
        }

        [Test]
        public void A_new_scan_forgets_the_cap()
        {
            var watch = new CapShadeWatch();
            watch.CapSeen("#1E4FA8");
            watch.Reset();
            Assert.IsFalse(watch.Hidden);
            Assert.AreEqual("", watch.PackColour);
            Assert.IsFalse(watch.Look("#B5303C"));
        }
    }
}
