using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Switches the app's notification code on by itself. When Unity's Mobile Notifications package is
    /// installed (it is listed in Packages/manifest.json), this adds the symbol GLOWUP_NOTIFICATIONS to
    /// the Android build's Scripting Define Symbols; if the package is removed, it takes the symbol
    /// away again, so the project always compiles. Nothing to do by hand.
    /// See Scripts/Notifications/LocalNotificationService.cs.
    /// </summary>
    [InitializeOnLoad]
    public static class NotificationPackageDefine
    {
        const string Symbol = "GLOWUP_NOTIFICATIONS";
        const string PackageType = "Unity.Notifications.Android.AndroidNotificationCenter, Unity.Notifications.Android";

        static NotificationPackageDefine()
        {
            // wait until the Editor has finished loading, then check once
            EditorApplication.delayCall += Sync;
        }

        static void Sync()
        {
            try
            {
                bool installed = Type.GetType(PackageType) != null;
                NamedBuildTarget android = NamedBuildTarget.Android;
                var symbols = PlayerSettings.GetScriptingDefineSymbols(android)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                bool present = symbols.Contains(Symbol);
                if (installed == present) return;

                if (installed) symbols.Add(Symbol); else symbols.Remove(Symbol);
                PlayerSettings.SetScriptingDefineSymbols(android, string.Join(";", symbols));
                Debug.Log(installed
                    ? "[GlowUp] Mobile Notifications package found: reminders and rule-update notifications are switched on for Android builds."
                    : "[GlowUp] Mobile Notifications package not found: notifications are switched off (the rest of the app is not affected).");
            }
            catch (Exception e) { Debug.LogWarning("[GlowUp] Could not check the Mobile Notifications package: " + e.Message); }
        }
    }
}
