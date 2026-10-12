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

        [Header("Hold-to-scan: is a camera picture good enough to be read?")]
        [Tooltip("A picture darker than this is not read (0 = black, 1 = white). The hint says: too dark.")]
        [Range(0.02f, 0.5f)] public float pictureDarkBelow = 0.14f;
        [Tooltip("A pixel at least this bright counts as burnt out by glare (0 to 1)")]
        [Range(0.8f, 1f)] public float glareBrightness = 0.97f;
        [Tooltip("The hint says 'there is glare' when more than this share of the picture is burnt out")]
        [Range(0.02f, 0.6f)] public float glareShare = 0.18f;
        [Tooltip("The phone counts as moving when the picture changed more than this since the look before (0 to 1). Such a picture is not read.")]
        [Range(0.01f, 0.3f)] public float movingAbove = 0.06f;
        [Tooltip("Sharpness below this counts as out of focus (the share of fine detail among the edges of the picture)")]
        [Range(0.1f, 0.9f)] public float blurBelow = 0.4f;
        [Tooltip("A picture with fewer edges than this has nothing to judge sharpness by (a plain wall): it is not called blurred")]
        [Range(0f, 0.5f)] public float minimumEdges = 0.1f;

        [Header("Hold-to-scan: when is a part of the ring full?")]
        [Tooltip("A line of text counts as clearly read when the text reader is at least this sure of it (0 to 1)")]
        [Range(0.3f, 0.95f)] public float clearLineConfidence = 0.55f;
        [Tooltip("A line the text reader is less sure of than this is thrown away (0 to 1)")]
        [Range(0.05f, 0.6f)] public float usableLineConfidence = 0.3f;
        [Tooltip("Front: this many pictures of the front fill the part (a brand the app knows counts double; a product the app knows fills it at once)")]
        [Range(1, 6)] public int frontPictures = 2;
        [Tooltip("Other sides: this many different views of the product fill the part")]
        [Range(1, 8)] public int viewsAround = 3;
        [Tooltip("A picture is a new view when less than this share of its clear lines was seen before")]
        [Range(0.1f, 0.9f)] public float newViewBelow = 0.4f;
        [Tooltip("Shade: this many pictures in a row with the same colour fill the part (a printed shade name fills it at once)")]
        [Range(1, 6)] public int colourPictures = 2;
        [Tooltip("Ingredients: the list must have at least this many names that count")]
        [Range(1, 10)] public int minimumIngredients = 2;
        [Tooltip("An ingredient name the app does not know counts once it was read in this many pictures (a name the app knows counts at once)")]
        [Range(1, 5)] public int picturesForAnUnknownName = 2;
        [Tooltip("The list is complete when this many pictures of it in a row brought no new name")]
        [Range(1, 6)] public int quietPicturesForDone = 2;

        [Header("Knowing a pack again by its look (compared on the phone)")]
        [Tooltip("How alike a pack must be to one the phone remembers for the question \"Is this ...?\" to be asked at once (1 = the same picture). Higher = asked less often, and wrong less often.")]
        [Range(0.5f, 0.95f)] public float packStrongLikeness = 0.70f;
        [Tooltip("From this likeness on the question is asked when a word of the product's or the brand's name was read on the pack as well")]
        [Range(0.3f, 0.9f)] public float packMediumLikeness = 0.55f;
        [Tooltip("The share of the camera picture, round its middle, that is compared (the edges are mostly the hand and the background)")]
        [Range(0.5f, 1f)] public float packPictureMiddle = 0.8f;

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
            AiBudget.RecognitionsPerSession = Mathf.Max(0, aiRecognitionsPerScanSession);
            AiBudget.ListLookUpsPerSession = Mathf.Max(0, aiListLookUpsPerScanSession);
            AiBudget.SkipWebSearchMinutes = Mathf.Max(0f, skipWebSearchMinutesAfterRefusal);

            FrameQuality.DarkBelow = Mathf.Clamp01(pictureDarkBelow);
            FrameQuality.GlareLevel = Mathf.Clamp01(glareBrightness); FrameQuality.GlareShare = Mathf.Clamp01(glareShare);
            FrameQuality.MovingAbove = Mathf.Max(0.001f, movingAbove);
            FrameQuality.BlurBelow = Mathf.Clamp01(blurBelow); FrameQuality.MinimumEdges = Mathf.Clamp01(minimumEdges);
            ScanEvidence.ClearLine = Mathf.Clamp01(clearLineConfidence);
            ScanEvidence.UsableLine = Mathf.Clamp(usableLineConfidence, 0f, ScanEvidence.ClearLine);
            ScanEvidence.FrontPictures = Mathf.Max(1, frontPictures); ScanEvidence.ViewsAround = Mathf.Max(1, viewsAround);
            ScanEvidence.NewViewBelow = Mathf.Clamp01(newViewBelow); ScanEvidence.ColourPictures = Mathf.Max(1, colourPictures);
            ScanEvidence.MinimumIngredients = Mathf.Max(1, minimumIngredients);
            ScanTextMerger.SeenToCount = Mathf.Max(1, picturesForAnUnknownName);
            ScanTextMerger.QuietFramesForDone = Mathf.Max(1, quietPicturesForDone);

            PackagingMemory.StrongLikeness = Mathf.Clamp01(packStrongLikeness);
            PackagingMemory.MediumLikeness = Mathf.Clamp(packMediumLikeness, 0f, PackagingMemory.StrongLikeness);
            PackEmbedder.Middle = Mathf.Clamp(packPictureMiddle, 0.3f, 1f);
        }

        void OnValidate() { if (Application.isPlaying) Apply(); }
    }
}
