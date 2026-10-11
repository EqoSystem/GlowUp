using System.Collections;
using System.Collections.Generic;
using GlowUp.Notifications;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlowUp.AppSettings
{
    /// <summary>
    /// Notifications on the Settings page: the daily skin-log reminder (with the hour it appears) and
    /// alerts when an admin changes the ingredient rules. The wording of the notifications themselves is
    /// typed here in the Inspector.
    ///
    /// Reminders are shown by the phone (Unity's Mobile Notifications package); the first time, the phone
    /// asks the user for permission. Rule-update alerts are noticed while the app is open and online and
    /// always show as a notice on the dashboard; they also appear as a phone notification when allowed.
    /// </summary>
    public class NotificationSettingsPanel : MonoBehaviour
    {
        [SerializeField] private Toggle dailyReminderToggle;
        [SerializeField] private TMP_Dropdown reminderHourDropdown;
        [SerializeField] private Toggle ruleUpdatesToggle;
        [SerializeField] private TextMeshProUGUI messageText;

        [Header("Wording on the page")]
        [Tooltip("How each hour is written in the list; {0} = 1-12, {1} = AM or PM")]
        [SerializeField] private string hourFormat = "{0}:00 {1}";
        [SerializeField] private string msgNeedsPermission = "Allow notifications for GlowUp in the phone's settings to get the reminder.";
        [SerializeField] private string msgNotOnThisDevice = "Reminders are scheduled on an Android phone. Nothing is scheduled here.";
        [Tooltip("{0} = the time")]
        [SerializeField] private string msgReminderOn = "You will be reminded every day at {0}.";
        [SerializeField] private string msgReminderOff = "The daily reminder is off.";
        [SerializeField] private string msgRulesOn = "You will see a notice when the ingredient rules change.";
        [SerializeField] private string msgRulesOff = "Rule update notices are off.";

        [Header("Wording of the notifications")]
        [SerializeField] private string reminderTitle = "Time for your skin log";
        [SerializeField] private string reminderText = "Take a moment to note how your skin feels today.";
        [SerializeField] private string ruleUpdateTitle = "Ingredient safety rules updated";
        [SerializeField] private string ruleUpdateText = "Your next scans use the latest ingredient information.";

        void Start()
        {
            // the notification wording typed above is what the phone shows
            NotificationPreferences.ReminderTitle = reminderTitle; NotificationPreferences.ReminderText = reminderText;
            NotificationPreferences.RuleUpdateTitle = ruleUpdateTitle; NotificationPreferences.RuleUpdateText = ruleUpdateText;

            if (reminderHourDropdown != null)
            {
                var hours = new List<string>();
                for (int h = 0; h < 24; h++) hours.Add(HourText(h));
                reminderHourDropdown.ClearOptions();
                reminderHourDropdown.AddOptions(hours);
                reminderHourDropdown.SetValueWithoutNotify(NotificationPreferences.ReminderHour);
                reminderHourDropdown.RefreshShownValue();
                reminderHourDropdown.onValueChanged.AddListener(OnHourChanged);
            }
            if (dailyReminderToggle != null)
            {
                dailyReminderToggle.SetIsOnWithoutNotify(NotificationPreferences.DailyReminder);
                dailyReminderToggle.onValueChanged.AddListener(OnDailyChanged);
            }
            if (ruleUpdatesToggle != null)
            {
                ruleUpdatesToggle.SetIsOnWithoutNotify(NotificationPreferences.RuleUpdates);
                ruleUpdatesToggle.onValueChanged.AddListener(OnRulesChanged);
            }
            if (messageText != null) messageText.text = "";
            PaintHour();
        }

        string HourText(int hour) => string.Format(hourFormat, hour % 12 == 0 ? 12 : hour % 12, hour < 12 ? "AM" : "PM");

        void PaintHour() { if (reminderHourDropdown != null) reminderHourDropdown.interactable = NotificationPreferences.DailyReminder; }

        void OnHourChanged(int hour)
        {
            NotificationPreferences.ReminderHour = hour;         // the reminder is scheduled again for the new time
            if (NotificationPreferences.DailyReminder) Say(string.Format(msgReminderOn, HourText(hour)));
        }

        void OnDailyChanged(bool on)
        {
            if (!on) { NotificationPreferences.DailyReminder = false; PaintHour(); Say(msgReminderOff); return; }
            StopAllCoroutines();
            StartCoroutine(SwitchReminderOn());
        }

        IEnumerator SwitchReminderOn()
        {
            if (!LocalNotificationService.Supported)
            {
                // keep the choice, so it takes effect on a phone; say why nothing happens here
                NotificationPreferences.DailyReminder = true; PaintHour();
                Say(msgNotOnThisDevice);
                yield break;
            }
            LocalNotificationService.PermissionAnswer answer = LocalNotificationService.RequestPermission();
            float waited = 0f;
            while (!answer.IsDone && waited < 60f) { waited += Time.unscaledDeltaTime; yield return null; }

            if (answer.Granted)
            {
                NotificationPreferences.DailyReminder = true;
                Say(string.Format(msgReminderOn, HourText(NotificationPreferences.ReminderHour)));
            }
            else
            {
                NotificationPreferences.DailyReminder = false;
                if (dailyReminderToggle != null) dailyReminderToggle.SetIsOnWithoutNotify(false);
                Say(msgNeedsPermission);
            }
            PaintHour();
        }

        void OnRulesChanged(bool on)
        {
            NotificationPreferences.RuleUpdates = on;
            Say(on ? msgRulesOn : msgRulesOff);
            if (on && LocalNotificationService.Supported && !LocalNotificationService.PermissionGranted) LocalNotificationService.RequestPermission();
        }

        void Say(string message) { if (messageText != null) messageText.text = message; }
    }
}
