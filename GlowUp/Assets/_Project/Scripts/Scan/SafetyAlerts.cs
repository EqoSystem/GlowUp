using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// The user's own choices for Ingredient Safety Alerts (Settings page), saved on the phone.
    /// A "trigger" is one thing the user wants to be warned about straight after a scan:
    ///   "allergens"  an ingredient from one of their own allergens
    ///   "harmful"    an ingredient that is banned or rated harmful
    ///   a skin-condition key ("eczema", "psoriasis" ...): an ingredient known to aggravate it
    /// </summary>
    public static class SafetyAlertPreferences
    {
        public const string Allergens = "allergens", Harmful = "harmful";
        /// <summary>The words the ingredient database uses for skin conditions.</summary>
        public static readonly string[] ConditionKeys = { "acne", "eczema", "psoriasis", "rosacea", "seborrheic", "melasma", "contact" };

        const string Prefix = "GlowUp.Alerts.";

        /// <summary>The master switch.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Prefix + "Enabled", 1) == 1;
            set { PlayerPrefs.SetInt(Prefix + "Enabled", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool Vibrate
        {
            get => PlayerPrefs.GetInt(Prefix + "Vibrate", 1) == 1;
            set { PlayerPrefs.SetInt(Prefix + "Vibrate", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Is this trigger switched on? Allergens and harmful ingredients start on; conditions start as in the user's profile.</summary>
        /// <summary>Alerts for skin conditions. Off since 27 Nov 2026: the app no longer asks for chronic skin conditions.</summary>
        public static bool ConditionAlerts = false;

        public static bool IsOn(string trigger)
        {
            bool startsOn = trigger == Allergens || trigger == Harmful;
            return PlayerPrefs.GetInt(Prefix + "On." + trigger, startsOn ? 1 : 0) == 1;
        }

        public static void Set(string trigger, bool on)
        {
            PlayerPrefs.SetInt(Prefix + "On." + trigger, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The first time (per account on this phone): the conditions the user listed in their profile are
        /// switched on, so a user with eczema is warned about eczema triggers without touching Settings.
        /// </summary>
        public static void SeedFromProfile(UserProfileData profile)
        {
            if (profile == null || !ConditionAlerts) return;
            string key = Prefix + "Seeded." + (string.IsNullOrEmpty(profile.uid) ? "guest" : profile.uid);
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
            foreach (string condition in profile.skinConditions ?? new List<string>())
            {
                string c = (condition ?? "").ToLowerInvariant();
                if (c.Contains("hyperpigmentation")) c += " melasma";
                if (c.Contains("atopic")) c += " eczema";
                foreach (string conditionKey in ConditionKeys) if (c.Contains(conditionKey)) Set(conditionKey, true);
            }
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }
    }

    /// <summary>One warning produced for a scan.</summary>
    [Serializable]
    public class SafetyAlert
    {
        /// <summary>A plain notice, not an ingredient warning (for example "this is a skin care product"). Its words are in `detail`.</summary>
        public const string Notice = "notice";
        public string trigger;                              // "allergens", "harmful", a condition key, or Notice
        public List<string> ingredients = new List<string>();
        public string detail = "";                          // allergens: the analysis' own sentence ("Contains your allergen ...")
    }

    /// <summary>
    /// Checks a scanned ingredient list against the triggers the user switched on. It runs on the phone
    /// (no internet needed), straight after the normal analysis.
    /// </summary>
    public static class SafetyAlertEvaluator
    {
        /// <summary>The warnings for this scan; empty when alerts are off or nothing matched.</summary>
        public static List<SafetyAlert> Evaluate(ScanRecord record, List<string> ingredientNames, UserProfileData profile)
        {
            var alerts = new List<SafetyAlert>();
            if (!SafetyAlertPreferences.Enabled || ingredientNames == null) return alerts;
            SafetyAlertPreferences.SeedFromProfile(profile);

            var harmful = new SafetyAlert { trigger = SafetyAlertPreferences.Harmful };
            var byCondition = new Dictionary<string, SafetyAlert>();
            foreach (string name in ingredientNames)
            {
                IngredientData data = IngredientKnowledge.Resolve(name);
                if (data == null) continue;
                string shown = !string.IsNullOrWhiteSpace(data.name) ? data.name : name;

                if (data.riskLevel == "harmful" && SafetyAlertPreferences.IsOn(SafetyAlertPreferences.Harmful) && !harmful.ingredients.Contains(shown))
                    harmful.ingredients.Add(shown);

                foreach (string condition in data.avoidForConditions ?? new List<string>())
                {
                    string conditionKey = (condition ?? "").Trim().ToLowerInvariant();
                    // skin-condition alerts ended with the chronic skin conditions (27 Nov 2026); a switch left on by an older version is ignored
                    if (!SafetyAlertPreferences.ConditionAlerts || conditionKey.Length == 0 || !SafetyAlertPreferences.IsOn(conditionKey)) continue;
                    if (!byCondition.TryGetValue(conditionKey, out SafetyAlert alert)) byCondition[conditionKey] = alert = new SafetyAlert { trigger = conditionKey };
                    if (!alert.ingredients.Contains(shown)) alert.ingredients.Add(shown);
                }
            }

            // the user's own allergens: the analysis has already matched them (it knows allergen families and typed-in allergens)
            if (record != null && SafetyAlertPreferences.IsOn(SafetyAlertPreferences.Allergens) && record.allergenMatch != null && record.allergenMatch.level == "bad")
            {
                alerts.Add(new SafetyAlert { trigger = SafetyAlertPreferences.Allergens, detail = record.allergenMatch.summary ?? "" });
            }
            if (harmful.ingredients.Count > 0) alerts.Add(harmful);
            foreach (string conditionKey in SafetyAlertPreferences.ConditionKeys)
                if (byCondition.TryGetValue(conditionKey, out SafetyAlert alert)) alerts.Add(alert);
            foreach (var extra in byCondition)                                  // a condition word the website added that is not in the list above
                if (Array.IndexOf(SafetyAlertPreferences.ConditionKeys, extra.Key) < 0) alerts.Add(extra.Value);
            return alerts;
        }
    }
}
