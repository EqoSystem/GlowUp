import assert from "node:assert/strict";
import { waitingForList, suggestionFrom, LISTS_PER_LOAD } from "../_Shared/productListFill.js";
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const { readAnswer } = require("../api/product-list.js");

let n = 0; const ok = (name, fn) => { fn(); n++; };
const P = (o) => Object.assign({ id: "p1", name: "Fit Me Foundation", brandName: "Maybelline", needsReview: true, binned: false, ingredients: [], listAiChecked: false }, o);

ok("waiting: pending product without a list", () => assert.equal(waitingForList([P({})], [], null).length, 1));
ok("waiting: has a list", () => assert.equal(waitingForList([P({ ingredients: ["Water"] })], [], null).length, 0));
ok("waiting: already looked up", () => assert.equal(waitingForList([P({ listAiChecked: true })], [], null).length, 0));
ok("waiting: already queued", () => assert.equal(waitingForList([P({})], [{ productId: "p1" }], null).length, 0));
ok("waiting: reviewed / binned / short name", () => {
    assert.equal(waitingForList([P({ needsReview: false })], [], null).length, 0);
    assert.equal(waitingForList([P({ binned: true })], [], null).length, 0);
    assert.equal(waitingForList([P({ name: "ab" })], [], null).length, 0);
});
ok("waiting: scope filter", () => assert.equal(waitingForList([P({})], [], () => false).length, 0));
ok("limit per load is small", () => assert.ok(LISTS_PER_LOAD <= 5));
ok("suggestion shape", () => {
    const s = suggestionFrom(P({}), { ingredients: ["Water", "Glycerin"], sources: ["Maybelline | https://maybelline.com/x"] }, "uid1", 100);
    assert.equal(s.productId, "p1"); assert.equal(s.source, "web-ai"); assert.equal(s.status, "Pending"); assert.equal(s.reviewed, false); assert.equal(s.addedAtUnix, 100);
});

// the server's reading of Gemini's answer
const answer = (data, sites) => ({ candidates: [{ content: { parts: [{ text: JSON.stringify(data) }] }, groundingMetadata: { groundingChunks: (sites || []).map((t) => ({ web: { title: t } })) } }] });
const good = { same: true, brandName: "Maybelline", productName: "Fit Me", cosmeticType: "Foundation", ingredients: ["Water", "Glycerin", "Dimethicone", "Talc", "Parfum"], confidence: 0.9, sources: [{ title: "Maybelline Fit Me", url: "https://www.maybelline.com/fit-me" }] };
ok("answer: good", () => { const r = readAnswer(answer(good, ["maybelline.com"])); assert.equal(r.found, true); assert.equal(r.ingredients.length, 5); assert.match(r.sources[0], /maybelline\.com/); });
ok("answer: link of a site the search did not open is dropped", () => assert.equal(readAnswer(answer(good, ["watsons.com.ph"])).found, false));
ok("answer: an ingredient database is never a source", () => {
    const db = Object.assign({}, good, { sources: [{ title: "INCIDecoder", url: "https://incidecoder.com/products/x" }] });
    assert.equal(readAnswer(answer(db, ["incidecoder.com"])).found, false);
});
ok("answer: not clear / low confidence / short list / no JSON", () => {
    assert.equal(readAnswer(answer(Object.assign({}, good, { same: false }), ["maybelline.com"])).found, false);
    assert.equal(readAnswer(answer(Object.assign({}, good, { confidence: 0.4 }), ["maybelline.com"])).found, false);
    assert.equal(readAnswer(answer(Object.assign({}, good, { ingredients: ["Water", "Glycerin"] }), ["maybelline.com"])).found, false);
    assert.equal(readAnswer({ candidates: [{ content: { parts: [{ text: "sorry" }] } }] }).found, false);
});
ok("answer: duplicates removed, curly braces cleaned", () => {
    const r = readAnswer(answer(Object.assign({}, good, { ingredients: ["Water", "water", "Glycerin", "Dimethicone", "Talc", "Par{fum}"] }), ["maybelline.com"]));
    assert.equal(r.found, true); assert.equal(r.ingredients.length, 5);
});
console.log(`product list fill: ${n} checks passed`);
