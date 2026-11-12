using System;
using System.Collections.Generic;
using GlowUp.Data;
using UnityEngine;

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
    /// A valid recommendation also requires a complete catalog record and every ingredient to be evaluable by the
    /// analysis engine. Users with a skin condition additionally need a sourced ingredient list.
    ///
    /// What a suggestion means: "no ingredients flagged for your profile". It never means "safe" or "good for your
    /// condition", and the app says so next to the list.
    /// </summary>
    public static class ProductRecommender
    {
        public enum Refusal
        {
            None,               // it may be suggested
            NotRecommendable,
            NotMarkedRecommendable,
            MissingProductName,
            InvalidBrand,
            InactiveProduct,
            MissingCategory,
            NotMakeup,
            NoIngredientList,
            TooYoung,
            Flagged,            // an ingredient is flagged for this user
            Unsourced,          // skin condition: nobody vouches for the ingredient list
            NotFullyChecked     // at least one ingredient cannot be evaluated
        }

        /// <summary>One product that passed, with the analysis made for this user.</summary>
        public class Pick
        {
            public ProductData product;
            public ScanRecord record;
        }

        /// <summary>The minimum profile needed for a personalized recommendation.</summary>
        public static bool IsProfileComplete(UserProfileData profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.skinType) || string.IsNullOrWhiteSpace(profile.skinTone)
                || profile.knownAllergens == null) return false;
            foreach (string answer in profile.knownAllergens) if (!string.IsNullOrWhiteSpace(answer)) return true;
            return false;
        }

        /// <summary>Whether the catalog record is complete enough to appear on either recommendation surface.</summary>
        public static bool IsRecommendable(ProductData product, BrandData brand, out string reasonCode)
        {
            reasonCode = "";
            if (product == null) { reasonCode = "product-missing"; return false; }
            if (!product.recommendable) { reasonCode = "recommendable-flag-false"; return false; }
            if (string.IsNullOrWhiteSpace(product.name)) { reasonCode = "product-name-missing"; return false; }
            if (product.binned) { reasonCode = "product-archived"; return false; }
            if (!string.Equals((product.status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = "product-not-active";
                return false;
            }
            if (string.IsNullOrWhiteSpace(product.brandId) || brand == null || brand.id != product.brandId)
            {
                reasonCode = "brand-missing";
                return false;
            }
            if (brand.binned) { reasonCode = "brand-archived"; return false; }
            if (!string.Equals((brand.status ?? "").Trim(), "Active", StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = "brand-inactive";
                return false;
            }
            if (string.IsNullOrWhiteSpace(brand.name)) { reasonCode = "brand-name-missing"; return false; }
            if (string.IsNullOrWhiteSpace(brand.category)) { reasonCode = "brand-category-missing"; return false; }
            if (!string.Equals(brand.category.Trim(), "Makeup", StringComparison.OrdinalIgnoreCase))
            {
                reasonCode = "brand-category-not-makeup";
                return false;
            }
            if (string.IsNullOrWhiteSpace(product.brandName) || !BrandNameMatches(product.brandName, brand))
            {
                reasonCode = "brand-name-mismatch";
                return false;
            }
            if (string.IsNullOrWhiteSpace(product.cosmeticType)) { reasonCode = "category-missing"; return false; }
            if (!ProductScope.IsMakeup(product.cosmeticType, product.name)) { reasonCode = "not-makeup"; return false; }
            if (product.ingredients == null || product.ingredients.Count == 0) { reasonCode = "ingredients-missing"; return false; }

            foreach (string ingredient in product.ingredients)
            {
                IngredientKnowledge.Match match = IngredientTextParser.MatchEntry(ingredient);
                if (!match.Found || match.data == null) { reasonCode = "ingredient-unresolved"; return false; }
                if (match.kind == IngredientKnowledge.MatchKind.Family || match.data.generic || match.data.IsUnreviewedAi())
                {
                    reasonCode = "ingredient-not-evaluable";
                    return false;
                }
            }
            return true;
        }

        static bool BrandNameMatches(string productBrandName, BrandData brand)
        {
            if (TextKey.Same(productBrandName, brand.name)) return true;
            if (brand.aliases != null)
                foreach (string alias in brand.aliases) if (TextKey.Same(productBrandName, alias)) return true;
            return false;
        }

        /// <summary>May this product be suggested to this user? `record` is the analysis made for the decision.</summary>
        public static Refusal Check(ProductData product, UserProfileData who, out ScanRecord record)
        {
            return Check(product, BrandRepository.FindById(product != null ? product.brandId : null), who, out record, out _);
        }

        public static Refusal Check(ProductData product, BrandData brand, UserProfileData who, out ScanRecord record)
        {
            return Check(product, brand, who, out record, out _);
        }

        static Refusal Check(ProductData product, BrandData brand, UserProfileData who, out ScanRecord record, out string reasonCode)
        {
            record = null;
            who = who ?? new UserProfileData();
            if (!IsRecommendable(product, brand, out reasonCode)) return RefusalFor(reasonCode);
            if (product.minAge > 0 && who.age > 0 && who.age < product.minAge) { reasonCode = "too-young"; return Refusal.TooYoung; }

            bool condition = ProductScope.HasSkinCondition(who);
            if (condition && !HasSource(product)) { reasonCode = "ingredient-list-unsourced"; return Refusal.Unsourced; }

            record = ProductAnalysisEngine.Analyse(product.ingredients, "", who, null, product.name, product.brandName, product.cosmeticType);
            if (record.status != "safe") { reasonCode = "analysis-flagged"; return Refusal.Flagged; }
            if (CountUnknown(record) > 0) { reasonCode = "analysis-incomplete"; return Refusal.NotFullyChecked; }

            record.source = "recommendation";
            record.brandId = product.brandId;
            record.productId = product.id;
            record.productShades = new List<string>(product.shades ?? new List<string>());
            reasonCode = "";
            return Refusal.None;
        }

        static Refusal RefusalFor(string reasonCode)
        {
            switch (reasonCode)
            {
                case "recommendable-flag-false": return Refusal.NotMarkedRecommendable;
                case "product-name-missing": return Refusal.MissingProductName;
                case "product-not-active":
                case "product-archived": return Refusal.InactiveProduct;
                case "brand-missing":
                case "brand-archived":
                case "brand-inactive":
                case "brand-name-missing":
                case "brand-category-missing":
                case "brand-category-not-makeup":
                case "brand-name-mismatch": return Refusal.InvalidBrand;
                case "category-missing": return Refusal.MissingCategory;
                case "not-makeup": return Refusal.NotMakeup;
                case "ingredients-missing": return Refusal.NoIngredientList;
                case "ingredient-unresolved":
                case "ingredient-not-evaluable": return Refusal.NotFullyChecked;
                default: return Refusal.NotRecommendable;
            }
        }

        /// <summary>
        /// The products that may be suggested to this user, in the order given, at most `maximum` of them.
        /// `alsoRequire` narrows the choice further (the result screen asks for the same kind of product).
        /// </summary>
        public static List<Pick> For(UserProfileData who, IEnumerable<ProductData> products, int maximum, Func<ProductData, bool> alsoRequire = null)
        {
            return For(who, products, BrandRepository.All(), maximum, alsoRequire);
        }

        public static List<Pick> For(UserProfileData who, IEnumerable<ProductData> products, IEnumerable<BrandData> brands, int maximum, Func<ProductData, bool> alsoRequire = null)
        {
            var picks = new List<Pick>();
            if (products == null) return picks;
            var brandById = new Dictionary<string, BrandData>(StringComparer.Ordinal);
            if (brands != null)
                foreach (BrandData brand in brands)
                    if (brand != null && !string.IsNullOrEmpty(brand.id)) brandById[brand.id] = brand;
            foreach (ProductData product in products)
            {
                if (picks.Count >= maximum) break;
                if (product == null || (alsoRequire != null && !alsoRequire(product))) continue;
                brandById.TryGetValue(product.brandId ?? "", out BrandData brand);
                Refusal refusal = Check(product, brand, who, out ScanRecord record, out string reasonCode);
                if (refusal == Refusal.None) picks.Add(new Pick { product = product, record = record });
                else if (product.recommendable && refusal != Refusal.NotMarkedRecommendable)
                    Debug.LogWarning($"[RecFilter] productId={product.id} reason={reasonCode}");
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
