// Quick fill decisions: what gets ticked from the rules and PubChem hazard codes, and the note shown for every group. No network.
import { suggestFit, RULE_SOURCES } from "../_Shared/ingredientFit.js";
import { hazardCodes, planFit } from "../_Shared/fitReasons.js";
let checks = 0, failed = 0;
const ok = (c, t) => { checks++; if (!c) { failed++; console.log("FAIL: " + t); } };

const view = (...lines) => ({ Record: { Section: [{ Section: [{ Information: lines.map((String) => ({ Value: { StringWithMarkup: [{ String }] } })) }] }] } });
ok(hazardCodes(view("H317 (61.7%): May cause an allergic skin reaction", "H315 (97.2%): Causes skin irritation")).join() === "H315,H317", "both skin codes are read");
ok(hazardCodes(view("H317 (4.1%): May cause an allergic skin reaction", "H317: May cause an allergic skin reaction")).length === 0, "a code reported by few notifiers is not used");
ok(hazardCodes(view("H315: Causes skin irritation")).join() === "H315", "a code with no share is used");
ok(hazardCodes(view("H302 (90%): Harmful if swallowed")).length === 0 && hazardCodes(null).length === 0, "other codes and nothing give nothing");

const blank = suggestFit(["Some Unknown Thing"]);
const hit = planFit({ rules: blank, hazards: { ok: true, codes: ["H317"], url: "https://pubchem.ncbi.nlm.nih.gov/compound/6549" } });
ok(hit.avoidForSkinTypes.join() === "Sensitive" && hit.avoidForConditions.join() === "allergiccontact,contact", "H317 = sensitive skin and allergic contact dermatitis");
ok(planFit({ rules: blank, hazards: { ok: true, codes: ["H315"], url: "u" } }).avoidForConditions.join() === "irritantcontact", "H315 = irritant contact dermatitis");
ok(hit.notes.skinTypes[0].url.includes("6549") && /H317/.test(hit.notes.skinTypes[0].text), "a filled box names the code and links its page");
ok(hit.sources.some((s) => /pubchem/.test(s)), "the page joins the reference links");
ok(/Not filled/.test(hit.notes.allergens[0].text) && /Not filled/.test(hit.notes.risk[0].text), "an empty group says why");

const none = planFit({ rules: blank, hazards: { ok: true, codes: [], url: "" } });
ok(none.avoidForSkinTypes.length === 0 && none.notes.conditions[0].text === "Not filled: no hazard or rule found for this name. This does not mean it is safe.", "nothing found is never called neutral or safe");
ok(/did not answer/.test(planFit({ rules: blank, hazards: { ok: false, codes: [], url: "" } }).notes.skinTypes[0].text), "a failed look-up is told apart from nothing found");

const parfum = planFit({ rules: suggestFit(["Parfum"]), hazards: { ok: false, codes: [], url: "" } });
ok(parfum.umbrella === true && parfum.allergenTags.join() === "fragrance" && parfum.avoidForSkinTypes.includes("Sensitive"), "rules still fill when PubChem fails; parfum is an umbrella term");
ok(parfum.notes.allergens[0].url.startsWith("https://") && parfum.sources.length > 0, "a rule carries its source page");
ok(planFit({ rules: suggestFit(["Linalool"]), hazards: null }).umbrella === false, "one named fragrance chemical is not an umbrella term");
ok(Object.values(RULE_SOURCES).every((s) => !s || /^.+ \| https:\/\/\S+$/.test(s)), "every rule source is a reference line");
console.log(failed === 0 ? `ALL PASSED  (checks made: ${checks})` : `${failed} FAILED of ${checks}`);
process.exit(failed === 0 ? 0 : 1);
