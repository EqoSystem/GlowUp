import { friendlyNameFixes, pickIngredientKeeper, mergeIngredients, splitJoinedName, joinedNameFixes, cleanupPlan, ingredientDuplicates, markedDifferent } from "../_Shared/glowupCatalog.js";

let checks = 0;
const equal = (a, b, why) => { checks++; if (JSON.stringify(a) !== JSON.stringify(b)) { console.error("FAILED:", why, "\n  got     ", JSON.stringify(a), "\n  expected", JSON.stringify(b)); process.exit(1); } };
const kinds = [{ id: "solvent", name: "Solvent" }, { id: "emollient", name: "Emollient" }];
const e = (id, name, techName, aliases, extra) => Object.assign({ id, name, techName: techName || "", aliases: aliases || [], description: "d", binned: false }, extra || {});

// one label name in techName: that is the INCI name, the other part is an alias
let r = splitJoinedName(e("1", "Dimethicone / Silicone Oil", "Dimethicone"), kinds);
equal([r.name, r.aliases, r.guess], ["Dimethicone", ["Silicone Oil"], false], "techName decides");
// a kind of ingredient becomes the classification, not an alias
r = splitJoinedName(e("2", "Solvent / Isododecane", "Isododecane"), kinds);
equal([r.name, r.aliases, r.classificationId], ["Isododecane", [], "solvent"], "Solvent is a kind");
// an entry that has a classification keeps it
r = splitJoinedName(e("3", "Solvent / Isododecane", "Isododecane", [], { classificationId: "emollient" }), kinds);
equal(r.classificationId, undefined, "own classification kept");
// techName joined too: first name, flagged as a guess
r = splitJoinedName(e("4", "AQUA / WATER / EAU", "AQUA (WATER)", ["EAU"]), kinds);
equal([r.name, r.aliases.includes("WATER"), r.aliases.includes("EAU")], ["AQUA", true, true], "Aqua / Water / Eau");
r = splitJoinedName(e("5", "Sodium Chloride / Salt", "Sodium Chloride / Salt"), kinds);
equal([r.name, r.aliases, r.guess], ["Sodium Chloride", ["Salt"], true], "joined techName is a guess");
// a slash without spaces is part of the name; a broken part is left alone
equal(splitJoinedName(e("6", "PEG/PPG-18/18 Dimethicone", ""), kinds), null, "slash inside a name");
equal(splitJoinedName(e("7", "2-Dihydroxyhexane / 1", "2-Dihydroxyhexane"), kinds), null, "broken name left to a person");
// joined aliases are split as well
r = splitJoinedName(e("8", "Water / Aqua", "", ["Water / Aqua / Eau"]), kinds);
equal(r.aliases.sort(), ["Aqua", "Eau"], "joined alias split");

// the step: nothing is ticked first, and Aqua / Water then merges with a separate Water entry in the duplicates step
const list = [e("a", "Aqua / Water", "Aqua"), e("b", "Water", "")];
const plan = cleanupPlan([], [], list, { only: ["joinedNames", "ingredients"], classifications: kinds });
equal(plan.steps.map((s) => s.id), ["joinedNames", "ingredients"], "steps");
equal(plan.steps[0].careful, true, "careful");
equal(plan.steps[0].changes.length, 1, "one split");
equal(plan.steps[0].changes[0].fields.name, "Aqua", "split result");
equal(plan.steps[1].changes.some((c) => c.action === "bin"), true, "the duplicates step then merges Aqua and Water");

// "different ingredient" mark: the pair is left alone by the duplicate finder, from either side
const x = e("x", "Zinc Oxide", "", ["Coated Zinc"]), y = e("y", "Coated Zinc Oxide", "", ["Coated Zinc"]);
equal(ingredientDuplicates([x, y]).length, 1, "shared alias is a duplicate");
equal(ingredientDuplicates([x, Object.assign({}, y, { differentFrom: ["x"] })]).length, 0, "marked on the new entry");
equal(ingredientDuplicates([Object.assign({}, x, { differentFrom: ["y"] }), y]).length, 0, "marked on the old entry");
equal(markedDifferent(x, y), false, "unmarked");

// the INCI entry is kept, the everyday name becomes its alias (the "Water" / "AQUA" case from the live list)
const water = e("w", "Water", "AQUA (WATER)", ["EAU"]), aqua = e("q", "AQUA", "");
equal(pickIngredientKeeper([water, aqua]).id, "q", "Aqua is the INCI entry, not Water");
const merged = mergeIngredients(aqua, [water]);
equal(merged.keepChanges.aliases, ["Water", "EAU"], "Water and Eau are aliases, the junk 'AQUA (WATER)' is not");
equal(merged.bin.length, 1, "the Water entry goes to the bin");
const plan2 = cleanupPlan([], [], [water, aqua], { only: ["friendlyNames", "ingredients"], classifications: kinds });
equal(plan2.steps[0].changes[0].fields.name, "AQUA", "friendly step renames Water to the label name");
equal(plan2.steps[1].changes.some((c) => c.action === "bin" && c.docId === "w"), true, "then the duplicate Water entry is binned, Aqua kept");
equal(friendlyNameFixes([e("z", "Zinc", "Zinc")]).length, 0, "same name: nothing to do");
equal(friendlyNameFixes([e("z", "Zinc Oxide (Nano)", "Zinc Oxide (Nano)")]).length, 0, "only brackets differ: left alone");

console.log("checks made:", checks);
console.log("ALL PASSED");
