using System;
using UnityEngine;
#if UNITY_ANDROID && GLOWUP_NOTIFICATIONS
using Unity.Notifications.Android;
#endif

namespace GlowUp.Notifications
{
    /// <summary>
    /// Shows and schedules notifications on the phone itself (no server): the daily reminder and the
    /// "rules were updated" notice. It uses Unity's Mobile Notifications package.
    ///
    /// The real code below is switched on by the symbol GLOWUP_NOTIFICATIONS, which the Editor script
    /// NotificationPackageDefine sets by itself as soon as that package is installed (Window > Package
    /// Manager > Mobile Notifications; it is already listed in Packages/manifest.json). Without the
    /// package everything here does nothing and <see cref="Supported"/> is false, so the project still builds.
    ///
    /// What this is NOT: push messages sent from a server (Firebase Cloud Messaging). The app notices a
    /// rule change the next time it is open and online.
    /// </summary>
    public static class LocalNotificationService
    {
        public const string ChannelId = "glowup_general";

        /// <summary>The answer to a permission request. Poll IsDone (the phone shows its own dialog on Android 13+).</summary>
        public class PermissionAnswer
        {
            public Func<bool> isDone = () => true;
            public Func<bool> granted = () => false;
            public bool IsDone => isDone();
            public bool Granted => granted();
        }

#if UNITY_ANDROID && GLOWUP_NOTIFICATIONS
        static bool channelReady;

        /// <summary>True on an Android phone with the notifications package in the build.</summary>
        public static bool Supported => Application.platform == RuntimePlatform.Android;

        public static bool PermissionGranted => Supported && AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.Allowed;

        public static PermissionAnswer RequestPermission()
        {
            if (!Supported) return new PermissionAnswer();
            if (PermissionGranted) return new PermissionAnswer { granted = () => true };
            var request = new PermissionRequest();
            return new PermissionAnswer
            {
                isDone = () => request.Status != PermissionStatus.RequestPending,
                granted = () => request.Status == PermissionStatus.Allowed
            };
        }

        static void EnsureChannel()
        {
            if (channelReady) return;
            AndroidNotificationCenter.RegisterNotificationChannel(
                new AndroidNotificationChannel(ChannelId, "GlowUp", "Reminders and ingredient rule updates", Importance.Default));
            channelReady = true;
        }

        /// <summary>A notification every day at this time (the phone's own clock). Replaces an earlier one with the same id.</summary>
        public static void ScheduleDaily(int id, string title, string text, int hour, int minute)
        {
            if (!Supported) return;
            try
            {
                EnsureChannel();
                DateTime now = DateTime.Now;
                DateTime first = new DateTime(now.Year, now.Month, now.Day, Mathf.Clamp(hour, 0, 23), Mathf.Clamp(minute, 0, 59), 0);
                if (first <= now.AddSeconds(30)) first = first.AddDays(1);
                AndroidNotificationCenter.CancelNotification(id);
                AndroidNotificationCenter.SendNotificationWithExplicitID(new AndroidNotification(title, text, first, TimeSpan.FromDays(1)), ChannelId, id);
            }
            catch (Exception e) { Debug.LogWarning("Reminder was not scheduled: " + e.Message); }
        }

        /// <summary>A notification a moment from now.</summary>
        public static void ShowNow(int id, string title, string text)
        {
            if (!Supported || !PermissionGranted) return;
            try
            {
                EnsureChannel();
                AndroidNotificationCenter.SendNotificationWithExplicitID(new AndroidNotification(title, text, DateTime.Now.AddSeconds(2)), ChannelId, id);
            }
            catch (Exception e) { Debug.LogWarning("Notification was not shown: " + e.Message); }
        }

        public static void Cancel(int id)
        {
            if (!Supported) return;
            try { AndroidNotificationCenter.CancelNotification(id); }
            catch (Exception e) { Debug.LogWarning("Notification was not cancelled: " + e.Message); }
        }
#else
        /// <summary>False: the notifications package is not in this build (or this is not Android).</summary>
        public static bool Supported => false;
        public static bool PermissionGranted => false;
        public static PermissionAnswer RequestPermission() => new PermissionAnswer();
        public static void ScheduleDaily(int id, string title, string text, int hour, int minute) { }
        public static void ShowNow(int id, string title, string text) { }
        public static void Cancel(int id) { }
#endif
    }

    /// <summary>
    /// Keeps the phone's scheduled reminder in step with the user's choices. It runs when the app starts
    /// and every time a choice changes on the Settings page.
    /// </summary>
    public static class ReminderScheduler
    {
        public const int DailyReminderId = 7001, RuleUpdateId = 7002;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            NotificationPreferences.Changed -= Apply;
            NotificationPreferences.Changed += Apply;
            Apply();
        }

        public static void Apply()
        {
            if (!LocalNotificationService.Supported) return;
            if (NotificationPreferences.DailyReminder && LocalNotificationService.PermissionGranted)
                LocalNotificationService.ScheduleDaily(DailyReminderId, NotificationPreferences.ReminderTitle, NotificationPreferences.ReminderText,
                                                       NotificationPreferences.ReminderHour, 0);
            else LocalNotificationService.Cancel(DailyReminderId);
        }
    }
}
