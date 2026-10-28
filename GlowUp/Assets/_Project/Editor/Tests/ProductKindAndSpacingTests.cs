using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Two faults of 27 October 2026 in finding a saved product from what the camera read:
    ///  - the pack prints "SUPER STAY", the catalogue holds "Superstay": no word matched, so a product that WAS saved (even the only
    ///    one of its brand) scored 0.75 to 0.82, below the "sure" line of 0.86, and was never taken;
    ///  - one brand and name saved twice in different kinds of make-up ("Fit Me": a foundation and a concealer) was taken silently,
    ///    whichever came first. The kind read on the pack decides when it can; otherwise the user chooses.
    /// </summary>
    public class ProductKindAndSpacingTests
    {
        const string Brand = "Maybelline New York";
        const string FrontOfThePack = "SUPER\nSTAY\nMAYBELLINE NEW YORK\nUP TO 30H\nLUMI-MATTE FOUNDATION\nSPF 16\n119\n";

        static ProductData P(string id, string name, string kind, int ingredients = 12)
        {
            var list = new List<string>();
            for (int i = 0; i < ingredients; i++) list.Add("Ingredient " + i);
            return new ProductData { id = id, brandName = Brand, name = name, cosmeticType = kind, ingredients = list, status = "Active" };
        }

        static ProductIndex Lines() => ProductIndex.Build(new[]
        {
            P("lumi", "Superstay Lumi-matte Foundation", "Foundation"),
            P("vinyl", "Superstay Vinyl Ink Longwear Liquid Lipstick", "Lipstick"),
            P("teddy", "Superstay Teddy Tint", "Lip Tint"),
            P("flex", "Superstay 30H Flex Powder Foundation", "Foundation"),
            P("fit", "Fit Me Matte + Poreless Foundation", "Foundation")
        });

        [Test]
        public void ASpacedNameOnThePackIsTheJoinedNameInTheList()
        {
            List<ProductIndex.Candidate> found = Lines().InText(FrontOfThePack, 3);

            Assert.AreEqual("lumi", found[0].product.id);
            Assert.IsTrue(found[0].exactName, "every word of the saved name is on the pack");
            Assert.GreaterOrEqual(found[0].score, ProductIndex.Sure);
        }

        [Test]
        public void TheOnlyProductOfABrandIsTakenFromItsPack()
        {
            ProductIndex only = ProductIndex.Build(new[] { P("lumi", "Superstay Lumi-matte Foundation", "Foundation") });

            ProductIndex.Candidate top = only.InText(FrontOfThePack, 1)[0];

            Assert.AreEqual("lumi", top.product.id);
            Assert.GreaterOrEqual(top.score, ProductIndex.Sure);
        }

        [Test]
        public void ASpacedNameTypedOrReadWholeIsTheSameName()
        {
            ProductIndex index = Lines();

            Assert.AreEqual("lumi", index.Exact(Brand, "Super Stay Lumi-Matte Foundation").id);
            List<ProductIndex.Candidate> found = index.Search(Brand, "Super Stay Lumi-Matte Foundation", 3);
            Assert.AreEqual("lumi", found[0].product.id);
            Assert.IsTrue(found[0].exactName);
        }

        [Test]
        public void AJoinedNameOnThePackIsTheSpacedNameInTheList()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("lumi", "Super Stay Lumi-Matte Foundation", "Foundation") });

            ProductIndex.Candidate top = index.InText("SUPERSTAY\nMAYBELLINE NEW YORK\nLUMI-MATTE FOUNDATION", 1)[0];

            Assert.AreEqual("lumi", top.product.id);
            Assert.GreaterOrEqual(top.score, ProductIndex.Sure);
        }

        [Test]
        public void JoiningWordsNeverMakesTwoDifferentNamesTheSame()
        {
            ProductIndex index = Lines();

            // a part of the name that exists in four products, said alone, does not pick one of them
            List<ProductIndex.Candidate> found = index.Search(Brand, "Super Stay", 5);
            Assert.IsFalse(found.Count > 0 && found[0].exactName, "'Super Stay' is the start of four different names");
            Assert.IsNull(index.Exact(Brand, "Super Stay"));
            Assert.AreEqual("fit", index.InText("MAYBELLINE NEW YORK\nFIT ME MATTE PORELESS FOUNDATION", 1)[0].product.id);
        }

        // ---- the real catalogue entry of 27 October 2026: "SPF 16" is printed small and the scan does not read it
        [Test]
        public void ANumberOrClaimOfTheNameThatWasNotReadDoesNotSinkTheMatch()
        {
            ProductIndex only = ProductIndex.Build(new[] { P("lumi", "SUPER STAY Lumi-Matte Foundation SPF 16", "Foundation") });

            ProductIndex.Candidate top = only.InText("SUPER\nSTAY\nMAYBELLINE NEW YORK\nUP TO 30H\nLUMI-MATTE FOUNDATION\n", 1)[0];

            Assert.AreEqual("lumi", top.product.id);
            Assert.IsTrue(top.exactName, "every word that matters is on the pack");
            Assert.IsTrue(top.leftOutWeak);
            Assert.GreaterOrEqual(top.score, ProductIndex.Sure);
        }

        [Test]
        public void TheWholeNameWithItsNumberIsStillExact()
        {
            ProductIndex only = ProductIndex.Build(new[] { P("lumi", "SUPER STAY Lumi-Matte Foundation SPF 16", "Foundation") });

            ProductIndex.Candidate top = only.InText(FrontOfThePack, 1)[0];

            Assert.IsTrue(top.exactName);
            Assert.IsFalse(top.leftOutWeak);
            Assert.GreaterOrEqual(top.score, ProductIndex.Sure);
        }

        [Test]
        public void TwoNamesThatDifferOnlyInANumberAreNotChosenByGuessing()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("h24", "Superstay 24H Foundation", "Foundation"), P("h30", "Superstay 30H Foundation", "Foundation") });

            // the number was not read: nobody can say which one it is
            ProductIndex.Candidate unsure = index.InText("MAYBELLINE NEW YORK\nSUPERSTAY\nFOUNDATION", 1)[0];
            Assert.Less(unsure.score, ProductIndex.Sure);

            // the number was read: it decides
            ProductIndex.Candidate sure = index.InText("MAYBELLINE NEW YORK\nSUPERSTAY 30H\nFOUNDATION", 1)[0];
            Assert.AreEqual("h30", sure.product.id);
            Assert.GreaterOrEqual(sure.score, ProductIndex.Sure);
        }

        [Test]
        public void ANameOfOneWordIsNotMadeExactByLeavingOutItsNumber()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("a", "Lash 24", "Mascara") });

            ProductIndex.Candidate top = index.InText("MAYBELLINE NEW YORK\nLASH", 1)[0];

            Assert.IsFalse(top.exactName, "one word that matters proves too little");
        }

        // ---- the same brand and name in two kinds of make-up
        static ProductIndex FitMe() => ProductIndex.Build(new[]
        {
            P("fit_f", "Fit Me", "Foundation"),
            P("fit_c", "Fit Me", "Concealer")
        });

        [Test]
        public void TheSameNameInTwoKindsIsNotTakenForGrantedWhenThePackDoesNotSayWhich()
        {
            ProductIndex.Candidate top = FitMe().InText("MAYBELLINE NEW YORK\nFIT ME", 1)[0];

            Assert.IsFalse(top.exactName);
            Assert.Less(top.score, ProductIndex.Sure);
            Assert.AreEqual(2, FitMe().SameNameDifferentKind(Brand, "Fit Me").Count, "the user is asked");
        }

        [Test]
        public void TheKindReadOnThePackChoosesBetweenTheSameName()
        {
            ProductIndex index = FitMe();

            ProductIndex.Candidate top = index.InText("MAYBELLINE NEW YORK\nFIT ME\nCONCEALER", 1, "Concealer")[0];
            Assert.AreEqual("fit_c", top.product.id);
            Assert.GreaterOrEqual(top.score, ProductIndex.Sure);
            Assert.AreEqual(0, index.SameNameDifferentKind(Brand, "Fit Me", "Concealer").Count, "no question: the pack said which");
            Assert.AreEqual("fit_c", index.Exact(Brand, "Fit Me", "Concealer").id);
            Assert.AreEqual("fit_f", index.Search(Brand, "Fit Me", 2, "Foundation")[0].product.id);
        }

        [Test]
        public void AKindThatFitsNeitherLeavesTheQuestion()
        {
            ProductIndex index = FitMe();

            Assert.IsFalse(index.InText("MAYBELLINE NEW YORK\nFIT ME", 1, "Lipstick")[0].exactName);
            Assert.AreEqual(2, index.SameNameDifferentKind(Brand, "Fit Me", "Lipstick").Count);
        }

        [Test]
        public void LiquidFoundationFitsFoundationAndTheOtherWayRound()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("a", "Fit Me", "Liquid Foundation"), P("b", "Fit Me", "Concealer") });

            Assert.AreEqual("a", index.Exact(Brand, "Fit Me", "Foundation").id);
            Assert.AreEqual(0, index.SameNameDifferentKind(Brand, "Fit Me", "Foundation").Count);
        }

        [Test]
        public void TheSameProductListedTwiceWithOneKindIsNoQuestion()
        {
            ProductIndex index = ProductIndex.Build(new[] { P("a", "Fit Me", "Foundation"), P("b", "Fit Me", "Foundation") });

            Assert.AreEqual(0, index.SameNameDifferentKind(Brand, "Fit Me").Count);
        }

        [Test]
        public void BothKindsAreOfferedInTheShortlist()
        {
            List<ScanShortlist.Entry> offered = ScanShortlist.Typed(FitMe(), "Fit Me", Brand);

            Assert.AreEqual(2, offered.FindAll(e => e.product != null).Count, "a foundation and a concealer are two choices, not one");
            Assert.AreEqual(2, new HashSet<string>(offered.ConvertAll(e => e.cosmeticType)).Count);
        }
    }
}
