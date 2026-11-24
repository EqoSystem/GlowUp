using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Common;
using GlowUp.Data;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// The user's profile picture (New22, 20 November 2026).
    ///   - Choosing: the app first asks in its own words (DashboardFlowController), then opens the phone's photo picker
    ///     (Assets/Plugins/Android/GlowUpPhotoPicker.java). The picker hands over only the chosen photo: no storage permission.
    ///   - Kept: a 256 x 256 JPEG (about 20 KB) in users/{uid}.photoData as "data:image/jpeg;base64,...", so the admins' User
    ///     Management page shows it at once (live), and a copy on the phone (persistentDataPath) so every screen shows it offline.
    ///   - Shown: Apply(image) puts it in any round avatar; the icon stays when there is no photo.
    /// </summary>
    public static class ProfilePhoto
    {
        public const int Side = 256;
        const string Prefix = "data:image/jpeg;base64,";
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public static event Action Changed;

        static string Uid => FirebaseAuth.DefaultInstance?.CurrentUser?.UserId;
        static string CachePath(string uid) => Path.Combine(Application.persistentDataPath, "avatar_" + uid + ".jpg");

        /// <summary>Opens the picker. done(true) after the photo is saved; done(false, reason) when cancelled or failed.</summary>
        public static void Choose(Action<bool, string> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("com.glowup.photo.GlowUpPhotoPicker"))
                    picker.CallStatic("pick", activity, Side, new PickerAnswer(done));
                return;
            }
            catch (Exception) { done?.Invoke(false, "no-picker"); return; }
#else
            done?.Invoke(false, "no-picker");
#endif
        }

        /// <summary>The picker's answer arrives on a Java thread: it is handed to Unity's main thread before anything is touched.</summary>
        class PickerAnswer : AndroidJavaProxy
        {
            readonly Action<bool, string> done;
            public PickerAnswer(Action<bool, string> done) : base("com.glowup.photo.GlowUpPhotoPicker$Callback") { this.done = done; }
            void onPicked(string base64) { MainThread.Run(async () => done?.Invoke(await Save(base64), "save-failed")); }
            void onFailed(string reason) { MainThread.Run(() => done?.Invoke(false, reason)); }
        }

        /// <summary>Checks the JPEG, keeps it on the phone and in the account. False when it is not a usable picture.</summary>
        public static async Task<bool> Save(string base64)
        {
            string uid = Uid;
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(base64) || base64.Length > 200000) return false;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); } catch (Exception) { return false; }
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(bytes)) { UnityEngine.Object.Destroy(texture); return false; }
            UnityEngine.Object.Destroy(texture);
            try { File.WriteAllBytes(CachePath(uid), bytes); } catch (Exception) { }
            sprites.Remove(uid);
            Changed?.Invoke();
            try
            {
                Task write = FirebaseFirestore.DefaultInstance.Collection("users").Document(uid)
                    .SetAsync(new Dictionary<string, object> { { "photoData", Prefix + base64 } }, SetOptions.MergeAll);
                await Task.WhenAny(write, Task.Delay(8000));          // offline: the database library sends it later
                return !write.IsFaulted;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The photo as a sprite, from the phone first, else from the account (once). Null when there is none.</summary>
        public static async Task<Sprite> Load()
        {
            string uid = Uid;
            if (string.IsNullOrEmpty(uid)) return null;
            if (sprites.TryGetValue(uid, out Sprite known) && known != null) return known;
            byte[] bytes = null;
            try { if (File.Exists(CachePath(uid))) bytes = File.ReadAllBytes(CachePath(uid)); } catch (Exception) { }
            if (bytes == null)
            {
                try
                {
                    DocumentSnapshot snap = await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).GetSnapshotAsync();
                    if (snap.Exists && snap.TryGetValue("photoData", out string data) && data != null && data.StartsWith(Prefix))
                    {
                        bytes = Convert.FromBase64String(data.Substring(Prefix.Length));
                        try { File.WriteAllBytes(CachePath(uid), bytes); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!texture.LoadImage(bytes)) return null;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            sprites[uid] = sprite;
            return sprite;
        }

        /// <summary>Shows the photo inside `avatar` (a round button or panel): a round mask and the picture over its icon.</summary>
        public static async void Apply(Graphic avatar)
        {
            if (avatar == null) return;
            Sprite sprite = await Load();
            if (avatar == null) return;
            Transform holder = avatar.transform.Find("PhotoMask");
            if (sprite == null) { if (holder != null) holder.gameObject.SetActive(false); return; }
            Image photo;
            if (holder == null)
            {
                Image mask = RuntimeUiKit.Box(avatar.transform, "PhotoMask", Color.white, false);
                mask.sprite = RuntimeIcons.Circle; mask.raycastTarget = false;
                RuntimeUiKit.Stretch(mask.rectTransform, 4, 4, 4, 4);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                photo = RuntimeUiKit.Box(mask.transform, "Photo", Color.white, false);
                photo.raycastTarget = false; photo.preserveAspect = true;
                RuntimeUiKit.Stretch(photo.rectTransform);
                // 24 Nov 2026 (owner: the Edit badge collided with the photo): the photo goes UNDER the pencil badge, not over it
                Transform badge = avatar.transform.Find("EditBadge");
                if (badge != null) mask.transform.SetSiblingIndex(badge.GetSiblingIndex());
                holder = mask.transform;
            }
            else photo = holder.Find("Photo").GetComponent<Image>();
            holder.gameObject.SetActive(true);
            photo.sprite = sprite;
        }
    }

    /// <summary>Runs work handed over from other threads on Unity's main thread.</summary>
    public class MainThread : MonoBehaviour
    {
        static MainThread instance;
        static readonly Queue<Action> work = new Queue<Action>();

        public static void Run(Action action)
        {
            lock (work) work.Enqueue(action);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("MainThread");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<MainThread>();
        }

        void Update()
        {
            while (true)
            {
                Action next;
                lock (work) { if (work.Count == 0) return; next = work.Dequeue(); }
                try { next(); } catch (Exception) { }
            }
        }
    }
}
