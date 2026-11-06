using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Onboarding;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Retaking or resetting the skin quiz must never touch the account (7 November 2026). The user's account document is
    /// captured BEFORE a retake, the retake's write is captured instead of being sent, and the document AFTER the write (merged
    /// the way the database merges it) must differ in the four skin-quiz fields only: never the name, email, sign-in way,
    /// birthday or the record of the Terms being accepted. Nothing is sent to Firebase.
    /// </summary>
    public class SkinRetakeTests
    {
        string writtenPath;
        Dictionary<string, object> written;

        [SetUp]
        public void SetUp()
        {
            writtenPath = null; written = null;
            UserProfileRepository.UidForTests = "u1";
            UserProfileRepository.SkinWriteForTests = (path, fields) => { writtenPath = path; written = fields; return Task.CompletedTask; };
        }

        [TearDown]
        public void TearDown()
        {
            UserProfileRepository.UidForTests = null;
            UserProfileRepository.SkinWriteForTests = null;
            OnboardingSession.EndRetake();
        }

        static Dictionary<string, object> AccountBefore() => new Dictionary<string, object>
        {
            { "firstName", "Maria" }, { "middleName", "Santos" }, { "lastName", "Cruz" }, { "username", "mariac" }, { "email", "maria@gmail.com" },
            { "birthday", "2004-10-15" }, { "fullName", "Maria Santos Cruz" }, { "nameId", "maria|santos|cruz" }, { "age", 22 },
            { "skinType", "Oily" }, { "skinTone", "Light" }, { "skinConditions", new List<string> { "Acne" } }, { "knownAllergens", new List<string> { "Fragrance" } },
            { "termsAccepted", true }, { "healthConsentAtUnix", 1762000000L }, { "theme", "dark" }, { "createdAt", "2026-10-01T08:00:00Z" },
            { "authProvider", "google.com" }
        };

        static void AssertOnlySkinFieldsChanged(Dictionary<string, object> before, Dictionary<string, object> after)
        {
            var skin = new HashSet<string>(UserProfileRepository.SkinFields);
            var keys = new HashSet<string>(before.Keys); keys.UnionWith(after.Keys);
            foreach (string key in keys)
            {
                if (skin.Contains(key)) continue;
                Assert.IsTrue(before.ContainsKey(key) && after.ContainsKey(key), "an account field appeared or disappeared: " + key);
                Assert.AreEqual(before[key], after[key], "the retake changed an account field: " + key);
            }
        }

        [Test]
        public void ARetakeChangesOnlyTheSkinFieldsOfTheAccountDocument()
        {
            Dictionary<string, object> before = AccountBefore();
            UserProfileRepository.SaveSkinProfile("Dry", "Medium", new List<string> { "Rosacea" }, new List<string> { "No known allergens" }).GetAwaiter().GetResult();

            Assert.AreEqual("users/u1", writtenPath, "the signed-in user's own document");
            Assert.IsNotNull(written);
            CollectionAssert.IsSubsetOf(written.Keys, UserProfileRepository.SkinFields, "the write holds skin-quiz fields only");
            Dictionary<string, object> after = UserProfileRepository.MergedWith(before, written);
            AssertOnlySkinFieldsChanged(before, after);
            Assert.AreEqual("Dry", after["skinType"]);
            Assert.AreEqual("Medium", after["skinTone"]);
            CollectionAssert.AreEqual(new[] { "Rosacea" }, (List<string>)after["skinConditions"]);
            Assert.AreEqual("maria@gmail.com", after["email"]);
            Assert.AreEqual(true, after["termsAccepted"], "the record of the Terms being accepted is untouched");
        }

        [Test]
        public void AResetClearsOnlyTheSkinFields()
        {
            Dictionary<string, object> before = AccountBefore();
            UserProfileRepository.SaveSkinProfile("", "", new List<string>(), new List<string>()).GetAwaiter().GetResult();
            Dictionary<string, object> after = UserProfileRepository.MergedWith(before, written);
            AssertOnlySkinFieldsChanged(before, after);
            Assert.AreEqual("", after["skinType"]);
            Assert.AreEqual("Maria", after["firstName"]);
        }

        [Test]
        public void TheSkinWriteNeverNamesAnAccountField()
        {
            Dictionary<string, object> patch = UserProfileRepository.SkinPatch("Dry", "Deep", null, null);
            foreach (string accountField in new[] { "email", "firstName", "lastName", "username", "birthday", "age", "termsAccepted", "healthConsentAtUnix", "createdAt", "password", "authProvider", "uid" })
                Assert.IsFalse(patch.ContainsKey(accountField), accountField + " must never be written by the quiz");
            CollectionAssert.AreEquivalent(UserProfileRepository.SkinFields, patch.Keys);
        }

        [Test]
        public void ARetakeIsStillARetakeAfterTheAppIsReopened()
        {
            OnboardingSession.BeginRetake("Settings");
            OnboardingSession.Retake = false; OnboardingSession.RetakeReturnScene = "";       // what a restart leaves in memory
            OnboardingSession.LoadDraft();
            Assert.IsTrue(OnboardingSession.Retake, "the quiz-only retake survives a restart, so Step 4 never leads on to Step 5");
            Assert.AreEqual("Settings", OnboardingSession.RetakeReturnScene);
            OnboardingSession.EndRetake();
            OnboardingSession.LoadDraft();
            Assert.IsFalse(OnboardingSession.Retake);
        }

        [Test]
        public void TheQuizScreensNeverSaveTheWholeProfile()
        {
            // the old way (UserProfileRepository.Save of the whole profile) wiped the account when the profile could not be loaded
            foreach (string file in new[] { "Onboarding/OnboardingStep4FlowController.cs", "Settings/SkinProfileResetAction.cs" })
            {
                string code = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Scripts", file));
                StringAssert.DoesNotContain("UserProfileRepository.Save(", code, file + " must use SaveSkinProfile");
                StringAssert.Contains("UserProfileRepository.SaveSkinProfile(", code, file);
            }
        }
    }
}
