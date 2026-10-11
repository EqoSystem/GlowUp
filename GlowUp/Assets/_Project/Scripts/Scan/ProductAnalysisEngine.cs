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
        public static ScanRecord Analyse(List<string> ingredientNames, string rawLabelText, UserProfileData profile,
            List<BrandData> brands = null, string productName = null, string brandName = null, string cosmeticType = null)
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
                IngredientData data = IngredientKnowledge.Resolve(raw);
                string shown = data != null && !string.IsNullOrEmpty(data.name) ? data.name : raw.Trim();
                if (!seen.Add(shown)) continue;                       // "Aqua" and "Water" are the same ingredient

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
                f.keyActive = data.keyActive; f.hidden = data.umbrella;
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
                f.level = level;
                f.reason = string.Join(" ", reasons.Distinct());
                if (level == "harmful") record.harmfulIngredients.Add(shown);
                if (data.keyActive) record.keyActives.Add(shown);
                record.findings.Add(f);
            }

            // ---------------- verdict ----------------
            int total = record.findings.Count;
            int unknown = record.findings.Count(x => x.level == "unknown");
            int harmful = record.findings.Count(x => x.level == "harmful");
            int caution = record.findings.Count(x => x.level == "caution");

            if (total == 0 || unknown == total)
            {
                record.status = "caution";
                record.summary = "We could not identify these ingredients, so we cannot judge this product yet.";
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
            else
            {
                record.status = "safe";
                record.summary = "No ingredients were flagged for your profile.";
            }

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
            record.explanation = BuildExplanation(record, total, unknown, typePositives, conditionPositives, conditionPositiveNames, skinType);
            return record;
        }

        // ---------------------------------------------------------------------------------------
        //  "Why this result?"
        // ---------------------------------------------------------------------------------------
        static string BuildExplanation(ScanRecord r, int total, int unknown, List<string> typePositives,
            List<string> conditionPositives, List<string> conditionPositiveNames, string skinType)
        {
            var sb = new StringBuilder();
            sb.Append(r.summary).Append(' ');
            sb.Append($"We read {Count(total, "ingredient")} and recognised {total - unknown} of them.");

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
            if (unknown > 0)
            {
                var names = r.findings.Where(f => f.level == "unknown").Select(f => f.name).ToList();
                sb.Append($"\n\n<b>Not checked</b>\n- {Count(unknown, "ingredient")} not verified yet (new to our database, or only described by the AI lookup so far): {Join(names, 5)}.");
            }
            sb.Append("\n\nThis is guidance, not a medical opinion. Patch-test any new product first.");
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------
        //  Matching helpers
        // ---------------------------------------------------------------------------------------
        static readonly string[][] AllergenFamilies =
        {
            new[] { "fragrance", "fragrance", "parfum", "perfume" },
            new[] { "paraben", "paraben" },
            new[] { "sulfate", "sulfate", "sulphate", "sls", "sles" },
            new[] { "formaldehyde", "formaldehyde" },
            new[] { "lanolin", "lanolin" },
            new[] { "essentialoil", "essential oil", "essential oils" },
            new[] { "nut", "nut" }
        };

        /// <summary>The user's allergen label that this ingredient matches, or null.</summary>
        static string MatchAllergen(IngredientData data, string shownName, List<string> userAllergens)
        {
            string name = (shownName + " " + (data.techName ?? "")).ToLowerInvariant();
            foreach (string label in userAllergens)
            {
                string l = label.ToLowerInvariant();
                foreach (var family in AllergenFamilies)
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
                if (k.Length > 0 && u.Contains(k)) return true;
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
        static readonly (string keyword, string type)[] TypeKeywords =
        {
            ("lip gloss", "Lip Gloss"), ("lipgloss", "Lip Gloss"), ("lip balm", "Lip Balm"), ("lipstick", "Lipstick"), ("lip stick", "Lipstick"),
            ("lip colour", "Lipstick"), ("lip color", "Lipstick"), ("lip tint", "Lipstick"),
            ("foundation", "Foundation"), ("concealer", "Concealer"), ("blush", "Blush"), ("bronzer", "Bronzer"), ("highlighter", "Highlighter"),
            ("eyeshadow", "Eyeshadow"), ("eye shadow", "Eyeshadow"), ("mascara", "Mascara"), ("eyeliner", "Eyeliner"),
            ("sunscreen", "Sunscreen"), ("sun screen", "Sunscreen"), ("spf", "Sunscreen"),
            ("cleanser", "Cleanser"), ("face wash", "Cleanser"), ("cleansing", "Cleanser"), ("shampoo", "Shampoo"), ("conditioner", "Conditioner"),
            ("toner", "Toner"), ("serum", "Serum"), ("moisturizer", "Moisturizer"), ("moisturiser", "Moisturizer"), ("lotion", "Moisturizer"),
            ("cream", "Moisturizer"), ("mask", "Mask"), ("powder", "Powder")
        };

        public static string GuessCosmeticType(string rawLabelText, List<string> ingredients)
        {
            string text = (rawLabelText ?? "").ToLowerInvariant();
            foreach (var (keyword, type) in TypeKeywords) if (text.Contains(keyword)) return type;

            string all = string.Join(" | ", ingredients ?? new List<string>()).ToLowerInvariant();
            bool pigment = all.Contains("iron oxide") || all.Contains("mica") || all.Contains("carmine") || all.Contains("ci 7") || all.Contains("ci 1");
            bool wax = all.Contains("wax") || all.Contains("castor oil") || all.Contains("polybutene");
            bool powder = all.Contains("talc") || all.Contains("silica") || all.Contains("kaolin");
            bool wash = all.Contains("sulfate") || all.Contains("betaine") || all.Contains("isethionate") || all.Contains("glucoside");
            bool sun = all.Contains("zinc oxide") || all.Contains("avobenzone") || all.Contains("octinoxate") || all.Contains("octocrylene") || all.Contains("homosalate") || all.Contains("oxybenzone");
            if (pigment && wax) return "Lipstick";
            if (pigment && powder) return "Powder";
            if (pigment) return "Foundation";
            if (sun) return "Sunscreen";
            if (wash) return "Cleanser";
            return "Skincare";
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
