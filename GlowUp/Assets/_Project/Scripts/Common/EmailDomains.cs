using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Common
{
    /// <summary>
    /// WHICH EMAIL ADDRESSES MAY SIGN UP (the rule of 12 October 2026: "allow other e-mail domains, that is only verified
    /// and legitimate"). An address is judged by the part after the @ (its DOMAIN):
    ///
    ///   1. a well-known mail provider (Gmail, Outlook, Yahoo, iCloud ...)  -> accepted at once, also without internet;
    ///   2. one letter away from a big provider ("gmial.com", "gmail.con")   -> refused, with "Did you mean ...?";
    ///   3. a throw-away mail service (an address that lives ten minutes)    -> refused;
    ///   4. anything else (a school, a company, a smaller provider)          -> accepted only after the internet's own
    ///      address book (DNS) has confirmed that this domain RECEIVES MAIL. See EmailDomainCheck for that question.
    ///
    /// Everything in this file is plain logic with no internet and no screen, so it is covered by tests (EmailRuleTests).
    /// </summary>
    public static class EmailDomains
    {
        public enum Kind { BadShape, WellKnown, LookAlike, ThrowAway, NeedsCheck }

        /// <summary>What the internet's address book said about a domain.</summary>
        public enum Mail { NoAnswer, Receives, None, NoSuchDomain, ThrowAway }

        /// <summary>The longest an address may be (the internet's own limit) and the longest part before the @.</summary>
        public const int LongestAddress = 254, LongestName = 64;

        /// <summary>The signs the part before the @ may hold next to English letters and digits.</summary>
        const string NameSigns = ".!#$%&'*+/=?^_`{|}~-";

        // Accepted at once. All of them are large public mail providers; every other real domain passes through the online check.
        static readonly HashSet<string> wellKnown = new HashSet<string>(StringComparer.Ordinal)
        {
            "gmail.com", "googlemail.com",
            "yahoo.com", "yahoo.com.ph", "ymail.com", "rocketmail.com",
            "outlook.com", "outlook.ph", "hotmail.com", "live.com", "live.com.ph", "msn.com",
            "icloud.com", "me.com", "mac.com",
            "proton.me", "protonmail.com", "pm.me",
            "aol.com", "zoho.com", "gmx.com", "gmx.net", "mail.com", "email.com", "yandex.com", "tuta.com", "tutanota.com", "fastmail.com",
        };

        // A domain ONE typing slip away from one of these (and not itself well known) is taken for a spelling mistake.
        static readonly string[] oftenMistyped = { "gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "icloud.com" };

        // The best-known throw-away services, so the rule holds even if the long list (a text file) is missing.
        // The long list is added by EmailDomainCheck.Prepare() from Resources/EmailThrowAwayDomains.txt.
        static readonly HashSet<string> throwAway = new HashSet<string>(StringComparer.Ordinal)
        {
            "mailinator.com", "guerrillamail.com", "guerrillamail.net", "sharklasers.com", "grr.la", "10minutemail.com", "10minutemail.net",
            "temp-mail.org", "tempmail.com", "tempmailo.com", "tempail.com", "yopmail.com", "yopmail.net", "trashmail.com", "getnada.com",
            "maildrop.cc", "dispostable.com", "fakeinbox.com", "mailnesia.com", "mintemail.com", "moakt.com", "emailondeck.com",
            "mohmal.com", "discard.email", "throwawaymail.com", "mytemp.email", "tempr.email", "inboxkitten.com", "spamgourmet.com",
            "getairmail.com", "mailcatch.com", "tempinbox.com", "trashmail.net", "mailsac.com", "burnermail.io", "minutemail.com",
        };

        /// <summary>How many throw-away domains are known (the built-in ones plus the long list once it is read).</summary>
        public static int ThrowAwayCount => throwAway.Count;

        // =====================================================================================
        //  Shape
        // =====================================================================================
        /// <summary>The part after the @, in small letters ("" when there is none).</summary>
        public static string DomainOf(string email)
        {
            int at = (email ?? "").LastIndexOf('@');
            return at < 0 || at == email.Length - 1 ? "" : email.Substring(at + 1).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// True when the text is written like an email address: one @, a name of letters, digits and the usual signs before it
        /// (no dot at either end, no two dots in a row), and a domain after it. It says nothing yet about whether the domain is real.
        /// </summary>
        public static bool WellFormed(string email)
        {
            if (string.IsNullOrEmpty(email) || email.Length > LongestAddress) return false;
            int at = email.IndexOf('@');
            if (at <= 0 || at != email.LastIndexOf('@') || at > LongestName) return false;
            for (int i = 0; i < at; i++)
            {
                char c = email[i];
                if (!(IsLetter(c) || IsDigit(c) || NameSigns.IndexOf(c) >= 0)) return false;
                if (c == '.' && (i == 0 || i == at - 1 || email[i - 1] == '.')) return false;
            }
            return DomainWellFormed(email.Substring(at + 1));
        }

        /// <summary>A domain is words of letters, digits and hyphens with dots between them, and its last word is letters only ("up.edu.ph").</summary>
        public static bool DomainWellFormed(string domain)
        {
            if (string.IsNullOrEmpty(domain) || domain.Length > 253) return false;
            string[] words = domain.Split('.');
            if (words.Length < 2) return false;
            foreach (string word in words)
            {
                if (word.Length == 0 || word.Length > 63 || word[0] == '-' || word[word.Length - 1] == '-') return false;
                foreach (char c in word) if (!(IsLetter(c) || IsDigit(c) || c == '-')) return false;
            }
            string last = words[words.Length - 1];
            if (last.Length < 2) return false;
            foreach (char c in last) if (!IsLetter(c)) return false;
            return true;
        }

        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        static bool IsDigit(char c) => c >= '0' && c <= '9';

        // =====================================================================================
        //  The verdict that needs no internet
        // =====================================================================================
        /// <summary>Judges an address. `alsoTrusted` = domains the project accepts at once on top of the well-known ones.</summary>
        public static Kind Judge(string email, IList<string> alsoTrusted = null)
        {
            email = (email ?? "").Trim();
            if (!WellFormed(email)) return Kind.BadShape;
            string domain = DomainOf(email);
            if (IsWellKnown(domain)) return Kind.WellKnown;
            if (alsoTrusted != null)
                foreach (string trusted in alsoTrusted)
                    if (!string.IsNullOrWhiteSpace(trusted) && domain == trusted.Trim().ToLowerInvariant()) return Kind.WellKnown;
            // the spelling slip first: "Did you mean ...?" helps more than "refused" (some slips, like gmial.com, are on the throw-away list too)
            if (LooksLike(domain).Length > 0) return Kind.LookAlike;
            if (IsThrowAway(domain)) return Kind.ThrowAway;
            return Kind.NeedsCheck;
        }

        public static bool IsWellKnown(string domain) => wellKnown.Contains((domain ?? "").Trim().ToLowerInvariant());

        /// <summary>
        /// True for a throw-away mail domain and for anything under one ("inbox.mailinator.com"). Also used on the NAME OF THE
        /// MAIL SERVER a domain hands its mail to: a made-up domain whose mail goes to a throw-away service is one too.
        /// </summary>
        public static bool IsThrowAway(string domain)
        {
            string d = (domain ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            while (d.Length > 0)
            {
                if (throwAway.Contains(d)) return true;
                int dot = d.IndexOf('.');
                if (dot < 0 || d.IndexOf('.', dot + 1) < 0) return false;       // only two words left: that was the last one to try
                d = d.Substring(dot + 1);
            }
            return false;
        }

        /// <summary>The big provider this domain is one typing slip away from ("gmial.com" gives "gmail.com"); "" when it is none.</summary>
        public static string LooksLike(string domain)
        {
            string d = (domain ?? "").Trim().ToLowerInvariant();
            if (d.Length == 0 || wellKnown.Contains(d)) return "";
            foreach (string provider in oftenMistyped)
                if (OneSlipApart(d, provider)) return provider;
            return "";
        }

        /// <summary>The address with the domain put right ("ana@gmial.com" gives "ana@gmail.com"); "" when nothing looks mistyped.</summary>
        public static string Suggestion(string email)
        {
            email = (email ?? "").Trim();
            int at = email.LastIndexOf('@');
            string provider = at > 0 ? LooksLike(DomainOf(email)) : "";
            return provider.Length == 0 ? "" : email.Substring(0, at + 1) + provider;
        }

        /// <summary>One slip = one letter missing, one too many, one wrong, or two neighbours swapped.</summary>
        static bool OneSlipApart(string a, string b)
        {
            if (a == b || Math.Abs(a.Length - b.Length) > 1) return false;
            int start = 0;
            while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
            int endA = a.Length - 1, endB = b.Length - 1;
            while (endA >= start && endB >= start && a[endA] == b[endB]) { endA--; endB--; }
            int leftA = endA - start + 1, leftB = endB - start + 1;      // what differs in the middle
            if (leftA + leftB == 1) return true;                         // one letter missing or one too many
            if (leftA == 1 && leftB == 1) return true;                   // one wrong letter
            return leftA == 2 && leftB == 2 && a[start] == b[start + 1] && a[start + 1] == b[start];     // two neighbours swapped
        }

        /// <summary>Adds a list of throw-away domains (one per line; lines starting with # are notes). Returns how many were new.</summary>
        public static int AddThrowAway(string listText)
        {
            int added = 0;
            foreach (string raw in (listText ?? "").Split('\n'))
            {
                string line = raw.Trim().ToLowerInvariant();
                if (line.Length == 0 || line[0] == '#' || !DomainWellFormed(line) || wellKnown.Contains(line)) continue;      // a well-known provider is never refused by a list
                if (throwAway.Add(line)) added++;
            }
            return added;
        }

        // =====================================================================================
        //  Reading the address book's answer
        // =====================================================================================
        /// <summary>
        /// Reads the answer of a public DNS service to "which mail servers does this domain have?" (the JSON form that
        /// dns.google and cloudflare-dns.com both give). Status 3 = there is no such domain. A domain with no mail server,
        /// or with the special entry "." that means "I take no mail", does not receive mail. An answer that cannot be
        /// understood is NoAnswer, so the next service is asked.
        /// </summary>
        public static Mail ReadAnswer(string json)
        {
            Dictionary<string, object> root = null;
            try { root = MiniJson.Deserialize(json ?? "") as Dictionary<string, object>; } catch (Exception) { }
            if (root == null || !root.TryGetValue("Status", out object statusValue)) return Mail.NoAnswer;
            long status = Number(statusValue);
            if (status == 3) return Mail.NoSuchDomain;
            if (status != 0) return Mail.NoAnswer;

            bool receives = false;
            if (root.TryGetValue("Answer", out object answerValue) && answerValue is List<object> answers)
                foreach (object item in answers)
                {
                    if (!(item is Dictionary<string, object> record) || !record.TryGetValue("type", out object type) || Number(type) != 15) continue;     // 15 = a mail server entry
                    string data = record.TryGetValue("data", out object d) && d is string s ? s.Trim() : "";
                    int space = data.LastIndexOf(' ');
                    string server = (space >= 0 ? data.Substring(space + 1) : data).Trim().TrimEnd('.');
                    if (server.Length == 0) continue;                    // "0 ." : this domain says it takes no mail
                    if (IsThrowAway(server)) return Mail.ThrowAway;
                    receives = true;
                }
            return receives ? Mail.Receives : Mail.None;
        }

        static long Number(object value)
        {
            if (value == null || value is bool) return -1;
            if (value is string text) return long.TryParse(text.Trim(), out long parsed) ? parsed : -1;
            try { return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { return -1; }
        }
    }
}
