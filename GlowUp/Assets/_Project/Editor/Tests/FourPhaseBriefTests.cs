using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>
    /// The four-phase brief of 11 November 2026: the barcode check digit and the nearest-barcode list (phase 2B), the volume
    /// button setting (phase 2A).
    /// </summary>
    public class FourPhaseBriefTests
    {
        static KeyValuePair<string, ProductData> Code(string number, string name) =>
            new KeyValuePair<string, ProductData>(ProductIndex.BarcodeKey(number), new ProductData { id = name, name = name, brandName = "Brand" });

        [Test]
        public void CheckDigitIsRightForRealNumbersAndWrongForAMisreadDigit()
        {
            Assert.IsTrue(BarcodeFuzzy.IsValidGtin("4006381333931"));        // EAN-13
            Assert.IsTrue(BarcodeFuzzy.IsValidGtin("036000291452"));         // UPC-A
            Assert.IsTrue(BarcodeFuzzy.IsValidGtin("36000291452"));          // the same, zero in front dropped (the app's key)
            Assert.IsTrue(BarcodeFuzzy.IsValidGtin("96385074"));             // EAN-8
            Assert.IsFalse(BarcodeFuzzy.IsValidGtin("4006381333937"));       // last digit wrong
            Assert.IsFalse(BarcodeFuzzy.IsValidGtin("4006381383931"));       // a 3 read as an 8
            Assert.IsFalse(BarcodeFuzzy.IsValidGtin("12345"));
        }

        [Test]
        public void NumbersPrintedWithSpacesAreFoundInText()
        {
            List<string> found = BarcodeFuzzy.NumbersIn("Net wt 15 g  4 006381 333931  Made in PH  Lot 2024");
            CollectionAssert.Contains(found, "4006381333931");
            CollectionAssert.DoesNotContain(found, "15");
        }

        [Test]
        public void NearestBarcodesAreListedBestFirstAndFarOnesAreLeftOut()
        {
            var catalogue = new List<KeyValuePair<string, ProductData>>
            {
                Code("4006381333931", "Right one"),
                Code("4006381333948", "Two digits off"),
                Code("8801234567893", "Far away"),
            };
            List<BarcodeFuzzy.Near> near = BarcodeFuzzy.Nearest("4006381383931", catalogue);   // one digit misread
            Assert.Greater(near.Count, 0);
            Assert.AreEqual("Right one", near[0].product.name);
            Assert.IsFalse(near.Exists(n => n.product.name == "Far away"));
        }

        [Test]
        public void APartOfTheNumberAndAMissingDigitAreNear()
        {
            Assert.AreEqual(1, BarcodeFuzzy.Distance("06381333931", "4006381333931"));       // the start was cut off
            Assert.AreEqual(1, BarcodeFuzzy.Distance("400638133931", "4006381333931"));      // one digit missing
            Assert.AreEqual(1, BarcodeFuzzy.Distance("4006318333931", "4006381333931"));     // two neighbours swapped
            Assert.AreEqual(-1, BarcodeFuzzy.Distance("1111111111111", "4006381333931"));
        }

        [Test]
        public void VolumeButtonDefaultsToDoNothingAndIsSaved()
        {
            UnityEngine.PlayerPrefs.DeleteKey("GlowUp.Camera.VolumeButton");
            Assert.AreEqual(UiPreferences.VolumeButtonAction.DoNothing, UiPreferences.VolumeButton);
            UiPreferences.VolumeButton = UiPreferences.VolumeButtonAction.CapturePhoto;
            Assert.AreEqual(UiPreferences.VolumeButtonAction.CapturePhoto, UiPreferences.VolumeButton);
            UnityEngine.PlayerPrefs.DeleteKey("GlowUp.Camera.VolumeButton");
        }
    }
}
