using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// Turns a list of ingredient names into a result for ONE user: each ingredient is looked up
    /// (website database first, then the built-in knowledge) and cross-checked against the user's
    /// allergens, skin type, skin conditions and age. Works fully offline.
    /// The wording of every sentence it produces is in this file, so it can be reworded here.
    /// </summary>
    public static class ProductAnalysisEngine
    {
        /// <summary>
        /// A result is called incomplete, and never "nothing flagged", when fewer than this share of the names on the label
        /// could be checked (0.5 = at least half). Set in the Scan Rules asset.
        /// </summary>
        public static float MinimumCheckedShare = 0.5f;

        /// <param name="unrecognized">names on the label that look like ingredients but match nothing the app knows: they are
        /// not checked and not part of the verdict, but the result says how many there were</param>
        public static ScanRecord Analyse(List<string> ingredientNames, string rawLabelText, UserProfileData profile,
            List<BrandData> brands = null, string productName = null, string brandName = null, string cosmeticType = null,
            List<string> unrecognized = null)
        {
            profile = profile ?? new UserProfileData();
            var record = new ScanRecord
            {
                productName = productName ?? "", brandName = brandName ?? "", source = "scan",
                scannedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string skinType = (profile.skinType ?? "").Trim();
            var conditions = (profile.skinConditions ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            var allergens = profile.RealAllergens();          // "No known allergens" is an answer, not something to look for

            var typeConflicts = new List<string>(); var typePositives = new List<string>();
            var allergenHits = new List<string>(); var allergenLabels = new List<string>();
            var conditionConflicts = new List<string>(); var conditionNames = new List<string>();
            var conditionPositives = new List<string>(); var conditionPositiveNames = new List<string>();
            var ageFlags = new List<string>(); int ageLimit = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in ingredientNames ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                IngredientData data = IngredientTextParser.MatchEntry(raw).data;          // "Water (Aqua)" and "Aqua/Water/Eau" are Water
                string shown = data != null && !string.IsNullOrEmpty(data.name) ? data.name : raw.Trim();
                // "Aqua" and "Water" are the same ingredient (one database entry), and so are two READINGS of one name:
                // "Dimethicone" and "Dirnethicone", "CI 77491" and "Ci77491" (the camera cannot be trusted on rn/m, 0/o, 1/l, spaces)
                string sameAs = data != null && !string.IsNullOrEmpty(data.id) ? "id:" + data.id : "read:" + TextKey.Squash(TextKey.Normalize(shown));
                if (!seen.Add(sameAs) | !seen.Add("name:" + TextKey.Normalize(shown))) continue;

                var f = new IngredientFinding { name = shown };
                record.ingredients.Add(shown);
                if (data == null)
                {
                    f.level = "unknown";
                    f.description = "Not in our ingredient database yet, so it is not part of this result.";
                    record.findings.Add(f);
                    continue;
                }

                f.techName = data.techName ?? ""; f.description = data.description ?? ""; f.commonUses = data.commonUses ?? "";
                f.keyActive = data.keyActive || ListsRepository.HasActiveHighlight(data.highlights); f.hidden = data.umbrella;
                bool aiOnly = data.IsUnreviewedAi();               // found by the AI lookup, not yet checked by an admin
                f.aiSourced = aiOnly;
                if (aiOnly) f.description = (f.description.TrimEnd('.') + ". (Found by the AI lookup; not yet reviewed.)").TrimStart('.', ' ');
                string level = string.IsNullOrEmpty(data.riskLevel) ? "safe" : data.riskLevel.ToLowerInvariant();
                string note = (data.cautionNote ?? "").Trim().TrimEnd('.');
                var reasons = new List<string>();

                // 1) the user's allergens
                string allergen = MatchAllergen(data, shown, allergens);
                if (allergen != null)
                {
                    level = "harmful";
                    reasons.Add($"Matches an allergen in your profile ({allergen}).");
                    allergenHits.Add(shown); if (!allergenLabels.Contains(allergen)) allergenLabels.Add(allergen);
                }

                // 2) skin type
                if (skinType.Length > 0 && Has(data.avoidForSkinTypes, skinType))
                {
                    level = Worse(level, "caution");
                    reasons.Add($"May not suit {skinType} skin" + (note.Length > 0 ? $": it {note}." : "."));
                    typeConflicts.Add(shown);
                }
                else if (skinType.Length > 0 && Has(data.goodForSkinTypes, skinType)) typePositives.Add(shown);

                // 3) saved skin conditions
                foreach (string condition in conditions)
                {
                    if (MatchesCondition(data.avoidForConditions, condition))
                    {
                        level = Worse(level, "caution");
                        reasons.Add($"May aggravate {ShortCondition(condition)}" + (note.Length > 0 ? $": it {note}." : "."));
                        conditionConflicts.Add(shown); if (!conditionNames.Contains(ShortCondition(condition))) conditionNames.Add(ShortCondition(condition));
                    }
                    else if (MatchesCondition(data.goodForConditions, condition))
                    {
                        conditionPositives.Add(shown); if (!conditionPositiveNames.Contains(ShortCondition(condition))) conditionPositiveNames.Add(ShortCondition(condition));
                    }
                }

                // 4) age
                if (data.minAge > 0 && profile.age > 0 && profile.age < data.minAge)
                {
                    level = Worse(level, "caution");
                    reasons.Add($"Usually not recommended under the age of {data.minAge}.");
                    ageFlags.Add(shown); ageLimit = Math.Max(ageLimit, data.minAge);
                }

                // 5) flagged for everybody
                if (reasons.Count == 0 && level != "safe" && note.Length > 0) reasons.Add("Worth knowing: it " + note + ".");
                if (data.umbrella)
                {
                    // what an earlier AI lookup (on this phone or any other) found behind this word; kept on the phone, so this works offline
                    string known = HiddenAdditives.Describe(HiddenAdditives.Find(shown, data.techName, data.name), shown);
                    record.hiddenAlerts.Add(known ?? $"\"{shown}\" is an umbrella term: the label does not have to say what is in it.");
                }

                if (aiOnly && level == "safe") level = "unknown";   // unreviewed AI information may warn, but never clears an ingredient
                if (data.generic && level == "safe")                 // only its family is known: said, but not counted as checked
                {
                    level = "unknown";
                    f.description = (f.description.TrimEnd('.') + ". This exact one is not in our ingredient database yet, so it is not part of this result.").TrimStart('.', ' ');
                }
                f.level = level;
                f.reason = string.Join(" ", reasons.Distinct());
                if (level == "harmful") record.harmfulIngredients.Add(shown);
                if (data.keyActive) record.keyActives.Add(shown);
                record.findings.Add(f);
            }

            // names the reader could not match with anything the app knows: kept apart, never part of the verdict
            foreach (string name in unrecognized ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name.Trim())) record.unrecognized.Add(name.Trim());

            // ---------------- verdict ----------------
            // It rests ONLY on the ingredients that could be checked. What could not be checked (an ingredient the app only
            // knows the family of, an entry of the AI lookup nobody reviewed, a name it does not know at all) never makes
            // a product "fine": the result says how many there were, and with too many of them it is called incomplete.
            int total = record.findings.Count;
            int unknown = record.findings.Count(x => x.level == "unknown");
            int harmful = record.findings.Count(x => x.level == "harmful");
            int caution = record.findings.Count(x => x.level == "caution");
            int names = total + record.unrecognized.Count;                  // every name on the label
            int notChecked = unknown + record.unrecognized.Count;
            int checkedNames = names - notChecked;
            bool incomplete = false;

            if (checkedNames == 0)
            {
                record.status = "caution";
                record.summary = "We could not identify these ingredients, so we cannot judge this product yet.";
                incomplete = true;
            }
            else if (harmful > 0)
            {
                record.status = "harmful";
                record.summary = "Flagged for your profile: " + Join(record.harmfulIngredients, 3) + ".";      // the status pill says the verdict: this line gives the reason
            }
            else if (caution > 0)
            {
                record.status = "caution";
                record.summary = caution == 1 ? "1 ingredient to watch." : $"{caution} ingredients to watch.";
            }
            else if (checkedNames < names * MinimumCheckedShare)
            {
                record.status = "caution";
                record.summary = $"Only {checkedNames} of the {names} names on this label could be checked, so this result is incomplete.";
                incomplete = true;
            }
            else
            {
                record.status = "safe";
                record.summary = "No ingredients were flagged for your profile.";
            }
            if (!incomplete && notChecked > 0)
                record.summary += notChecked == 1 ? " 1 name on the label could not be checked." : $" {notChecked} names on the label could not be checked.";

            // ---------------- compatibility matrix ----------------
            if (skinType.Length == 0) record.skinTypeMatch = Item("unknown", "No skin type saved in your profile.");
            else if (typeConflicts.Count > 0) record.skinTypeMatch = Item("caution", Count(typeConflicts.Count, "ingredient") + $" may not suit {skinType} skin ({Join(typeConflicts, 3)}).");
            else if (typePositives.Count > 0) record.skinTypeMatch = Item("good", $"Suits {skinType} skin: contains {Join(typePositives, 3)}.");
            else record.skinTypeMatch = Item("good", $"No conflicts with {skinType} skin.");

            if (profile.SaysNoKnownAllergens()) record.allergenMatch = Item("good", "You have no known allergens saved, so there was nothing to match.");
            else if (allergens.Count == 0) record.allergenMatch = Item("unknown", "No allergens saved in your profile.");
            else if (allergenHits.Count > 0) record.allergenMatch = Item("bad", $"Contains your allergen{(allergenLabels.Count == 1 ? "" : "s")} {Join(allergenLabels, 3)} ({Join(allergenHits, 3)}).");
            else record.allergenMatch = Item("good", "None of your saved allergens were found.");

            if (conditions.Count == 0) record.concernMatch = Item("unknown", "No skin concerns saved in your profile.");
            else if (conditionConflicts.Count > 0) record.concernMatch = Item("caution", $"May aggravate {Join(conditionNames, 3)} ({Join(conditionConflicts.Distinct().ToList(), 3)}).");
            else if (conditionPositives.Count > 0) record.concernMatch = Item("good", $"Contains ingredients often used for {Join(conditionPositiveNames, 3)} ({Join(conditionPositives.Distinct().ToList(), 3)}).");
            else record.concernMatch = Item("good", "No conflicts with your saved skin concerns.");

            if (profile.age <= 0) record.ageMatch = Item("unknown", "No age saved in your profile.");
            else if (ageFlags.Count > 0) record.ageMatch = Item("caution", $"Contains ingredients usually not advised under {ageLimit} ({Join(ageFlags, 3)}).");
            else record.ageMatch = Item("good", $"Nothing age-restricted for a {profile.age}-year-old.");

            // ---------------- the rest ----------------
            record.cosmeticType = !string.IsNullOrWhiteSpace(cosmeticType) ? cosmeticType : GuessCosmeticType(rawLabelText, record.ingredients);
            MatchBrand(record, rawLabelText, brands);
            record.dermatologistAdvised = harmful > 0 || caution >= 2 || (conditions.Count > 0 && record.status != "safe");
            record.recommendedShades = ShadeAdvisor.Recommend(profile.skinTone, ShadeAdvisor.AreaFor(record.cosmeticType));
            record.explanation = BuildExplanation(record, names, notChecked, typePositives, conditionPositives, conditionPositiveNames, skinType);
            return record;
        }

        // ---------------------------------------------------------------------------------------
        //  "Why this result?"
        // ---------------------------------------------------------------------------------------
        static string BuildExplanation(ScanRecord r, int names, int notChecked, List<string> typePositives,
            List<string> conditionPositives, List<string> conditionPositiveNames, string skinType)
        {
            var sb = new StringBuilder();
            sb.Append(r.summary).Append(' ');
            sb.Append($"We read {Count(names, "name")} on the label and could check {names - notChecked} of them.");

            var bad = r.findings.Where(f => f.level == "harmful").ToList();
            if (bad.Count > 0)
            {
                sb.Append("\n\n<b>What conflicts with your profile</b>");
                foreach (var f in bad) sb.Append("\n- ").Append(f.name).Append(": ").Append(f.reason);
            }
            var watch = r.findings.Where(f => f.level == "caution").ToList();
            if (watch.Count > 0)
            {
                sb.Append("\n\n<b>Worth watching</b>");
                foreach (var f in watch) sb.Append("\n- ").Append(f.name).Append(": ").Append(f.reason);
            }
            if (typePositives.Count > 0 || conditionPositives.Count > 0)
            {
                sb.Append("\n\n<b>What works in your favour</b>");
                if (typePositives.Count > 0) sb.Append($"\n- {Join(typePositives, 4)} suit{(typePositives.Count == 1 ? "s" : "")} {skinType} skin.");
                if (conditionPositives.Count > 0) sb.Append($"\n- {Join(conditionPositives.Distinct().ToList(), 4)} {(conditionPositives.Distinct().Count() == 1 ? "is" : "are")} often used for {Join(conditionPositiveNames, 3)}.");
            }
            if (r.hiddenAlerts.Count > 0)
            {
                sb.Append("\n\n<b>Hidden ingredients</b>");
                foreach (string a in r.hiddenAlerts.Distinct()) sb.Append("\n- ").Append(a);
            }
            if (notChecked > 0)
            {
                // ingredients the app only half knows, and names it does not know at all: neither is part of the result
                var partly = r.findings.Where(f => f.level == "unknown").Select(f => f.name).ToList();
                sb.Append("\n\n<b>Not checked</b>");
                if (partly.Count > 0) sb.Append($"\n- {Count(partly.Count, "ingredient")} not verified yet (only the kind of ingredient is known, or it was only described by the AI lookup so far): {Join(partly, 6)}.");
                if (r.unrecognized.Count > 0) sb.Append($"\n- {Count(r.unrecognized.Count, "name")} on the label not recognised, so not counted as ingredients: {Join(r.unrecognized, 6)}.");
            }
            sb.Append("\n\nThis is guidance, not a medical opinion. Patch-test any new product first.");
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------
        //  Matching helpers
        // ---------------------------------------------------------------------------------------
        // The allergen families come from the admin website's list (ListsRepository; the same seven built in): each is { id, word, word, ... }
        // and a user's saved allergen that contains one of the words belongs to it.

        /// <summary>The user's allergen label that this ingredient matches, or null.</summary>
        static string MatchAllergen(IngredientData data, string shownName, List<string> userAllergens)
        {
            string name = (shownName + " " + (data.techName ?? "")).ToLowerInvariant();
            string[][] families = ListsRepository.AllergenFamilies();
            foreach (string label in userAllergens)
            {
                string l = label.ToLowerInvariant();
                foreach (var family in families)
                {
                    bool userHasFamily = false;
                    for (int i = 1; i < family.Length; i++) if (l.Contains(family[i])) { userHasFamily = true; break; }
                    if (userHasFamily && data.allergenTags != null && data.allergenTags.Any(t => string.Equals(t, family[0], StringComparison.OrdinalIgnoreCase))) return label;
                }
                // an allergen the user typed in themselves: match it against the ingredient's name
                foreach (string part in l.Split(new[] { '/', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    if (p.Length >= 4 && name.Contains(p)) return label;
                }
            }
            return null;
        }

        static readonly string[] ConditionKeys = { "acne", "eczema", "psoriasis", "rosacea", "seborrheic", "melasma", "contact" };

        static bool MatchesCondition(List<string> ingredientConditions, string userCondition)
        {
            if (ingredientConditions == null || ingredientConditions.Count == 0) return false;
            string u = userCondition.ToLowerInvariant();
            if (u.Contains("hyperpigmentation")) u += " melasma";
            if (u.Contains("atopic")) u += " eczema";
            foreach (string c in ingredientConditions)
            {
                string k = c.ToLowerInvariant().Trim();
                if (k.Length == 0) continue;
                // the condition's id and the words the admin website lists for it (a condition added there works the same way)
                foreach (string word in ListsRepository.ConditionWords(k)) if (u.Contains(word)) return true;
            }
            return false;
        }

        /// <summary>"Eczema (Atopic Dermatitis)" -> "Eczema".</summary>
        static string ShortCondition(string c)
        {
            int cut = c.IndexOfAny(new[] { '(', '/' });
            return (cut > 0 ? c.Substring(0, cut) : c).Trim();
        }

        static bool Has(List<string> list, string value) =>
            list != null && list.Any(x => string.Equals(x.Trim(), value, StringComparison.OrdinalIgnoreCase));

        static string Worse(string a, string b)
        {
            int Rank(string s) => s == "harmful" ? 3 : s == "caution" ? 2 : s == "safe" ? 1 : 0;
            return Rank(a) >= Rank(b) ? a : b;
        }

        static CompatibilityItem Item(string level, string summary) => new CompatibilityItem { level = level, summary = summary };
        static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

        static string Join(List<string> items, int max)
        {
            if (items == null || items.Count == 0) return "";
            var shown = items.Take(max).ToList();
            string text = string.Join(", ", shown);
            return items.Count > max ? text + $" and {items.Count - max} more" : text;
        }

        // ---------------------------------------------------------------------------------------
        //  Product type and brand
        // ---------------------------------------------------------------------------------------
        // words on a label that name a kind of make-up
        static readonly (string keyword, string type)[] TypeKeywords =
        {
            ("lip gloss", "Lip Gloss"), ("lipgloss", "Lip Gloss"), ("lip tint", "Lip Tint"), ("lip liner", "Lip Liner"),
            ("lipstick", "Lipstick"), ("lip stick", "Lipstick"), ("lip colour", "Lipstick"), ("lip color", "Lipstick"),
            ("foundation", "Foundation"), ("concealer", "Concealer"), ("blush", "Blush"), ("bronzer", "Bronzer"), ("highlighter", "Highlighter"),
            ("eyeshadow", "Eyeshadow"), ("eye shadow", "Eyeshadow"), ("mascara", "Mascara"), ("eyeliner", "Eyeliner"), ("primer", "Primer")
        };

        /// <summary>
        /// A GUESS at the kind of make-up, from the words of the label and from what the ingredients look like. It only
        /// ever names a kind of make-up, and gives "" when it cannot tell: a guess is never a reason to call a product
        /// "not make-up" (that takes the product's name, the AI, the inventory or the user: see ProductScope).
        /// </summary>
        public static string GuessCosmeticType(string rawLabelText, List<string> ingredients)
        {
            string text = (rawLabelText ?? "").ToLowerInvariant();
            foreach (var (keyword, type) in TypeKeywords) if (text.Contains(keyword)) return type;

            string all = string.Join(" | ", ingredients ?? new List<string>()).ToLowerInvariant();
            bool pigment = all.Contains("iron oxide") || all.Contains("mica") || all.Contains("carmine") || all.Contains("ci 7") || all.Contains("ci 1");
            bool wax = all.Contains("wax") || all.Contains("castor oil") || all.Contains("polybutene");
            bool powder = all.Contains("talc") || all.Contains("silica") || all.Contains("kaolin");
            if (pigment && wax) return "Lipstick";
            if (pigment && powder) return "Powder";
            return "";
        }

        static void MatchBrand(ScanRecord record, string rawLabelText, List<BrandData> brands)
        {
            if (brands == null || string.IsNullOrWhiteSpace(rawLabelText)) return;
            string text = " " + rawLabelText.ToLowerInvariant() + " ";
            foreach (var b in brands.OrderByDescending(x => (x.name ?? "").Length))
            {
                string n = (b.name ?? "").Trim().ToLowerInvariant();
                if (n.Length < 3 || !text.Contains(n)) continue;
                record.brandId = b.id;
                if (string.IsNullOrWhiteSpace(record.brandName)) record.brandName = b.name;
                return;
            }
        }
    }
}
