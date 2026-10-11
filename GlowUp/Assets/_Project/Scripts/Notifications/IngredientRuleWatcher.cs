using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Notifications
{
    /// <summary>
    /// Notices when an admin changed the ingredient rules on the website. Each time the app downloads
    /// the ingredient list it compares it with the one it saw last; a difference in the entries the app
    /// trusts (typed or approved by an admin) counts as an update.
    ///
    /// The user is then told: a notice inside the app (RuleUpdateBanner on the dashboard) and, when
    /// notifications are allowed, a notification on the phone. This happens while the app is open and
    /// online; nothing is pushed from a server.
    /// </summary>
    public static class IngredientRuleWatcher
    {
        const string SignatureKey = "GlowUp.Rules.Signature", UnseenKey = "GlowUp.Rules.Unseen", ChangedAtKey = "GlowUp.Rules.ChangedAtUnix";

        /// <summary>Raised when a rule change was noticed.</summary>
        public static event Action Updated;

        /// <summary>True from the moment a change is noticed until the user dismisses the notice.</summary>
        public static bool HasUnseenUpdate => PlayerPrefs.GetInt(UnseenKey, 0) == 1;

        /// <summary>When the last change was noticed on this phone (Unix seconds; 0 = never).</summary>
        public static long LastChangeUnix => long.TryParse(PlayerPrefs.GetString(ChangedAtKey, "0"), out long t) ? t : 0;

        public static void MarkSeen()
        {
            PlayerPrefs.SetInt(UnseenKey, 0);
            PlayerPrefs.Save();
        }

        /// <summary>Called by IngredientRepository after each successful download of the website's list.</summary>
        public static void OnDownloaded(List<IngredientData> items)
        {
            try
            {
                string signature = Signature(items);
                string before = PlayerPrefs.GetString(SignatureKey, "");
                if (signature == before) return;
                PlayerPrefs.SetString(SignatureKey, signature);
                if (before.Length == 0) { PlayerPrefs.Save(); return; }        // the first download on this phone is not "an update"

                PlayerPrefs.SetInt(UnseenKey, 1);
                PlayerPrefs.SetString(ChangedAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                PlayerPrefs.Save();
                if (NotificationPreferences.RuleUpdates)
                    LocalNotificationService.ShowNow(ReminderScheduler.RuleUpdateId, NotificationPreferences.RuleUpdateTitle, NotificationPreferences.RuleUpdateText);
                Updated?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning("Rule update check skipped: " + e.Message); }
        }

        /// <summary>One short text that changes whenever a trusted entry is added, removed or edited.</summary>
        static string Signature(List<IngredientData> items)
        {
            var lines = new List<string>();
            foreach (IngredientData i in items ?? new List<IngredientData>())
            {
                if (i == null || i.IsUnreviewedAi()) continue;                 // entries the app's own AI lookup added are not an admin's change
                lines.Add(string.Join("|", i.id, i.name, i.techName, i.description, i.riskLevel, i.cautionNote, i.minAge.ToString(),
                    i.keyActive ? "1" : "0", i.umbrella ? "1" : "0", Join(i.aliases), Join(i.allergenTags), Join(i.avoidForSkinTypes), Join(i.avoidForConditions),
                    Join(i.goodForSkinTypes), Join(i.goodForConditions)));
            }
            lines.Sort(string.CompareOrdinal);

            ulong hash = 14695981039346656037UL;                               // FNV-1a, 64 bit
            foreach (string line in lines)
            {
                foreach (char c in line) { hash ^= c; hash *= 1099511628211UL; }
                hash ^= '\n'; hash *= 1099511628211UL;
            }
            return lines.Count + ":" + hash.ToString("x16");
        }

        static string Join(List<string> list) => list == null ? "" : string.Join(",", list);
    }
}
