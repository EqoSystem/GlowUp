using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using UnityEngine;

namespace GlowUp.Data
{
    /// <summary>
    /// Initialises Firebase once and turns on Firestore offline persistence. Every repository
    /// awaits <see cref="Ready"/> before touching Firebase.
    /// </summary>
    public static class FirebaseBootstrap
    {
        static Task<bool> initTask;

        public static Task<bool> Ready => initTask ??= Init();

        // Started with the app itself, before any scene's scripts run. Whoever uses the database first fixes its
        // settings, and that has to be this class: older scripts in the login scene use the database on their own.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartEarly() { _ = Ready; }

        static async Task<bool> Init()
        {
            try
            {
                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning("Firebase unavailable: " + status);
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Firebase init failed (offline mode): " + e.Message);
                return false;
            }

            // Firebase itself is ready from here on. The database's settings can only be chosen before its first
            // use; if another script got there first, the database refuses the change and keeps its defaults (on a
            // phone those also keep data on the device). That refusal must never switch the whole app to "offline":
            // it used to, and then the automatic sign-in was skipped and the login page came up again.
            try
            {
                var db = FirebaseFirestore.DefaultInstance;
                db.Settings.PersistenceEnabled = true;           // local cache so reads/writes work offline
                db.Settings.CacheSizeBytes = 100L * 1024 * 1024;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Firestore settings were left at their defaults: " + e.Message);
            }
            return true;
        }
    }
}
