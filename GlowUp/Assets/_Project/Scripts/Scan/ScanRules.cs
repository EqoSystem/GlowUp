using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// The rules of scanning and recommending that hold for the WHOLE app, in one place:
    /// Assets/_Project/Resources/ScanRules.asset (menu: GlowUp > Open Scan Rules). Select it and change the numbers in
    /// the Inspector; no code needs to be touched.
    ///
    /// They are read once when the app starts, before the first screen, so every screen (the scanner, the result, the
    /// dashboard's recommendations) works by the same numbers. Settings that belong to one screen only are still on that
    /// screen's own controller.
    /// </summary>
    [CreateAssetMenu(menuName = "GlowUp/Scan Rules", fileName = "ScanRules")]
    public class ScanRules : ScriptableObject
    {
        public const string ResourceName = "ScanRules";

        [Header("Reading the ingredient list")]
        [Tooltip("How close a misread name must be to a known ingredient name to still count as that ingredient. 1 = only exact matches. 0.85 = about one wrong letter in seven (two in fourteen). Lower = more forgiving, and more wrong matches.")]
        [Range(0.7f, 1f)] public float fuzzyMatchThreshold = 0.85f;
        [Tooltip("A result is called incomplete (and never 'Nothing flagged') when fewer than this share of the names on the label could be checked. 0.5 = at least half.")]
        [Range(0f, 1f)] public float minimumCheckedShare = 0.5f;
        [Tooltip("The longest text that can still be one ingredient name (longer pieces of label text are thrown away)")]
        [Range(40, 120)] public int longestIngredientName = 70;

        [Header("Recommendations for a user with a skin condition")]
        [Tooltip("A product is recommended to such a user only when the app knows every one of its ingredients. This many unknown ingredients are tolerated (0 = none).")]
        [Min(0)] public int unknownIngredientsAllowed = 0;

        [Header("Keeping the product, brand and ingredient lists up to date")]
        [Tooltip("A list that was checked less than this many minutes ago is used as it is, without asking the database again")]
        [Min(0f)] public float refreshMinutes = 10f;
        [Tooltip("Normally only what changed is downloaded. After this many hours everything is downloaded again once, to catch entries that were deleted.")]
        [Min(1f)] public float fullDownloadEveryHours = 24f;

        [Header("AI limits (protects the shared AI quota)")]
        [Tooltip("How many times the AI may look at pictures to recognise a product in one visit of the scan screen. After that the user is asked to type the name.")]
        [Min(0)] public int aiRecognitionsPerScanSession = 2;
        [Tooltip("How many times the AI may be asked for a product's ingredient list in one visit of the scan screen")]
        [Min(0)] public int aiListLookUpsPerScanSession = 2;
        [Tooltip("After the AI's web search was refused once (the key does not offer it), it is not tried again for this many minutes: that saves one wasted request per question")]
        [Min(0f)] public float skipWebSearchMinutesAfterRefusal = 30f;

        /// <summary>The rules in force (the asset, or the defaults above when the asset is missing).</summary>
        public static ScanRules Current
        {
            get
            {
                if (current == null) current = Resources.Load<ScanRules>(ResourceName);
                if (current == null) current = CreateInstance<ScanRules>();
                return current;
            }
        }
        static ScanRules current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStart() { current = null; Current.Apply(); }

        /// <summary>Hands the numbers to the parts of the app that use them.</summary>
        public void Apply()
        {
            IngredientKnowledge.FuzzyThreshold = Mathf.Clamp(fuzzyMatchThreshold, 0.5f, 1f);
            ProductAnalysisEngine.MinimumCheckedShare = Mathf.Clamp01(minimumCheckedShare);
            IngredientStoplist.MaximumLength = Mathf.Max(20, longestIngredientName);
            ProductRecommender.UnknownAllowedWithCondition = Mathf.Max(0, unknownIngredientsAllowed);
            CatalogSync.RefreshMinutes = Mathf.Max(0f, refreshMinutes);
            CatalogSync.FullDownloadEveryHours = Mathf.Max(1f, fullDownloadEveryHours);
        }

        void OnValidate() { if (Application.isPlaying) Apply(); }
    }
}
