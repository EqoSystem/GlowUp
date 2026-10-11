using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Single access point for users/{uid}. Reads from Firestore when online (and refreshes the
    /// copy on the device), falls back to that copy offline. Writes go to Firestore or the SyncQueue.
    /// The copy on the device is only used for the account it belongs to.
    /// </summary>
    public static class UserProfileRepository
    {
        const string CacheKey = "user_profile";
        public static UserProfileData Current { get; private set; }

        /// <summary>Set by the login screen when someone signs in WITHOUT internet (checked against the saved password hash).</summary>
        public static string OfflineEmail
        {
            get
            {
                if (!offlineEmailRead) { offlineEmailRead = true; string saved = PlayerPrefs.GetString(OfflineSessionKey, ""); offlineEmail = saved.Length > 0 ? saved : null; }
                return offlineEmail;
            }
            set
            {
                offlineEmailRead = true;
                offlineEmail = string.IsNullOrEmpty(value) ? null : value;
                // kept on the phone until the user signs out, so closing the app (or a crash) does not sign them out
                if (offlineEmail == null) PlayerPrefs.DeleteKey(OfflineSessionKey); else PlayerPrefs.SetString(OfflineSessionKey, offlineEmail);
                PlayerPrefs.Save();
            }
        }
        const string OfflineSessionKey = "glowup.offlineSession";
        static string offlineEmail;
        static bool offlineEmailRead;

        /// <summary>The signed-in Firebase user's id, or null (also null when Firebase is not available on this device).</summary>
        public static string CurrentUid
        {
            get
            {
                try { return FirebaseAuth.DefaultInstance.CurrentUser?.UserId; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Whose data the app is showing: the Firebase user, or the offline-signed-in profile's owner.</summary>
        public static string ActiveUid => CurrentUid ?? (Current != null && !string.IsNullOrEmpty(Current.uid) ? Current.uid : null);

        /// <summary>Signs out. The copy of the profile stays on the device so the same person can sign back in offline.</summary>
        public static void SignOut()
        {
            try { FirebaseAuth.DefaultInstance.SignOut(); } catch (Exception e) { Debug.LogWarning("Sign out: " + e.Message); }
            Current = null; OfflineEmail = null;
        }

        /// <summary>The profile saved on this phone for whoever is signed in. Instant: the cloud is not asked.</summary>
        public static UserProfileData Cached()
        {
            string uid = CurrentUid;
            if (Current != null && Belongs(Current, uid)) return Current;
            return OfflineCache.TryLoad(CacheKey, out UserProfileData cached) && cached != null && Belongs(cached, uid) ? cached : null;
        }

        public static async Task<UserProfileData> Load()
        {
            string uid = CurrentUid;
            if (Current != null && Belongs(Current, uid)) return Current;
            Current = null;

            if (OfflineCache.TryLoad(CacheKey, out UserProfileData cached) && cached != null && Belongs(cached, uid)) Current = cached;

            // A change made on this phone that is still waiting to be sent is newer than the cloud's copy:
            // the cloud is not read until it has been sent, or it would bring the older profile back.
            bool waitingToSend = Current != null && uid != null && SyncQueue.HasPending("users/" + uid);
            if (uid != null && !waitingToSend && ConnectivityMonitor.Reachable && await FirebaseBootstrap.Ready)
            {
                try
                {
                    var snap = await FirebaseFirestore.DefaultInstance.Collection("users").Document(uid).GetSnapshotAsync()
                        .OrTimeout(Current != null ? 6f : 20f, "Loading your profile");
                    bool fromCloud = !snap.Metadata.IsFromCache;
                    if (fromCloud) ConnectivityMonitor.ReportAnswer();
                    // an answer from the phone's own store (no answer from the cloud) only counts when there is nothing better
                    if (snap.Exists && (fromCloud || Current == null)) { Current = FromSnapshot(snap); Current.uid = uid; OfflineCache.Save(CacheKey, Current); }
                }
                catch (Exception e) { TaskLimits.NoteFailure(e); Debug.LogWarning("Profile load failed, using the copy on this device: " + e.Message); }
            }
            return Current;
        }

        /// <summary>Does this saved profile belong to whoever is signed in now?</summary>
        static bool Belongs(UserProfileData p, string uid)
        {
            if (p == null) return false;
            if (uid != null) return p.uid == uid;
            return !string.IsNullOrEmpty(OfflineEmail) && string.Equals(p.email, OfflineEmail, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>How long a screen waits for the cloud to confirm a profile save before it carries on (the save itself is never lost).</summary>
        public static float SaveWaitSeconds = 8f;

        public static async Task Save(UserProfileData p)
        {
            Current = p;
            OfflineCache.Save(CacheKey, p);
            string uid = p.uid ?? CurrentUid;
            if (uid == null) return;
            string path = "users/" + uid;
            if (ConnectivityMonitor.Online && await FirebaseBootstrap.Ready)
            {
                try
                {
                    Task write = FirebaseFirestore.DefaultInstance.Document(path).SetAsync(ToDict(p), SetOptions.MergeAll);
                    if (await write.Within(SaveWaitSeconds)) return;

                    // Connected, but no answer yet (slow or no internet). The database library has already kept this
                    // write on the phone and sends it by itself when the connection is back, so the screen that is
                    // saving does not wait any longer. Only if that later sending fails is the profile queued again.
                    TaskScheduler main = SynchronizationContext.Current != null ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default;
                    _ = write.ContinueWith(t =>
                    {
                        try { Debug.LogWarning("Profile save queued: " + (t.Exception != null ? t.Exception.GetBaseException().Message : "failed")); SyncQueue.EnqueueSet(path, p); }
                        catch (Exception late) { Debug.LogWarning("Profile save: " + late.Message); }
                    }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, main);
                    return;
                }
                catch (Exception e) { Debug.LogWarning("Profile save queued: " + e.Message); }
            }
            SyncQueue.EnqueueSet(path, p);
        }

        public static void Clear() { Current = null; OfflineCache.Delete(CacheKey); }

        static Dictionary<string, object> ToDict(UserProfileData p) => new Dictionary<string, object>
        {
            { "firstName", p.firstName ?? "" }, { "middleName", p.middleName ?? "" }, { "lastName", p.lastName ?? "" },
            { "username", p.username ?? "" }, { "email", p.email ?? "" }, { "birthday", p.birthday ?? "" },
            // the person's own name identifies the record (sign-up no longer asks for a username; accounts made before keep theirs)
            { "fullName", FullName(p) }, { "nameId", NameId(p) },
            { "age", p.age }, { "skinType", p.skinType ?? "" }, { "skinTone", p.skinTone ?? "" },
            { "skinConditions", p.skinConditions ?? new List<string>() },
            { "knownAllergens", p.knownAllergens ?? new List<string>() },
            { "termsAccepted", p.termsAccepted }, { "theme", p.theme ?? "light" },
            { "createdAt", FieldValue.ServerTimestamp }
        };

        /// <summary>First, middle and last name as one text.</summary>
        public static string FullName(UserProfileData p)
        {
            if (p == null) return "";
            var parts = new List<string>();
            foreach (string part in new[] { p.firstName, p.middleName, p.lastName }) if (!string.IsNullOrWhiteSpace(part)) parts.Add(part.Trim());
            return string.Join(" ", parts);
        }

        /// <summary>The name in one fixed spelling ("maria|santos|cruz"), so two records of the same person can be told apart or found.</summary>
        public static string NameId(UserProfileData p)
        {
            if (p == null) return "";
            string Part(string s) => (s ?? "").Trim().ToLowerInvariant().Replace("|", " ");
            return Part(p.firstName) + "|" + Part(p.middleName) + "|" + Part(p.lastName);
        }

        static UserProfileData FromSnapshot(DocumentSnapshot snapshot)
        {
            var f = new FirestoreFields(snapshot);
            return new UserProfileData
            {
                firstName = f.Str("firstName"), middleName = f.Str("middleName"), lastName = f.Str("lastName"),
                username = f.Str("username"), email = f.Str("email"), birthday = f.Str("birthday"),
                age = f.Int("age"),
                skinType = f.Str("skinType"), skinTone = f.Str("skinTone"),
                skinConditions = f.List("skinConditions"), knownAllergens = f.List("knownAllergens"),
                termsAccepted = f.Bool("termsAccepted"), theme = f.Str("theme", "light")
            };
        }
    }
}
