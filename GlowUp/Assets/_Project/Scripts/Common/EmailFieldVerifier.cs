using System;
using GlowUp.Net;
using GlowUp.Theme;
using TMPro;
using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// Belongs to the email input of the sign-up pages and decides whether the typed address may be used (see EmailDomains
    /// for the rule). A well-known provider is accepted at once. Any other domain is checked online a moment after the
    /// user stops typing: the page says "Checking ...", the check has a time limit, and without internet the page says
    /// what to do instead of waiting for ever. It tries again by itself when the connection comes back.
    ///
    /// The page's controller calls Look(text) every time the text changes and reads the answer; Changed fires when a
    /// check that was running has finished. The line under the field (Status Text) is optional: without it the
    /// controller shows Message in its own message line.
    /// </summary>
    public class EmailFieldVerifier : ThemeAwareBehaviour
    {
        public enum State { Empty, BadShape, LookAlike, ThrowAway, Waiting, Checking, Good, NoMail, CannotCheck }

        [Header("The line under the field (optional; it takes no room while it has nothing to say)")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Checking a less common mail domain online")]
        [Tooltip("Seconds after the last typed letter before the check starts, so half-typed domains are not asked about")]
        [SerializeField] private float waitAfterTyping = 0.8f;
        [Tooltip("The longest one check may take (seconds). After that the page says the address could not be checked.")]
        [SerializeField] private float timeLimitSeconds = 8f;
        [Tooltip("Seconds before a check that got no answer is tried again by itself")]
        [SerializeField] private float tryAgainSeconds = 5f;
        [Tooltip("Mail domains accepted at once on top of the well-known providers, for example a school's own domain (written like school.edu.ph)")]
        [SerializeField] private string[] alsoTrusted = { };

        [Header("Messages ({0} = the part after @, {1} = the address as it was probably meant)")]
        [SerializeField] private string msgShape = "Enter an email address like name@example.com.";
        [SerializeField] private string msgLookAlike = "Did you mean {1}? Check the spelling after @.";
        [SerializeField] private string msgThrowAway = "Temporary email addresses cannot be used. Use your own email.";
        [SerializeField] private string msgChecking = "Checking that {0} receives email...";
        [SerializeField] private string msgGood = "{0} receives email.";
        [SerializeField] private string msgNoMail = "{0} does not receive email. Check the spelling after @.";
        [SerializeField] private string msgCannotCheck = "No internet to check {0}. Connect, or use a Gmail, Outlook, Yahoo or iCloud address.";

        [Header("Colours (filled in from the theme while Use Theme Colors is on)")]
        [SerializeField] private Color goodColor = new Color32(111, 143, 91, 255);
        [SerializeField] private Color busyColor = new Color32(150, 140, 132, 255);
        [SerializeField] private Color problemColor = new Color32(181, 71, 47, 255);

        public State Now { get; private set; } = State.Empty;
        /// <summary>True when the address may be used.</summary>
        public bool IsGood => Now == State.Good;
        /// <summary>True while the answer is still on its way.</summary>
        public bool Busy => Now == State.Waiting || Now == State.Checking;
        /// <summary>What to tell the user about the address ("" when there is nothing to say).</summary>
        public string Message { get; private set; } = "";
        /// <summary>True when this component shows its messages itself, under the field.</summary>
        public bool ShowsOwnMessages => statusText != null;
        /// <summary>A running check has finished (or the connection came back and a new one started).</summary>
        public event Action Changed;

        string looked = "", domain = "", note = "";
        bool noteGood;
        int ticket;
        float actAt;
        bool checkedOnline;

        /// <summary>
        /// The verifier a page uses: the one the scene gives, or (in a scene built before this rule) a new one on `host`.
        /// The host must be an object that is always switched on, because the waiting and the trying-again happen in Update.
        /// </summary>
        public static EmailFieldVerifier On(GameObject host, EmailFieldVerifier given = null)
        {
            if (given != null || host == null) return given;
            var found = host.GetComponent<EmailFieldVerifier>();
            return found != null ? found : host.AddComponent<EmailFieldVerifier>();
        }

        void Awake() { EmailDomainCheck.Prepare(); Paint(); }

        /// <summary>
        /// A line from the page itself, shown under the field in place of this script's own for as long as it is set
        /// ("Email confirmed.", "Waiting for you to confirm this email."). "" = this script's own line again.
        /// </summary>
        public void SetNote(string text, bool good)
        {
            note = text ?? ""; noteGood = good;
            Paint();
        }

        protected override void ApplyPalette(ThemeProfile.Palette p)
        {
            goodColor = p.success; problemColor = p.warning; busyColor = p.secondaryText;
            Paint();
        }

        /// <summary>Judges the text of the field. Call it on every change; the same text twice costs nothing. Returns IsGood.</summary>
        public bool Look(string email)
        {
            email = (email ?? "").Trim();
            if (email == looked) return IsGood;
            looked = email; ticket++; checkedOnline = false;
            domain = EmailDomains.DomainOf(email);
            EmailDomainCheck.Prepare();

            if (email.Length == 0) { Set(State.Empty); return false; }
            switch (EmailDomains.Judge(email, alsoTrusted))
            {
                case EmailDomains.Kind.BadShape: Set(State.BadShape); break;
                case EmailDomains.Kind.WellKnown: Set(State.Good); break;
                case EmailDomains.Kind.ThrowAway: Set(State.ThrowAway); break;
                case EmailDomains.Kind.LookAlike: Set(State.LookAlike); break;
                default:
                    if (EmailDomainCheck.Recall(domain, out EmailDomains.Mail known)) Take(known);
                    else if (!isActiveAndEnabled) StartCheck();          // switched off: Update does not run, so the check starts at once
                    else { actAt = Time.unscaledTime + Mathf.Max(0f, waitAfterTyping); Set(State.Waiting); }
                    break;
            }
            return IsGood;
        }

        void Update()
        {
            if (Now == State.Waiting && Time.unscaledTime >= actAt) StartCheck();
            else if (Now == State.CannotCheck && Time.unscaledTime >= actAt && ConnectivityMonitor.Online) { StartCheck(); Changed?.Invoke(); }
        }

        async void StartCheck()
        {
            int mine = ticket;
            if (!ConnectivityMonitor.Online) { Take(EmailDomains.Mail.NoAnswer); Changed?.Invoke(); return; }
            Set(State.Checking);
            EmailDomains.Mail mail = await EmailDomainCheck.Ask(domain, timeLimitSeconds);
            if (this == null || mine != ticket) return;          // the page was left, or the text changed meanwhile
            Take(mail);
            Changed?.Invoke();
        }

        void Take(EmailDomains.Mail mail)
        {
            switch (mail)
            {
                case EmailDomains.Mail.Receives: checkedOnline = true; Set(State.Good); break;
                case EmailDomains.Mail.ThrowAway: Set(State.ThrowAway); break;
                case EmailDomains.Mail.None:
                case EmailDomains.Mail.NoSuchDomain: Set(State.NoMail); break;
                default: actAt = Time.unscaledTime + Mathf.Max(1f, tryAgainSeconds); Set(State.CannotCheck); break;
            }
        }

        void Set(State state)
        {
            Now = state;
            switch (state)
            {
                case State.BadShape: Message = msgShape; break;
                case State.LookAlike: Message = string.Format(msgLookAlike, domain, EmailDomains.Suggestion(looked)); break;
                case State.ThrowAway: Message = msgThrowAway; break;
                case State.Waiting:
                case State.Checking: Message = string.Format(msgChecking, domain); break;
                case State.NoMail: Message = string.Format(msgNoMail, domain); break;
                case State.CannotCheck: Message = string.Format(msgCannotCheck, domain); break;
                default: Message = ""; break;
            }
            Paint();
        }

        void Paint()
        {
            if (statusText == null) return;
            if (!Application.isPlaying) { statusText.color = busyColor; return; }      // in the Editor only the colour is previewed: the sample wording stays
            // a domain that was checked online gets a short "it works" line; a well-known provider needs no comment
            string line = note.Length > 0 ? note : Now == State.Good && checkedOnline ? string.Format(msgGood, domain) : Message;
            statusText.text = line;
            statusText.color = note.Length > 0 ? (noteGood ? goodColor : busyColor) : Now == State.Good ? goodColor : Busy ? busyColor : problemColor;
            if (statusText.gameObject.activeSelf != (line.Length > 0)) statusText.gameObject.SetActive(line.Length > 0);
        }
    }
}
