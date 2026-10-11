using System;
using System.Collections.Generic;
using GlowUp.Data;

namespace GlowUp.Scan
{
    /// <summary>
    /// Decides which inventory products may be suggested to ONE user ("Recommendation for you" on the dashboard and the
    /// alternatives on the result screen). Everything is worked out on the phone: nothing about the user is sent anywhere.
    ///
    /// A product is suggested only when ALL of this is true:
    ///   1. it is make-up (skin care, hair, body, nail and fragrance products are never suggested);
    ///   2. it has an ingredient list;
    ///   3. the user is old enough for it (the product's minimum age, when the website gives one);
    ///   4. not one of its ingredients is flagged for this user (allergens, skin type, skin conditions, age).
    /// For a user who saved a SKIN CONDITION the check is stricter, because "we do not know" is not good enough for them:
    ///   5. the ingredient list must come from a source (an admin typed or reviewed it, or pages are on file);
    ///   6. every ingredient must be one the app knows (at most <see cref="UnknownAllowedWithCondition"/> unknown ones).
    ///
    /// What a suggestion means: "no ingredients flagged for your profile". It never means "safe" or "good for your
    /// condition", and the app says so next to the list.
    /// </summary>
    public static class ProductRecommender
    {
        /// <summary>For a user with a skin condition: how many ingredients of a product the app may not know and still suggest it. 0 = none.</summary>
        public static int UnknownAllowedWithCondition = 0;

        public enum Refusal
        {
            None,               // it may be suggested
            NotMakeup,
            NoIngredientList,
            TooYoung,
            Flagged,            // an ingredient is flagged for this user
            Unsourced,          // skin condition: nobody vouches for the ingredient list
            NotFullyChecked     // skin condition: some ingredients are unknown to the app
        }

        /// <summary>One product that passed, with the analysis made for this user.</summary>
        public class Pick
        {
            public ProductData product;
            public ScanRecord record;
        }

        /// <summary>May this product be suggested to this user? `record` is the analysis that was made for the decision (null when it never got that far).</summary>
        public static Refusal Check(ProductData product, UserProfileData who, out ScanRecord record)
        {
            record = null;
            who = who ?? new UserProfileData();
            if (product == null || !ProductScope.IsMakeup(product.cosmeticType, product.name)) return Refusal.NotMakeup;
            if (product.ingredients == null || product.ingredients.Count == 0) return Refusal.NoIngredientList;
            if (product.minAge > 0 && who.age > 0 && who.age < product.minAge) return Refusal.TooYoung;

            bool condition = ProductScope.HasSkinCondition(who);
            if (condition && !HasSource(product)) return Refusal.Unsourced;

            record = ProductAnalysisEngine.Analyse(product.ingredients, "", who, null, product.name, product.brandName, product.cosmeticType);
            if (record.status != "safe") return Refusal.Flagged;
            if (condition && CountUnknown(record) > Math.Max(0, UnknownAllowedWithCondition)) return Refusal.NotFullyChecked;

            record.source = "recommendation";
            record.brandId = product.brandId;
            record.productId = product.id;
            record.productShades = new List<string>(product.shades ?? new List<string>());
            return Refusal.None;
        }

        /// <summary>
        /// The products that may be suggested to this user, in the order given, at most `maximum` of them.
        /// `alsoRequire` narrows the choice further (the result screen asks for the same kind of product).
        /// </summary>
        public static List<Pick> For(UserProfileData who, IEnumerable<ProductData> products, int maximum, Func<ProductData, bool> alsoRequire = null)
        {
            var picks = new List<Pick>();
            if (products == null) return picks;
            foreach (ProductData product in products)
            {
                if (picks.Count >= maximum) break;
                if (product == null || (alsoRequire != null && !alsoRequire(product))) continue;
                if (Check(product, who, out ScanRecord record) == Refusal.None) picks.Add(new Pick { product = product, record = record });
            }
            return picks;
        }

        /// <summary>
        /// True when somebody vouches for the product's ingredient list: an admin typed the product in or reviewed it, it came
        /// with a named data source, or the pages it was taken from are on file. A list the app itself read in a scan, or
        /// that the AI looked up, has no source until an admin reviews it.
        /// </summary>
        public static bool HasSource(ProductData product)
        {
            if (product == null) return false;
            if (product.reviewed) return true;
            if (product.sources != null && product.sources.Count > 0) return true;
            string source = (product.source ?? "").Trim().ToLowerInvariant();
            return source != "scan" && source != "ai" && source != "lookup";
        }

        static int CountUnknown(ScanRecord record)
        {
            int unknown = 0;
            if (record != null && record.findings != null)
                foreach (IngredientFinding finding in record.findings) if (finding.level == "unknown") unknown++;
            return unknown;
        }
    }
}
