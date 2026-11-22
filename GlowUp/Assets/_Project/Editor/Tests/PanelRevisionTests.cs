using System.Collections.Generic;
using System.Linq;
using GlowUp.Data;
using GlowUp.Onboarding;
using GlowUp.Result;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>Panel revision of 22 Nov 2026: the skin type finder, the list sources and "Ask about this product".</summary>
    public class PanelRevisionTests
    {
        [Test]
        public void TheFinderPointsToTheTypeTheAnswersDescribe()
        {
            Assert.AreEqual(SkinTypeFinder.Oily, SkinTypeFinder.Result(new List<int> { 2, 2, 2 }));
            Assert.AreEqual(SkinTypeFinder.Dry, SkinTypeFinder.Result(new List<int> { 0, 0, 0 }));
            Assert.AreEqual(SkinTypeFinder.Normal, SkinTypeFinder.Result(new List<int> { 1, 1, 1 }));
            Assert.AreEqual(SkinTypeFinder.Mix, SkinTypeFinder.Result(new List<int> { 3, 3, 3 }));
            Assert.AreEqual(SkinTypeFinder.Mix, SkinTypeFinder.Result(new List<int> { 3, 3, 1 }), "two mix answers and one in between: mix");
        }

        [Test]
        public void ATieGoesToHowTheSkinFeels()
        {
            Assert.AreEqual(SkinTypeFinder.Dry, SkinTypeFinder.Result(new List<int> { 0, 2, 1 }), "dry feel, oily look, normal pores: the feel decides");
            Assert.AreEqual(SkinTypeFinder.Normal, SkinTypeFinder.Result(new List<int>()), "no answers: normal, never an error");
            Assert.AreEqual(SkinTypeFinder.Oily, SkinTypeFinder.Result(new List<int> { 2, 9, -1 }), "answers out of range are ignored");
        }

        [Test]
        public void EveryQuestionHasAPointForEachAnswerAndOnlyKnownTypes()
        {
            var types = new HashSet<string> { SkinTypeFinder.Oily, SkinTypeFinder.Normal, SkinTypeFinder.Dry, SkinTypeFinder.Mix };
            foreach (var q in SkinTypeFinder.Questions)
            {
                Assert.AreEqual(q.answers.Length, q.points.Length, q.text);
                Assert.IsTrue(q.points.All(p => p.Length > 0 && p.All(types.Contains)), q.text);
            }
            foreach (string t in types) Assert.IsTrue(SkinTypeHints.Back.ContainsKey(t), "the finder's result is one of the tiles: " + t);
        }

        [Test]
        public void TheListSourcesNameEveryBuiltInCondition()
        {
            foreach (string c in new[] { "Acne", "Eczema", "Psoriasis", "Rosacea", "Seborrheic", "Melasma", "Contact dermatitis" })
                StringAssert.Contains(c.ToLowerInvariant(), SkinListSources.Body.ToLowerInvariant());
            StringAssert.Contains("not a medical", SkinListSources.Note.ToLowerInvariant());
        }

        static ScanRecord Flagged() => new ScanRecord
        {
            productName = "Velvet Lip Cream", status = "harmful", summary = "Flagged for your profile: Fragrance.",
            findings = new List<IngredientFinding>
            {
                new IngredientFinding { name = "Fragrance", level = "harmful", reason = "you said you react to fragrance" },
                new IngredientFinding { name = "Mica", level = "safe" },
                new IngredientFinding { name = "Niacinamide", level = "safe", keyActive = true, description = "A form of vitamin B3. It helps an even skin tone." },
                new IngredientFinding { name = "Oddname", level = "unknown" }
            },
            allergenMatch = new CompatibilityItem { level = "bad", summary = "Contains your allergen Fragrance (Fragrance)." },
            concernMatch = new CompatibilityItem { level = "good", summary = "No conflicts with your saved chronic skin conditions." },
            skinTypeMatch = new CompatibilityItem { level = "good", summary = "No conflicts with Oily skin." },
            ageMatch = new CompatibilityItem { level = "unknown", summary = "" },
            fdaStatus = "registered", fdaNumber = "NN-1000012345", listFrom = "inventory", listCheck = "matched",
            recommendedShades = new List<string> { "Rose", "Mauve" }, dermatologistAdvised = true
        };

        [Test]
        public void TheQuestionsAreAnsweredFromTheResult()
        {
            var items = ProductQuestions.For(Flagged());
            string Answer(string start) => items.First(i => i.question.StartsWith(start)).answer;
            StringAssert.Contains("Flagged for your profile", Answer("Is it safe"));
            StringAssert.Contains("Fragrance", Answer("Which ingredients"));
            StringAssert.Contains("you said you react to fragrance", Answer("Which ingredients"));
            StringAssert.StartsWith("<b>Yes.</b>", Answer("Does it contain my allergens"));
            StringAssert.Contains("NN-1000012345", Answer("Is it registered"));
            StringAssert.Contains("second source has the same list", Answer("Where does"));
            StringAssert.Contains("Rose", Answer("Which shade"));
            StringAssert.Contains("vitamin B3", Answer("What do its key"));
            StringAssert.Contains("1 name has", Answer("Why do some"));
            StringAssert.StartsWith("Yes", Answer("Should I see"));
            Assert.IsFalse(items.Any(i => i.question.StartsWith("Is it right for my age")), "an empty match is left out");
        }

        [Test]
        public void ASafeResultSaysNothingIsFlaggedAndNothingCrashesOnEmptyRecords()
        {
            var safe = new ScanRecord { status = "safe", summary = "No ingredients were flagged for your profile.", findings = new List<IngredientFinding> { new IngredientFinding { name = "Mica", level = "safe" } } };
            var items = ProductQuestions.For(safe);
            StringAssert.Contains("None of the checked ingredients", items.First(i => i.question.StartsWith("Which ingredients")).answer);
            StringAssert.Contains("Not checked yet", items.First(i => i.question.StartsWith("Is it registered")).answer);
            StringAssert.StartsWith("Nothing here", items.First(i => i.question.StartsWith("Should I see")).answer);
            Assert.AreEqual(0, ProductQuestions.For(null).Count);
            Assert.DoesNotThrow(() => ProductQuestions.For(new ScanRecord { findings = null, recommendedShades = null }));
        }

        [Test]
        public void TheNoteSaysItIsNotMedicalAdviceAndNothingLeavesThePhone()
        {
            StringAssert.Contains("not medical advice", ProductQuestions.Note);
            StringAssert.Contains("on this phone", ProductQuestions.Note);
        }
    }
}
