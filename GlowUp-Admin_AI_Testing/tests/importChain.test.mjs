// Search and import chain (redesign plan, 27 Nov 2026): run with `node tests/importChain.test.mjs`.
import assert from "node:assert/strict";
process.removeAllListeners("warning");          // the site's modules carry no package type: Node warns while it detects ES syntax
const { splitList, findEntry, judgeEntry, reviewRows, countResults, runImport, FLAGGED, NO_RULE, NOT_IDENTIFIED } = await import("../_Shared/importChain.js");

const library = [
    { id: "i1", name: "Parfum", aliases: ["Fragrance"], allergenTags: ["fragrance"], avoidForSkinTypes: ["Sensitive"], sources: ["Fragrance rule | https://dermnetnz.org/topics/fragrance-allergy"] },
    { id: "i2", name: "Dimethicone", aliases: [], allergenTags: [], avoidForSkinTypes: [], sources: ["PubChem | https://pubchem.ncbi.nlm.nih.gov/compound/24764"] },
    { id: "i3", name: "Water", techName: "Aqua", aliases: [], allergenTags: [], avoidForSkinTypes: [], sources: [] },
    { id: "i4", name: "Old", aliases: [], binned: true }
];

let passed = 0;
const check = (name, fn) => { fn(); passed++; };

check("splitList drops the heading and splits on commas", () => assert.deepEqual(splitList("Ingredients: Aqua, Dimethicone; Parfum."), ["Aqua", "Dimethicone", "Parfum"]));
check("findEntry matches the INCI name, an alias, ignores case and spaces", () => {
    assert.equal(findEntry("aqua", library).id, "i3");
    assert.equal(findEntry("FRAGRANCE", library).id, "i1");
    assert.equal(findEntry("Old", library), null);
});
check("a flagged entry says what it is a caution for, with its source", () => {
    const j = judgeEntry(library[0]);
    assert.equal(j.result, FLAGGED);
    assert.ok(j.cautionFor.includes("Sensitive"));
    assert.match(j.source.url, /dermnetnz/);
});
check("no rule matched is never called safe", () => {
    const j = judgeEntry(library[1]);
    assert.equal(j.result, NO_RULE);
    assert.match(j.why, /does not mean safe/);
});
check("an unknown name is Not identified", () => assert.equal(judgeEntry(null).result, NOT_IDENTIFIED));
check("review rows put problems first and count the three results", () => {
    const rows = reviewRows(["Dimethicone", "Tocophr yl Acet", "Parfum", "Aqua", "aqua"], library);
    assert.deepEqual(rows.map((r) => r.result), [NOT_IDENTIFIED, FLAGGED, NO_RULE, NO_RULE]);
    assert.deepEqual(countResults(rows), { all: 4, [FLAGGED]: 1, [NO_RULE]: 2, [NOT_IDENTIFIED]: 1 });
});

const lookup = async () => ({ candidates: [{ name: "Sample Tint", brand: "Sample Brand", barcode: "4800000000000", ingredients: ["Aqua", "Parfum", "Newthing"], originLabel: "Open Beauty Facts", sources: [] }] });
const drafted = [];
const draftIngredient = async (name) => { drafted.push(name); return { name, identified: true, allergenTags: [], avoidForSkinTypes: ["Oily"], sources: [] }; };

const plan = await runImport("4800000000000", { lookupProduct: lookup, library, draftIngredient }, () => {});
check("the chain reuses known names and drafts only new ones", () => {
    assert.equal(plan.known, 2); assert.equal(plan.fresh, 1);
    assert.deepEqual(drafted, ["Newthing"]);
    assert.equal(plan.counts[FLAGGED], 2);           // Parfum (rule) + Newthing (Oily caution from its draft)
});

const fromReport = await runImport("", { lookupProduct: lookup, library, draftIngredient, fallback: { name: "Typed Tint", brand: "B", ingredientText: "Aqua, Parfum" } }, () => {});
check("a user report's text is used when the search is empty, and marked not verified", () => {
    assert.equal(fromReport.product.name, "Typed Tint");
    assert.match(fromReport.listFrom, /not verified/);
    assert.equal(fromReport.names.length, 2);
});

const stopped = new AbortController(); stopped.abort();
await assert.rejects(runImport("x", { lookupProduct: lookup, library, draftIngredient }, () => {}, stopped.signal), (e) => e.stopped === true);
passed++;
await assert.rejects(runImport("nothing", { lookupProduct: async () => ({ candidates: [] }), library, draftIngredient }, () => {}), (e) => e.notFound === true);
passed++;

console.log(`importChain: ${passed} checks passed`);
