using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// New21 (owner's decision, 17 November 2026), security guideline §8 and privacy §12. Runs on every Android build, after
    /// Unity has written the Gradle project, and changes only the LAUNCHER manifest (the main one the merger starts from), so the
    /// manifests under Assets/Plugins are never touched:
    ///  1. No app-data backup. Android otherwise copies the app's saved data (PlayerPrefs: an unfinished sign-up with skin
    ///     conditions and allergies, saved e-mails, the offline sign-in check) to the user's Google Drive and to a new phone.
    ///     allowBackup="false" stops cloud backup; on Android 12+ the data-extraction rules also stop phone-to-phone transfer.
    ///     The account itself is in Firestore, so a user loses nothing but a few settings after a reinstall.
    ///  2. The four advertising permissions are removed. They come from the Firebase Analytics library, which GlowUp's code
    ///     never calls; no feature uses them.
    /// Checked by Editor/Tests/AndroidPrivacyManifestTests and, after a build, with aapt on the APK.
    /// </summary>
    public class AndroidPrivacyManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 999;

        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";
        public const string RulesName = "glowup_data_rules";

        public static readonly string[] RemovedPermissions =
        {
            "com.google.android.gms.permission.AD_ID",
            "android.permission.ACCESS_ADSERVICES_AD_ID",
            "android.permission.ACCESS_ADSERVICES_ATTRIBUTION",
            "com.google.android.finsky.permission.BIND_GET_INSTALL_REFERRER_SERVICE"
        };

        /// <summary>Android 12+ rules: nothing goes to the cloud backup and nothing is carried to a new phone.</summary>
        public const string DataRulesXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<data-extraction-rules>
    <cloud-backup>
        <exclude domain=""root"" path="".""/>
        <exclude domain=""file"" path="".""/>
        <exclude domain=""database"" path="".""/>
        <exclude domain=""sharedpref"" path="".""/>
        <exclude domain=""external"" path="".""/>
    </cloud-backup>
    <device-transfer>
        <exclude domain=""root"" path="".""/>
        <exclude domain=""file"" path="".""/>
        <exclude domain=""database"" path="".""/>
        <exclude domain=""sharedpref"" path="".""/>
        <exclude domain=""external"" path="".""/>
    </device-transfer>
</data-extraction-rules>
";

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string main = Path.GetFullPath(Path.Combine(unityLibraryPath, "..", "launcher", "src", "main"));
            string manifest = Path.Combine(main, "AndroidManifest.xml");
            if (!File.Exists(manifest)) { Debug.LogWarning("[GlowUp] Privacy manifest: launcher manifest not found at " + manifest); return; }
            File.WriteAllText(manifest, Apply(File.ReadAllText(manifest)));
            string xmlDir = Path.Combine(main, "res", "xml");
            Directory.CreateDirectory(xmlDir);
            File.WriteAllText(Path.Combine(xmlDir, RulesName + ".xml"), DataRulesXml);
            Debug.Log("[GlowUp] Privacy manifest: app-data backup off, " + RemovedPermissions.Length + " advertising permissions removed.");
        }

        /// <summary>Returns the manifest text with backup off and the advertising permissions marked for removal.</summary>
        public static string Apply(string manifestXml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(manifestXml);
            XmlElement root = doc.DocumentElement;
            if (root.GetAttribute("xmlns:tools") != ToolsNs) root.SetAttribute("xmlns:tools", ToolsNs);

            var app = root.SelectSingleNode("application") as XmlElement;
            if (app == null) { app = doc.CreateElement("application"); root.AppendChild(app); }
            app.SetAttribute("allowBackup", AndroidNs, "false");
            app.SetAttribute("fullBackupContent", AndroidNs, "false");
            app.SetAttribute("dataExtractionRules", AndroidNs, "@xml/" + RulesName);
            var replace = app.GetAttribute("replace", ToolsNs).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            foreach (string a in new[] { "android:allowBackup", "android:fullBackupContent", "android:dataExtractionRules" })
                if (!replace.Contains(a)) replace.Add(a);
            app.SetAttribute("replace", ToolsNs, string.Join(",", replace));

            foreach (string permission in RemovedPermissions)
            {
                XmlElement node = root.ChildNodes.OfType<XmlElement>()
                    .FirstOrDefault(e => e.Name == "uses-permission" && e.GetAttribute("name", AndroidNs) == permission);
                if (node == null)
                {
                    node = doc.CreateElement("uses-permission");
                    node.SetAttribute("name", AndroidNs, permission);
                    root.InsertBefore(node, app);
                }
                node.SetAttribute("node", ToolsNs, "remove");
            }

            using (var sw = new StringWriterUtf8())
            {
                using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true }))
                    doc.Save(xw);
                return sw.ToString();
            }
        }

        class StringWriterUtf8 : StringWriter { public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false); }
    }
}
