using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// Keeps count of how often the AI is asked during ONE visit of the scan screen, so that scanning the same product
    /// again and again cannot use up the AI quota (which every user of the app shares).
    ///
    ///   Recognising the product from pictures: at most <see cref="RecognitionsPerSession"/> times. After that the scan
    ///     screen asks the user to type the name instead.
    ///   Looking a product's ingredient list up: at most <see cref="ListLookUpsPerSession"/> times.
    ///   Web search: when the AI service refuses it once (the key does not offer it), it is not tried again for a
    ///     while, which saves one wasted request for every question.
    ///
    /// A visit starts when the scan screen opens; "Scan again" on that screen does NOT start a new one.
    /// The numbers are set in the Scan Rules asset (GlowUp > Open Scan Rules).
    /// </summary>
    public static class AiBudget
    {
        public static int RecognitionsPerSession = 2;
        public static int ListLookUpsPerSession = 2;
        public static float SkipWebSearchMinutes = 30f;

        static int recognitions, lookUps;
        static float searchRefusedAt = -1f;

        /// <summary>The scan screen was opened: the counts start again.</summary>
        public static void NewSession() { recognitions = 0; lookUps = 0; }

        public static int RecognitionsUsed => recognitions;
        public static int RecognitionsLeft => Mathf.Max(0, RecognitionsPerSession - recognitions);
        public static bool CanRecognise => recognitions < RecognitionsPerSession;
        public static bool CanLookUp => lookUps < ListLookUpsPerSession;

        /// <summary>Takes one recognition out of the budget. False = none is left: do not ask the AI.</summary>
        public static bool TakeRecognition()
        {
            if (!CanRecognise) return false;
            recognitions++;
            return true;
        }

        /// <summary>Takes one ingredient-list look-up out of the budget. False = none is left: do not ask the AI.</summary>
        public static bool TakeLookUp()
        {
            if (!CanLookUp) return false;
            lookUps++;
            return true;
        }

        /// <summary>False for a while after the AI service refused a web search: asking with it again would only waste a request.</summary>
        public static bool WebSearchWorthTrying =>
            searchRefusedAt < 0f || Time.realtimeSinceStartup - searchRefusedAt > Mathf.Max(0f, SkipWebSearchMinutes) * 60f;

        /// <summary>The AI service refused a web search (the key or plan does not offer it).</summary>
        public static void WebSearchRefused() { searchRefusedAt = Time.realtimeSinceStartup; }
    }
}
