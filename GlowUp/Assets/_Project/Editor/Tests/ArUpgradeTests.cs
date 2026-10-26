using System.Collections.Generic;
using GlowUp.AR;
using GlowUp.Performance;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.EditorTools.Tests
{
    /// <summary>The pure logic of the AR Try-On upgrade of 26 October 2026: smoothing, tiers, shade codes, colour math, areas.</summary>
    public class ArUpgradeTests
    {
        // ---------------------------------------------------------------- One Euro filter
        [Test]
        public void OneEuro_StillSignal_StaysPut()
        {
            var f = new OneEuroFilter(1.5f, 150f, 1f);
            float v = 0f;
            for (int i = 0; i < 60; i++) v = f.Step(0.25f, 1f / 30f);
            Assert.AreEqual(0.25f, v, 1e-5f);
        }

        [Test]
        public void OneEuro_Jitter_IsSmaller()
        {
            var f = new OneEuroFilter(1.5f, 150f, 1f);
            var random = new System.Random(7);
            float rawSpread = 0f, smoothSpread = 0f;
            f.Step(0f, 1f / 30f);
            for (int i = 0; i < 300; i++)
            {
                float noise = (float)(random.NextDouble() - 0.5) * 0.001f;          // half a millimetre of tracker trembling
                float s = f.Step(noise, 1f / 30f);
                if (i > 30) { rawSpread += Mathf.Abs(noise); smoothSpread += Mathf.Abs(s); }
            }
            Assert.Less(smoothSpread, rawSpread * 0.6f, "the trembling is not reduced enough");
        }

        [Test]
        public void OneEuro_FastMove_FollowsWithLittleLag()
        {
            var adaptive = new OneEuroFilter(1.5f, 150f, 1f);
            var plain = new OneEuroFilter(1.5f, 0f, 1f);                              // the same filter without speed adaptation
            adaptive.Step(0f, 1f / 30f); plain.Step(0f, 1f / 30f);
            float a = 0f, p = 0f;
            for (int i = 1; i <= 6; i++) { float x = 0.004f * i; a = adaptive.Step(x, 1f / 30f); p = plain.Step(x, 1f / 30f); }   // the mouth opens 2.4 cm in 0.2 s
            float target = 0.024f;
            Assert.Less(target - a, (target - p) * 0.5f, "fast movement lags behind");
        }

        [Test]
        public void OneEuroVertices_FirstStepCopies_ThenSmooths()
        {
            var f = new OneEuroVertexFilter();
            var raw = new[] { new Vector3(0, 0, 0), new Vector3(1, 1, 1) };
            f.Step(raw, 2, 1f / 30f);
            Assert.AreEqual(raw[1], f.Values[1]);
            raw[1] = new Vector3(1.0005f, 1f, 1f);
            f.Step(raw, 2, 1f / 30f);
            Assert.Less(f.Values[1].x, 1.0005f);
            Assert.Greater(f.Values[1].x, 1f);
        }

        // ---------------------------------------------------------------- tier policy
        static ArTierPolicy.Window W(float fps, float p95, float seconds = 1.5f) => new ArTierPolicy.Window { averageFps = fps, p95Ms = p95, seconds = seconds };

        [Test]
        public void Tier_SlowAverage_MovesDown()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Full, false);
            Assert.AreEqual(ArTierPolicy.Verdict.MoveToLight, p.Observe(W(15f, 70f), true, 30));
            Assert.AreEqual(ArTier.Light, p.Tier);
        }

        [Test]
        public void Tier_Stutter_MovesDown_EvenWithGoodAverage()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Full, false);
            Assert.AreEqual(ArTierPolicy.Verdict.MoveToLight, p.Observe(W(40f, 120f), true, 30));
        }

        [Test]
        public void Tier_UserChoice_IsNeverChanged()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Full, false);
            Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(5f, 300f), false, 30));
            Assert.AreEqual(ArTier.Full, p.Tier);
        }

        [Test]
        public void Tier_LightFromMemoryOnly_NeverTriesFull()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Light, false);                // a phone with little memory: never moved down by a check
            for (int i = 0; i < 40; i++) Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(30f, 34f), true, 30));
        }

        [Test]
        public void Tier_AfterMoveDown_OneTrial_ThenBackDownWhenSlow()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Full, false);
            p.Observe(W(15f, 70f), true, 30);                                         // down
            ArTierPolicy.Verdict v = ArTierPolicy.Verdict.Stay;
            int windows = 0;
            while (v == ArTierPolicy.Verdict.Stay && windows < 100) { v = p.Observe(W(30f, 34f), true, 30); windows++; }
            Assert.AreEqual(ArTierPolicy.Verdict.TryFull, v);
            Assert.GreaterOrEqual(windows * 1.5f, p.SteadySecondsBeforeTrial);
            Assert.AreEqual(ArTierPolicy.Verdict.MoveToLight, p.Observe(W(14f, 90f), true, 30));     // the trial fails
            for (int i = 0; i < 40; i++) Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(30f, 34f), true, 30));   // no second trial
        }

        [Test]
        public void Tier_TrialPasses_AfterThreeGoodWindows()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Light, true);                 // moved down on an earlier visit
            ArTierPolicy.Verdict v = ArTierPolicy.Verdict.Stay;
            for (int i = 0; i < 100 && v != ArTierPolicy.Verdict.TryFull; i++) v = p.Observe(W(30f, 34f), true, 30);
            Assert.AreEqual(ArTierPolicy.Verdict.TryFull, v);
            Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(45f, 30f), true, 30));
            Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(45f, 30f), true, 30));
            Assert.AreEqual(ArTierPolicy.Verdict.TrialPassed, p.Observe(W(45f, 30f), true, 30));
            Assert.AreEqual(ArTier.Full, p.Tier);
        }

        [Test]
        public void Tier_UnsteadyLight_ResetsTheWait()
        {
            var p = new ArTierPolicy(); p.Begin(ArTier.Light, true);
            for (int i = 0; i < 12; i++) p.Observe(W(30f, 34f), true, 30);           // 18 s steady
            p.Observe(W(30f, 60f), true, 30);                                         // one rough window
            for (int i = 0; i < 12; i++) Assert.AreEqual(ArTierPolicy.Verdict.Stay, p.Observe(W(30f, 34f), true, 30));
        }

        [Test]
        public void P95_IsTheSlowEnd()
        {
            var times = new float[100];
            for (int i = 0; i < 100; i++) times[i] = 0.010f;
            for (int i = 0; i < 5; i++) times[i * 20] = 0.100f;                       // five slow frames of 100
            Assert.AreEqual(10f, ArTierPolicy.P95Ms(times, 100, new float[100]), 0.01f);
            times[1] = 0.100f;                                                        // six: the 95th percentile is slow now
            Assert.AreEqual(100f, ArTierPolicy.P95Ms(times, 100, new float[100]), 0.01f);
        }

        // ---------------------------------------------------------------- shade codes
        static readonly List<string> Codes = new List<string> { "12A", "131", "NC20", "120 Classic Ivory" };
        static readonly List<string> Hexes = new List<string> { "#B5463F", "#C24B62", "#D4A86F", "#F2DEC9" };

        [TestCase("12A", 0)]
        [TestCase("12 A", 0)]
        [TestCase("12-a", 0)]
        [TestCase("Shade 12A", 0)]
        [TestCase("No. 131", 1)]
        [TestCase("#131", 1)]
        [TestCase("nc 20", 2)]
        [TestCase("120 Classic Ivory", 3)]
        [TestCase("I2A", 0)]          // 1 read as I
        [TestCase("13l", 1)]          // 1 read as l
        [TestCase("NC2O", 2)]         // 0 read as O
        [TestCase("999", -1)]
        [TestCase("", -1)]
        public void ShadeCode_Finds(string read, int expected)
        {
            Assert.AreEqual(expected, ShadeCode.Find(read, Codes));
        }

        [Test]
        public void ShadeCode_FirstWordOfALongerReading()
        {
            var codes = new List<string> { "110", "120" };
            Assert.AreEqual(1, ShadeCode.Find("120 Classic Ivory", codes));
        }

        [Test]
        public void ShadeCode_NeverGuessesBetweenLookAlikes()
        {
            var codes = new List<string> { "12B", "128" };
            Assert.AreEqual(0, ShadeCode.Find("12B", codes), "an exact code still wins");
            Assert.AreEqual(-1, ShadeCode.Find("I2B", codes), "folded, both codes match: no guess");
        }

        [Test]
        public void ShadeCode_HexFor_EmptyWhenNoCodes()
        {
            Assert.AreEqual("", ShadeCode.HexFor("12A", new List<string>(), Hexes));
            Assert.AreEqual("#B5463F", ShadeCode.HexFor("12a", Codes, Hexes));
        }

        // ---------------------------------------------------------------- colour math (FaceMakeupBlend)
        [Test]
        public void Tint_SkinOfTheAssumedColour_BecomesTheShade()
        {
            var skin = new Color(0.72f, 0.45f, 0.45f);
            var shade = new Color(0.6f, 0.1f, 0.15f);
            Color result = ArColorMath.Tint(skin, shade, skin, 1f);
            Assert.AreEqual(shade.r, result.r, 1e-4f); Assert.AreEqual(shade.g, result.g, 1e-4f); Assert.AreEqual(shade.b, result.b, 1e-4f);
        }

        [Test]
        public void Tint_KeepsTheLipsOwnLightAndShadow()
        {
            var skin = new Color(0.72f, 0.45f, 0.45f);
            var shade = new Color(0.6f, 0.1f, 0.15f);
            Color lit = ArColorMath.Tint(skin * 1.2f, shade, skin, 1f);
            Color shadow = ArColorMath.Tint(skin * 0.5f, shade, skin, 1f);
            Color normal = ArColorMath.Tint(skin, shade, skin, 1f);
            Assert.AreEqual(1.2f, ArColorMath.Luminance(lit) / ArColorMath.Luminance(normal), 0.02f);
            Assert.AreEqual(0.5f, ArColorMath.Luminance(shadow) / ArColorMath.Luminance(normal), 0.02f);
        }

        [Test]
        public void Tint_ZeroAmount_ChangesNothing()
        {
            var pixel = new Color(0.3f, 0.2f, 0.1f);
            Color result = ArColorMath.Tint(pixel, Color.red, new Color(0.7f, 0.5f, 0.4f), 0f);
            Assert.AreEqual(pixel.r, result.r, 1e-5f); Assert.AreEqual(pixel.g, result.g, 1e-5f); Assert.AreEqual(pixel.b, result.b, 1e-5f);
        }

        [Test]
        public void Tint_CanAtMostDoubleAPixel()
        {
            var deepLips = new Color(0.20f, 0.10f, 0.10f);
            Color result = ArColorMath.Tint(deepLips, Color.white, deepLips, 1f);
            Assert.LessOrEqual(result.r, 0.4f + 1e-4f);
        }

        [Test]
        public void Gloss_IsBrightestFacingTheLight_AndFadesAway()
        {
            Vector3 toCamera = Vector3.back;
            Vector3 light = new Vector3(0f, 0.45f, 0f);
            Vector3 facing = ((toCamera.normalized + (toCamera + light).normalized) * 0.5f).normalized;
            float best = ArColorMath.Gloss(facing, toCamera, light, 90f);
            float side = ArColorMath.Gloss(new Vector3(1f, 0f, -0.3f), toCamera, light, 90f);
            Assert.Greater(best, 0.95f);
            Assert.Less(side, 0.01f);
            Assert.Greater(ArColorMath.Gloss(new Vector3(0.3f, 0f, -1f), toCamera, light, 8f), ArColorMath.Gloss(new Vector3(0.3f, 0f, -1f), toCamera, light, 90f), "satin is wider than gloss");
        }

        // ---------------------------------------------------------------- where the colour goes
        [TestCase("Brow", "Brows")]
        [TestCase("Eyebrow Pencil", "Brows")]
        [TestCase("Contour", "Cheeks")]
        [TestCase("Primer", "Face")]
        [TestCase("Setting Spray", "Face")]
        [TestCase("Eyeshadow", "Eyes")]
        [TestCase("Lip Tint", "Lips")]
        [TestCase("Bronzer", "Cheeks")]
        public void AreaFor_SendsEachKindToItsPart(string kind, string area)
        {
            Assert.AreEqual(area, ShadeAdvisor.AreaFor(kind));
        }

        [Test]
        public void Brows_HaveSixSuggestionsPerTone()
        {
            foreach (string tone in new[] { "Fair", "Light", "Medium", "Tan", "Deep", "Dark", "" })
                Assert.AreEqual(6, ShadeAdvisor.Recommend(tone, ShadeAdvisor.Brows).Count);
        }

        [Test]
        public void TestShades_CoverEveryFinishAndPart()
        {
            var finishes = new HashSet<string>(); var areas = new HashSet<string>(); var undertones = new HashSet<string>();
            foreach (ArTestShades.Shade s in ArTestShades.All)
            {
                finishes.Add(s.finish); areas.Add(s.area); undertones.Add(s.undertone);
                Assert.IsTrue(ColorUtility.TryParseHtmlString(s.hex, out _), s.hex);
            }
            Assert.GreaterOrEqual(ArTestShades.All.Length, 6);
            CollectionAssert.IsSubsetOf(new[] { "matte", "satin", "glossy", "shimmer", "metallic" }, finishes);
            CollectionAssert.IsSubsetOf(new[] { "warm", "cool", "neutral" }, undertones);
            CollectionAssert.IsSubsetOf(ShadeAdvisor.Areas, areas);
        }
    }
}
