using System.Collections.Generic;
using System.Text.RegularExpressions;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// What GlowUp is for, in one place. The app is about MAKE-UP and products for the FACE:
    ///
    ///   Make-up     - checked against the user's profile, and AR Try-On is offered.
    ///   Skin care   - the app only says whether it holds ingredients that could harm the user's skin
    ///                 type or make a skin condition worse. No consultation, no AR Try-On.
    ///   Out of scope (hair, body, nails, fragrance ...) - the user is told the app puts make-up and
    ///                 face products first; only ingredient warnings are given. No AR Try-On.
    ///
    /// The app never suggests a cure or a treatment. When the user has a skin condition it warns and
    /// explains how the product may affect it, and every result ends with the advice to see a
    /// dermatologist. The admin website uses the same rule (productScope in adminCommon.js).
    /// The wording below can be changed here.
    /// </summary>
    public static class ProductScope
    {
        public enum Kind { Makeup, SkinCare, OutOfScope }

        public const string DermatologistLine = "Consult a dermatologist for the best treatment.";
        public static string OutOfScopeNotice = "GlowUp puts make-up and face products first. This looks like a {0} product, so the result only lists ingredient warnings and AR Try-On is not available for it.";
        public static string SkinCareNotice = "This is a skin care product. GlowUp does not give consultation or treatment advice: it only tells you whether the product holds ingredients that could harm your skin or make a skin condition worse. AR Try-On is not available for skin care.";
        public static string ConditionNotice = "You listed {0}. GlowUp cannot suggest a cure or a treatment. It only warns you and explains how this product may affect your skin type and that condition.";

        static readonly Regex FaceWords = new Regex(@"\b(face|facial|lip|lips|eye|eyes|brow|brows|lash|lashes|cheek|cheeks|complexion)\b", RegexOptions.IgnoreCase);
        static readonly Regex OutWords = new Regex(@"\b(shampoo|conditioner|hair|scalp|body|hand|hands|foot|feet|nail|nails|deodorant|antiperspirant|perfume|fragrance|cologne|eau de|soap|shower|bath|shaving|shave|toothpaste|oral|intimate|baby|tanning|massage)\b", RegexOptions.IgnoreCase);
        static readonly Regex MakeupWords = new Regex(@"\b(lipstick|lip gloss|lip tint|lip balm|lip liner|gloss|tint|foundation|concealer|primer|powder|blush|blusher|bronzer|highlighter|contour|eyeshadow|eye shadow|eyeliner|liner|mascara|brow|cushion|bb cream|cc cream|makeup|make-up|setting spray)\b", RegexOptions.IgnoreCase);
        static readonly Regex HairWords = new Regex(@"\b(shampoo|conditioner|hair|scalp)\b", RegexOptions.IgnoreCase);
        static readonly Regex NailWords = new Regex(@"\b(nail|nails)\b", RegexOptions.IgnoreCase);
        static readonly Regex ScentWords = new Regex(@"\b(perfume|fragrance|cologne|eau de)\b", RegexOptions.IgnoreCase);

        public static Kind Classify(string cosmeticType, string productName)
        {
            string type = cosmeticType ?? "", text = type + " " + (productName ?? "");
            if (MakeupWords.IsMatch(type)) return Kind.Makeup;
            if (OutWords.IsMatch(text) && !FaceWords.IsMatch(text)) return Kind.OutOfScope;
            if (MakeupWords.IsMatch(text)) return Kind.Makeup;
            return Kind.SkinCare;
        }

        public static Kind Classify(ScanRecord record) => record == null ? Kind.SkinCare : Classify(record.cosmeticType, record.productName);

        /// <summary>AR Try-On is only for make-up.</summary>
        public static bool CanTryOn(ScanRecord record) => record != null && Classify(record) == Kind.Makeup;

        /// <summary>"hair", "nail", "fragrance" or "body": the word used in the out-of-scope notice.</summary>
        static string FamilyOf(string text)
        {
            if (HairWords.IsMatch(text)) return "hair";
            if (NailWords.IsMatch(text)) return "nail";
            if (ScentWords.IsMatch(text)) return "fragrance";
            return "body";
        }

        /// <summary>The notice for this product and this user ("" when there is nothing to say).</summary>
        public static string NoticeFor(ScanRecord record, UserProfileData profile)
        {
            var parts = new List<string>();
            Kind kind = Classify(record);
            if (kind == Kind.OutOfScope) parts.Add(string.Format(OutOfScopeNotice, FamilyOf((record.cosmeticType ?? "") + " " + (record.productName ?? ""))));
            else if (kind == Kind.SkinCare) parts.Add(SkinCareNotice);

            var conditions = new List<string>();
            if (profile != null && profile.skinConditions != null)
                foreach (string c in profile.skinConditions) if (!string.IsNullOrWhiteSpace(c)) conditions.Add(c.Trim());
            if (conditions.Count > 0) parts.Add(string.Format(ConditionNotice, string.Join(", ", conditions)));
            return string.Join("\n\n", parts);
        }

        /// <summary>
        /// Writes the scope into a finished scan: its kind, and the notice at the top of "Why this result?". Returns the
        /// notice ("" when there is none). The advice to see a dermatologist is the last card of the result screen
        /// (ScanResultFlowController): it is no longer repeated at the end of this text.
        /// </summary>
        public static string Apply(ScanRecord record, UserProfileData profile)
        {
            if (record == null) return "";
            // a face powder is tried on as white: the colour of its box (or of the powder in the pan) is not how it looks on skin
            if (ShadeAdvisor.IsFacePowder(record.cosmeticType)) record.shadeHex = ShadeAdvisor.PowderShade;
            Kind kind = Classify(record);
            record.scope = kind == Kind.Makeup ? "makeup" : kind == Kind.SkinCare ? "skincare" : "out";
            string notice = NoticeFor(record, profile);
            record.scopeNote = notice;

            string explanation = (record.explanation ?? "").Trim();
            if (notice.Length > 0 && !explanation.Contains(notice)) explanation = "<b>Please note</b>\n" + notice + (explanation.Length > 0 ? "\n\n" + explanation : "");
            record.explanation = explanation;
            return notice;
        }

        /// <summary>The rules every question to the AI carries.</summary>
        public const string AiRules =
            "RULES YOU MUST FOLLOW: This app is about make-up and products for the face. " +
            "Never suggest a cure, a treatment, a medicine or a dose for any skin condition, and never diagnose. " +
            "When the user has a skin condition, only warn and explain how this product may affect their skin type and that condition. " +
            "If the product is skin care, do not give consultation: only say whether it holds ingredients that could harm the skin or make a skin condition worse. " +
            "If the product is for hair, body, nails or is a fragrance, say in one sentence that the app puts make-up and face products first, then give only ingredient warnings. " +
            "Do not end with advice of your own: the app adds the line about seeing a dermatologist itself.";
    }
}
