// The staged-product id rule of firestore.rules (idFitsNames), repeated here in JavaScript and checked against the app's own way of
// making the id (TextKey.Slug / PendingProductRepository.IdFor). The two must agree for every plain-ASCII name; the rules cannot be run here.
//   Run: node tests/stagedId.test.mjs
import { readFileSync } from "node:fs";

let checks = 0, failed = 0;
const check = (ok, what) => { checks++; if (!ok) { failed++; console.log("FAILED: " + what); } };

// ---- the rules, as written in firestore.rules (the checks at the end make sure the file still says this)
const slugOf = (t) => t.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "");
const plainAscii = (t) => /^[ -~]*$/.test(t);
const stagedId = (brand, name) => { const b = slugOf(brand); return "snap_" + (b === "" ? "nobrand" : b) + "__" + slugOf(name); };
const idFitsNames = (id, d, uid) => {
    const tail = "__" + uid, base = id.replace(new RegExp(tail + "$"), "");
    return id === "snap_" + d.key && new RegExp("^snap_.+" + tail + "$").test(id)
        && (!(plainAscii(d.brandName) && plainAscii(d.name)) || base === stagedId(d.brandName, d.name)
            || (base.length === 110 - tail.length && /^[a-z0-9_]+$/.test(base) && new RegExp("^" + base + ".*$").test(stagedId(d.brandName, d.name))));
};

// ---- the app: TextKey.Normalize / Slug and PendingProductRepository.IdFor
const normalize = (t) => t.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, " ").trim();
const UID = "kT3x9QmZ2LbV8nYpA1cD4eFgH7jK", OTHER = "Zz9yXw8vUt7sRq6pOn5mLk4jIh3g";
const appId = (brand, name, uid = UID) => {
    const b = normalize(brand).replace(/ /g, "_"), n = normalize(name).replace(/ /g, "_");
    if (!n || !uid) return "";
    const tail = "__" + uid, head = "snap_" + (b || "nobrand") + "__" + n, room = 110 - tail.length;
    return (head.length > room ? head.slice(0, room) : head) + tail;
};
const entry = (brandName, name, uid = UID) => { const id = appId(brandName, name, uid); return { id, d: { brandName, name, key: id.slice(5) } }; };

const names = [["Maybelline New York", "Fit Me Matte + Poreless Foundation"], ["L'Oreal Paris", "Infallible 24H Fresh Wear"], ["", "House Brand Lipstick"],
    ["NYX", "  Butter   Gloss  "], ["e.l.f.", "Halo Glow Liquid Filter 1.0"], ["Brand " + "B".repeat(50), "Name " + "N".repeat(95)], ["A&B", "100% Pure -- Blush"]];
for (const [brand, name] of names) {
    const { id, d } = entry(brand, name);
    check(id.length <= 110 && id.endsWith("__" + UID), `the id is at most 110 characters and ends with the account id: ${id.slice(0, 50)}`);
    check(idFitsNames(id, d, UID), `the app's own id is accepted: ${id.slice(0, 60)}`);
}
// two users, one product: two entries, neither blocks the other
const mine = entry("Maybelline New York", "Fit Me Matte"), theirs = entry("Maybelline New York", "Fit Me Matte", OTHER);
check(mine.id !== theirs.id && idFitsNames(theirs.id, theirs.d, OTHER), "another user's entry for the same product has its own id");
// the attacker
check(!idFitsNames(theirs.id, theirs.d, UID), "an id made for another account is refused");
check(!idFitsNames(mine.id, { ...mine.d, name: "Lash Sensational" }, UID), "the id of one product with another product's name is refused");
check(!idFitsNames("snap_other__thing__" + UID, { ...mine.d }, UID), "an id that is not the key is refused");
check(!idFitsNames(mine.id, { ...mine.d, key: "something_else" }, UID), "a key that is not the id is refused");
check(!idFitsNames(mine.id, { ...mine.d, brandName: "Revlon" }, UID), "the right id with another brand is refused");
check(!idFitsNames("snap_maybelline_new_york__fit_me_matte", { brandName: "Maybelline New York", name: "Fit Me Matte", key: "maybelline_new_york__fit_me_matte" }, UID), "an id without the account id is refused");
// a long id that is a pattern, not a name, cannot pass the cut-off branch
const tail = "__" + UID, pattern = ("snap_" + ".*".repeat(55)).slice(0, 110 - tail.length) + tail;
check(!idFitsNames(pattern, { brandName: "B".repeat(60), name: "N".repeat(100), key: pattern.slice(5) }, UID), "an id made of regex characters is refused in the cut-off branch");
// accents: the app folds them, the rules cannot: only the key and the account id have to fit
const accented = entry("Lancôme", "Teint Idole Ultra Wear");
check(idFitsNames(accented.id, accented.d, UID), "an accented brand is accepted when the id is the key and ends with the account id");
check(!idFitsNames(accented.id, { ...accented.d, key: "other" }, UID) && !idFitsNames(accented.id, accented.d, OTHER), "and refused otherwise");

// ---- the rules file still says what this test repeats
const rules = readFileSync(new URL("../firestore.rules", import.meta.url), "utf8");
for (const line of ["t.lower().replace('[^a-z0-9]+', '_').replace('^_+|_+$', '')", "t.matches('[ -~]*')", "return 'snap_' + (b == '' ? 'nobrand' : b) + '__' + slugOf(name);",
    "let tail = '__' + uid;", "let base = id.replace(tail + '$', '');", "id == 'snap_' + data.key", "id.matches('snap_.+' + tail)", "base == stagedId(data.brandName, data.name)",
    "base.size() == 110 - tail.size() && base.matches('[a-z0-9_]+') && stagedId(data.brandName, data.name).matches(base + '.*')",
    "idFitsNames(pendingId, request.resource.data, request.auth.uid)",
    "existsAfter(/databases/$(database)/documents/pending_products/$(pendingId))", "getAfter(/databases/$(database)/documents/pending_products/$(pendingId)).data.createdByUid == request.auth.uid",
    "getAfter(/databases/$(database)/documents/pending_products/$(pendingId)).data.hasImage == true"])
    check(rules.includes(line), "firestore.rules contains: " + line.slice(0, 70));

console.log("checks made: " + checks);
console.log(failed === 0 ? "ALL PASSED" : failed + " FAILED");
process.exit(failed === 0 ? 0 : 1);
