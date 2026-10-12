using System.Collections.Generic;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks how the app recognises a product without asking the AI: the memory of what packs look like
    /// (PackagingMemory: strong and medium matches), the shortlist "Is it one of these?" (ScanShortlist: from the text
    /// on the pack, from remembered packs, from what the user types) and the names a phone learns (LocalAliases).
    /// Nothing here touches the phone's storage.
    /// </summary>
    public class RecognitionTests
    {
        [SetUp]
        public void SetUp()
        {
            PackagingMemory.Enabled = true;
            PackagingMemory.ColourMatch = 0.88f; PackagingMemory.PatternDifference = 12;
            PackagingMemory.StrongColourMatch = 0.93f; PackagingMemory.StrongPatternDifference = 7;
            PackagingMemory.StrongLikeness = 0.70f; PackagingMemory.MediumLikeness = 0.55f; PackagingMemory.SameViewLikeness = 0.9f;
            ScanShortlist.Maximum = 3; ScanShortlist.LookStrong = 0.9f; ScanShortlist.LookMedium = 0.6f;
            ScanShortlist.WordBonus = 0.15f; ScanShortlist.BothBonus = 0.1f;
        }

        // ------------------------------------------------------------------------------------ sample data
        static ProductData P(string id, string brand, string name, string kind = "Foundation", params string[] aliases)
        {
            return new ProductData { id = id, brandName = brand, name = name, cosmeticType = kind, status = "Active", aliases = new List<string>(aliases) };
        }

        static ProductIndex Index(IDictionary<string, string> learned = null) => ProductIndex.Build(new[]
        {
            P("p1", "Maybelline", "Fit Me Matte + Poreless Foundation"),
            P("p2", "Maybelline", "Fit Me Concealer", "Concealer"),
            P("p3", "Maybelline", "Superstay Matte Ink Liquid Lipstick", "Lipstick"),
            P("p4", "Absidy", "Complexion Blur Translucent Perfecting Powder", "Powder"),
            P("p5", "Careline", "Oil Control Blush On", "Blush"),
            P("p6", "Vice Cosmetics", "Phenomenal Velvet Lipstick", "Lipstick")
        }, learned);

        /// <summary>A pack made of four coloured fields: `shift` moves the borders, `colours` are 0xRRGGBB.</summary>
        static int[] Pack(int size, int shift, params int[] colours)
        {
            var pixels = new int[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int field = ((x + shift) * 2 / size % 2) + 2 * ((y + shift) * 2 / size % 2);
                    pixels[y * size + x] = unchecked((int)0xFF000000) | colours[field % colours.Length];
                }
            return pixels;
        }

        static PackagingMemory.Look Named(PackagingMemory.Look look, string name, string brand, string id = "", string kind = "Foundation")
        {
            look.productName = name; look.brandName = brand; look.productId = id; look.cosmeticType = kind;
            return look;
        }

        /// <summary>A look made by hand: `sameShare` of its colours and all but `differentPoints` of its pattern agree with Base().</summary>
        static PackagingMemory.Look Base(float sameShare = 1f, int differentPoints = 0)
        {
            var look = new PackagingMemory.Look();
            for (int i = 0; i < 10; i++) look.colours[i] = 0.1f * sameShare;
            look.colours[40] = 1f - sameShare;                      // the rest of the picture has a colour the other look does not have
            long pattern = 0x5A5A5A5A5A5A5A5AL;
            for (int i = 0; i < differentPoints; i++) pattern ^= 1L << (i * 3);
            look.pattern = pattern;
            return look;
        }

        // ------------------------------------------------------------------------------------ the memory of packs
        [Test]
        public void TheSamePictureIsAStrongMatchAndAnotherPackIsNone()
        {
            PackagingMemory.Look red = Named(PackagingMemory.SignatureOf(Pack(96, 0, 0xC02040, 0xF0E0E0, 0x202020, 0xC02040), 96, 96), "Velvet Lipstick", "Vice Cosmetics");
            PackagingMemory.Look again = PackagingMemory.SignatureOf(Pack(96, 0, 0xC02040, 0xF0E0E0, 0x202020, 0xC02040), 96, 96);
            PackagingMemory.Look blue = PackagingMemory.SignatureOf(Pack(96, 0, 0x2040C0, 0x20C040, 0xF0F0F0, 0x808000), 96, 96);
            Assert.IsNotNull(red); Assert.IsNotNull(blue);

            List<PackagingMemory.Match> same = PackagingMemory.MatchesAmong(new[] { red }, again, "");
            Assert.AreEqual(1, same.Count);
            Assert.AreEqual(PackagingMemory.Strength.Strong, same[0].strength);
            Assert.IsTrue(same[0].WorthAsking, "a strong match is worth asking about at once");
            Assert.AreEqual(0, PackagingMemory.MatchesAmong(new[] { red }, blue, "").Count, "a pack of other colours is no match");
            Assert.AreEqual(0, PackagingMemory.MatchesAmong(new[] { red }, null, "").Count);
            Assert.IsNull(PackagingMemory.SignatureOf(new int[10], 4, 4), "a picture that is too small has no look");
        }

        [Test]
        public void AMediumMatchNeedsAWordOfTheNameOnThePack()
        {
            PackagingMemory.Look known = Named(Base(), "Fit Me Matte + Poreless Foundation", "Maybelline", "p1");
            PackagingMemory.Look seen = Base(0.9f, 10);                // 90% of the colours agree, 10 of 64 points differ
            List<PackagingMemory.Match> silent = PackagingMemory.MatchesAmong(new[] { known }, seen, "");
            Assert.AreEqual(1, silent.Count);
            Assert.AreEqual(PackagingMemory.Strength.Medium, silent[0].strength);
            Assert.IsFalse(silent[0].WorthAsking, "a medium likeness alone is not enough to ask");

            PackagingMemory.Match withWord = PackagingMemory.MatchesAmong(new[] { known }, seen, "MAYBELLINE NEW YORK\n30 ml")[0];
            Assert.IsTrue(withWord.wordSeen);
            Assert.IsTrue(withWord.WorthAsking, "a medium likeness and the brand on the pack: worth asking");

            Assert.AreEqual(0, PackagingMemory.MatchesAmong(new[] { known }, Base(0.8f, 4), "MAYBELLINE").Count, "too few colours agree");
            Assert.AreEqual(0, PackagingMemory.MatchesAmong(new[] { known }, Base(1f, 20), "MAYBELLINE").Count, "the pattern is too different");
        }

        [Test]
        public void EachRememberedProductIsOfferedOnceWithItsBestSide()
        {
            PackagingMemory.Look front = Named(Base(), "Fit Me Concealer", "Maybelline", "p2");
            PackagingMemory.Look side = Named(Base(0.9f, 9), "Fit Me Concealer", "Maybelline", "p2");
            PackagingMemory.Look other = Named(Base(0.91f, 8), "Oil Control Blush On", "Careline", "p5");
            List<PackagingMemory.Match> found = PackagingMemory.MatchesAmong(new[] { side, other, front }, Base(), "");
            Assert.AreEqual(2, found.Count, "two products, although three looks are alike");
            Assert.AreEqual("p2", found[0].look.productId);
            Assert.AreEqual(PackagingMemory.Strength.Strong, found[0].strength, "its best side counts");
            Assert.AreEqual("p5", found[1].look.productId);
            Assert.AreEqual(1, PackagingMemory.MatchesAmong(new[] { side, other, front }, Base(), "", 1).Count);
        }

        // ------------------------------------------------------------------------------------ the picture model's numbers
        /// <summary>
        /// What the phone's picture model would answer, made by hand: 80 numbers as Base64 text, 16 of them not zero. The
        /// first `agree` of those are the same in every such answer, the rest stand in a block that belongs to `own` alone
        /// (0 to 3). So two answers with different `own` are alike by (the smaller `agree`) / 16.
        /// </summary>
        static string Numbers(int agree, int own)
        {
            var bytes = new byte[80];
            for (int i = 0; i < 16; i++) if (i < agree) bytes[i] = 100;
            for (int i = agree; i < 16; i++) bytes[16 + own * 16 + i] = 100;
            return System.Convert.ToBase64String(bytes);
        }

        static PackagingMemory.Look WithNumbers(PackagingMemory.Look look, string numbers) { look.embedding = numbers; return look; }

        [Test]
        public void ThePictureModelsNumbersSayHowAlikeTwoPacksAre()
        {
            PackagingMemory.Look a = WithNumbers(Base(), Numbers(16, 0)), same = WithNumbers(Base(), Numbers(16, 1));
            Assert.AreEqual(1f, PackagingMemory.Likeness(a, same), 0.001f, "the same numbers: the same picture");
            // 12 of 16 numbers agree: the likeness is 12/16
            Assert.AreEqual(0.75f, PackagingMemory.Likeness(WithNumbers(Base(), Numbers(12, 0)), WithNumbers(Base(), Numbers(12, 1))), 0.001f);
            Assert.AreEqual(0f, PackagingMemory.Likeness(WithNumbers(Base(), Numbers(0, 0)), WithNumbers(Base(), Numbers(0, 1))), 0.001f, "nothing in common");

            // a look without numbers, or with damaged ones, cannot be compared this way
            Assert.AreEqual(-1f, PackagingMemory.Likeness(a, Base()));
            Assert.AreEqual(-1f, PackagingMemory.Likeness(a, WithNumbers(Base(), "not base64 !!")));
            Assert.AreEqual(-1f, PackagingMemory.Likeness(a, null));
        }

        [Test]
        public void WithThePictureModelTheNumbersDecideNotTheColours()
        {
            // the colours and the pattern of these looks are identical (Base()): only the picture model's numbers differ
            PackagingMemory.Look strong = Named(WithNumbers(Base(), Numbers(12, 1)), "Fit Me Concealer", "Maybelline", "p2");          // 0.75 alike
            PackagingMemory.Look medium = Named(WithNumbers(Base(), Numbers(10, 2)), "Oil Control Blush On", "Careline", "p5");       // 0.625 alike ...
            PackagingMemory.Look unlike = Named(WithNumbers(Base(), Numbers(4, 3)), "Phenomenal Velvet Lipstick", "Vice Cosmetics", "p6");
            PackagingMemory.Look seen = WithNumbers(Base(), Numbers(12, 0));
            // ... measured against what is seen: they share 10 of their 16 numbers -> 10 / 16 = 0.625

            List<PackagingMemory.Match> found = PackagingMemory.MatchesAmong(new[] { unlike, medium, strong }, seen, "");
            Assert.AreEqual(2, found.Count, "the pack that is not alike is left out although its colours are the same");
            Assert.AreEqual("p2", found[0].look.productId);
            Assert.AreEqual(PackagingMemory.Strength.Strong, found[0].strength);
            Assert.AreEqual(0.75f, found[0].score, 0.001f);
            Assert.AreEqual("p5", found[1].look.productId);
            Assert.AreEqual(PackagingMemory.Strength.Medium, found[1].strength);
            Assert.IsFalse(found[1].WorthAsking);
            Assert.IsTrue(PackagingMemory.MatchesAmong(new[] { medium }, seen, "CARELINE blush")[0].WorthAsking, "medium and a word of the name: worth asking");

            // a look kept before the phone had the picture model is still compared by its colours and pattern
            PackagingMemory.Look older = Named(Base(), "Complexion Blur Translucent Perfecting Powder", "Absidy", "p4");
            found = PackagingMemory.MatchesAmong(new[] { older }, seen, "");
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(PackagingMemory.Strength.Strong, found[0].strength);
        }

        // ------------------------------------------------------------------------------------ the shortlist
        [Test]
        public void WordsReadOnThePackGiveCandidates()
        {
            List<ScanShortlist.Entry> found = ScanShortlist.For(Index(), "MAYBELLINE\nFIT ME\nmatte + poreless", null);
            Assert.GreaterOrEqual(found.Count, 1);
            Assert.AreEqual("p1", found[0].product.id);
            Assert.IsTrue(found[0].byText);
            Assert.IsFalse(found[0].byLook);
            Assert.AreEqual("Maybelline, Foundation", found[0].Detail);
            Assert.LessOrEqual(found.Count, 3, "never more than three");

            Assert.AreEqual(0, ScanShortlist.For(Index(), "", null).Count);
            Assert.AreEqual(0, ScanShortlist.For(Index(), "NET WT 30 ML\nMADE IN FRANCE", null).Count, "nothing of a product's name: no candidates");
            Assert.AreEqual(0, ScanShortlist.For(null, "MAYBELLINE FIT ME", null).Count);
        }

        [Test]
        public void TheLookAndTheTextTogetherComeFirst()
        {
            ProductIndex index = Index();
            var looks = new List<PackagingMemory.Match>
            {
                new PackagingMemory.Match { look = Named(Base(), "Fit Me Concealer", "Maybelline", "p2", "Concealer"), strength = PackagingMemory.Strength.Medium, score = 0.9f }
            };
            // the words on the pack and the look of the pack both say: the concealer
            List<ScanShortlist.Entry> found = ScanShortlist.For(index, "MAYBELLINE FIT ME CONCEALER", looks);
            Assert.AreEqual("p2", found[0].product.id);
            Assert.IsTrue(found[0].byLook && found[0].byText);
            Assert.AreSame(looks[0].look, found[0].look);

            // a strong look without any text is offered as well
            looks[0].strength = PackagingMemory.Strength.Strong;
            found = ScanShortlist.For(index, "", looks);
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(0.9f, found[0].score, 0.001f);

            // what the user said "no" to is not offered again
            Assert.AreEqual(0, ScanShortlist.For(index, "", looks, new HashSet<string> { found[0].Key }).Count);
        }

        [Test]
        public void AProductOnlyThisPhoneRemembersIsOfferedToo()
        {
            var looks = new List<PackagingMemory.Match>
            {
                new PackagingMemory.Match { look = Named(Base(), "Glow Stick No 4", "Homemade Beauty", "", "Highlighter"), strength = PackagingMemory.Strength.Strong, score = 0.95f, wordSeen = true }
            };
            List<ScanShortlist.Entry> found = ScanShortlist.For(Index(), "", looks);
            Assert.AreEqual(1, found.Count);
            Assert.IsNull(found[0].product, "it is not in the inventory");
            Assert.AreEqual("Glow Stick No 4", found[0].productName);
            Assert.AreEqual("Homemade Beauty, Highlighter", found[0].Detail);
            Assert.AreEqual(1f, found[0].score, 0.001f, "strong, and a word of its name was read");
        }

        [Test]
        public void TheSearchBoxFindsHalfTypedNames()
        {
            ProductIndex index = Index();
            Assert.AreEqual("p1", ScanShortlist.Typed(index, "fit me fou", "")[0].product.id);
            Assert.AreEqual("p3", ScanShortlist.Typed(index, "superst", "")[0].product.id);
            Assert.AreEqual("p4", ScanShortlist.Typed(index, "blur powder", "")[0].product.id, "words in any order, not all of them");
            Assert.AreEqual("p6", ScanShortlist.Typed(index, "vice velvet", "")[0].product.id, "the brand may be typed into the name box");

            // a brand leaves out the products of other brands
            List<ScanShortlist.Entry> lipsticks = ScanShortlist.Typed(index, "lipstick", "");
            Assert.AreEqual(2, lipsticks.Count);
            lipsticks = ScanShortlist.Typed(index, "lipstick", "vice");
            Assert.AreEqual(1, lipsticks.Count);
            Assert.AreEqual("p6", lipsticks[0].product.id);

            // only a brand: its products
            List<ScanShortlist.Entry> ofBrand = ScanShortlist.Typed(index, "", "maybelline");
            Assert.AreEqual(3, ofBrand.Count);
            foreach (ScanShortlist.Entry entry in ofBrand) Assert.AreEqual("Maybelline", entry.brandName);

            Assert.AreEqual(0, ScanShortlist.Typed(index, "", "").Count);
            Assert.AreEqual(0, ScanShortlist.Typed(index, "zzzz", "").Count);
            Assert.AreEqual(0, ScanShortlist.Typed(index, "f", "revlon").Count);
        }

        [Test]
        public void TheWholeNameComesBeforeALongerOne()
        {
            List<ScanShortlist.Entry> found = ScanShortlist.Typed(Index(), "Fit Me Concealer", "");
            Assert.AreEqual("p2", found[0].product.id);
            Assert.AreEqual(1f, found[0].score, 0.001f);
            Assert.IsTrue(found[0].byTyping);
        }

        // ------------------------------------------------------------------------------------ names a phone learns
        [Test]
        public void ANameThePhoneLearnedFindsItsProduct()
        {
            ProductIndex learned = Index(new Dictionary<string, string> { { "fitme poreless", "p1" } });
            Assert.AreEqual("p1", ScanShortlist.Typed(learned, "FitMe Poreless", "")[0].product.id);
            Assert.AreEqual(1f, ScanShortlist.Typed(learned, "FitMe Poreless", "")[0].score, 0.001f);
            Assert.Less(ScanShortlist.Typed(Index(), "fitme poreless", "")[0].score, 0.7f, "without the learned name it is only a guess");
        }

        [Test]
        public void OnlyANameThatSaysSomethingNewIsKept()
        {
            ProductData product = P("p4", "Absidy", "Complexion Blur Translucent Perfecting Powder", "Powder", "Complexion Blur Powder");
            Assert.IsTrue(LocalAliases.WorthKeeping("absidy blur", product));
            Assert.IsFalse(LocalAliases.WorthKeeping("COMPLEXION BLUR - Translucent Perfecting Powder", product), "that is its name already");
            Assert.IsFalse(LocalAliases.WorthKeeping("complexion blur powder", product), "the website lists that name already");
            Assert.IsFalse(LocalAliases.WorthKeeping("cb", product), "too short: it would fit too much");
            Assert.IsFalse(LocalAliases.WorthKeeping("blur powder", null));
        }
    }
}
