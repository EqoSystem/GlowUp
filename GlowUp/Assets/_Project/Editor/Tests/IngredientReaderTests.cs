using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the ingredient reader (IngredientTextParser, IngredientStoplist, the matching in IngredientKnowledge) with
    /// messy label texts of the kind a phone camera really delivers. Everything runs without the internet: the
    /// ingredients used are in the app's built-in list.
    /// </summary>
    public class IngredientReaderTests
    {
        [SetUp]
        public void SetUp()
        {
            IngredientKnowledge.FuzzyThreshold = 0.85f;
            ProductAnalysisEngine.MinimumCheckedShare = 0.5f;
            IngredientStoplist.MaximumLength = 70;
            IngredientTextParser.SetNotIngredients(null);
        }

        static void AssertNames(IEnumerable<string> expected, List<string> actual, string what)
        {
            CollectionAssert.AreEqual(new List<string>(expected), actual, what + ": got [" + string.Join(", ", actual) + "]");
        }

        // ------------------------------------------------------------------------------------ whole labels
        [Test]
        public void ALabelWithEverythingRoundTheList()
        {
            var label = IngredientTextParser.Read(
                "DIRECTIONS: Apply evenly on the face.\n" +
                "INGREDIENTS: TALC, MICA, DIMETHICONE, ZINC STEARATE, SILICA, TOCOPHERYL ACETATE, PHENOXYETHANOL, FRAGRANCE (PARFUM). " +
                "MAY CONTAIN: CI 77491, CI 77492, CI 77499, CI 77891.\n" +
                "NET WT. 8.5 g / 0.29 OZ\nMADE IN CHINA\nwww.somebrand.com  Batch L2304A  EXP 01/2028");

            Assert.IsTrue(label.anchorFound);
            AssertNames(new[] { "Talc", "Mica", "Dimethicone", "Silica", "Vitamin E", "Phenoxyethanol", "Fragrance", "Iron Oxides", "Titanium Dioxide" }, label.ingredients, "known ingredients");
            AssertNames(new[] { "Zinc Stearate" }, label.unrecognized, "names the app does not know");
            AssertNames(new[] { "Iron Oxides", "Titanium Dioxide" }, label.mayContain, "may contain");
            foreach (string name in label.inOrder)
            {
                StringAssert.DoesNotContain("NET", name.ToUpperInvariant());
                StringAssert.DoesNotContain("CHINA", name.ToUpperInvariant());
                StringAssert.DoesNotContain("APPLY", name.ToUpperInvariant());
                StringAssert.DoesNotContain("L2304A", name.ToUpperInvariant());
            }
        }

        [Test]
        public void ALabelWithoutTheWordIngredientsAndWithTextGluedToBothEnds()
        {
            var label = IngredientTextParser.Read("POWDER 8.5g Talc, Mica, Silica\nDimethicone, Kaolin, Phenoxyethanol 12M");
            Assert.IsFalse(label.anchorFound);
            AssertNames(new[] { "Talc", "Mica", "Silica", "Dimethicone", "Kaolin", "Phenoxyethanol" }, label.ingredients, "known ingredients");
            Assert.IsEmpty(label.unrecognized);
        }

        [Test]
        public void LabelWordingInsideTheListIsThrownAway()
        {
            var label = IngredientTextParser.Read("Ingredients: Water, Glycerin, New Formula, Paraben Free, L2304A, 30 ml, 100% Vegan, Long Lasting Colour, 0917 123 4567, Talc");
            AssertNames(new[] { "Water", "Glycerin", "Talc" }, label.ingredients, "known ingredients");
            Assert.IsEmpty(label.unrecognized, "none of the label wording may end up as an unrecognised ingredient: [" + string.Join(", ", label.unrecognized) + "]");
            Assert.AreEqual(7, label.rejected.Count, string.Join(" | ", label.rejected));
        }

        [Test]
        public void TheListStopsWhereTheLabelGoesOnToSomethingElse()
        {
            var label = IngredientTextParser.Read("INGREDIENTS: WATER, GLYCERIN, TALC. WARNING: FOR EXTERNAL USE ONLY. KEEP OUT OF REACH OF CHILDREN. DISTRIBUTED BY SOME COMPANY INC, MANILA, PHILIPPINES");
            AssertNames(new[] { "Water", "Glycerin", "Talc" }, label.inOrder, "names");
        }

        [Test]
        public void ASecondListInAnotherLanguageIsNotReadTwice()
        {
            var label = IngredientTextParser.Read("INGREDIENTS: WATER, TALC, MICA. INGREDIENTES: AGUA, TALCO, MICA");
            AssertNames(new[] { "Water", "Talc", "Mica" }, label.inOrder, "names");
        }

        [Test]
        public void TheFrontOfAPackIsNotAnIngredientList()
        {
            var label = IngredientTextParser.Read("MAYBELLINE NEW YORK\nFIT ME\nMATTE + PORELESS\nPOWDER\n120 CLASSIC IVORY\n8.5 g");
            Assert.IsEmpty(label.ingredients, string.Join(", ", label.ingredients));
        }

        [Test]
        public void ATypedListWithOneNamePerLine()
        {
            var label = IngredientTextParser.Read("water\nglycerin\nparfum\n");
            AssertNames(new[] { "Water", "Glycerin", "Fragrance" }, label.ingredients, "known ingredients");
        }

        // ------------------------------------------------------------------------------------ single pieces
        [Test]
        public void BracketsAndSlashesGiveOneIngredient()
        {
            var label = IngredientTextParser.Read(
                "INGREDIENTS: AQUA/WATER/EAU, BUTYROSPERMUM PARKII (SHEA) BUTTER, CAPRYLIC/CAPRIC TRIGLYCERIDE, IRON OXIDES (CI 77491), CI 77492, " +
                "TITANIUM DIOXIDE [NANO], MINERAL OIL / PARAFFINUM LIQUIDUM / HUILE MINERALE");
            AssertNames(new[] { "Water", "Shea Butter", "Caprylic/Capric Triglyceride", "Iron Oxides", "Titanium Dioxide", "Mineral Oil" }, label.ingredients, "known ingredients");
            Assert.IsEmpty(label.unrecognized, string.Join(", ", label.unrecognized));
        }

        [Test]
        public void ReadingMistakesOfTheCameraAreForgiven()
        {
            var label = IngredientTextParser.Read("Ingredients: Glycerln, Niacinarnide, Phenoxyethano1, Tocopheryl Acetale, D1methicone, SodiumHyaluronate, DIMETHI-\nCONE");
            AssertNames(new[] { "Glycerin", "Niacinamide", "Phenoxyethanol", "Vitamin E", "Dimethicone", "Hyaluronic Acid" }, label.ingredients, "known ingredients");
            Assert.IsEmpty(label.unrecognized, string.Join(", ", label.unrecognized));
        }

        [Test]
        public void ANameThatWrapsOverTwoLinesIsPutTogether()
        {
            var label = IngredientTextParser.Read("Ingredients: Water, Caprylic/Capric\nTriglyceride, Butylene\nGlycol, Talc");
            AssertNames(new[] { "Water", "Caprylic/Capric Triglyceride", "Butylene Glycol", "Talc" }, label.ingredients, "known ingredients");
        }

        [Test]
        public void TwoKnownNamesWhoseCommaWasMissedAreSeparated()
        {
            AssertNames(new[] { "Water", "Glycerin", "Niacinamide", "Talc" }, IngredientTextParser.Read("Ingredients: Water Glycerin Niacinamide, Talc").ingredients, "in one line");
            AssertNames(new[] { "Water", "Glycerin", "Niacinamide", "Talc" }, IngredientTextParser.Read("Ingredients: Water, Glycerin\nNiacinamide, Talc").ingredients, "at a line break");
        }

        [Test]
        public void ANameTheAppDoesNotKnowIsKeptApartAndNotCountedAsAnIngredient()
        {
            var label = IngredientTextParser.Read("Ingredients: Water, Isododecane, Trimethylsiloxysilicate, Talc");
            AssertNames(new[] { "Water", "Talc" }, label.ingredients, "known ingredients");
            AssertNames(new[] { "Isododecane", "Trimethylsiloxysilicate" }, label.unrecognized, "not recognised");
            AssertNames(new[] { "Water", "Isododecane", "Trimethylsiloxysilicate", "Talc" }, label.inOrder, "order of the label");
        }

        [Test]
        public void AClaimIsNotTheIngredientItNames()
        {
            var label = IngredientTextParser.Read("Ingredients: Water, Fragrance Free, Paraben-Free, Alcohol Free, Talc");
            AssertNames(new[] { "Water", "Talc" }, label.inOrder, "names");
            Assert.AreEqual(3, label.rejected.Count);
        }

        [Test]
        public void ABrandOrProductNameIsNeverAnIngredient()
        {
            IngredientTextParser.SetNotIngredients(new[] { "Maybelline", "Fit Me" });
            var label = IngredientTextParser.Read("Ingredients: Maybelline, Fit Me, Water, Talc");
            AssertNames(new[] { "Water", "Talc" }, label.inOrder, "names");
        }

        [Test]
        public void AFamilyIsRecognisedButNotCleared()
        {
            var label = IngredientTextParser.Read("Ingredients: Water, Camellia Sinensis Leaf Extract, CI 15850, Isobutylparaben");
            AssertNames(new[] { "Water", "Camellia Sinensis Leaf Extract", "CI 15850", "Isobutylparaben" }, label.ingredients, "known ingredients and families");

            ScanRecord record = ProductAnalysisEngine.Analyse(label.ingredients, "", new UserProfileData { skinType = "Normal" }, null, null, null, null, label.unrecognized);
            Assert.AreEqual("unknown", record.findings.Find(f => f.name == "Camellia Sinensis Leaf Extract").level, "a plant extract the app does not know itself is not cleared");
            Assert.AreEqual("unknown", record.findings.Find(f => f.name == "CI 15850").level, "a colourant the app does not know itself is not cleared");
        }

        // ------------------------------------------------------------------------------------ matching
        [Test]
        public void OneChemicalIsNeverTakenForAnother()
        {
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("ethylparaben", "methylparaben", 0.85f), "another first letter is another chemical");
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("ci 77491", "ci 77492", 0.85f), "digits must be the same");
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("laureth 7", "laureth 4", 0.85f));
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("lauric acid", "lactic acid", 0.85f), "short words must be exact");
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("dimethiconol", "dimethicone", 0.85f));
            Assert.AreEqual(1, IngredientKnowledge.EditsBetween("salicyllc acid", "salicylic acid", 0.85f));
            Assert.AreEqual(0, IngredientKnowledge.EditsBetween("salicyllc acid", "salicylic acid", 1f), "threshold 1 = exact names only");

            Assert.AreNotEqual("Lactic Acid", IngredientKnowledge.Resolve("Lauric Acid")?.name);
            Assert.IsNull(IngredientKnowledge.Resolve("Dimethiconol"), "Dimethiconol is not Dimethicone");
            Assert.AreEqual("Methylparaben", IngredientKnowledge.Resolve("methylparaben").name);
            Assert.AreEqual("Ethylparaben", IngredientKnowledge.Resolve("ETHYLPARABEN").name);
        }

        [Test]
        public void TheForgivenessCanBeTunedDownToExactNamesOnly()
        {
            IngredientKnowledge.FuzzyThreshold = 1f;
            Assert.IsNull(IngredientKnowledge.Resolve("Tocopheryl Acetale"));
            IngredientKnowledge.FuzzyThreshold = 0.85f;
            Assert.AreEqual("Vitamin E", IngredientKnowledge.Resolve("Tocopheryl Acetale").name);
        }

        [Test]
        public void TheStoplistOnlyMatchesWholeWords()
        {
            Assert.AreEqual("ml", IngredientStoplist.Hit("30 ML"));
            Assert.IsNull(IngredientStoplist.Hit("Emulsifying Wax"), "'ml' is not inside 'Emulsifying'");
            Assert.AreEqual("free", IngredientStoplist.Hit("Paraben-Free"));
            Assert.AreEqual("made in", IngredientStoplist.Hit("MADE IN KOREA"));
            Assert.IsNull(IngredientStoplist.Hit("Red 7 Lake"));
            Assert.IsNull(IngredientStoplist.Hit("Japan Wax"));
        }

        [Test]
        public void ShapesThatCannotBeAnIngredientName()
        {
            foreach (string junk in new[] { "L2304A", "12345", "0917 123 4567", "A", "30 g", "this is a whole sentence about how to use the product well", "EXP0126" })
                Assert.IsTrue(IngredientStoplist.WrongShape(junk), junk);
            foreach (string real in new[] { "CI 77491", "BHT", "Talc", "PEG-40 Hydrogenated Castor Oil", "C12-15 Alkyl Benzoate", "1,2-Hexanediol", "Polysorbate 20",
                                            "PENTAERYTHRITYL TETRA-DI-T-BUTYL HYDROXYHYDROCINNAMATE", "Acrylates/C10-30 Alkyl Acrylate Crosspolymer" })
                Assert.IsFalse(IngredientStoplist.WrongShape(real), real);
        }

        // ------------------------------------------------------------------------------------ what follows from it
        [Test]
        public void WhatCouldNotBeCheckedIsSaidAndNeverMakesAProductFine()
        {
            var who = new UserProfileData { skinType = "Normal" };
            var known = new List<string> { "Water", "Talc" };

            ScanRecord mostlyChecked = ProductAnalysisEngine.Analyse(known, "", who, null, null, null, null, new List<string> { "Isododecane" });
            Assert.AreEqual("safe", mostlyChecked.status);
            StringAssert.Contains("1 name on the label could not be checked", mostlyChecked.summary);
            Assert.AreEqual(2, mostlyChecked.ingredients.Count, "an unrecognised name is not counted as an ingredient");
            AssertNames(new[] { "Isododecane" }, mostlyChecked.unrecognized, "kept apart");

            ScanRecord mostlyUnknown = ProductAnalysisEngine.Analyse(known, "", who, null, null, null, null, new List<string> { "Isododecane", "Trimethylsiloxysilicate", "Zinc Stearate" });
            Assert.AreEqual("caution", mostlyUnknown.status, "2 of 5 checked is less than half: the result is incomplete, not fine");
            StringAssert.Contains("Only 2 of the 5 names", mostlyUnknown.summary);

            ScanRecord nothingKnown = ProductAnalysisEngine.Analyse(new List<string>(), "", who, null, null, null, null, new List<string> { "Isododecane", "Zinc Stearate" });
            Assert.AreEqual("caution", nothingKnown.status);
        }

        [Test]
        public void TheAiMayOnlyAddOneRealIngredientAtATime()
        {
            Assert.IsTrue(IngredientResearcher.IsOneIngredientName("Magnesium Stearate"));
            Assert.IsTrue(IngredientResearcher.IsOneIngredientName("Caprylic/Capric Triglyceride"));
            Assert.IsTrue(IngredientResearcher.IsOneIngredientName("Acrylates/C10-30 Alkyl Acrylate Crosspolymer"));
            Assert.IsFalse(IngredientResearcher.IsOneIngredientName("Carbomer & Triethanolamine"));
            Assert.IsFalse(IngredientResearcher.IsOneIngredientName("Acrylates Copolymer, Propylene Glycol"));
            Assert.IsFalse(IngredientResearcher.IsOneIngredientName("Net Wt 30 g"));
            Assert.IsFalse(IngredientResearcher.IsOneIngredientName("Long Lasting"));
            Assert.IsFalse(IngredientResearcher.IsOneIngredientName("May Contain"));
        }

        // ------------------------------------------------------------------------------------ an allergen typed in by the user
        static bool FlagsAllergen(string typedAllergen, params string[] ingredients)
        {
            var who = new UserProfileData { skinType = "Normal", knownAllergens = new List<string> { typedAllergen } };
            ScanRecord record = ProductAnalysisEngine.Analyse(new List<string>(ingredients), "", who, null, null, null, null, new List<string>());
            return record.allergenMatch != null && record.allergenMatch.level == "bad";
        }

        [Test]
        public void AnAlcoholAllergenFlagsDryingAlcoholsButNotFattyAlcohols()
        {
            Assert.IsTrue(FlagsAllergen("Alcohol", "Water", "Alcohol Denat.", "Mica"), "Alcohol Denat. is the drying kind");
            Assert.IsTrue(FlagsAllergen("alcohol", "Water", "Isopropyl Alcohol"), "Isopropyl Alcohol too, typed in small letters");
            Assert.IsTrue(FlagsAllergen("Alcohol", "Water", "Benzyl Alcohol"), "Benzyl Alcohol is not excused: it is a preservative and fragrance component");
            Assert.IsFalse(FlagsAllergen("Alcohol", "Water", "Cetearyl Alcohol", "Mica"), "Cetearyl Alcohol is a fatty alcohol: it only shares the word");
            Assert.IsFalse(FlagsAllergen("Alcohol", "Water", "Stearyl Alcohol", "Glycerin"), "Stearyl Alcohol too");
            Assert.IsFalse(FlagsAllergen("Alcohol", "Water", "Cetyl Alcohol", "Glycerin"), "Cetyl Alcohol too");
            Assert.IsTrue(FlagsAllergen("Alcohol", "Cetearyl Alcohol", "Alcohol Denat."), "a product with both is still flagged for the drying one");
        }

        [Test]
        public void AMoreSpecificTypedAllergenStillMatchesItsOwnName()
        {
            Assert.IsTrue(FlagsAllergen("Cetearyl Alcohol", "Water", "Cetearyl Alcohol"), "someone who is allergic to Cetearyl Alcohol itself is warned");
            Assert.IsFalse(FlagsAllergen("Cetearyl Alcohol", "Water", "Alcohol Denat."), "and is not warned about a different alcohol");
        }
    }
}
