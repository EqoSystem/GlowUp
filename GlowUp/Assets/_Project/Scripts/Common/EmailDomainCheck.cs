using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Data;
using GlowUp.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace GlowUp.Common
{
    /// <summary>
    /// THE ONLINE HALF OF THE EMAIL RULE: asks the internet's own address book (DNS) whether a mail domain really receives
    /// mail. It is the same question every mail program asks before it sends a message.
    ///
    /// What is sent: ONLY THE PART AFTER THE @ ("up.edu.ph"). Never the name before it, never the whole address, and
    /// nothing else about the user. It goes to a public DNS service over a protected connection (first Google's, and
    /// Cloudflare's when the first gives no answer). No key is needed.
    ///
    /// Measured on 12 October 2026: one question takes 0.1 to 0.6 seconds; a domain that does not exist comes back as
    /// "status 3"; a domain that takes no mail has no mail server entry or the entry "."; a throw-away service shows in
    /// the name of the mail server. Each question has a time limit, and answers are kept (a good one for 30 days on the
    /// phone, a bad one for this run of the app) so the same domain is not asked again on the next page.
    /// </summary>
    public static class EmailDomainCheck
    {
        /// <summary>{0} = the domain. Asked in this order until one answers.</summary>
        public static string[] Services =
        {
            "https://dns.google/resolve?name={0}&type=MX",
            "https://cloudflare-dns.com/dns-query?name={0}&type=MX",
        };
        public const string ThrowAwayList = "EmailThrowAwayDomains";        // a text file in a Resources folder
        public static int KeepGoodDays = 30;

        static readonly Dictionary<string, EmailDomains.Mail> thisRun = new Dictionary<string, EmailDomains.Mail>(StringComparer.Ordinal);
        static bool prepared;

        /// <summary>Reads the long list of throw-away mail domains once. Safe to call often.</summary>
        public static void Prepare()
        {
            if (prepared) return;
            prepared = true;
            try
            {
                var list = Resources.Load<TextAsset>(ThrowAwayList);
                if (list != null) EmailDomains.AddThrowAway(list.text);
                else { }
            }
            catch (Exception e) { { } }
        }

        /// <summary>An answer the app already has for this domain (from this run, or a good one kept on the phone).</summary>
        public static bool Recall(string domain, out EmailDomains.Mail mail)
        {
            domain = (domain ?? "").Trim().ToLowerInvariant();
            if (thisRun.TryGetValue(domain, out mail)) return true;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (KeptDomain k in Kept().items)
                if (k != null && k.domain == domain && now - k.savedAtUnix >= 0 && now - k.savedAtUnix <= KeepGoodDays * 86400L) { mail = EmailDomains.Mail.Receives; return true; }
            mail = EmailDomains.Mail.NoAnswer;
            return false;
        }

        /// <summary>
        /// Asks whether the domain receives mail. Never throws and never takes longer than `timeLimitSeconds` in all:
        /// when no service answers in time the result is NoAnswer, and the caller says so on the screen.
        /// </summary>
        public static async Task<EmailDomains.Mail> Ask(string domain, float timeLimitSeconds = 8f)
        {
            domain = (domain ?? "").Trim().ToLowerInvariant();
            if (!EmailDomains.DomainWellFormed(domain)) return EmailDomains.Mail.NoSuchDomain;
            if (Recall(domain, out EmailDomains.Mail known)) return known;

            int each = Mathf.Max(2, Mathf.RoundToInt(timeLimitSeconds / Mathf.Max(1, Services.Length)));       // EVERY LONG STEP HAS A TIME LIMIT
            foreach (string service in Services)
            {
                EmailDomains.Mail mail = await AskOne(string.Format(service, UnityWebRequest.EscapeURL(domain)), each);
                if (mail == EmailDomains.Mail.NoAnswer) continue;
                thisRun[domain] = mail;
                if (mail == EmailDomains.Mail.Receives) Keep(domain);
                return mail;
            }
            return EmailDomains.Mail.NoAnswer;
        }

        static async Task<EmailDomains.Mail> AskOne(string address, int timeoutSeconds)
        {
            try
            {
                using (var request = UnityWebRequest.Get(address))
                {
                    request.timeout = timeoutSeconds;
                    request.SetRequestHeader("Accept", "application/dns-json");
                    await request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200) return EmailDomains.Mail.NoAnswer;
                    return EmailDomains.ReadAnswer(request.downloadHandler.text);
                }
            }
            catch (Exception e)
            {
                { }
                return EmailDomains.Mail.NoAnswer;
            }
        }

        // =====================================================================================
        //  Good answers kept on the phone (domain names only, never an address)
        // =====================================================================================
        [Serializable] class KeptDomain { public string domain = ""; public long savedAtUnix; }
        [Serializable] class KeptDomains { public List<KeptDomain> items = new List<KeptDomain>(); }

        const string KeptFile = "email_domains";
        const int KeepMost = 40;
        static KeptDomains kept;

        static KeptDomains Kept()
        {
            if (kept == null && !OfflineCache.TryLoad(KeptFile, out kept)) kept = new KeptDomains();
            return kept ?? (kept = new KeptDomains());
        }

        static void Keep(string domain)
        {
            try
            {
                KeptDomains all = Kept();
                all.items.RemoveAll(k => k == null || k.domain == domain);
                all.items.Add(new KeptDomain { domain = domain, savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                while (all.items.Count > KeepMost) all.items.RemoveAt(0);
                OfflineCache.Save(KeptFile, all);
            }
            catch (Exception e) { { } }
        }
    }
}
