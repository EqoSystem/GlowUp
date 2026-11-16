using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Switches the phone's flashlight (the light next to the back camera) on and off.
    ///
    /// While the app's camera is on, the light is switched THROUGH THAT CAMERA (Android does not let an app use the
    /// phone's flashlight service while its own camera is open: the light belongs to the camera then). The Java half
    /// is Assets/Plugins/Android/GlowUpTorch.java. While no camera is on, the phone's flashlight service is used.
    ///
    /// The light goes out by itself when the camera is closed; CameraStopped() keeps IsOn true to that.
    /// When the phone refuses, TrySet returns false with the reason and the caller tells the user.
    /// </summary>
    public static class Flashlight
    {
        public static bool IsOn { get; private set; }

        static int available = -1;      // -1 = not asked yet, 0 = no, 1 = yes

        /// <summary>True when the phone has a flashlight at all (asked once). A screen hides its flashlight button when it has none.</summary>
        public static bool Available
        {
            get
            {
                if (available >= 0) return available == 1;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var torch = new AndroidJavaClass(TorchClass))
                        available = torch.CallStatic<bool>("hasFlash", activity) ? 1 : 0;
                }
                catch (System.Exception e)
                {
                    { }
                    available = 1;              // not known: keep the button, the phone says no when it is pressed
                }
#else
                available = 1;                  // in the Editor there is no flashlight: pretend, so the button can be tried
#endif
                return available == 1;
            }
        }

        /// <summary>The camera was closed: its light went out with it.</summary>
        public static void CameraStopped() { cameraLight = false; IsOn = false; }

        /// <summary>
        /// The app's camera was opened. Android gives the light to the camera then: a light that was switched on before
        /// (through the phone's flashlight service) went out. Whoever wants it on switches it on again, through the camera.
        /// </summary>
        public static void CameraStarted() { if (!cameraLight) IsOn = false; }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string TorchClass = "com.glowup.ocr.GlowUpTorch";
#endif
        static bool cameraLight;        // the light was switched on through the open camera (not through the flashlight service)

        /// <summary>Returns true when the phone accepted the change. `problem` says why not otherwise.</summary>
        public static bool TrySet(bool on, out string problem)
        {
            problem = null;
            if (!on && !IsOn) return true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // 1. the app's camera is on: its own light
                    bool cameraOpen = false;
                    try
                    {
                        using (var torch = new AndroidJavaClass(TorchClass))
                        {
                            cameraOpen = torch.CallStatic<bool>("cameraIsOpen", activity);
                            if (cameraOpen)
                            {
                                string refused = torch.CallStatic<string>("set", activity, on);
                                if (string.IsNullOrEmpty(refused)) { IsOn = on; cameraLight = on; return true; }
                                problem = refused;
                                { }
                                if (!on) { IsOn = false; cameraLight = false; }
                                return false;
                            }
                        }
                    }
                    catch (System.Exception e) { { } }

                    // 2. no camera is on: the phone's flashlight service
                    if (!on && cameraLight) { IsOn = false; cameraLight = false; return true; }      // it went out with the camera
                    using (AndroidJavaObject manager = activity.Call<AndroidJavaObject>("getSystemService", "camera"))
                    using (var keys = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics"))
                    using (AndroidJavaObject hasFlashKey = keys.GetStatic<AndroidJavaObject>("FLASH_INFO_AVAILABLE"))
                    {
                        string[] ids = manager.Call<string[]>("getCameraIdList");
                        foreach (string id in ids)
                        {
                            using (AndroidJavaObject about = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id))
                            using (AndroidJavaObject hasFlash = about.Call<AndroidJavaObject>("get", hasFlashKey))
                            {
                                if (hasFlash == null || !hasFlash.Call<bool>("booleanValue")) continue;
                                manager.Call("setTorchMode", id, on);
                                IsOn = on; cameraLight = false;
                                return true;
                            }
                        }
                    }
                }
                problem = "This phone has no flashlight.";
                return false;
            }
            catch (System.Exception e)
            {
                IsOn = false;
                problem = e.Message;
                { }
                return false;
            }
#else
            IsOn = on;              // in the Editor there is no flashlight: pretend, so the button can be tried
            return true;
#endif
        }
    }
}
