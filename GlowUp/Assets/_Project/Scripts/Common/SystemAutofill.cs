using System;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Keeps the phone's own list of saved accounts (Android "autofill": saved e-mails and passwords, mostly the
    /// Google account) from opening by itself over the app. It used to appear at an odd place as soon as an email
    /// field was tapped, because Android ties it to Unity's hidden keyboard text box and not to the field on the screen.
    ///
    /// The app offers its own list instead, in the right place and only when asked: see Auth/SavedAccountsList.
    /// The Android work is in Assets/Plugins/Android/GlowUpAutofill.java. Nothing happens in the Editor or on iOS.
    /// </summary>
    public static class SystemAutofill
    {
        /// <summary>False = leave the phone's own list alone (set it before the first scene, or the first step has already run).</summary>
        public static bool SwitchedOff = true;

        /// <summary>Runs by itself once, when the app has started.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AtStart()
        {
            if (!SwitchedOff) return;
            string result = Call("switchOff");
            if (result != null) { }
        }

        /// <summary>Call when one of the app's input fields is tapped: the keyboard's own text box is marked as soon as it exists.</summary>
        public static void Quiet()
        {
            if (SwitchedOff) Call("quiet");
        }

        static string Call(string method)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var helper = new AndroidJavaClass("com.glowup.input.GlowUpAutofill"))
                {
                    if (method == "switchOff") return helper.CallStatic<string>(method, activity);
                    helper.CallStatic(method, activity);
                    return "";
                }
            }
            catch (Exception e)
            {
                { }
                return null;
            }
#else
            return null;
#endif
        }
    }
}
