using System.Collections.Generic;
using System.IO;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the make-up-only rule (ProductScope) against the examples in MakeupScopeCases.txt.
    /// Run them in Unity: Window > General > Test Runner > EditMode > Run All.
    /// The admin website checks its own copy of the rule against the same file (tests/makeupScope.test.mjs).
    /// </summary>
    public class MakeupScopeTests
    {
        static string CasesPath => Path.Combine(Application.dataPath, "_Project/Editor/Tests/MakeupScopeCases.txt");

        static string KindWord(ProductScope.Kind kind) =>
            kind == ProductScope.Kind.Makeup ? "makeup" : kind == ProductScope.Kind.SkinCare ? "skincare" : kind == ProductScope.Kind.OutOfScope ? "out" : "unclear";

        [Test]
        public void EveryExampleInTheListIsJudgedAsWritten()
        {
            Assert.IsTrue(File.Exists(CasesPath), "The list of examples is missing: " + CasesPath);
            var wrong = new List<string>();
            int checkedLines = 0;
            foreach (string raw in File.ReadAllLines(CasesPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split('|');
                Assert.GreaterOrEqual(parts.Length, 3, "A line needs at least: kind | name | what the rule must say. Line: " + line);
                string type = parts[0].Trim(), name = parts[1].Trim(), expected = parts[2].Trim(), expectedType = parts.Length > 3 ? parts[3].Trim() : "";
                checkedLines++;

                if (type == "@label")
                {
                    string said = KindWord(ProductScope.JudgeLabel(name).kind);
                    if (said != expected) wrong.Add($"pack text \"{name}\": expected {expected}, the rule said {said}");
                    continue;
                }

                string kind = KindWord(ProductScope.Judge(type, name).kind);
                if (kind != expected) { wrong.Add($"\"{type}\" | \"{name}\": expected {expected}, the rule said {kind}"); continue; }
                if (expected == "makeup" && expectedType.Length > 0)
                {
                    string own = ProductScope.MakeupType(type, name);
                    if (own != expectedType) wrong.Add($"\"{type}\" | \"{name}\": expected the kind \"{expectedType}\", got \"{own}\"");
                }
            }
            Assert.Greater(checkedLines, 80, "The list of examples looks cut short.");
            Assert.IsEmpty(wrong, "\n" + string.Join("\n", wrong) + "\n");
        }

        [Test]
        public void SkinCareHairBodyNailAndFragranceAreNeverMakeup()
        {
            foreach (string name in new[] { "Hydrating Face Cream", "Vitamin C Serum", "Gentle Cleanser", "Anti-Dandruff Shampoo", "Body Lotion", "Nail Polish", "Eau De Parfum", "Sunscreen SPF 50" })
                Assert.IsTrue(ProductScope.Judge("", name).IsNotMakeup, name + " must not count as make-up");
        }

        [Test]
        public void AProductWithoutNameAndKindIsUnclearNotMakeup()
        {
            Assert.AreEqual(ProductScope.Kind.Unclear, ProductScope.Classify("", ""));
            Assert.AreEqual(ProductScope.Kind.Unclear, ProductScope.Classify(null, null));
            Assert.IsFalse(ProductScope.IsMakeup("", "Absidy Beauty Base"));
        }

        [Test]
        public void TheEdgeCasesAreDecidedByHowTheyAreSold()
        {
            // Decided on 12 October 2026 ("if sources consider them skin care, they are out"). Tinted bases are face make-up:
            foreach (string name in new[] { "Tinted Moisturizer", "BB Cream", "CC Cream", "Skin Tint" })
            {
                Assert.AreEqual(ProductScope.Kind.Makeup, ProductScope.Classify("", name), name);
                Assert.AreEqual("Foundation", ProductScope.MakeupType("", name), name + " is filed with the foundations");
            }
            // lip care and sun care are not:
            foreach (string name in new[] { "Lip Balm", "Lip Oil", "Tinted Sunscreen" })
                Assert.AreEqual(ProductScope.Kind.SkinCare, ProductScope.Classify("", name), name);
            // care and colour at once: no source places it clearly, so an admin decides per product
            foreach (string name in new[] { "Tinted Lip Balm", "Tinted Lip Oil" })
                Assert.AreEqual(ProductScope.Kind.Unclear, ProductScope.Classify("", name), name);
        }

        [Test]
        public void SettingSprayIsMakeup()
        {
            Assert.IsTrue(ProductScope.IsMakeup("", "All Nighter Setting Spray"));
            Assert.AreEqual("Setting Spray", ProductScope.MakeupType("", "Makeup Fixing Spray"));
        }

        [Test]
        public void ARemoverIsNotMakeupEvenWhenItsKindSaysLipstick()
        {
            Assert.IsTrue(ProductScope.Judge("Lipstick", "Lip + Eye Make Up Remover").IsNotMakeup);
            Assert.AreEqual("", ProductScope.MakeupType("Lipstick", "Instant Eye And Lip Makeup Remover"));
        }

        [Test]
        public void EveryKnownMakeupKindIsMakeupAndKeepsItsName()
        {
            foreach (string kind in ProductScope.MakeupTypes)
            {
                Assert.IsTrue(ProductScope.IsMakeup(kind, ""), kind);
                Assert.AreEqual(kind, ProductScope.MakeupType(kind, ""), kind);
            }
        }

        [Test]
        public void TheAnswersTheAiGivesForOtherProductsStopAScan()
        {
            foreach (string kind in new[] { "Skin Care", "Hair", "Body", "Nail", "Fragrance" })
                Assert.IsTrue(ProductScope.Judge(kind, "Some Product").IsNotMakeup, kind);
            Assert.AreEqual(ProductScope.Kind.Unclear, ProductScope.Classify("Unknown", ""));
        }

        [Test]
        public void TheStopMessageNamesTheKindOfProduct()
        {
            string message = ProductScope.StopMessage(ProductScope.Judge("", "Anti-Dandruff Shampoo"));
            StringAssert.Contains("GlowUp only checks make-up products", message);
            StringAssert.Contains("a hair product", message);
        }

        [Test]
        public void TextOnAPackOnlyStopsAScanForUnmistakableWords()
        {
            Assert.IsTrue(ProductScope.JudgeLabel("HEAD & SHOULDERS SHAMPOO").IsNotMakeup);
            Assert.IsFalse(ProductScope.JudgeLabel("with moisturizer and sunscreen").IsNotMakeup, "claims on a make-up pack must not stop a scan");
            Assert.IsFalse(ProductScope.JudgeLabel("FOUNDATION with sunscreen, washes off with facial wash").IsNotMakeup, "a make-up word on the pack wins");
        }
    }
}
