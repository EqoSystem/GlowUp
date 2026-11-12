

import { cleanupPlan, definitionFixes, withoutSafetyClaims, safetyClaimFixes } from "../_Shared/glowupCatalog.js";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };
const equal = (a, b, what) => check(JSON.stringify(a) === JSON.stringify(b), `${what} (got ${JSON.stringify(a)})`);
const item = (id, name, description, extra) => Object.assign({ id, name, techName: name, description, aliases: [], source: "", binned: false }, extra || {});


const wrong = item("a", "TRIETHANOLAMINE", "A synthetic polymer used to thicken cosmetic products, improve their texture, and keep ingredients mixed evenly.");
equal(definitionFixes([wrong]).length, 1, "a known wrong definition is found");
equal(definitionFixes([item("a", "TRIETHANOLAMINE", "A pH adjuster that balances the acidity of a formula.")]).length, 0, "a definition that was corrected is left alone: running the step twice does nothing");
equal(definitionFixes([item("b", "Titanium Dioxide", "A metallic element that can be used as a colorant.")]).length, 1, "the name is matched in any letter case");
equal(definitionFixes([item("c", "ALUMINUM", "A metallic element that can be used in cosmetics as a colorant.")]).length, 0, "an ingredient whose text is right (Aluminum) is not touched");
const plan = cleanupPlan([], [], [wrong], { only: ["definitions"] });
equal(plan.steps[0].careful, true, "the step starts with nothing ticked");
equal(plan.steps[0].changes[0].fields.description.startsWith("A pH adjuster"), true, "the new text says what the ingredient is");


equal(withoutSafetyClaims("A synthetic polymer that thickens a formula. It is safe for cosmetic use, blends evenly, and stays stable on skin."),
    { to: "A synthetic polymer that thickens a formula.", removed: ["It is safe for cosmetic use, blends evenly, and stays stable on skin."] }, "a sentence that calls the ingredient safe is taken out");
equal(withoutSafetyClaims("A safe, mineral-based pigment that gives products warm red tones and blends smoothly on skin.").to,
    "A mineral-based pigment that gives products warm red tones and blends smoothly on skin.", 'the adjective "safe," is taken out and the sentence stays');
equal(withoutSafetyClaims("A preservative that helps keep products fresh and safe to use. It stops mold from growing in the product over time."), null, "a claim about the PRODUCT (keeps it safe) stays");
equal(withoutSafetyClaims("A UV filter that turns UV light into harmless heat. It is used in many sunscreens and make-up products."), null, '"harmless heat" is physics, not a safety claim');
equal(withoutSafetyClaims("A stabilizer that locks onto metal ions in water, holding them harmless so they cannot spoil the formula. It has no skincare effect."), null, '"holding them harmless" is about metal ions');
equal(withoutSafetyClaims("It is non-toxic."), null, "when too little is left to be a definition the entry is left for a person");
equal(withoutSafetyClaims("A white pigment that makes products opaque."), null, "no claim, nothing to do");
equal(safetyClaimFixes([item("a", "X", "A pigment that gives products a clear, even colour and a smooth finish on skin. It is safe for cosmetic use and long lasting."), item("b", "Y", "A pigment that colours.", { binned: true })]).length, 1, "the Recycle bin is not searched");
equal(cleanupPlan([], [], [item("a", "X", "A pigment that gives products a clear, even colour and a smooth finish on skin. It is safe for cosmetic use and long lasting.")], { only: ["safetyClaims"] }).steps[0].careful, true, "the safety step starts with nothing ticked");


const star = item("s", "Octinoxate", "The most widely used **UVB chemical sunscreen filter** worldwide, and it does **not** protect against UVA.");
const starPlan = cleanupPlan([], [], [star, item("t", "Water", "Water.")], { only: ["markdown"] });
equal(starPlan.steps[0].changes.length, 1, "only the definition with stars is in the step");
equal(starPlan.steps[0].changes[0].fields.description, "The most widely used UVB chemical sunscreen filter worldwide, and it does not protect against UVA.", "the stars are taken out and the words stay");
equal(starPlan.steps[0].careful, false, "the star step is a plain tidy-up: ticked at first");

console.log(`\ndataFixes: ${checks} checks made`);
console.log(failed === 0 ? "ALL PASSED" : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
