using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Rebuilds only the scenes that were named, in batch mode (the Editor must be closed): the rest of the project is left alone.
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod GlowUp.EditorTools.BatchRebuild.Run
    /// Environment: GLOWUP_BUILD_LIST = "Class.Method;Class.Method" (classes in GlowUp.EditorTools), for example
    ///   "SendForReviewSceneBuilder.Build;OnboardingTileBuilders.BuildStep2".
    /// </summary>
    public static class BatchRebuild
    {
        public static void Run()
        {
            string list = Environment.GetEnvironmentVariable("GLOWUP_BUILD_LIST");
            if (string.IsNullOrEmpty(list)) { Debug.LogError("[GlowUp] GLOWUP_BUILD_LIST is empty"); EditorApplication.Exit(2); return; }
            int failed = 0;
            foreach (string item in list.Split(';'))
            {
                string name = item.Trim();
                if (name.Length == 0) continue;
                int dot = name.LastIndexOf('.');
                Type type = dot > 0 ? typeof(BatchRebuild).Assembly.GetType("GlowUp.EditorTools." + name.Substring(0, dot)) : null;
                MethodInfo method = type != null ? type.GetMethod(name.Substring(dot + 1), BindingFlags.Public | BindingFlags.Static) : null;
                if (method == null) { Debug.LogError("[GlowUp] BatchRebuild: no method " + name); failed++; continue; }
                try { method.Invoke(null, null); Debug.Log("[GlowUp] BatchRebuild: built " + name); }
                catch (Exception e) { Debug.LogError("[GlowUp] BatchRebuild: " + name + " failed: " + e); failed++; }
            }
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
