using GlowUp.Onboarding;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>Panel revision of 22 Nov 2026: the selfie skin tone match and the list sources text.</summary>
    public class SkinToneSelfieTests
    {
        [Test]
        public void EveryTileColourMatchesItself()
        {
            for (int i = 0; i < SkinToneMatch.Palette.Length; i++)
                Assert.AreEqual(i, SkinToneMatch.Nearest(SkinToneMatch.Palette[i]), SkinToneMatch.Names[i]);
        }

        [Test]
        public void ANearbyColourPicksTheNearestTile()
        {
            Assert.AreEqual("Light", SkinToneMatch.NameOf(new Color32(0xF0, 0xDA, 0xC4, 255)));
            Assert.AreEqual("Dark", SkinToneMatch.NameOf(new Color32(0x55, 0x3C, 0x33, 255)));
            Assert.AreEqual("Medium", SkinToneMatch.NameOf(new Color32(0xD0, 0xA4, 0x6C, 255)));
        }

        [Test]
        public void TheMedianIgnoresAGlareAndAShadow()
        {
            var pixels = new System.Collections.Generic.List<Color32>();
            for (int i = 0; i < 9; i++) pixels.Add(new Color32(0xD4, 0xA8, 0x6F, 255));
            pixels.Add(new Color32(255, 255, 255, 255));          // glare
            pixels.Add(new Color32(0, 0, 0, 255));                // shadow
            Color32 m = SkinToneMatch.Median(pixels);
            Assert.AreEqual(0xD4, m.r); Assert.AreEqual(0xA8, m.g); Assert.AreEqual(0x6F, m.b);
        }

        [Test]
        public void NoPixelsAndNoPaletteNeverThrow()
        {
            Assert.DoesNotThrow(() => SkinToneMatch.Median(null));
            Assert.AreEqual(-1, SkinToneMatch.Nearest(new Color32(1, 2, 3, 255), new Color32[0]));
        }

        [Test]
        public void TheSourcesNameTheStudiesAndSayNotMedical()
        {
            StringAssert.Contains("Fitzpatrick", SkinListSources.ToneBody);
            StringAssert.Contains("Monk", SkinListSources.ToneBody);
            StringAssert.Contains("Baumann", SkinListSources.Body);
            StringAssert.Contains("not a medical", SkinListSources.Note.ToLowerInvariant());
        }
    }
}
