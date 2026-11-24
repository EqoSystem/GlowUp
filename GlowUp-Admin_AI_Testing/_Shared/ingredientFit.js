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

// ---- the reference table (_Shared/data/ingredientRefs.json, built by tools/buildIngredientRefs.mjs) ----
// Columns: function, fungalAcne (1 = Malassezia substrate, 0 = reported safe, null = not assessed), comedogenic (0-5 or null), fragrance, euAllergen,
// alcohol ("d" drying | "f" fatty | ""), pregnancyCaution, pubchemCid. null means NOT ASSESSED, never "safe": it produces no flag.
// Data: CureSkin "Indian Skincare Ingredient Flags" (CC BY 4.0) and BEAUTEE's PubChem ids (MIT). The credit goes into every ingredient it flags.
export const REF_CREDIT = "CureSkin Indian Skincare Decoder (CC BY 4.0) | https://cureskin.com/indianskincaredecoder/dataset";
const refKey = (name) => String(name || "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
function refRow(names, refs) {
    if (!refs || !refs.items) return null;
    for (const n of names) { const row = refs.items[refKey(n)]; if (row) return row; }
    return null;
}

const normal = (value) => String(value === null || value === undefined ? "" : value).toLowerCase().replace(/\s+/g, " ").trim();

/**
 * What the rules say about ONE ingredient. `names` = its name, INCI name and aliases.
 * Returns { allergenTags, avoidForSkinTypes, avoidForConditions, why: [text], rules: [id] } (all arrays empty when no rule knows it).
 */
export function suggestFit(names, refs) {
    const list = (Array.isArray(names) ? names : [names]).map(normal).filter((n) => n.length > 1);
    const out = { allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [], why: [], rules: [] };
    const add = (into, values) => values.forEach((v) => { if (v && !into.includes(v)) into.push(v); });
    RULES.forEach((rule) => {
        if (!list.some((n) => rule.test(n))) return;
        add(out.allergenTags, [rule.allergen]); add(out.avoidForSkinTypes, rule.types); add(out.avoidForConditions, rule.conditions);
        out.why.push(rule.why); out.rules.push(rule.id);
    });
    const row = refRow(list, refs);
    if (row) {
        const [, fungal, comedogenic, fragrance, eu, alcohol] = row;
        const note = (text, id) => { out.why.push(text + " (CureSkin, CC BY 4.0)"); out.rules.push(id); };
        if (fragrance === 1 || eu === 1) { add(out.allergenTags, ["fragrance"]); add(out.avoidForSkinTypes, ["Sensitive"]); add(out.avoidForConditions, FRAGRANCE_CONDITIONS); note("Listed as a fragrance or EU-declared allergen.", "ref-fragrance"); }
        if (alcohol === "d") { add(out.avoidForSkinTypes, ["Dry", "Sensitive"]); add(out.avoidForConditions, ["rosacea", "atopic", "eczema", "irritantcontact"]); note("A drying alcohol that can dehydrate the skin barrier.", "ref-drying-alcohol"); }
        if (typeof comedogenic === "number" && comedogenic >= 3) { add(out.avoidForSkinTypes, ["Oily"]); add(out.avoidForConditions, ["acne"]); note(`Published comedogenic rating ${comedogenic} of 5.`, "ref-comedogenic"); }
        // fungal acne is folliculitis caused by Malassezia yeast; the general condition "folliculitis" carries it (it is not a user-chosen chronic condition)
        if (fungal === 1) { add(out.avoidForConditions, ["folliculitis"]); note("Reported as food for Malassezia yeast (fungal acne).", "ref-fungal-acne"); }
    }
    return out;
}

/** The ingredient's usual job in a formula (CureSkin's wording), or "" when the table does not know it. */
export function refFunction(names, refs) {
    const row = refRow((Array.isArray(names) ? names : [names]).map(normal), refs);
    return row ? String(row[0] || "") : "";
}

/** The PubChem compound page of the ingredient ("Name | url" as a Sources line), or "". */
export function refPubchemSource(names, refs) {
    const row = refRow((Array.isArray(names) ? names : [names]).map(normal), refs);
    return row && row[7] ? `PubChem | https://pubchem.ncbi.nlm.nih.gov/compound/${row[7]}` : "";
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
export function autoFitPatch(item, allowedConditionIds, allowedAllergenIds, refs) {
    if (!item || item.fitAuto === true || !hasNoFit(item)) return null;
    const fit = suggestFit([item.name, item.techName].concat(Array.isArray(item.aliases) ? item.aliases : []), refs);
    if (fit.rules.length === 0) return null;
    const conditions = fit.avoidForConditions.filter((id) => !allowedConditionIds || allowedConditionIds.includes(id));
    const allergens = fit.allergenTags.filter((id) => !allowedAllergenIds || allowedAllergenIds.includes(id));
    const patch = { allergenTags: allergens, avoidForSkinTypes: fit.avoidForSkinTypes, avoidForConditions: conditions, fitAuto: true, fitAutoWhy: fit.why.join(" ").slice(0, 400) };
    if (fit.rules.some((id) => id.startsWith("ref-"))) patch.sources = Array.from(new Set((Array.isArray(item.sources) ? item.sources : []).concat(REF_CREDIT))).slice(0, 10);
    return patch;
}

/**
 * Details the table can fill without anyone typing: an EMPTY purpose (the ingredient's usual job) and a PubChem page in Sources.
 * Never replaces something an admin wrote. Returns null when there is nothing to add.
 */
export function autoDetailsPatch(item, refs, maxPurpose) {
    if (!item || !refs) return null;
    const names = [item.name, item.techName].concat(Array.isArray(item.aliases) ? item.aliases : []);
    const patch = {};
    const job = refFunction(names, refs);
    if (job && !String(item.purpose || "").trim()) patch.purpose = job.split("/")[0].trim().slice(0, maxPurpose || 120);
    const page = refPubchemSource(names, refs);
    const have = Array.isArray(item.sources) ? item.sources : [];
    if (page && !have.some((line) => /pubchem\.ncbi\.nlm\.nih\.gov\/compound\//i.test(String(line)))) patch.sources = have.concat(page).slice(0, 10);
    return Object.keys(patch).length ? patch : null;
}
