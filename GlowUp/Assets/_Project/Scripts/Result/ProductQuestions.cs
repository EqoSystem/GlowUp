using System.Collections.Generic;
using System.Linq;
using GlowUp.Data;

namespace GlowUp.Result
{
    /// <summary>
    /// "Ask about this product" on the result screen: the questions users ask most, each answered from what the scan already
    /// found for THIS user (the analysis, the ingredient findings, the FDA Philippines check, the list's origin).
    /// Nothing leaves the phone and no AI is asked: the skin profile stays private (RA 10173), it works offline, and the same
    /// product always gets the same answer. A question with nothing to say is left out. Not medical advice.
    /// Pure logic, tested in Editor/Tests/ProductQuestionsTests.cs.
    /// </summary>
    public static class ProductQuestions
    {
        public struct Item { public string question, answer; public Item(string q, string a) { question = q; answer = a; } }

        public const string Note = "Answers come from GlowUp's own ingredient data and your saved skin profile, on this phone. They are information, not medical advice: a dermatologist can tell you what is right for your skin.";

        public static List<Item> For(ScanRecord r)
        {
            var list = new List<Item>();
            if (r == null) return list;

            list.Add(new Item("Is it safe for my skin?", Verdict(r)));

            var watch = (r.findings ?? new List<IngredientFinding>()).Where(f => f != null && (f.level == "harmful" || f.level == "caution")).ToList();
            if (watch.Count > 0)
            {
                string lines = string.Join("\n", watch.Take(8).Select(f =>
                    "• <b>" + f.name + "</b> (" + (f.level == "harmful" ? "flagged" : "use with care") + ")" + (string.IsNullOrWhiteSpace(f.reason) ? "" : ": " + f.reason.Trim())));
                if (watch.Count > 8) lines += $"\n...and {watch.Count - 8} more in \"See all ingredients\".";
                list.Add(new Item("Which ingredients should I watch?", lines));
            }
            else if ((r.findings ?? new List<IngredientFinding>()).Count > 0)
                list.Add(new Item("Which ingredients should I watch?", "None of the checked ingredients is flagged for your profile."));

            AddMatch(list, "Does it contain my allergens?", r.allergenMatch);
            AddMatch(list, "Could it set off my skin reactions?", r.concernMatch);
            AddMatch(list, "Does it suit my skin type?", r.skinTypeMatch);
            AddMatch(list, "Is it right for my age?", r.ageMatch);

            var actives = (r.findings ?? new List<IngredientFinding>()).Where(f => f != null && f.keyActive && !string.IsNullOrWhiteSpace(f.description)).Take(4).ToList();
            if (actives.Count > 0)
                list.Add(new Item("What do its key ingredients do?", string.Join("\n", actives.Select(f => "• <b>" + f.name + "</b>: " + FirstSentence(f.description)))));

            // the panel's own wording (22 Nov 2026): what it does, how it affects the skin condition, how it triggers an allergy
            list.Insert(1, new Item("What does it do?", WhatItDoes(r)));
            list.Add(new Item("How does this product affect my skin?", Affect(r.concernMatch, r.skinTypeMatch, r.findings, "condition")));
            list.Add(new Item("How could this product trigger my allergy or skin reaction?", Trigger(r)));

            list.Add(new Item("Is it registered with the FDA Philippines?", Fda(r)));

            string from = ListOrigin(r);
            if (from.Length > 0) list.Add(new Item("Where does this ingredient list come from?", from));

            if (r.recommendedShades != null && r.recommendedShades.Count > 0)
                list.Add(new Item("Which shade suits my skin tone?", "For the skin tone in your profile, GlowUp suggests: " + string.Join(", ", r.recommendedShades.Take(4))
                    + ". Shades on a screen are approximate; try it on with AR Try-On, or test on your jaw line in daylight."));

            int unknown = (r.findings ?? new List<IngredientFinding>()).Count(f => f != null && f.level == "unknown");
            if (unknown > 0)
                list.Add(new Item("Why do some ingredients say \"Not checked\"?", $"{unknown} name{(unknown == 1 ? " has" : "s have")} no reviewed entry in GlowUp yet. They count neither for nor against this result. The GlowUp team adds them over time."));

            list.Add(new Item("Should I see a dermatologist?", r.dermatologistAdvised
                ? "Yes, it is a good idea before you use it: something in it is flagged for your profile. Bring the product or a photo of its ingredient list."
                : "Nothing here calls for it. See one anyway if you get redness, itching, burning or swelling after using it, and stop using the product."));
            return list;
        }

        static string WhatItDoes(ScanRecord r)
        {
            string kind = string.IsNullOrWhiteSpace(r.cosmeticType) ? "make-up product" : r.cosmeticType.ToLowerInvariant();
            var actives = (r.findings ?? new List<IngredientFinding>()).Where(f => f != null && f.keyActive).Select(f => f.name).Take(4).ToList();
            string text = $"It is a {kind}. ";
            text += actives.Count > 0 ? "Its key ingredients are " + string.Join(", ", actives) + ": tap \"What do its key ingredients do?\" for what each one does." : "No key active ingredient is marked in GlowUp's data for this product, so it is mainly a colour and texture product.";
            return text;
        }

        static string Affect(CompatibilityItem concern, CompatibilityItem skinType, List<IngredientFinding> findings, string what)
        {
            var parts = new List<string>();
            if (concern != null && !string.IsNullOrWhiteSpace(concern.summary)) parts.Add(concern.summary.Trim());
            if (skinType != null && !string.IsNullOrWhiteSpace(skinType.summary)) parts.Add(skinType.summary.Trim());
            if (parts.Count == 0) return "Your skin profile has no skin type saved, so GlowUp has nothing to compare. Add it in Settings, then scan again.";
            return string.Join(((char)10).ToString() + (char)10, parts);
        }

        static string Trigger(ScanRecord r)
        {
            var lines = new List<string>();
            if (r.allergenMatch != null && r.allergenMatch.level == "bad" && !string.IsNullOrWhiteSpace(r.allergenMatch.summary)) lines.Add("<b>Allergy:</b> " + r.allergenMatch.summary.Trim());
            else if (r.allergenMatch != null && r.allergenMatch.level == "good") lines.Add("<b>Allergy:</b> none of your saved allergens was found in the list.");
            if (r.concernMatch != null && (r.concernMatch.level == "caution" || r.concernMatch.level == "bad") && !string.IsNullOrWhiteSpace(r.concernMatch.summary)) lines.Add("<b>Skin reaction:</b> " + r.concernMatch.summary.Trim());
            else if (r.concernMatch != null && r.concernMatch.level == "good") lines.Add("<b>Skin reaction:</b> no caution rule matched your saved reactions.");
            var why = (r.findings ?? new List<IngredientFinding>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.reason) && (f.level == "harmful" || f.level == "caution")).Take(6)
                .Select(f => "• <b>" + f.name + "</b>: " + f.reason.Trim()).ToList();
            if (why.Count > 0) lines.Add("The ingredients behind it:" + (char)10 + string.Join(((char)10).ToString(), why));
            if (lines.Count == 0) return "Nothing to compare yet: save your allergens and skin type in your profile, then scan again.";
            lines.Add("Only the ingredients on the list can trigger a reaction, so GlowUp matches each one against what you saved. Names it does not know show as \"Not checked\".");
            return string.Join(((char)10).ToString() + (char)10, lines);
        }

        static string Verdict(ScanRecord r)
        {
            string head = r.status == "harmful" ? "<b>Flagged for your profile.</b> " : r.status == "caution" ? "<b>Caution.</b> " : r.status == "safe" ? "<b>No caution found for your profile.</b> " : "";
            return head + (r.summary ?? "").Trim();
        }

        static void AddMatch(List<Item> list, string question, CompatibilityItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.summary)) return;
            string lead = item.level == "bad" ? "<b>Yes, watch out.</b> " : item.level == "caution" ? "<b>Possibly.</b> " : item.level == "good" ? "" : "";
            if (question.StartsWith("Does it suit") || question.StartsWith("Is it OK") || question.StartsWith("Is it right"))
                lead = item.level == "good" ? "<b>Yes.</b> " : item.level == "caution" || item.level == "bad" ? "<b>Take care.</b> " : "";
            if (question.StartsWith("Does it contain")) lead = item.level == "bad" ? "<b>Yes.</b> " : item.level == "good" ? "<b>No.</b> " : "";
            list.Add(new Item(question, lead + item.summary.Trim()));
        }

        static string Fda(ScanRecord r)
        {
            switch ((r.fdaStatus ?? "").Trim())
            {
                case "registered": return "Yes. It is on the FDA Philippines list of notified cosmetics" + (string.IsNullOrWhiteSpace(r.fdaNumber) ? "." : $" (number {r.fdaNumber}).") + " Notification means it was declared to the FDA; it does not check the product for your skin.";
                case "similar": return "A similar product of this brand is on the FDA Philippines list, but not this exact name. Check the pack for an FDA notification number.";
                case "expired": return "It was on the FDA Philippines list, but the notification has ended" + (string.IsNullOrWhiteSpace(r.fdaValidUntil) ? "." : $" ({r.fdaValidUntil}).");
                case "none": return "It was not found on the FDA Philippines list of notified cosmetics. That can mean it is not sold officially in the Philippines; check the pack.";
                default: return r.fdaChecked ? (string.IsNullOrWhiteSpace(r.fdaLine) ? "The FDA Philippines answer was not clear." : r.fdaLine) : "Not checked yet: it needs the internet. Open this result again when you are online, or use the FDA Philippines button.";
            }
        }

        static string ListOrigin(ScanRecord r)
        {
            string from;
            switch ((r.listFrom ?? "").Trim())
            {
                case "pack": from = "It was read from the pack by the camera."; break;
                case "inventory": from = "It is the list the GlowUp team keeps for this product."; break;
                case "web": from = "It was looked up online" + (r.ingredientSources != null && r.ingredientSources.Count > 0 ? " (see the sources on this screen)." : "."); break;
                case "typed": from = "You typed it in."; break;
                default: return "";
            }
            if (r.listCheck == "matched") from += " A second source has the same list.";
            else if (r.listCheck == "differs") from += $" A second source differs by {r.listDifferCount} name{(r.listDifferCount == 1 ? "" : "s")}: compare with the pack.";
            return from;
        }

        static string FirstSentence(string text)
        {
            string t = (text ?? "").Trim();
            int end = t.IndexOf(". ");
            return end > 20 && end < 220 ? t.Substring(0, end + 1) : (t.Length > 220 ? t.Substring(0, 217) + "..." : t);
        }
    }
}
