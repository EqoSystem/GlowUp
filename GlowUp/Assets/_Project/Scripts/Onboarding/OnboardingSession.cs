using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// Everything the user enters during onboarding, shared by all five steps and the Terms scene.
    /// Non-secret fields are mirrored to PlayerPrefs so a restart mid-onboarding doesn't lose them.
    /// The PASSWORD IS KEPT IN MEMORY ONLY and is sent to Firebase Auth once, at account creation (on Step 1).
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
        /// <summary>Optional answer to "How does your skin react?" (ids of SensitiveReactions.All); only kept while the skin type is Sensitive.</summary>
        public static List<string> SensitiveReactions = new List<string>();
        public static bool HealthConsent;             // the second box on the Terms page was ticked: set just before the profile is saved, never persisted
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

        /// <summary>Said on the sign-up steps after a Google sign-in: Google gives only a name and an email, so everything else is asked here.</summary>
        public const string SocialReminder = "You signed in with Google. Please fill in every field in these steps. Google does not share your age or birthday, so you will be asked for it on the last step.";

        /// <summary>
        /// The account this sign-up already has, by its id ("" = none yet). Since 12 October 2026 the account is made on
        /// Step 1, so that the "confirm your email" message can be sent; it is also set when somebody logs in with an
        /// account that has no profile yet. With an account, Step 1 asks for no password any more, the email cannot be
        /// changed without dropping the account, and the Terms page only saves the profile. Kept with the draft (an id
        /// is not a secret), so a sign-up that was interrupted carries on with the same account.
        /// </summary>
        public static string AccountUid = "";
        /// <summary>True when that account was made during THIS sign-up: leaving the sign-up removes it again, so the email stays free.</summary>
        public static bool AccountMadeHere;
        public static bool HasAccount => !string.IsNullOrEmpty(AccountUid);

        /// <summary>The sign-up goes on without its account (it was removed, or nobody is signed in to it any more).</summary>
        public static void ForgetAccount()
        {
            AccountUid = ""; AccountMadeHere = false; Password = "";
            SaveDraft();
        }

        /// <summary>Starts the sign-up steps for somebody who logged in with an account that has no profile yet.</summary>
        public static void BeginExisting(string uid, string email)
        {
            Clear();
            AccountUid = uid ?? ""; AccountMadeHere = false;
            Email = (email ?? "").Trim().ToLowerInvariant();
            SaveDraft();
        }

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
            Retake = true; RetakeReturnScene = returnScene ?? ""; RetakeWithDetails = false;
            SaveDraft();
        }

        /// <summary>
        /// New17: true while the retake started from Settings runs through ALL steps (1 to 5) with the saved answers filled in. The email
        /// and password are locked, and Step 5 ends with "Save edits" / "Cancel" instead of the Terms page. False = the older
        /// quiz-only retake (Steps 2 to 4, saved on Step 4), still used after a sign-in whose quiz was reset.
        /// </summary>
        public static bool RetakeWithDetails;

        /// <summary>Starts the full retake (Steps 1 to 5) with what `profile` holds now, so the user edits instead of starting over.</summary>
        public static void BeginRetakeWith(UserProfileData profile, string returnScene)
        {
            BeginRetake(returnScene);
            RetakeWithDetails = true;
            if (profile != null)
            {
                FirstName = profile.firstName ?? ""; MiddleName = profile.middleName ?? ""; LastName = profile.lastName ?? "";
                Email = (profile.email ?? "").Trim().ToLowerInvariant();
                BirthdayIso = profile.birthday ?? ""; Age = profile.age;
                SkinType = profile.skinType ?? ""; SkinTone = profile.skinTone ?? "";
                SkinConditions = new List<string>(profile.skinConditions ?? new List<string>());
                Allergens = new List<string>(profile.knownAllergens ?? new List<string>());
                SensitiveReactions = new List<string>(profile.sensitiveReactions ?? new List<string>());
            }
            SaveDraft();
        }

        public static void EndRetake()
        {
            Retake = false; RetakeReturnScene = ""; RetakeWithDetails = false;
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
            PlayerPrefs.SetString(Prefix + "React", string.Join("|", SensitiveReactions));
            PlayerPrefs.SetString(Prefix + "Account", AccountUid ?? "");
            PlayerPrefs.SetInt(Prefix + "AccountMadeHere", AccountMadeHere ? 1 : 0);
            // a quiz retake is remembered too: after a restart it is still ONLY the quiz (Steps 2 to 4), never the account steps
            PlayerPrefs.SetInt(Prefix + "Retake", Retake ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "RetakeReturn", RetakeReturnScene ?? "");
            PlayerPrefs.SetInt(Prefix + "RetakeDetails", RetakeWithDetails ? 1 : 0);
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
            SensitiveReactions = Split(PlayerPrefs.GetString(Prefix + "React", ""));
            AccountUid = PlayerPrefs.GetString(Prefix + "Account", AccountUid ?? "");
            AccountMadeHere = PlayerPrefs.GetInt(Prefix + "AccountMadeHere", AccountMadeHere ? 1 : 0) == 1;
            Retake = PlayerPrefs.GetInt(Prefix + "Retake", Retake ? 1 : 0) == 1;
            RetakeReturnScene = PlayerPrefs.GetString(Prefix + "RetakeReturn", RetakeReturnScene ?? "");
            RetakeWithDetails = Retake && PlayerPrefs.GetInt(Prefix + "RetakeDetails", RetakeWithDetails ? 1 : 0) == 1;
        }

        public static void Clear()
        {
            Social = false; AccountUid = ""; AccountMadeHere = false;
            FirstName = MiddleName = LastName = Username = Email = BirthdayIso = SkinType = SkinTone = Password = "";
            Age = 0; SkinConditions = new List<string>(); Allergens = new List<string>(); SensitiveReactions = new List<string>();
            CustomConditions = new List<string>(); CustomAllergens = new List<string>();
            foreach (var k in new[] { "First", "Middle", "Last", "User", "Email", "Birthday", "Age", "Type", "Tone", "Cond", "Allergens", "React", "Account", "AccountMadeHere", "Retake", "RetakeReturn", "RetakeDetails" })
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
            sensitiveReactions = string.Equals(SkinType, "Sensitive", System.StringComparison.OrdinalIgnoreCase) ? new List<string>(SensitiveReactions) : new List<string>(),
            termsAccepted = true, createdAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            healthConsentAtUnix = HealthConsent ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0
        };
    }
}
