#if GLOWUP_FACEBOOK
using System.Collections.Generic;
using Facebook.Unity.Settings;
using UnityEditor;
using UnityEngine;

namespace GlowUp.EditorTools
{
    /// <summary>
    /// Fills in the Facebook SDK's settings (Facebook > Edit Settings) for GlowUp: the App ID and the Client
    /// Token of the Facebook app. It runs by itself once the SDK is in the project and the settings are still
    /// empty, and from the menu GlowUp > Set Up Facebook Sign-In. Both values are public (every copy of the
    /// app carries them); the App SECRET is not kept here: it lives only in the Firebase console.
    /// To use another Facebook app, change the two values below (or type them under Facebook > Edit Settings).
    /// </summary>
    [InitializeOnLoad]
    public static class FacebookSignInSetup
    {
        const string AppId = "2053807801928969";
        const string ClientToken = "777cf4925e5f18218468e0c33444e0cd";
        const string AppLabel = "GlowUp";

        static FacebookSignInSetup() { EditorApplication.delayCall += () => Apply(false); }

        [MenuItem("GlowUp/Set Up Facebook Sign-In", false, 300)]
        static void FromMenu() { Apply(true); }

        static void Apply(bool always)
        {
            try
            {
                bool empty = FacebookSettings.AppIds == null || FacebookSettings.AppIds.Count == 0 || string.IsNullOrEmpty(FacebookSettings.AppIds[0]) || FacebookSettings.AppIds[0] == "0";
                bool noToken = FacebookSettings.ClientTokens == null || FacebookSettings.ClientTokens.Count == 0 || string.IsNullOrEmpty(FacebookSettings.ClientTokens[0]);
                if (!always && !empty && !noToken) return;
                FacebookSettings.AppLabels = new List<string> { AppLabel };
                FacebookSettings.AppIds = new List<string> { AppId };
                FacebookSettings.ClientTokens = new List<string> { ClientToken };
                EditorUtility.SetDirty(FacebookSettings.Instance);
                AssetDatabase.SaveAssets();
                Debug.Log("[GlowUp] Facebook sign-in: App ID and Client Token are set (Facebook > Edit Settings shows them). " +
                          "Still to do on developers.facebook.com: add the Android platform with the package name and key hash shown on that page.");
            }
            catch (System.Exception e) { Debug.LogWarning("[GlowUp] Facebook settings could not be filled in: " + e.Message + " Type them under Facebook > Edit Settings."); }
        }
    }
}
#endif
