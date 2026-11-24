// Automatic allergy / skin flags for an ingredient, worked out from its name alone, with NO outside call (free plan, instant, repeatable).
// The result uses the ids of the official lists: allergen families (allergenTags), skin types (Normal, Dry, Oily, Mix, Sensitive) and the
// skin-condition ids of config/lists (see listRules.js BUILT_IN). Every rule below names where it comes from; an admin reviews the result on
// the Ingredient Curator ("flags set automatically: check") and saving the ingredient clears that mark.
//
// Sources behind the rules: the EU Cosmetics Regulation Annex III (26 fragrance allergens that must be named on the label), ECHA / EU
// classifications for skin sensitisers (isothiazolinones, formaldehyde releasers), and the common dermatology guidance on irritants for
// eczema, rosacea and sensitive skin (sulfate cleansers, denatured alcohol, menthol) and on pore-clogging esters for acne-prone skin.
// An ingredient that no rule knows gets NO flag: silence is never a statement that it is safe.

const FRAGRANCE_CONDITIONS = ["atopic", "eczema", "allergiccontact", "contact", "rosacea"];
const IRRITANT_CONDITIONS = ["atopic", "eczema", "rosacea", "irritantcontact"];
const SENSITISER_CONDITIONS = ["atopic", "eczema", "allergiccontact", "contact"];

// the 26 EU Annex III fragrance allergens (INCI) plus the label words "fragrance" and "parfum"
const EU_FRAGRANCE_ALLERGENS = [
    "amyl cinnamal", "amylcinnamyl alcohol", "anise alcohol", "benzyl alcohol", "benzyl benzoate", "benzyl cinnamate", "benzyl salicylate",
    "butylphenyl methylpropional", "cinnamal", "cinnamyl alcohol", "citral", "citronellol", "coumarin", "eugenol", "farnesol", "geraniol",
    "hexyl cinnamal", "hydroxycitronellal", "hydroxyisohexyl 3-cyclohexene carboxaldehyde", "isoeugenol", "limonene", "linalool",
    "methyl 2-octynoate", "alpha-isomethyl ionone", "evernia prunastri", "evernia furfuracea", "fragrance", "parfum", "perfume", "aroma"
];

const word = (name, term) => new RegExp("(^|[^a-z0-9])" + term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "([^a-z0-9]|$)").test(name);

const RULES = [
    { id: "eu-fragrance", why: "EU fragrance allergen (Annex III): may cause allergic reactions.",
      test: (n) => EU_FRAGRANCE_ALLERGENS.some((t) => word(n, t)),
      allergen: "fragrance", types: ["Sensitive"], conditions: FRAGRANCE_CONDITIONS },
    { id: "paraben", why: "Paraben preservative: a known contact allergen for some people.",
      test: (n) => /paraben/.test(n), allergen: "paraben", types: ["Sensitive"], conditions: ["allergiccontact", "contact"] },
    { id: "sulfate", why: "Sulfate cleanser (SLS / SLES / ALS): strips skin oils and irritates dry, sensitive or eczema-prone skin.",
      test: (n) => /\b(sodium|ammonium|potassium|magnesium|tea|mea)\s+(lauryl|laureth|myreth|coco)[\w-]*\s*sul(?:f|ph)ate\b/.test(n) || /^(sls|sles)$/.test(n),
      allergen: "sulfate", types: ["Dry", "Sensitive"], conditions: IRRITANT_CONDITIONS },
    { id: "formaldehyde", why: "Formaldehyde or a formaldehyde-releasing preservative: a common contact allergen.",
      test: (n) => /\bformaldehyde\b|dmdm hydantoin|quaternium-15|imidazolidinyl urea|diazolidinyl urea|bronopol|2-bromo-2-nitropropane|hydroxymethylglycinate/.test(n),
      allergen: "formaldehyde", types: ["Sensitive"], conditions: SENSITISER_CONDITIONS },
    { id: "lanolin", why: "Lanolin (wool wax): a known contact allergen; can also clog pores.",
      test: (n) => /\blanolin\b|\bwool (wax|alcohol)\b|\bcholesterol and lanolin\b/.test(n),
      allergen: "lanolin", types: ["Sensitive", "Oily"], conditions: ["allergiccontact", "contact", "atopic", "eczema", "acne"] },
    { id: "essential-oil", why: "Essential or fragrant plant oil: a common allergen and irritant.",
      test: (n) => /(lavandula|melaleuca|mentha|citrus [a-z ]*(peel|fruit)? ?oil|rosmarinus|eucalyptus|cinnamomum|cananga|pelargonium|jasminum|rosa damascena|santalum|cymbopogon|thymus|origanum|salvia sclarea|ocimum|juniperus|cedrus)[a-z ]* (leaf |flower |peel |bark |wood |seed |fruit )?oil\b|essential oil|tea tree/.test(n),
      allergen: "essentialoil", types: ["Sensitive"], conditions: ["rosacea", "atopic", "eczema", "allergiccontact", "contact"] },
    { id: "nut", why: "Derived from a nut or peanut: can trigger a nut allergy.",
      test: (n) => /prunus amygdalus dulcis|sweet almond|arachis hypogaea|peanut|corylus (avellana|americana)|hazelnut|juglans regia|walnut|macadamia|pistacia vera|bertholletia|anacardium occidentale|cashew/.test(n),
      allergen: "nut", types: [], conditions: [] },
    { id: "alcohol-denat", why: "Denatured (drying) alcohol: dries and irritates sensitive, dry or rosacea-prone skin.",
      test: (n) => /\balcohol denat\b|\bsd alcohol\b|\bdenatured alcohol\b|\bisopropyl alcohol\b/.test(n),
      allergen: "", types: ["Dry", "Sensitive"], conditions: ["rosacea", "atopic", "eczema", "irritantcontact"] },
    { id: "cooling", why: "Menthol / camphor / eucalyptol: cooling irritants for sensitive and rosacea-prone skin.",
      test: (n) => /\bmenthol\b|\bcamphor\b|\beucalyptol\b|menthyl|peppermint/.test(n),
      allergen: "", types: ["Sensitive"], conditions: ["rosacea", "atopic", "eczema"] },
    { id: "isothiazolinone", why: "Isothiazolinone preservative: a strong contact sensitiser (restricted in the EU).",
      test: (n) => /isothiazolinone/.test(n),
      allergen: "", types: ["Sensitive"], conditions: SENSITISER_CONDITIONS },
    { id: "pore-clogging", why: "Rich ester or butter that can clog pores or feed skin yeast: a trigger for acne-prone and oily skin.",
      test: (n) => /isopropyl (myristate|palmitate|isostearate)|myristyl myristate|cocos nucifera (coconut )?oil|coconut oil|theobroma cacao|cocoa butter|acetylated lanolin|wheat germ oil|triticum vulgare germ oil/.test(n),
      allergen: "", types: ["Oily"], conditions: ["acne", "folliculitis", "seborrheic"] }
];

const normal = (value) => String(value === null || value === undefined ? "" : value).toLowerCase().replace(/\s+/g, " ").trim();

/**
 * What the rules say about ONE ingredient. `names` = its name, INCI name and aliases.
 * Returns { allergenTags, avoidForSkinTypes, avoidForConditions, why: [text], rules: [id] } (all arrays empty when no rule knows it).
 */
export function suggestFit(names) {
    const list = (Array.isArray(names) ? names : [names]).map(normal).filter((n) => n.length > 1);
    const out = { allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [], why: [], rules: [] };
    const add = (into, values) => values.forEach((v) => { if (v && !into.includes(v)) into.push(v); });
    RULES.forEach((rule) => {
        if (!list.some((n) => rule.test(n))) return;
        add(out.allergenTags, [rule.allergen]); add(out.avoidForSkinTypes, rule.types); add(out.avoidForConditions, rule.conditions);
        out.why.push(rule.why); out.rules.push(rule.id);
    });
    return out;
}

/** True when the ingredient has no flag of any kind yet (nobody and no rule has said anything). */
export function hasNoFit(item) {
    const some = (v) => Array.isArray(v) && v.length > 0;
    return !some(item.allergenTags) && !some(item.avoidForSkinTypes) && !some(item.avoidForConditions)
        && !some(item.goodForSkinTypes) && !some(item.goodForConditions);
}

/**
 * The write for an ingredient with no flags yet: the flags the rules found, marked "auto" so an admin reviews them.
 * Returns null when there is nothing to write (a reviewed human choice is never touched; no rule = no write).
 */
export function autoFitPatch(item, allowedConditionIds, allowedAllergenIds) {
    if (!item || item.fitAuto === true || !hasNoFit(item)) return null;
    const fit = suggestFit([item.name, item.techName].concat(Array.isArray(item.aliases) ? item.aliases : []));
    if (fit.rules.length === 0) return null;
    const conditions = fit.avoidForConditions.filter((id) => !allowedConditionIds || allowedConditionIds.includes(id));
    const allergens = fit.allergenTags.filter((id) => !allowedAllergenIds || allowedAllergenIds.includes(id));
    return { allergenTags: allergens, avoidForSkinTypes: fit.avoidForSkinTypes, avoidForConditions: conditions, fitAuto: true, fitAutoWhy: fit.why.join(" ").slice(0, 400) };
}
