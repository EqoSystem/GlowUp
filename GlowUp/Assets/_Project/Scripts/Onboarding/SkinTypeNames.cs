namespace GlowUp.Onboarding
{
    /// <summary>
    /// The official skin types (23 November 2026): Normal, Dry, Oily, Combination, Sensitive. A profile that was saved with the old
    /// word "Mix" is still read as Combination: the stored word stays "Mix" (ingredient rules and saved profiles carry it), only the
    /// words users read change.
    /// </summary>
    public static class SkinTypeNames
    {
        public const string Combination = "Mix";                // the stored value of Combination skin
        public const string Sensitive = "Sensitive";

        /// <summary>The stored values, in the official order.</summary>
        public static readonly string[] Values = { "Normal", "Dry", "Oily", Combination, Sensitive };

        /// <summary>What users read for a stored value ("Mix" reads "Combination").</summary>
        public static string Display(string value)
        {
            string v = (value ?? "").Trim();
            return string.Equals(v, "Mix", System.StringComparison.OrdinalIgnoreCase) || string.Equals(v, "Combination", System.StringComparison.OrdinalIgnoreCase) ? "Combination" : v;
        }

        /// <summary>The stored value for what a user (or an old/new profile) calls a skin type.</summary>
        public static string Stored(string text)
        {
            string v = (text ?? "").Trim();
            return string.Equals(v, "Combination", System.StringComparison.OrdinalIgnoreCase) ? Combination : v;
        }
    }
}
