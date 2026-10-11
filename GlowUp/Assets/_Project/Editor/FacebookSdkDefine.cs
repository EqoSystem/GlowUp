using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Switches the app's Facebook sign-in code on by itself. When the "Facebook SDK for Unity" package
    /// has been imported into the project, this adds the symbol GLOWUP_FACEBOOK to the Android build's
    /// Scripting Define Symbols; if the SDK is removed, it takes the symbol away again, so the project
    /// always compiles. Nothing to do by hand. See Scripts/Auth/SocialSignIn.cs for the steps.
    /// </summary>
    [InitializeOnLoad]
    public static class FacebookSdkDefine
    {
        const string Symbol = "GLOWUP_FACEBOOK";
        const string SdkType = "Facebook.Unity.FB, Facebook.Unity";

        static FacebookSdkDefine()
        {
            // wait until the Editor has finished loading, then check once
            EditorApplication.delayCall += Sync;
        }

        static void Sync()
        {
            try
            {
                bool installed = Type.GetType(SdkType) != null;
                NamedBuildTarget android = NamedBuildTarget.Android;
                var symbols = PlayerSettings.GetScriptingDefineSymbols(android)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                bool present = symbols.Contains(Symbol);
                if (installed == present) return;

                if (installed) symbols.Add(Symbol); else symbols.Remove(Symbol);
                PlayerSettings.SetScriptingDefineSymbols(android, string.Join(";", symbols));
                Debug.Log(installed
                    ? "[GlowUp] Facebook SDK found: the Facebook button on the login page is switched on for Android builds."
                    : "[GlowUp] Facebook SDK not found: the Facebook button explains that it is not set up (the rest of the app is not affected).");
            }
            catch (Exception e) { Debug.LogWarning("[GlowUp] Could not check for the Facebook SDK: " + e.Message); }
        }
    }
}
