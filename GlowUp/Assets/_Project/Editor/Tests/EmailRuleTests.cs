using GlowUp.Common;
using NUnit.Framework;
using UnityEngine;

namespace GlowUp.Tests
{
    /// <summary>
    /// Checks which email addresses may sign up (EmailDomains): how an address must be written, that well-known providers
    /// need no internet, that throw-away services and one-letter spelling slips are refused, that every other domain is
    /// sent to the online check, and how the answer of that check is read. The answers used here are the ones the two
    /// public DNS services really gave on 12 October 2026.
    /// </summary>
    public class EmailRuleTests
    {
        [Test]
        public void AnAddressMustBeWrittenLikeOne()
        {
            foreach (string good in new[] { "ana@gmail.com", "ana.cruz+shop@up.edu.ph", "a@b.co", "JUAN_dela-cruz99@student.dlsu.edu.ph", "o'brien@example.org" })
                Assert.IsTrue(EmailDomains.WellFormed(good), good);
            foreach (string bad in new[]
            {
                "", "ana", "ana@", "@gmail.com", "ana@@gmail.com", "ana@gmail", "ana@gmail.", "ana@.com", "ana@gmail..com",
                ".ana@gmail.com", "ana.@gmail.com", "an..a@gmail.com", "ana cruz@gmail.com", "ana@gma il.com",
                "ana@-gmail.com", "ana@gmail-.com", "ana@gmail.c", "ana@gmail.c0m", "ana@192.168.1.1", "aña@gmail.com", "ana@gmail,com",
            })
                Assert.IsFalse(EmailDomains.WellFormed(bad), "'" + bad + "'");
            Assert.IsFalse(EmailDomains.WellFormed(null));
            Assert.IsTrue(EmailDomains.WellFormed(new string('a', 64) + "@gmail.com"), "64 letters before the @ is the most");
            Assert.IsFalse(EmailDomains.WellFormed(new string('a', 65) + "@gmail.com"));
            Assert.IsFalse(EmailDomains.WellFormed("ana@" + new string('b', 64) + ".com"), "one word of a domain is 63 letters at most");
            Assert.IsTrue(InputRules.IsValidEmail("ana@up.edu.ph"), "the old helper now only looks at the shape");
            Assert.AreEqual("gmail.com", EmailDomains.DomainOf("Ana@GMAIL.com"));
            Assert.AreEqual("", EmailDomains.DomainOf("ana@"));
            Assert.AreEqual("", EmailDomains.DomainOf(null));
        }

        [Test]
        public void WellKnownProvidersNeedNoInternet()
        {
            foreach (string email in new[] { "ana@gmail.com", "ana@Yahoo.com", "ana@yahoo.com.ph", "ana@outlook.com", "ana@hotmail.com", "ana@icloud.com", "ana@proton.me", " ana@gmail.com " })
                Assert.AreEqual(EmailDomains.Kind.WellKnown, EmailDomains.Judge(email), email);
            Assert.AreEqual(EmailDomains.Kind.BadShape, EmailDomains.Judge("ana@gmail"));
            Assert.AreEqual(EmailDomains.Kind.BadShape, EmailDomains.Judge(""));
        }

        [Test]
        public void EveryOtherDomainGoesToTheOnlineCheck()
        {
            foreach (string email in new[] { "ana@up.edu.ph", "ana@deped.gov.ph", "ana@smallcompany.ph", "ana@gmail.com.ph", "ana@mail.co.uk" })
                Assert.AreEqual(EmailDomains.Kind.NeedsCheck, EmailDomains.Judge(email), email);
            // a domain the project decides to trust (typed in the Inspector) is accepted at once
            Assert.AreEqual(EmailDomains.Kind.WellKnown, EmailDomains.Judge("ana@myschool.edu.ph", new[] { " MySchool.edu.ph " }));
            Assert.AreEqual(EmailDomains.Kind.NeedsCheck, EmailDomains.Judge("ana@otherschool.edu.ph", new[] { "myschool.edu.ph", "", null }));
        }

        [Test]
        public void ThrowAwayAddressesAreRefused()
        {
            Assert.AreEqual(EmailDomains.Kind.ThrowAway, EmailDomains.Judge("ana@mailinator.com"));
            Assert.AreEqual(EmailDomains.Kind.ThrowAway, EmailDomains.Judge("ana@inbox.mailinator.com"), "anything under a throw-away service is one too");
            Assert.IsTrue(EmailDomains.IsThrowAway("mail2.mailinator.com."), "the name of a mail server, as the address book writes it");
            Assert.IsFalse(EmailDomains.IsThrowAway("hellomailinator.example"), "only whole words of the name count");
            Assert.IsFalse(EmailDomains.IsThrowAway("gmail.com"));
            Assert.IsFalse(EmailDomains.IsThrowAway(""));
            Assert.IsFalse(EmailDomains.IsThrowAway(null));

            // a list in the form of the text file: notes, empty lines and lines that are no domain are skipped
            Assert.IsFalse(EmailDomains.IsThrowAway("test-only-throwaway.example"));
            int added = EmailDomains.AddThrowAway("# a note\n\nTest-Only-Throwaway.example\r\nnot a domain\ngmail.com\ntest-only-throwaway.example\n");
            Assert.AreEqual(1, added, "one new domain: the note, the empty line, the broken line, the well-known provider and the repeat add nothing");
            Assert.AreEqual(EmailDomains.Kind.ThrowAway, EmailDomains.Judge("ana@test-only-throwaway.example"));
            Assert.AreEqual(EmailDomains.Kind.WellKnown, EmailDomains.Judge("ana@gmail.com"), "a list can never refuse a well-known provider");
        }

        [Test]
        public void TheLongListOfThrowAwayDomainsIsInTheApp()
        {
            var list = Resources.Load<TextAsset>(EmailDomainCheck.ThrowAwayList);
            Assert.IsNotNull(list, "Resources/" + EmailDomainCheck.ThrowAwayList + ".txt");
            EmailDomains.AddThrowAway(list.text);
            Assert.Greater(EmailDomains.ThrowAwayCount, 5000);
            foreach (string domain in new[] { "yopmail.com", "guerrillamail.com", "10minutemail.com", "temp-mail.org" }) Assert.IsTrue(EmailDomains.IsThrowAway(domain), domain);
            foreach (string domain in new[] { "gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "icloud.com", "proton.me", "up.edu.ph", "deped.gov.ph" }) Assert.IsFalse(EmailDomains.IsThrowAway(domain), domain);
        }

        [Test]
        public void ASpellingSlipIsNotAProvider()
        {
            foreach (string domain in new[] { "gmial.com", "gmai.com", "gmaill.com", "gmail.con", "gmail.co", "gnail.com" }) Assert.AreEqual("gmail.com", EmailDomains.LooksLike(domain), domain);
            Assert.AreEqual("hotmail.com", EmailDomains.LooksLike("hotmial.com"));
            Assert.AreEqual("yahoo.com", EmailDomains.LooksLike("yahooo.com"));
            Assert.AreEqual("outlook.com", EmailDomains.LooksLike("outlok.com"));
            Assert.AreEqual("icloud.com", EmailDomains.LooksLike("iclould.com"));
            Assert.AreEqual(EmailDomains.Kind.LookAlike, EmailDomains.Judge("ana@gmial.com"));
            EmailDomains.AddThrowAway("gmial.com");
            Assert.AreEqual(EmailDomains.Kind.LookAlike, EmailDomains.Judge("ana@gmial.com"), "a slip that is also on the throw-away list still gets the helpful answer");
            Assert.AreEqual("ana@gmail.com", EmailDomains.Suggestion("ana@gmial.com"));
            Assert.AreEqual("", EmailDomains.Suggestion("ana@gmail.com"));
            Assert.AreEqual("", EmailDomains.Suggestion("ana@up.edu.ph"));

            // real providers that happen to be one letter away from another one stay what they are
            foreach (string real in new[] { "ymail.com", "mail.com", "email.com", "gmail.com" }) Assert.AreEqual("", EmailDomains.LooksLike(real), real);
            // two slips or more is simply another domain: the online check decides
            foreach (string other in new[] { "gmail.com.ph", "gmx.com", "hotmail.fr", "yahoo.co.uk", "mygmail.com" }) Assert.AreEqual("", EmailDomains.LooksLike(other), other);
        }

        [Test]
        public void TheAddressBooksAnswerIsRead()
        {
            // a university: it has mail servers
            Assert.AreEqual(EmailDomains.Mail.Receives, EmailDomains.ReadAnswer(
                "{\"Status\":0,\"TC\":false,\"Question\":[{\"name\":\"up.edu.ph.\",\"type\":15}],\"Answer\":[{\"name\":\"up.edu.ph.\",\"type\":15,\"TTL\":300,\"data\":\"5 alt2.aspmx.l.google.com.\"},{\"name\":\"up.edu.ph.\",\"type\":15,\"TTL\":300,\"data\":\"10 aspmx5.googlemail.com.\"}]}"));
            // example.com: the entry "." says "I take no mail"
            Assert.AreEqual(EmailDomains.Mail.None, EmailDomains.ReadAnswer(
                "{\"Status\":0,\"Question\":[{\"name\":\"example.com\",\"type\":15}],\"Answer\":[{\"name\":\"example.com\",\"type\":15,\"TTL\":300,\"data\":\"0 .\"}]}"));
            // a misspelt domain that somebody owns but that has no mail server
            Assert.AreEqual(EmailDomains.Mail.None, EmailDomains.ReadAnswer(
                "{\"Status\":0,\"Question\":[{\"name\":\"gmial.com.\",\"type\":15}],\"Comment\":\"Response from ns2.domainactive.org.\"}"));
            // a domain that does not exist
            Assert.AreEqual(EmailDomains.Mail.NoSuchDomain, EmailDomains.ReadAnswer(
                "{\"Status\":3,\"Question\":[{\"name\":\"no-such-domain-xyz-12345.com.\",\"type\":15}],\"Authority\":[{\"name\":\"com.\",\"type\":6,\"TTL\":900,\"data\":\"a.gtld-servers.net.\"}]}"));
            // a made-up looking domain whose mail goes to a throw-away service
            Assert.AreEqual(EmailDomains.Mail.ThrowAway, EmailDomains.ReadAnswer(
                "{\"Status\":0,\"Answer\":[{\"name\":\"some-new-name.com.\",\"type\":15,\"TTL\":300,\"data\":\"1 mail2.mailinator.com.\"}]}"));
            // another name for the domain (type 5) comes first, the mail server after it
            Assert.AreEqual(EmailDomains.Mail.Receives, EmailDomains.ReadAnswer(
                "{\"Status\":0,\"Answer\":[{\"name\":\"mail.school.ph.\",\"type\":5,\"TTL\":60,\"data\":\"school.ph.\"},{\"name\":\"school.ph.\",\"type\":15,\"TTL\":60,\"data\":\"10 mx.school.ph.\"}]}"));
            // only another name and no mail server
            Assert.AreEqual(EmailDomains.Mail.None, EmailDomains.ReadAnswer("{\"Status\":0,\"Answer\":[{\"name\":\"a.ph.\",\"type\":5,\"TTL\":60,\"data\":\"b.ph.\"}]}"));
            // the service itself is in trouble, or the answer is not an answer: the next service is asked
            Assert.AreEqual(EmailDomains.Mail.NoAnswer, EmailDomains.ReadAnswer("{\"Status\":2}"));
            Assert.AreEqual(EmailDomains.Mail.NoAnswer, EmailDomains.ReadAnswer("{}"));
            Assert.AreEqual(EmailDomains.Mail.NoAnswer, EmailDomains.ReadAnswer("<html>Sign in to this Wi-Fi</html>"));
            Assert.AreEqual(EmailDomains.Mail.NoAnswer, EmailDomains.ReadAnswer(""));
            Assert.AreEqual(EmailDomains.Mail.NoAnswer, EmailDomains.ReadAnswer(null));
        }
    }
}
