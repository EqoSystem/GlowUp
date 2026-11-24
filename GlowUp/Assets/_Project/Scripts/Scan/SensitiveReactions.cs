using System;
using System.Collections.Generic;
using System.Linq;

namespace GlowUp.Scan
{
    /// <summary>
    /// "How does your skin react?" (24 November 2026): the optional follow-up to the Sensitive skin type. Sensitive skin is a tendency, not one
    /// kind of skin: it can show as redness, stinging, itching, breakouts or tightness, and different ingredients set each one off.
    /// Each reaction points at things the app already understands (a skin condition, a skin type, an allergen family), so no new ingredient data
    /// is needed: the ingredient flags (the website's avoid-for fields, automatic ones included) do the work. Nothing here is a diagnosis.
    /// The choice is saved on the user's profile (UserProfileData.sensitiveReactions) and read by ProductAnalysisEngine.
    /// The pick screen is added to the Step 2 scene after its layout is redesigned.
    /// </summary>
    public static class SensitiveReactions
    {
        public class Reaction
        {
            public string id, label;
            /// <summary>The skin condition (by its name, matched like a saved condition) whose avoid-list this reaction also follows; or null.</summary>
            public string condition;
            /// <summary>A skin type whose avoid-list this reaction also follows; or null.</summary>
            public string skinType;
            /// <summary>An allergen label (as the allergen screen writes it) this reaction adds to the user's allergens; or null.</summary>
            public string allergen;
        }

        public static readonly Reaction[] All =
        {
            new Reaction { id = "redness", label = "Redness or flushing", condition = "Rosacea" },
            new Reaction { id = "stinging", label = "Stinging or burning", condition = "Irritant Contact Dermatitis" },
            new Reaction { id = "itching", label = "Itching or rash", condition = "Atopic Dermatitis" },
            new Reaction { id = "breakouts", label = "Breakouts", condition = "Acne" },
            new Reaction { id = "tightness", label = "Tightness or dryness", skinType = "Dry" },
            new Reaction { id = "products", label = "Reacts to fragrance or certain products", allergen = "Fragrance / Parfum" },
        };

        public static Reaction Find(string id) => All.FirstOrDefault(r => string.Equals(r.id, (id ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Only known ids, each once, in the order of <see cref="All"/>.</summary>
        public static List<string> Clean(IEnumerable<string> ids)
        {
            var wanted = new HashSet<string>((ids ?? new List<string>()).Select(x => (x ?? "").Trim().ToLowerInvariant()));
            return All.Where(r => wanted.Contains(r.id)).Select(r => r.id).ToList();
        }

        /// <summary>The reactions a profile has saved. Empty unless the skin type is Sensitive (the question belongs to that choice only).</summary>
        public static List<Reaction> Of(GlowUp.Data.UserProfileData profile)
        {
            if (profile == null || !string.Equals((profile.skinType ?? "").Trim(), "Sensitive", StringComparison.OrdinalIgnoreCase)) return new List<Reaction>();
            return Clean(profile.sensitiveReactions).Select(Find).Where(r => r != null).ToList();
        }
    }
}
