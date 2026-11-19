using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// Log-in lock-out (20 November 2026, the owner): after 3 wrong passwords for one e-mail, Log-In refuses that e-mail for 5 minutes
    /// and recommends Forgot Password. The count is kept on this phone only (PlayerPrefs), under a hash of the e-mail so the address
    /// itself is not stored. Firebase Authentication still applies its own server-side limit on top; this is the visible part.
    /// A successful log-in clears the count. The admin website has the same rule (_Index/loginLockout.js).
    /// </summary>
    public static class LoginLockout
    {
        public const int MaxTries = 3;
        public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);
        const string Prefix = "GlowUp.LoginLock.";

        // replaceable by tests
        public static Func<DateTime> Now = () => DateTime.UtcNow;
        public static Func<string, string> Read = key => PlayerPrefs.GetString(key, "");
        public static Action<string, string> Write = (key, value) => { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); };
        public static Action<string> Forget = key => { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); };

        public struct Result { public int fails; public int left; public TimeSpan locked; }

        static string KeyOf(string email)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes((email ?? "").Trim().ToLowerInvariant()));
                var sb = new StringBuilder(Prefix);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // stored as "fails|untilTicks|lastTicks"
        static void Load(string key, out int fails, out long until, out long last)
        {
            fails = 0; until = 0; last = 0;
            string[] parts = (Read(key) ?? "").Split('|');
            if (parts.Length == 3) { int.TryParse(parts[0], out fails); long.TryParse(parts[1], out until); long.TryParse(parts[2], out last); }
            long now = Now().Ticks;
            // a finished lock starts again from zero; a count older than a day is forgotten
            if ((until > 0 && until <= now) || (until == 0 && fails > 0 && now - last > TimeSpan.TicksPerDay)) { fails = 0; until = 0; }
        }

        /// <summary>How long this e-mail is still locked (zero = not locked).</summary>
        public static TimeSpan Remaining(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return TimeSpan.Zero;
            Load(KeyOf(email), out _, out long until, out _);
            long left = until - Now().Ticks;
            return left > 0 ? TimeSpan.FromTicks(left) : TimeSpan.Zero;
        }

        /// <summary>A wrong password for this e-mail.</summary>
        public static Result Fail(string email)
        {
            string key = KeyOf(email);
            Load(key, out int fails, out long until, out _);
            long now = Now().Ticks;
            fails++;
            if (fails >= MaxTries) until = now + LockFor.Ticks;
            Write(key, fails + "|" + until + "|" + now);
            return new Result { fails = fails, left = Math.Max(0, MaxTries - fails), locked = until > now ? TimeSpan.FromTicks(until - now) : TimeSpan.Zero };
        }

        /// <summary>A successful log-in.</summary>
        public static void Clear(string email) { if (!string.IsNullOrWhiteSpace(email)) Forget(KeyOf(email)); }

        public static string MinSec(TimeSpan t)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt((float)t.TotalSeconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
