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
            + "• Acne\n• Eczema (atopic dermatitis)\n• Psoriasis\n• Rosacea\n• Seborrheic dermatitis\n"
            + "• Melasma / hyperpigmentation\n• Contact dermatitis\n\n"
            + "<b>Allergens</b>: the ingredient families most often behind cosmetic reactions in patch testing (fragrance, preservatives "
            + "such as parabens and formaldehyde releasers, lanolin and others), from the American Contact Dermatitis Society's lists.\n\n"
            + "Not on the list? Use \"Not listed? Add it here\": the GlowUp team reviews what users add and can put it on the list for everyone.";

        public const string Note = "GlowUp is not a medical service. Pick only what a doctor has told you, or what you know you have.";
    }
}
