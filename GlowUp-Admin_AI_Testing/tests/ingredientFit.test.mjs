// Automatic allergy / skin flags: what the rules say, and that nothing is invented for an unknown ingredient.
import { suggestFit, autoFitPatch, hasNoFit } from "../_Shared/ingredientFit.js";
import { BUILT_IN } from "../_Shared/listRules.js";
let checks = 0, failed = 0;
const ok = (cond, text) => { checks++; if (!cond) { failed++; console.log("FAIL: " + text); } };
const conditionIds = BUILT_IN.conditions.map((c) => c.id), allergenIds = BUILT_IN.allergens.map((a) => a.id);

const linalool = suggestFit(["Linalool"]);
ok(linalool.allergenTags.includes("fragrance"), "linalool is a fragrance allergen");
ok(linalool.avoidForSkinTypes.includes("Sensitive") && linalool.avoidForConditions.includes("rosacea"), "linalool: sensitive skin and rosacea");
ok(suggestFit(["Parfum"]).allergenTags.includes("fragrance"), "parfum is fragrance");
ok(suggestFit(["Sodium Lauryl Sulfate"]).allergenTags.includes("sulfate"), "SLS is a sulfate");
ok(suggestFit(["Sodium Laureth Sulfate"]).avoidForSkinTypes.includes("Dry"), "SLES avoided on dry skin");
ok(suggestFit(["Methylparaben"]).allergenTags.includes("paraben"), "paraben");
ok(suggestFit(["DMDM Hydantoin"]).allergenTags.includes("formaldehyde"), "formaldehyde releaser");
ok(suggestFit(["Lanolin"]).allergenTags.includes("lanolin"), "lanolin");
ok(suggestFit(["Melaleuca Alternifolia Leaf Oil"]).allergenTags.includes("essentialoil"), "tea tree oil is an essential oil");
ok(suggestFit(["Prunus Amygdalus Dulcis Oil"]).allergenTags.includes("nut"), "sweet almond oil is a nut oil");
ok(suggestFit(["Alcohol Denat."]).avoidForConditions.includes("irritantcontact"), "denatured alcohol");
ok(suggestFit(["Isopropyl Myristate"]).avoidForConditions.includes("acne"), "isopropyl myristate and acne");
ok(suggestFit(["Methylisothiazolinone"]).avoidForConditions.includes("allergiccontact"), "isothiazolinone");
// no invention: harmless or unknown ingredients get nothing
["Water", "Glycerin", "Titanium Dioxide", "Mica", "Tocopherol", "Some Unknown Thing"].forEach((n) => {
    const f = suggestFit([n]);
    ok(f.rules.length === 0 && f.allergenTags.length === 0 && f.avoidForConditions.length === 0, n + " gets no flag");
});
// "alcohol" inside another word, and benzyl alcohol vs alcohol denat
ok(suggestFit(["Cetearyl Alcohol"]).rules.length === 0, "cetearyl alcohol (a fatty alcohol) is not flagged");
ok(suggestFit(["Benzyl Alcohol"]).rules.includes("eu-fragrance"), "benzyl alcohol is on the EU list");

// the patch: only for an ingredient with no flags; a human's flags are never touched; ids stay inside the official lists
const fresh = { name: "Linalool", aliases: [], allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [], goodForSkinTypes: [], goodForConditions: [] };
const patch = autoFitPatch(fresh, conditionIds, allergenIds);
ok(patch && patch.fitAuto === true && patch.allergenTags.includes("fragrance") && patch.fitAutoWhy.length > 0, "patch for a fresh ingredient");
ok(patch.avoidForConditions.every((id) => conditionIds.includes(id)), "every condition id is on the official list");
ok(autoFitPatch(Object.assign({}, fresh, { avoidForConditions: ["acne"] }), conditionIds, allergenIds) === null, "an ingredient someone flagged is left alone");
ok(autoFitPatch(Object.assign({}, fresh, { goodForSkinTypes: ["Dry"] }), conditionIds, allergenIds) === null, "good-for choices count as a human choice");
ok(autoFitPatch(Object.assign({}, fresh, { fitAuto: true }), conditionIds, allergenIds) === null, "already automatic: not written twice");
ok(autoFitPatch({ name: "Water", allergenTags: [], avoidForSkinTypes: [], avoidForConditions: [] }, conditionIds, allergenIds) === null, "no rule, no write");
ok(hasNoFit(fresh) && !hasNoFit({ allergenTags: ["nut"] }), "hasNoFit");
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED of ${checks}`);
process.exit(failed === 0 ? 0 : 1);
