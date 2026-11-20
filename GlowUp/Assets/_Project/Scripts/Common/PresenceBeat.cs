using System.Collections;
using System.Collections.Generic;
using System;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// "Online now" on the admin website's User Management (22 November 2026). While the app is open and someone is signed in,
    /// it writes users/{uid}.lastActiveAt (a server time) immediately when authentication completes, when the app comes to the front,
    /// and then every two minutes. The website
    /// counts a user as online when that time is under five minutes old. Nothing else is written; when the app goes to the
    /// background it simply stops, so a closed app drops off the count within five minutes. Made once per run, survives scenes.
    /// The field is the user's own (the rules allow an owner to write any field that is not an admin field).
    /// </summary>
    public class PresenceBeat : MonoBehaviour
    {
        const float EverySeconds = 120f;
        static PresenceBeat instance;
        float next;
        bool writing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("PresenceBeat");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PresenceBeat>();
        }

        void OnEnable()
        {
            try
            {
                FirebaseAuth.DefaultInstance.StateChanged += OnAuthStateChanged;
                OnAuthStateChanged(null, EventArgs.Empty);
            }
            catch (Exception) { }
        }

        void OnDisable()
        {
            try { if (FirebaseAuth.DefaultInstance != null) FirebaseAuth.DefaultInstance.StateChanged -= OnAuthStateChanged; }
            catch (Exception) { }
        }

        void OnAuthStateChanged(object sender, EventArgs args)
        {
            next = 0f; // do not make a newly signed-in user wait for the two-minute timer
        }

        void OnApplicationFocus(bool focused) { if (focused) next = 0f; }        // a beat as soon as the app is back

        void Update()
        {
            if (!Application.isFocused || Time.unscaledTime < next) return;
            next = Time.unscaledTime + EverySeconds;
            if (writing) return;
            writing = true;
            StartCoroutine(Beat());
        }

        IEnumerator Beat()
        {
            string uid = null;
            try { uid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId; } catch (System.Exception) { }
            if (string.IsNullOrEmpty(uid)) { writing = false; yield break; }
            System.Threading.Tasks.Task write = null;
            try
            {
                write = FirebaseFirestore.DefaultInstance.Collection("users").Document(uid)
                    .SetAsync(new Dictionary<string, object> { { "lastActiveAt", FieldValue.ServerTimestamp } }, SetOptions.MergeAll);
            }
            catch (System.Exception) { writing = false; yield break; }
            while (write != null && !write.IsCompleted) yield return null;   // offline: the database library sends it later
            writing = false;
        }
    }
}
