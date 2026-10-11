using System.IO;
using GlowUp.Scan;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Makes sure the Scan Rules asset exists (Assets/_Project/Resources/ScanRules.asset): it holds the numbers that
    /// rule scanning and recommending in the whole app (how forgiving the ingredient reader is, when a result is called
    /// incomplete, how strict the recommendations are for a user with a skin condition, the AI limits ...).
    ///
    /// Menu: GlowUp > Open Scan Rules selects it, so the numbers can be changed in the Inspector.
    /// The asset is made by itself when the Editor loads; numbers that were changed are never overwritten.
    /// </summary>
    [InitializeOnLoad]
    public static class ScanRulesSetup
    {
        const string Folder = "Assets/_Project/Resources";
        static string AssetPath => Folder + "/" + ScanRules.ResourceName + ".asset";

        static ScanRulesSetup() { EditorApplication.delayCall += () => Ensure(); }

        [MenuItem("GlowUp/Open Scan Rules", false, 205)]
        static void Open()
        {
            ScanRules rules = Ensure();
            Selection.activeObject = rules;
            EditorGUIUtility.PingObject(rules);
        }

        public static ScanRules Ensure()
        {
            var rules = AssetDatabase.LoadAssetAtPath<ScanRules>(AssetPath);
            if (rules != null) return rules;
            if (!Directory.Exists(Folder)) { Directory.CreateDirectory(Folder); AssetDatabase.Refresh(); }
            rules = ScriptableObject.CreateInstance<ScanRules>();
            AssetDatabase.CreateAsset(rules, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[GlowUp] Scan Rules made: " + AssetPath + " (menu GlowUp > Open Scan Rules).");
            return rules;
        }
    }
}
