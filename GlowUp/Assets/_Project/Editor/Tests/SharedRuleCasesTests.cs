using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlowUp.Data;
using GlowUp.Scan;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks the rules the app shares word for word with the admin website (the name key of TextKey, and the barcode
    /// key, brand comparison and scan ids of ProductIndex, the stoplist of IngredientStoplist) against the examples in SharedRuleCases.txt.
    /// The website checks its own copy (_Shared/glowupRules.js) against the same file: tests/rules.test.mjs.
    /// </summary>
    public class SharedRuleCasesTests
    {
        static string CasesPath => Path.Combine(Application.dataPath, "_Project/Editor/Tests/SharedRuleCases.txt");

        /// <summary>In the list a special character is written as a backslash, the letter u and its code (four digits).</summary>
        static string Decode(string text) =>
            Regex.Replace(text, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        static string YesNo(bool value) => value ? "yes" : "no";

        [Test]
        public void EveryExampleGivesWhatTheListSays()
        {
            Assert.IsTrue(File.Exists(CasesPath), "The list of examples is missing: " + CasesPath);
            var wrong = new List<string>();
            int checkedLines = 0;
            foreach (string raw in File.ReadAllLines(CasesPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split('|');
                for (int i = 0; i < parts.Length; i++) parts[i] = Decode(parts[i].Trim());
                string rule = parts[0];
                int needed = rule == "same" || rule == "squashsame" || rule == "brands" || rule == "scanid" ? 4 : 3;
                Assert.AreEqual(needed, parts.Length, "This line does not have the columns its rule needs: " + line);
                checkedLines++;

                string expected = parts[needed - 1], got;
                switch (rule)
                {
                    case "key": got = TextKey.Normalize(parts[1]); break;
                    case "slug": got = TextKey.Slug(parts[1]); break;
                    case "compact": got = TextKey.Compact(parts[1]); break;
                    case "same": got = YesNo(TextKey.Same(parts[1], parts[2])); break;
                    case "squashsame":
                        string a = TextKey.Squash(TextKey.Normalize(parts[1])), b = TextKey.Squash(TextKey.Normalize(parts[2]));
                        got = YesNo(a.Length > 0 && a == b);
                        break;
                    case "barcode": got = ProductIndex.BarcodeKey(parts[1]); break;
                    case "brands": got = YesNo(ProductIndex.BrandsAgree(parts[1], parts[2])); break;
                    case "scanid": got = ProductIndex.ScanId(parts[1], parts[2]); break;
                    case "brandid": got = ProductIndex.ScanBrandId(parts[1]); break;
                    case "stoplist": got = IngredientStoplist.Hit(parts[1]) ?? ""; break;
                    case "shape": got = IngredientStoplist.WrongShape(parts[1]) ? "wrong" : "ok"; break;
                    default: Assert.Fail("The list names a rule this test does not know: " + rule); return;
                }
                if (got != expected) wrong.Add($"{line}\n      expected \"{expected}\", the app said \"{got}\"");
            }
            Assert.Greater(checkedLines, 60, "The list of examples looks cut short.");
            Assert.IsEmpty(wrong, "\n" + string.Join("\n", wrong) + "\n");
        }
    }
}
