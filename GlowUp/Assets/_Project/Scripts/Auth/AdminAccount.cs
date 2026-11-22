using System;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Data;
using GlowUp.Net;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// Is the signed-in account one of the website's admins (a document admins/{uid}, set by hand in the console or by an approved
    /// sign-up request)? Used only to switch on "quick registration" in Smart Scan: an admin's scan goes to the product catalogue
    /// at once. This is a convenience gate, not the security: the database rules decide who may write the catalogue (only
    /// admins), so an account that is not an admin is refused there whatever this says.
    /// The answer is kept on the phone per account (so it works offline) and asked again when the app starts and every 6 hours.
    /// </summary>
    public static class AdminAccount
    {
        const string KeyPrefix = "GlowUp.IsAdmin.";
        const long RecheckSeconds = 6 * 3600;

        static string checkedUid;
        static bool isAdmin;

        /// <summary>The last known answer for the signed-in account (false when nobody is signed in).</summary>
        public static bool IsAdmin
        {
            get
            {
                string uid = UserProfileRepository.CurrentUid;
                if (string.IsNullOrEmpty(uid)) return false;
                if (checkedUid == uid) return isAdmin;
                return PlayerPrefs.GetString(KeyPrefix + uid, "") == "1";
            }
        }

        /// <summary>Forget the answer (sign-out).</summary>
        public static void Forget() { checkedUid = null; isAdmin = false; }

        /// <summary>Asks the database (at most `waitSeconds`); on no answer the last known answer stands.</summary>
        public static async Task<bool> Ensure(float waitSeconds = 3f)
        {
            string uid = UserProfileRepository.CurrentUid;
            if (string.IsNullOrEmpty(uid)) return false;
            if (checkedUid == uid) return isAdmin;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long.TryParse(PlayerPrefs.GetString(KeyPrefix + uid + ".at", "0"), out long at);
            bool known = PlayerPrefs.HasKey(KeyPrefix + uid);
            if (known && now - at < RecheckSeconds) { checkedUid = uid; isAdmin = PlayerPrefs.GetString(KeyPrefix + uid) == "1"; return isAdmin; }

            try
            {
                if (!ConnectivityMonitor.Online || !await FirebaseBootstrap.Ready) return IsAdmin;
                Task<DocumentSnapshot> ask = FirebaseFirestore.DefaultInstance.Collection("admins").Document(uid).GetSnapshotAsync();
                Task first = await Task.WhenAny(ask, Task.Delay(TimeSpan.FromSeconds(waitSeconds)));
                if (first != ask) return IsAdmin;
                bool yes = ask.Result.Exists;
                PlayerPrefs.SetString(KeyPrefix + uid, yes ? "1" : "0");
                PlayerPrefs.SetString(KeyPrefix + uid + ".at", now.ToString());
                PlayerPrefs.Save();
                checkedUid = uid; isAdmin = yes;
                return yes;
            }
            catch (Exception) { return IsAdmin; }
        }
    }
}
