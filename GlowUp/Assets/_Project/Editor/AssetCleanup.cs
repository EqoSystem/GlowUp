using System;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Deletes approved leftover assets through Unity (so each .meta file goes with its asset), from the command line:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod GlowUp.EditorTools.AssetCleanup.DeleteFromCommandLine
    ///             -deleteAssets "Assets/a.cs;Assets/b.cs"
    /// Only paths under Assets/_Project are accepted. Each deletion is a separate, revertable git change.
    /// </summary>
    public static class AssetCleanup
    {
        public static void DeleteFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-deleteAssets");
            if (at < 0 || at + 1 >= args.Length) { EditorApplication.Exit(2); return; }
            int failed = 0;
            foreach (string raw in args[at + 1].Split(';'))
            {
                string path = raw.Trim().Replace('\\', '/');
                if (path.Length == 0) continue;
                if (!path.StartsWith("Assets/_Project/", StringComparison.Ordinal) || AssetDatabase.LoadMainAssetAtPath(path) == null) { failed++; continue; }
                if (!AssetDatabase.DeleteAsset(path)) failed++;
            }
            AssetDatabase.Refresh();
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
