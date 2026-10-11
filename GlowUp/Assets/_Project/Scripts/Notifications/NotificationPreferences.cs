using System;
using UnityEngine;

namespace GlowUp.Notifications
{
    /// <summary>
    /// The user's notification choices (Settings page), saved on the phone:
    /// the daily skin-log reminder with its time, and alerts when an admin changes the ingredient rules.
    /// The wording of each notification is typed on the Settings page's NotificationSettingsPanel and
    /// remembered here, so the reminder can be scheduled again each time the app starts.
    /// </summary>
    public static class NotificationPreferences
    {
        const string Prefix = "GlowUp.Notify.";
        public static event Action Changed;

        /// <summary>Off until the user switches it on (a reminder needs the phone's permission to notify).</summary>
        public static bool DailyReminder
        {
            get => PlayerPrefs.GetInt(Prefix + "Daily", 0) == 1;
            set { PlayerPrefs.SetInt(Prefix + "Daily", value ? 1 : 0); Save(); }
        }

        /// <summary>Hour of the day (0-23) the reminder appears, in the phone's own time.</summary>
        public static int ReminderHour
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Hour", 20), 0, 23);
            set { PlayerPrefs.SetInt(Prefix + "Hour", Mathf.Clamp(value, 0, 23)); Save(); }
        }

        /// <summary>On from the start: inside the app it needs no permission.</summary>
        public static bool RuleUpdates
        {
            get => PlayerPrefs.GetInt(Prefix + "Rules", 1) == 1;
            set { PlayerPrefs.SetInt(Prefix + "Rules", value ? 1 : 0); Save(); }
        }

        public static string ReminderTitle
        {
            get => PlayerPrefs.GetString(Prefix + "ReminderTitle", "Time for your skin log");
            set => PlayerPrefs.SetString(Prefix + "ReminderTitle", value ?? "");
        }

        public static string ReminderText
        {
            get => PlayerPrefs.GetString(Prefix + "ReminderText", "Take a moment to note how your skin feels today.");
            set => PlayerPrefs.SetString(Prefix + "ReminderText", value ?? "");
        }

        public static string RuleUpdateTitle
        {
            get => PlayerPrefs.GetString(Prefix + "RuleTitle", "Ingredient safety rules updated");
            set => PlayerPrefs.SetString(Prefix + "RuleTitle", value ?? "");
        }

        public static string RuleUpdateText
        {
            get => PlayerPrefs.GetString(Prefix + "RuleText", "Your next scans use the latest ingredient information.");
            set => PlayerPrefs.SetString(Prefix + "RuleText", value ?? "");
        }

        static void Save() { PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}
