using System.Collections.Generic;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// What the back of each skin type card says when it is chosen: how to tell that type by touch and by look (10 November 2026).
    /// General guidance, not medical advice. Edit the words here; keep each to two short lines so it fits the card.
    /// </summary>
    public static class SkinTypeHints
    {
        public static readonly Dictionary<string, string> Back = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Oily",   "Feels: greasy by midday.\nLooks: shiny, visible pores." },
            { "Normal", "Feels: comfortable, balanced.\nLooks: even, rarely shiny." },
            { "Dry",    "Feels: tight after washing.\nLooks: rough, dull or flaky." },
            { "Mix",    "Feels: oily T-zone, drier cheeks.\nLooks: shiny nose and forehead." }
        };

        public static string For(string value) => value != null && Back.TryGetValue(value, out string words) ? words : "";
    }
}
