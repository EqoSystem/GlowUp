namespace GlowUp.AppSettings
{
    /// <summary>
    /// The short tips shown under the message box of Contact support, per kind of report (10 November 2026). Edit them here.
    /// Index = the tab: 0 Bug, 1 Wrong scan result, 2 Suggestion (phase 4, 11 November 2026), 3 Other.
    /// </summary>
    public static class SupportSuggestions
    {
        public static readonly string[][] ByCategory =
        {
            new[] { "Say what you tapped and what happened.", "Say whether it happens every time.", "Restarting the app sometimes helps; tell us if it did." },
            new[] { "Retake the photo in better light, with the front of the pack in the box.", "Search the product by name instead (Type it in).", "Turn on \"Include my last scan\" so we see what was read." },
            new[] { "Say what you would like the app to do, and where.", "One idea per message helps us track it.", "Your suggestion goes to the GlowUp team's Suggestions list." },
            new[] { "Ideas and questions are welcome.", "We read every report; we cannot always answer each one." }
        };

        public static string[] For(int index) => index >= 0 && index < ByCategory.Length ? ByCategory[index] : new string[0];
    }
}
