using System.Xml;
using GlowUp.EditorTools;
using NUnit.Framework;

namespace GlowUp.Tests
{
    /// <summary>New21: the build turns app-data backup off and removes the four unused advertising permissions.</summary>
    public class AndroidPrivacyManifestTests
    {
        const string A = "http://schemas.android.com/apk/res/android", T = "http://schemas.android.com/tools";

        const string Launcher =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" package=""x"">
  <uses-permission android:name=""android.permission.INTERNET"" />
  <application android:label=""@string/app_name"" android:icon=""@mipmap/app_icon"" android:allowBackup=""true"" />
</manifest>";

        static XmlDocument Run(string xml) { var d = new XmlDocument(); d.LoadXml(AndroidPrivacyManifest.Apply(xml)); return d; }

        [Test]
        public void BackupIsOffAndWinsTheMerge()
        {
            var app = (XmlElement)Run(Launcher).DocumentElement.SelectSingleNode("application");
            Assert.AreEqual("false", app.GetAttribute("allowBackup", A));
            Assert.AreEqual("false", app.GetAttribute("fullBackupContent", A));
            Assert.AreEqual("@xml/" + AndroidPrivacyManifest.RulesName, app.GetAttribute("dataExtractionRules", A));
            StringAssert.Contains("android:allowBackup", app.GetAttribute("replace", T));
            Assert.AreEqual("@string/app_name", app.GetAttribute("label", A), "the rest of the application tag is kept");
        }

        [Test]
        public void AdvertisingPermissionsAreRemovedAndOthersKept()
        {
            var root = Run(Launcher).DocumentElement;
            foreach (string p in AndroidPrivacyManifest.RemovedPermissions)
            {
                XmlElement found = null;
                foreach (XmlElement e in root.GetElementsByTagName("uses-permission")) if (e.GetAttribute("name", A) == p) found = e;
                Assert.IsNotNull(found, p + " is listed");
                Assert.AreEqual("remove", found.GetAttribute("node", T), p + " is marked for removal");
            }
            bool internet = false;
            foreach (XmlElement e in root.GetElementsByTagName("uses-permission"))
                if (e.GetAttribute("name", A) == "android.permission.INTERNET") internet = e.GetAttribute("node", T) == "";
            Assert.IsTrue(internet, "INTERNET stays");
        }

        [Test]
        public void RunningTwiceChangesNothingMore()
        {
            string once = AndroidPrivacyManifest.Apply(Launcher);
            Assert.AreEqual(once, AndroidPrivacyManifest.Apply(once));
        }

        [Test]
        public void DataRulesExcludeEverythingFromCloudAndTransfer()
        {
            var d = new XmlDocument(); d.LoadXml(AndroidPrivacyManifest.DataRulesXml);
            Assert.AreEqual(5, d.SelectNodes("/data-extraction-rules/cloud-backup/exclude").Count);
            Assert.AreEqual(5, d.SelectNodes("/data-extraction-rules/device-transfer/exclude").Count);
        }
    }
}
