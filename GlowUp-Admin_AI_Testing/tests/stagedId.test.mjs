// The staged-product id rule of firestore.rules (idFitsNames), repeated here in JavaScript and checked against the app's own way of
// making the id (TextKey.Slug / PendingProductRepository.IdFor). The two must agree for every plain-ASCII name; the rules cannot be run here.
//   Run: node tests/stagedId.test.mjs
import { readFileSync } from "node:fs";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

// ---- the rules, as written in firestore.rules (the checks below make sure the file still says this)
const slugOf = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "");
const plainAscii = (t) => /^[ -~]*$/.test(t);
const stagedId = (brand, name) => { const b = slugOf(brand); return "snap_" + (b === "" ? "nobrand" : b) + "__" + slugOf(name); };
const idFitsNames = (id, d) => id === "snap_" + d.key && (!(plainAscii(d.brandName) && plainAscii(d.name)) || id === stagedId(d.brandName, d.name)
    || (id.length === 110 && new RegExp("^" + id + ".*$").test(stagedId(d.brandName, d.name))));

// ---- the app: TextKey.Normalize / Slug and PendingProductRepository.IdFor
const normalize = (t) => t.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
const appId = (brand, name) => { const b = normalize(brand).replace(/ /g, "_"), n = normalize(name).replace(/ /g, "_"); if (!n) return ""; const id = "snap_" + (b || "nobrand") + "__" + n; return id.length > 110 ? id.slice(0, 110) : id; };
const entry = (brandName, name) => { const id = appId(brandName, name); return { id, d: { brandName, name, key: id.slice(5) } }; };

const names = [["Maybelline New York", "Fit Me Matte + Poreless Foundation"], ["L'Oreal Paris", "Infallible 24H Fresh Wear"], ["", "House Brand Lipstick"],
    ["NYX", "  Butter   Gloss  "], ["e.l.f.", "Halo Glow Liquid Filter 1.0"], ["Brand " + "B".repeat(50), "Name " + "N".repeat(95)], ["A&B", "100% Pure -- Blush"]];
for (const [brand, name] of names) {
    const { id, d } = entry(brand, name);
    check(idFitsNames(id, d), `the app's own id is accepted: ${id.slice(0, 60)}`);
}
// the attacker: a real product's id with other words, or an id made for another product
let { id, d } = entry("Maybelline New York", "Fit Me Matte");
check(!idFitsNames(id, { ...d, name: "Lash Sensational" }), "the id of one product with another product's name is refused");
check(!idFitsNames("snap_other__thing", { ...d }), "an id that is not the key is refused");
check(!idFitsNames(id, { ...d, key: "something_else" }), "a key that is not the id is refused");
check(!idFitsNames(id, { ...d, brandName: "Revlon" }), "the right id with another brand is refused");
check(!idFitsNames("snap_maybelline_new_york__fit_me_matte", { brandName: "Maybelline New York", name: "Fit Me Matte!!", key: "maybelline_new_york__fit_me_matte" }) === false, "punctuation in the name does not change the slug");
// accents: the app folds them, the rules cannot: only the key has to fit
({ id, d } = entry("Lancôme", "Teint Idole Ultra Wear"));
check(idFitsNames(id, d), "an accented brand is accepted when the id is the key");
check(!idFitsNames(id, { ...d, key: "other" }), "and refused when it is not");

// ---- the rules file still says what this test repeats
const rules = readFileSync(new URL("../firestore.rules", import.meta.url), "utf8");
for (const line of ["t.lower().replace('[^a-z0-9]+', '_').replace('^_+|_+$', '')", "t.matches('[ -~]*')", "return 'snap_' + (b == '' ? 'nobrand' : b) + '__' + slugOf(name);",
    "id == 'snap_' + data.key", "stagedId(data.brandName, data.name).matches(id + '.*')", "idFitsNames(pendingId, request.resource.data)",
    "existsAfter(/databases/$(database)/documents/pending_products/$(pendingId))", "getAfter(/databases/$(database)/documents/pending_products/$(pendingId)).data.createdByUid == request.auth.uid",
    "getAfter(/databases/$(database)/documents/pending_products/$(pendingId)).data.hasImage == true"])
    check(rules.includes(line), "firestore.rules contains: " + line.slice(0, 70));

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
