using System.Collections.Generic;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks where an ingredient list may come from (ListSources): what kind of place a page is, the order of trust,
    /// that an ingredient database is never the source of one product's list, that a list without a page the app can
    /// show is not accepted, and how two lists of one product are compared ("Matched 2 sources").
    /// </summary>
    public class ListSourceTests
    {
        [SetUp]
        public void SetUp()
        {
            IngredientKnowledge.FuzzyThreshold = 0.85f;
            ListSources.AgreeFrom = 0.85f; ListSources.MinimumToCompare = 4; ListSources.NamesShown = 5;
        }

        [Test]
        public void ASourceIsATitleAndAnAddress()
        {
            Assert.AreEqual("https://www.maybelline.com.ph/face/fit-me", ListSources.AddressOf("Fit Me | https://www.maybelline.com.ph/face/fit-me"));
            Assert.AreEqual("Fit Me", ListSources.TitleOf("Fit Me | https://www.maybelline.com.ph/face/fit-me"));
            Assert.AreEqual("https://example.com/a", ListSources.AddressOf("see https://example.com/a."), "the full stop after it is not part of the address");
            Assert.AreEqual("", ListSources.AddressOf("the back of the pack"));
            Assert.AreEqual("the back of the pack", ListSources.TitleOf("the back of the pack"));
            Assert.AreEqual("", ListSources.TitleOf("https://example.com/a"));
            Assert.AreEqual("www.maybelline.com.ph", ListSources.HostOf("https://WWW.Maybelline.com.ph/face?x=1"));
            Assert.AreEqual("", ListSources.AddressOf(null));
        }

        [Test]
        public void TheKindOfPlaceIsToldFromItsAddress()
        {
            Assert.AreEqual(ListSources.Kind.BrandSite, ListSources.KindOf("Fit Me | https://www.maybelline.com.ph/face/fit-me", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.BrandSite, ListSources.KindOf("Fit Me | https://www.maybelline.com/face", "Maybelline New York"), "the first word of a long brand name is enough");
            Assert.AreEqual(ListSources.Kind.BrandSite, ListSources.KindOf("Butter Gloss | https://www.nyxcosmetics.com/lip/butter-gloss", "NYX"), "a three-letter brand at the start of the site's name");
            Assert.AreEqual(ListSources.Kind.Other, ListSources.KindOf("Review | https://www.onyxbeautyblog.com/post", "NYX"), "three letters in the middle of a name prove nothing");
            Assert.AreEqual(ListSources.Kind.Retailer, ListSources.KindOf("Watsons | https://www.watsons.com.ph/fit-me/p/BP_123", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.Retailer, ListSources.KindOf("Maybelline at Lazada | https://www.lazada.com.ph/shop/maybelline/fit-me", "Maybelline"), "the brand's shelf in a shop is still the shop");
            Assert.AreEqual(ListSources.Kind.OpenBeautyFacts, ListSources.KindOf("Open Beauty Facts | https://world.openbeautyfacts.org/product/3600531324506", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.Database, ListSources.KindOf("INCIDecoder | https://incidecoder.com/products/maybelline-fit-me", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.Database, ListSources.KindOf("EWG Skin Deep | https://www.ewg.org/skindeep/products/1", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.Other, ListSources.KindOf("A beauty blog | https://beautyblog.example/fit-me-review", "Maybelline"));
            Assert.AreEqual(ListSources.Kind.Unusable, ListSources.KindOf("the brand's leaflet", "Maybelline"), "nothing the app could show");
            Assert.AreEqual(ListSources.Kind.Unusable, ListSources.KindOf("", "Maybelline"));
        }

        [Test]
        public void ASearchEnginesRedirectIsJudgedByThePageItLeadsTo()
        {
            const string redirect = " | https://vertexaisearch.cloud.google.com/grounding-api-redirect/AbCdEf123";
            Assert.AreEqual(ListSources.Kind.BrandSite, ListSources.KindOf("absidy.com" + redirect, "Absidy"));
            Assert.AreEqual(ListSources.Kind.Database, ListSources.KindOf("incidecoder.com" + redirect, "Absidy"));
            Assert.AreEqual(ListSources.Kind.Retailer, ListSources.KindOf("watsons.com.ph" + redirect, "Absidy"));
            Assert.AreEqual(ListSources.Kind.Other, ListSources.KindOf("https://vertexaisearch.cloud.google.com/grounding-api-redirect/AbCdEf123", "Absidy"), "no title: the page is not known");
        }

        [Test]
        public void TheOrderOfTrust()
        {
            ListSources.Kind[] bestFirst =
            {
                ListSources.Kind.Pack, ListSources.Kind.Inventory, ListSources.Kind.BrandSite, ListSources.Kind.Retailer,
                ListSources.Kind.OpenBeautyFacts, ListSources.Kind.Other, ListSources.Kind.Database, ListSources.Kind.Unusable
            };
            for (int i = 1; i < bestFirst.Length; i++)
                Assert.Less(ListSources.Rank(bestFirst[i - 1]), ListSources.Rank(bestFirst[i]), bestFirst[i - 1] + " comes before " + bestFirst[i]);
            Assert.IsFalse(ListSources.CanCarryAList(ListSources.Kind.Database), "a database explains ingredients; it is not the source of one product's list");
            Assert.IsFalse(ListSources.CanCarryAList(ListSources.Kind.Unusable));
            Assert.IsTrue(ListSources.CanCarryAList(ListSources.Kind.OpenBeautyFacts));
        }

        [Test]
        public void OnlySourcesThatCanBeShownCountAndTheBestComesFirst()
        {
            var given = new List<string>
            {
                "INCIDecoder | https://incidecoder.com/products/maybelline-fit-me",
                "Open Beauty Facts | https://world.openbeautyfacts.org/product/3600531324506",
                "Watsons | https://www.watsons.com.ph/fit-me/p/BP_123",
                "the back of the pack",
                "Fit Me | https://www.maybelline.com.ph/face/fit-me",
                "Fit Me again | https://www.maybelline.com.ph/face/fit-me",
                "A beauty blog | https://beautyblog.example/fit-me-review"
            };
            List<string> usable = ListSources.Usable(given, "Maybelline");
            Assert.AreEqual(4, usable.Count, "the database, the source without an address and the page given twice are left out");
            StringAssert.Contains("maybelline.com.ph", usable[0]);
            StringAssert.Contains("watsons", usable[1]);
            StringAssert.Contains("openbeautyfacts", usable[2]);
            StringAssert.Contains("beautyblog", usable[3]);

            // an answer that rests on databases only, or names no page at all: no usable source, so no list
            Assert.AreEqual(0, ListSources.Usable(new[] { "INCIDecoder | https://incidecoder.com/products/x", "CosIng | https://ec.europa.eu/growth/tools-databases/cosing/details/1" }, "X").Count);
            Assert.AreEqual(0, ListSources.Usable(new[] { "my own knowledge" }, "X").Count);
            Assert.AreEqual(0, ListSources.Usable(null, "X").Count);
        }

        [Test]
        public void TwoListsThatSayTheSameAgree()
        {
            string[] pack = { "Talc", "Mica", "Dimethicone", "Zinc Stearate", "Silica", "Tocopherol" };
            string[] shop = { "TALC", "mica", "Silica", "Dimethicone", "zinc stearate", "Tocopherol" };          // other capitals, another order
            ListSources.Comparison same = ListSources.Compare(pack, shop);
            Assert.AreEqual(6, same.shared);
            Assert.AreEqual(1f, same.Agreement, 0.001f);
            Assert.IsTrue(same.Agree);
            Assert.AreEqual(0, same.onlyInFirstCount + same.onlyInSecondCount);

            // "Aqua" and "Water" are one ingredient, and a name the camera misread by a letter is still that ingredient
            ListSources.Comparison names = ListSources.Compare(new[] { "Aqua", "Glycerin", "Niacinarnide", "Dimethicone" }, new[] { "Water", "Glycerin", "Niacinamide", "Dimethicone" });
            Assert.AreEqual(4, names.shared);
            Assert.IsTrue(names.Agree);

            // the same name twice in one list counts once
            Assert.AreEqual(4, ListSources.Compare(new[] { "Talc", "Talc", "Mica", "Silica", "Dimethicone" }, new[] { "Talc", "Mica", "Silica", "Dimethicone" }).firstCount);
        }

        [Test]
        public void ListsThatDifferSayWhatDiffers()
        {
            string[] brand = { "Talc", "Mica", "Dimethicone", "Zinc Stearate", "Silica", "Tocopherol", "Phenoxyethanol", "Fragrance" };
            string[] other = { "Talc", "Mica", "Dimethicone", "Zinc Stearate", "Silica", "Methylparaben" };
            ListSources.Comparison differs = ListSources.Compare(brand, other);
            Assert.AreEqual(5, differs.shared);
            Assert.IsFalse(differs.Agree, "5 of 8 is not the same list");
            Assert.IsTrue(differs.Comparable);
            Assert.AreEqual(3, differs.onlyInFirstCount);
            CollectionAssert.AreEquivalent(new[] { "Tocopherol", "Phenoxyethanol", "Fragrance" }, differs.onlyInFirst);
            CollectionAssert.AreEquivalent(new[] { "Methylparaben" }, differs.onlyInSecond);

            // one name in twenty read differently is still the same list (the camera is not perfect)
            var longList = new List<string>(); var nearly = new List<string>();
            string[] twenty = { "Talc", "Mica", "Dimethicone", "Zinc Stearate", "Silica", "Tocopherol", "Phenoxyethanol", "Glycerin", "Niacinamide", "Squalane",
                                "Kaolin", "Caprylyl Glycol", "Titanium Dioxide", "Zinc Oxide", "Beeswax", "Lanolin", "Panthenol", "Allantoin", "Limonene", "Linalool" };
            longList.AddRange(twenty); nearly.AddRange(twenty); nearly[19] = "Citronellol";
            Assert.IsTrue(ListSources.Compare(longList, nearly).Agree);

            // too short to say anything
            ListSources.Comparison tiny = ListSources.Compare(new[] { "Talc", "Mica" }, new[] { "Talc", "Mica" });
            Assert.IsFalse(tiny.Comparable);
            Assert.IsFalse(tiny.Agree);
            Assert.IsFalse(ListSources.Compare(null, brand).Agree);
        }

        [Test]
        public void WhatDependsOnTheShadeIsLeftOutOfTheComparison()
        {
            Assert.IsTrue(ListSources.DependsOnShade("CI 77491"));
            Assert.IsTrue(ListSources.DependsOnShade("ci77891"));
            Assert.IsTrue(ListSources.DependsOnShade("77499"));
            Assert.IsFalse(ListSources.DependsOnShade("Mica"));
            Assert.IsFalse(ListSources.DependsOnShade("CI 7749"), "four digits are not a colour number");
            Assert.IsFalse(ListSources.DependsOnShade("PEG-12345 Dimethicone"));

            List<string> baseList = ListSources.BaseList(new[] { "Talc", "Mica", "CI 77491", "Dimethicone", "Iron Oxides", "Silica" }, new[] { "iron oxides" });
            CollectionAssert.AreEqual(new[] { "Talc", "Mica", "Dimethicone", "Silica" }, baseList, "the colour number and the name behind 'may contain' are apart");
            Assert.AreEqual(0, ListSources.BaseList(null).Count);

            // two shades of one product: the same base, other colour numbers -> the same list
            string[] rose = { "Talc", "Mica", "Dimethicone", "Silica", "CI 15850", "CI 77491" };
            string[] nude = { "Talc", "Mica", "Dimethicone", "Silica", "CI 77492", "CI 77499", "CI 77891" };
            Assert.IsTrue(ListSources.Compare(ListSources.BaseList(rose), ListSources.BaseList(nude)).Agree);
        }

        [Test]
        public void ASecondPlaceThatAgreesIsNamedBeforeOneThatDiffers()
        {
            var shown = new List<string> { "Talc", "Mica", "Dimethicone", "Zinc Stearate", "Silica", "Tocopherol" };
            var sameThere = new ListSources.Other { kind = ListSources.Kind.OpenBeautyFacts, source = "Open Beauty Facts | https://world.openbeautyfacts.org/product/1", dateUnix = 1777807269,
                                                    names = new List<string> { "TALC", "MICA", "DIMETHICONE", "ZINC STEARATE", "SILICA", "TOCOPHEROL" } };
            var otherThere = new ListSources.Other { kind = ListSources.Kind.Inventory, names = new List<string> { "Talc", "Mica", "Kaolin", "Beeswax", "Lanolin", "Glycerin" } };
            var tooShort = new ListSources.Other { kind = ListSources.Kind.Inventory, names = new List<string> { "Talc" } };

            ListSources.Check matched = ListSources.Decide(shown, new[] { otherThere, sameThere });
            Assert.AreEqual(ListSources.Matched, matched.outcome, "one place agrees: that is the good news worth showing");
            Assert.AreSame(sameThere, matched.with);

            ListSources.Check differs = ListSources.Decide(shown, new[] { otherThere });
            Assert.AreEqual(ListSources.Differs, differs.outcome);
            Assert.AreSame(otherThere, differs.with);
            Assert.AreEqual(4, differs.comparison.onlyInFirstCount);
            Assert.AreEqual(4, differs.comparison.onlyInSecondCount);

            Assert.AreEqual("", ListSources.Decide(shown, new[] { tooShort }).outcome, "a list of one name cannot be compared");
            Assert.AreEqual("", ListSources.Decide(shown, new ListSources.Other[0]).outcome);
            Assert.AreEqual("", ListSources.Decide(null, new[] { sameThere }).outcome);

            // two places agree: the more trusted one is named
            var inventorySame = new ListSources.Other { kind = ListSources.Kind.Inventory, names = new List<string>(shown) };
            Assert.AreSame(inventorySame, ListSources.Decide(shown, new[] { sameThere, inventorySame }).with);
        }

        [Test]
        public void TheSecondOpinionsAnswerIsUnderstood()
        {
            const string found = "{\"code\":\"3600531469498\",\"product\":{\"brands\":\"Maybelline, L'OREAL\",\"ingredients_text\":\"DIMETHICONE, TRIMETHYLSILOXYSILICATE, ISODODECANE\"," +
                                 "\"ingredients_text_en\":\"\",\"last_modified_t\":1777807801,\"product_name\":\"Maybelline New York SuperStay\"},\"status\":1,\"status_verbose\":\"product found\"}";
            OpenBeautyFacts.Result r = OpenBeautyFacts.Parse(found, "3600531469498");
            Assert.IsTrue(r.answered);
            Assert.IsTrue(r.found);
            Assert.AreEqual("Maybelline", r.brandName, "the first of several brands");
            Assert.AreEqual("Maybelline New York SuperStay", r.productName);
            StringAssert.StartsWith("DIMETHICONE", r.ingredientText);
            Assert.AreEqual(1777807801L, r.lastEditedUnix);
            Assert.AreEqual("https://world.openbeautyfacts.org/product/3600531469498", r.Page);
            Assert.AreEqual(ListSources.Kind.OpenBeautyFacts, ListSources.KindOf(r.Source, "Maybelline"));

            // the product is there, but nobody typed its list in: answered, and nothing to use
            const string noList = "{\"code\":\"4059729233301\",\"product\":{\"brands\":\"Essence\",\"product_name\":\"Crystal power lipstick\",\"last_modified_t\":1699027044},\"status\":1}";
            r = OpenBeautyFacts.Parse(noList, "4059729233301");
            Assert.IsTrue(r.answered);
            Assert.IsFalse(r.found);

            // not there at all
            r = OpenBeautyFacts.Parse("{\"code\":\"0000000000017\",\"status\":0,\"status_verbose\":\"no code or invalid code\"}", "0000000000017");
            Assert.IsTrue(r.answered);
            Assert.IsFalse(r.found);

            // an answer that is not one (an error page): "did not answer", so it is asked again another time
            Assert.IsFalse(OpenBeautyFacts.Parse("<html>Service unavailable</html>", "3600531469498").answered);
            Assert.IsFalse(OpenBeautyFacts.Parse(null, "3600531469498").answered);

            Assert.AreEqual("3600531469498", OpenBeautyFacts.Digits(" 3 600531 469498 "));
            Assert.AreEqual("", OpenBeautyFacts.Digits("12345"), "too short to be a product barcode");
            Assert.AreEqual("", OpenBeautyFacts.Digits(null));
        }

        [Test]
        public void ASimilarFdaRecordBecomesTheUsersAnswer()
        {
            var mine = new GlowUp.Data.ScanRecord { fdaStatus = PhFdaVerification.Similar, fdaChecked = true, fdaNumber = "NN-1", fdaProductName = "Absidy Powder Petal", fdaValidUntil = "2099-04-16 00:00:00" };
            Assert.IsTrue(PhFdaVerification.ConfirmSimilar(mine, true));
            Assert.AreEqual(PhFdaVerification.Registered, PhFdaVerification.StatusOf(mine));
            Assert.AreEqual("NN-1", mine.fdaNumber, "the record stays with the scan");

            var ended = new GlowUp.Data.ScanRecord { fdaStatus = PhFdaVerification.Similar, fdaChecked = true, fdaNumber = "NN-2", fdaValidUntil = "2020-01-31 00:00:00" };
            PhFdaVerification.ConfirmSimilar(ended, true);
            Assert.AreEqual(PhFdaVerification.Expired, PhFdaVerification.StatusOf(ended), "it is the user's product, but its registration has ended");

            var notMine = new GlowUp.Data.ScanRecord { fdaStatus = PhFdaVerification.Similar, fdaChecked = true, fdaNumber = "NN-3", fdaProductName = "Something Else", fdaCompany = "A Company" };
            Assert.IsTrue(PhFdaVerification.ConfirmSimilar(notMine, false));
            Assert.AreEqual(PhFdaVerification.None, PhFdaVerification.StatusOf(notMine));
            Assert.AreEqual("", notMine.fdaNumber);
            Assert.AreEqual("", PhFdaVerification.RecordNameOf(notMine));

            // only a similar record can be answered about
            var registered = new GlowUp.Data.ScanRecord { fdaStatus = PhFdaVerification.Registered, fdaNumber = "NN-4" };
            Assert.IsFalse(PhFdaVerification.ConfirmSimilar(registered, false));
            Assert.AreEqual("NN-4", registered.fdaNumber);
            Assert.IsFalse(PhFdaVerification.ConfirmSimilar(null, true));
        }
    }
}
