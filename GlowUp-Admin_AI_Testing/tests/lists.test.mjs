


import { BUILT_IN, cleanItem, normalizeLists, slugId, checkItem, keepOldName, usageCount, colourDistance, nearShade } from "../_Shared/listRules.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);


const empty = normalizeLists(null);
equal(empty.allergens.map((i) => i.id), BUILT_IN.allergens.map((i) => i.id), "no document: the built-in allergen families");
equal(empty.conditions.length, 7, "no document: the seven built-in skin conditions");
equal(empty.highlights.map((i) => i.id), ["keyActive", "umbrella"], "no document: the two built-in highlights");
equal(empty.version, 0, "no document: version 0");


equal(cleanItem({ id: "bad id!", label: "X" }), null, "an id with spaces or signs is refused");
equal(cleanItem({ id: "dandruff", label: "   " }), null, "an empty label is refused");
equal(cleanItem({ id: "dandruff", label: "  Dandruff ", keywords: ["Dandruff", "dandruff", "x", "Flakes"], retired: "yes" }),
    { id: "dandruff", label: "Dandruff", keywords: ["dandruff", "flakes"], retired: false }, "label trimmed, keywords lower-cased, one-letter and repeated ones dropped, retired only when true");
const cleaned = normalizeLists({ version: 3.9, allergens: [{ id: "a1", label: "One" }, { id: "A1", label: "Again" }, "junk"], highlights: [{ id: "shiny", label: "Shiny" }] });
equal(cleaned.version, 3, "version is a whole number");
equal(cleaned.allergens.length, 1, "a repeated id (any letter case) and a non-item are dropped");
equal(cleaned.highlights.map((i) => i.id), ["keyActive", "umbrella", "shiny"], "the built-in highlights are always there, first");
const retiredBuiltIn = normalizeLists({ highlights: [{ id: "umbrella", label: "Umbrella", retired: true }] });
check(retiredBuiltIn.highlights.every((i) => i.retired === false), "a built-in highlight cannot be retired");


equal(slugId("Hair Loss", []), "hairloss", "an id is the label without spaces, lower case");
equal(slugId("Élan & Co.", []), "elanco", "accents and signs go");
equal(slugId("Acne", ["acne"]), "acne2", "a taken id gets a number");
equal(slugId("Key active", []), "keyactive2", "a label that spells a reserved id never gets that id");
equal(slugId("keyActive", ["keyactive2"]), "keyactive3", "and the next one counts on");
equal(slugId("!", []), "item", "nothing usable: a plain id");


const conditions = BUILT_IN.conditions.map((i) => Object.assign({ retired: false }, i));
check(checkItem(conditions, "", [], null).error !== "", "an empty name is refused");
check(checkItem(conditions, "acne", [], null).error !== "", "the same name in other letters is refused");
check(checkItem(conditions, "ACNE ", [], null).error !== "", "capitals and spaces do not make a new name");
check(checkItem(conditions, "Eczema (Atopic Dermatitis)", [], null).error !== "", "an existing label is refused");
equal(checkItem(conditions, "Dandruff", ["dandruff"], null), { error: "", warnings: [] }, "a new name passes cleanly");
check(checkItem(conditions, "Rosacia", [], null).warnings.length === 1, "a look-alike (one letter off) is a warning, not a refusal");
check(checkItem(conditions, "Eczema", [], null).error !== "", "a name that is an existing id is refused");
check(checkItem(conditions, "Psoriasis vulgaris", [], null).warnings.some((w) => /"Psoriasis"/.test(w)), "a name that contains an existing one is a warning");
check(checkItem(conditions, "Pimples", ["acne"], null).warnings.some((w) => /"acne"/.test(w)), "a keyword another item uses is a warning");
equal(checkItem(conditions, "Acne", [], "acne"), { error: "", warnings: [] }, "an item may keep its own name when it is the one being edited");
const withRetired = conditions.concat([{ id: "old", label: "Old thing", keywords: [], retired: true }]);
check(/retired/.test(checkItem(withRetired, "Old thing", [], null).error), "a retired item is named as retired: restore it");


const renamed = keepOldName({ id: "paraben", label: "Paraben preservatives", keywords: ["paraben"], retired: false }, "Parabens");
equal(renamed.keywords, ["paraben", "parabens"], "the old label is kept as a keyword");
const same = keepOldName({ id: "nut", label: "Nut-derived oils", keywords: ["nut"], retired: false }, "nut derived oils");
check(same.keywords.length === 1, "a spelling variant of the same name adds nothing");
const unchanged = keepOldName({ id: "nut", label: "Nut-derived oils", keywords: ["nut"], retired: false }, "Nut-derived oils");
equal(unchanged.keywords, ["nut"], "the same name adds nothing");


const ingredients = [
    { allergenTags: ["fragrance"], avoidForConditions: ["acne"], goodForConditions: [], keyActive: true, umbrella: false, highlights: [] },
    { allergenTags: ["Fragrance", "nut"], avoidForConditions: [], goodForConditions: ["acne"], keyActive: false, umbrella: true, highlights: ["shiny"] },
    { allergenTags: [], avoidForConditions: ["eczema"], goodForConditions: [], keyActive: false, umbrella: false, highlights: [] }
];
equal(usageCount("allergens", "fragrance", ingredients), 2, "allergen use counts any letter case");
equal(usageCount("conditions", "acne", ingredients), 2, "a condition counts as avoid or as good");
equal(usageCount("highlights", "keyActive", ingredients), 1, "the built-in key active is its field");
equal(usageCount("highlights", "umbrella", ingredients), 1, "the built-in umbrella is its field");
equal(usageCount("highlights", "shiny", ingredients), 1, "a custom highlight is in the highlights list");
equal(usageCount("allergens", "lanolin", ingredients), 0, "an unused item has no users");


equal(colourDistance("#000000", "#000000"), 0, "the same colour is 0 apart");
equal(colourDistance("#000000", "#FFFFFF"), 442, "black and white are as far apart as it gets");
equal(nearShade(["#C24B62", "#112233"], "#C24B64"), "#C24B62", "a colour two steps away is a look-alike");
equal(nearShade(["#C24B62"], "#C24B62"), "", "the very same colour is not a look-alike (it is a duplicate, handled apart)");
equal(nearShade(["#C24B62"], "#3355AA"), "", "a different colour is not a look-alike");

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
