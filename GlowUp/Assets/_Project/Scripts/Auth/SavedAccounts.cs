using System;
using System.Collections.Generic;
using GlowUp.Common;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// THE EMAILS THAT HAVE SIGNED IN ON THIS PHONE, for the list under the email field of the log-in page.
    ///
    /// Only email addresses are kept, on this phone only, newest first, five at most. NEVER A PASSWORD. An address gets
    /// here when somebody signs in with email and password (online or offline) or finishes signing up with one; a
    /// Google sign-in does not, because that account is not opened by typing its email. The user can take an
    /// address off the list on the log-in page; deleting the account takes it off too.
    ///
    /// With / Without are plain logic (covered by SavedAccountsTests); the rest reads and writes the small file.
    /// </summary>
    public static class SavedAccounts
    {
        /// <summary>How many addresses are kept.</summary>
        public const int Most = 5;

        [Serializable] class Kept { public List<string> emails = new List<string>(); }

        const string KeptFile = "saved_accounts";
        static List<string> emails;
        static bool memoryOnly;

        /// <summary>
        /// For tests and for trying a screen out: the list starts as `start` and is kept in memory only, so nothing is
        /// read from or written to the phone (or the computer the Editor runs on).
        /// </summary>
        public static void UseMemoryOnly(IList<string> start)
        {
            memoryOnly = true;
            emails = With(start, null, Most);
        }

        /// <summary>Back to the normal way after UseMemoryOnly: the list is read from its file again the next time it is needed.</summary>
        public static void UseKeptFile()
        {
            memoryOnly = false;
            emails = null;
        }

        /// <summary>The kept addresses, newest first.</summary>
        public static IReadOnlyList<string> All()
        {
            Load();
            return emails;
        }

        /// <summary>Puts an address at the top of the list (a text that is not written like an email is ignored).</summary>
        public static void Remember(string email)
        {
            Load();
            List<string> next = With(emails, email, Most);
            if (Same(next, emails)) return;
            emails = next; Save();
        }

        /// <summary>Takes an address off the list. The account itself is not touched.</summary>
        public static void Forget(string email)
        {
            Load();
            List<string> next = Without(emails, email);
            if (Same(next, emails)) return;
            emails = next; Save();
        }

        /// <summary>The list with `email` at the top: written in small letters, never twice, `most` entries at the most.</summary>
        public static List<string> With(IList<string> current, string email, int most)
        {
            string clean = Clean(email);
            var result = new List<string>();
            if (EmailDomains.WellFormed(clean)) result.Add(clean);
            if (current != null)
                foreach (string kept in current)
                {
                    string other = Clean(kept);
                    if (other.Length > 0 && !result.Contains(other)) result.Add(other);
                }
            if (result.Count > Mathf.Max(0, most)) result.RemoveRange(Mathf.Max(0, most), result.Count - Mathf.Max(0, most));
            return result;
        }

        /// <summary>The list without `email` (capitals and spaces around it make no difference).</summary>
        public static List<string> Without(IList<string> current, string email)
        {
            string clean = Clean(email);
            var result = new List<string>();
            if (current != null)
                foreach (string kept in current)
                {
                    string other = Clean(kept);
                    if (other.Length > 0 && other != clean && !result.Contains(other)) result.Add(other);
                }
            return result;
        }

        static string Clean(string email) => (email ?? "").Trim().ToLowerInvariant();

        static bool Same(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static void Load()
        {
            if (emails != null) return;
            try { emails = OfflineCache.TryLoad(KeptFile, out Kept kept) && kept != null && kept.emails != null ? With(kept.emails, null, Most) : new List<string>(); }
            catch (Exception e) { Debug.LogWarning("Saved emails: the list could not be read: " + e.Message); emails = new List<string>(); }
        }

        static void Save()
        {
            if (memoryOnly) return;
            try { OfflineCache.Save(KeptFile, new Kept { emails = new List<string>(emails) }); }
            catch (Exception e) { Debug.LogWarning("Saved emails: the list was not kept: " + e.Message); }
        }
    }
}
