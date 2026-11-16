using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Isopoh.Cryptography.Argon2;
using UnityEngine;

namespace GlowUp.Auth
{
    /// <summary>
    /// Argon2id hashing for the OFFLINE credential check only. Firebase Auth still receives
    /// the real password over TLS and does its own server-side hashing.
    /// Cost settings are public fields so they can be tuned (default = OWASP minimum).
    /// Test the hash time on a mid-range Android phone and lower memory if it's too slow.
    /// </summary>
    public static class Argon2idHasher
    {
        public static int TimeCost = 2;          // iterations
        public static int MemoryCostKiB = 19456; // 19 MiB
        public static int Parallelism = 1;
        public static int HashLength = 32;

        /// <summary>Returns the standard "$argon2id$v=19$..." encoded string (salt included).</summary>
        public static string Hash(string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            var config = new Argon2Config
            {
                Type = Argon2Type.HybridAddressing,   // Argon2id
                Version = Argon2Version.Nineteen,
                TimeCost = TimeCost,
                MemoryCost = MemoryCostKiB,
                Lanes = Parallelism,
                Threads = Parallelism,
                Password = Encoding.UTF8.GetBytes(password),
                Salt = salt,
                HashLength = HashLength
            };
            using (var argon2 = new Argon2(config))
            using (var hash = argon2.Hash())
                return config.EncodeString(hash.Buffer);
        }

        public static bool Verify(string encodedHash, string password)
        {
            if (string.IsNullOrEmpty(encodedHash) || password == null) return false;
            try { return Argon2.Verify(encodedHash, password); }
            catch (Exception e) { { } return false; }
        }
    }

    /// <summary>
    /// Stores the Argon2id verifier per email so a returning user can be checked while offline.
    /// Only the hash is stored (never the password).
    /// </summary>
    public static class OfflineCredentialStore
    {
        static string Key(string email) => "GlowUp.OfflineHash." + (email ?? "").Trim().ToLowerInvariant();

        /// <summary>
        /// Saves the verifier for this email so the same person can sign in later without internet.
        /// The hashing is heavy on purpose, so it runs in the background: the screen never freezes, and a phone
        /// on which it cannot run simply has no offline sign-in. It can never make a sign-in or sign-up fail.
        /// </summary>
        public static void Remember(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;
            string key = Key(email);
            try
            {
                TaskScheduler mainThread = TaskScheduler.FromCurrentSynchronizationContext();
                Task.Run(() =>
                {
                    try { return Argon2idHasher.Hash(password); }
                    catch (Exception e) { { } return null; }
                }).ContinueWith(done =>
                {
                    if (done.Status != TaskStatus.RanToCompletion || string.IsNullOrEmpty(done.Result)) return;
                    PlayerPrefs.SetString(key, done.Result);
                    PlayerPrefs.Save();
                }, mainThread);
            }
            catch (Exception e) { { } }
        }

        public static bool Check(string email, string password)
        {
            return Argon2idHasher.Verify(PlayerPrefs.GetString(Key(email), ""), password);
        }

        /// <summary>The same check without freezing the screen while the hash is worked out. False on any problem.</summary>
        public static async Task<bool> CheckAsync(string email, string password)
        {
            string saved = PlayerPrefs.GetString(Key(email), "");
            if (string.IsNullOrEmpty(saved) || password == null) return false;
            try { return await Task.Run(() => Argon2idHasher.Verify(saved, password)); }
            catch (Exception e) { { } return false; }
        }

        public static bool HasCredential(string email) => PlayerPrefs.HasKey(Key(email));

        /// <summary>Removes the saved verifier (used when an account is deleted): that email can no longer sign in offline on this phone.</summary>
        public static void Forget(string email)
        {
            PlayerPrefs.DeleteKey(Key(email));
            PlayerPrefs.Save();
        }
    }
}
