using System;
using System.Collections.Generic;

namespace GlowUp.Data
{
    /// <summary>User profile mirrored in Firestore users/{uid} and in the offline cache.</summary>
    [Serializable]
    public class UserProfileData
    {
        public string uid, firstName, middleName, lastName, username, email;
        public string birthday;                 // yyyy-MM-dd
        public int age;
        public string skinType, skinTone;
        public List<string> skinConditions = new List<string>();
        public List<string> knownAllergens = new List<string>();
        public bool termsAccepted;
        public string theme = "light";
        public long createdAtUnix;

        /// <summary>What knownAllergens holds when the user ticked "No known allergens" at onboarding.</summary>
        public const string NoKnownAllergens = "No known allergens";

        public static bool IsNoAllergenChoice(string value) =>
            string.Equals((value ?? "").Trim(), NoKnownAllergens, StringComparison.OrdinalIgnoreCase);

        /// <summary>The user's actual allergens: the "No known allergens" choice is an answer, not an allergen.</summary>
        public List<string> RealAllergens()
        {
            var real = new List<string>();
            if (knownAllergens != null)
                foreach (string a in knownAllergens)
                    if (!string.IsNullOrWhiteSpace(a) && !IsNoAllergenChoice(a)) real.Add(a);
            return real;
        }

        /// <summary>True when the user answered "No known allergens" (and listed nothing else).</summary>
        public bool SaysNoKnownAllergens()
        {
            if (knownAllergens == null) return false;
            bool said = false;
            foreach (string a in knownAllergens) if (IsNoAllergenChoice(a)) said = true;
            return said && RealAllergens().Count == 0;
        }
    }

    /// <summary>
    /// One ingredient. The admin website's 'ingredients' collection supplies name / techName / description;
    /// every other field is optional there and is filled from the app's built-in knowledge when missing.
    /// </summary>
    [Serializable]
    public class IngredientData
    {
        public string id, name, techName, description;
        public string riskLevel;                 // "safe" | "caution" | "harmful"
        public string cautionNote;               // short reason shown when the ingredient is flagged
        public string commonUses;                // what kinds of product it is used in
        public int minAge;
        public bool keyActive;                   // worth highlighting as a chip
        public bool umbrella;                    // a label term that can hide undisclosed ingredients (e.g. Fragrance)
        public List<string> highlights = new List<string>();          // ids of the custom highlights the admin website lists (config/lists)
        public List<string> aliases = new List<string>();
        public List<string> allergenTags = new List<string>();        // fragrance, paraben, sulfate, formaldehyde, lanolin, essentialoil, nut
        public List<string> avoidForSkinTypes = new List<string>();   // Oily, Normal, Dry, Mix
        public List<string> avoidForConditions = new List<string>();  // acne, eczema, psoriasis, rosacea, seborrheic, melasma, contact
        public List<string> goodForSkinTypes = new List<string>();
        public List<string> goodForConditions = new List<string>();

        // ---- where the entry came from ----
        public string source;                    // "" / "admin" = typed on the website, "starter", "fda", "ai" = found by the app's AI lookup
        public bool reviewed;                    // an admin has checked it on the website (only matters for source "ai")
        public List<string> sources = new List<string>();   // web pages the information came from
        public string fdaUnii;                   // the US FDA substance registry's code for it, when the FDA lists the substance
        public bool generic;                     // only the ingredient's FAMILY was recognised ("... extract", a Colour Index number): shown, but never counted as checked

        /// <summary>
        /// True for an entry the AI lookup added that no admin has checked yet. Such an entry is shown to the
        /// user, but it can only add a warning: it is never taken as proof that an ingredient is safe.
        /// </summary>
        public bool IsUnreviewedAi() => source == "ai" && !reviewed;
    }
    [Serializable] public class IngredientCacheHolder { public List<IngredientData> items = new List<IngredientData>(); public long savedAtUnix; public CatalogSync.State sync = new CatalogSync.State(); }

    [Serializable]
    public class BrandData
    {
        public string id, name, category, market, status;
        public string website = "";              // the brand's CONFIRMED website (an admin checked that the brand owns it); "" = none
        public long scans;
        public bool binned;                      // in the website's Recycle bin: never used, but known, so it is not added a second time
        public List<string> aliases = new List<string>();             // other names the brand is known under
    }
    [Serializable] public class BrandCacheHolder { public List<BrandData> items = new List<BrandData>(); public long savedAtUnix; public CatalogSync.State sync = new CatalogSync.State(); }

    /// <summary>A catalogue product ('products' collection): used for recommendations, alternatives and AR Try-On.</summary>
    [Serializable]
    public class ProductData
    {
        public string id, name, brandId, brandName, cosmeticType, description, status;
        public string officialIngredientSource, shadeColor;
        public int minAge;
        public long scans;                       // how many times app users scanned this product (shown on the website)
        public string source;                    // "" = typed on the website, "scan" = added automatically from an app scan, or the name of the data source
        public string barcode;                   // the number under the product's barcode ("" = not known): the scan screen finds the product by it
        public bool reviewed;                    // an admin has checked it (only matters for entries the app or a data import added)
        public List<string> ingredients = new List<string>();
        public List<string> skinCareActiveIngredients = new List<string>();
        public List<string> baseTextureFormulators = new List<string>();
        public List<string> pigmentsPreservatives = new List<string>();
        public List<string> shades = new List<string>();              // hex colours, e.g. #C24B62
        public List<string> sources = new List<string>();             // pages the entry's details came from, each as "page title | address" (written on the website)
        public string fdaNumber;                 // the FDA Philippines notification number the website keeps with the product ("" = none on file)
        public bool binned;                      // in the website's Recycle bin: never used, but known, so it is not added a second time
        public List<string> aliases = new List<string>();             // other names the product is known under (old names, misspellings)
    }
    [Serializable] public class ProductCacheHolder { public List<ProductData> items = new List<ProductData>(); public long savedAtUnix; public CatalogSync.State sync = new CatalogSync.State(); }

    /// <summary>What the analysis found for one ingredient of a scanned product, for THIS user.</summary>
    [Serializable]
    public class IngredientFinding
    {
        public string name, techName;
        public string level;                     // "safe" | "caution" | "harmful" | "unknown"
        public string description;
        public string commonUses;
        public string reason;                    // why it is flagged for this user ("" when it is fine)
        public bool keyActive;
        public bool hidden;                      // umbrella term
        public bool aiSourced;                   // the description comes from the AI lookup and has not been reviewed yet
    }

    /// <summary>One line of the compatibility matrix.</summary>
    [Serializable]
    public class CompatibilityItem
    {
        public string level = "unknown";         // "good" | "caution" | "bad" | "unknown"
        public string summary = "";
    }

    /// <summary>One scanned product saved to history (users/{uid}/scans/{id}).</summary>
    [Serializable]
    public class ScanRecord
    {
        public string id, brandId, brandName, productName, cosmeticType;
        public string productId;                 // set when the scan was recognised as a product from the website's inventory
        public string officialIngredientSource;  // attribution stored with the catalogue ingredient list
        public string displayName;               // the user's own name for it - LOCAL ONLY, never synced back
        public string status;                    // "safe" | "caution" | "harmful"
        public string summary;                   // one-line verdict
        public string explanation;               // "Why this result?"
        public string source;                    // "scan" | "manual" | "recommendation"
        public long scannedAtUnix;
        public List<string> ingredients = new List<string>();
        public List<string> skinCareActiveIngredients = new List<string>();
        public List<string> baseTextureFormulators = new List<string>();
        public List<string> pigmentsPreservatives = new List<string>();
        public List<string> unrecognized = new List<string>();        // names on the label that look like ingredients but match nothing the app knows: NOT part of the verdict
        public List<string> mayContain = new List<string>();          // names that stood behind "may contain" or "+/-" on the label: they depend on the shade
        public List<string> harmfulIngredients = new List<string>();
        public List<IngredientFinding> findings = new List<IngredientFinding>();
        public List<string> keyActives = new List<string>();
        public List<string> hiddenAlerts = new List<string>();
        public CompatibilityItem skinTypeMatch = new CompatibilityItem();
        public CompatibilityItem allergenMatch = new CompatibilityItem();
        public CompatibilityItem concernMatch = new CompatibilityItem();
        public CompatibilityItem ageMatch = new CompatibilityItem();
        public string shadeHex;                  // colour picked up from the scan / product
        public string shadeName;                 // the shade's name or number printed on the product, if it was read
        public string scope;                     // "makeup" | "skincare" | "out" (hair, body ...): see Scan/ProductScope.cs
        public string scopeNote;                 // what the user was told because of that, and because of their skin conditions
        public string finish;                    // texture of the colour: matte | satin | glossy | shimmer | metallic ("" = not known)
        public string barcode;                   // the number under the product's barcode, when the camera saw it ("" = not seen)
        public string fdaNumber;                 // FDA Philippines notification number ("" = not found there, or not checked)
        public string fdaLine;                   // what the FDA Philippines portal says about it, in one line
        public bool fdaChecked;                  // the portal was asked (online); with fdaNumber empty = it is not on the FDA's list
        public string fdaStatus;                 // the FDA Philippines answer in one word: "registered" | "similar" | "expired" | "none"; "" = not checked (see PhFdaVerification.StatusOf)
        public string fdaProductName;            // the product's name on the FDA's record
        public string fdaCompany;                // the company on the FDA's record
        public string fdaValidUntil;             // until when the FDA's record is valid, as the FDA writes it
        public string listFrom;                  // where the ingredient list came from: "pack" (read by the camera) | "inventory" | "web" (looked up online) | "typed"
        public bool listWebBacked;               // "web" only: true when the look-up came with pages a live web search really opened
        public List<string> ingredientSources = new List<string>();   // pages the ingredient list came from when it was looked up, not read from the label
        public long listDateUnix;                // when the ingredient list was read or looked up (0 = not known)
        public string listCheck;                 // a SECOND place that has this product's list: "matched" (the same list) | "differs" | "" (none found, or not checked)
        public string listCheckKind;             // what that second place is: "inventory" | "obf" (Open Beauty Facts)
        public string listCheckSource;           // its page as "name | address" ("" for the GlowUp product list)
        public long listCheckDateUnix;           // when that second place was last edited (0 = not known)
        public int listDifferCount;              // "differs": how many names only one of the two lists has
        public List<string> listOnlyHere = new List<string>();        // "differs": a few names only the shown list has
        public List<string> listOnlyThere = new List<string>();       // "differs": a few names only the second place has
        public List<string> productShades = new List<string>();
        public List<string> recommendedShades = new List<string>();
        public bool dermatologistAdvised;
        public bool aiEnhanced;
        public bool pendingApproval;             // the product came from an AI photo reading (pending_products): an admin has not approved it yet
        public bool deleted;                     // in the recoverable trash
        public bool synced;

        /// <summary>The name to show: the user's own name if they renamed it, otherwise the product name.</summary>
        public string Title => !string.IsNullOrWhiteSpace(displayName) ? displayName
                             : !string.IsNullOrWhiteSpace(productName) ? productName : "Scanned product";
    }
    [Serializable] public class ScanHistoryHolder { public List<ScanRecord> items = new List<ScanRecord>(); }

    /// <summary>
    /// A small, anonymous note of one scan ('scanActivity' collection). It feeds the admin website's
    /// dashboard: scans today, flagged allergens and the recent activity list.
    /// It holds NO user id, name or email - only what was scanned and how it was judged.
    /// </summary>
    [Serializable]
    public class ScanActivity
    {
        public string productName, brandId, brandName, cosmeticType;
        public string status;                    // "safe" | "caution" | "harmful"
        public string source;                    // "scan" | "manual"
        public long scannedAtUnix;
        public int ingredientCount, harmfulCount, cautionCount;
        public bool allergenFlagged;             // one of the user's own allergens was found in the product
        public List<string> flaggedIngredients = new List<string>();   // ingredient names only

        public static ScanActivity From(ScanRecord r)
        {
            var a = new ScanActivity
            {
                productName = r.productName ?? "", brandId = r.brandId ?? "", brandName = r.brandName ?? "",
                cosmeticType = r.cosmeticType ?? "", status = r.status ?? "", source = r.source ?? "scan",
                scannedAtUnix = r.scannedAtUnix,
                ingredientCount = r.ingredients != null ? r.ingredients.Count : 0,
                allergenFlagged = r.allergenMatch != null && r.allergenMatch.level == "bad"
            };
            if (r.findings != null)
                foreach (IngredientFinding f in r.findings)
                {
                    if (f.level == "harmful") { a.harmfulCount++; if (a.flaggedIngredients.Count < 10) a.flaggedIngredients.Add(f.name); }
                    else if (f.level == "caution") a.cautionCount++;
                }
            return a;
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  What the app itself adds to the shared database (always marked, always "not reviewed").
    //  The field lists below are exactly what the security rules accept from the app.
    // ---------------------------------------------------------------------------------------------

    /// <summary>An ingredient the app's AI lookup found ('ingredients/ai_...').</summary>
    [Serializable]
    public class AiIngredientRecord
    {
        public string name, techName, description, riskLevel, cautionNote, commonUses, fdaUnii, model;
        public int minAge;
        public bool keyActive, umbrella;
        public List<string> aliases, allergenTags, avoidForSkinTypes, avoidForConditions, goodForSkinTypes, goodForConditions, sources;
        public string source = "web";
        public bool reviewed = false;
        public bool webSearched;                 // the AI's answer was backed by a live web search
        public string addedBy;                   // the signed-in user whose scan triggered the lookup
        public long addedAtUnix;

        public static AiIngredientRecord From(IngredientData d, string uid, long now, bool webSearched, string model) => new AiIngredientRecord
        {
            name = d.name ?? "", techName = d.techName ?? "", description = d.description ?? "", riskLevel = d.riskLevel ?? "",
            cautionNote = d.cautionNote ?? "", commonUses = d.commonUses ?? "", fdaUnii = d.fdaUnii ?? "", model = model ?? "",
            minAge = d.minAge, keyActive = d.keyActive, umbrella = d.umbrella,
            aliases = d.aliases ?? new List<string>(), allergenTags = d.allergenTags ?? new List<string>(),
            avoidForSkinTypes = d.avoidForSkinTypes ?? new List<string>(), avoidForConditions = d.avoidForConditions ?? new List<string>(),
            goodForSkinTypes = d.goodForSkinTypes ?? new List<string>(), goodForConditions = d.goodForConditions ?? new List<string>(),
            sources = d.sources ?? new List<string>(), webSearched = webSearched, addedBy = uid, addedAtUnix = now
        };
    }

    /// <summary>The same, for a scan in which the camera also saw the barcode: the number is kept so the next scan finds the product by it.</summary>
    [Serializable]
    public class ScannedProductWithBarcodeRecord : ScannedProductRecord
    {
        public string barcode;
        public static ScannedProductWithBarcodeRecord From(ScannedProductRecord p, string barcode) => new ScannedProductWithBarcodeRecord
        {
            name = p.name, brandId = p.brandId, brandName = p.brandName, cosmeticType = p.cosmeticType, description = p.description,
            ingredients = p.ingredients, shades = p.shades, minAge = p.minAge, status = p.status, scans = p.scans, source = p.source,
            reviewed = p.reviewed, addedBy = p.addedBy, addedAtUnix = p.addedAtUnix, sources = p.sources, barcode = barcode
        };
    }

    /// <summary>A product added automatically from an app scan ('products/scan_...'). Always Pending.</summary>
    [Serializable]
    public class ScannedProductRecord
    {
        public string name, brandId, brandName, cosmeticType, description;
        public List<string> ingredients = new List<string>();
        public List<string> shades = new List<string>();
        public List<string> sources = new List<string>();
        public int minAge;
        public string status = "Pending";
        public long scans;
        public string source = "scan";
        public bool reviewed = false;
        public string addedBy;
        public long addedAtUnix;
    }

    /// <summary>A web-found ingredient list proposed for an existing product; the website applies it after admin review.</summary>
    [Serializable]
    public class ProductIngredientSuggestionRecord
    {
        public string productId, productName, brandName;
        public List<string> ingredients = new List<string>();
        public List<string> sources = new List<string>();
        public string source = "ai";
        public string status = "Pending";
        public bool reviewed = false;
        public string addedBy;
        public long addedAtUnix;
    }

    /// <summary>A brand added automatically from an app scan ('brands/scan_...'). Always Pending.</summary>
    [Serializable]
    public class ScannedBrandRecord
    {
        public string name, category, market;
        public string status = "Pending";
        public long scans;
        public string source = "scan";
        public bool reviewed = false;
        public string addedBy;
        public long addedAtUnix;
    }

    /// <summary>A Firestore write waiting for the network.</summary>
    [Serializable]
    public class PendingWrite
    {
        public string op;            // "set" | "increment" | "delete"
        public string path;          // e.g. users/abc/scans/xyz
        public string json;          // payload as JSON (set)
        public string field;         // increment target
        public long amount;
        public int attempts;         // how many times sending it has failed so far
    }
    [Serializable] public class PendingWriteHolder { public List<PendingWrite> items = new List<PendingWrite>(); }
}
