using System;
using GlowUp.AppSettings;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// Photos from the phone's gallery (the system photo picker of GlowUpPhotoPicker.java: no storage permission, only the chosen
    /// photos are handed over). The permission-free picker is asked only when the staff member taps Add photo. Photos come whole
    /// (not cropped): a label or a barcode must keep its edges.
    /// </summary>
    public static class GalleryPicker
    {
        const int MaxSide = 1280;

        /// <summary>One whole photo.</summary>
        public static void Pick(Action<Texture2D> picked, Action<string> failed)
        {
            bool got = false;
            PickMany(1, tex => { got = true; picked?.Invoke(tex); }, count => { if (!got) picked?.Invoke(null); }, failed);
        }

        /// <summary>Several photos at once (batch): `onPhoto` for each, then `onDone(count)`. Cancelling gives onDone(0).</summary>
        public static void PickMany(int max, Action<Texture2D> onPhoto, Action<int> onDone, Action<string> failed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("com.glowup.photo.GlowUpPhotoPicker"))
                    picker.CallStatic("pickMany", activity, MaxSide, Mathf.Clamp(max, 1, 50), new Answer(onPhoto, onDone, failed));
            }
            catch (Exception) { failed?.Invoke("The photo picker could not open on this phone."); }
#else
            failed?.Invoke("Choosing photos works on the phone only.");
#endif
        }

        class Answer : AndroidJavaProxy
        {
            readonly Action<Texture2D> photo; readonly Action<int> done; readonly Action<string> failed;
            public Answer(Action<Texture2D> photo, Action<int> done, Action<string> failed) : base("com.glowup.photo.GlowUpPhotoPicker$ManyCallback") { this.photo = photo; this.done = done; this.failed = failed; }
            void onPhoto(string base64) => MainThread.Run(() =>
            {
                try
                {
                    string data = base64 ?? "";
                    int comma = data.IndexOf(',');
                    if (data.StartsWith("data:") && comma > 0) data = data.Substring(comma + 1);
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!tex.LoadImage(Convert.FromBase64String(data))) { UnityEngine.Object.Destroy(tex); failed?.Invoke("That file is not a photo GlowUp can read (use JPEG or PNG)."); return; }
                    photo?.Invoke(tex);
                }
                catch (Exception) { failed?.Invoke("That photo could not be read."); }
            });
            void onDone(int count) => MainThread.Run(() => done?.Invoke(count));
            void onFailed(string reason) => MainThread.Run(() => { if (reason == "cancelled" || reason == "canceled") done?.Invoke(0); else failed?.Invoke("No photo was chosen."); });
        }
    }
}
