using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Everything the user enters during onboarding, shared by all five steps and the Terms scene.
    /// Non-secret fields are mirrored to PlayerPrefs so a restart mid-onboarding doesn't lose them.
    /// The PASSWORD IS KEPT IN MEMORY ONLY and is sent to Firebase Auth once, at account creation.
    /// </summary>
    public static class OnboardingSession
    {
        const string Prefix = "GlowUp.Onb.";

        public static string FirstName = "", MiddleName = "", LastName = "", Username = "", Email = "";
        public static string BirthdayIso = "";        // yyyy-MM-dd
        public static int Age;
        public static string SkinType = "", SkinTone = "";
        public static List<string> SkinConditions = new List<string>();
        public static List<string> Allergens = new List<string>();
        public static string Password = "";           // never persisted
        // Items the user typed that were not in the lists. Sent to the database for review after the account exists.
        public static List<string> CustomConditions = new List<string>();
        public static List<string> CustomAllergens = new List<string>();

        /// <summary>
        /// True while a SIGNED-IN user retakes the skin quiz from the Settings page. Steps 2 to 4 then run on
        /// their own: Back on Step 2 returns to <see cref="RetakeReturnScene"/>, and Continue on Step 4 saves the
        /// answers into the existing profile instead of going on to the summary and account creation.
        /// Kept in memory only.
        /// </summary>
        public static bool Retake;
        public static string RetakeReturnScene = "";

        /// <summary>
        /// True while someone who has just signed in with Google or Facebook goes through the sign-up steps.
        /// Their account already exists, so Step 1 asks for no password and the Terms page only saves the
        /// profile. Kept in memory only (the login page sets it again if the app was closed in between).
        /// </summary>
        public static bool Social;

        /// <summary>Starts the sign-up steps for a Google / Facebook account: the email and name come from that account.</summary>
        public static void BeginSocial(string email, string displayName)
        {
            Clear();
            Social = true;
            Email = (email ?? "").Trim().ToLowerInvariant();
            string[] parts = (displayName ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) FirstName = parts[0];
            else if (parts.Length > 1) { LastName = parts[parts.Length - 1]; FirstName = string.Join(" ", parts, 0, parts.Length - 1); }
            SaveDraft();
        }

        /// <summary>Starts a retake: the quiz opens with no answers; nothing of the account (name, email) is put in the draft.</summary>
        public static void BeginRetake(string returnScene)
        {
            Clear();
            Retake = true; RetakeReturnScene = returnScene ?? "";
            SaveDraft();
        }

        public static void EndRetake()
        {
            Retake = false; RetakeReturnScene = "";
            Clear();
        }

        public static void SaveDraft()
        {
            PlayerPrefs.SetString(Prefix + "First", FirstName);
            PlayerPrefs.SetString(Prefix + "Middle", MiddleName);
            PlayerPrefs.SetString(Prefix + "Last", LastName);
            PlayerPrefs.SetString(Prefix + "User", Username);
            PlayerPrefs.SetString(Prefix + "Email", Email);
            PlayerPrefs.SetString(Prefix + "Birthday", BirthdayIso);
            PlayerPrefs.SetInt(Prefix + "Age", Age);
            PlayerPrefs.SetString(Prefix + "Type", SkinType);
            PlayerPrefs.SetString(Prefix + "Tone", SkinTone);
            PlayerPrefs.SetString(Prefix + "Cond", string.Join("|", SkinConditions));
            PlayerPrefs.SetString(Prefix + "Allergens", string.Join("|", Allergens));
            PlayerPrefs.Save();
        }

        public static void LoadDraft()
        {
            FirstName = PlayerPrefs.GetString(Prefix + "First", FirstName);
            MiddleName = PlayerPrefs.GetString(Prefix + "Middle", MiddleName);
            LastName = PlayerPrefs.GetString(Prefix + "Last", LastName);
            Username = PlayerPrefs.GetString(Prefix + "User", Username);
            Email = PlayerPrefs.GetString(Prefix + "Email", Email);
            BirthdayIso = PlayerPrefs.GetString(Prefix + "Birthday", BirthdayIso);
            Age = PlayerPrefs.GetInt(Prefix + "Age", Age);
            SkinType = PlayerPrefs.GetString(Prefix + "Type", SkinType);
            SkinTone = PlayerPrefs.GetString(Prefix + "Tone", SkinTone);
            SkinConditions = Split(PlayerPrefs.GetString(Prefix + "Cond", ""));
            Allergens = Split(PlayerPrefs.GetString(Prefix + "Allergens", ""));
        }

        public static void Clear()
        {
            Social = false;
            FirstName = MiddleName = LastName = Username = Email = BirthdayIso = SkinType = SkinTone = Password = "";
            Age = 0; SkinConditions = new List<string>(); Allergens = new List<string>();
            CustomConditions = new List<string>(); CustomAllergens = new List<string>();
            foreach (var k in new[] { "First", "Middle", "Last", "User", "Email", "Birthday", "Age", "Type", "Tone", "Cond", "Allergens" })
                PlayerPrefs.DeleteKey(Prefix + k);
            PlayerPrefs.Save();
        }

        static List<string> Split(string s)
        {
            var l = new List<string>();
            foreach (var p in s.Split('|')) if (!string.IsNullOrWhiteSpace(p)) l.Add(p.Trim());
            return l;
        }

        public static UserProfileData ToProfile(string uid) => new UserProfileData
        {
            uid = uid, firstName = FirstName, middleName = MiddleName, lastName = LastName,
            username = Username, email = Email, birthday = BirthdayIso, age = Age,
            skinType = SkinType, skinTone = SkinTone,
            skinConditions = new List<string>(SkinConditions), knownAllergens = new List<string>(Allergens),
            termsAccepted = true, createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
