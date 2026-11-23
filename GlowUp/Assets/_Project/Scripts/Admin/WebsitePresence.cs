using System;
using Firebase.Firestore;
using UnityEngine;

namespace GlowUp.Admin
{
    /// <summary>
    /// "Online" on the staff dashboard means: at least one admin has the website open. The website marks admins/{uid}.lastActiveAt
    /// (server time) every 30 seconds while a page is open, so a record newer than two minutes means a page is open.
    /// This listens to the admins collection live (staff are admins, so the rules allow the read) and re-checks the age while it
    /// waits, because silence does not produce a snapshot. Nothing is written.
    /// </summary>
    public static class WebsitePresence
    {
        const double FreshSeconds = 120;
        public static event Action Changed;
        public static bool AnyOpen { get; private set; }

        static ListenerRegistration listener;
        static readonly System.Collections.Generic.Dictionary<string, DateTime> seen = new System.Collections.Generic.Dictionary<string, DateTime>();

        public static void Start()
        {
            if (listener != null) return;
            try
            {
                listener = FirebaseFirestore.DefaultInstance.Collection("admins")
                    .Listen(snap => GlowUp.AppSettings.MainThread.Run(() => Take(snap)));
            }
            catch (Exception) { }
        }

        public static void Stop()
        {
            try { listener?.Stop(); } catch (Exception) { }
            listener = null; seen.Clear(); Set(false);
        }

        static void Take(QuerySnapshot snap)
        {
            if (snap == null) return;
            seen.Clear();
            foreach (DocumentSnapshot d in snap.Documents)
            {
                if (d.TryGetValue("lastActiveAt", out Timestamp t)) seen[d.Id] = t.ToDateTime();
            }
            Recheck();
        }

        /// <summary>Called now and then by the screen showing the state: an admin whose last beat is old has closed the site.</summary>
        public static void Recheck()
        {
            bool any = false;
            DateTime now = DateTime.UtcNow;
            foreach (DateTime t in seen.Values) if ((now - t).TotalSeconds < FreshSeconds) { any = true; break; }
            Set(any);
        }

        static void Set(bool value)
        {
            if (AnyOpen == value) return;
            AnyOpen = value; Changed?.Invoke();
        }
    }
}
