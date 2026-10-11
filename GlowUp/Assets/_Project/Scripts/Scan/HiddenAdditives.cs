using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// Extra additives and hidden ingredients: what a word on a label can stand for without saying so
    /// ("Fragrance" can be dozens of substances). The AI finds these online, with a source; this class
    /// keeps each finding
    ///   - on the phone (so the next scan that meets the same word shows it, also OFFLINE), and
    ///   - in the shared database as an ingredient entry "ai_hidden_...", marked as found by the AI and
    ///     not reviewed. It shows on the admin website's Ingredient Curator like every AI entry, where
    ///     an admin can approve, correct or delete it. Other phones receive it with the ingredient list.
    ///
    /// The entry is stored in the ordinary ingredient fields: its label name starts with "hidden:" and
    /// its Common Uses field holds the substances, separated by "; ".
    /// </summary>
    public static class HiddenAdditives
    {
        public const string IdPrefix = "ai_hidden_";
        public const string LabelPrefix = "hidden:";

        /// <summary>True for an ingredient entry that is really a hidden-additive finding (it is not an ingredient to match on labels).</summary>
        public static bool IsFinding(IngredientData data) => data != null && data.techName != null && data.techName.StartsWith(LabelPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>What is known to hide behind this label word (tried under each of the given names), or null.</summary>
        public static IngredientData Find(params string[] labelNames)
        {
            foreach (string name in labelNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                IngredientData found = IngredientRepository.Find(LabelPrefix + IngredientKnowledge.Key(name));
                if (IsFinding(found)) return found;
            }
            return null;
        }

        /// <summary>The substances of a finding.</summary>
        public static List<string> Substances(IngredientData finding)
        {
            var list = new List<string>();
            if (finding == null || string.IsNullOrWhiteSpace(finding.commonUses)) return list;
            foreach (string part in finding.commonUses.Split(';')) if (part.Trim().Length > 0) list.Add(part.Trim());
            return list;
        }

        /// <summary>The sentence shown to the user for a label word with a finding.</summary>
        public static string Describe(IngredientData finding, string shownName)
        {
            List<string> substances = Substances(finding);
            if (substances.Count == 0) return null;
            string text = $"\"{shownName}\" can also contain: {string.Join(", ", substances)}.";
            if (finding.IsUnreviewedAi()) text += " (Found by the AI lookup; not yet reviewed.)";
            return text;
        }

        /// <summary>
        /// Keeps a finding. `term` = the word on the label. Nothing is kept when the same word already has a finding
        /// (the first one stays until an admin changes it on the website).
        /// </summary>
        public static bool Learn(string term, IEnumerable<string> substances, string note, IEnumerable<string> sources, bool webSearched, string model)
        {
            if (string.IsNullOrWhiteSpace(term) || substances == null) return false;
            // Only a word that really is an umbrella term the app knows ("Fragrance", "Parfum", "Aroma") gets a note. It is
            // kept under the ingredient's own name, so "Parfum" and "Perfume" share one note and "may contain" gets none.
            IngredientData umbrella = IngredientKnowledge.Resolve(term);
            if (umbrella == null || !umbrella.umbrella || string.IsNullOrWhiteSpace(umbrella.name)) return false;
            if (Find(term, umbrella.name, umbrella.techName) != null) return false;
            term = umbrella.name;
            string key = IngredientKnowledge.Key(term);
            if (key.Length < 2) return false;

            var names = new List<string>();
            foreach (string s in substances)
            {
                string clean = (s ?? "").Replace(";", ",").Trim();
                if (clean.Length >= 2 && clean.Length <= 60 && !names.Contains(clean) && names.Count < 12) names.Add(clean);
            }
            if (names.Count == 0) return false;

            string shownTerm = term.Trim();
            string description = $"Products that list \"{shownTerm}\" can also contain: {string.Join(", ", names)}.";
            if (!string.IsNullOrWhiteSpace(note)) description += " " + note.Trim();
            if (description.Length > 690) description = description.Substring(0, 690);
            string name = $"Hidden in \"{shownTerm}\"";
            if (name.Length > 80) name = name.Substring(0, 80);

            var data = new IngredientData
            {
                id = IdPrefix + key.Replace(' ', '_'), name = name, techName = LabelPrefix + key, description = description,
                riskLevel = "caution", cautionNote = "can stand for ingredients the label does not name", commonUses = string.Join("; ", names),
                umbrella = true, source = "ai", reviewed = false
            };
            if (sources != null) foreach (string s in sources) if (!string.IsNullOrWhiteSpace(s) && data.sources.Count < 8) data.sources.Add(s.Trim());

            IngredientRepository.AddLocal(data);                    // on the phone: available offline from now on
            string uid = UserProfileRepository.CurrentUid;          // not signed in to the cloud: it stays on this phone only
            if (!string.IsNullOrEmpty(uid))
                SyncQueue.EnqueueSet("ingredients/" + data.id, AiIngredientRecord.From(data, uid, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), webSearched, model));
            return true;
        }
    }
}
