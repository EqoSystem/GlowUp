using System.Collections.Generic;

namespace GlowUp.Onboarding
{
    /// <summary>
    /// "Not sure? Find your skin type" on Step 2: three questions about how the skin feels and looks after washing and by
    /// midday, the usual signs dermatology guides use to tell the skin types apart. The answers point to one of the app's
    /// four types. General guidance only; the sources named to the user must be checked by the owner before the defence.
    /// Pure logic, so it is tested without a scene (Editor/Tests/SkinTypeFinderTests.cs).
    /// </summary>
    public static class SkinTypeFinder
    {
        public const string Oily = "Oily", Normal = "Normal", Dry = "Dry", Mix = "Mix";

        public class Question
        {
            public string text;
            public string[] answers;      // each answer adds points to the types in `points` (same order)
            public string[][] points;
        }

        public static readonly Question[] Questions =
        {
            new Question
            {
                text = "Wash your face with a mild cleanser, pat it dry and wait 30 minutes without putting anything on. How does it feel?",
                answers = new[] { "Tight or itchy all over", "Comfortable, nothing special", "Greasy all over", "Greasy on the nose and forehead only" },
                points = new[] { new[] { Dry }, new[] { Normal }, new[] { Oily }, new[] { Mix } }
            },
            new Question
            {
                text = "By midday, how does your face look in the mirror?",
                answers = new[] { "Dull, rough or flaky patches", "Even, rarely shiny", "Shiny all over", "Shiny T-zone, normal or dry cheeks" },
                points = new[] { new[] { Dry }, new[] { Normal }, new[] { Oily }, new[] { Mix } }
            },
            new Question
            {
                text = "How do your pores look, on the nose and on the cheeks?",
                answers = new[] { "Small, hardly visible", "Visible on the nose only", "Large and visible almost everywhere", "Large on the nose and forehead, small on the cheeks" },
                points = new[] { new[] { Dry, Normal }, new[] { Normal, Mix }, new[] { Oily }, new[] { Mix } }
            }
        };

        /// <summary>The type with the most points; a tie goes to the type of the first question's answer (how the skin feels).</summary>
        public static string Result(IList<int> answers)
        {
            var score = new Dictionary<string, int> { { Oily, 0 }, { Normal, 0 }, { Dry, 0 }, { Mix, 0 } };
            for (int q = 0; q < Questions.Length && answers != null && q < answers.Count; q++)
            {
                int a = answers[q];
                if (a < 0 || a >= Questions[q].points.Length) continue;
                foreach (string type in Questions[q].points[a]) score[type] += 2;
            }
            string first = answers != null && answers.Count > 0 && answers[0] >= 0 && answers[0] < Questions[0].points.Length ? Questions[0].points[answers[0]][0] : Normal;
            string best = first; int top = score[first];
            foreach (var pair in score) if (pair.Value > top) { best = pair.Key; top = pair.Value; }
            return best;
        }

        public static string Explain(string type)
        {
            switch (type)
            {
                case Oily: return "Your answers point to <b>oily</b> skin: shine and visible pores across the face.";
                case Dry: return "Your answers point to <b>dry</b> skin: tightness, dullness or flaking.";
                case Mix: return "Your answers point to <b>combination (mix)</b> skin: an oily T-zone with normal or dry cheeks.";
                default: return "Your answers point to <b>normal</b> skin: balanced, comfortable, rarely shiny.";
            }
        }

        public const string SourceNote = "Based on the usual signs dermatology guides describe for each skin type (for example the American Academy of Dermatology, aad.org). General guidance, not a diagnosis: skin can change with weather, age and hormones, and you can change your answer later in Settings.";
    }

    /// <summary>Where the app's skin lists come from, shown under the lists so users do not have to look it up.</summary>
    public static class SkinListSources
    {
        public const string Title = "Where these lists come from";

        public const string Body =
            "<b>Skin types</b> (Oily, Normal, Dry, Mix): the four everyday types named by the American Academy of Dermatology. "
            + "Sensitive skin is covered on the allergies step, by the ingredients you react to.\n\n"
            + "<b>Chronic skin conditions</b>: long-lasting conditions that make-up ingredients are known to affect, as described by the "
            + "American Academy of Dermatology and DermNet (New Zealand):\n"
            + "• <b>Acne</b>: blocked pores and inflamed pimples, mostly on the face. Heavy, pore-clogging make-up can make it worse.\n"
            + "• <b>Eczema (atopic dermatitis)</b>: dry, itchy, easily irritated skin. Fragrance and some preservatives can set it off.\n"
            + "• <b>Psoriasis</b>: thick, scaly patches from skin renewing too fast. Irritation can bring new patches.\n"
            + "• <b>Rosacea</b>: lasting redness and flushing of the cheeks and nose. Alcohol and fragrance are common triggers.\n"
            + "• <b>Seborrheic dermatitis</b>: red, flaky skin where skin is oily (sides of the nose, brows, hairline).\n"
            + "• <b>Melasma / hyperpigmentation</b>: brown patches, often on the cheeks and forehead. Sun makes them darker.\n"
            + "• <b>Contact dermatitis</b>: a red, itchy rash where a substance touched the skin, from irritation or an allergy.\n\n"
            + TypeSources + "\n\n"
            + "<b>Allergens</b>: the ingredient families most often behind cosmetic reactions in patch testing (fragrance, preservatives "
            + "such as parabens and formaldehyde releasers, lanolin and others), from the American Contact Dermatitis Society's lists.\n\n"
            + "Not on the list? Use \"Not listed? Add it here\": the GlowUp team reviews what users add and can put it on the list for everyone.";

        public const string Note = "GlowUp is not a medical service. Pick only what a doctor has told you, or what you know you have.";

        public const string ToneTitle = "Where the skin tones come from";

        public const string ToneBody =
            "<b>Fitzpatrick scale</b> (Fitzpatrick, 1975; Archives of Dermatology 1988): the long-standing six-type scale of how skin reacts to the sun, "
            + "from \"always burns\" to \"rarely burns\". It is the scale dermatologists use most, so the one-line description on each tile follows it.\n\n"
            + "<b>Monk Skin Tone Scale</b> (Dr Ellis Monk with Google, 2022): ten shades made to cover a wider range of skin, especially darker skin. "
            + "A 2025 comparison with measured skin colour found it matched the measurement more closely than the Fitzpatrick scale "
            + "(Diamantidis et al., Medical University of Vienna). A shade on a screen is still an approximation.\n\n"
            + "<b>GlowUp's six tones</b> (Fair, Light, Medium, Tan, Deep, Dark) group these scales into six easy-to-pick tiles. "
            + "The selfie option reads the colour of the skin in the middle of one photo and picks the nearest tile; lighting changes what a camera sees, so you can always choose another.";

        public const string TypeSources =
            "<b>Baumann Skin Type Indicator</b> (Baumann, Dermatologic Clinics 2008): argues that normal / dry / oily / combination says little about the skin, "
            + "and adds sensitive vs resistant, pigmented vs not, wrinkled vs tight. GlowUp keeps the four everyday types for simplicity and handles sensitivity "
            + "through the ingredients you react to (the allergens step).";
    }
}
