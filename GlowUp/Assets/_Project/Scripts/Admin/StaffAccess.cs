using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using GlowUp.Data;

namespace GlowUp.Admin
{
    /// <summary>
    /// Staff access for the mobile capture companion (2026-11-23 plan "Mobile Admin UI & Real-Time Website Integration").
    /// The answer comes from the database every time (admins/{uid}: exists, status "active", not in the bin), exactly the
    /// isAdmin() test of firestore.rules. This screen check is for routing only: every catalogue write is refused by the
    /// rules for an account that is not an admin, whatever the app shows. Nothing is kept on the phone as proof.
    /// </summary>
    public static class StaffAccess
    {
        public enum Result { Granted, NotStaff, Disabled, NotSignedIn, Offline }

        public const string WebsiteHome = "https://glowupapp.xyz/";
        public const string WebsiteInventory = "https://glowupapp.xyz/_Admin_Brand_Inventory/adminBrandInventory.html";

        public static FirebaseUser User => FirebaseAuth.DefaultInstance?.CurrentUser;
        public static string Uid => User?.UserId ?? "";
        public static string Email => User?.Email ?? "";

        /// <summary>Asks the database whether the signed-in account is an active admin. Never answers Granted from a cached value.</summary>
        public static async Task<Result> Verify(float waitSeconds = 12f)
        {
            if (!await FirebaseBootstrap.Ready) return Result.Offline;
            FirebaseUser user = User;
            if (user == null) return Result.NotSignedIn;
            try
            {
                DocumentSnapshot snap = await FirebaseFirestore.DefaultInstance.Collection("admins").Document(user.UserId)
                    .GetSnapshotAsync(Source.Server).OrTimeout(waitSeconds, "Checking staff access");
                if (!snap.Exists) return Result.NotStaff;
                Dictionary<string, object> f = snap.ToDictionary();
                string status = f.TryGetValue("status", out object s) && s is string st && st.Length > 0 ? st : "active";
                bool binned = f.TryGetValue("inBin", out object b) && b is bool bb && bb;
                return status == "active" && !binned ? Result.Granted : Result.Disabled;
            }
            catch (Exception e)
            {
                // the rules refuse an account that is not an admin and not the owner of the document: that is "not staff", not "offline"
                if (e.Message != null && e.Message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) return Result.NotStaff;
                return Result.Offline;
            }
        }

        /// <summary>The account's own staff request on the website (adminRequests/{uid}): "pending", "rejected"... or "" when there is none.</summary>
        public static async Task<string> RequestStatus()
        {
            FirebaseUser user = User;
            if (user == null) return "";
            try
            {
                DocumentSnapshot snap = await FirebaseFirestore.DefaultInstance.Collection("adminRequests").Document(user.UserId)
                    .GetSnapshotAsync(Source.Server).OrTimeout(8f, "Checking the staff request");
                if (!snap.Exists) return "";
                return snap.ToDictionary().TryGetValue("status", out object s) && s != null ? s.ToString() : "pending";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Signs the staff session out (the customer login starts clean).</summary>
        public static void SignOut()
        {
            try { FirebaseAuth.DefaultInstance?.SignOut(); } catch (Exception) { }
            GlowUp.Auth.AdminAccount.Forget();
            SubmissionStore.Stop();
        }

        public static string Message(Result r)
        {
            switch (r)
            {
                case Result.NotStaff: return "This account has no staff access. Ask an admin on the website to approve your access request.";
                case Result.Disabled: return "This staff account is switched off. Ask an admin on the website.";
                case Result.NotSignedIn: return "Your session ended. Sign in again.";
                case Result.Offline: return "No connection to GlowUp. Staff access needs the internet: try again when you are online.";
                default: return "";
            }
        }
    }
}
